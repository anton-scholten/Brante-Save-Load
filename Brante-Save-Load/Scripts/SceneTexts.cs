using System;
using System.Collections.Generic;
using System.IO;

namespace Doorstop
{
	// The game's own scene text, extracted offline from the localisation table
	// (tools/localization.py) and shipped next to the DLL as scene-texts.tsv:
	//
	//   S<TAB>sceneId<TAB>title
	//   B<TAB>sceneId<TAB>ButtonDescriptionN<TAB>answer text
	//
	// It lists every answer of every scene, including scenes played before the
	// mod was installed (no snapshot, no sidecar). The texts themselves come
	// from the game's live localisation when it has them - that follows the
	// game's language, and it is the only complete source: the CSV the table
	// is extracted from is an older export with chapter 5 in Russian only,
	// while the game's runtime language asset has every language. The table's
	// own text is the fallback.
	public static class SceneTexts
	{
		public sealed class Scene
		{
			public string Title = "";
			public List<KeyValuePair<string, string>> Buttons =
				new List<KeyValuePair<string, string>>();
		}

		private static Dictionary<string, Scene> _scenes;
		private static bool _tried;

		private static string FilePath
		{
			get
			{
				string dir = Path.GetDirectoryName(typeof(SceneTexts).Assembly.Location);
				return Path.Combine(dir ?? ".", "scene-texts.tsv");
			}
		}

		private static void Load()
		{
			if (_tried)
			{
				return;
			}
			_tried = true;
			_scenes = new Dictionary<string, Scene>();
			try
			{
				string path = FilePath;
				if (!File.Exists(path))
				{
					ModLog.Info("SceneTexts: " + path + " missing - history shows captured scenes only");
					return;
				}
				foreach (string raw in File.ReadAllLines(path))
				{
					string[] parts = raw.Split('\t');
					if (parts.Length >= 3 && parts[0] == "S")
					{
						Scene s = Get(parts[1]);
						s.Title = Unescape(parts[2]);
					}
					else if (parts.Length >= 4 && parts[0] == "B")
					{
						Scene s = Get(parts[1]);
						s.Buttons.Add(new KeyValuePair<string, string>(
							parts[2], Unescape(parts[3])));
					}
				}
				ModLog.Info("SceneTexts: " + _scenes.Count + " scene(s) loaded from "
					+ Path.GetFileName(path));
			}
			catch (Exception e)
			{
				ModLog.Error("SceneTexts: loading failed:\n" + e);
			}
		}

		private static Scene Get(string scene)
		{
			Scene s;
			if (!_scenes.TryGetValue(scene, out s))
			{
				s = new Scene();
				_scenes[scene] = s;
			}
			return s;
		}

		public static string Title(string scene)
		{
			Load();
			string live = Loc.SceneTitle(scene);
			if (!string.IsNullOrEmpty(live))
			{
				return live;
			}
			Scene s;
			return _scenes != null && _scenes.TryGetValue(scene, out s) ? s.Title : "";
		}

		// The game's text for a key of this scene, else the table's.
		private static string Live(string scene, string key, string tableText)
		{
			string live = Loc.Game(scene + "_" + key, "");
			return string.IsNullOrEmpty(live) || live == scene + "_" + key ? tableText : live;
		}

		// The text of one answer, or "" when the table has no such key.
		public static string Text(string scene, string buttonName)
		{
			Load();
			Scene s;
			if (_scenes == null || !_scenes.TryGetValue(scene, out s) || string.IsNullOrEmpty(buttonName))
			{
				return "";
			}
			foreach (KeyValuePair<string, string> b in s.Buttons)
			{
				if (b.Key == buttonName)
				{
					return Live(scene, b.Key, b.Value);
				}
			}
			return "";
		}

		// Answers offered at a scene, in key order (ButtonDescription1 first).
		public static List<KeyValuePair<string, string>> Buttons(string scene)
		{
			Load();
			Scene s;
			if (_scenes == null || !_scenes.TryGetValue(scene, out s))
			{
				return new List<KeyValuePair<string, string>>();
			}
			List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
			foreach (KeyValuePair<string, string> b in s.Buttons)
			{
				list.Add(new KeyValuePair<string, string>(b.Key, Live(scene, b.Key, b.Value)));
			}
			// by number: ButtonDescription2 before ButtonDescription10
			list.Sort(delegate (KeyValuePair<string, string> a, KeyValuePair<string, string> b)
			{
				int c = Number(a.Key).CompareTo(Number(b.Key));
				return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
			});
			return list;
		}

		private static int Number(string key)
		{
			int end = key.Length, start = end;
			while (start > 0 && char.IsDigit(key[start - 1]))
			{
				start--;
			}
			int n;
			return start < end && int.TryParse(key.Substring(start), out n) ? n : 0;
		}

		private static string Unescape(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return "";
			}
			System.Text.StringBuilder sb = new System.Text.StringBuilder(text.Length);
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (c != '\\' || i + 1 >= text.Length)
				{
					sb.Append(c);
					continue;
				}
				char n = text[++i];
				if (n == 't')
				{
					sb.Append('\t');
				}
				else if (n == 'n')
				{
					sb.Append('\n');
				}
				else if (n == '\\')
				{
					sb.Append('\\');
				}
				else
				{
					sb.Append(n);
				}
			}
			return sb.ToString();
		}
	}
}
