using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using _Scripts.Helpers;

namespace Doorstop
{
	// Key chapter parameters (Post, TriggerBlock, PeaceTree, ...) changed by
	// scenes and answers, extracted by tools/scene_params.py. The chapter flow
	// branches on them; the tree uses this to simulate the picked path before
	// a fast-forward.
	public static class SceneParams
	{
		private sealed class Change
		{
			public int Button;
			public string Name;
			public string Value;
		}

		private static Dictionary<string, List<Change>> _byScene;
		private static bool _tried;

		private static string FilePath
		{
			get
			{
				string dir = Path.GetDirectoryName(typeof(SceneParams).Assembly.Location);
				return Path.Combine(dir ?? ".", "scene-params.tsv");
			}
		}

		private static void Load()
		{
			if (_tried)
			{
				return;
			}
			_tried = true;
			_byScene = new Dictionary<string, List<Change>>();
			try
			{
				string path = FilePath;
				if (!File.Exists(path))
				{
					ModLog.Info("SceneParams: " + path + " missing - key parameters not simulated");
					return;
				}
				foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
				{
					string[] p = raw.Split('\t');
					if (p.Length < 4 || p[0].Length == 0)
					{
						continue;
					}
					int button;
					if (!int.TryParse(p[1], out button))
					{
						continue;
					}
					List<Change> list;
					if (!_byScene.TryGetValue(p[0], out list))
					{
						list = new List<Change>();
						_byScene[p[0]] = list;
					}
					list.Add(new Change { Button = button, Name = p[2], Value = p[3] });
				}
				ModLog.Info("SceneParams: " + _byScene.Count + " scene(s) with key parameter changes");
			}
			catch (Exception e)
			{
				ModLog.Error("SceneParams: loading failed:\n" + e);
			}
		}

		// "ButtonDescription3" -> 3; -1 when the name has no number.
		public static int ButtonOf(string buttonName)
		{
			if (string.IsNullOrEmpty(buttonName))
			{
				return -1;
			}
			int i = buttonName.Length;
			while (i > 0 && char.IsDigit(buttonName[i - 1]))
			{
				i--;
			}
			int n;
			return i < buttonName.Length
				&& int.TryParse(buttonName.Substring(i), out n) ? n : -1;
		}

		// The key parameters a save was recorded with.
		public static Dictionary<string, string> FromSave(SaveLoadHelper helper)
		{
			Dictionary<string, string> values = new Dictionary<string, string>();
			if (helper != null && helper.ChapterParameters != null)
			{
				foreach (_Scripts.KeyParams.KeyChapterParameterModel p in helper.ChapterParameters)
				{
					if (p != null && !string.IsNullOrEmpty(p.Name))
					{
						values[p.Name] = p.Value ?? "";
					}
				}
			}
			return values;
		}

		// Applies the changes of one event: 0 = the scene opening, N = answer N.
		public static void Apply(string scene, int button, Dictionary<string, string> values)
		{
			Load();
			List<Change> list;
			if (scene == null || values == null || _byScene == null
				|| !_byScene.TryGetValue(scene, out list))
			{
				return;
			}
			foreach (Change c in list)
			{
				// -1: a changer on a button every answer passes (the text
				// pages' Next, each answer's continue): it applies with any
				// answer
				if (c.Button == button || (c.Button == -1 && button > 0))
				{
					values[c.Name] = c.Value;
				}
			}
		}

		// Simulates the picked answers and the scenes they open, starting from
		// the start save's key parameters. True when every next scene is still
		// reached under the resulting labels; when it is not, `bad` names the
		// scene whose branch the answers left.
		public static bool Simulate(List<string> order, Dictionary<string, string> picks,
			string startPath, out string bad)
		{
			bad = "";
			Load();
			if (_byScene == null || _byScene.Count == 0 || order == null || order.Count < 2)
			{
				return true;
			}
			Dictionary<string, string> values;
			try
			{
				SaveLoadHelper h = File.Exists(startPath) ? Recorder.ReadHelper(startPath) : null;
				values = FromSave(h);
			}
			catch
			{
				return true;
			}
			if (values.Count == 0)
			{
				return true;
			}
			for (int i = 0; i < order.Count; i++)
			{
				string pick;
				if (picks != null && picks.TryGetValue(order[i], out pick))
				{
					Apply(order[i], ButtonOf(pick), values);
				}
				if (i + 1 >= order.Count)
				{
					break;
				}
				string next = order[i + 1];
				if (!SceneFlow.SceneFits(next, values))
				{
					bad = next;
					return false;
				}
				// The next scene's own OnEnable changes apply once it opens.
				Apply(next, 0, values);
			}
			return true;
		}
	}
}
