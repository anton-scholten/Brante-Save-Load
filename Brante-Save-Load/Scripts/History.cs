using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using I2.Loc;
using _Scripts.Helpers;
using _Scripts.Managers;

namespace Doorstop
{
	// Phase 6: what the current playthrough has already been through.
	//
	// The game adds a scene name to GameManager.ShowedScenesList the first
	// time that scene opens, and flips IsShowed when it is actually shown.
	// The list is therefore the ordered path this playthrough has taken,
	// recorded by the game itself - including the part played before the mod
	// was installed. It records THAT a scene was shown, not which answer was
	// clicked, so it is a history of scenes, not of choices.
	public static class History
	{
		public sealed class Choice
		{
			public int Index;
			public string Text;
			public string Name;
			public bool Chosen;
		}

		public sealed class Entry
		{
			public int Index;
			public string SceneName;
			public bool IsShowed;
			public string Title;
			public string SnapshotPath;
			public string ChosenName;      // CurrentConsequence in this scene's snapshot
			public List<Choice> Choices = new List<Choice>();

			public bool HasSnapshot
			{
				get { return !string.IsNullOrEmpty(SnapshotPath); }
			}
		}

		// Reads the scene path from a save (no game state is touched).
		public static List<Entry> FromHelper(SaveLoadHelper helper)
		{
			return helper == null ? new List<Entry>()
				: FromList(helper.ShowedScenesList, helper.CurrentConsequence);
		}

		public static List<Entry> FromCurrentGame()
		{
			GameManager gm = GameManager.Instance;
			if (gm == null)
			{
				gm = UnityEngine.Object.FindObjectOfType<GameManager>();
			}
			return gm == null ? new List<Entry>() : FromList(gm.ShowedScenesList, gm.CurrentConsequene);
		}

		// The game's ShowedScenesList as entries; `latest` is the answer the
		// game holds now (CurrentConsequence), for the newest decision.
		private static List<Entry> FromList(List<ShowedScenes> shown, string latest)
		{
			List<Entry> list = new List<Entry>();
			if (shown == null)
			{
				return list;
			}
			for (int i = 0; i < shown.Count; i++)
			{
				ShowedScenes s = shown[i];
				if (s == null || string.IsNullOrEmpty(s.SceneName))
				{
					continue;
				}
				Entry e = new Entry();
				e.Index = i;
				e.SceneName = s.SceneName;
				e.IsShowed = s.IsShowed;
				e.Title = Loc.SceneTitle(s.SceneName);
				e.SnapshotPath = SnapshotFor(s.SceneName);
				LoadChoices(e);
				list.Add(e);
			}
			MarkChosen(list, latest ?? "");
			return list;
		}

		// The offered choices: from the scene's sidecar when the mod captured it,
		// otherwise from the game's own localisation table (SceneTexts), which
		// covers scenes played before the mod was installed.
		private static void LoadChoices(Entry e)
		{
			e.Choices = new List<Choice>();
			// The localisation table is the clean source: scene ids and answer
			// texts straight from the game, no stale sidecar entries (older
			// builds recorded empty rows for unused buttons). The sidecar is only
			// used when the table has no entry for this scene.
			List<KeyValuePair<string, string>> table = SceneTexts.Buttons(e.SceneName);
			if (table.Count > 0)
			{
				foreach (KeyValuePair<string, string> b in table)
				{
					if (string.IsNullOrEmpty(b.Value))
					{
						continue;
					}
					Choice c = new Choice();
					c.Index = e.Choices.Count + 1;
					c.Name = b.Key;
					c.Text = b.Value;
					e.Choices.Add(c);
				}
				return;
			}
			string sidecar = string.IsNullOrEmpty(e.SnapshotPath) ? "" :
				Path.ChangeExtension(e.SnapshotPath, ".meta.txt");
			if (string.IsNullOrEmpty(sidecar) || !File.Exists(sidecar))
			{
				return;
			}
			try
			{
				foreach (string raw in File.ReadAllLines(sidecar))
				{
					string line = raw.Trim();
					if (!line.StartsWith("[", StringComparison.Ordinal))
					{
						continue;
					}
					int close = line.IndexOf(']');
					if (close < 2)
					{
						continue;
					}
					Choice c = new Choice();
					int idx;
					if (int.TryParse(line.Substring(1, close - 1), out idx))
					{
						c.Index = idx;
					}
					string text = line.Substring(close + 1).Trim();
					// "(name: X)" follows the text, but the spaces before it are
					// trimmed away when the text itself is empty.
					int nameAt = text.IndexOf("(name: ", StringComparison.Ordinal);
					if (nameAt >= 0)
					{
						string name = text.Substring(nameAt + 7).TrimEnd(')', ' ');
						c.Name = name;
						text = text.Substring(0, nameAt);
					}
					c.Text = text.Trim();
					// Scenes carry inactive variants of the same button; keep
					// one node per distinct text, and skip empty entries.
					if (c.Text.Length == 0)
					{
						continue;
					}
					bool dup = false;
					foreach (Choice other in e.Choices)
					{
						dup = dup || other.Text == c.Text;
					}
					if (!dup)
					{
						e.Choices.Add(c);
					}
				}
			}
			catch (Exception ex)
			{
				ModLog.Error("History: reading choices of " + sidecar + " failed:\n" + ex);
			}
			CleanChoices(e);
		}

		// Sidecars of older builds can contain a button key instead of its text
		// (the game has no localisation for some unused buttons), or an empty
		// entry. Fill those from the localisation table and drop the rest, so the
		// tree never shows "ButtonDescription5".
		private static void CleanChoices(Entry e)
		{
			List<Choice> kept = new List<Choice>();
			foreach (Choice c in e.Choices)
			{
				if (string.IsNullOrEmpty(c.Text) || c.Text == c.Name)
				{
					string fromTable = SceneTexts.Text(e.SceneName, c.Name);
					if (!string.IsNullOrEmpty(fromTable))
					{
						c.Text = fromTable;
					}
				}
				if (string.IsNullOrEmpty(c.Text) || c.Text == c.Name)
				{
					continue;
				}
				bool dup = false;
				foreach (Choice other in kept)
				{
					dup = dup || other.Text == c.Text;
				}
				if (!dup)
				{
					kept.Add(c);
				}
			}
			e.Choices = kept;
		}

		// The answer the player picked is recorded by the click hook in the
		// scene's sidecar (chosen=). Without that line it is read from the
		// NEXT choice scene's snapshot: the game keeps the clicked button's
		// name in CurrentConsequence until the next click, so the snapshot
		// taken when the next choice scene opened still holds it. For the
		// newest decision `latest` (the live game's or the save's own value)
		// stands in when the scene on screen has no snapshot yet.
		private static void MarkChosen(List<Entry> list, string latest)
		{
			for (int i = 0; i < list.Count; i++)
			{
				Entry e = list[i];
				if (e.Choices.Count == 0)
				{
					continue;
				}
				string sidecar = string.IsNullOrEmpty(e.SnapshotPath) ? "" :
					Path.ChangeExtension(e.SnapshotPath, ".meta.txt");
				string chosen = RecordedChoice(sidecar);
				if (string.IsNullOrEmpty(chosen))
				{
					// The next scene that offers answers (machinery scenes in
					// between have no click and leave the value alone).
					for (int j = i + 1; j < list.Count; j++)
					{
						if (list[j].Choices.Count == 0)
						{
							continue;
						}
						chosen = ConsequenceIn(list[j]);
						if (string.IsNullOrEmpty(chosen) && j == list.Count - 1)
						{
							chosen = latest;
						}
						break;
					}
				}
				if (string.IsNullOrEmpty(chosen))
				{
					continue;
				}
				foreach (Choice c in e.Choices)
				{
					if (!string.IsNullOrEmpty(c.Name) && c.Name == chosen)
					{
						c.Chosen = true;
					}
				}
			}
		}

		// The sidecar's "chosen=" line ("" without one), re-read only when the
		// file changes (the click hook appends the line after the snapshot).
		private static readonly Dictionary<string, KeyValuePair<DateTime, string>> _chosen =
			new Dictionary<string, KeyValuePair<DateTime, string>>();

		private static string RecordedChoice(string sidecar)
		{
			if (string.IsNullOrEmpty(sidecar))
			{
				return "";
			}
			try
			{
				if (!File.Exists(sidecar))
				{
					return "";
				}
				DateTime t = File.GetLastWriteTimeUtc(sidecar);
				KeyValuePair<DateTime, string> hit;
				if (_chosen.TryGetValue(sidecar, out hit) && hit.Key == t)
				{
					return hit.Value;
				}
				string chosen = "";
				foreach (string raw in File.ReadAllLines(sidecar))
				{
					if (raw.StartsWith("chosen=", StringComparison.Ordinal))
					{
						chosen = raw.Substring(7).Trim();
					}
				}
				_chosen[sidecar] = new KeyValuePair<DateTime, string>(t, chosen);
				return chosen;
			}
			catch
			{
				return "";
			}
		}

		// CurrentConsequence stored in the entry's snapshot ("" without one).
		// Snapshot names carry a hash of their content, so a path always
		// holds the same value and the cache never goes stale.
		private static readonly Dictionary<string, string> _consequences =
			new Dictionary<string, string>();

		private static string ConsequenceIn(Entry e)
		{
			if (e.ChosenName != null)
			{
				return e.ChosenName;
			}
			e.ChosenName = "";
			if (!e.HasSnapshot)
			{
				return "";
			}
			string cached;
			if (_consequences.TryGetValue(e.SnapshotPath, out cached))
			{
				e.ChosenName = cached;
				return cached;
			}
			try
			{
				SaveLoadHelper h = Recorder.ReadHelper(e.SnapshotPath);
				e.ChosenName = h != null && h.CurrentConsequence != null ? h.CurrentConsequence : "";
				_consequences[e.SnapshotPath] = e.ChosenName;
			}
			catch
			{
				// unreadable snapshot: no highlight for the scene before it
			}
			return e.ChosenName;
		}

		// The newest choice-point snapshot the mod has for a scene (its file
		// names start with the scene name), or "" when it has none. Scenes
		// played before the mod was installed have no snapshot at all.
		public static string SnapshotFor(string sceneName)
		{
			List<string> paths;
			return SnapshotMap().TryGetValue(sceneName.Replace('/', '_'), out paths) && paths.Count > 0 ? paths[0] : "";
		}

		// The answer recorded as clicked in a snapshot's scene ("" if none).
		public static string ChosenIn(string snapshotPath)
		{
			return string.IsNullOrEmpty(snapshotPath) ? "" : RecordedChoice(Path.ChangeExtension(snapshotPath, ".meta.txt"));
		}

		// Every answer recorded as clicked in a snapshot's scene, newest last
		// (a save loaded more than once may have been answered differently).
		public static List<string> ChosenAllIn(string snapshotPath)
		{
			List<string> all = new List<string>();
			try
			{
				string sidecar = string.IsNullOrEmpty(snapshotPath) ? "" : Path.ChangeExtension(snapshotPath, ".meta.txt");
				if (sidecar.Length > 0 && File.Exists(sidecar))
				{
					foreach (string raw in File.ReadAllLines(sidecar))
					{
						if (raw.StartsWith("chosen=", StringComparison.Ordinal))
						{
							string c = raw.Substring(7).Trim();
							all.Remove(c);
							all.Add(c);
						}
					}
				}
			}
			catch
			{
				// unreadable sidecar: no answers
			}
			return all;
		}

		// The game's CurrentConsequence in a snapshot: the answer clicked in
		// the choice scene before it ("" if unknown).
		public static string ConsequenceOf(string snapshotPath)
		{
			if (string.IsNullOrEmpty(snapshotPath))
			{
				return "";
			}
			Entry e = new Entry();
			e.SnapshotPath = snapshotPath;
			return ConsequenceIn(e);
		}

		// Every snapshot of a scene, newest first. A scene has several when it
		// was reached on other paths too (a Load scene replay with other
		// answers before it records its own), or when its state changed while
		// it was on screen.
		public static List<string> SnapshotsFor(string sceneName)
		{
			List<string> paths;
			return SnapshotMap().TryGetValue(sceneName.Replace('/', '_'), out paths)
				? new List<string>(paths) : new List<string>();
		}

		// Snapshot files are "<scene>_t<turn>_<hash>.dat"; the scene part is
		// matched whole (a prefix test would let "01.02.01" pick up the files
		// of "01.02.01_2").
		private static readonly System.Text.RegularExpressions.Regex SnapshotName =
			new System.Text.RegularExpressions.Regex(@"^(.+)_t\d+_[0-9a-f]+$");
		private static string _mapDir;
		private static DateTime _mapStamp;
		private static Dictionary<string, List<string>> _map = new Dictionary<string, List<string>>();

		// Scene -> its snapshots in the current run, newest first; rebuilt only
		// when the folder changes (a file added or removed).
		private static Dictionary<string, List<string>> SnapshotMap()
		{
			try
			{
				string dir = Recorder.RunDir(Recorder.CurrentRunId(), "Auto");
				if (!Directory.Exists(dir))
				{
					return new Dictionary<string, List<string>>();
				}
				DateTime stamp = Directory.GetLastWriteTimeUtc(dir);
				if (dir == _mapDir && stamp == _mapStamp)
				{
					return _map;
				}
				Dictionary<string, List<string>> map = new Dictionary<string, List<string>>();
				Dictionary<string, DateTime> when = new Dictionary<string, DateTime>();
				foreach (string f in Directory.GetFiles(dir, "*.dat"))
				{
					System.Text.RegularExpressions.Match m = SnapshotName.Match(Path.GetFileNameWithoutExtension(f));
					if (!m.Success)
					{
						continue;
					}
					string scene = m.Groups[1].Value;
					List<string> list;
					if (!map.TryGetValue(scene, out list))
					{
						list = new List<string>();
						map[scene] = list;
					}
					list.Add(f);
					when[f] = File.GetLastWriteTimeUtc(f);
				}
				foreach (List<string> list in map.Values)
				{
					list.Sort((a, b) => when[b].CompareTo(when[a]));
				}
				_mapDir = dir;
				_mapStamp = stamp;
				_map = map;
				return map;
			}
			catch
			{
				return new Dictionary<string, List<string>>();
			}
		}

		// Dev dump (marker: Snapshots/dev-history.txt): logs the scene path
		// from the newest game save, and from the live game when one is loaded.
		public static void Dump()
		{
			try
			{
				SaveLoadHelper newest = NewestGameSave();
				if (newest != null)
				{
					DumpList("save file", newest);
				}
				DumpList("live game", null);
			}
			catch (Exception e)
			{
				ModLog.Error("History dump failed:\n" + e);
			}
		}

		private static void DumpList(string label, SaveLoadHelper helper)
		{
			List<Entry> list = helper != null ? FromHelper(helper) : FromCurrentGame();
			ModLog.Info("HISTORY (" + label + "): " + list.Count + " scene(s)");
			foreach (Entry e in list)
			{
				ModLog.Info("  [" + e.Index + "] " + e.SceneName
					+ (e.IsShowed ? " shown" : " pending")
					+ (e.HasSnapshot ? " *snapshot*" : "")
					+ (e.Title.Length > 0 ? "  '" + e.Title + "'" : ""));
			}
		}

		// Fallback when no game is loaded in memory (e.g. at the menu).
		public static List<Entry> FromNewestSave()
		{
			SaveLoadHelper helper = NewestGameSave();
			return helper != null ? FromHelper(helper) : new List<Entry>();
		}

		private static SaveLoadHelper NewestGameSave()
		{
			string saves = Path.Combine(UnityEngine.Application.persistentDataPath, "Saves");
			if (!Directory.Exists(saves))
			{
				return null;
			}
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
				return null;
			}
			using (FileStream fs = File.OpenRead(newest))
			{
				return (SaveLoadHelper)new BinaryFormatter().Deserialize(fs);
			}
		}
	}
}
