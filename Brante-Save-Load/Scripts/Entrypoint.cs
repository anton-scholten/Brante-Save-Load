using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Doorstop
{
	// Doorstop runs this BEFORE the Unity engine initialises, and the engine's
	// icalls (native function bindings) are not registered yet. Two hard-won
	// facts, both verified in-game 2026-10-23:
	//
	// 1. `new GameObject()` at that point throws MissingMethodException on
	//    GameObject.Internal_CreateGameObject - and what is worse, Mono then
	//    caches the FAILED icall wrapper for that method slot: every later
	//    call from our assembly keeps throwing, even after the engine is
	//    fully up (observed: still failing inside a GameManager.Awake postfix
	//    while the game itself was instantiating prefabs).
	//    => NEVER touch the Unity API until the engine is up. Not once.
	//
	// 2. Creating the host from an AppDomain.AssemblyLoad callback is
	//    unreliable for the same reason (attempts during engine module
	//    loading all failed).
	//
	// So the host is created the way real mod loaders do it: pure-managed
	// Harmony patching (safe at any time, no engine needed) on game methods
	// that are guaranteed to run on the main thread once the engine is fully
	// up. Whichever of them fires first creates the host GameObject.
	//   - GameManager.Awake       - persistent singleton, early boot
	//   - SaveLoadManager.Awake   - persistent singleton, early boot
	//   - MainMenuController.Awake - present in the main menu scene
	public static class Entrypoint
	{
		private static bool _hostCreated;
		private static bool _patched;
		private static int _loadCount;

		public static void Start()
		{
			// Pure managed code only from here on - no UnityEngine calls!
			ModLog.Info("Doorstop entrypoint reached (engine not up yet)");
			AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
		}

		private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
		{
			string name;
			try { name = args.LoadedAssembly.GetName().Name; }
			catch { name = "?"; }
			_loadCount++;
			if (name == "Assembly-CSharp")
			{
				ModLog.Info("Assembly-CSharp loaded (assembly #" + _loadCount + ")");
				if (!_patched)
				{
					_patched = true;
					try { PatchGameBootMethods(); }
					catch (Exception e) { ModLog.Error("Harmony patch failed:\n" + e); }
				}
			}
		}

		private static void PatchGameBootMethods()
		{
			var harmony = new Harmony("brante.save-load");
			// type name -> method to postfix -> postfix handler; each fires on
			// the main thread early in the respective scene, engine fully up.
			string[][] targets =
			{
				new[] { "_Scripts.Managers.GameManager, Assembly-CSharp", "Awake", "BootHookPostfix" },
				new[] { "_Scripts.Managers.SaveLoadManager, Assembly-CSharp", "Awake", "BootHookPostfix" },
				new[] { "_Scripts.AMVCC.Controllers.MainMenuController, Assembly-CSharp", "Awake", "BootHookPostfix" },
				// every choice scene has a SceneController; its Awake is the
				// choice-point capture trigger (Phase 3)
				new[] { "_Scripts.AMVCC.Controllers.SceneController, Assembly-CSharp", "Awake", "ChoiceScenePostfix" },
				// the pause/settings menu toggling = our moment to (re)inject
				// the "Save / Load" button (Phase 4/6); fires only when the
				// engine is up, so the Unity API is safe here
				new[] { "_Scripts.Managers.UIManager, Assembly-CSharp", "ShowPauseMenu", "PauseMenuPostfix" },
			};
			foreach (string[] t in targets)
			{
				Type type = Type.GetType(t[0]);
				if (type == null)
				{
					ModLog.Error("Type not found: " + t[0]);
					continue;
				}
				MethodInfo method = type.GetMethod(t[1],
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (method == null)
				{
					ModLog.Error("Method not found: " + t[0] + "." + t[1]);
					continue;
				}
				harmony.Patch(method,
					postfix: new HarmonyMethod(typeof(Entrypoint), t[2]));
				ModLog.Info("Patched " + type.Name + "." + t[1] + " (postfix -> " + t[2] + ")");
			}
			PatchSaveRepair(harmony);
		}

		// The game's save calls are left alone, but if its slot bookkeeping is
		// empty at that moment (it can be, after a mod load) the files would
		// land in Saves/ itself. The prefix only fills in a MISSING id.
		private static void PatchSaveRepair(Harmony harmony)
		{
			Type slm = Type.GetType("_Scripts.Managers.SaveLoadManager, Assembly-CSharp");
			if (slm == null)
			{
				return;
			}
			foreach (MethodInfo m in slm.GetMethods(
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if ((m.Name == "SaveGame" && m.GetParameters().Length == 4)
					|| (m.Name == "SaveChapterState" && m.GetParameters().Length == 0))
				{
					harmony.Patch(m,
						prefix: new HarmonyMethod(typeof(Entrypoint), "SaveRepairPrefix"));
					ModLog.Info("Patched " + slm.Name + "." + m.Name + " (slot repair prefix)");
				}
			}
			// The single funnel every save path uses to find its folder.
			MethodInfo folder = slm.GetMethod("GetSaveFolderPath",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (folder != null)
			{
				harmony.Patch(folder,
					postfix: new HarmonyMethod(typeof(Entrypoint), "SaveFolderPostfix"));
				ModLog.Info("Patched " + slm.Name + ".GetSaveFolderPath (folder repair postfix)");
			}
		}

		private static void SaveFolderPostfix(ref string __result)
		{
			Recorder.FixSaveFolder(ref __result);
		}

		private static void SaveRepairPrefix(object __instance)
		{
			Recorder.EnsureSlotId(__instance);
		}

		// Runs on the main thread, engine fully up, at the end of Awake() of
		// the first scene that contains one of the patched components.
		// FIRST touch of the Unity API by this mod - must not run earlier
		// (fact 1 in the class comment).
		private static void BootHookPostfix()
		{
			ModLog.Info("Game boot hook reached - engine is up");
			TryCreateHost();
		}

		// Harmony postfix on SceneController.Awake: a choice scene just
		// appeared on screen. Hand it to the recorder (Phase 3).
		private static void ChoiceScenePostfix()
		{
			Recorder.OnChoiceSceneShown();
		}

		// Harmony postfix on UIManager.ShowPauseMenu: the settings menu
		// toggled - make sure the "Save / Load" button is in it.
		private static void PauseMenuPostfix()
		{
			ChoiceWindow.EnsurePauseButton();
		}

		private static void TryCreateHost()
		{
			if (_hostCreated)
			{
				return;
			}
			// Preferred: our own persistent host object.
			GameObject go = null;
			try
			{
				go = new GameObject("Brante-Save-Load");
				UnityEngine.Object.DontDestroyOnLoad(go);
				go.AddComponent<ModHost>();
				_hostCreated = true;
				ModLog.Info("Host created: own DontDestroyOnLoad object");
			}
			catch (Exception e)
			{
				try { if (go != null) UnityEngine.Object.Destroy(go); } catch { }
				ModLog.Info("new GameObject failed, falling back to attaching "
					+ "to a game object:\n" + e);
			}
			if (_hostCreated)
			{
				return;
			}
			// Fallback: different icall - attach ModHost to an existing game
			// object instead of creating one (persistent if GameManager is
			// there, menu-lifetime otherwise).
			string[] candidates =
			{
				"_Scripts.Managers.GameManager, Assembly-CSharp",
				"_Scripts.Managers.SaveLoadManager, Assembly-CSharp",
				"_Scripts.AMVCC.Controllers.MainMenuController, Assembly-CSharp",
			};
			foreach (string tname in candidates)
			{
				try
				{
					Type t = Type.GetType(tname);
					if (t == null) { continue; }
					Component c = UnityEngine.Object.FindObjectOfType(t) as Component;
					if (c == null) { continue; }
					c.gameObject.AddComponent<ModHost>();
					_hostCreated = true;
					ModLog.Info("Host attached to '" + c.gameObject.name + "' (fallback)");
					return;
				}
				catch (Exception e)
				{
					ModLog.Error("Fallback attach via " + tname + " failed:\n" + e);
				}
			}
			if (!_hostCreated)
			{
				ModLog.Error("Host creation FAILED entirely");
			}
		}
	}
}
