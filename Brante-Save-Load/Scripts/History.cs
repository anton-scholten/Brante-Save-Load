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
		public sealed class Entry
		{
			public int Index;
			public string SceneName;
			public bool IsShowed;
			public string Title;
			public bool HasSnapshot;
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
				e.HasSnapshot = HasSnapshotFor(s.SceneName);
				list.Add(e);
			}
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
				e.HasSnapshot = HasSnapshotFor(s.SceneName);
				list.Add(e);
			}
			return list;
		}

		// The mod's own choice-point snapshots for the current main save,
		// by scene name (file names start with the scene).
		private static bool HasSnapshotFor(string sceneName)
		{
			try
			{
				string dir = Recorder.RunDir(Recorder.CurrentRunId(), "Auto");
				if (!Directory.Exists(dir))
				{
					return false;
				}
				string prefix = sceneName.Replace('/', '_') + "_";
				foreach (string f in Directory.GetFiles(dir, "*.dat"))
				{
					if (Path.GetFileName(f).StartsWith(prefix, StringComparison.Ordinal))
					{
						return true;
					}
				}
			}
			catch
			{
				// history is best effort
			}
			return false;
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
