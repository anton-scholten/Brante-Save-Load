using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using _Scripts.Helpers;
using _Scripts.AMVCC.Models.Static;
using _Scripts.Managers;

namespace Doorstop
{
	// What the game really did between two choice points, learned from the
	// mod's own snapshots: for a scene, the answer given and the next choice
	// scene, the change of every stat and relation, the statuses, objectives
	// and key chapter parameters that changed, the scenes shown in between and
	// the time at the next scene. The extracted tables miss a lot of this
	// (world stats changed on consequence pages, chapter-start conversions,
	// flags the story scripts set, relations drifting); a recorded step has it
	// all. Quick load builds an estimated save from these steps, falling back
	// to the tables (SceneEffects, SceneObjectives, SceneParams, SceneFlow)
	// for a step nobody has played yet.
	public static class Transitions
	{
		public sealed class Step
		{
			public string From, Answer, To, Run;
			public readonly Dictionary<string, int> Stats = new Dictionary<string, int>();
			public readonly Dictionary<CharacterList, int> Relations = new Dictionary<CharacterList, int>();
			public readonly Dictionary<CharacterList, CharacterStatus> Statuses = new Dictionary<CharacterList, CharacterStatus>();
			public readonly List<CharacterList> Met = new List<CharacterList>();
			public readonly List<_Scripts.AMVCC.Views.Windows.ObjectiveEnum> Objectives = new List<_Scripts.AMVCC.Views.Windows.ObjectiveEnum>();
			public readonly Dictionary<string, string> Keys = new Dictionary<string, string>();
			public readonly List<string> Between = new List<string>();
			public int Turn, Season, Year, PreviousYear, HeroAge, Chapter;
			public string ChapterName;
		}

		private static readonly FieldInfo[] StatFields = Array.FindAll(
			typeof(ModelParameters).GetFields(BindingFlags.Instance | BindingFlags.Public), f => f.FieldType == typeof(int));

		private static Dictionary<string, List<Step>> _steps;
		private static DateTime _builtAt;

		private static string Key(string from, string answer, string to)
		{
			return from + "|" + answer + "|" + to;
		}

		// Every recorded step of every run (built once, again when a run has
		// recorded a newer choice point since).
		private static void Build()
		{
			DateTime newest = DateTime.MinValue;
			List<string> runs = new List<string>();
			if (Directory.Exists(Recorder.RunsDir))
			{
				runs.AddRange(Directory.GetDirectories(Recorder.RunsDir));
			}
			foreach (string run in runs)
			{
				string dir = Path.Combine(run, "Choice");
				if (Directory.Exists(dir))
				{
					DateTime t = Directory.GetLastWriteTimeUtc(dir);
					if (t > newest)
					{
						newest = t;
					}
				}
			}
			if (_steps != null && newest <= _builtAt)
			{
				return;
			}
			_builtAt = newest;
			_steps = new Dictionary<string, List<Step>>();
			int n = 0;
			foreach (string run in runs)
			{
				string dir = Path.Combine(run, "Choice");
				if (!Directory.Exists(dir))
				{
					continue;
				}
				List<string> files = new List<string>(Directory.GetFiles(dir, "*.dat"));
				files.Sort((a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
				SaveLoadHelper prev = null;
				string prevPath = null;
				foreach (string f in files)
				{
					SaveLoadHelper cur;
					if (Recorder.IsEstimated(f))
					{
						prev = null;   // an estimated state is not what the game did
						continue;
					}
					try
					{
						cur = Recorder.ReadHelper(f);
					}
					catch (Exception)
					{
						cur = null;
					}
					if (prev != null && cur != null)
					{
						Step s = Learn(prev, History.ChosenIn(prevPath), cur);
						if (s != null)
						{
							s.Run = Path.GetFileName(run);
							List<Step> list;
							string k = Key(s.From, s.Answer, s.To);
							if (!_steps.TryGetValue(k, out list))
							{
								list = new List<Step>();
								_steps[k] = list;
							}
							list.Add(s);
							n++;
						}
					}
					prev = cur;
					prevPath = f;
				}
			}
			ModLog.Info("Transitions: " + n + " recorded step(s), " + _steps.Count + " distinct");
		}

		// The step from choice point `a` (answered `answer`) to `b`, or null
		// when b does not continue a on one path.
		public static Step Learn(SaveLoadHelper a, string answer, SaveLoadHelper b)
		{
			if (a == null || b == null || string.IsNullOrEmpty(answer) || a.ShowedScenesList == null
				|| b.ShowedScenesList == null || a.ModelParameters == null || b.ModelParameters == null)
			{
				return null;
			}
			string from = a.LastOpenScene, to = b.LastOpenScene;
			int ia = a.ShowedScenesList.FindIndex(s => s != null && s.SceneName == from);
			int ib = b.ShowedScenesList.FindIndex(s => s != null && s.SceneName == to);
			if (ia < 0 || ib <= ia || ia >= b.ShowedScenesList.Count)
			{
				return null;
			}
			for (int k = 0; k <= ia; k++)
			{
				if (b.ShowedScenesList[k] == null || a.ShowedScenesList[k] == null
					|| b.ShowedScenesList[k].SceneName != a.ShowedScenesList[k].SceneName)
				{
					return null;
				}
			}
			Step s = new Step { From = from, Answer = answer, To = to };
			for (int k = ia + 1; k < ib; k++)
			{
				if (b.ShowedScenesList[k] != null)
				{
					s.Between.Add(b.ShowedScenesList[k].SceneName);
				}
			}
			foreach (FieldInfo f in StatFields)
			{
				int d = (int)f.GetValue(b.ModelParameters) - (int)f.GetValue(a.ModelParameters);
				if (d != 0)
				{
					s.Stats[f.Name] = d;
				}
			}
			if (a.CharacterModel != null && b.CharacterModel != null)
			{
				foreach (CharacterModel cb in b.CharacterModel)
				{
					if (cb == null)
					{
						continue;
					}
					CharacterModel ca = a.CharacterModel.Find(c => c != null && c.Character == cb.Character);
					if (ca == null)
					{
						continue;
					}
					if (cb.Relation != ca.Relation)
					{
						s.Relations[cb.Character] = cb.Relation - ca.Relation;
					}
					if (cb.Status != ca.Status)
					{
						s.Statuses[cb.Character] = cb.Status;
					}
					if (cb.IsUnlocked && !ca.IsUnlocked)
					{
						s.Met.Add(cb.Character);
					}
				}
			}
			HashSet<_Scripts.AMVCC.Views.Windows.ObjectiveEnum> had = Unlocked(a);
			foreach (_Scripts.AMVCC.Views.Windows.ObjectiveEnum o in Unlocked(b))
			{
				if (!had.Contains(o))
				{
					s.Objectives.Add(o);
				}
			}
			Dictionary<string, string> ka = SceneParams.FromSave(a);
			foreach (KeyValuePair<string, string> kv in SceneParams.FromSave(b))
			{
				string old;
				if (!ka.TryGetValue(kv.Key, out old) || old != kv.Value)
				{
					s.Keys[kv.Key] = kv.Value;
				}
			}
			s.Turn = b.Turn;
			s.Season = b.Season;
			s.Year = b.Year;
			s.PreviousYear = b.PreviousYear;
			s.HeroAge = b.HeroAge;
			s.Chapter = b.Chapter;
			s.ChapterName = b.ChapterName;
			return s;
		}

		private static HashSet<_Scripts.AMVCC.Views.Windows.ObjectiveEnum> Unlocked(SaveLoadHelper h)
		{
			HashSet<_Scripts.AMVCC.Views.Windows.ObjectiveEnum> set = new HashSet<_Scripts.AMVCC.Views.Windows.ObjectiveEnum>();
			if (h.AllGameObjectives != null && h.AllGameObjectives.Objectives != null)
			{
				foreach (ObjectiveStatuses os in h.AllGameObjectives.Objectives)
				{
					if (os != null && os.Status)
					{
						set.Add(os.Name);
					}
				}
			}
			return set;
		}

		// A recorded step for this scene, answer and next choice scene, the
		// newest one not from `excludeRun` (a test leaves the run it checks
		// out), or null.
		public static Step Find(string from, string answer, string to, string excludeRun = null)
		{
			Build();
			List<Step> list;
			if (!_steps.TryGetValue(Key(from, answer, to), out list))
			{
				return null;
			}
			for (int i = list.Count - 1; i >= 0; i--)
			{
				if (list[i].Run != excludeRun)
				{
					return list[i];
				}
			}
			return null;
		}

		// `h` (the state as `from` opened) moved on to `to` by a recorded
		// step: stats and relations change by what the game changed then,
		// statuses, objectives and key parameters take the values it set, the
		// scenes in between are shown, and the time is the next scene's.
		public static void Apply(SaveLoadHelper h, Step s)
		{
			foreach (FieldInfo f in StatFields)
			{
				int d;
				if (s.Stats.TryGetValue(f.Name, out d))
				{
					f.SetValue(h.ModelParameters, (int)f.GetValue(h.ModelParameters) + d);
				}
			}
			foreach (CharacterModel c in h.CharacterModel)
			{
				if (c == null)
				{
					continue;
				}
				int d;
				CharacterStatus st;
				if (s.Relations.TryGetValue(c.Character, out d))
				{
					c.Relation += d;
				}
				if (s.Statuses.TryGetValue(c.Character, out st))
				{
					c.Status = st;
				}
				if (s.Met.Contains(c.Character))
				{
					c.IsUnlocked = true;
				}
			}
			SetObjectives(h, s.Objectives);
			SetKeys(h, s.Keys);
			MoveOn(h, s.From, s.Between, s.To);
			h.Turn = s.Turn;
			h.Season = s.Season;
			h.Year = s.Year;
			h.PreviousYear = s.PreviousYear;
			h.HeroAge = s.HeroAge;
			h.Chapter = s.Chapter;
			h.ChapterName = s.ChapterName;
		}

		public static void SetObjectives(SaveLoadHelper h, IEnumerable<_Scripts.AMVCC.Views.Windows.ObjectiveEnum> gained)
		{
			if (h.AllGameObjectives == null || h.AllGameObjectives.Objectives == null)
			{
				return;
			}
			foreach (_Scripts.AMVCC.Views.Windows.ObjectiveEnum o in gained)
			{
				ObjectiveStatuses os = h.AllGameObjectives.Objectives.Find(x => x != null && x.Name == o);
				if (os != null)
				{
					os.Status = true;
				}
			}
		}

		public static void SetKeys(SaveLoadHelper h, Dictionary<string, string> keys)
		{
			if (h.ChapterParameters == null)
			{
				h.ChapterParameters = new List<_Scripts.KeyParams.KeyChapterParameterModel>();
			}
			foreach (KeyValuePair<string, string> kv in keys)
			{
				_Scripts.KeyParams.KeyChapterParameterModel p = h.ChapterParameters.Find(x => x != null && x.Name == kv.Key);
				if (p == null)
				{
					h.ChapterParameters.Add(new _Scripts.KeyParams.KeyChapterParameterModel { Name = kv.Key, Value = kv.Value });
				}
				else
				{
					p.Value = kv.Value;
				}
			}
		}

		// `from` is answered, the scenes in between were shown, `to` is the
		// scene on screen (opened, not yet answered) and the one to resume in.
		public static void MoveOn(SaveLoadHelper h, string from, List<string> between, string to)
		{
			Shown(h, from, true);
			foreach (string b in between)
			{
				Shown(h, b, true);
			}
			Shown(h, to, false);
			h.LastOpenScene = to;
		}

		private static void Shown(SaveLoadHelper h, string scene, bool showed)
		{
			ShowedScenes s = h.ShowedScenesList.Find(x => x != null && x.SceneName == scene);
			if (s == null)
			{
				h.ShowedScenesList.Add(new ShowedScenes { SceneName = scene, IsShowed = showed });
			}
			else
			{
				s.IsShowed = s.IsShowed || showed;
			}
		}
	}
}
