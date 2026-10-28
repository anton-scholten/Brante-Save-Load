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
	//    we only ever write into <persistentDataPath>/Brante-Save-Load/)
	//  - SlotInfo kept empty and stable so identical states produce
	//    byte-identical blobs, which makes dedup exact
	public static class Recorder
	{
		private static bool _armed;

		public static string SnapshotDir
		{
			get
			{
				return Path.Combine(Application.persistentDataPath, "Brante-Save-Load", "Snapshots");
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
				return Path.Combine(Application.persistentDataPath, "Brante-Save-Load", "Runs");
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
				AddLegacy(legacy, Path.Combine(Application.persistentDataPath, "Brante-Save-Load", "Slots"), "Manual");
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

		public static SaveLoadHelper ReadHelper(string path)
		{
			using (FileStream fs = File.OpenRead(path))
			{
				return (SaveLoadHelper)new BinaryFormatter().Deserialize(fs);
			}
		}

		// The real (unsanitised) main-save ID, e.g. "Biggie_10/23/2026103816AM".
		// The game needs it verbatim for its own save folder; a folder name
		// from RunsDir is sanitised, so it is matched against the game's saves.
		public static string RealRunId(string runFolderOrId)
		{
			if (string.IsNullOrEmpty(runFolderOrId))
			{
				return "";
			}
			try
			{
				string saves = Path.Combine(Application.persistentDataPath, "Saves");
				if (!Directory.Exists(saves))
				{
					return "";
				}
				string want = Sanitize(runFolderOrId);
				int seen = 0;
				foreach (string f in Directory.GetFiles(saves, "Autosave.dat", SearchOption.AllDirectories))
				{
					seen++;
					try
					{
						SaveLoadHelper h = ReadHelper(f);
						string id = h.SlotInfo != null ? h.SlotInfo.Id : null;
						if (!string.IsNullOrEmpty(id) && Sanitize(id) == want)
						{
							return id;
						}
					}
					catch
					{
						// unreadable save: skip
					}
				}
				if (!_realIdLogged)
				{
					_realIdLogged = true;
					ModLog.Info("Recorder: no match for '" + want + "' among " + seen + " game save(s)");
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: looking up the main save ID failed:\n" + e);
			}
			return "";
		}

		// The main-save id encoded in a game save path:
		//   .../Saves/Biggie_10/23/2026103816AM/Autosave.dat -> "Biggie_10/23/2026103816AM"
		// ("" when the file sits directly in Saves/, i.e. not in a save folder).
		public static string IdFromSavePath(string autosavePath)
		{
			try
			{
				string root = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "Saves"));
				string full = Path.GetFullPath(autosavePath);
				string dir = Path.GetDirectoryName(full);
				if (string.IsNullOrEmpty(dir) || !dir.StartsWith(root + Path.DirectorySeparatorChar,
					StringComparison.OrdinalIgnoreCase))
				{
					return "";
				}
				return dir.Substring(root.Length + 1);
			}
			catch
			{
				return "";
			}
		}

		// The raw id of the save the game currently has loaded ("" if none).
		public static string LiveRealId()
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
				FieldInfo f = typeof(SaveLoadManager).GetField("_currentSaveFile",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				SaveLoadHelper cur = f != null ? f.GetValue(slm) as SaveLoadHelper : null;
				if (cur == null || cur.SlotInfo == null)
				{
					return "";
				}
				return cur.SlotInfo.Id ?? "";
			}
			catch
			{
				return "";
			}
		}

		// Copies the game's own save folder aside, once, before the first
		// rewind. From then on the game saves normally into its own folder
		// (as it would after loading an older save from its own menu), so the
		// pre-mod timeline is kept here instead of being silently replaced.
		public static void BackupOriginalSave(string id)
		{
			try
			{
				if (string.IsNullOrEmpty(id))
				{
					return;
				}
				string src = Path.Combine(Application.persistentDataPath, "Saves", id);
				if (!Directory.Exists(src))
				{
					return;
				}
				string dst = Path.Combine(Application.persistentDataPath, "Brante-Save-Load",
					"Backups", Sanitize(id));
				if (Directory.Exists(dst))
				{
					return;
				}
				Directory.CreateDirectory(dst);
				int copied = 0;
				foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
				{
					if (string.Equals(Path.GetFileName(f), "steam_autocloud.vdf",
						StringComparison.OrdinalIgnoreCase))
					{
						continue; // Steam's own marker, not game data
					}
					string rel = f.Substring(src.Length).TrimStart(
						Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
					string target = Path.Combine(dst, rel);
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					File.Copy(f, target, false);
					copied++;
				}
				File.WriteAllText(Path.Combine(dst, "README.txt"), BackupReadme(id));
				ModLog.Info("Backed up the game's own save (" + copied + " file(s)) to " + dst);
			}
			catch (Exception e)
			{
				ModLog.Error("Backup of the game's own save failed:\n" + e);
			}
		}

		private static string BackupReadme(string id)
		{
			string saves = Path.Combine(Application.persistentDataPath, "Saves");
			return "Saves from before the Brante-Save-Load mod rewound the game\r\n"
				+ "===============================================================\r\n\r\n"
				+ "This folder is a copy of the game's own save \"" + id + "\", taken by the\r\n"
				+ "Brante-Save-Load mod right before it first loaded one of its own saves.\r\n"
				+ "From that moment on the game saved normally again, so the timeline you\r\n"
				+ "have in the game now is the rewound one; this is the only copy of the\r\n"
				+ "timeline you had before using the mod.\r\n\r\n"
				+ "To restore it:\r\n"
				+ "  1. Close the game.\r\n"
				+ "  2. Open the game's save folder (on this machine: " + saves + "):\r\n"
				+ "     Windows: %USERPROFILE%\\AppData\\LocalLow\\SEVER\\The Life and Suffering of Sir Brante\\Saves\\\r\n"
				+ "     Proton/Linux: inside the game's prefix, .../compatdata/1272160/pfx/drive_c/users/steamuser/AppData/LocalLow/SEVER/...\r\n"
				+ "     macOS (native build): ~/Library/Application Support/SEVER/The Life and Suffering of Sir Brante/Saves/\r\n"
				+ "     Delete (or rename) the folder named:\r\n"
				+ "       " + id + "\r\n"
				+ "  3. Copy the files in THIS folder back into a folder with that same name\r\n"
				+ "     (one level up from here, so that ...\\Saves\\" + id + "\\Autosave.dat exists).\r\n"
				+ "  4. Start the game and continue / load from its own menu.\r\n\r\n"
				+ "The mod's own saves are not affected by any of this: they live in\r\n"
				+ "Brante-Save-Load\\Runs\\ and are only removed from the mod's window.\r\n";
		}

		// The id the loaded save belongs to; used to repair the game's slot
		// bookkeeping if it is empty when the game saves (which would drop
		// files into Saves/ itself).
		private static string _adoptedId;
		private static bool _realIdLogged;
		private static bool _repairLogged;

		// Called from a prefix on SaveLoadManager.SaveGame/SaveChapterState:
		// only fills in a MISSING id, never changes a real one.
		public static void EnsureSlotId(object slmObj)
		{
			try
			{
				SaveLoadManager slm = slmObj as SaveLoadManager;
				if (slm == null)
				{
					return;
				}
				// The adopted id is the normal source; the live save is the
				// fallback for a save that happens before the mod adopted one.
				string id = _adoptedId;
				if (string.IsNullOrEmpty(id))
				{
					id = RealRunId(CurrentRunId());
				}
				if (string.IsNullOrEmpty(id))
				{
					return;
				}
				FieldInfo f = typeof(SaveLoadManager).GetField("_currentSaveSlotInfo",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (f == null)
				{
					return;
				}
				SaveSlotInfo slot = new SaveSlotInfo { Id = id };
				f.SetValue(slm, slot);
				// The chapter files are written through _currentSaveFile.
				FieldInfo cf = typeof(SaveLoadManager).GetField("_currentSaveFile",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				SaveLoadHelper cur = cf != null ? cf.GetValue(slm) as SaveLoadHelper : null;
				if (cur != null)
				{
					if (cur.SlotInfo == null)
					{
						cur.SlotInfo = new SaveSlotInfo();
					}
					cur.SlotInfo.Id = id;
				}
				if (!_repairLogged)
				{
					_repairLogged = true;
					ModLog.Info("Save repair: the game's slot id was empty, set to " + id);
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: save repair failed:\n" + e);
			}
		}

		// Last line of defence: SaveLoadManager.GetSaveFolderPath() returns
		// the bare Saves/ folder when the slot id is empty, which would drop
		// autosaves and chapter files next to the save folders. Corrected in
		// place, only when it points at the root and a run is known.
		public static void FixSaveFolder(ref string path)
		{
			try
			{
				if (string.IsNullOrEmpty(path))
				{
					return;
				}
				string root = Path.Combine(Application.persistentDataPath, "Saves");
				bool isRoot = string.Equals(path.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'),
					StringComparison.OrdinalIgnoreCase);
				bool mangled = !isRoot && !Directory.Exists(path)
					&& !string.IsNullOrEmpty(_adoptedId)
					&& Sanitize(_adoptedId).Equals(
						Path.GetFileName(path.TrimEnd('\\', '/')), StringComparison.OrdinalIgnoreCase);
				if (!isRoot && !mangled)
				{
					return;
				}
				string id = _adoptedId;
				if (string.IsNullOrEmpty(id))
				{
					id = RealRunId(CurrentRunId());
				}
				if (string.IsNullOrEmpty(id))
				{
					return;
				}
				path = Path.Combine(root, id);
				if (!_repairLogged)
				{
					_repairLogged = true;
					ModLog.Info("Save repair: save folder was the bare Saves/ or a mangled name, "
						+ "redirected to " + path);
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: save folder repair failed:\n" + e);
			}
		}

		// Points the game's slot bookkeeping at the save we just loaded, so
		// its own autosaves and chapter files go to that save's folder (they
		// would otherwise land in Saves/ itself).
		public static void AdoptAsCurrentSave(SaveLoadManager slm, SaveLoadHelper helper)
		{
			try
			{
				if (slm == null)
				{
					slm = UnityEngine.Object.FindObjectOfType<SaveLoadManager>();
				}
				if (slm == null)
				{
					ModLog.Info("Recorder: no SaveLoadManager to adopt the save slot on");
					return;
				}
				if (helper == null || helper.SlotInfo == null
					|| string.IsNullOrEmpty(helper.SlotInfo.Id))
				{
					// Nothing trustworthy to adopt; keep the previous one so a
					// known-good id is never replaced by an empty one.
					ModLog.Info("Recorder: loaded save has no main-save id; keeping the previous one");
					return;
				}
				FieldInfo f = typeof(SaveLoadManager).GetField("_currentSaveSlotInfo",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (f != null)
				{
					f.SetValue(slm, helper.SlotInfo);
				}
				_adoptedId = helper.SlotInfo.Id;
				ModLog.Info("Game saves continue into Saves/" + helper.SlotInfo.Id);
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: adopting the save slot failed:\n" + e);
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

		// One-off rename of the mod's data folder after the mod was renamed
		// from BranteLoadAny. Runs before anything else touches the folder.
		public static void MigrateDataDir()
		{
			try
			{
				string oldDir = Path.Combine(Application.persistentDataPath, "BranteLoadAny");
				string newDir = Path.Combine(Application.persistentDataPath, "Brante-Save-Load");
				if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
				{
					Directory.Move(oldDir, newDir);
					ModLog.Info("Moved mod data folder to " + newDir);
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Moving the mod data folder failed:\n" + e);
			}
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
			// The hash above was taken with an empty ID (stable dedup), but
			// the stored file carries the real main-save ID: the game derives
			// its save folder from it (e.g. "Restart chapter" after a death
			// reads Saves/<ID>/Chapter_N.dat; an empty ID would point at
			// Saves/ and make the game reset the run).
			byte[] stored = blob;
			string runId = CurrentRunId();
			if (!string.IsNullOrEmpty(runId))
			{
				helper.SlotInfo.Id = runId;
				stored = Serialize(helper);
			}
			File.WriteAllBytes(dat, stored);
			WriteSidecar(dir, baseName, scene, helper);
			TryFillChoices(dir, baseName);
			ModLog.Info("Choice point recorded: " + baseName + " (" + blob.Length + " bytes)");
		}

		// Appends one line to the run's trace log: every scene load and every
		// click, in order. Merging traces offline gives the graph edges
		// (which scene a choice leads to), which neither the save nor the
		// guides contain.
		public static void Trace(string kind, string a, string b)
		{
			try
			{
				string run = CurrentRunId();
				if (string.IsNullOrEmpty(run))
				{
					return;
				}
				string dir = Path.Combine(RunsDir, Sanitize(run));
				Directory.CreateDirectory(dir);
				string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
					+ "\t" + kind + "\t" + (a ?? "") + "\t" + (b ?? "");
				File.AppendAllText(Path.Combine(dir, "trace.log"), line + "\n");
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: trace write failed:\n" + e);
			}
		}

		// Harmony postfix on ParameterButtonChanger.Button_Click: the player
		// picked an answer. The game stores the button's GameObject name in
		// CurrentConsequence; we write it into this scene's sidecar so the
		// history tree can mark the branch actually taken.
		public static void RecordChoice(ParameterButtonChanger button)
		{
			try
			{
				if (button == null)
				{
					return;
				}
				string scene = GameManager.Instance != null ? GameManager.Instance.OpenedSceneName : "";
				string dir = RunDir(CurrentRunId(), "Auto");
				if (string.IsNullOrEmpty(scene) || !Directory.Exists(dir))
				{
					ModLog.Info("Recorder: click not recorded (scene '" + scene + "', folder "
						+ (Directory.Exists(dir) ? "ok" : "missing: " + dir) + ")");
					return;
				}
				string prefix = Sanitize(scene) + "_";
				string newest = null;
				DateTime newestTime = DateTime.MinValue;
				foreach (string f in Directory.GetFiles(dir, "*.meta.txt"))
				{
					if (!Path.GetFileName(f).StartsWith(prefix, StringComparison.Ordinal))
					{
						continue;
					}
					// the snapshot file must still exist
					// "<name>.meta.txt" -> "<name>.dat" (GetFileNameWithoutExtension
					// would leave "<name>.meta")
					string side = Path.GetFileName(f);
					string dat = Path.Combine(dir,
						side.Substring(0, side.Length - ".meta.txt".Length) + ".dat");
					if (!File.Exists(dat))
					{
						continue;
					}
					DateTime t = File.GetLastWriteTimeUtc(dat);
					if (t > newestTime)
					{
						newestTime = t;
						newest = f;
					}
				}
				if (newest == null)
				{
					ModLog.Info("Recorder: click not recorded (no snapshot of " + scene + " in " + dir + ")");
					return;
				}
				string sceneName = GameManager.Instance != null ? GameManager.Instance.OpenedSceneName : "";
				Trace("click", sceneName, button.gameObject.name + " | " + T(button.ButtonTextKey));
				string chosen = "chosen=" + button.gameObject.name;
				string text = File.ReadAllText(newest);
				if (text.IndexOf(chosen, StringComparison.Ordinal) < 0)
				{
					File.AppendAllText(newest, chosen + "\n");
					ModLog.Info("Choice recorded for " + Path.GetFileName(newest)
						+ ": " + button.gameObject.name);
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: recording the clicked choice failed:\n" + e);
			}
		}

		// Manual save: a new entry in the mod's own folder every time. Never
		// touches the game's Saves/ folder.
		public static void SaveManual()
		{
			SaveLoadHelper helper = BuildHelper();
			string runId = CurrentRunId();
			if (!string.IsNullOrEmpty(runId))
			{
				helper.SlotInfo.Id = runId;
			}
			byte[] blob = Serialize(helper);
			string scene = string.IsNullOrEmpty(helper.LastOpenScene) ? "(unknown)" : helper.LastOpenScene;
			string dir = RunDir(runId, "Manual");
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

		// Buttons of the scene currently on screen only: choice scenes are
		// additive, so buttons of earlier scenes are still loaded and would
		// otherwise end up in this scene's choice list.
		private static ParameterButtonChanger[] InCurrentScene(ParameterButtonChanger[] all, string scene)
		{
			if (string.IsNullOrEmpty(scene))
			{
				return all;
			}
			List<ParameterButtonChanger> keep = new List<ParameterButtonChanger>();
			foreach (ParameterButtonChanger b in all)
			{
				if (b != null && b.gameObject.scene.name == scene)
				{
					keep.Add(b);
				}
			}
			return keep.Count > 0 ? keep.ToArray() : all;
		}

		private static ParameterButtonChanger[] CollectButtons()
		{
			string scene = GameManager.Instance != null ? GameManager.Instance.OpenedSceneName : "";
			ParameterButtonChanger[] active =
				InCurrentScene(UnityEngine.Object.FindObjectsOfType<ParameterButtonChanger>(), scene);
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
				if (b != null && b.gameObject.scene.IsValid()
					&& (string.IsNullOrEmpty(scene) || b.gameObject.scene.name == scene))
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
			// The scene's own title key, not whatever SceneController is
			// found: choice scenes are additive, so FindObjectOfType can still
			// return the previous scene's controller (which made the sidecar
			// titles off by one).
			string titleKey = scene + "_Title";
			string title = T(titleKey);
			if (string.IsNullOrEmpty(title) || title == titleKey)
			{
				SceneController sc = UnityEngine.Object.FindObjectOfType<SceneController>();
				if (sc != null && !string.IsNullOrEmpty(sc.TitleKey))
				{
					titleKey = sc.TitleKey;
					title = T(titleKey);
				}
			}
			if (!string.IsNullOrEmpty(title) && title != titleKey)
			{
				sb.AppendLine("title=" + title + "   [key: " + titleKey + "]");
			}
			sb.AppendLine("choices:");
			ParameterButtonChanger[] buttons =
				UnityEngine.Object.FindObjectsOfType<ParameterButtonChanger>();
			Array.Sort(buttons, (a, b) => a.ButtonIndex.CompareTo(b.ButtonIndex));
			foreach (ParameterButtonChanger b in buttons)
			{
				sb.AppendLine("  [" + b.ButtonIndex + "] " + T(b.ButtonTextKey)
					+ "   (name: " + b.gameObject.name + ")");
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
				sb.AppendLine("  [" + b.ButtonIndex + "] " + T(b.ButtonTextKey)
					+ "   (name: " + b.gameObject.name + ")");
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

		// Illegal on Windows, plus the Unix ones, so a save id maps to the
		// same folder name whichever platform the game runs on.
		private static readonly char[] BadNameChars =
		{
			'\0', '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\n', '\r', '\t'
		};

		public static string SafeName(string name)
		{
			return Sanitize(name);
		}

		private static string Sanitize(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "";
			}
			StringBuilder sb = new StringBuilder(name.Length);
			foreach (char c in name)
			{
				bool bad = Array.IndexOf(BadNameChars, c) >= 0
					|| Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0;
				sb.Append(bad ? '_' : c);
			}
			return sb.ToString();
		}
	}
}
