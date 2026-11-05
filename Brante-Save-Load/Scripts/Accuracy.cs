using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using _Scripts.Helpers;
using _Scripts.AMVCC.Models.Static;
using _Scripts.Managers;

namespace Doorstop
{
	// Dev (dev-accuracy.txt: run folders, or "all"): how well the extracted
	// tables predict the game. For every two consecutive choice points of a
	// linear run (A, then B, with B's scene list continuing A's), the state at
	// A plus what the tables say about A's answer and the scenes in between is
	// compared with the real state at B, field by field. The summary says which
	// stats, relations, statuses, objectives and key chapter parameters the
	// tables get wrong, and how often - what an estimated save built from tree
	// picks would get wrong.
	public static class Accuracy
	{
		private sealed class Tally
		{
			public int Wrong;
			public readonly List<string> Examples = new List<string>();
		}

		public static void Run(string spec)
		{
			try
			{
				List<string> runs = new List<string>();
				foreach (string w in spec.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
				{
					if (w == "all")
					{
						foreach (string d in Directory.GetDirectories(Recorder.RunsDir))
						{
							runs.Add(Path.GetFileName(d));
						}
					}
					else
					{
						runs.Add(w);
					}
				}
				Dictionary<string, Tally> tables = new Dictionary<string, Tally>();
				Dictionary<string, Tally> learned = new Dictionary<string, Tally>();
				int pairs = 0, exactTables = 0, known = 0, exactLearned = 0;
				foreach (string run in runs)
				{
					string dir = Path.Combine(Path.Combine(Recorder.RunsDir, run), "Choice");
					if (!Directory.Exists(dir))
					{
						continue;
					}
					List<string> files = new List<string>(Directory.GetFiles(dir, "*.dat"));
					files.Sort((a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
					for (int i = 0; i + 1 < files.Count; i++)
					{
						SaveLoadHelper a = Recorder.ReadHelper(files[i]);
						SaveLoadHelper b = Recorder.ReadHelper(files[i + 1]);
						string answer = History.ChosenIn(files[i]);
						Transitions.Step real = Transitions.Learn(a, answer, b);
						if (real == null)
						{
							continue;
						}
						pairs++;
						string step = real.From + " [" + answer.Replace("ButtonDescription", "") + "] -> " + real.To;
						SaveLoadHelper guess = ByTables(files[i], answer, real);
						if (guess != null && Compare(guess, b, step, tables) == 0)
						{
							exactTables++;
						}
						Transitions.Step seen = Transitions.Find(real.From, answer, real.To, run);
						if (seen != null)
						{
							known++;
							SaveLoadHelper g2 = Recorder.ReadHelper(files[i]);
							Transitions.Apply(g2, seen);
							if (Compare(g2, b, step, learned) == 0)
							{
								exactLearned++;
							}
						}
					}
				}
				ModLog.Info(Summary("tables", pairs, exactTables, tables) + "\n"
					+ Summary("learned from the other runs (" + known + " of the steps seen there)", known, exactLearned, learned));
			}
			catch (Exception e)
			{
				ModLog.Error("DEV ACCURACY failed:\n" + e);
			}
		}

		private static string Summary(string what, int steps, int exact, Dictionary<string, Tally> tally)
		{
			List<KeyValuePair<string, Tally>> sorted = new List<KeyValuePair<string, Tally>>(tally);
			sorted.Sort((a, b) => b.Value.Wrong.CompareTo(a.Value.Wrong));
			StringBuilder sb = new StringBuilder();
			sb.Append("DEV ACCURACY (" + what + "): " + steps + " step(s), " + exact + " predicted exactly; fields wrong (steps):");
			for (int i = 0; i < sorted.Count && i < 25; i++)
			{
				sb.Append("\n  " + sorted[i].Key + ": " + sorted[i].Value.Wrong + "  e.g. "
					+ string.Join("; ", sorted[i].Value.Examples.ToArray()));
			}
			return sb.ToString();
		}

		// The state at the next choice point as the tables predict it: A's
		// answer (effects, objectives, key parameters), A's own objectives, the
		// opening changes of the scenes in between.
		private static SaveLoadHelper ByTables(string pathA, string answer, Transitions.Step real)
		{
			SaveLoadHelper g = Recorder.ReadHelper(pathA);
			SceneEffects.Hero hero = SceneEffects.FromSnapshot(pathA);
			if (g == null || hero == null)
			{
				return null;
			}
			Dictionary<string, string> keys = SceneParams.FromSave(g);
			int button = SceneParams.ButtonOf(answer);
			hero.Apply(SceneEffects.For(real.From, answer));
			hero.ApplyObjectives(real.From, button);
			SceneParams.Apply(real.From, button, keys);
			hero.ApplyObjectives(real.From, 0);
			foreach (string mid in real.Between)
			{
				SceneParams.Apply(mid, 0, keys);
				hero.ApplyObjectives(mid, 0);
			}
			g.ModelParameters = hero.Stats;
			g.CharacterModel = hero.Characters;
			if (hero.Objectives != null)
			{
				Transitions.SetObjectives(g, hero.Objectives);
			}
			Transitions.SetKeys(g, keys);
			return g;
		}

		// Fields the guess gets wrong against the real state.
		private static int Compare(SaveLoadHelper guess, SaveLoadHelper b, string step, Dictionary<string, Tally> tally)
		{
			int wrong = 0;
			foreach (FieldInfo f in typeof(ModelParameters).GetFields(BindingFlags.Instance | BindingFlags.Public))
			{
				if (f.FieldType != typeof(int))
				{
					continue;
				}
				int want = (int)f.GetValue(guess.ModelParameters), got = (int)f.GetValue(b.ModelParameters);
				if (want != got)
				{
					wrong += Note(tally, "stat " + f.Name, step + " " + want + "/" + got);
				}
			}
			foreach (CharacterModel real in b.CharacterModel)
			{
				if (real == null)
				{
					continue;
				}
				CharacterModel g = guess.CharacterModel.Find(c => c != null && c.Character == real.Character);
				if (g == null)
				{
					continue;
				}
				if (g.Relation != real.Relation)
				{
					wrong += Note(tally, "relation " + real.Character, step + " " + g.Relation + "/" + real.Relation);
				}
				if (g.Status != real.Status)
				{
					wrong += Note(tally, "status " + real.Character, step + " " + g.Status + "/" + real.Status);
				}
				// IsUnlocked only says the character has been met (its card in
				// the relations window): no condition reads it, so not counted
			}
			HashSet<string> realObj = Objectives(b), guessObj = Objectives(guess);
			foreach (string o in realObj)
			{
				if (!guessObj.Contains(o))
				{
					wrong += Note(tally, "objective missed " + o, step);
				}
			}
			foreach (string o in guessObj)
			{
				if (!realObj.Contains(o))
				{
					wrong += Note(tally, "objective extra " + o, step);
				}
			}
			Dictionary<string, string> keys = SceneParams.FromSave(guess);
			foreach (KeyValuePair<string, string> kv in SceneParams.FromSave(b))
			{
				string g;
				keys.TryGetValue(kv.Key, out g);
				if ((g ?? "") != (kv.Value ?? ""))
				{
					wrong += Note(tally, "key " + kv.Key, step + " " + g + "/" + kv.Value);
				}
			}
			return wrong;
		}

		// Dev: the fields two states differ in, as one line.
		public static string Diff(SaveLoadHelper guess, SaveLoadHelper real)
		{
			if (guess == null || real == null)
			{
				return "(a state is missing)";
			}
			Dictionary<string, Tally> tally = new Dictionary<string, Tally>();
			int n = Compare(guess, real, "", tally);
			List<string> parts = new List<string>();
			foreach (KeyValuePair<string, Tally> kv in tally)
			{
				parts.Add(kv.Key + " " + string.Join(",", kv.Value.Examples.ToArray()).Trim());
			}
			return n + " field(s) differ" + (n > 0 ? ": " + string.Join("; ", parts.ToArray()) : "");
		}

		private static HashSet<string> Objectives(SaveLoadHelper h)
		{
			HashSet<string> set = new HashSet<string>();
			if (h.AllGameObjectives != null && h.AllGameObjectives.Objectives != null)
			{
				foreach (ObjectiveStatuses os in h.AllGameObjectives.Objectives)
				{
					if (os != null && os.Status)
					{
						set.Add(os.Name.ToString());
					}
				}
			}
			return set;
		}

		private static int Note(Dictionary<string, Tally> tally, string field, string example)
		{
			Tally t;
			if (!tally.TryGetValue(field, out t))
			{
				t = new Tally();
				tally[field] = t;
			}
			t.Wrong++;
			if (t.Examples.Count < 3)
			{
				t.Examples.Add(example);
			}
			return 1;
		}
	}
}
