using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using _Scripts.Helpers;

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
				// a choice button was clicked: record which one (history tree)
				new[] { "_Scripts.AMVCC.Views.ParameterButtonChanger, Assembly-CSharp", "Button_Click", "ChoiceClickPostfix" },
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
			PatchInterlude(harmony);
			PatchTrueDeath(harmony);
		}

		// The game's own bug: on the true-death pages FourthDeath_3..6 the
		// CONTINUE button calls FourthDeathContinueButtonController.Button_Click,
		// whose DeathObjective is empty in the shipped scenes, so the objective
		// pop-up throws in UIManager.SetAchievementInfo, the button stays
		// disabled and the story stops there. FourthDeath_1, _2, _7 and _8 call
		// SceneButton_Click instead: mark the page shown, go on to the next one
		// in the GameFinals flow. Filling the objective in does not help - the
		// pop-up's close sets GoToFinals and reruns GameFinals, which reopens the
		// page it never marked shown, forever. So a CONTINUE with no objective
		// does what the other pages' CONTINUE does.
		private static void PatchTrueDeath(Harmony harmony)
		{
			try
			{
				MethodInfo m = typeof(FourthDeathContinueButtonController).GetMethod("Button_Click");
				if (m == null)
				{
					ModLog.Error("FourthDeathContinueButtonController.Button_Click not found");
					return;
				}
				harmony.Patch(m, prefix: new HarmonyMethod(typeof(Entrypoint), "TrueDeathPrefix"));
				ModLog.Info("Patched FourthDeathContinueButtonController.Button_Click (true-death pages without an objective)");
			}
			catch (Exception e)
			{
				ModLog.Error("True-death patch failed:\n" + e);
			}
		}

		private static bool TrueDeathPrefix(FourthDeathContinueButtonController __instance, UnityEngine.UI.Button button)
		{
			try
			{
				if (__instance.DeathObjective != null || __instance.DeathWindow == null)
				{
					return true;
				}
				ModLog.Info("True death: CONTINUE in " + _Scripts.Managers.GameManager.Instance.OpenedSceneName
					+ " has no objective - going on to the next page, as the other true-death pages do");
				__instance.SceneButton_Click(button);
				return false;
			}
			catch (Exception e)
			{
				ModLog.Error("True death prefix failed:\n" + e);
				return true;
			}
		}

		// The interlude pop-up (a scene's state-machine event) applies its
		// stat, relation and status changes when its close button is clicked
		// (CalculateParams). Logged with the scene and the stats it changed,
		// because a load that re-opens a scene can show it a second time.
		private static void PatchInterlude(Harmony harmony)
		{
			try
			{
				Type t = Type.GetType("_Scripts.AMVCC.Views.Windows.Popup.InterludePopupController, Assembly-CSharp");
				MethodInfo m = t == null ? null : t.GetMethod("CalculateParams", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (m == null)
				{
					ModLog.Error("InterludePopupController.CalculateParams not found");
					return;
				}
				harmony.Patch(m,
					prefix: new HarmonyMethod(typeof(Entrypoint), "InterludePrefix"),
					postfix: new HarmonyMethod(typeof(Entrypoint), "InterludePostfix"));
				ModLog.Info("Patched InterludePopupController.CalculateParams (logged)");
			}
			catch (Exception e)
			{
				ModLog.Error("Interlude patch failed:\n" + e);
			}
		}

		private static string _interludeBefore;

		private static void InterludePrefix()
		{
			_interludeBefore = Recorder.StatLine();
		}

		private static void InterludePostfix(object __instance)
		{
			try
			{
				FieldInfo f = __instance.GetType().GetField("_interlude", BindingFlags.Instance | BindingFlags.NonPublic);
				UnityEngine.Object il = f == null ? null : f.GetValue(__instance) as UnityEngine.Object;
				ModLog.Info("Interlude " + (il != null ? il.name : "?") + " applied in "
					+ _Scripts.Managers.GameManager.Instance.OpenedSceneName
					+ (AutoPlay.Running ? " (replay)" : "") + ": "
					+ Recorder.StatDiff(_interludeBefore, Recorder.StatLine()));
			}
			catch (Exception e)
			{
				ModLog.Error("Interlude log failed: " + e.Message);
			}
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
			const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (MethodInfo m in slm.GetMethods(all))
			{
				if ((m.Name == "SaveGame" && m.GetParameters().Length == 4)
					|| (m.Name == "SaveChapterState" && m.GetParameters().Length == 0))
				{
					harmony.Patch(m,
						prefix: new HarmonyMethod(typeof(Entrypoint), "SaveRepairPrefix"),
						postfix: new HarmonyMethod(typeof(Entrypoint), "SaveMirrorPostfix"));
					ModLog.Info("Patched " + slm.Name + "." + m.Name
						+ " (slot repair prefix, autosave mirror postfix)");
				}
			}
			// The single funnel every save path uses to find its folder.
			MethodInfo folder = slm.GetMethod("GetSaveFolderPath", all);
			if (folder != null)
			{
				harmony.Patch(folder,
					postfix: new HarmonyMethod(typeof(Entrypoint), "SaveFolderPostfix"));
				ModLog.Info("Patched " + slm.Name + ".GetSaveFolderPath (folder repair postfix)");
			}
			// Saves written by older versions can have no SlotInfo.Id; the
			// folder is the id. Fix it wherever the game reads a save, so its
			// own Load/Continue and its next autosave use the right folder.
			MethodInfo loadSaves = slm.GetMethod("LoadSaves", all);
			if (loadSaves != null)
			{
				harmony.Patch(loadSaves,
					postfix: new HarmonyMethod(typeof(Entrypoint), "SaveListPostfix"));
				ModLog.Info("Patched " + slm.Name + ".LoadSaves (id recovery postfix)");
			}
			MethodInfo loadData = slm.GetMethod("LoadData", all, null, new Type[] { typeof(string) }, null);
			if (loadData != null)
			{
				harmony.Patch(loadData,
					postfix: new HarmonyMethod(typeof(Entrypoint), "LoadDataPostfix"));
				ModLog.Info("Patched " + slm.Name + ".LoadData (id recovery postfix)");
			}
			// Know exactly when the game changes saves, instead of guessing.
			foreach (MethodInfo m in slm.GetMethods(all))
			{
				if (m.Name == "LoadGame" && m.GetParameters().Length == 1
					&& m.GetParameters()[0].ParameterType == typeof(int))
				{
					harmony.Patch(m,
						prefix: new HarmonyMethod(typeof(Entrypoint), "GameLoadPrefix"),
						postfix: new HarmonyMethod(typeof(Entrypoint), "GameLoadPostfix"));
					ModLog.Info("Patched " + slm.Name + ".LoadGame(int) (pre-rewind checkpoint, run state)");
				}
				else if (m.Name == "LoadGame" && m.GetParameters().Length == 2
					&& m.GetParameters()[0].ParameterType == typeof(string))
				{
					harmony.Patch(m,
						prefix: new HarmonyMethod(typeof(Entrypoint), "ChapterLoadPrefix"),
						postfix: new HarmonyMethod(typeof(Entrypoint), "ChapterLoadPostfix"));
					ModLog.Info("Patched " + slm.Name + ".LoadGame(string,int) (chapter checkpoint, run state)");
				}
				else if (m.Name == "CreateSaveLoadSlot")
				{
					harmony.Patch(m,
						postfix: new HarmonyMethod(typeof(Entrypoint), "NewGamePostfix"));
					ModLog.Info("Patched " + slm.Name + ".CreateSaveLoadSlot (run state)");
				}
			}
		}

		// Harmony postfix on ParameterButtonChanger.Button_Click.
		private static void ChoiceClickPostfix(object __instance)
		{
			Recorder.RecordChoice(__instance as _Scripts.AMVCC.Views.ParameterButtonChanger);
		}

		private static void SaveFolderPostfix(ref string __result)
		{
			Recorder.FixSaveFolder(ref __result);
		}

		private static void SaveRepairPrefix(object __instance)
		{
			Recorder.EnsureSlotId(__instance);
		}

		// After the game has written Autosave.dat, keep a copy for the table.
		private static void SaveMirrorPostfix()
		{
			Recorder.MirrorGameAutosave();
		}

		private static void SaveListPostfix(List<SaveLoadHelper> __result)
		{
			Recorder.RecoverListIds(__result);
		}

		private static void LoadDataPostfix(string __0, SaveLoadHelper __result)
		{
			Recorder.RecoverId(__result, __0);
		}

		// The menu's save slot (SaveSlotBehaviour.OnPointerClick) calls
		// LoadGame(int) on every click and nothing stops a second one: a double
		// click loaded the save twice, the scene opened twice per load (four
		// copies), and the game sat on the timeline screen. The game's own load
		// screen needs seconds anyway, so a second call within LoadGuardSeconds
		// of the first is dropped.
		private const float LoadGuardSeconds = 3f;
		private static float _lastMenuLoad = -100f;

		private static bool GameLoadPrefix(object __instance, int __0, out bool __state)
		{
			float now = Time.realtimeSinceStartup;
			__state = now - _lastMenuLoad < LoadGuardSeconds;
			if (__state)
			{
				ModLog.Info("LoadGame(" + __0 + ") again " + (now - _lastMenuLoad).ToString("0.00")
					+ " s after the last one (a double click) - ignored");
				return false;
			}
			_lastMenuLoad = now;
			Recorder.BeforeGameMenuLoad(__instance, __0);
			return true;
		}

		private static void GameLoadPostfix(bool __state)
		{
			if (!__state)
			{
				Recorder.MarkGameSave(false);
			}
		}

		private static void ChapterLoadPrefix(int __1)
		{
			Recorder.BeforeChapterRestart(__1);
		}

		private static void ChapterLoadPostfix()
		{
			Recorder.MarkChapterLoad();
		}

		private static void NewGamePostfix(object __instance)
		{
			Recorder.MarkGameSave(true);
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
