using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Doorstop
{
	// The story objectives a scene or an answer unlocks, extracted offline from
	// the scene files (tools/scene_objectives.py) and shipped as
	// scene-objectives.tsv:
	//
	//   <scene> <button> <order> <objective asset name> <ObjectiveEnum value>
	//
	// Button 0 is the scene itself (a no-answer consequence scene earns its
	// objectives when it ends); button N is answer ButtonDescriptionN. A scene
	// or answer can earn several objectives at once - the game shows one popup
	// per objective and unlocks all of them - so the loader keeps the list.
	//
	// Only KeyChapterParametersController.EarnObjective writes Objective.Unlocked
	// at runtime, and it is called from the scene consequence generators; the
	// enum value is what SaveLoadHelper stores, so no name lookup is needed.
	public static class SceneObjectives
	{
		private static Dictionary<string, Dictionary<int, List<int>>> _byScene;
		// the same, by objective asset name (what ObjectiveAsset looks up)
		private static readonly Dictionary<string, List<string>> _names = new Dictionary<string, List<string>>();

		private static void Load()
		{
			if (_byScene != null)
			{
				return;
			}
			_byScene = new Dictionary<string, Dictionary<int, List<int>>>();
			try
			{
				string dir = Path.GetDirectoryName(typeof(SceneObjectives).Assembly.Location);
				string path = Path.Combine(dir ?? ".", "scene-objectives.tsv");
				if (!File.Exists(path))
				{
					ModLog.Info("SceneObjectives: " + path + " missing - no scene objectives");
					return;
				}
				int n = 0;
				foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
				{
					string[] p = raw.Split('\t');
					int button, value;
					if (p.Length < 5 || !int.TryParse(p[1], out button)
						|| !int.TryParse(p[4], out value))
					{
						continue;
					}
					Dictionary<int, List<int>> scene;
					if (!_byScene.TryGetValue(p[0], out scene))
					{
						scene = new Dictionary<int, List<int>>();
						_byScene[p[0]] = scene;
					}
					List<int> list;
					if (!scene.TryGetValue(button, out list))
					{
						list = new List<int>();
						scene[button] = list;
					}
					if (!list.Contains(value))
					{
						list.Add(value);
						List<string> names;
						string at = p[0] + "#" + button;
						if (!_names.TryGetValue(at, out names))
						{
							names = new List<string>();
							_names[at] = names;
						}
						names.Add(p[3]);
					}
					n++;
				}
				ModLog.Info("SceneObjectives: " + n + " objective(s) for " + _byScene.Count + " scene(s)");
			}
			catch (Exception e)
			{
				ModLog.Error("SceneObjectives: loading failed:\n" + e);
			}
		}

		// The objective enum values `scene` earns with `button` (0: the scene
		// itself), empty when nothing is known.
		public static List<int> For(string scene, int button)
		{
			Load();
			Dictionary<int, List<int>> byButton;
			List<int> list;
			if (scene != null && _byScene.TryGetValue(scene, out byButton)
				&& byButton.TryGetValue(button, out list))
			{
				return list;
			}
			return Empty;
		}

		private static readonly List<int> Empty = new List<int>();

		// The objective asset names `scene` earns with `button`, in the
		// TSV's order (for display: SceneEffects.Label of an 'E' effect).
		public static List<string> Names(string scene, int button)
		{
			Load();
			List<string> list;
			return scene != null && _names.TryGetValue(scene + "#" + button, out list) ? list : NoNames;
		}

		// Every scene/answer that earns objective `value`: (scene, button).
		public static List<KeyValuePair<string, int>> Earners(int value)
		{
			Load();
			List<KeyValuePair<string, int>> all = new List<KeyValuePair<string, int>>();
			foreach (KeyValuePair<string, Dictionary<int, List<int>>> s in _byScene)
			{
				foreach (KeyValuePair<int, List<int>> b in s.Value)
				{
					if (b.Value.Contains(value))
					{
						all.Add(new KeyValuePair<string, int>(s.Key, b.Key));
					}
				}
			}
			return all;
		}

		private static readonly List<string> NoNames = new List<string>();
	}
}
