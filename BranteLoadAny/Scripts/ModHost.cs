using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.SceneManagement;
using _Scripts.Helpers;
using _Scripts.Managers;

namespace Doorstop
{
	// The mod's host object. Phase 2 = runtime probe: logs what the
	// decompile says about the game so the two can be compared. Later
	// phases add the snapshot recorder and the choice-list UI here.
	public class ModHost : MonoBehaviour
	{
		// The one live host; lets static code (Recorder) start coroutines.
		public static ModHost Current;

		private void Awake()
		{
			Current = this;
		}

		// Start fires the frame after the boot-hook postfix (see Entrypoint)
		// created us, i.e. the engine is fully up by now.
		private void Start()
		{
			Probe("Start", RunProbes);
			ProbeGameManagers();
			Recorder.Arm();
			StartCoroutine(DevAutoload());
		}

		private void RunProbes()
		{
			ModLog.Info("=== BranteLoadAny probe ===");
			ModLog.Info("Unity " + Application.unityVersion + ", platform " + Application.platform);
			ModLog.Info("persistentDataPath: " + Application.persistentDataPath);
			ProbeSaves();
			ProbeChoiceLog();
		}

		// Wraps a probe so one failing call does not hide the others, and so
		// the log says exactly which call the game's Mono objected to.
		private static void Probe(string stage, Action body)
		{
			try
			{
				body();
			}
			catch (Exception e)
			{
				ModLog.Error("Probe " + stage + " failed:\n" + e.ToString());
			}
		}

		private void ProbeSaves()
		{
			string saves = Path.Combine(Application.persistentDataPath, "Saves");
			if (!Directory.Exists(saves))
			{
				ModLog.Info("Saves folder missing: " + saves);
				return;
			}
			string[] dirs = Directory.GetDirectories(saves);
			ModLog.Info("Saves: " + dirs.Length + " slot(s) in " + saves);
			foreach (string dir in dirs)
			{
				foreach (string f in Directory.GetFiles(dir))
				{
					ModLog.Info("  " + Path.GetFileName(dir) + "/" + Path.GetFileName(f) + " ("
						+ new FileInfo(f).Length + " bytes)");
				}
			}
		}

		private void ProbeChoiceLog()
		{
			string log = Path.Combine(Application.persistentDataPath, "log.txt");
			if (File.Exists(log))
			{
				string text = File.ReadAllText(log);
				ModLog.Info("log.txt (answer log, decompile said dead code): "
					+ (text.Length > 300 ? text.Substring(0, 300) + "..." : text));
			}
			else
			{
				ModLog.Info("log.txt does not exist yet (expected before first save)");
			}
		}

		// GameManager/SaveLoadManager only exist once the game has booted
		// far enough; poll from Start instead of Awake.
		private void ProbeGameManagers()
		{
			StartCoroutine(WaitForGame());
		}

		private System.Collections.IEnumerator WaitForGame()
		{
			for (int i = 0; i < 120; i++)
			{
				yield return new WaitForSeconds(1f);
				Type gm = Type.GetType("_Scripts.Managers.GameManager, Assembly-CSharp");
				Type slm = Type.GetType("_Scripts.Managers.SaveLoadManager, Assembly-CSharp");
				if (gm != null && slm != null)
				{
					UnityEngine.Object gmInst = UnityEngine.Object.FindObjectOfType(gm);
					UnityEngine.Object slmInst = UnityEngine.Object.FindObjectOfType(slm);
					ModLog.Info("GameManager found: " + (gmInst != null)
						+ ", SaveLoadManager found: " + (slmInst != null));
					// Confirm the public LoadGame(SaveLoadHelper) overload exists
					// as decompiled (by type walk; the instance may still be null
					// in the main menu).
					Type helper = Type.GetType("_Scripts.Helpers.SaveLoadHelper, Assembly-CSharp");
					var method = slm != null
						? slm.GetMethod("LoadGame", new Type[] { helper })
						: null;
					ModLog.Info("SaveLoadHelper type: " + (helper != null)
						+ ", SaveLoadManager.LoadGame(SaveLoadHelper) present: " + (method != null));
					// Also confirm the choice UI types we plan to hook.
					ModLog.Info("SceneController type: "
						+ (Type.GetType("_Scripts.AMVCC.Controllers.SceneController, Assembly-CSharp") != null)
						+ ", ParameterButtonChanger type: "
						+ (Type.GetType("_Scripts.AMVCC.Views.ParameterButtonChanger, Assembly-CSharp") != null)
						+ ", WindowPause type: "
						+ (Type.GetType("_Scripts.AMVCC.Views.Windows.WindowPause, Assembly-CSharp") != null));
					yield break;
				}
			}
			ModLog.Info("GameManager/SaveLoadManager never appeared in 120s (still in main menu?)");
		}

		// DEV-ONLY test harness (not part of the player-facing mod): if
		// <persistentDataPath>/BranteLoadAny/Snapshots/dev-autoload.txt
		// exists shortly after boot, load the newest autosave and play out
		// the game's OWN load sequence (loading screen -> dissolve ->
		// AutomaticSceneLoad) without any UI interaction - used to exercise
		// the recorder end to end and dry-run Phase 4's load path. The
		// marker is deleted immediately, so this can never fire twice.
		private System.Collections.IEnumerator DevAutoload()
		{
			yield return new WaitForSeconds(5f);
			string marker = Path.Combine(Recorder.SnapshotDir, "dev-autoload.txt");
			if (!File.Exists(marker))
			{
				yield break;
			}
			try { File.Delete(marker); } catch { }
			ModLog.Info("DEV AUTOLOAD: marker found, loading newest autosave");
			// The harness bypasses the game's slot bookkeeping (which decides
			// WHERE autosaves are written), so block the game's own saves for
			// the rest of this run - Saves/ must stay untouched.
			Recorder.DevBlockGameSaves = true;
			string scene = DevAutoloadDo();
			if (string.IsNullOrEmpty(scene))
			{
				yield break;
			}
			yield return new WaitForSeconds(1f);

			// Mirror the vanilla flow (SaveSlot click -> LoadGame, then what
			// the Continue button does): open GameLoadingScreen additively -
			// SceneTransitionDissolve + GameLoadingScreenBehaviour live there
			// - and broadcast StartSceneTransitionEvent. The dissolve's
			// animation event broadcasts SceneTransitionCompleteEvent ->
			// AutomaticSceneLoad() re-opens the save's scene (IsShowed=false
			// branch), which is where the recorder fires.
			AsyncOperation ao = DevOpenLoadingScreen();
			if (ao == null)
			{
				yield break;
			}
			while (!ao.isDone)
			{
				yield return null;
			}
			ModLog.Info("DEV AUTOLOAD: GameLoadingScreen loaded; broadcasting StartSceneTransitionEvent");
			DevBroadcastTransition();

			// Wait for the target scene to open (capture fires in its
			// SceneController.Awake); the show animation then puts the
			// answer buttons on screen for the sidecar's choice texts.
			for (int i = 0; i < 30; i++)
			{
				yield return new WaitForSeconds(1f);
				Scene opened = SceneManager.GetSceneByName(scene);
				if (opened.IsValid() && opened.isLoaded)
				{
					ModLog.Info("DEV AUTOLOAD: scene " + scene + " is open");
					yield break;
				}
			}
			ModLog.Error("DEV AUTOLOAD: scene " + scene + " never opened");
		}

		// try/catch without yields (C# forbids yield inside try-catch).
		private AsyncOperation DevOpenLoadingScreen()
		{
			try
			{
				return SceneManager.LoadSceneAsync("GameLoadingScreen", LoadSceneMode.Additive);
			}
			catch (Exception e)
			{
				ModLog.Error("DEV AUTOLOAD: loading screen failed to open:\n" + e);
				return null;
			}
		}

		private void DevBroadcastTransition()
		{
			try
			{
				Messenger.Broadcast("StartSceneTransitionEvent");
			}
			catch (Exception e)
			{
				ModLog.Error("DEV AUTOLOAD: transition broadcast failed:\n" + e);
			}
		}

		// Does the file+reflection part of the dev autoload; no yields in
		// here so it can use try/catch (C# forbids yield inside try-catch).
		private string DevAutoloadDo()
		{
			try
			{
				string saves = Path.Combine(Application.persistentDataPath, "Saves");
				string newest = null;
				DateTime newestTime = DateTime.MinValue;
				foreach (string f in Directory.GetFiles(saves, "Autosave.dat", SearchOption.AllDirectories))
				{
					DateTime t = File.GetLastWriteTimeUtc(f);
					if (t > newestTime)
					{
						newestTime = t;
						newest = f;
					}
				}
				if (newest == null)
				{
					ModLog.Error("DEV AUTOLOAD: no Autosave.dat found");
					return null;
				}
				SaveLoadHelper helper;
				using (FileStream fs = File.OpenRead(newest))
				{
					helper = (SaveLoadHelper)new BinaryFormatter().Deserialize(fs);
				}
				ModLog.Info("DEV AUTOLOAD: " + newest + " -> hero '" + helper.HeroName
					+ "', scene '" + helper.LastOpenScene + "', turn " + helper.Turn);
				SaveLoadManager.Instance.LoadGame(helper);
				return helper.LastOpenScene;
			}
			catch (Exception e)
			{
				ModLog.Error("DEV AUTOLOAD failed:\n" + e);
				return null;
			}
		}
	}
}
