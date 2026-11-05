using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.SceneManagement;
using _Scripts.Helpers;
using _Scripts.Managers;
using _Scripts.AMVCC.Views;

namespace Doorstop
{
	// The mod's host object. Created by the first Awake postfix (engine
	// fully up). Probes what the decompile says about the game, arms the
	// snapshot recorder, and runs the dev autoload/UI-test coroutines.
	// The features themselves live in companion statics (Recorder,
	// ChoiceWindow); this component is their Unity-side anchor.
	public class ModHost : MonoBehaviour
	{
		private static List<string> _devVars;
		// The one live host; lets static code (Recorder) start coroutines.
		public static ModHost Current;

		private void Awake()
		{
			Current = this;
		}

		// Every scene load is traced, so a click can be paired with the scene
		// it leads to (see Recorder.Trace).
		private void OnEnable()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private void OnDisable()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			Recorder.Trace("scene", scene.name, mode.ToString());
			if (AutoPlay.Running)
			{
				ModLog.Info("AUTOPLAY: scene loaded " + scene.name);
			}
			StartCoroutine(BookTabSoon());
		}

		// The scene's HUD is set up during its first frames; add the book tab
		// once it is there.
		private System.Collections.IEnumerator BookTabSoon()
		{
			for (int i = 0; i < 6; i++)
			{
				yield return new WaitForSecondsRealtime(0.5f);
				ChoiceWindow.EnsureBookTab();
			}
		}

		// Start fires the frame after the boot-hook postfix (see Entrypoint)
		// created us, i.e. the engine is fully up by now.
		private void Start()
		{
			Probe("Start", RunProbes);
			ProbeGameManagers();
			// The game writes no Unity log of its own here: its errors and
			// exceptions go into the mod's log, the only diagnostic channel.
			Application.logMessageReceived += GameLog;
			Recorder.MigrateDataDir();
			Recorder.MigrateFolderNames();
			Recorder.DedupeStores();
			// The player's own saves as they are now: copied once, before the
			// mod touches anything.
			Recorder.BaselineAll();
			// Saves from older versions: recover ids, merge a loose root save.
			Recorder.RepairIds();
			Recorder.RepairLooseRoot();
			Recorder.Arm();
			Recorder.MigrateLegacy();
			Recorder.DedupeSidecars();
			StartCoroutine(DevAutoload());
			StartCoroutine(DevStatTrail());
		}

		// Dev (dev-stattrail.txt: "<run folder> <stat> [<stat> ...]"): the
		// stats at every choice point of a run, oldest first - where a stat
		// such as Death or Will changed along a playthrough.
		private System.Collections.IEnumerator DevStatTrail()
		{
			string marker = Path.Combine(Recorder.SnapshotDir, "dev-stattrail.txt");
			if (!File.Exists(marker))
			{
				yield break;
			}
			string[] args = File.ReadAllText(marker).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			try { File.Delete(marker); }
			catch (Exception) { }
			yield return new WaitForSeconds(8f);
			try
			{
				string dir = Path.Combine(Path.Combine(Recorder.RunsDir, args[0]), "Choice");
				List<string> files = new List<string>(Directory.GetFiles(dir, "*.dat"));
				files.Sort((a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
				string last = null;
				foreach (string f in files)
				{
					SceneEffects.Hero h = SceneEffects.FromSnapshot(f);
					if (h == null)
					{
						continue;
					}
					System.Text.StringBuilder sb = new System.Text.StringBuilder();
					for (int i = 1; i < args.Length; i++)
					{
						sb.Append(" " + args[i] + "=" + h.Value(SceneEffects.ParameterAsset(args[i])));
					}
					string line = sb.ToString();
					if (line != last)
					{
						ModLog.Info("DEV STATTRAIL: " + Path.GetFileNameWithoutExtension(f) + line);
						last = line;
					}
				}
				ModLog.Info("DEV STATTRAIL: " + files.Count + " choice point(s)");
			}
			catch (Exception e)
			{
				ModLog.Error("DEV STATTRAIL failed:\n" + e);
			}
		}

		// Achievements: the game unlocks one only while
		// GameManager.BlockAchievements is false (set by AchievementsBlocker
		// objects in its scenes). Logged whenever it changes, with the
		// blockers that are loaded and whether Steam's stats arrived.
		private float _achNext;
		private int _achLast = -1;

		private void Update()
		{
			if (Time.realtimeSinceStartup < _achNext)
			{
				return;
			}
			_achNext = Time.realtimeSinceStartup + 1f;
			try
			{
				GameManager gm = GameManager.Instance;
				if (gm == null)
				{
					return;
				}
				int now = gm.BlockAchievements ? 1 : 0;
				if (now == _achLast)
				{
					return;
				}
				_achLast = now;
				System.Text.StringBuilder sb = new System.Text.StringBuilder();
				foreach (AchievementsBlocker b in Resources.FindObjectsOfTypeAll<AchievementsBlocker>())
				{
					sb.Append(" " + b.gameObject.scene.name + "/" + b.gameObject.name + "=" + b.BlockAchievements);
				}
				string stats = "?";
				foreach (_Scripts.AMVCC.Core.SteamAPI.SteamStatsAndAchievements st in
					Resources.FindObjectsOfTypeAll<_Scripts.AMVCC.Core.SteamAPI.SteamStatsAndAchievements>())
				{
					System.Reflection.FieldInfo f = st.GetType().GetField("m_bStatsValid",
						System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					stats = f != null ? f.GetValue(st).ToString() : "?";
				}
				ModLog.Info("Achievements: " + (now == 1 ? "BLOCKED" : "allowed") + " in " + AutoPlay.Story()
					+ ", Steam stats " + stats + ", blockers:" + (sb.Length > 0 ? sb.ToString() : " none"));
			}
			catch (Exception e)
			{
				_achNext = float.MaxValue;
				ModLog.Error("Achievements probe failed:\n" + e);
			}
		}

		private static string _lastGameLog;

		private static void GameLog(string message, string stack, LogType type)
		{
			if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
			{
				return;
			}
			string line = message + (string.IsNullOrEmpty(stack) ? "" : "\n" + stack.TrimEnd());
			if (line == _lastGameLog)
			{
				return;   // the same error every frame: once is enough
			}
			_lastGameLog = line;
			ModLog.Info("GAME " + type + ": " + line);
		}

		private void RunProbes()
		{
			ModLog.Info("=== Brante-Save-Load probe ===");
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
		// <persistentDataPath>/Brante-Save-Load/Snapshots/dev-autoload.txt
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
					string tableMarker = Path.Combine(Recorder.SnapshotDir, "dev-table.txt");
					if (File.Exists(tableMarker))
					{
						try { File.Delete(tableMarker); }
						catch (Exception) { }
						yield return new WaitForSeconds(1f);
						ChoiceWindow.DevTable();
						yield break;
					}
					// Dev: load one mod save through the real table path
					// (pre-rewind checkpoint, baseline, adopt, loading screen).
					// "dev-load.txt" holds a file name under Runs/.
					string loadMarker = Path.Combine(Recorder.SnapshotDir, "dev-load.txt");
					if (File.Exists(loadMarker))
					{
						string want = "";
						try { want = File.ReadAllText(loadMarker).Trim(); File.Delete(loadMarker); }
						catch (Exception) { }
						string found = null;
						if (!string.IsNullOrEmpty(want) && Directory.Exists(Recorder.RunsDir))
						{
							foreach (string f in Directory.GetFiles(Recorder.RunsDir, want,
								SearchOption.AllDirectories))
							{
								found = f;
							}
						}
						ModLog.Info("DEV LOAD: " + (found ?? "not found: " + want));
						if (found != null)
						{
							ChoiceWindow.LoadSnapshot(found);
							for (int w = 0; w < 30; w++)
							{
								yield return new WaitForSeconds(1f);
							}
							ModLog.Info("DEV LOAD: now at '" + AutoPlay.Story() + "', main save '"
								+ Recorder.CurrentRunId() + "'");
							string keyMarker = Path.Combine(Recorder.SnapshotDir, "dev-keyparam.txt");
							if (File.Exists(keyMarker))
							{
								string spec = "";
								try { spec = File.ReadAllText(keyMarker).Trim(); File.Delete(keyMarker); }
								catch (Exception) { }
								int eq = spec.IndexOf('=');
								if (eq > 0)
								{
									_Scripts.AMVCC.Models.Static.KeyChapterParametersController kc =
										_Scripts.AMVCC.Models.Static.KeyChapterParametersController.Initiate;
									if (kc != null)
									{
										kc.SetKeyChapterParameterValue(spec.Substring(0, eq), spec.Substring(eq + 1));
										ModLog.Info("DEV KEYPARAM: " + spec);
									}
								}
							}
							string statMarker = Path.Combine(Recorder.SnapshotDir, "dev-stat.txt");
							if (File.Exists(statMarker))
							{
								string spec = "";
								try { spec = File.ReadAllText(statMarker).Trim(); File.Delete(statMarker); }
								catch (Exception) { }
								int eq = spec.IndexOf('=');
								ParametersManager pm = ParametersManager.Instance;
								int value;
								if (eq > 0 && pm != null && pm.Parameters != null
									&& int.TryParse(spec.Substring(eq + 1), out value))
								{
									System.Reflection.FieldInfo f = pm.Parameters.GetType().GetField(
										spec.Substring(0, eq),
										System.Reflection.BindingFlags.Instance
										| System.Reflection.BindingFlags.Public);
									if (f != null)
									{
										f.SetValue(pm.Parameters, value);
										ModLog.Info("DEV STAT: " + spec);
									}
								}
							}
							string captureMarker = Path.Combine(Recorder.SnapshotDir, "dev-capture.txt");
							if (File.Exists(captureMarker))
							{
								try { File.Delete(captureMarker); }
								catch (Exception) { }
								Recorder.DevCapture();
							}
							string loadPlanMarker = Path.Combine(Recorder.SnapshotDir, "dev-plan.txt");
							if (File.Exists(loadPlanMarker))
							{
								string target = "";
								try { target = File.ReadAllText(loadPlanMarker).Trim(); File.Delete(loadPlanMarker); }
								catch (Exception) { }
								if (!string.IsNullOrEmpty(target))
								{
									string replayMarker = Path.Combine(Recorder.SnapshotDir, "dev-replay.txt");
									bool replay = File.Exists(replayMarker);
									int times = 1;   // dev-replay.txt "N": load the save again and replay, N times in one session
									if (replay)
									{
										try { int.TryParse(File.ReadAllText(replayMarker).Trim(), out times); File.Delete(replayMarker); }
										catch (Exception) { }
									}
									_devVars = times > 1 ? BoltDump.VariableLines() : null;
									if (_devVars != null)
									{
										ModLog.Info("DEV BOLT VARS before the first replay (" + _devVars.Count + "):\n  " + string.Join("\n  ", _devVars.ToArray()));
									}
									ChoiceWindow.DevPlan(target, replay);
									for (int again = 1; again < times; again++)
									{
										for (int w = 0; w < 60 && !AutoPlay.Running; w++)
										{
											yield return new WaitForSeconds(1f);
										}
										while (AutoPlay.Running)
										{
											yield return new WaitForSeconds(1f);
										}
										yield return new WaitForSeconds(3f);
										ModLog.Info("DEV REPLAY: again (" + (again + 1) + " of " + times + "), from " + found);
										List<string> now = BoltDump.VariableLines();
										if (_devVars != null)
										{
											foreach (string l in now)
											{
												if (!_devVars.Contains(l))
												{
													ModLog.Info("DEV BOLT VAR new/changed: " + l);
												}
											}
											foreach (string l in _devVars)
											{
												if (!now.Contains(l))
												{
													ModLog.Info("DEV BOLT VAR gone/was: " + l);
												}
											}
										}
										ChoiceWindow.LoadSnapshot(found);
										for (int w = 0; w < 20; w++)
										{
											yield return new WaitForSeconds(1f);
										}
										ChoiceWindow.DevPlan(target, true);
									}
								}
							}
						}
						// Fall through to the other dev markers (dev-book and
						// friends) so a load can be followed by a UI test.
					}
					// Phase 4 UI test: a second marker drives the choice-list
					// window (open -> list -> simulated clicks -> load).
					string uitest = Path.Combine(Recorder.SnapshotDir, "dev-uitest.txt");
					if (File.Exists(uitest))
					{
						try { File.Delete(uitest); }
						catch (Exception) { }
						ModLog.Info("DEV UITEST: marker found - starting UI test in 3s");
						yield return new WaitForSeconds(3f);
						ChoiceWindow.DevTest();
					}
					// Dev: dump the choice scene's UI pieces, to find what
					// turns the pages (and therefore reveals the buttons).
					string tree = Path.Combine(Recorder.SnapshotDir, "dev-scene-tree.txt");
					if (File.Exists(tree))
					{
						try { File.Delete(tree); }
						catch (Exception) { }
						yield return new WaitForSeconds(1f);
						DumpActiveScene();
					}
					// Dev: dump the Bolt graph that handles the choice click.
					string bolt = Path.Combine(Recorder.SnapshotDir, "dev-bolt.txt");
					if (File.Exists(bolt))
					{
						try { File.Delete(bolt); }
						catch (Exception) { }
						yield return new WaitForSeconds(2f);
						BoltDump.DumpFor(SceneManager.GetActiveScene().name);
					}
					// Dev: auto-play probe - page, answer, then log the buttons the
					// game puts up next.
					string autoplay = Path.Combine(Recorder.SnapshotDir, "dev-autoplay.txt");
					if (File.Exists(autoplay))
					{
						string spec = "";
						try { spec = File.ReadAllText(autoplay).Trim(); File.Delete(autoplay); }
						catch (Exception) { }
						if (spec.StartsWith("chapters", StringComparison.Ordinal))
						{
							List<int> want = new List<int>();
							foreach (string w in spec.Substring(8).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
							{
								int n;
								if (int.TryParse(w, out n))
								{
									want.Add(n);
								}
							}
							yield return AutoPlay.DebugRun(want.ToArray());
						}
						else
						{
							yield return AutoPlay.Probe(80f);
						}
					}
					// The game's chapter restart, as its Restart button makes it
					// (RestartOrContinuePopupController.RestartButton_Click):
					// the pre-restart checkpoint and the run state after it.
					string restart = Path.Combine(Recorder.SnapshotDir, "dev-restart.txt");
					if (File.Exists(restart))
					{
						try { File.Delete(restart); }
						catch (Exception) { }
						yield return new WaitForSeconds(2f);
						DevRestartChapter();
						yield return new WaitForSeconds(15f);
						GameManager rg = GameManager.Instance;
						ModLog.Info("DEV RESTART: now at " + AutoPlay.Story() + ", chapter "
							+ (rg != null ? rg.Chapter.ToString() : "?") + ", run '" + Recorder.CurrentRunId() + "'");
					}
					// Phase 6: dump the scene path the playthrough has taken.
					string history = Path.Combine(Recorder.SnapshotDir, "dev-history.txt");
					if (File.Exists(history))
					{
						try { File.Delete(history); }
						catch (Exception) { }
						yield return new WaitForSeconds(1f);
						ModLog.Info("DEV HISTORY: marker found - dumping the scene path");
						History.Dump();
					}
					// UI test: the book tabs, the save table and the tree view
					// (ChoiceWindow.DevBook).
					string book = Path.Combine(Recorder.SnapshotDir, "dev-book.txt");
					if (File.Exists(book))
					{
						try { File.Delete(book); }
						catch (Exception) { }
						ModLog.Info("DEV BOOK: marker found - opening the book in 3s");
						ChoiceWindow.DevLanguageSwitch = File.Exists(
							Path.Combine(Recorder.SnapshotDir, "dev-lang.txt"));
						yield return new WaitForSeconds(3f);
						ChoiceWindow.DevBook();
					}
					yield break;
				}
			}
			ModLog.Error("DEV AUTOLOAD: scene " + scene + " never opened");
		}

		private static void DevRestartChapter()
		{
			try
			{
				GameManager gm = GameManager.Instance;
				SaveLoadManager slm = Recorder.GameSaveManager();
				ModLog.Info("DEV RESTART: restarting chapter " + (gm != null ? gm.Chapter : 0) + " from " + AutoPlay.Story());
				slm.LoadChapterState(gm.Chapter);
			}
			catch (Exception e)
			{
				ModLog.Error("DEV RESTART: failed:\n" + e);
			}
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

		// Dumps the components of the scene on screen that look like story UI.
		private static void DumpActiveScene()
		{
			Scene scene = SceneManager.GetActiveScene();
			ModLog.Info("DEV SCENE: '" + scene.name + "' root objects:"
				+ scene.GetRootGameObjects().Length);
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				DumpGo(root.transform, 0);
			}
		}

		private static void DumpGo(Transform t, int depth)
		{
			if (depth > 6)
			{
				return;
			}
			string comps = "";
			bool interesting = false;
			foreach (Component c in t.GetComponents<Component>())
			{
				if (c is Transform || c == null)
				{
					continue;
				}
				string n = c.GetType().Name;
				comps += " " + n;
				string lower = n.ToLowerInvariant();
				if (lower.Contains("button") || lower.Contains("page") || lower.Contains("text")
					|| lower.Contains("book") || lower.Contains("scene"))
				{
					interesting = true;
				}
			}
			if (interesting || depth <= 1)
			{
				ModLog.Info("DEV SCENE: " + new string('-', depth * 2) + t.name
					+ (t.gameObject.activeSelf ? "" : " (off)") + " [" + comps.Trim() + "]");
			}
			for (int i = 0; i < t.childCount; i++)
			{
				DumpGo(t.GetChild(i), depth + 1);
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
					// A file directly in Saves/ is not in a save folder (the
					// game wrote it without a slot); it is not a real save.
					if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(f)),
						Path.GetFullPath(saves).TrimEnd(Path.DirectorySeparatorChar),
						StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
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
				// Dev: run the loaded save as an Iron Man game, so the mod's
				// load path can be exercised in that mode.
				if (File.Exists(Path.Combine(Recorder.SnapshotDir, "dev-ironman.txt")))
				{
					helper.IronManMode = true;
					ModLog.Info("DEV IRONMAN: forcing IronManMode on the loaded save");
				}
				// The blob's id can be missing; the folder name is reliable.
				string fromPath = Recorder.IdFromSavePath(newest);
				if (helper.SlotInfo == null || string.IsNullOrEmpty(helper.SlotInfo.Id))
				{
					if (helper.SlotInfo == null)
					{
						helper.SlotInfo = new SaveSlotInfo { Id = fromPath };
					}
					else
					{
						helper.SlotInfo.Id = fromPath;
					}
				}
				ModLog.Info("DEV AUTOLOAD: main save '" + helper.SlotInfo.Id + "'");
				Recorder.PinRunId(helper.SlotInfo != null ? helper.SlotInfo.Id : null);
				Recorder.AdoptAsCurrentSave(SaveLoadManager.Instance, helper);
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
