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
	}
}
