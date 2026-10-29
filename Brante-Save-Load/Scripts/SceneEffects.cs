using System;
using System.Collections.Generic;
using System.IO;
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

		private static int ButtonIndex(string name)
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

			public Hero Clone()
			{
				Hero h = new Hero();
				h.Stats = DeepCopy(Stats);
				h.Characters = DeepCopy(Characters);
				h.Live = false;
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
							// only the hero as he is now can tell
							if (!Live)
							{
								return null;
							}
							Objective o = ObjectiveAsset(r.Name);
							return o == null ? (bool?)null : (r.Op == 1 ? !o.Unlocked : o.Unlocked);
						}
					}
				}
				catch (Exception)
				{
					return null;
				}
			}
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
			return h;
		}

		private static T DeepCopy<T>(T value)
		{
			if (value == null)
			{
				return value;
			}
			System.Runtime.Serialization.Formatters.Binary.BinaryFormatter f =
				new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
			using (MemoryStream ms = new MemoryStream())
			{
				f.Serialize(ms, value);
				ms.Position = 0;
				return (T)f.Deserialize(ms);
			}
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
