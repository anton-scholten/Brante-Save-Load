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
			public string ChosenName;      // CurrentConsequence stored in the save
			public List<Choice> Choices = new List<Choice>();

			public bool HasSnapshot
			{
				get { return !string.IsNullOrEmpty(SnapshotPath); }
			}
		}

		// Reads the scene path from a save (no game state is touched).
		public static List<Entry> FromHelper(SaveLoadHelper helper)
		{
			List<Entry> list = new List<Entry>();
			if (helper == null || helper.ShowedScenesList == null)
			{
				return list;
			}
			for (int i = 0; i < helper.ShowedScenesList.Count; i++)
			{
				ShowedScenes s = helper.ShowedScenesList[i];
				if (s == null || string.IsNullOrEmpty(s.SceneName))
				{
					continue;
				}
				Entry e = new Entry();
				e.Index = i;
				e.SceneName = s.SceneName;
				e.IsShowed = s.IsShowed;
				e.Title = TitleOf(s.SceneName);
				e.SnapshotPath = SnapshotFor(s.SceneName);
				e.ChosenName = helper.CurrentConsequence ?? "";
				LoadChoices(e);
				list.Add(e);
			}
			MarkChosen(list);
			return list;
		}

		public static List<Entry> FromCurrentGame()
		{
			GameManager gm = GameManager.Instance;
			if (gm == null)
			{
				gm = UnityEngine.Object.FindObjectOfType<GameManager>();
			}
			if (gm == null || gm.ShowedScenesList == null)
			{
				return new List<Entry>();
			}
			List<Entry> list = new List<Entry>();
			for (int i = 0; i < gm.ShowedScenesList.Count; i++)
			{
				ShowedScenes s = gm.ShowedScenesList[i];
				if (s == null || string.IsNullOrEmpty(s.SceneName))
				{
					continue;
				}
				Entry e = new Entry();
				e.Index = i;
				e.SceneName = s.SceneName;
				e.IsShowed = s.IsShowed;
				e.Title = TitleOf(s.SceneName);
				e.SnapshotPath = SnapshotFor(s.SceneName);
				e.ChosenName = gm.CurrentConsequene ?? "";
				LoadChoices(e);
				list.Add(e);
			}
			MarkChosen(list);
			return list;
		}

		// The offered choices: from the scene's sidecar when the mod captured it,
		// otherwise from the game's own localisation table (SceneTexts), which
		// covers scenes played before the mod was installed.
		private static void LoadChoices(Entry e)
		{
			e.Choices = new List<Choice>();
			string sidecar = string.IsNullOrEmpty(e.SnapshotPath) ? "" :
				Path.ChangeExtension(e.SnapshotPath, ".meta.txt");
			if (string.IsNullOrEmpty(sidecar) || !File.Exists(sidecar))
			{
				foreach (KeyValuePair<string, string> b in SceneTexts.Buttons(e.SceneName))
				{
					Choice c = new Choice();
					c.Index = e.Choices.Count + 1;
					c.Name = b.Key;
					c.Text = b.Value;
					e.Choices.Add(c);
				}
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
					int nameAt = text.IndexOf("   (name: ", StringComparison.Ordinal);
					if (nameAt >= 0)
					{
						string name = text.Substring(nameAt + 10).TrimEnd(')', ' ');
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
		}

		// The answer the player picked is stored in the NEXT snapshot
		// (CurrentConsequence holds the clicked button's name), and, for the
		// newest decision, in the sidecar by the click hook.
		private static void MarkChosen(List<Entry> list)
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
				string chosen = "";
				try
				{
					if (File.Exists(sidecar))
					{
						foreach (string raw in File.ReadAllLines(sidecar))
						{
							if (raw.StartsWith("chosen=", StringComparison.Ordinal))
							{
								chosen = raw.Substring(7).Trim();
							}
						}
					}
				}
				catch
				{
					// best effort
				}
				if (string.IsNullOrEmpty(chosen) && i + 1 < list.Count)
				{
					chosen = list[i + 1].ChosenName;
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

		// The newest choice-point snapshot the mod has for a scene (its file
		// names start with the scene name), or "" when it has none. Scenes
		// played before the mod was installed have no snapshot at all.
		public static string SnapshotFor(string sceneName)
		{
			try
			{
				string dir = Recorder.RunDir(Recorder.CurrentRunId(), "Auto");
				if (!Directory.Exists(dir))
				{
					return "";
				}
				string prefix = sceneName.Replace('/', '_') + "_";
				string best = "";
				DateTime bestTime = DateTime.MinValue;
				foreach (string f in Directory.GetFiles(dir, "*.dat"))
				{
					if (!Path.GetFileName(f).StartsWith(prefix, StringComparison.Ordinal))
					{
						continue;
					}
					DateTime t = File.GetLastWriteTimeUtc(f);
					if (t > bestTime)
					{
						bestTime = t;
						best = f;
					}
				}
				return best;
			}
			catch
			{
				return "";
			}
		}

		private static string TitleOf(string sceneName)
		{
			try
			{
				string t = LocalizationManager.GetTranslation(sceneName + "_Title");
				return string.IsNullOrEmpty(t) ? "" : t;
			}
			catch
			{
				return "";
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
