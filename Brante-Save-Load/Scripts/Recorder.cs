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

		// The run the session is on, exactly as the game last changed saves
		// (new game, its own Load/Continue, or one of the mod's loads). "" is
		// a valid value: the game is playing a save with no main-save id.
		// Nothing here guesses an id from a previous run.
		private static string _runId;
		private static bool _runKnown;
		private static bool _idProblemLogged;

		// True while the game is playing a save the mod loaded. Only then may
		// the mod repair a missing slot id or redirect a save path; a save the
		// player loaded through the game's own menu is always left alone.
		private static bool _modTimeline;

		// The folders under Runs/<run>/.
		public const string KindChoice = "Choice";
		public const string KindManual = "Manual";
		public const string KindCheckpoints = "Checkpoints";
		public const string KindAutosaves = "Autosaves";

		// Retention (fixed, not a setting): the newest distinct game
		// autosaves and pre-rewind checkpoints kept per run. Manual saves,
		// choice points and the original backup are never pruned.
		private const int KeepAutosaves = 100;
		private const int KeepCheckpoints = 100;

		public static string ModDir
		{
			get { return Path.Combine(Application.persistentDataPath, "Brante-Save-Load"); }
		}

		public static string SavesDir
		{
			get { return Path.Combine(Application.persistentDataPath, "Saves"); }
		}

		// The save managers whose slot matters: the one the game saves through
		// (SaveLoadManager.Instance - set in Awake and never cleared, so it
		// can be an object Unity has destroyed, which a Unity "== null" calls
		// null; its fields still work and the game still saves with it), then
		// a live one if that is another object.
		public static List<SaveLoadManager> SaveManagers()
		{
			List<SaveLoadManager> list = new List<SaveLoadManager>();
			SaveLoadManager inst = SaveLoadManager.Instance;
			if (!object.ReferenceEquals(inst, null))
			{
				list.Add(inst);
			}
			SaveLoadManager live = UnityEngine.Object.FindObjectOfType<SaveLoadManager>();
			if (!object.ReferenceEquals(live, null) && !object.ReferenceEquals(live, inst))
			{
				list.Add(live);
			}
			return list;
		}

		// The one the game saves through, else a live one (null if none).
		public static SaveLoadManager GameSaveManager()
		{
			List<SaveLoadManager> all = SaveManagers();
			return all.Count > 0 ? all[0] : null;
		}

		// The folder for saves recorded while the game had no main-save id. A
		// folder name only: it must never reach the game as a save slot id
		// (the game would autosave into Saves/_no_save/, which nothing
		// creates, and the throw stops the scene from drawing - a blank book).
		public const string NoRun = "_no_save";

		// "" for the placeholder, else the id with canonical forward slashes
		// (the game builds its own ids from a date string; a backslash id
		// would be a literal character on macOS/Linux builds).
		public static string RealOrEmpty(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return "";
			}
			if (id == NoRun)
			{
				return "";
			}
			return id.IndexOf('\\') >= 0 ? id.Replace('\\', '/') : id;
		}

		// Dev: as if no run had been adopted or pinned (a game started from a
		// save without a main-save id).
		public static void DevForgetRun()
		{
			_runId = "";
			_runKnown = false;
			_modTimeline = false;
			FieldInfo f = typeof(SaveLoadManager).GetField("_currentSaveSlotInfo",
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			FieldInfo cf = typeof(SaveLoadManager).GetField("_currentSaveFile",
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			foreach (SaveLoadManager m in SaveManagers())
			{
				f.SetValue(m, new SaveSlotInfo { Id = "", Date = "" });
				SaveLoadHelper cur = cf.GetValue(m) as SaveLoadHelper;
				if (cur != null && cur.SlotInfo != null)
				{
					cur.SlotInfo.Id = "";
				}
			}
		}

		// Set when the mod loads a save (Load scene included).
		public static void BeginModLoad(string runId)
		{
			_runId = RealOrEmpty(runId);
			_runKnown = true;
			_modTimeline = true;
			ModLog.Info("Recorder: mod timeline, run '"
				+ (_runId == "" ? "(none)" : _runId) + "'");
		}

		// Set when the game itself changes saves (its Load / Continue / a new
		// game): any slot repair for the previous save no longer applies.
		public static void MarkGameSave(bool newGame)
		{
			Estimated = false;
			string live = LiveRealIdOf(GameSaveManager());
			_runId = RealOrEmpty(live);
			_runKnown = true;
			_modTimeline = false;
			_idProblemLogged = true;
			ModLog.Info("Recorder: " + (newGame ? "new game" : "game save loaded")
				+ ", run '" + (_runId == "" ? "(none)" : _runId) + "'");
		}

		public static bool ModTimeline
		{
			get { return _modTimeline; }
		}

		// A chapter restart (death -> "Restart chapter") loads Chapter_N.dat
		// over the live state; it is still the current timeline.
		public static void MarkChapterLoad()
		{
			string live = RealOrEmpty(LiveRealIdOf(GameSaveManager()));
			_runId = live;
			_runKnown = true;
			ModLog.Info("Recorder: chapter state loaded, run '"
				+ (live == "" ? "(none)" : live) + "'");
		}

		// Before the game's own Load/Continue: at the main menu there is no
		// live state, so keep the run's Autosave.dat (the file the load
		// replaces) as a pre-rewind checkpoint.
		public static void BeforeGameMenuLoad(object slmObj, int slotId)
		{
			try
			{
				SaveLoadManager slm = slmObj as SaveLoadManager;
				if (object.ReferenceEquals(slm, null))
				{
					return;
				}
				FieldInfo f = typeof(SaveLoadManager).GetField("_savedGames",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				List<SaveLoadHelper> list = f != null ? f.GetValue(slm) as List<SaveLoadHelper> : null;
				if (list == null || slotId < 0 || slotId >= list.Count)
				{
					return;
				}
				SaveLoadHelper h = list[slotId];
				string runId = h != null && h.SlotInfo != null ? RealOrEmpty(h.SlotInfo.Id) : "";
				BeforeGameLoad(runId, h != null ? h.LastOpenScene : "an older save");
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: game-load checkpoint failed:\n" + e);
			}
		}

		public static void PinRunId(string id)
		{
			_runId = RealOrEmpty(id);
			_runKnown = true;
			ModLog.Info("Recorder: run pinned to '"
				+ (_runId == "" ? "(none)" : _runId) + "'");
		}

		// The current main save's ID, or "" when the game has none.
		public static string CurrentRunId()
		{
			string live = LiveRunId();
			if (!string.IsNullOrEmpty(live))
			{
				_runId = live;
				_runKnown = true;
				return RealOrEmpty(live);
			}
			if (_runKnown)
			{
				return RealOrEmpty(_runId);
			}
			if (!_idProblemLogged)
			{
				_idProblemLogged = true;
				ModLog.Info("Recorder: no current main save ID yet");
			}
			return "";
		}

		private static string LiveRunId()
		{
			return LiveRealIdOf(GameSaveManager());
		}

		private static string LiveRealIdOf(SaveLoadManager slm)
		{
			try
			{
				if (object.ReferenceEquals(slm, null))
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
				AddLegacy(legacy, SnapshotDir, KindChoice);
				AddLegacy(legacy, Path.Combine(Application.persistentDataPath, "Brante-Save-Load", "Slots"), KindManual);
				AddLegacy(legacy, RunDir(null, KindChoice), KindChoice);
				AddLegacy(legacy, RunDir(null, "Auto"), KindChoice);
				AddLegacy(legacy, RunDir(null, KindManual), KindManual);
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
				DeleteIfEmpty(RunDir(null, KindManual));
				DeleteIfEmpty(RunDir(null, KindChoice));
				DeleteIfEmpty(Path.GetDirectoryName(RunDir(null, KindChoice)));
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

		// The game adds the scene it opens to ShowedScenesList (not yet shown)
		// only when the scene has finished loading (GameManager.
		// OnLoadOperationComplete); a choice scene is recorded as it appears,
		// which at ten times speed can be before that. A save whose
		// LastOpenScene is missing from the list loads as an empty book, so
		// the entry the game is about to add is added here. True if it was
		// missing (also repairs saves recorded before this fix, on load).
		public static bool EnsureSceneListed(SaveLoadHelper helper)
		{
			if (helper == null || string.IsNullOrEmpty(helper.LastOpenScene))
			{
				return false;
			}
			if (helper.ShowedScenesList == null)
			{
				helper.ShowedScenesList = new List<ShowedScenes>();
			}
			if (helper.ShowedScenesList.Exists(sc => sc != null && sc.SceneName == helper.LastOpenScene))
			{
				return false;
			}
			helper.ShowedScenesList.Add(new ShowedScenes { SceneName = helper.LastOpenScene, IsShowed = false });
			return true;
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
						// The folder name IS the id (Saves/<id>/Autosave.dat).
						// Saves written by older mod builds can carry an empty
						// SlotInfo.Id, so the path must be matched too.
						string folderId = IdFromSavePath(f);
						if (!string.IsNullOrEmpty(folderId) && Sanitize(folderId) == want)
						{
							return folderId;
						}
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
				return dir.Substring(root.Length + 1).Replace('\\', '/');
			}
			catch
			{
				return "";
			}
		}

		// The raw id of the save the game currently has loaded ("" if none).
		public static string LiveRealId()
		{
			return LiveRealIdOf(GameSaveManager());
		}

		public static string BackupsDir
		{
			get { return Path.Combine(ModDir, "Backups"); }
		}

		// One-time baseline: at the first start of this version every existing
		// game save folder is copied to Backups/<id>/ (immutable, never pruned,
		// never re-copied). It is the player's timeline before the mod was
		// used; later states are kept by the checkpoints and manual saves.
		public static void BaselineAll()
		{
			try
			{
				string marker = Path.Combine(ModDir, ".baseline");
				if (File.Exists(marker))
				{
					return;
				}
				Directory.CreateDirectory(BackupsDir);
				int n = 0;
				if (Directory.Exists(SavesDir))
				{
					foreach (string dir in Directory.GetDirectories(SavesDir, "*", SearchOption.AllDirectories))
					{
						bool hasAutosave = File.Exists(Path.Combine(dir, "Autosave.dat"));
						bool hasChapter = Directory.GetFiles(dir, "Chapter_*.dat").Length > 0;
						if (!hasAutosave && !hasChapter)
						{
							continue;
						}
						if (BaselineRun(IdFromSavePath(Path.Combine(dir, "Autosave.dat")), dir, false))
						{
							n++;
						}
					}
					if (File.Exists(Path.Combine(SavesDir, "Autosave.dat"))
						|| Directory.GetFiles(SavesDir, "Chapter_*.dat").Length > 0)
					{
						if (BaselineRun("", SavesDir, true))
						{
							n++;
						}
					}
				}
				File.WriteAllText(marker, "baseline taken " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
					+ Environment.NewLine);
				ModLog.Info("Baseline: " + n + " game save(s) copied to " + BackupsDir);
			}
			catch (Exception e)
			{
				ModLog.Error("Baseline failed:\n" + e);
			}
		}

		// Copies one game save folder to Backups/<id>/ once. True when a copy
		// was made (false when it already existed or there was nothing to do).
		public static bool EnsureBaseline(string id)
		{
			try
			{
				id = RealOrEmpty(id);
				string src = id == "" ? SavesDir : Path.Combine(SavesDir, id);
				return BaselineRun(id, src, id == "");
			}
			catch (Exception e)
			{
				ModLog.Error("Baseline of '" + id + "' failed:\n" + e);
				return false;
			}
		}

		private static bool BaselineRun(string id, string src, bool rootOnly)
		{
			if (!Directory.Exists(src))
			{
				return false;
			}
			string dest = Path.Combine(BackupsDir, Sanitize(id == "" ? NoRun : id));
			if (File.Exists(Path.Combine(dest, ".complete")))
			{
				return false;
			}
			if (Directory.Exists(dest))
			{
				// A backup taken by an older version: keep it untouched and
				// treat it as the baseline rather than replacing it.
				File.WriteAllText(Path.Combine(dest, ".complete"),
					"existing backup kept " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
					+ Environment.NewLine);
				ModLog.Info("Baseline: kept the existing backup " + dest);
				return false;
			}
			string tmp = dest + ".tmp-" + System.Diagnostics.Process.GetCurrentProcess().Id;
			try
			{
				if (Directory.Exists(tmp))
				{
					Directory.Delete(tmp, true);
				}
				Directory.CreateDirectory(tmp);
				int copied = 0;
				StringBuilder manifest = new StringBuilder();
				foreach (string f in Directory.GetFiles(src, "*",
					rootOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories))
				{
					if (string.Equals(Path.GetFileName(f), "steam_autocloud.vdf",
						StringComparison.OrdinalIgnoreCase))
					{
						continue; // Steam's own marker, not game data
					}
					string rel = rootOnly ? Path.GetFileName(f) : f.Substring(src.Length).TrimStart(
						Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
					string target = Path.Combine(tmp, rel);
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					File.Copy(f, target, false);
					manifest.AppendLine(rel + "\t" + new FileInfo(f).Length + "\t" + Sha1File(f));
					copied++;
				}
				File.WriteAllText(Path.Combine(tmp, "MANIFEST.txt"), manifest.ToString());
				File.WriteAllText(Path.Combine(tmp, "README.txt"), BackupReadme(id));
				File.WriteAllText(Path.Combine(tmp, ".complete"),
					DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
				Directory.Move(tmp, dest);
				ModLog.Info("Baseline: " + copied + " file(s) of '" + (id == "" ? "(no id)" : id)
					+ "' copied to " + dest);
				return true;
			}
			catch (Exception e)
			{
				ModLog.Error("Baseline of '" + id + "' failed:\n" + e);
				try
				{
					if (Directory.Exists(tmp))
					{
						Directory.Delete(tmp, true);
					}
				}
				catch
				{
				}
				return false;
			}
		}

		private static string BackupReadme(string id)
		{
			return "The game's own save \"" + (id == "" ? "(no main save id)" : id) + "\"\r\n"
				+ "===============================================================\r\n\r\n"
				+ "This folder is a copy of the game's own save as it was when the\r\n"
				+ "Brante-Save-Load mod was first started. It is never changed by the mod.\r\n"
				+ "The game keeps using its normal save folder for the live timeline;\r\n"
				+ "this copy exists so the timeline from before the mod was used can\r\n"
				+ "still be restored.\r\n\r\n"
				+ "To restore it:\r\n"
				+ "  1. Close the game.\r\n"
				+ "  2. Open the game's save folder (on this machine: " + SavesDir + "):\r\n"
				+ "     Windows: %USERPROFILE%\\AppData\\LocalLow\\SEVER\\The Life and Suffering of Sir Brante\\Saves\\\r\n"
				+ "     Proton/Linux: inside the game's prefix, .../compatdata/1272160/pfx/drive_c/users/steamuser/AppData/LocalLow/SEVER/...\r\n"
				+ "     macOS (native build): ~/Library/Application Support/SEVER/The Life and Suffering of Sir Brante/Saves/\r\n"
				+ "  3. Copy the files in THIS folder back into the save folder with the same\r\n"
				+ "     name (one level up from here), so that ...\\Saves\\" + id + "\\Autosave.dat exists.\r\n"
				+ "  4. Start the game and continue / load from its own menu.\r\n\r\n"
				+ "The mod's own saves are not affected by any of this: they live in\r\n"
				+ "Brante-Save-Load\\Runs\\ and are only removed from the mod's window.\r\n";
		}

		// -- repair of saves written by older versions --------------------

		// Saves written by older builds can carry an empty SlotInfo.Id even
		// though they sit in Saves/<id>/. The folder is the id: write it back
		// once, so the game's Load/Delete and its next autosave are correct.
		public static void RepairIds()
		{
			try
			{
				if (!Directory.Exists(SavesDir))
				{
					return;
				}
				foreach (string f in Directory.GetFiles(SavesDir, "Autosave.dat", SearchOption.AllDirectories))
				{
					try
					{
						SaveLoadHelper h = ReadHelper(f);
						string id = RealOrEmpty(IdFromSavePath(f));
						if (string.IsNullOrEmpty(id))
						{
							continue;
						}
						if (h.SlotInfo == null)
						{
							h.SlotInfo = new SaveSlotInfo();
						}
						if (RealOrEmpty(h.SlotInfo.Id) == id)
						{
							continue;
						}
						h.SlotInfo.Id = id;
						File.WriteAllBytes(f, Serialize(h));
						ModLog.Info("Repair: " + f + " had no id - written back as " + id);
					}
					catch (Exception e)
					{
						ModLog.Error("Repair: " + f + " failed:\n" + e);
					}
				}
			}
			catch (Exception e)
			{
				ModLog.Error("RepairIds failed:\n" + e);
			}
		}

		// A save written loose in Saves/ (a game that lost its main-save id)
		// is the same timeline as one of the run folders when the hero matches
		// and one scene list is the start of the other. Move it into that
		// folder instead of leaving a duplicate save entry behind.
		public static void RepairLooseRoot()
		{
			try
			{
				string rootAuto = Path.Combine(SavesDir, "Autosave.dat");
				if (!File.Exists(rootAuto))
				{
					return;
				}
				SaveLoadHelper h = ReadHelper(rootAuto);
				string why;
				string match = MatchRunFor(h, File.GetLastWriteTimeUtc(rootAuto), out why);
				if (string.IsNullOrEmpty(match))
				{
					ModLog.Info("Repair: Saves/Autosave.dat left in place - " + why);
					return;
				}
				string dest = Path.Combine(SavesDir, match);
				Directory.CreateDirectory(dest);
				string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
				string quarantine = Path.Combine(ModDir, "Backups", "quarantine", Sanitize(match));
				Directory.CreateDirectory(quarantine);
				string existing = Path.Combine(dest, "Autosave.dat");
				if (File.Exists(existing))
				{
					File.Copy(existing, Path.Combine(quarantine, "Autosave-" + stamp + ".dat"), false);
				}
				if (h.SlotInfo == null)
				{
					h.SlotInfo = new SaveSlotInfo();
				}
				h.SlotInfo.Id = RealOrEmpty(match);
				File.WriteAllBytes(rootAuto, Serialize(h));
				File.Copy(rootAuto, existing, true);
				File.Delete(rootAuto);
				CopyRootFile("testSave.txt", dest, quarantine, stamp);
				foreach (string f in Directory.GetFiles(SavesDir, "Chapter_*"))
				{
					CopyRootFile(Path.GetFileName(f), dest, quarantine, stamp);
				}
				ModLog.Info("Repair: the loose Saves/ files belonged to Saves/" + match + " - merged");
			}
			catch (Exception e)
			{
				ModLog.Error("RepairLooseRoot failed:\n" + e);
			}
		}

		private static void CopyRootFile(string name, string dest, string quarantine, string stamp)
		{
			try
			{
				string src = Path.Combine(SavesDir, name);
				if (!File.Exists(src))
				{
					return;
				}
				string target = Path.Combine(dest, name);
				if (File.Exists(target))
				{
					File.Copy(target, Path.Combine(quarantine, name + "-" + stamp), false);
				}
				File.Copy(src, target, true);
				File.Delete(src);
			}
			catch (Exception e)
			{
				ModLog.Error("Repair: moving " + name + " failed:\n" + e);
			}
		}

		// The run a loose save belongs to, or "" (with the reason in `why`).
		// It must continue that run's story - the run's scene list is the
		// start of its own, so it is the same timeline, further on or level -
		// and be newer than the run's save; otherwise merging would put an
		// older or another timeline's state where the game's Continue looks.
		// Every run shares the game's opening scenes, so at least five scenes
		// must be shared, and one run must share clearly more than any other.
		private static string MatchRunFor(SaveLoadHelper h, DateTime looseTime, out string why)
		{
			why = "no run folder continues into it";
			if (h == null || !Directory.Exists(SavesDir))
			{
				return "";
			}
			string best = "";
			int bestLen = -1;
			bool tie = false;
			foreach (string f in Directory.GetFiles(SavesDir, "Autosave.dat", SearchOption.AllDirectories))
			{
				string id = IdFromSavePath(f);
				if (string.IsNullOrEmpty(id))
				{
					continue;
				}
				try
				{
					SaveLoadHelper o = ReadHelper(f);
					if (!string.Equals(o.HeroName, h.HeroName, StringComparison.Ordinal))
					{
						continue;
					}
					int len = SharedPrefix(h.ShowedScenesList, o.ShowedScenesList);
					// the run's whole list is the start of the loose one
					if (len < 5 || len != Count(o.ShowedScenesList) || Count(o.ShowedScenesList) > Count(h.ShowedScenesList))
					{
						continue;
					}
					if (File.GetLastWriteTimeUtc(f) >= looseTime)
					{
						ModLog.Info("Repair: Saves/" + id + " continues into the loose save but is newer - not replaced");
						continue;
					}
					if (len > bestLen)
					{
						bestLen = len;
						best = id;
						tie = false;
					}
					else if (len == bestLen)
					{
						tie = true;
					}
				}
				catch
				{
					// unreadable save: skip
				}
			}
			if (tie)
			{
				why = "more than one run folder matches it equally";
				return "";
			}
			return best;
		}

		private static int Count(List<ShowedScenes> list)
		{
			return list == null ? 0 : list.Count;
		}

		// How many leading scenes two save histories share.
		private static int SharedPrefix(List<ShowedScenes> a, List<ShowedScenes> b)
		{
			if (a == null || b == null)
			{
				return 0;
			}
			int n = Math.Min(a.Count, b.Count);
			for (int i = 0; i < n; i++)
			{
				if (a[i] == null || b[i] == null || a[i].SceneName != b[i].SceneName)
				{
					return i;
				}
			}
			return n;
		}

		// Collapses copies of the same state in the automatic stores: two
		// mirror writes of one state (the game saved the same scene twice) and
		// checkpoints written before loading the same state. Manual saves are
		// never deleted here (the table hides identical rows instead).
		public static void DedupeStores()
		{
			try
			{
				if (!Directory.Exists(RunsDir))
				{
					return;
				}
				foreach (string run in Directory.GetDirectories(RunsDir))
				{
					PruneDuplicateCopies(Path.Combine(run, KindAutosaves));
					PruneDuplicateCopies(Path.Combine(run, KindCheckpoints));
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: deduplicating the stores failed:\n" + e);
			}
		}

		private static void PruneDuplicateCopies(string dir)
		{
			if (!Directory.Exists(dir))
			{
				return;
			}
			string[] files;
			try
			{
				files = Directory.GetFiles(dir, "*.dat");
			}
			catch
			{
				return;
			}
			Array.Sort(files, delegate(string a, string b)
			{
				return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
			});
			HashSet<string> seen = new HashSet<string>();
			int removed = 0;
			foreach (string f in files)
			{
				try
				{
					SaveLoadHelper h = ReadHelper(f);
					string sig = StateSignature(h);
					if (string.IsNullOrEmpty(sig))
					{
						continue;
					}
					if (!seen.Add(sig))
					{
						File.Delete(f);
						string side = SidecarOf(f);
						if (File.Exists(side))
						{
							File.Delete(side);
						}
						removed++;
					}
				}
				catch (Exception e)
				{
					ModLog.Error("Recorder: duplicate check of " + f + " failed:\n" + e);
				}
			}
			if (removed > 0)
			{
				ModLog.Info("Recorder: removed " + removed + " duplicate save(s) from " + dir);
			}
		}

		// One-off repair of sidecars written while two copies of a scene were
		// loaded: their choices: block lists every answer twice. Keep the first
		// line per [index], and every chosen= line after it.
		public static void DedupeSidecars()
		{
			try
			{
				if (!Directory.Exists(RunsDir))
				{
					return;
				}
				int fixedFiles = 0;
				foreach (string run in Directory.GetDirectories(RunsDir))
				{
					string dir = Path.Combine(run, KindChoice);
					if (!Directory.Exists(dir))
					{
						continue;
					}
					foreach (string file in Directory.GetFiles(dir, "*.meta.txt"))
					{
						try
						{
							string[] lines = File.ReadAllLines(file);
							List<string> keep = new List<string>();
							HashSet<int> seen = new HashSet<int>();
							bool inChoices = false;
							bool changed = false;
							foreach (string line in lines)
							{
								string trimmed = line.Trim();
								if (trimmed == "choices:")
								{
									inChoices = true;
									keep.Add(line);
									continue;
								}
								if (inChoices && trimmed.StartsWith("["))
								{
									int close = trimmed.IndexOf(']');
									int index;
									if (close > 1 && int.TryParse(trimmed.Substring(1, close - 1), out index))
									{
										if (!seen.Add(index))
										{
											changed = true;
											continue;
										}
									}
								}
								else if (inChoices && (trimmed.StartsWith("chosen=")
									|| trimmed.Length == 0))
								{
									// chosen= lines and the trailing blank are kept
								}
								else
								{
									inChoices = false;
								}
								keep.Add(line);
							}
							if (changed)
							{
								File.WriteAllLines(file, keep.ToArray());
								fixedFiles++;
							}
						}
						catch (Exception e)
						{
							ModLog.Error("Recorder: sidecar repair of " + file + " failed:\n" + e);
						}
					}
				}
				if (fixedFiles > 0)
				{
					ModLog.Info("Recorder: removed duplicate answer lines from "
						+ fixedFiles + " sidecar(s)");
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: sidecar repair failed:\n" + e);
			}
		}

		// Renames the old Runs/<run>/Auto folder to Choice, so the tree's
		// saves and the game autosaves are never mistaken for one another.
		public static void MigrateFolderNames()
		{
			try
			{
				if (!Directory.Exists(RunsDir))
				{
					return;
				}
				int n = 0;
				foreach (string run in Directory.GetDirectories(RunsDir))
				{
					string oldDir = Path.Combine(run, "Auto");
					if (!Directory.Exists(oldDir))
					{
						continue;
					}
					string newDir = Path.Combine(run, KindChoice);
					if (!Directory.Exists(newDir))
					{
						Directory.Move(oldDir, newDir);
					}
					else
					{
						foreach (string f in Directory.GetFiles(oldDir))
						{
							string target = Path.Combine(newDir, Path.GetFileName(f));
							if (!File.Exists(target))
							{
								File.Move(f, target);
							}
							else
							{
								File.Delete(f);
							}
						}
						try
						{
							if (Directory.GetFileSystemEntries(oldDir).Length == 0)
							{
								Directory.Delete(oldDir);
							}
						}
						catch
						{
						}
					}
					n++;
				}
				if (n > 0)
				{
					ModLog.Info("Renamed Runs/<run>/Auto -> " + KindChoice + " (" + n + " run(s))");
				}
			}
			catch (Exception e)
			{
				ModLog.Error("MigrateFolderNames failed:\n" + e);
			}
		}

		// -- pre-rewind checkpoints ---------------------------------------

		// Before a mod load: keep the live state (and the original folders of
		// both runs involved) so the load can always be undone.
		public static void BeforeModLoad(string targetRunId, string targetTitle)
		{
			try
			{
				string live = CurrentRunId();
				EnsureBaseline(live);
				if (!string.Equals(RealOrEmpty(targetRunId), live, StringComparison.OrdinalIgnoreCase))
				{
					EnsureBaseline(targetRunId);
				}
				CheckpointLive("before loading " + (targetTitle ?? targetRunId), targetRunId);
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: pre-rewind checkpoint of a mod load failed:\n" + e);
			}
		}

		// Before a load from the game's own menu: at the main menu there is no
		// live state, so the run's Autosave.dat - the file the load replaces -
		// is the state to keep.
		public static void BeforeGameLoad(string runId, string title)
		{
			try
			{
				runId = RealOrEmpty(runId);
				EnsureBaseline(runId);
				string file = runId == "" ? Path.Combine(SavesDir, "Autosave.dat")
					: Path.Combine(SavesDir, runId, "Autosave.dat");
				if (!File.Exists(file))
				{
					return;
				}
				byte[] bytes = File.ReadAllBytes(file);
				SaveLoadHelper h = (SaveLoadHelper)Deserialize(bytes);
				if (h == null)
				{
					return;
				}
				if (h.SlotInfo == null)
				{
					h.SlotInfo = new SaveSlotInfo();
				}
				if (string.IsNullOrEmpty(h.SlotInfo.Id))
				{
					h.SlotInfo.Id = runId;
				}
				// SlotInfo.Date is the game's save time, not state: clearing
				// it makes two identical states dedupe (the sidecar keeps the
				// recorded time).
				h.SlotInfo.Date = "";
				WriteCheckpoint(h, Serialize(h), "before loading " + (title ?? "an older save"), runId);
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: pre-rewind checkpoint of a game load failed:\n" + e);
			}
		}

		// Death -> "Restart chapter" loads Chapter_N.dat over the live state.
		public static void BeforeChapterRestart(int chapter)
		{
			try
			{
				EnsureBaseline(CurrentRunId());
				CheckpointLive("before restarting chapter " + chapter, null);
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: pre-rewind checkpoint of a chapter restart failed:\n" + e);
			}
		}

		private static void CheckpointLive(string reason, string alsoRun)
		{
			if (GameManager.Instance == null)
			{
				return;   // main menu: no live state to keep
			}
			SaveLoadHelper h = BuildHelper();
			string live = CurrentRunId();
			h.SlotInfo.Id = live;
			byte[] blob = Serialize(h);
			WriteCheckpoint(h, blob, reason, live);
			// A load that switches runs: the checkpoint is also filed with the
			// target run, so the table there shows it and can go back.
			if (!string.IsNullOrEmpty(alsoRun)
				&& !string.Equals(RealOrEmpty(alsoRun), live, StringComparison.OrdinalIgnoreCase))
			{
				WriteCheckpoint(h, blob, reason, alsoRun);
			}
		}

		private static void WriteCheckpoint(SaveLoadHelper h, byte[] blob, string reason, string runId)
		{
			string dir = RunDir(runId, KindCheckpoints);
			string scene = string.IsNullOrEmpty(h.LastOpenScene) ? "(unknown)" : h.LastOpenScene;
			string hash = Sha1(blob).Substring(0, 10);
			string name = Sanitize(scene) + "_t" + h.Turn + "_" + hash;
			string dat = Path.Combine(dir, name + ".dat");
			Directory.CreateDirectory(dir);
			if (!File.Exists(dat))
			{
				File.WriteAllBytes(dat, blob);
				WriteSidecar(dir, name, scene, h);
				AppendSidecar(dir, name, "kind=checkpoint" + Environment.NewLine
					+ "reason=" + (reason ?? "").Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine);
				Prune(dir, KeepCheckpoints);
			}
			ModLog.Info("Checkpoint: " + reason + " -> " + name);
		}

		// -- game autosave mirror -----------------------------------------

		// Copies the game's just-written Autosave.dat into the run's
		// Autosaves/ folder. This is the game's own resume point (state
		// GAME_SETUP), not a choice point; the two are kept apart.
		public static void MirrorGameAutosave()
		{
			try
			{
				if (AutoPlay.Running)
				{
					return;   // a replay gets one checkpoint, not one per scene
				}
				string runId = CurrentRunId();
				string file = runId == "" ? Path.Combine(SavesDir, "Autosave.dat")
					: Path.Combine(SavesDir, runId, "Autosave.dat");
				if (!File.Exists(file))
				{
					return;
				}
				byte[] bytes = File.ReadAllBytes(file);
				SaveLoadHelper h = (SaveLoadHelper)Deserialize(bytes);
				if (h == null)
				{
					return;
				}
				string scene = h.LastOpenScene ?? "(unknown)";
				// SlotInfo.Date changes on every game save while the state
				// does not: clear it before hashing/storing so identical
				// states dedupe (the sidecar keeps the recorded time).
				if (h.SlotInfo != null)
				{
					h.SlotInfo.Date = "";
				}
				byte[] state = Serialize(h);
				string hash = Sha1(state).Substring(0, 10);
				string dir = RunDir(runId, KindAutosaves);
				string name = Sanitize(scene) + "_t" + h.Turn + "_" + hash;
				string dat = Path.Combine(dir, name + ".dat");
				if (File.Exists(dat))
				{
					return;
				}
				Directory.CreateDirectory(dir);
				File.WriteAllBytes(dat, state);
				WriteSidecar(dir, name, scene, h);
				AppendSidecar(dir, name, "kind=autosave" + Environment.NewLine);
				Prune(dir, KeepAutosaves);
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: mirroring the game autosave failed:\n" + e);
			}
		}

		private static void AppendSidecar(string dir, string baseName, string text)
		{
			try
			{
				File.AppendAllText(Path.Combine(dir, baseName + ".meta.txt"), text);
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: sidecar append failed:\n" + e);
			}
		}

		// Keeps the newest `keep` .dat files (and their sidecars) in a folder.
		private static void Prune(string dir, int keep)
		{
			try
			{
				if (!Directory.Exists(dir))
				{
					return;
				}
				List<string> files = new List<string>(Directory.GetFiles(dir, "*.dat"));
				if (files.Count <= keep)
				{
					return;
				}
				files.Sort(delegate (string a, string b)
				{
					return File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b));
				});
				for (int i = 0; i < files.Count - keep; i++)
				{
					File.Delete(files[i]);
					string side = Path.Combine(Path.GetDirectoryName(files[i]),
						Path.GetFileNameWithoutExtension(files[i]) + ".meta.txt");
					if (File.Exists(side))
					{
						File.Delete(side);
					}
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: pruning " + dir + " failed:\n" + e);
			}
		}

		private static string Sha1File(string path)
		{
			try
			{
				using (FileStream fs = File.OpenRead(path))
				{
					using (SHA1 sha = SHA1.Create())
					{
						byte[] h = sha.ComputeHash(fs);
						StringBuilder sb = new StringBuilder(h.Length * 2);
						foreach (byte b in h)
						{
							sb.Append(b.ToString("x2"));
						}
						return sb.ToString();
					}
				}
			}
			catch
			{
				return "";
			}
		}

		private static bool _realIdLogged;
		private static bool _repairLogged;

		// The game's saves can carry an empty SlotInfo.Id (older mod builds,
		// or a game that loaded one of them). The folder name IS the id; fill
		// it in whenever the game reads a save, so its own Load/Continue and
		// its next autosave go back to the right folder.
		public static void RecoverId(SaveLoadHelper helper, string path)
		{
			try
			{
				if (helper == null || helper.SlotInfo == null
					|| !string.IsNullOrEmpty(helper.SlotInfo.Id) || string.IsNullOrEmpty(path))
				{
					return;
				}
				string id = RealOrEmpty(IdFromSavePath(path));
				if (string.IsNullOrEmpty(id))
				{
					return;   // a file directly in Saves/: genuinely no slot
				}
				helper.SlotInfo.Id = id;
				ModLog.Info("Save repair: " + Path.GetFileName(path) + " in Saves/" + id
					+ " had no id - taken from its folder");
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: id recovery failed for " + path + ":\n" + e);
			}
		}

		// Postfix on SaveLoadManager.LoadSaves: the helpers come from files
		// whose paths LoadSaves does not keep. Match them back by hero and
		// save date (both written by every save).
		public static void RecoverListIds(List<SaveLoadHelper> saves)
		{
			try
			{
				if (saves == null || saves.Count == 0 || !Directory.Exists(SavesDir))
				{
					return;
				}
				Dictionary<string, List<string>> byKey = new Dictionary<string, List<string>>();
				foreach (string f in Directory.GetFiles(SavesDir, "Autosave.dat", SearchOption.AllDirectories))
				{
					try
					{
						SaveLoadHelper h = ReadHelper(f);
						string key = (h.HeroName ?? "") + "\u0001"
							+ (h.SlotInfo != null ? h.SlotInfo.Date : "");
						List<string> list;
						if (!byKey.TryGetValue(key, out list))
						{
							list = new List<string>();
							byKey[key] = list;
						}
						list.Add(f);
					}
					catch
					{
						// unreadable save: skip
					}
				}
				foreach (SaveLoadHelper h in saves)
				{
					if (h == null || h.SlotInfo == null || !string.IsNullOrEmpty(h.SlotInfo.Id))
					{
						continue;
					}
					string key = (h.HeroName ?? "") + "\u0001" + (h.SlotInfo.Date ?? "");
					List<string> paths;
					// Only an unambiguous match: two runs saved in the same
					// hero/second would otherwise get each other's id.
					if (byKey.TryGetValue(key, out paths) && paths.Count == 1)
					{
						RecoverId(h, paths[0]);
					}
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: save-list id recovery failed:\n" + e);
			}
		}

		// Called from a prefix on SaveLoadManager.SaveGame/SaveChapterState:
		// fills in a MISSING id for a save the MOD loaded, never changes the
		// game's own slot and never invents an id.
		public static void EnsureSlotId(object slmObj)
		{
			try
			{
				// the object saving: possibly one Unity has destroyed, which
				// still saves - so not Unity's "== null"
				SaveLoadManager slm = slmObj as SaveLoadManager;
				if (object.ReferenceEquals(slm, null))
				{
					return;
				}
				if (!string.IsNullOrEmpty(RealOrEmpty(LiveRealIdOf(slm))))
				{
					return;   // the game knows its slot: leave it alone
				}
				if (!_modTimeline)
				{
					return;   // an id-less save the game itself is playing
				}
				string id = RealOrEmpty(_runId);
				if (string.IsNullOrEmpty(id))
				{
					return;   // id-less run
				}
				SetSlotId(slm, id);
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: save repair failed:\n" + e);
			}
		}

		// Sets the slot the game saves through (and the loaded save's own id,
		// used for Chapter_N.dat), keeping the slot's Date and object.
		private static void SetSlotId(SaveLoadManager slm, string id)
		{
			FieldInfo f = typeof(SaveLoadManager).GetField("_currentSaveSlotInfo",
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (f == null)
			{
				return;
			}
			SaveSlotInfo slot = f.GetValue(slm) as SaveSlotInfo;
			if (slot == null)
			{
				slot = new SaveSlotInfo();
				f.SetValue(slm, slot);
			}
			slot.Id = id;
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

		// Last line of defence: GetSaveFolderPath() returns the bare Saves/
		// folder when the slot id is empty. Only a save the MOD loaded may be
		// redirected; the game's own saves are never touched.
		public static void FixSaveFolder(ref string path)
		{
			try
			{
				if (string.IsNullOrEmpty(path) || !_modTimeline)
				{
					return;
				}
				string root = SavesDir;
				if (!string.Equals(path.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'),
					StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
				string id = RealOrEmpty(_runId);
				if (string.IsNullOrEmpty(id))
				{
					return;
				}
				path = Path.Combine(root, id);
				// the game creates its folder before this postfix runs; a
				// redirected one must exist too, or its save throws
				Directory.CreateDirectory(path);
				if (!_repairLogged)
				{
					_repairLogged = true;
					ModLog.Info("Save repair: save folder was the bare Saves/, redirected to " + path);
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: save folder repair failed:\n" + e);
			}
		}

		// Points the game's slot bookkeeping at the save we just loaded, so
		// its own autosaves and chapter files go to that save's folder (they
		// would otherwise land in Saves/ itself). An empty id is adopted too:
		// keeping the previous run is how saves ended up in another run.
		public static void AdoptAsCurrentSave(SaveLoadManager slm, SaveLoadHelper helper)
		{
			try
			{
				List<SaveLoadManager> managers = SaveManagers();
				if (!object.ReferenceEquals(slm, null) && !managers.Contains(slm))
				{
					managers.Add(slm);
				}
				if (managers.Count == 0)
				{
					ModLog.Info("Recorder: no SaveLoadManager to adopt the save slot on");
					return;
				}
				SaveSlotInfo slot = helper != null && helper.SlotInfo != null
					? helper.SlotInfo : new SaveSlotInfo();
				string id = RealOrEmpty(slot.Id) ?? "";
				if (id == "")
				{
					// A save with no main-save id (an old game, or one that
					// lost it): give it one, the way the game names a new
					// game's folder, so its autosaves and chapter files get a
					// folder of their own and the game's menu lists it.
					id = NewSlotId(helper);
					if (string.IsNullOrEmpty(slot.Date))
					{
						slot.Date = DateTime.Now.ToString();
					}
					ModLog.Info("Recorder: the save had no main-save id - it is now " + id);
				}
				slot.Id = id;
				FieldInfo f = typeof(SaveLoadManager).GetField("_currentSaveSlotInfo",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (f != null)
				{
					foreach (SaveLoadManager m in managers)
					{
						f.SetValue(m, slot);
					}
				}
				FieldInfo cf = typeof(SaveLoadManager).GetField("_currentSaveFile",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				foreach (SaveLoadManager m in managers)
				{
					SaveLoadHelper cur = cf != null ? cf.GetValue(m) as SaveLoadHelper : null;
					if (cur != null)
					{
						if (cur.SlotInfo == null)
						{
							cur.SlotInfo = new SaveSlotInfo();
						}
						cur.SlotInfo.Id = slot.Id;
					}
				}
				BeginModLoad(id);
				ModLog.Info("Game saves continue into Saves/"
					+ (string.IsNullOrEmpty(id) ? "(no main save id)" : id));
			}
			catch (Exception e)
			{
				ModLog.Error("Recorder: adopting the save slot failed:\n" + e);
			}
		}

		// A main-save id as SaveLoadManager.CreateSaveLoadSlot makes one:
		// hero name + "_" + DateTime.Now with its spaces, colons and dots
		// removed (slashes stay: the folder is nested). One second later if a
		// folder of that name exists already.
		private static string NewSlotId(SaveLoadHelper helper)
		{
			string hero = helper != null ? helper.HeroName : null;
			if (string.IsNullOrEmpty(hero) && ParametersManager.Instance != null)
			{
				hero = ParametersManager.Instance.HeroName;
			}
			if (string.IsNullOrEmpty(hero))
			{
				hero = "Hero";
			}
			DateTime now = DateTime.Now;
			for (int i = 0; ; i++)
			{
				string stamp = string.Format("{0}", now.AddSeconds(i))
					.Replace(" ", string.Empty).Replace(":", string.Empty).Replace(".", string.Empty);
				string id = hero + "_" + stamp;
				if (i >= 60 || !Directory.Exists(Path.Combine(SavesDir, id)))
				{
					return id;
				}
			}
		}

		public static bool IsBackupPath(string path)
		{
			try
			{
				string backups = Path.GetFullPath(BackupsDir).TrimEnd(Path.DirectorySeparatorChar);
				string full = Path.GetFullPath(path);
				return full.StartsWith(backups + Path.DirectorySeparatorChar,
					StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}

		// The run folder a save file lives in, or "" if it is not under
		// RunsDir (e.g. a file from the old layout).
		public static string RunIdForSavePath(string datPath)
		{
			try
			{
				string runs = Path.GetFullPath(RunsDir).TrimEnd(Path.DirectorySeparatorChar);
				string backups = Path.GetFullPath(BackupsDir).TrimEnd(Path.DirectorySeparatorChar);
				string full = Path.GetFullPath(datPath);
				if (full.StartsWith(backups + Path.DirectorySeparatorChar,
					StringComparison.OrdinalIgnoreCase))
				{
					// Backups/<sanitized id>/Autosave.dat
					string rest = full.Substring(backups.Length + 1);
					int sep = rest.IndexOf(Path.DirectorySeparatorChar);
					string folder = sep > 0 ? rest.Substring(0, sep) : rest;
					return RealOrEmpty(folder == NoRun ? "" : folder);
				}
				if (full.StartsWith(runs + Path.DirectorySeparatorChar,
					StringComparison.OrdinalIgnoreCase))
				{
					return RunIdFromPath(full);
				}
			}
			catch
			{
			}
			return "";
		}

		// <RunsDir>/<run>/... -> the run id ("" for the placeholder or when
		// the file is not under RunsDir).
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
				return sep > 0 ? RealOrEmpty(rest.Substring(0, sep)) : "";
			}
			catch
			{
				return "";
			}
		}

		// <RunsDir>/<run>/<kind>; kinds are KindChoice, KindManual,
		// KindCheckpoints and KindAutosaves.
		public static string RunDir(string runId, string kind)
		{
			string run = string.IsNullOrEmpty(runId) ? NoRun : Sanitize(runId);
			return Path.Combine(RunsDir, run, kind);
		}

		// The tree's choice-point folder. Reads the old "Auto" name too, in
		// case MigrateFolderNames could not rename it.
		public static string ChoiceDir(string runId)
		{
			string dir = RunDir(runId, KindChoice);
			if (Directory.Exists(dir))
			{
				return dir;
			}
			string old = RunDir(runId, "Auto");
			return Directory.Exists(old) ? old : dir;
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
		// The snapshot of the scene on screen, recorded or found unchanged: a
		// click there is noted in its sidecar. (The newest snapshot of the
		// scene could be another path's, when this state was saved before.)
		private static string _shownScene;
		private static string _shownSnapshot;

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

		// Dev: capture the current scene now (after changing a stat), so a
		// test plan starts from the modified state.
		public static void DevCapture()
		{
			OnChoiceSceneShown();
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
			string dir = RunDir(CurrentRunId(), KindChoice);
			string dat = Path.Combine(dir, baseName + ".dat");
			_shownScene = scene;
			_shownSnapshot = dat;
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
				string dir = RunDir(CurrentRunId(), KindChoice);
				if (string.IsNullOrEmpty(scene) || !Directory.Exists(dir))
				{
					ModLog.Info("Recorder: click not recorded (scene '" + scene + "', folder "
						+ (Directory.Exists(dir) ? "ok" : "missing: " + dir) + ")");
					return;
				}
				string prefix = Sanitize(scene) + "_";
				string newest = null;
				DateTime newestTime = DateTime.MinValue;
				if (scene == _shownScene && _shownSnapshot != null && File.Exists(_shownSnapshot))
				{
					newest = Path.ChangeExtension(_shownSnapshot, ".meta.txt");
					newestTime = DateTime.MaxValue;
				}
				foreach (string f in Directory.GetFiles(dir, "*.meta.txt"))
				{
					if (newestTime == DateTime.MaxValue)
					{
						break;
					}
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
			string dir = RunDir(runId, KindManual);
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
			helper.ShowedScenesList = new List<ShowedScenes>(gm.ShowedScenesList);
			EnsureSceneListed(helper);
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

		// A hash of the game state in a save, ignoring the save metadata that
		// changes on every write (SlotInfo.Date and Id). Two saves of the same
		// state therefore hash the same, which the table uses to show one row
		// instead of a row per press.
		public static string StateSignature(SaveLoadHelper helper)
		{
			if (helper == null)
			{
				return "";
			}
			SaveSlotInfo slot = helper.SlotInfo;
			string id = null;
			string date = null;
			if (slot != null)
			{
				id = slot.Id;
				date = slot.Date;
				slot.Id = "";
				slot.Date = "";
			}
			try
			{
				return Sha1(Serialize(helper));
			}
			catch
			{
				return "";
			}
			finally
			{
				if (slot != null)
				{
					slot.Id = id;
					slot.Date = date;
				}
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
				return UniqueButtons(active);
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
			ModLog.Info("Buttons: 0 active, " + UniqueButtons(inScenes.ToArray()).Length
				+ " inactive in loaded scenes");
			return UniqueButtons(inScenes.ToArray());
		}

		// Two additive copies of the same scene (the game can reload a scene
		// while its old copy is still up) carry the same answer buttons; keep
		// one per ButtonIndex so a sidecar is never written twice over.
		private static ParameterButtonChanger[] UniqueButtons(ParameterButtonChanger[] buttons)
		{
			List<ParameterButtonChanger> keep = new List<ParameterButtonChanger>();
			HashSet<int> seen = new HashSet<int>();
			foreach (ParameterButtonChanger b in buttons)
			{
				if (b != null && seen.Add(b.ButtonIndex))
				{
					keep.Add(b);
				}
			}
			return keep.ToArray();
		}

		// True while the game plays a state a quick load estimated (or one
		// grown from it): its choice points are marked estimated=yes, so they
		// never teach Transitions what the game does.
		public static bool Estimated;

		public static bool IsEstimated(string datPath)
		{
			try
			{
				if (datPath.Replace('\\', '/').IndexOf("/Estimated/", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
				string meta = Path.ChangeExtension(datPath, ".meta.txt");
				return File.Exists(meta) && File.ReadAllText(meta).IndexOf("estimated=yes", StringComparison.Ordinal) >= 0;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static void WriteSidecar(string dir, string baseName, string scene, SaveLoadHelper helper)
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("scene=" + scene);
			if (Estimated)
			{
				sb.AppendLine("estimated=yes");
			}
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
			ParameterButtonChanger[] buttons = UniqueButtons(
				UnityEngine.Object.FindObjectsOfType<ParameterButtonChanger>());
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
			buttons = UniqueButtons(buttons);
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
