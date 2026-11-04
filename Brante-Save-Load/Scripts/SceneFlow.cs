using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using _Scripts.AMVCC.Models.Static;
using _Scripts.AMVCC.Views.Windows.Destiny;
using _Scripts.Helpers;
using _Scripts.Managers;

namespace Doorstop
{
	// The story's scene order for chapters 1-5, extracted offline from the Bolt
	// chapter macros (tools/bolt_story.py) and shipped as scene-flow.tsv:
	//
	//   <chapter> <order> <scene> <turn> <season> <labels> <conditions>
	//
	// The game walks a chapter's macro again after every scene and loads the
	// first scene whose time slot (turn, season) has come, that has not been
	// shown, and whose switches (key chapter parameters such as Post) and
	// branch conditions (objectives, parameters, character status) pass on
	// the way. This class answers "what comes after scene X" the same way:
	// later time slots of the same chapter, filtered by the switch labels and
	// conditions evaluated against the live game. A condition that cannot be
	// evaluated (an object not loaded) counts as unknown: the scene is kept and
	// marked as possible rather than certain.
	public static class SceneFlow
	{
		public enum Tri { False, True, Unknown }

		public sealed class Row
		{
			public int Chapter;
			public int Order;
			public string Scene;
			public int Turn;       // 0 when not gated on time; the first turn of a range
			public int TurnMax;    // the last turn it can open in (= Turn unless a range)
			public bool Timed;     // gated on the turn at all
			public int Season;     // 0 when not gated on time
			public List<List<KeyValuePair<string, string>>> Labels;
			public string Condition;
		}

		// One scene ahead, and whether its conditions hold right now.
		public sealed class Ahead
		{
			public Row Row;
			public Tri Condition;
			// The scene's time gate matches the live turn/season (or it is a
			// trigger scene): it can be the very next scene, before later slots.
			public bool Immediate;
		}

		private static List<Row> _rows;
		private static readonly Dictionary<Row, Node> _parsed = new Dictionary<Row, Node>();
		private static Dictionary<string, Row> _byScene;
		private static bool _tried;

		private static string FilePath
		{
			get
			{
				string dir = Path.GetDirectoryName(typeof(SceneFlow).Assembly.Location);
				return Path.Combine(dir ?? ".", "scene-flow.tsv");
			}
		}

		private static void Load()
		{
			if (_tried)
			{
				return;
			}
			_tried = true;
			_rows = new List<Row>();
			_byScene = new Dictionary<string, Row>();
			try
			{
				string path = FilePath;
				if (!File.Exists(path))
				{
					ModLog.Info("SceneFlow: " + path + " missing - no upcoming scenes");
					return;
				}
				foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
				{
					string[] p = raw.Split('\t');
					if (p.Length < 7 || p[2].Length == 0)
					{
						continue;
					}
					Row r = new Row();
					r.Chapter = ToInt(p[0]);
					r.Order = ToInt(p[1]);
					r.Scene = p[2];
					ParseTurn(p[3], r);
					r.Season = ToInt(p[4]);
					r.Labels = ParseLabels(p[5]);
					r.Condition = p[6];
					_rows.Add(r);
					if (!_byScene.ContainsKey(r.Scene))
					{
						_byScene[r.Scene] = r;
					}
				}
				ModLog.Info("SceneFlow: " + _byScene.Count + " scene(s), " + _rows.Count
					+ " flow row(s) loaded from " + Path.GetFileName(path));
			}
			catch (Exception e)
			{
				ModLog.Error("SceneFlow: loading failed:\n" + e);
			}
		}

		private static int ToInt(string s)
		{
			int v;
			return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
		}

		// "Post=Judge;Disclosure=No|Post=Rebel" -> alternatives of label sets.
		// "N" (the gate's Turn == N), or the inclusive range "N-M" of a
		// trigger gate (Turn >= N && Turn < M+1), open at either end
		// ("N-", "-M"); empty when the gate does not look at the turn.
		private static void ParseTurn(string text, Row r)
		{
			text = (text ?? "").Trim();
			r.Timed = text.Length > 0;
			int dash = text.IndexOf('-');
			if (dash < 0)
			{
				r.Turn = ToInt(text);
				r.TurnMax = r.Turn;
				return;
			}
			string lo = text.Substring(0, dash), hi = text.Substring(dash + 1);
			r.Turn = lo.Length > 0 ? ToInt(lo) : 0;
			r.TurnMax = hi.Length > 0 ? ToInt(hi) : int.MaxValue;
		}

		// Whether the row's time gate is open at this turn and season.
		private static bool InSlot(Row r, int turn, int season)
		{
			if (!r.Timed)
			{
				return true;
			}
			return turn >= r.Turn && turn <= r.TurnMax && (r.Season == 0 || r.Season == season);
		}

		private static List<List<KeyValuePair<string, string>>> ParseLabels(string text)
		{
			List<List<KeyValuePair<string, string>>> alts = new List<List<KeyValuePair<string, string>>>();
			foreach (string alt in text.Split('|'))
			{
				List<KeyValuePair<string, string>> set = new List<KeyValuePair<string, string>>();
				foreach (string lab in alt.Split(';'))
				{
					int eq = lab.IndexOf('=');
					if (eq > 0)
					{
						set.Add(new KeyValuePair<string, string>(lab.Substring(0, eq), lab.Substring(eq + 1)));
					}
				}
				alts.Add(set);
			}
			return alts;
		}

		public static Row Find(string scene)
		{
			Load();
			Row r;
			return scene != null && _byScene != null && _byScene.TryGetValue(scene, out r) ? r : null;
		}

		// The chapter a scene belongs to, 0 when it is not in the flow.
		public static int ChapterOf(string scene)
		{
			Row r = Find(scene);
			return r != null ? r.Chapter : 0;
		}

		// The scenes that can come next, in the order the game would try them:
		// every scene of the current chapter that has not been shown yet, in
		// chapter flow order, and with `laterChapters` every scene of the
		// chapters after it. The game re-enters the chapter flow at the top
		// after every scene, so a triggered scene whose flow position is
		// before the scene on screen is still a candidate; filtering by
		// "after the current scene" would lose those. `shown` (may be null)
		// are left out.
		//
		// In the current chapter the run's key parameters and the conditions
		// are checked against the live game. Later chapters are too far ahead
		// for today's state to say much: their conditional scenes count as
		// undecided, and a track picked by a key parameter the run has not set
		// yet (Post, in chapter 4) shows only what every option has in common.
		public static List<Ahead> After(string from, ICollection<string> shown, bool laterChapters)
		{
			return After(from, shown, laterChapters, null);
		}

		// As above, evaluating the scene conditions with `hero`'s stats,
		// characters and unlocked objectives (the state after the answer on
		// screen), by briefly swapping them into the game's managers.
		public static List<Ahead> After(string from, ICollection<string> shown, bool laterChapters,
			SceneEffects.Hero hero)
		{
			return WithHero(hero, delegate { return AfterCore(from, shown, laterChapters); });
		}

		// Runs `body` with `hero`'s stats, characters and objectives swapped
		// into the game's managers, and puts the live ones back afterwards.
		private static List<Ahead> WithHero(SceneEffects.Hero hero, Func<List<Ahead>> body)
		{
			ParametersManager pm = ParametersManager.Instance;
			CharacterParametersSerializeHelper csh = CharacterParametersSerializeHelper.Initiate;
			KeyChapterParametersController kc = KeyChapterParametersController.Initiate;
			if (hero == null || hero.Stats == null || pm == null || csh == null)
			{
				return body();
			}
			ModelParameters stats = pm.Parameters;
			List<CharacterModel> chars = csh.Characters;
			bool[] unlocked = null;
			try
			{
				pm.Parameters = hero.Stats;
				if (hero.Characters != null)
				{
					csh.Characters = hero.Characters;
				}
				if (hero.Objectives != null && kc != null && kc.AllGameObjectives != null)
				{
					unlocked = new bool[kc.AllGameObjectives.Count];
					for (int i = 0; i < kc.AllGameObjectives.Count; i++)
					{
						Objective o = kc.AllGameObjectives[i];
						unlocked[i] = o.Unlocked;
						o.Unlocked = hero.Objectives.Contains(o.Name);
					}
				}
				return body();
			}
			finally
			{
				pm.Parameters = stats;
				csh.Characters = chars;
				if (unlocked != null)
				{
					for (int i = 0; i < kc.AllGameObjectives.Count && i < unlocked.Length; i++)
					{
						kc.AllGameObjectives[i].Unlocked = unlocked[i];
					}
				}
			}
		}

		private static List<Ahead> AfterCore(string from, ICollection<string> shown, bool laterChapters)
		{
			Row start = Find(from);
			if (start == null)
			{
				return new List<Ahead>();
			}
			return AfterCore(start.Chapter, from, shown, laterChapters);
		}

		private static List<Ahead> AfterCore(int chapter, string from, ICollection<string> shown, bool laterChapters)
		{
			return AfterCore(chapter, from, shown, laterChapters, new Context());
		}

		private static List<Ahead> AfterCore(int chapter, string from, ICollection<string> shown, bool laterChapters,
			Context ctx)
		{
			List<Ahead> result = new List<Ahead>();
			Row start = new Row { Chapter = chapter, Scene = from ?? "" };
			GameManager gm = GameManager.Instance;
			int turn = gm != null ? gm.Turn : 0;
			int season = gm != null ? gm.Season : 0;
			List<Row> later = new List<Row>();
			foreach (Row r in _rows)
			{
				if (r.Chapter == start.Chapter)
				{
					if (r.Scene == from && shown == null)
					{
						continue;   // no history to exclude it: not a next scene
					}
				}
				else if (!(laterChapters && r.Chapter > start.Chapter))
				{
					continue;
				}
				if (shown != null && shown.Contains(r.Scene))
				{
					continue;
				}
				if (r.Chapter == start.Chapter && r.Timed && turn > 0 && r.TurnMax < turn)
				{
					continue;   // its turns are over: the game will not open it any more
				}
				later.Add(r);
			}
			later.Sort(delegate(Row a, Row b)
			{
				return a.Chapter != b.Chapter ? a.Chapter.CompareTo(b.Chapter) : Compare(a, b);
			});
			HashSet<string> seenScene = new HashSet<string>();
			foreach (Row r in later)
			{
				bool future = r.Chapter != start.Chapter;
				if (!LabelsMatch(r, ctx, future))
				{
					continue;
				}
				Tri cond = future
					? (string.IsNullOrEmpty(r.Condition) ? Tri.True : Tri.Unknown)
					: Evaluate(r, ctx);
				if (cond == Tri.False)
				{
					continue;
				}
				// The TSV has one row per flow occurrence (a scene can be
				// reached under several switch label sets); show it once.
				if (!seenScene.Add(r.Scene))
				{
					continue;
				}
				Ahead a = new Ahead();
				a.Row = r;
				a.Condition = cond;
				a.Immediate = !future
					&& InSlot(r, turn, season);
				result.Add(a);
			}
			// Immediate scenes first (flow order), then the rest of the
			// chapter (later time slots), then later chapters. This is the
			// order the game can actually take them in.
			result.Sort(delegate(Ahead a, Ahead b)
			{
				int ra = a.Row.Chapter != start.Chapter ? 2 : (a.Immediate ? 0 : 1);
				int rb = b.Row.Chapter != start.Chapter ? 2 : (b.Immediate ? 0 : 1);
				if (ra != rb)
				{
					return ra.CompareTo(rb);
				}
				if (a.Row.Chapter != b.Row.Chapter)
				{
					return a.Row.Chapter.CompareTo(b.Row.Chapter);
				}
				int ta = Math.Max(a.Row.Turn, turn), tb = Math.Max(b.Row.Turn, turn);
				if (ra == 1 && (ta != tb || a.Row.Season != b.Row.Season))
				{
					// later time slots of this chapter: the game moves on to
					// the earliest one, whatever the flow order
					return ta != tb ? ta.CompareTo(tb) : a.Row.Season.CompareTo(b.Row.Season);
				}
				return Compare(a.Row, b.Row);
			});
			return result;
		}

		// The scene AfterCore would list first, for a step of the simulation:
		// the candidates are put in that order (immediate ones in flow order,
		// then the earliest later slot) before any condition is evaluated,
		// and the first that can open wins - the conditions of the rest of
		// the chapter are never evaluated. An empty list when none can.
		private static List<Ahead> FirstCore(int chapter, ICollection<string> shown, Context ctx)
		{
			GameManager gm = GameManager.Instance;
			int turn = gm != null ? gm.Turn : 0;
			int season = gm != null ? gm.Season : 0;
			List<Row> now = new List<Row>();
			List<Row> later = new List<Row>();
			foreach (Row r in ChapterRows(chapter))
			{
				if (shown.Contains(r.Scene) || (r.Timed && turn > 0 && r.TurnMax < turn))
				{
					continue;
				}
				(InSlot(r, turn, season) ? now : later).Add(r);
			}
			later.Sort(delegate(Row a, Row b)
			{
				int ta = Math.Max(a.Turn, turn), tb = Math.Max(b.Turn, turn);
				if (ta != tb)
				{
					return ta.CompareTo(tb);
				}
				return a.Season != b.Season ? a.Season.CompareTo(b.Season) : Compare(a, b);
			});
			List<Ahead> result = new List<Ahead>();
			foreach (List<Row> group in new[] { now, later })
			{
				foreach (Row r in group)
				{
					if (!LabelsMatch(r, ctx, false))
					{
						continue;
					}
					Tri cond = Evaluate(r, ctx);
					if (cond == Tri.False)
					{
						continue;
					}
					result.Add(new Ahead { Row = r, Condition = cond, Immediate = group == now });
					return result;
				}
			}
			return result;
		}

		// A chapter's rows in flow order (kept: the TSV does not change).
		private static readonly Dictionary<int, List<Row>> _chapterRows = new Dictionary<int, List<Row>>();

		private static List<Row> ChapterRows(int chapter)
		{
			List<Row> rows;
			if (!_chapterRows.TryGetValue(chapter, out rows))
			{
				rows = _rows.FindAll(r => r.Chapter == chapter);
				rows.Sort(Compare);
				_chapterRows[chapter] = rows;
			}
			return rows;
		}

		// One step of a forward simulation: the scenes of `chapter` that can
		// come next for a simulated game - its hero (stats, characters,
		// objectives), key chapter parameters, scenes shown so far, turn and
		// season - immediate ones first. The simulated state is swapped into
		// the game's managers only while the conditions are evaluated (the
		// evaluator reads them there) and always put back.
		public static List<Ahead> Step(int chapter, SceneEffects.Hero hero, Dictionary<string, string> keys,
			List<ShowedScenes> shownList, int turn, int season)
		{
			Load();
			GameManager gm = GameManager.Instance;
			KeyChapterParametersController kc = KeyChapterParametersController.Initiate;
			if (_rows == null || gm == null || kc == null || hero == null)
			{
				return new List<Ahead>();
			}
			HashSet<string> shown = new HashSet<string>();
			foreach (ShowedScenes sc in shownList)
			{
				if (sc != null && sc.IsShowed)
				{
					shown.Add(sc.SceneName);
				}
			}
			List<ShowedScenes> liveList = gm.ShowedScenesList;
			int liveTurn = gm.Turn, liveSeason = gm.Season;
			List<_Scripts.KeyParams.KeyChapterParameterModel> liveKeys = kc.AllKeyParameters;
			try
			{
				gm.ShowedScenesList = shownList;
				gm.Turn = turn;
				gm.Season = season;
				List<_Scripts.KeyParams.KeyChapterParameterModel> simKeys = new List<_Scripts.KeyParams.KeyChapterParameterModel>();
				foreach (KeyValuePair<string, string> kv in keys)
				{
					simKeys.Add(new _Scripts.KeyParams.KeyChapterParameterModel { Name = kv.Key, Value = kv.Value });
				}
				kc.AllKeyParameters = simKeys;
				// The steps of one simulation run in one frame: the scene's
				// objects and singletons are the same for all of them, and
				// looking them up (FindObjectsOfTypeAll) is most of a step's
				// cost. Key parameters change every step and are read again.
				int frame = UnityEngine.Time.frameCount;
				if (_stepCtx == null || _stepFrame != frame)
				{
					_stepCtx = new Context();
					_stepFrame = frame;
				}
				_stepCtx.ResetKeys();
				Context ctx = _stepCtx;
				return WithHero(hero, delegate { return FirstCore(chapter, shown, ctx); });
			}
			finally
			{
				gm.ShowedScenesList = liveList;
				gm.Turn = liveTurn;
				gm.Season = liveSeason;
				kc.AllKeyParameters = liveKeys;
			}
		}

		private static Context _stepCtx;
		private static int _stepFrame = -1;

		// The earliest time slot (turn, season) of a chapter's flow, for a
		// simulation entering the chapter.
		public static void FirstSlot(int chapter, out int turn, out int season)
		{
			Load();
			turn = int.MaxValue;
			season = 0;
			foreach (Row r in _rows)
			{
				if (r.Chapter == chapter && r.Timed && r.Turn > 0 && r.Turn < turn)
				{
					turn = r.Turn;
					season = r.Season;
				}
			}
			if (turn == int.MaxValue)
			{
				turn = 1;
				season = 1;
			}
		}

		// Whether `scene` can be reached with these key chapter parameters: its
		// switch label alternatives include one every label of which matches the
		// values (a parameter not in `values` is unknown and does not veto).
		public static bool SceneFits(string scene, IDictionary<string, string> values)
		{
			Load();
			if (_rows == null || values == null)
			{
				return true;
			}
			bool found = false;
			foreach (Row r in _rows)
			{
				if (r.Scene != scene)
				{
					continue;
				}
				found = true;
				if (r.Labels.Count == 0)
				{
					return true;
				}
				foreach (List<KeyValuePair<string, string>> set in r.Labels)
				{
					bool ok = true;
					foreach (KeyValuePair<string, string> lab in set)
					{
						string v;
						if (values.TryGetValue(lab.Key, out v) && !string.IsNullOrEmpty(v)
							&& !string.Equals(v, lab.Value, StringComparison.OrdinalIgnoreCase))
						{
							ok = false;
							break;
						}
					}
					if (ok)
					{
						return true;
					}
				}
			}
			return !found;
		}

		// The first scene after `scene`, or "" when none is known.
		public static string Next(string scene)
		{
			List<Ahead> after = After(scene, null, true);
			return after.Count > 0 ? after[0].Row.Scene : "";
		}

		// Dev (dev-book.txt): parses and evaluates every row against the live
		// game and logs how many conditions hold, fail or stay undecided.
		public static void DevCheck()
		{
			Load();
			Context ctx = new Context();
			int t = 0, f = 0, u = 0, bad = 0;
			foreach (Row r in _rows)
			{
				if (string.IsNullOrEmpty(r.Condition))
				{
					continue;
				}
				try
				{
					Node n;
					if (!_parsed.TryGetValue(r, out n))
					{
						n = new Parser(r.Condition).Parse();
						_parsed[r] = n;
					}
					Tri v = ToTri(n.Eval(ctx));
					if (v == Tri.True) t++;
					else if (v == Tri.False) f++;
					else u++;
				}
				catch (Exception e)
				{
					if (bad++ < 5)
					{
						ModLog.Info("SceneFlow: dev check, " + r.Scene + ": " + e.GetType().Name + ": " + e.Message);
					}
				}
			}
			ModLog.Info("SceneFlow: key parameters Post='" + ctx.KeyParameter("Post")
				+ "' TriggerBlock='" + ctx.KeyParameter("TriggerBlock") + "' PeaceTree='"
				+ ctx.KeyParameter("PeaceTree") + "'");
			ModLog.Info("SceneFlow: dev check over " + _rows.Count + " scene(s): conditions true "
				+ t + ", false " + f + ", undecided " + u + ", failed " + bad);
		}

		// Flow position first: the chapter macro's order is the order the game
		// tries the scene gates, and a trigger scene may carry no time slot.
		private static int Compare(Row a, Row b)
		{
			if (a.Order != b.Order)
			{
				return a.Order.CompareTo(b.Order);
			}
			if (a.Turn != b.Turn)
			{
				return a.Turn.CompareTo(b.Turn);
			}
			if (a.Season != b.Season)
			{
				return a.Season.CompareTo(b.Season);
			}
			return 0;
		}

		// Any alternative whose switches all agree with the key chapter
		// parameters; a parameter the game has not set yet agrees with anything.
		// For a later chapter (`strict`) the live values mean nothing yet - a
		// new game already holds Post=Inquisitor long before the post is
		// chosen - so there the scene must be reachable whatever value the
		// parameter will get.
		private static bool LabelsMatch(Row r, Context ctx, bool strict)
		{
			if (r.Labels.Count == 0)
			{
				return true;
			}
			if (strict)
			{
				foreach (List<KeyValuePair<string, string>> set in r.Labels)
				{
					foreach (KeyValuePair<string, string> lab in set)
					{
						if (!Covers(r, lab.Key))
						{
							return false;
						}
					}
				}
				return true;
			}
			foreach (List<KeyValuePair<string, string>> set in r.Labels)
			{
				bool ok = true;
				foreach (KeyValuePair<string, string> lab in set)
				{
					string value = ctx.KeyParameter(lab.Key);
					if (!string.IsNullOrEmpty(value)
						&& !string.Equals(value, lab.Value, StringComparison.OrdinalIgnoreCase))
					{
						ok = false;
						break;
					}
				}
				if (ok)
				{
					return true;
				}
			}
			return false;
		}

		// Whether a scene is reachable under every value its chapter switches
		// `key` to: some alternative ignores the switch, or the alternatives
		// name every value the chapter uses.
		private static readonly Dictionary<KeyValuePair<Row, string>, bool> _covers =
			new Dictionary<KeyValuePair<Row, string>, bool>();

		private static bool Covers(Row r, string key)
		{
			KeyValuePair<Row, string> ck = new KeyValuePair<Row, string>(r, key);
			bool known;
			if (_covers.TryGetValue(ck, out known))
			{
				return known;
			}
			known = CoversUncached(r, key);
			_covers[ck] = known;
			return known;
		}

		private static bool CoversUncached(Row r, string key)
		{
			HashSet<string> mine = new HashSet<string>();
			foreach (List<KeyValuePair<string, string>> set in r.Labels)
			{
				bool has = false;
				foreach (KeyValuePair<string, string> lab in set)
				{
					if (lab.Key == key)
					{
						has = true;
						mine.Add(lab.Value);
					}
				}
				if (!has)
				{
					return true;
				}
			}
			foreach (Row other in _rows)
			{
				if (other.Chapter != r.Chapter)
				{
					continue;
				}
				foreach (List<KeyValuePair<string, string>> set in other.Labels)
				{
					foreach (KeyValuePair<string, string> lab in set)
					{
						if (lab.Key == key && !mine.Contains(lab.Value))
						{
							return false;
						}
					}
				}
			}
			return true;
		}

		private static Tri Evaluate(Row r, Context ctx)
		{
			if (string.IsNullOrEmpty(r.Condition))
			{
				return Tri.True;
			}
			try
			{
				Node n;
				if (!_parsed.TryGetValue(r, out n))
				{
					n = new Parser(r.Condition).Parse();
					_parsed[r] = n;
				}
				return ToTri(n.Eval(ctx));
			}
			catch (Exception e)
			{
				if (!ctx.Reported)
				{
					ctx.Reported = true;
					ModLog.Info("SceneFlow: condition of " + r.Scene + " not evaluated: " + e.Message);
				}
				return Tri.Unknown;
			}
		}

		private static Tri ToTri(object v)
		{
			if (v is bool)
			{
				return (bool)v ? Tri.True : Tri.False;
			}
			return Tri.Unknown;
		}

		// ------------------------------------------------------------------
		// Expression evaluation (the grammar is documented in bolt_story.py)
		// ------------------------------------------------------------------

		// Stands for "could not be evaluated"; propagates through operators.
		private static readonly object Unknown = new object();

		// Per-query lookups: singletons, Unity objects by type and name.
		private sealed class Context
		{
			public bool Reported;
			private readonly Dictionary<string, object> _instances = new Dictionary<string, object>();
			private readonly Dictionary<string, Dictionary<string, UnityEngine.Object>> _objects =
				new Dictionary<string, Dictionary<string, UnityEngine.Object>>();
			private readonly Dictionary<string, string> _keys = new Dictionary<string, string>();

			public void ResetKeys()
			{
				_keys.Clear();
			}

			public string KeyParameter(string name)
			{
				string v;
				if (_keys.TryGetValue(name, out v))
				{
					return v;
				}
				v = "";
				try
				{
					KeyChapterParametersController k = KeyChapterParametersController.Initiate;
					if (k == null)
					{
						k = UnityEngine.Object.FindObjectOfType<KeyChapterParametersController>();
					}
					if (k != null)
					{
						v = k.GetKeyChapterParameterValue(name) ?? "";
					}
				}
				catch
				{
					v = "";
				}
				_keys[name] = v;
				return v;
			}

			public object Instance(string typeName)
			{
				object inst;
				if (_instances.TryGetValue(typeName, out inst))
				{
					return inst;
				}
				inst = null;
				Type t = TypeByName(typeName);
				if (t != null)
				{
					foreach (string prop in new[] { "Instance", "Initiate" })
					{
						PropertyInfo pi = t.GetProperty(prop, BindingFlags.Public | BindingFlags.Static);
						if (pi != null)
						{
							inst = pi.GetValue(null, null);
							if (inst is UnityEngine.Object && (UnityEngine.Object)inst == null)
							{
								inst = null;   // died with its scene
							}
							if (inst != null)
							{
								break;
							}
						}
					}
					if (inst == null && typeof(UnityEngine.Object).IsAssignableFrom(t))
					{
						UnityEngine.Object found = UnityEngine.Object.FindObjectOfType(t);
						inst = found != null ? found : null;
					}
				}
				_instances[typeName] = inst;
				return inst;
			}

			public object UnityObject(string typeName, string name)
			{
				Dictionary<string, UnityEngine.Object> byName;
				if (!_objects.TryGetValue(typeName, out byName))
				{
					byName = new Dictionary<string, UnityEngine.Object>();
					Type t = TypeByName(typeName);
					if (t != null && typeof(UnityEngine.Object).IsAssignableFrom(t))
					{
						foreach (UnityEngine.Object o in UnityEngine.Resources.FindObjectsOfTypeAll(t))
						{
							if (o != null && !byName.ContainsKey(o.name))
							{
								byName[o.name] = o;
							}
						}
					}
					_objects[typeName] = byName;
				}
				UnityEngine.Object obj;
				return byName.TryGetValue(name, out obj) ? (object)obj : Unknown;
			}

			// The character list the game passes along with a character.
			public object Characters()
			{
				CharacterParametersSerializeHelper h = CharacterParametersSerializeHelper.Initiate;
				if (h == null)
				{
					h = UnityEngine.Object.FindObjectOfType<CharacterParametersSerializeHelper>();
				}
				return h != null ? (object)h.Characters : null;
			}
		}

		private static Dictionary<string, Type> _types;

		private static Type TypeByName(string name)
		{
			if (_types == null)
			{
				_types = new Dictionary<string, Type>();
				foreach (Type t in typeof(GameManager).Assembly.GetTypes())
				{
					if (!_types.ContainsKey(t.Name))
					{
						_types[t.Name] = t;
					}
				}
			}
			Type found;
			return _types.TryGetValue(name, out found) ? found : null;
		}

		private abstract class Node
		{
			internal abstract object Eval(Context ctx);
		}

		private sealed class Const : Node
		{
			public object Value;
			internal override object Eval(Context ctx) { return Value; }
		}

		private sealed class ObjRef : Node
		{
			public string Type;
			public string Name;
			internal override object Eval(Context ctx) { return ctx.UnityObject(Type, Name); }
		}

		private sealed class Not : Node
		{
			public Node Inner;
			internal override object Eval(Context ctx)
			{
				object v = Inner.Eval(ctx);
				return v is bool ? (object)!(bool)v : Unknown;
			}
		}

		private sealed class Logic : Node
		{
			public bool IsAnd;
			public List<Node> Parts = new List<Node>();
			internal override object Eval(Context ctx)
			{
				bool unknown = false;
				foreach (Node n in Parts)
				{
					object v = n.Eval(ctx);
					if (!(v is bool))
					{
						unknown = true;
					}
					else if ((bool)v != IsAnd)
					{
						return !IsAnd;   // a false in an and, a true in an or
					}
				}
				return unknown ? Unknown : (object)IsAnd;
			}
		}

		private sealed class Cmp : Node
		{
			public string Op;
			public Node A;
			public Node B;
			internal override object Eval(Context ctx)
			{
				object a = A.Eval(ctx);
				object b = B.Eval(ctx);
				if (a == Unknown || b == Unknown || a == null || b == null)
				{
					return Unknown;
				}
				double x, y;
				if (Number(a, out x) && Number(b, out y))
				{
					switch (Op)
					{
						case "==": return x == y;
						case "!=": return x != y;
						case "<": return x < y;
						case "<=": return x <= y;
						case ">": return x > y;
						default: return x >= y;
					}
				}
				bool eq = string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
				if (Op == "==") return eq;
				if (Op == "!=") return !eq;
				return Unknown;
			}

			private static bool Number(object v, out double d)
			{
				d = 0;
				if (v is bool || v is string || v is Enum)
				{
					return false;
				}
				try
				{
					d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
					return true;
				}
				catch
				{
					return false;
				}
			}
		}

		// Type.Member or Type.Method(args) on the game's live singleton.
		private sealed class Call : Node
		{
			public string Type;
			public string Member;
			public List<Node> Args;    // null for a member read

			internal override object Eval(Context ctx)
			{
				object target = ctx.Instance(Type);
				if (target == null)
				{
					return Unknown;
				}
				Type t = target.GetType();
				if (Args == null)
				{
					PropertyInfo pi = t.GetProperty(Member, BindingFlags.Public | BindingFlags.Instance);
					if (pi != null)
					{
						return pi.GetValue(target, null);
					}
					FieldInfo fi = t.GetField(Member, BindingFlags.Public | BindingFlags.Instance);
					return fi != null ? fi.GetValue(target) : Unknown;
				}
				object[] given = new object[Args.Count];
				for (int i = 0; i < Args.Count; i++)
				{
					given[i] = Args[i].Eval(ctx);
					if (given[i] == Unknown)
					{
						return Unknown;
					}
				}
				foreach (MethodInfo m in MethodsNamed(t, Member))
				{
					object[] call = Bind(m.GetParameters(), given, ctx);
					if (call != null)
					{
						object result = m.Invoke(target, call);
						return result is Enum ? result.ToString() : result;
					}
				}
				return Unknown;
			}

			// Arguments for this overload, or null when they do not fit. A
			// trailing character list is the game's own, as the macros pass it.
			private static object[] Bind(ParameterInfo[] ps, object[] given, Context ctx)
			{
				if (ps.Length < given.Length)
				{
					return null;
				}
				object[] call = new object[ps.Length];
				for (int i = 0; i < ps.Length; i++)
				{
					Type pt = ps[i].ParameterType;
					if (i < given.Length)
					{
						object v = given[i];
						if (v != null && pt.IsInstanceOfType(v))
						{
							call[i] = v;
						}
						else if (pt == typeof(string) && v is string)
						{
							call[i] = v;
						}
						else
						{
							return null;
						}
					}
					else if (pt == typeof(List<CharacterModel>))
					{
						call[i] = ctx.Characters();
						if (call[i] == null)
						{
							return null;
						}
					}
					else
					{
						return null;
					}
				}
				return call;
			}
		}

		private static readonly Dictionary<KeyValuePair<Type, string>, MethodInfo[]> _methods =
			new Dictionary<KeyValuePair<Type, string>, MethodInfo[]>();

		// A type's public instance methods of one name (reflection is slow,
		// and a refresh evaluates hundreds of calls).
		private static MethodInfo[] MethodsNamed(Type t, string name)
		{
			KeyValuePair<Type, string> key = new KeyValuePair<Type, string>(t, name);
			MethodInfo[] found;
			if (!_methods.TryGetValue(key, out found))
			{
				List<MethodInfo> list = new List<MethodInfo>();
				foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
				{
					if (m.Name == name)
					{
						list.Add(m);
					}
				}
				found = list.ToArray();
				_methods[key] = found;
			}
			return found;
		}

		private sealed class Parser
		{
			private readonly string _s;
			private int _i;

			public Parser(string s)
			{
				_s = s;
			}

			public Node Parse()
			{
				Node n = Or();
				Skip();
				if (_i < _s.Length)
				{
					throw new FormatException("unexpected '" + _s.Substring(_i, Math.Min(12, _s.Length - _i)) + "'");
				}
				return n;
			}

			private void Skip()
			{
				while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
				{
					_i++;
				}
			}

			private bool Eat(string tok)
			{
				Skip();
				if (string.CompareOrdinal(_s, _i, tok, 0, tok.Length) == 0)
				{
					_i += tok.Length;
					return true;
				}
				return false;
			}

			private Node Or()
			{
				Node first = And();
				if (!Peek("||"))
				{
					return first;
				}
				Logic l = new Logic { IsAnd = false };
				l.Parts.Add(first);
				while (Eat("||"))
				{
					l.Parts.Add(And());
				}
				return l;
			}

			private Node And()
			{
				Node first = Unary();
				if (!Peek("&&"))
				{
					return first;
				}
				Logic l = new Logic { IsAnd = true };
				l.Parts.Add(first);
				while (Eat("&&"))
				{
					l.Parts.Add(Unary());
				}
				return l;
			}

			private bool Peek(string tok)
			{
				Skip();
				return string.CompareOrdinal(_s, _i, tok, 0, tok.Length) == 0;
			}

			private Node Unary()
			{
				Skip();
				if (_i < _s.Length && _s[_i] == '!' && !Peek("!="))
				{
					_i++;
					return new Not { Inner = Unary() };
				}
				Node a = Atom();
				foreach (string op in new[] { "==", "!=", "<=", ">=", "<", ">" })
				{
					if (Eat(op))
					{
						return new Cmp { Op = op, A = a, B = Atom() };
					}
				}
				return a;
			}

			private Node Atom()
			{
				Skip();
				if (_i >= _s.Length)
				{
					throw new FormatException("unexpected end");
				}
				char c = _s[_i];
				if (c == '(')
				{
					_i++;
					Node n = Or();
					if (!Eat(")"))
					{
						throw new FormatException("missing ')'");
					}
					return n;
				}
				if (c == '"')
				{
					return new Const { Value = QuotedString('"') };
				}
				if (c == '@')
				{
					_i++;
					string type = Word();
					if (_i >= _s.Length || _s[_i] != '\'')
					{
						throw new FormatException("bad object reference");
					}
					return new ObjRef { Type = type, Name = QuotedString('\'') };
				}
				if (c == '?')
				{
					_i++;
					return new Const { Value = Unknown };
				}
				if (char.IsDigit(c) || c == '-')
				{
					int start = _i++;
					while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
					{
						_i++;
					}
					return new Const { Value = double.Parse(_s.Substring(start, _i - start), CultureInfo.InvariantCulture) };
				}
				string word = Word();
				if (word.Length == 0)
				{
					throw new FormatException("unexpected '" + c + "'");
				}
				if (word == "true" || word == "false")
				{
					return new Const { Value = word == "true" };
				}
				if (_i < _s.Length && _s[_i] == '.')
				{
					_i++;
					string member = Word();
					List<Node> args = null;
					if (Eat("("))
					{
						args = new List<Node>();
						if (!Eat(")"))
						{
							do
							{
								args.Add(Or());
							}
							while (Eat(","));
							if (!Eat(")"))
							{
								throw new FormatException("missing ')' after arguments");
							}
						}
					}
					return new Call { Type = word, Member = member, Args = args };
				}
				return new Const { Value = word };   // an enum value, compared by name
			}

			private string Word()
			{
				int start = _i;
				while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_'))
				{
					_i++;
				}
				return _s.Substring(start, _i - start);
			}

			private string QuotedString(char quote)
			{
				_i++;   // opening quote
				StringBuilder sb = new StringBuilder();
				while (_i < _s.Length && _s[_i] != quote)
				{
					if (_s[_i] == '\\' && _i + 1 < _s.Length)
					{
						_i++;
					}
					sb.Append(_s[_i++]);
				}
				_i++;   // closing quote
				return sb.ToString();
			}
		}
	}
}
