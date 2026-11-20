using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Doorstop
{
	// scene-list.tsv: every scene of the story in flow order, with what it
	// says, when it opens, what it changes, and each answer with its name,
	// text, requirements, effects, destinies and key parameters - written
	// by the mod (dev-pick's "scenelist") from the shipped tables, in the
	// game's language, with the game's own names for stats, people and
	// destinies (those only resolve with the game's assets loaded).
	//
	//   Chapter Scene Kind Title Text OpensWhen OnOpening Answer Name
	//   AnswerText Requires Changes Destinies KeyParameters
	//
	// One row for the scene (Answer empty), then one per answer.
	public static class SceneList
	{
		public static string Write()
		{
			string dir = Path.GetDirectoryName(typeof(SceneList).Assembly.Location) ?? ".";
			string path = Path.Combine(dir, "scene-list.tsv");
			List<string> order = new List<string>();
			Dictionary<string, int> chapter = new Dictionary<string, int>();
			foreach (SceneFlow.Row r in SceneFlow.Rows())
			{
				if (!chapter.ContainsKey(r.Scene))
				{
					chapter[r.Scene] = r.Chapter;
					order.Add(r.Scene);
				}
			}
			List<string> rest = SceneTexts.All().FindAll(s => !chapter.ContainsKey(s));
			rest.Sort(StringComparer.Ordinal);
			order.AddRange(rest);

			StringBuilder sb = new StringBuilder();
			sb.Append("Chapter\tScene\tKind\tTitle\tText\tOpens when\tOn opening\tAnswer\tName\tAnswer text\tRequires\tChanges\tDestinies\tKey parameters\n");
			int rows = 0;
			foreach (string scene in order)
			{
				List<KeyValuePair<string, string>> buttons = SceneTexts.Buttons(scene);
				List<SceneEffects.Effect> opening = SceneEffects.SceneChanges(scene);
				string title = SceneTexts.Title(scene) ?? "";
				string text = Pages(scene);
				if (buttons.Count == 0 && opening.Count == 0 && title.Length == 0 && text.Length == 0)
				{
					continue;
				}
				int ch;
				chapter.TryGetValue(scene, out ch);
				string kind = buttons.Count > 0 ? "choice" : opening.Count > 0 ? "event" : "page";
				List<string> keys = SceneParams.Changes(scene, 0);
				keys.AddRange(SceneParams.Changes(scene, -1));
				Row(sb, ch > 0 ? ch.ToString() : "", scene, kind, title, text,
					StatPanel.NeedsText(StatPanel.GateOf(scene)),
					Changes(opening.FindAll(e => e.Kind != 'E')), "", "", "", "", "",
					Destinies(opening), string.Join(", ", keys.ToArray()));
				rows++;
				foreach (KeyValuePair<string, string> b in buttons)
				{
					int n = SceneParams.ButtonOf(b.Key);
					List<SceneEffects.Effect> all = SceneEffects.Shown(scene, b.Key);
					Row(sb, "", "", "", "", "", "", "", n.ToString(),
						SceneEffects.NobleParticle(SceneTexts.Name(scene, b.Key) ?? "").Trim(),
						SceneEffects.NobleParticle(b.Value ?? "").Trim(),
						StatPanel.NeedsText(all),
						Changes(all.FindAll(e => !e.IsRequirement && e.Kind != 'E')),
						Destinies(all),
						string.Join(", ", SceneParams.Changes(scene, n).ToArray()));
					rows++;
				}
			}
			File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
			ModLog.Info("SceneList: " + rows + " row(s), " + order.Count + " scene(s) -> " + path);
			return path;
		}

		private static void Row(StringBuilder sb, params string[] cells)
		{
			for (int i = 0; i < cells.Length; i++)
			{
				sb.Append(Clean(cells[i])).Append(i < cells.Length - 1 ? '\t' : '\n');
			}
		}

		private static string Clean(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				return "";
			}
			s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "");
			return s.Replace("\t", " ").Replace("\r", "").Replace("\n", " / ").Trim();
		}

		// The scene's text pages, in order ("<scene>_Text_N", or under its
		// title key's prefix), joined by " | ".
		private static string Pages(string scene)
		{
			List<string> prefixes = new List<string> { scene };
			string key = SceneEffects.TitleKey(scene);
			if (!string.IsNullOrEmpty(key) && key.EndsWith("_Title", StringComparison.Ordinal))
			{
				prefixes.Add(key.Substring(0, key.Length - "_Title".Length));
			}
			foreach (string prefix in prefixes)
			{
				List<string> pages = new List<string>();
				int misses = 0;
				for (int i = 1; i <= 150 && misses < 8; i++)
				{
					string k = prefix + "_Text_" + i;
					string t = Loc.Game(k, "");
					if (t == k || t.Trim().Length == 0 || t.IndexOf("need translation", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						misses++;
						continue;
					}
					misses = 0;
					pages.Add(SceneEffects.NobleParticle(t.Trim().Replace("<username>", "[name]")));
				}
				if (pages.Count > 0)
				{
					return string.Join(" | ", pages.ToArray());
				}
			}
			return "";
		}

		private static string Changes(List<SceneEffects.Effect> effects)
		{
			List<string> parts = new List<string>();
			foreach (SceneEffects.Effect e in effects)
			{
				UnityEngine.Sprite icon;
				string label = SceneEffects.Label(e, out icon);
				switch (e.Kind)
				{
					case 'P':
					case 'R':
						parts.Add(label + " " + (e.Value > 0 ? "+" : "") + e.Value);
						break;
					case 'S':
						parts.Add(label);
						break;
					case 'U':
						parts.Add("Meets " + label);
						break;
					case 'C':
						parts.Add("(skills converted)");
						break;
				}
			}
			return string.Join(", ", parts.ToArray());
		}

		private static string Destinies(List<SceneEffects.Effect> effects)
		{
			List<string> names = new List<string>();
			foreach (SceneEffects.Effect e in effects)
			{
				if (e.Kind == 'E')
				{
					UnityEngine.Sprite icon;
					names.Add(SceneEffects.Label(e, out icon));
				}
			}
			return string.Join(", ", names.ToArray());
		}
	}
}
