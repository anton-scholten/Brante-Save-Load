using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using _Scripts.AMVCC.Models.Static;
using _Scripts.AMVCC.Views.Windows.Destiny;
using _Scripts.Helpers;
using _Scripts.Managers;

namespace Doorstop
{
	// What every answer does to the hero and what it requires, extracted
	// offline from the scene files' ParameterButtonChanger components
	// (tools/scene_effects.py) and shipped as scene-effects.tsv:
	//
	//   <scene> <button index> <kind> <object name> <value> [<op> <group>]
	//
	// Effects: P = stat change (a Parameter asset), R = relation change and
	// S = status change (a Character asset; the value is a CharacterStatus).
	// Requirements: Q = stat, W = relation (op = the game's Operations),
	// T = status (op 1 = "not"), O = objective (op 1 = "not"); group "and"
	// for the ones that must all hold, "or" for the group of which one must.
	// The assets are looked up by name at runtime for their icon, localised
	// name, and - for requirements - the hero's current value.
	public static class SceneEffects
	{
		public sealed class Effect
		{
			public char Kind;
			public string Name;
			public int Value;
			public int Op;
			public bool Or;
			public bool IsRequirement
			{
				get { return Kind == 'Q' || Kind == 'W' || Kind == 'T' || Kind == 'O'; }
			}
		}

		private static Dictionary<string, Dictionary<int, List<Effect>>> _effects;
		private static Dictionary<string, Parameter> _parameters;
		private static Dictionary<string, Character> _characters;

		private static void Load()
		{
			if (_effects != null)
			{
				return;
			}
			_effects = new Dictionary<string, Dictionary<int, List<Effect>>>();
			try
			{
				string dir = Path.GetDirectoryName(typeof(SceneEffects).Assembly.Location);
				string path = Path.Combine(dir ?? ".", "scene-effects.tsv");
				if (!File.Exists(path))
				{
					ModLog.Info("SceneEffects: " + path + " missing - no answer effects");
					return;
				}
				int n = 0;
				foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
				{
					string[] p = raw.Split('\t');
					int button, value;
					if (p.Length < 5 || p[2].Length != 1 || !int.TryParse(p[1], out button)
						|| !int.TryParse(p[4], out value))
					{
						continue;
					}
					Dictionary<int, List<Effect>> scene;
					if (!_effects.TryGetValue(p[0], out scene))
					{
						scene = new Dictionary<int, List<Effect>>();
						_effects[p[0]] = scene;
					}
					List<Effect> list;
					if (!scene.TryGetValue(button, out list))
					{
						list = new List<Effect>();
						scene[button] = list;
					}
					Effect fx = new Effect { Kind = p[2][0], Name = p[3], Value = value };
					if (p.Length >= 7)
					{
						int.TryParse(p[5], out fx.Op);
						fx.Or = p[6] == "or";
					}
					list.Add(fx);
					n++;
				}
				ModLog.Info("SceneEffects: " + n + " effect(s) for " + _effects.Count + " scene(s)");
			}
			catch (Exception e)
			{
				ModLog.Error("SceneEffects: loading failed:\n" + e);
			}
		}

		// The effects of one answer; `choiceName` is its localisation name
		// ("ButtonDescription3" is button 3). Empty when nothing is known.
		public static List<Effect> For(string scene, string choiceName)
		{
			Load();
			int button = ButtonIndex(choiceName);
			Dictionary<int, List<Effect>> byButton;
			List<Effect> list;
			if (scene != null && button > 0 && _effects.TryGetValue(scene, out byButton)
				&& byButton.TryGetValue(button, out list))
			{
				return list;
			}
			return new List<Effect>();
		}

		public static int ButtonIndex(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return 0;
			}
			int end = name.Length;
			int start = end;
			while (start > 0 && char.IsDigit(name[start - 1]))
			{
				start--;
			}
			int n;
			return start < end && int.TryParse(name.Substring(start), out n) ? n : 0;
		}

		// ------------------------------------------------------------------
		// The hero along a path: stats and characters as they would be at a
		// scene, to tell which answers would be open there.
		// ------------------------------------------------------------------

		// A copy of the hero's stats and characters. Effects are applied and
		// requirements checked with the game's own ParametersManager methods
		// (pure: no events), the stats by briefly swapping this copy in.
		public sealed class Hero
		{
			public ModelParameters Stats;
			public List<CharacterModel> Characters;
			public bool Live;        // the hero right now (objectives can be checked)
			// the story objectives set, as a save recorded them (null: not known;
			// carried on past a changed answer, since answers do not set them)
			public HashSet<_Scripts.AMVCC.Views.Windows.ObjectiveEnum> Objectives;

			// Both models are flat classes of numbers and enums, so a field
			// copy is a full copy (and far cheaper than serialising).
			public Hero Clone()
			{
				Hero h = new Hero();
				h.Stats = (ModelParameters)ShallowCopy(Stats);
				h.Characters = new List<CharacterModel>(Characters.Count);
				foreach (CharacterModel c in Characters)
				{
					h.Characters.Add(c == null ? null : (CharacterModel)ShallowCopy(c));
				}
				h.Live = false;
				h.Objectives = Objectives == null ? null
					: new HashSet<_Scripts.AMVCC.Views.Windows.ObjectiveEnum>(Objectives);
				return h;
			}

			// The answer's stat, relation and status changes.
			public void Apply(List<Effect> effects)
			{
				ParametersManager pm = ParametersManager.Instance;
				if (pm == null)
				{
					return;
				}
				ModelParameters real = pm.Parameters;
				try
				{
					pm.Parameters = Stats;
					foreach (Effect e in effects)
					{
						try
						{
							if (e.Kind == 'P')
							{
								Parameter p = ParameterAsset(e.Name);
								if (p != null)
								{
									pm.SetParameterValue(p, e.Value);
								}
							}
							else if (e.Kind == 'R' || e.Kind == 'S')
							{
								Character c = CharacterAsset(e.Name);
								if (c == null)
								{
									continue;
								}
								if (e.Kind == 'R')
								{
									pm.SetCharacterRelation(c, e.Value, Characters);
								}
								else
								{
									pm.SetParameterValue(c, (CharacterStatus)e.Value, Characters);
								}
							}
						}
						catch (Exception)
						{
							// a character missing from this hero: skip that change
						}
					}
				}
				finally
				{
					pm.Parameters = real;
				}
			}

			// The objectives the scene or an answer earns (SceneObjectives;
			// button 0 is the scene itself, N is answer ButtonDescriptionN).
			// The game unlocks every entry of its list at once, so a set keeps
			// this idempotent; a hero whose objectives are unknown starts from
			// what is unlocked right now rather than from nothing.
			public void ApplyObjectives(string scene, int button)
			{
				List<int> ids = SceneObjectives.For(scene, button);
				if (ids.Count == 0)
				{
					return;
				}
				if (Objectives == null)
				{
					Objectives = new HashSet<_Scripts.AMVCC.Views.Windows.ObjectiveEnum>();
					KeyChapterParametersController kc = KeyChapterParametersController.Initiate;
					if (kc != null && kc.AllGameObjectives != null)
					{
						foreach (Objective o in kc.AllGameObjectives)
						{
							if (o != null && o.Unlocked)
							{
								Objectives.Add(o.Name);
							}
						}
					}
				}
				foreach (int id in ids)
				{
					Objectives.Add((_Scripts.AMVCC.Views.Windows.ObjectiveEnum)id);
				}
			}

			// A stat's value for this hero, by the game's own lookup.
			public int Value(Parameter p)
			{
				ParametersManager pm = ParametersManager.Instance;
				if (pm == null || p == null)
				{
					return 0;
				}
				ModelParameters real = pm.Parameters;
				try
				{
					pm.Parameters = Stats;
					return pm.GetParameterValue(p);
				}
				catch (Exception)
				{
					return 0;
				}
				finally
				{
					pm.Parameters = real;
				}
			}

			// Whether the answer is open: every "and" requirement holds, and one
			// of the "or" group if there is one. Unknown parts count as met.
			public bool Allows(List<Effect> effects)
			{
				bool anyOr = false, orMet = false;
				foreach (Effect r in effects)
				{
					if (!r.IsRequirement)
					{
						continue;
					}
					bool? met = Meets(r);
					if (r.Or)
					{
						anyOr = true;
						orMet |= met != false;
					}
					else if (met == false)
					{
						return false;
					}
				}
				return !anyOr || orMet;
			}

			public bool? Meets(Effect r)
			{
				ParametersManager pm = ParametersManager.Instance;
				if (pm == null)
				{
					return null;
				}
				try
				{
					switch (r.Kind)
					{
						case 'Q':
						{
							Parameter p = ParameterAsset(r.Name);
							if (p == null)
							{
								return null;
							}
							ModelParameters real = pm.Parameters;
							try
							{
								pm.Parameters = Stats;
								return pm.CompareParametersValue(p.ParameterName, pm.GetParameterValue(p),
									(Assets._Scripts.AMVCC.Views.Operations)r.Op, r.Value);
							}
							finally
							{
								pm.Parameters = real;
							}
						}
						case 'W':
						{
							Character c = CharacterAsset(r.Name);
							return c == null ? (bool?)null : pm.CompareCharacterRelation(c, r.Value,
								(Assets._Scripts.AMVCC.Views.Operations)r.Op, Characters);
						}
						case 'T':
						{
							Character c = CharacterAsset(r.Name);
							return c == null ? (bool?)null
								: pm.CompareCharacterStatus(c, (CharacterStatus)r.Value, r.Op == 1, Characters);
						}
						default:
						{
							// objectives are unlocked by the story, not by answers:
							// the hero as he is now, or as a save recorded him
							Objective o = ObjectiveAsset(r.Name);
							if (o == null)
							{
								return null;
							}
							bool set;
							if (Live)
							{
								set = o.Unlocked;
							}
							else if (Objectives != null && o.Name != _Scripts.AMVCC.Views.Windows.ObjectiveEnum.None)
							{
								set = Objectives.Contains(o.Name);
							}
							else
							{
								return null;
							}
							return r.Op == 1 ? !set : set;
						}
					}
				}
				catch (Exception)
				{
					return null;
				}
			}
		}

		private static readonly FieldInfo[] StatFields =
			typeof(ModelParameters).GetFields(BindingFlags.Instance | BindingFlags.Public);

		// How far apart two heroes are: stat by stat, relation by relation,
		// and a changed fate counted heavier. Used to tell which of a scene's
		// snapshots belongs to the path being shown.
		public static int Distance(Hero a, Hero b)
		{
			if (a == null || b == null || a.Stats == null || b.Stats == null)
			{
				return int.MaxValue;
			}
			int d = 0;
			foreach (FieldInfo f in StatFields)
			{
				if (f.FieldType == typeof(int))
				{
					d += Math.Abs((int)f.GetValue(a.Stats) - (int)f.GetValue(b.Stats));
				}
			}
			if (a.Characters != null && b.Characters != null)
			{
				foreach (CharacterModel ca in a.Characters)
				{
					CharacterModel cb = ca == null ? null : b.Characters.Find(x => x != null && x.Character == ca.Character);
					if (cb != null)
					{
						d += Math.Abs(ca.Relation - cb.Relation) + (ca.Status != cb.Status ? 5 : 0);
					}
				}
			}
			return d;
		}

		private static readonly Dictionary<string, Hero> _snapshotHeroes = new Dictionary<string, Hero>();

		// The hero as a mod snapshot recorded him (cached; snapshot files never
		// change), or null.
		public static Hero FromSnapshot(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			Hero h;
			if (!_snapshotHeroes.TryGetValue(path, out h))
			{
				try
				{
					SaveLoadHelper helper = Recorder.ReadHelper(path);
					h = new Hero { Stats = helper.ModelParameters, Characters = helper.CharacterModel };
					if (helper.AllGameObjectives != null && helper.AllGameObjectives.Objectives != null)
					{
						h.Objectives = new HashSet<_Scripts.AMVCC.Views.Windows.ObjectiveEnum>();
						foreach (ObjectiveStatuses os in helper.AllGameObjectives.Objectives)
						{
							if (os != null && os.Status)
							{
								h.Objectives.Add(os.Name);
							}
						}
					}
				}
				catch (Exception)
				{
					h = null;
				}
				_snapshotHeroes[path] = h;
			}
			return h != null && h.Stats != null && h.Characters != null ? h.Clone() : null;
		}

		// The hero right now, or null.
		public static Hero Now()
		{
			ParametersManager pm = ParametersManager.Instance;
			CharacterParametersSerializeHelper csh = CharacterParametersSerializeHelper.Initiate;
			if (pm == null || csh == null || pm.Parameters == null || csh.Characters == null)
			{
				return null;
			}
			Hero h = new Hero { Stats = pm.Parameters, Characters = csh.Characters }.Clone();
			h.Live = true;
			ObjectiveAsset("");   // fills the cache
			h.Objectives = new HashSet<_Scripts.AMVCC.Views.Windows.ObjectiveEnum>();
			foreach (Objective o in _objectives.Values)
			{
				if (o != null && o.Unlocked && o.Name != _Scripts.AMVCC.Views.Windows.ObjectiveEnum.None)
				{
					h.Objectives.Add(o.Name);
				}
			}
			return h;
		}

		private static readonly MethodInfo MemberwiseCloneMethod =
			typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

		private static object ShallowCopy(object value)
		{
			return value == null ? null : MemberwiseCloneMethod.Invoke(value, null);
		}

		public static Objective ObjectiveAsset(string name)
		{
			if (_objectives == null)
			{
				_objectives = new Dictionary<string, Objective>();
				foreach (Objective o in Resources.FindObjectsOfTypeAll<Objective>())
				{
					if (o != null && !_objectives.ContainsKey(o.name))
					{
						_objectives[o.name] = o;
					}
				}
			}
			Objective found;
			return _objectives.TryGetValue(name, out found) && found != null ? found : null;
		}

		private static Dictionary<string, Objective> _objectives;

		// "≥ 4", the way the game compares (ParametersManager.Compare).
		// The ">=" / ">" stat requirements of the gate a story track ends on
		// (scene names starting with `prefix`, e.g. "053." - the rebel's
		// chapter 4; trigger scenes, numbered 100 and up, left out): its
		// latest scene with an answer that sets Death to 4 (the rebel's
		// 053.080.010_SecretMeet, before 053.090.070_Revolution), else its
		// last scene (the judge's 054.080.090_Court). Debug runs steer by it.
		public static Dictionary<string, int> ThresholdsIn(string prefix)
		{
			Load();
			Dictionary<string, int> need = new Dictionary<string, int>();
			if (_effects == null)
			{
				return need;
			}
			string last = null, deadly = null;
			foreach (KeyValuePair<string, Dictionary<int, List<Effect>>> s in _effects)
			{
				string name = s.Key;
				string[] parts = name.Split('.', '_');
				int part;
				if (!name.StartsWith(prefix, StringComparison.Ordinal) || parts.Length < 3
					|| !int.TryParse(parts[1], out part) || part >= 100)
				{
					continue;
				}
				if (last == null || string.CompareOrdinal(name, last) > 0)
				{
					last = name;
				}
				bool kills = false;
				foreach (List<Effect> fx in s.Value.Values)
				{
					kills |= fx.Exists(e => e.Kind == 'P' && e.Name == "Death" && e.Value >= 4);
				}
				if (kills && (deadly == null || string.CompareOrdinal(name, deadly) > 0))
				{
					deadly = name;
				}
			}
			last = deadly ?? last;
			foreach (KeyValuePair<string, Dictionary<int, List<Effect>>> scene in _effects)
			{
				if (scene.Key != last)
				{
					continue;
				}
				foreach (List<Effect> fx in scene.Value.Values)
				{
					foreach (Effect e in fx)
					{
						int v = e.Op == 0 ? e.Value + 1 : e.Value;
						int had;
						if (e.Kind == 'Q' && (e.Op == 0 || e.Op == 3) && (!need.TryGetValue(e.Name, out had) || v > had))
						{
							need[e.Name] = v;
						}
					}
				}
			}
			return need;
		}

		private static string OpText(int op, int value)
		{
			// ASCII only: the game's font has no ≥/≤/≠ glyphs.
			string[] ops = { ">", "<", "=", ">=", "<=", "!=" };
			return (op >= 0 && op < ops.Length ? ops[op] : "?") + " " + value;
		}


		// The game's "<el>" placeholder: the noble particle once the family
		// holds the noble sword, nothing before (ParameterButtonChanger.Start).
		public static string NobleParticle(string text)
		{
			if (string.IsNullOrEmpty(text) || text.IndexOf("<el>", StringComparison.Ordinal) < 0)
			{
				return text;
			}
			string el = "";
			try
			{
				KeyChapterParametersController k = KeyChapterParametersController.Initiate;
				if (k != null && k.GetNobleSwordStatus())
				{
					el = I2.Loc.LocalizationManager.GetTranslation("SwordNobleEl") ?? "";
				}
			}
			catch
			{
				el = "";
			}
			return text.Replace("<el>", el).Replace("  ", " ");
		}

		// Every stat the game has (with an icon: one asset is a stray copy).
		public static List<Parameter> AllStats()
		{
			ParameterAsset("");   // fills the cache
			List<Parameter> list = new List<Parameter>();
			foreach (Parameter p in _parameters.Values)
			{
				if (p != null && p.Icon != null)
				{
					list.Add(p);
				}
			}
			return list;
		}


		public static Parameter ParameterAsset(string name)
		{
			if (_parameters == null)
			{
				_parameters = new Dictionary<string, Parameter>();
				foreach (Parameter p in Resources.FindObjectsOfTypeAll<Parameter>())
				{
					if (p != null && !_parameters.ContainsKey(p.name))
					{
						_parameters[p.name] = p;
					}
				}
			}
			Parameter found;
			return _parameters.TryGetValue(name, out found) && found != null ? found : null;
		}

		public static Character CharacterAsset(string name)
		{
			if (_characters == null)
			{
				_characters = new Dictionary<string, Character>();
				foreach (Character c in Resources.FindObjectsOfTypeAll<Character>())
				{
					if (c != null && !_characters.ContainsKey(c.name))
					{
						_characters[c.name] = c;
					}
				}
			}
			Character found;
			return _characters.TryGetValue(name, out found) && found != null ? found : null;
		}

		// An effect's or requirement's text and icon, in the game's language:
		// "Perception", "Perception >= 4", "Nathan Brante", "Nathan Brante:
		// GRATITUDE", "not <objective>". Whether a requirement is met is
		// Hero.Meets's business.
		public static string Label(Effect e, out Sprite icon)
		{
			icon = null;
			string not = e.Op == 1 && (e.Kind == 'T' || e.Kind == 'O') ? Loc.T(Loc.K.NotWord) + " " : "";
			switch (e.Kind)
			{
				case 'P':
				case 'Q':
				{
					Parameter p = ParameterAsset(e.Name);
					icon = p != null ? p.Icon : null;
					string name = Translated(e.Name, p != null ? p.ParameterName.ToString() : e.Name);
					return e.Kind == 'Q' ? name + " " + OpText(e.Op, e.Value) : name;
				}
				case 'O':
				{
					Objective o = ObjectiveAsset(e.Name);
					if (o == null)
					{
						return not + e.Name;
					}
					icon = o.Icon;
					string key = o.Name == _Scripts.AMVCC.Views.Windows.ObjectiveEnum.None ? o.name : o.Name.ToString();
					return not + Translated(key + ".Title", e.Name);
				}
				default:
				{
					Character c = CharacterAsset(e.Name);
					string key = c != null ? c.Name.ToString() : e.Name;
					icon = c != null ? c.TooltipPortret : null;
					string person = Translated(key, e.Name);
					if (e.Kind == 'W')
					{
						return person + " " + OpText(e.Op, e.Value);
					}
					if (e.Kind == 'S' || e.Kind == 'T')
					{
						string status = Enum.GetName(typeof(CharacterStatus), e.Value) ?? e.Value.ToString();
						return person + ": " + not + Translated(key + "." + status, Translated(status, status));
					}
					return person;
				}
			}
		}

		// The game's translation of `key`, else of `fallbackKey`, else that key
		// (a one-character translation is a placeholder such as "—").
		private static string Translated(string key, string fallbackKey)
		{
			foreach (string k in new[] { key, fallbackKey })
			{
				string t = Loc.Game(k, "");
				if (t.Trim().Length > 1)
				{
					return NobleParticle(t);
				}
			}
			return fallbackKey;
		}
	}
}
