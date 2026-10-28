using System;
using System.Collections.Generic;
using System.IO;

namespace Doorstop
{
	// The story's scene chains, extracted offline from the Bolt chapter macros
	// (tools/bolt_edges.py) and shipped next to the DLL as scene-edges.tsv:
	//
	//   <from scene><TAB><to scene><TAB><chapter>
	//
	// It lets the history's explore mode walk forward through answers that were
	// never played.
	public static class SceneEdges
	{
		private static Dictionary<string, string> _next;
		private static bool _tried;

		private static string FilePath
		{
			get
			{
				string dir = Path.GetDirectoryName(typeof(SceneEdges).Assembly.Location);
				return Path.Combine(dir ?? ".", "scene-edges.tsv");
			}
		}

		private static void Load()
		{
			if (_tried)
			{
				return;
			}
			_tried = true;
			_next = new Dictionary<string, string>();
			try
			{
				string path = FilePath;
				if (!File.Exists(path))
				{
					ModLog.Info("SceneEdges: " + path + " missing - explore mode may not advance");
					return;
				}
				foreach (string raw in File.ReadAllLines(path))
				{
					string[] parts = raw.Split('\t');
					if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0
						&& !_next.ContainsKey(parts[0]))
					{
						_next[parts[0]] = parts[1];
					}
				}
				ModLog.Info("SceneEdges: " + _next.Count + " edge(s) loaded from "
					+ Path.GetFileName(path));
			}
			catch (Exception e)
			{
				ModLog.Error("SceneEdges: loading failed:\n" + e);
			}
		}

		// The scene that follows `scene` on its chain, or "" when unknown.
		public static string Next(string scene)
		{
			Load();
			string next;
			return _next != null && scene != null && _next.TryGetValue(scene, out next)
				? next : "";
		}
	}
}
