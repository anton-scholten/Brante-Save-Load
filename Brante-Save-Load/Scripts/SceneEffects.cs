using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using _Scripts.AMVCC.Models.Static;

namespace Doorstop
{
	// What every answer does to the hero, extracted offline from the scene
	// files' ParameterButtonChanger components (tools/scene_effects.py) and
	// shipped as scene-effects.tsv:
	//
	//   <scene> <button index> <P|R|S> <object name> <value>
	//
	// P = stat change (a Parameter asset), R = relation change and S = status
	// change (a Character asset; the value is a CharacterStatus). The assets
	// are looked up by name at runtime for their icon and localised name.
	public static class SceneEffects
	{
		public sealed class Effect
		{
			public char Kind;
			public string Name;
			public int Value;
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
					list.Add(new Effect { Kind = p[2][0], Name = p[3], Value = value });
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

		// Display name and icon for an effect, in the game's language.
		public static string Label(Effect e, out Sprite icon)
		{
			icon = null;
			if (e.Kind == 'P')
			{
				Parameter p = ParameterAsset(e.Name);
				if (p != null)
				{
					icon = p.Icon;
					return Translated(e.Name, p.ParameterName.ToString());
				}
				return Translated(e.Name, e.Name);
			}
			Character c = CharacterAsset(e.Name);
			string person = e.Name;
			string key = e.Name;
			if (c != null)
			{
				icon = c.TooltipPortret;
				key = c.Name.ToString();
				person = Translated(key, e.Name);
			}
			if (e.Kind == 'S')
			{
				string status = Enum.GetName(typeof(CharacterStatus), e.Value) ?? e.Value.ToString();
				return person + ": " + Translated(key + "." + status, Translated(status, status));
			}
			return person;
		}

		// The game's translation of `key`, else of `fallbackKey`, else that key.
		private static string Translated(string key, string fallbackKey)
		{
			foreach (string k in new[] { key, fallbackKey })
			{
				try
				{
					string t = I2.Loc.LocalizationManager.GetTranslation(k);
					if (!string.IsNullOrEmpty(t) && t.Trim().Length > 1)
					{
						return t;
					}
				}
				catch
				{
					// fall through to the next key
				}
			}
			return fallbackKey;
		}
	}
}
