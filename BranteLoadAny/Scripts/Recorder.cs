using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using I2.Loc;
using Assets._Scripts.AMVCC.Core.SceneStateMachine;
using _Scripts.AMVCC.Controllers;
using _Scripts.AMVCC.Models.Static;
using _Scripts.AMVCC.Views;
using _Scripts.AMVCC.Views.Windows.Destiny;
using _Scripts.Helpers;
using _Scripts.Managers;

namespace Doorstop
{
	// Records a snapshot of the full game state every time a choice scene
	// appears (Phase 3). The snapshot is a SaveLoadHelper serialised exactly
	// the way the game serialises its own saves (BinaryFormatter), so the
	// game's own LoadGame(SaveLoadHelper) can restore it unchanged in Phase 4.
	//
	// Deliberate differences from the game's SaveData():
	//  - no file I/O into the game's Saves/ folder (structural isolation:
	//    we only ever write into <persistentDataPath>/BranteLoadAny/)
	//  - SlotInfo kept empty and stable so identical states produce
	//    byte-identical blobs, which makes dedup exact
	public static class Recorder
	{
		private static bool _armed;

		// Session-wide switch: while true, the game's own SaveGame/
		// SaveChapterState are Harmony-blocked (see Entrypoint), so loading
		// a choice snapshot can never write into the game's Saves/ folder -
		// the player's original save stays exactly as it was. Set by the
		// dev autoload harness and by the choice-list load flow.
		public static bool BlockGameSaves;

		public static string SnapshotDir
		{
			get
			{
				return Path.Combine(Application.persistentDataPath, "BranteLoadAny", "Snapshots");
			}
		}

		// Phase 5: the player's own manual saves. Same snapshot format as the
		// choice points, kept in a separate folder so the two lists never see
		// each other's files.
		// One folder per in-game main save (= one playthrough). The game
		// names each one "<Hero>_<timestamp>" when a new game starts, so the
		// same hero name can exist several times; the ID is what tells them
		// apart.
		public static string RunsDir
		{
			get
			{
				return Path.Combine(Application.persistentDataPath, "BranteLoadAny", "Runs");
			}
		}

		// The main save the mod last loaded, used when the game's own
		// bookkeeping has lost it (a snapshot load restores the state, but the
		// game's SaveLoadManager can be recreated with an empty slot info).
		private static string _pinnedRunId;
		private static bool _idProblemLogged;

		public static void PinRunId(string id)
		{
			if (!string.IsNullOrEmpty(id))
			{
				_pinnedRunId = id;
				ModLog.Info("Recorder: main save pinned to " + id);
			}
		}

		// The current main save's ID, or "" when the game has none yet.
		public static string CurrentRunId()
		{
			string live = LiveRunId();
			if (!string.IsNullOrEmpty(live))
			{
				_pinnedRunId = live;
				return live;
			}
			if (!string.IsNullOrEmpty(_pinnedRunId))
			{
				return _pinnedRunId;
			}
			if (!_idProblemLogged)
			{
				_idProblemLogged = true;
				ModLog.Info("Recorder: no current main save ID (game has no save loaded yet)");
			}
			return "";
		}

		private static string LiveRunId()
		{
			try
			{
				SaveLoadManager slm = SaveLoadManager.Instance;
				if (slm == null)
				{
					slm = UnityEngine.Object.FindObjectOfType<SaveLoadManager>();
				}
				if (slm == null)
				{
					return "";
				}
				FieldInfo info = typeof(SaveLoadManager).GetField("_currentSaveSlotInfo",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				SaveSlotInfo slot = info != null ? info.GetValue(slm) as SaveSlotInfo : null;
				if (slot != null && !string.IsNullOrEmpty(slot.Id))
				{
					return slot.Id;
				}
				FieldInfo file = typeof(SaveLoadManager).GetField("_currentSaveFile",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				SaveLoadHelper cur = file != null ? file.GetValue(slm) as SaveLoadHelper : null;
				if (cur != null && cur.SlotInfo != null && !string.IsNullOrEmpty(cur.SlotInfo.Id))
				{
					return cur.SlotInfo.Id;
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: reading the current save ID failed:\n" + e);
			}
			return "";
		}

		// One-off move of the saves written before run folders existed. Those
		// files carry no main-save ID, so each is matched to the game's own
		// save by hero name, and only when that hero name belongs to exactly
		// one main save on disk. Anything ambiguous stays where it is.
		public static void MigrateLegacy()
		{
			try
			{
				Dictionary<string, HashSet<string>> idsByHero = new Dictionary<string, HashSet<string>>();
				string saves = Path.Combine(Application.persistentDataPath, "Saves");
				if (Directory.Exists(saves))
				{
					foreach (string f in Directory.GetFiles(saves, "Autosave.dat", SearchOption.AllDirectories))
					{
						try
						{
							SaveLoadHelper h = ReadHelper(f);
							string id = h.SlotInfo != null ? h.SlotInfo.Id : null;
							if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(h.HeroName))
							{
								continue;
							}
							HashSet<string> set;
							if (!idsByHero.TryGetValue(h.HeroName, out set))
							{
								set = new HashSet<string>();
								idsByHero[h.HeroName] = set;
							}
							set.Add(id);
						}
						catch (Exception e)
						{
							ModLog.Error("Migration: cannot read game save " + f + ":\n" + e);
						}
					}
				}

				List<string[]> legacy = new List<string[]>();
				AddLegacy(legacy, SnapshotDir, "Auto");
				AddLegacy(legacy, Path.Combine(Application.persistentDataPath, "BranteLoadAny", "Slots"), "Manual");
				AddLegacy(legacy, RunDir(null, "Auto"), "Auto");
				AddLegacy(legacy, RunDir(null, "Manual"), "Manual");
				if (legacy.Count == 0)
				{
					return;
				}

				int moved = 0;
				int kept = 0;
				foreach (string[] item in legacy)
				{
					string path = item[0];
					string kind = item[1];
					try
					{
						SaveLoadHelper h = ReadHelper(path);
						HashSet<string> ids;
						if (string.IsNullOrEmpty(h.HeroName)
							|| !idsByHero.TryGetValue(h.HeroName, out ids) || ids.Count != 1)
						{
							kept++;
							ModLog.Info("Migration: kept " + Path.GetFileName(path)
								+ " (no single main save for hero '" + h.HeroName + "')");
							continue;
						}
						string id = null;
						foreach (string one in ids)
						{
							id = one;
						}
						string dir = RunDir(id, kind);
						string dest = Path.Combine(dir, Path.GetFileName(path));
						Directory.CreateDirectory(dir);
						// The file is moved untouched: its name carries the
						// content hash the recorder dedups by.
						if (!File.Exists(dest))
						{
							File.Move(path, dest);
							string side = SidecarOf(path);
							if (File.Exists(side))
							{
								File.Move(side, SidecarOf(dest));
							}
						}
						else
						{
							// Already there (an identical state); drop the copy.
							File.Delete(path);
							string side = SidecarOf(path);
							if (File.Exists(side))
							{
								File.Delete(side);
							}
						}
						moved++;
					}
					catch (Exception e)
					{
						kept++;
						ModLog.Error("Migration: " + path + " failed:\n" + e);
					}
				}
				DeleteIfEmpty(RunDir(null, "Manual"));
				DeleteIfEmpty(RunDir(null, "Auto"));
				DeleteIfEmpty(Path.GetDirectoryName(RunDir(null, "Auto")));
				ModLog.Info("Migration: " + moved + " legacy save(s) moved into run folders, " + kept + " kept");
			}
			catch (Exception e)
			{
				ModLog.Error("Migration failed:\n" + e);
			}
		}

		private static void AddLegacy(List<string[]> list, string dir, string kind)
		{
			if (!Directory.Exists(dir))
			{
				return;
			}
			foreach (string f in Directory.GetFiles(dir, "*.dat"))
			{
				list.Add(new string[] { f, kind });
			}
		}

		private static string SidecarOf(string datPath)
		{
			return Path.Combine(Path.GetDirectoryName(datPath),
				Path.GetFileNameWithoutExtension(datPath) + ".meta.txt");
		}

		private static void DeleteIfEmpty(string dir)
		{
			if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
			{
				Directory.Delete(dir);
			}
		}

		private static SaveLoadHelper ReadHelper(string path)
		{
			using (FileStream fs = File.OpenRead(path))
			{
				return (SaveLoadHelper)new BinaryFormatter().Deserialize(fs);
			}
		}

		// The run folder a save file lives in, or "" if it is not under
		// RunsDir (e.g. a file from the old layout).
		public static string RunIdFromPath(string datPath)
		{
			try
			{
				string runs = Path.GetFullPath(RunsDir).TrimEnd(Path.DirectorySeparatorChar);
				string full = Path.GetFullPath(datPath);
				if (!full.StartsWith(runs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					return "";
				}
				string rest = full.Substring(runs.Length + 1);
				int sep = rest.IndexOf(Path.DirectorySeparatorChar);
				return sep > 0 ? rest.Substring(0, sep) : "";
			}
			catch
			{
				return "";
			}
		}

		// <RunsDir>/<run>/<kind>, kind = "Auto" (choice points) or "Manual".
		public static string RunDir(string runId, string kind)
		{
			string run = string.IsNullOrEmpty(runId) ? "_no_save" : Sanitize(runId);
			return Path.Combine(RunsDir, run, kind);
		}

		// Called from ModHost.Start (engine fully up). Proves the plumbing
		// (state copy + BinaryFormatter round-trip) before the first real
		// capture; at the main menu some managers may not exist yet, which
		// is fine - capture only ever happens in-game.
		public static void Arm()
		{
			if (_armed)
			{
				return;
			}
			_armed = true;
			ModLog.Info("Recorder armed; snapshot dir " + SnapshotDir);
			try
			{
				Directory.CreateDirectory(SnapshotDir);
				SaveLoadHelper probe = BuildHelper();
				byte[] blob = Serialize(probe);
				SaveLoadHelper back = (SaveLoadHelper)Deserialize(blob);
				ModLog.Info("Round-trip self-test OK: " + blob.Length + " bytes, hero '"
					+ back.HeroName + "', scene '" + back.LastOpenScene + "', turn " + back.Turn);
			}
			catch (Exception e)
			{
				ModLog.Info("Self-test deferred (expected at the main menu): " + e.Message);
			}
		}

		// Harmony postfix on SceneController.Awake: a choice scene just
		// appeared. The state is final at this moment - the chosen answer's
		// effects are only applied when a button is clicked - and
		// ShowedScenesList still has this scene as NOT IsShowed (that is
		// flipped later by SceneShowChecker.OnEnable). The not-yet-showed
		// flag is exactly what makes the game's loader re-open this very
		// scene instead of continuing past it.
		public static void OnChoiceSceneShown()
		{
			try
			{
				Capture();
			}
			catch (Exception e)
			{
				ModLog.Error("Choice-point capture failed:\n" + e);
			}
		}

		private static void Capture()
		{
			SaveLoadHelper helper = BuildHelper();
			byte[] blob = Serialize(helper);
			string scene = GameManager.Instance.OpenedSceneName;
			if (string.IsNullOrEmpty(scene))
			{
				scene = "(unknown)";
			}
			string hash = Sha1(blob).Substring(0, 10);
			string baseName = Sanitize(scene) + "_t" + helper.Turn + "_" + hash;
			string dir = RunDir(CurrentRunId(), "Auto");
			string dat = Path.Combine(dir, baseName + ".dat");
			if (File.Exists(dat))
			{
				ModLog.Info("Choice point unchanged, skipped: " + baseName);
				// Idempotent: fill in choice texts if a previous run did not.
				TryFillChoices(dir, baseName);
				return;
			}
			Directory.CreateDirectory(dir);
			File.WriteAllBytes(dat, blob);
			WriteSidecar(dir, baseName, scene, helper);
			TryFillChoices(dir, baseName);
			ModLog.Info("Choice point recorded: " + baseName + " (" + blob.Length + " bytes)");
		}

		// Manual save: a new entry in the mod's own folder every time. Never
		// touches the game's Saves/ folder.
		public static void SaveManual()
		{
			SaveLoadHelper helper = BuildHelper();
			byte[] blob = Serialize(helper);
			string scene = string.IsNullOrEmpty(helper.LastOpenScene) ? "(unknown)" : helper.LastOpenScene;
			string dir = RunDir(CurrentRunId(), "Manual");
			Directory.CreateDirectory(dir);
			string baseName = "save_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
			File.WriteAllBytes(Path.Combine(dir, baseName + ".dat"), blob);
			WriteSidecar(dir, baseName, scene, helper);
			ModLog.Info("Manual save " + baseName + " written to " + dir + " (" + blob.Length + " bytes, scene " + scene + ")");
		}

		// Mirrors SaveLoadManager.SaveData(int, int, SceneStates) field by
		// field, minus every bit of file I/O. Names below match the game's
		// own code (including its "CurrentConsequene" typo).
		private static SaveLoadHelper BuildHelper()
		{
			// The Instance/Initiate statics can point at objects that died
			// with their scene (Unity fake-null), so fall back to a fresh
			// scene search - the state managers are alive whenever a choice
			// scene is on screen, whichever scene hosts them.
			GameManager gm = GameManager.Instance;
			if (gm == null) { gm = UnityEngine.Object.FindObjectOfType<GameManager>(); }
			ParametersManager pm = ParametersManager.Instance;
			if (pm == null) { pm = UnityEngine.Object.FindObjectOfType<ParametersManager>(); }
			KeyChapterParametersController kpc = KeyChapterParametersController.Initiate;
			if (kpc == null) { kpc = UnityEngine.Object.FindObjectOfType<KeyChapterParametersController>(); }
			CharacterParametersSerializeHelper csh = CharacterParametersSerializeHelper.Initiate;
			if (csh == null) { csh = UnityEngine.Object.FindObjectOfType<CharacterParametersSerializeHelper>(); }
			SaveLoadManager slm = SaveLoadManager.Instance;
			if (slm == null) { slm = UnityEngine.Object.FindObjectOfType<SaveLoadManager>(); }
			if (gm == null) throw new InvalidOperationException("GameManager not alive");
			if (pm == null) throw new InvalidOperationException("ParametersManager not alive");
			if (kpc == null) throw new InvalidOperationException("KeyChapterParametersController not alive");
			if (csh == null) throw new InvalidOperationException("CharacterParametersSerializeHelper not alive");

			SaveLoadHelper helper = new SaveLoadHelper();
			// Kept empty and stable so identical states produce byte-identical
			// blobs; the main save is carried by the folder, not the file.
			helper.SlotInfo = new SaveSlotInfo { Id = "", Date = "" };
			helper.ModelParameters = pm.Parameters;
			helper.CharacterModel = csh.Characters;
			helper.ChapterParameters = kpc.AllKeyParameters;
			helper.SceneState = SceneStates.SceneState_SHOW_ANIMATION;
			helper.GodMode = gm.GodMode;
			helper.CurrentConsequence = gm.CurrentConsequene;
			helper.LastOpenScene = gm.OpenedSceneName;
			helper.HeroName = pm.HeroName;
			helper.Year = gm.GameManagerSerializer.CurrentYear;
			helper.HeroAge = pm.Age;
			helper.PreviousYear = gm.GameManagerSerializer.PreviousYear;
			helper.Turn = gm.Turn;
			helper.InsurrectionDayStage = gm.InsurrectionDayStage;
			helper.InsurrectionPreviousDayStage = gm.InsurrectionPreviousDayStage;
			helper.Season = gm.Season;
			helper.Chapter = gm.Chapter;
			helper.StoryMode = gm.StoryMode;
			helper.IronManMode = gm.IronManMode;
			helper.ShowedScenesList = gm.ShowedScenesList;
			helper.SceneInSequence = gm.SceneInSequence;
			string chapterName;
			if (slm == null || !slm.ChapterName.TryGetValue(gm.Chapter, out chapterName))
			{
				// SaveLoadManager can die with its scene; its ChapterName
				// table is a constant, so fall back to a verbatim copy.
				chapterName = FallbackChapterName(gm.Chapter);
			}
			helper.ChapterName = chapterName;

			helper.AllGameObjectives = new GameObjectives();
			helper.AllGameObjectives.Objectives = new List<ObjectiveStatuses>();
			foreach (Objective o in kpc.AllGameObjectives)
			{
				helper.AllGameObjectives.Objectives.Add(new ObjectiveStatuses
				{
					Name = o.Name,
					Status = o.Unlocked,
					Chapter = o.Chapter,
					UnlockDate = o.UnlockInfo.UnlockDate,
					UnlockYear = o.UnlockInfo.UnlockYear
				});
			}
			helper.AllGameEvents = new GameEvents();
			helper.AllGameEvents.GameEventsList = new List<GameEventStatus>();
			foreach (GameEvent ev in kpc.AllGameEvents)
			{
				helper.AllGameEvents.GameEventsList.Add(new GameEventStatus
				{
					Name = ev.name,
					Unlocked = ev.Unlocked,
					Chapter = ev.Chapter,
					UnlockDate = ev.Info.UnlockDate,
					UnlockYear = ev.Info.UnlockYear
				});
			}
			return helper;
		}

		// Verbatim copy of SaveLoadManager.ChapterName's default table,
		// used only when the manager object itself is not around.
		private static string FallbackChapterName(int chapter)
		{
			switch (chapter)
			{
				case 0: return "Chapter0.Childhood";
				case 1: return "Chapter1.Childhood";
				case 2: return "Chapter2.Teen";
				case 3: return "Chapter3.Youth";
				case 4: return "Chapter4.Peace";
				case 5: return "Chapter5.War";
				default: return "";
			}
		}

		private static byte[] Serialize(SaveLoadHelper helper)
		{
			using (MemoryStream ms = new MemoryStream())
			{
				new BinaryFormatter().Serialize(ms, helper);
				return ms.ToArray();
			}
		}

		private static object Deserialize(byte[] blob)
		{
			using (MemoryStream ms = new MemoryStream(blob))
			{
				return new BinaryFormatter().Deserialize(ms);
			}
		}

		// The answer buttons are usually serialized in the scene file as
		// INACTIVE objects (the show animation switches them on), so read
		// them regardless of activity; only fall back to waiting for the
		// animation if they do not exist yet at all.
		private static void TryFillChoices(string dir, string baseName)
		{
			ParameterButtonChanger[] buttons = CollectButtons();
			if (buttons.Length > 0)
			{
				try
				{
					FillChoicesNow(dir, baseName, buttons);
				}
				catch (Exception e)
				{
					ModLog.Error("Filling choice texts failed:\n" + e);
				}
			}
			else if (ModHost.Current != null)
			{
				ModHost.Current.StartCoroutine(FillChoicesWhenVisible(dir, baseName));
			}
		}

		private static ParameterButtonChanger[] CollectButtons()
		{
			ParameterButtonChanger[] active =
				UnityEngine.Object.FindObjectsOfType<ParameterButtonChanger>();
			if (active.Length > 0)
			{
				return active;
			}
			// FindObjectsOfType only sees ACTIVE objects; the buttons may be
			// inactive scene objects at this moment. Their texts are plain
			// serialized fields, so they are readable either way. The scene
			// filter drops prefab assets that FindObjectsOfTypeAll also
			// returns.
			List<ParameterButtonChanger> inScenes = new List<ParameterButtonChanger>();
			foreach (ParameterButtonChanger b in
				Resources.FindObjectsOfTypeAll<ParameterButtonChanger>())
			{
				if (b != null && b.gameObject.scene.IsValid())
				{
					inScenes.Add(b);
				}
			}
			ModLog.Info("Buttons: 0 active, " + inScenes.Count + " inactive in loaded scenes");
			return inScenes.ToArray();
		}

		private static void WriteSidecar(string dir, string baseName, string scene, SaveLoadHelper helper)
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("scene=" + scene);
			sb.AppendLine("recorded=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			sb.AppendLine("hero=" + helper.HeroName
				+ "  age=" + helper.HeroAge
				+ "  year=" + helper.Year
				+ "  turn=" + helper.Turn
				+ "  season=" + helper.Season);
			sb.AppendLine("chapter=" + helper.Chapter + " (" + helper.ChapterName + ")");
			SceneController sc = UnityEngine.Object.FindObjectOfType<SceneController>();
			if (sc != null)
			{
				sb.AppendLine("title=" + T(sc.TitleKey) + "   [key: " + sc.TitleKey + "]");
			}
			sb.AppendLine("choices:");
			ParameterButtonChanger[] buttons =
				UnityEngine.Object.FindObjectsOfType<ParameterButtonChanger>();
			Array.Sort(buttons, (a, b) => a.ButtonIndex.CompareTo(b.ButtonIndex));
			foreach (ParameterButtonChanger b in buttons)
			{
				sb.AppendLine("  [" + b.ButtonIndex + "] " + T(b.ButtonTextKey));
			}
			File.WriteAllText(Path.Combine(dir, baseName + ".meta.txt"), sb.ToString());
		}

		// Waits until the choice buttons are actually on screen, then appends
		// their texts to the sidecar written at capture time. Runs as a
		// coroutine on the host (see Capture).
		private static System.Collections.IEnumerator FillChoicesWhenVisible(string dir, string baseName)
		{
			for (int i = 0; i < 20; i++)
			{
				yield return new WaitForSeconds(0.5f);
				ParameterButtonChanger[] buttons =
					UnityEngine.Object.FindObjectsOfType<ParameterButtonChanger>();
				if (buttons.Length == 0)
				{
					continue;
				}
				try
				{
					FillChoicesNow(dir, baseName, buttons);
				}
				catch (Exception e)
				{
					ModLog.Error("Filling choice texts failed:\n" + e);
				}
				yield break;
			}
			ModLog.Info("No choice buttons ever appeared for " + baseName
				+ " (0 active, " + InactiveButtonCount() + " inactive in loaded scenes)");
		}

		private static int InactiveButtonCount()
		{
			int n = 0;
			foreach (ParameterButtonChanger b in
				Resources.FindObjectsOfTypeAll<ParameterButtonChanger>())
			{
				if (b != null && b.gameObject.scene.IsValid())
				{
					n++;
				}
			}
			return n;
		}

		private static void FillChoicesNow(string dir, string baseName, ParameterButtonChanger[] buttons)
		{
			Array.Sort(buttons, (a, b) => a.ButtonIndex.CompareTo(b.ButtonIndex));
			StringBuilder sb = new StringBuilder();
			foreach (ParameterButtonChanger b in buttons)
			{
				sb.AppendLine("  [" + b.ButtonIndex + "] " + T(b.ButtonTextKey));
			}
			string path = Path.Combine(dir, baseName + ".meta.txt");
			string text = File.ReadAllText(path);
			int cut = text.IndexOf("choices:", StringComparison.Ordinal);
			if (cut >= 0)
			{
				File.WriteAllText(path,
					text.Substring(0, cut + "choices:".Length) + "\n" + sb);
				ModLog.Info("Choice texts added for " + baseName + " ("
					+ buttons.Length + " button(s))");
			}
		}

		// I2 localisation lookup with the raw key as fallback (the game does
		// the same thing elsewhere).
		private static string T(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				return "";
			}
			try
			{
				string s = LocalizationManager.GetTranslation(key);
				return string.IsNullOrEmpty(s) ? key : s;
			}
			catch
			{
				return key;
			}
		}

		private static string Sha1(byte[] data)
		{
			using (SHA1 sha = SHA1.Create())
			{
				byte[] h = sha.ComputeHash(data);
				StringBuilder sb = new StringBuilder(h.Length * 2);
				foreach (byte b in h)
				{
					sb.Append(b.ToString("x2"));
				}
				return sb.ToString();
			}
		}

		private static string Sanitize(string name)
		{
			StringBuilder sb = new StringBuilder(name.Length);
			foreach (char c in name)
			{
				sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
			}
			return sb.ToString();
		}
	}
}
