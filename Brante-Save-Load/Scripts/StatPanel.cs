using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using _Scripts.AMVCC.Models.Static;
using _Scripts.AMVCC.Views.Windows;
using _Scripts.AMVCC.Views.Windows.Components;

namespace Doorstop
{
	// The tree view's stats: one column per stat category (personality,
	// family, province, ...), each stat a row in the game's own style - the
	// game's "Parameter_Horizontal" row (icon, name, value, the segment word,
	// the slider with the stat's scale marks), cloned and filled with the
	// hero's values along the picked path.
	//
	// The game's row comes with scripts that refill it from the live hero;
	// the clone is made inside an inactive holder (so none of them wakes up)
	// and those scripts are removed before it is shown.
	public static class StatPanel
	{
		private sealed class Row
		{
			public Parameter Param;
			public int Shown;           // the value on display (for the tooltip)
			public TextMeshProUGUI Value;
			public TextMeshProUGUI Descr;
			public Slider Slider;
			public Tab Tab;
			public TextMeshProUGUI Name;
			public float NameSize, ValueSize;   // before any fitting
			public Image Mark;                  // behind the row while a hovered answer changes it
			public Character Person;            // a Family/Relations row: the character (Param null)
			public Image Icon;
			public CharacterStatus Status;      // the status on display
		}

		// What the panel under the tree shows: the stats, the family or the
		// other characters (the game's Family and Relations windows), picked
		// by three tabs at its top.
		public enum Mode { Stats, Family, Relations }

		public static Mode Showing = Mode.Stats;
		private const float TabStrip = 40f;   // the tabs' height
		private static RectTransform _area, _content;
		private static SceneEffects.Hero _lastShown, _lastNow;
		private static int _lastChapter;
		private static string _peopleKey;
		private static float _statScale = 0.7f;   // the stat rows' scale, the last time they were laid out     // the characters the content was built for

		private static float _fitLimit;   // the row's drawn width, for FitText
		private static string _layoutLogged;   // the last layout written to the log

		// A long name with its value ("TOLERANCE OF FAITHS 4") is wider than the
		// row's text column, and the value would land in the next column:
		// both are shrunk to fit. Measured from the laid-out text, so the
		// row must be on screen.
		private static void FitText(Row row, float limit)
		{
			if (row.Name == null || row.Value == null || !row.Name.isActiveAndEnabled)
			{
				return;
			}
			row.Name.fontSize = row.NameSize;
			row.Value.fontSize = row.ValueSize;
			row.Name.ForceMeshUpdate();
			row.Value.ForceMeshUpdate();
			// the text column starts right of the icon (95 of the row's units)
			float avail = limit - 95f - 8f;
			float w = row.Name.textBounds.size.x + row.Value.textBounds.size.x + 10f;
			if (w > avail && w > 1f)
			{
				float f = avail / w;
				row.Name.fontSize *= f;
				row.Value.fontSize *= f;
			}
		}

		// One of the game's stat tabs (its book tab, its window's stats) and
		// the chapter the tab opens in.
		private sealed class Tab
		{
			public string Hud;            // the HUD button that opens it
			public int Chapter;
			public string[] Order;        // the window's stats, in its order ("|": a new group)
			public readonly List<int> Groups = new List<int>();   // where each group starts in Stats
			public ParameterCategory[] Takes;   // stats of these categories not in any window
			public readonly List<Parameter> Stats = new List<Parameter>();
			public TextMeshProUGUI Title;
		}

		// The game's tabs and their windows' stats (Window_Character, _Home,
		// _Empire, _Work, _Insurrection); stats no window shows join the tab of
		// their category. A tab still locked at the hero's chapter is greyed
		// out, its stats shown as the game already keeps them.
		//
		// The Destiny tab (Window_Soul) is left out: every stat it shows is
		// dead - no answer, interlude, flow gate or objective changes or reads
		// it, and 2998 saves held each at its start value (Love 3, Tolerance 8,
		// the rest 0). Dead lists those stats, so a category no tab takes can
		// never bring one back. Сontention starts with a Cyrillic С in the
		// game's own name.
		private static readonly HashSet<string> Dead = new HashSet<string>
		{
			"Volition", "Love", "Rule", "Cruelty", "Weight", "\u0421ontention", "Control",
			"GWrath", "Tolerance",
		};

		// Heir is live (Stephan or the hero), but it says nothing a reader of
		// the tree needs: the panel leaves it out.
		private static readonly HashSet<string> LeftOut = new HashSet<string> { "Heir" };

		private static bool IsDead(Parameter p)
		{
			return Dead.Contains(p.name) || Dead.Contains(p.ParameterName.ToString())
				|| LeftOut.Contains(p.name) || LeftOut.Contains(p.ParameterName.ToString());
		}

		// Where each stat of `t` goes with `rows` rows to a column: a group
		// (an occupation and the two chapter-3 skills it feeds, Willpower and
		// Deaths) starts a new column rather than be split, unless it is
		// taller than a column. Returns (column, row) per stat; `columns` is
		// how many it takes.
		private static List<Vector2> Place(Tab t, int rows, out int columns)
		{
			List<Vector2> at = new List<Vector2>(t.Stats.Count);
			int sub = 0, r = 0;
			for (int i = 0; i < t.Stats.Count; i++)
			{
				int g = t.Groups.IndexOf(i);
				if (g >= 0 && r > 0)
				{
					int end = g + 1 < t.Groups.Count ? t.Groups[g + 1] : t.Stats.Count;
					if (r + (end - i) > rows)
					{
						sub++;
						r = 0;
					}
				}
				if (r == rows)
				{
					sub++;
					r = 0;
				}
				at.Add(new Vector2(sub, r));
				r++;
			}
			columns = t.Stats.Count == 0 ? 0 : sub + 1;
			return at;
		}

		private static List<Tab> Tabs()
		{
			List<Tab> tabs = new List<Tab>
			{
				new Tab { Hud = "HUD.Character", Chapter = 1, Takes = new[] { ParameterCategory.CharacterParameter },
					// each occupation beside the two chapter-3 skills it feeds
					// (CalculateChapterParams: Diplomacy = Curiosity + Discipline,
					// Valor = Activity + Discipline, ...); Deaths under Willpower
					Order = new[] { "Will", "Death", "Curiosity", "Activity",
						"|", "Discipline", "Diplomacy", "Valor",
						"|", "Cunning", "Manipulation", "Sabotage",
						"|", "Spirituality", "Theology", "Preaching" } },
				new Tab { Hud = "HUD.Home", Chapter = 1, Takes = new[] { ParameterCategory.HomeParameter },
					Order = new[] { "Unity", "Money", "Reputation", "Heir" } },
				new Tab { Hud = "HUD.Empire", Chapter = 2, Takes = new[] { ParameterCategory.EmpireParameter },
					Order = new[] { "State", "Church", "Prosperity", "Order" } },
				new Tab { Hud = "HUD.Inquisition", Chapter = 4, Takes = new[] { ParameterCategory.WorkParameter },
					Order = new[] { "Authority", "Carier", "Protest", "Justice", "Repression", "Toleration" } },
				new Tab { Hud = "HUD.Insurrection", Chapter = 5,
					Takes = new[] { ParameterCategory.WarParameter, ParameterCategory.Insurrection },
					Order = new[] { "Insurrection", "Army", "NoblesMans", "PriestsMans", "CommonerMans" } },
			};
			// the unlock chapter as the HUD button has it, where it is loaded
			foreach (_Scripts.AMVCC.Views.HudButtonBehavior hb in Resources.FindObjectsOfTypeAll<_Scripts.AMVCC.Views.HudButtonBehavior>())
			{
				Tab t = hb != null && hb.gameObject.scene.IsValid() ? tabs.Find(x => x.Hud == hb.name) : null;
				if (t != null && hb.ChapterToUnlock > 0)
				{
					t.Chapter = hb.ChapterToUnlock;
				}
			}
			HashSet<Parameter> placed = new HashSet<Parameter>();
			foreach (Tab t in tabs)
			{
				bool group = true;
				foreach (string name in t.Order)
				{
					if (name == "|")
					{
						group = true;
						continue;
					}
					Parameter p = SceneEffects.ParameterAsset(name);
					if (p != null && p.Icon != null && !IsDead(p) && placed.Add(p))
					{
						if (group)
						{
							t.Groups.Add(t.Stats.Count);
							group = false;
						}
						t.Stats.Add(p);
					}
				}
			}
			List<Parameter> rest = SceneEffects.AllStats();
			rest.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			foreach (Parameter p in rest)
			{
				Tab t = tabs.Find(x => Array.IndexOf(x.Takes, p.ParameterCategory) >= 0);
				if (t != null && !IsDead(p) && placed.Add(p))
				{
					t.Stats.Add(p);
				}
			}
			tabs.RemoveAll(t => t.Stats.Count == 0);
			return tabs;
		}

		private static string TabTitle(Tab t)
		{
			switch (t.Hud)
			{
				case "HUD.Character": return Loc.Game("HUD.Character", "Personality");
				case "HUD.Home": return Loc.Game("HUD.Home", "House of Brante");
				case "HUD.Empire": return Loc.Game("HUD.Empire", "Province");
				case "HUD.Inquisition": return Loc.Game("Work", "Work");
				default: return Loc.Game(t.Hud, t.Hud);
			}
		}

		private static readonly List<Row> _rows = new List<Row>();
		private static readonly List<Tab> _tabs = new List<Tab>();
		private static GameObject _template;
		private static GameObject _holder;

		// The game's row prefab (the asset, not one of the live tab rows).
		private static GameObject Template()
		{
			if (_template != null)
			{
				return _template;
			}
			GameObject any = null;
			foreach (ParameterComponent pc in Resources.FindObjectsOfTypeAll<ParameterComponent>())
			{
				if (pc == null)
				{
					continue;
				}
				if (pc.name == "Parameter_Horizontal" && !pc.gameObject.scene.IsValid())
				{
					_template = pc.gameObject;
					return _template;
				}
				if (any == null && pc.name.StartsWith("Parameter_Horizontal", StringComparison.Ordinal))
				{
					any = pc.gameObject;
				}
			}
			_template = any;
			return _template;
		}

		// Lays the stats out in `area`: categories side by side, a category
		// with more stats than fit in one column taking several, rows scaled
		// down from the game's size until everything fits.
		public static void Build(RectTransform area)
		{
			_area = area;
			for (int i = area.childCount - 1; i >= 0; i--)
			{
				GameObject old = area.GetChild(i).gameObject;
				old.SetActive(false);
				UnityEngine.Object.Destroy(old);
			}
			_content = null;
			GameObject template = Template();
			if (template == null)
			{
				return;
			}
			ModeTabs(area, template);
			GameObject cgo = new GameObject("Content", typeof(RectTransform));
			cgo.transform.SetParent(area, false);
			_content = cgo.GetComponent<RectTransform>();
			_content.anchorMin = Vector2.zero;
			_content.anchorMax = Vector2.one;
			_content.offsetMin = Vector2.zero;
			_content.offsetMax = new Vector2(0f, -TabStrip);
			_peopleKey = null;
			BuildContent();
		}

		private static void ClearContent()
		{
			_rows.Clear();
			_roots.Clear();
			_tabs.Clear();
			if (_content == null)
			{
				return;
			}
			for (int i = _content.childCount - 1; i >= 0; i--)
			{
				GameObject old = _content.GetChild(i).gameObject;
				old.SetActive(false);
				UnityEngine.Object.Destroy(old);
			}
		}

		private static void BuildContent()
		{
			ClearContent();
			if (_content == null)
			{
				return;
			}
			if (Showing == Mode.Stats)
			{
				BuildStats(_content);
			}
			else if (_lastShown != null)
			{
				BuildPeople(_content, People(_lastShown, _lastNow));
			}
		}

		// The three tabs along the panel's top: the active one in the accent
		// colour and underlined, the others in ink.
		private static readonly List<KeyValuePair<Mode, TextMeshProUGUI>> _modeLabels
			= new List<KeyValuePair<Mode, TextMeshProUGUI>>();

		private static void ModeTabs(RectTransform area, GameObject template)
		{
			_modeLabels.Clear();
			Mode[] modes = { Mode.Stats, Mode.Family, Mode.Relations };
			const float w = 220f;
			float x0 = (area.rect.width - modes.Length * w) * 0.5f;
			for (int i = 0; i < modes.Length; i++)
			{
				Mode mode = modes[i];
				GameObject go = new GameObject("Tab_" + mode, typeof(RectTransform));
				go.transform.SetParent(area, false);
				RectTransform rt = go.GetComponent<RectTransform>();
				rt.anchorMin = new Vector2(0f, 1f);
				rt.anchorMax = new Vector2(0f, 1f);
				rt.pivot = new Vector2(0f, 1f);
				rt.anchoredPosition = new Vector2(x0 + i * w, 0f);
				rt.sizeDelta = new Vector2(w, TabStrip - 6f);
				Image img = go.AddComponent<Image>();
				img.color = Color.clear;
				Button b = go.AddComponent<Button>();
				b.targetGraphic = img;
				b.transition = Selectable.Transition.None;
				b.onClick.AddListener(delegate { SetMode(mode); });
				TextMeshProUGUI t = Label(go.transform, ModeTitle(mode), template);
				RectTransform lrt = t.rectTransform;
				lrt.anchorMin = Vector2.zero;
				lrt.anchorMax = Vector2.one;
				lrt.offsetMin = Vector2.zero;
				lrt.offsetMax = Vector2.zero;
				t.fontSize = 24f;
				_modeLabels.Add(new KeyValuePair<Mode, TextMeshProUGUI>(mode, t));
			}
			StyleModeTabs();
		}

		private static string ModeTitle(Mode m)
		{
			switch (m)
			{
				case Mode.Family: return Loc.Game("HUD.Family", "Family");
				case Mode.Relations: return Loc.Game("HUD.Relation", "Relations");
				default: return Loc.T(Loc.K.StatsTab);
			}
		}

		private static void StyleModeTabs()
		{
			foreach (KeyValuePair<Mode, TextMeshProUGUI> kv in _modeLabels)
			{
				bool on = kv.Key == Showing;
				kv.Value.color = on ? Palette.Accent : Palette.Fade(Palette.Ink, 0.6f);
				kv.Value.fontStyle = on ? FontStyles.Underline : FontStyles.Normal;
			}
		}

		public static void SetMode(Mode m)
		{
			if (m == Showing && _content != null)
			{
				return;
			}
			Showing = m;
			ModLog.Info("StatPanel: showing " + m);
			HideTooltip();
			StyleModeTabs();
			_peopleKey = null;
			BuildContent();
			if (_lastShown != null)
			{
				Show(_lastShown, _lastNow, _lastChapter);
			}
		}

		private static void BuildStats(RectTransform area)
		{
			GameObject template = Template();
			_tabs.AddRange(Tabs());
			Metrics m = Measure(template);
			Vector2 natural = m.Natural;
			float contentTop = m.Top, contentLeft = m.Left, contentRight = m.Right;
			_hoverInset = new Vector2(m.Top, natural.y - m.Bottom);
			_drawnX = new Vector2(contentLeft, contentRight);
			float pitch = m.Pitch;

			float width = area.rect.width;
			float usable = Mathf.Max(40f, area.rect.height - Header);
			// the largest scale at which every category fits
			float scale = 0.3f;
			int rowsFit = 1;
			for (float s = 1.3f; s >= 0.3f; s -= 0.02f)
			{
				int fit = Mathf.Max(1, Mathf.FloorToInt(usable / (pitch * s)));
				scale = s;
				rowsFit = fit;
				if (FitsWidth(_tabs, m, s, fit, width))
				{
					break;
				}
			}
			int total = 0;
			foreach (Tab t in _tabs)
			{
				int c;
				Place(t, rowsFit, out c);
				total += c;
			}
			float subW = width / Mathf.Max(1, total);
			// spare height goes under the titles, so a locked tab's note has
			// room before its first stat
			float gap = Mathf.Clamp(usable - rowsFit * pitch * scale, 0f, 20f);
			_statScale = scale;
			string layoutKey = area.rect.size + " " + scale.ToString("0.00") + " " + rowsFit;
			if (layoutKey != _layoutLogged)
			{
				_layoutLogged = layoutKey;
				List<string> tabs = new List<string>();
				foreach (Tab t in _tabs)
				{
					tabs.Add(t.Hud + ":" + string.Join(",", t.Stats.ConvertAll(p => p.name).ToArray()));
				}
				ModLog.Info("StatPanel: area " + area.rect.size + ", scale " + scale.ToString("0.00")
					+ ", " + rowsFit + " rows, " + total + " columns; " + string.Join(" | ", tabs.ToArray()));
			}

			float x = 0f;
			foreach (Tab tab in _tabs)
			{
				List<Parameter> list = tab.Stats;
				int subs;
				List<Vector2> at = Place(tab, rowsFit, out subs);
				if (x > 0f)
				{
					Separator(area, x);
				}
				TextMeshProUGUI title = Label(area, TabTitle(tab), template);
				tab.Title = title;
				RectTransform trt = title.rectTransform;
				trt.anchorMin = new Vector2(0f, 1f);
				trt.anchorMax = new Vector2(0f, 1f);
				trt.pivot = new Vector2(0f, 1f);
				trt.anchoredPosition = new Vector2(x + 8f, 0f);
				trt.sizeDelta = new Vector2(subs * subW - 16f, Header);
				for (int j = 0; j < list.Count; j++)
				{
					Row row = MakeRow(list[j], template);
					if (row == null)
					{
						continue;
					}
					row.Tab = tab;
					RectTransform rrt = RowRoot(row);
					rrt.SetParent(area, false);
					rrt.anchorMin = new Vector2(0f, 1f);
					rrt.anchorMax = new Vector2(0f, 1f);
					rrt.pivot = new Vector2(0f, 1f);
					rrt.sizeDelta = natural;
					rrt.localScale = new Vector3(scale, scale, 1f);
					int sub = (int)at[j].x;
					int r = (int)at[j].y;
					// centred on what it draws (icon to slider end), so a rule
					// between tabs is as far from a slider as from an icon
					rrt.anchoredPosition = new Vector2(
						x + sub * subW + (subW - (contentRight - contentLeft) * scale) * 0.5f - contentLeft * scale,
						-(Header + gap + r * pitch * scale - contentTop * scale));
					rrt.gameObject.SetActive(true);
					_fitLimit = contentRight;
					FitText(row, _fitLimit);
					AddHover(row, rrt);
					_rows.Add(row);
				}
				x += subs * subW;
			}
		}

		// ---- Family and Relations ----

		private static Dictionary<CharacterList, Character> _people;

		private static Character PersonAsset(CharacterList name)
		{
			if (_people == null)
			{
				_people = new Dictionary<CharacterList, Character>();
				foreach (Character c in Resources.FindObjectsOfTypeAll<Character>())
				{
					if (c != null && !_people.ContainsKey(c.Name))
					{
						_people[c.Name] = c;
					}
				}
			}
			Character found;
			return _people.TryGetValue(name, out found) && found != null ? found : null;
		}

		private static CharacterModel ModelOf(SceneEffects.Hero h, CharacterList name)
		{
			return h == null || h.Characters == null ? null : h.Characters.Find(m => m != null && m.Character == name);
		}

		// The characters the tab shows for these heroes: the family (the
		// hero left out) or everyone else, as far as the hero has met them
		// (unlocked, or a relation already changed by a predicted answer),
		// in the order the hero met them, as the game's windows list them.
		private static List<Character> People(SceneEffects.Hero shown, SceneEffects.Hero now)
		{
			List<KeyValuePair<string, Character>> found = new List<KeyValuePair<string, Character>>();
			HashSet<CharacterList> seen = new HashSet<CharacterList>();
			foreach (SceneEffects.Hero h in new[] { shown, now })
			{
				if (h == null || h.Characters == null)
				{
					continue;
				}
				foreach (CharacterModel m in h.Characters)
				{
					if (m == null || m.Character == CharacterList.Hero || seen.Contains(m.Character)
						|| !(m.IsUnlocked || m.Relation != 0))
					{
						continue;
					}
					Character c = PersonAsset(m.Character);
					if (c == null || c.IsFamily != (Showing == Mode.Family) || (h == now && ModelOf(shown, m.Character) != null
						&& !ModelOf(shown, m.Character).IsUnlocked && ModelOf(shown, m.Character).Relation == 0))
					{
						continue;   // the hero shown has not met them yet
					}
					seen.Add(m.Character);
					found.Add(new KeyValuePair<string, Character>(m.UnlockDate ?? "", c));
				}
			}
			// stable: by meeting date, ties in the game's list order
			List<KeyValuePair<string, Character>> sorted = new List<KeyValuePair<string, Character>>(found);
			sorted.Sort((a, b) =>
			{
				int d = string.CompareOrdinal(a.Key, b.Key);
				return d != 0 ? d : found.IndexOf(a) - found.IndexOf(b);
			});
			return sorted.ConvertAll(kv => kv.Value);
		}

		private static string PeopleKey(List<Character> people)
		{
			return Showing + ":" + string.Join(",", people.ConvertAll(c => c.Name.ToString()).ToArray());
		}

		// The characters as stat rows, column after column, as large as fits.
		private static void BuildPeople(RectTransform area, List<Character> people)
		{
			_peopleKey = PeopleKey(people);
			GameObject template = Template();
			if (template == null)
			{
				return;
			}
			Metrics m = Measure(template);
			_hoverInset = new Vector2(m.Top, m.Natural.y - m.Bottom);
			_drawnX = new Vector2(m.Left, m.Right);
			const float top = 10f;
			float width = area.rect.width;
			float usable = Mathf.Max(40f, area.rect.height - top);
			float scale = 0.3f;
			int rows = 1, columns = 1;
			// no larger than the stat rows, so the tabs look alike
			for (float sc = _statScale; sc >= 0.3f; sc -= 0.02f)
			{
				rows = Mathf.Max(1, Mathf.FloorToInt(usable / (m.Pitch * sc)));
				columns = Mathf.Max(1, Mathf.CeilToInt(people.Count / (float)rows));
				scale = sc;
				if ((m.Right - m.Left) * sc <= width / Mathf.Max(columns, 5) - ColumnGap)
				{
					break;
				}
			}
			// at least five columns' width each, so a short family list does
			// not stretch across the panel
			float subW = width / Mathf.Max(columns, 5);
			float x0 = (width - subW * columns) * 0.5f;
			for (int j = 0; j < people.Count; j++)
			{
				Row row = MakePersonRow(people[j], template);
				if (row == null)
				{
					continue;
				}
				RectTransform rrt = RowRoot(row);
				rrt.SetParent(area, false);
				rrt.anchorMin = new Vector2(0f, 1f);
				rrt.anchorMax = new Vector2(0f, 1f);
				rrt.pivot = new Vector2(0f, 1f);
				rrt.sizeDelta = m.Natural;
				rrt.localScale = new Vector3(scale, scale, 1f);
				int sub = j / rows, r = j % rows;
				if (sub > 0 && r == 0)
				{
					Separator(area, x0 + sub * subW);
				}
				rrt.anchoredPosition = new Vector2(
					x0 + sub * subW + (subW - (m.Right - m.Left) * scale) * 0.5f - m.Left * scale,
					-(top + r * m.Pitch * scale - m.Top * scale));
				rrt.gameObject.SetActive(true);
				_fitLimit = m.Right;
				FitText(row, _fitLimit);
				AddHover(row, rrt);
				_rows.Add(row);
			}
			if (people.Count == 0)
			{
				TextMeshProUGUI t = Label(area, "\u2014", template);
				t.rectTransform.anchorMin = Vector2.zero;
				t.rectTransform.anchorMax = Vector2.one;
				t.rectTransform.offsetMin = Vector2.zero;
				t.rectTransform.offsetMax = Vector2.zero;
				t.color = Palette.Fade(Palette.Ink, 0.5f);
			}
			ModLog.Info("StatPanel: " + Showing + " " + people.Count + " rows, " + columns + " columns, scale "
				+ scale.ToString("0.00"));
		}

		// The game's stat row for a character: portrait, name, relation
		// (-5 ... +5 on the two-sided slider) and the relation's word, or
		// the character's status when it is not the usual one.
		private static Row MakePersonRow(Character c, GameObject template)
		{
			return MakeRow(null, template, c);
		}

		private static Sprite Portrait(Character c, int chapter)
		{
			Sprite s;
			switch (chapter)
			{
				case 2: s = c.TeenSprite; break;
				case 3: s = c.YouthSprite; break;
				case 4: s = c.PeaceSprite; break;
				case 5: s = c.WarSprite; break;
				default: s = c.ChildSprite; break;
			}
			return s != null ? s : c.DefaultSprite;
		}

		private static string PersonName(Character c)
		{
			try
			{
				if (KeyChapterParametersController.Initiate != null)
				{
					return KeyChapterParametersController.Initiate.GetCharacterTrueName(c.Name);
				}
			}
			catch (Exception)
			{
			}
			return SceneEffects.NobleParticle(Loc.Game(c.Name.ToString(), c.name));
		}

		// The game's own word for a relation (ParametersManager.CheckParameterValue).
		private static string RelationWord(int value)
		{
			return value <= -4 ? Loc.Game("Hatred", "") : value <= -2 ? Loc.Game("Dislike", "")
				: value <= 1 ? Loc.Game("Indifference", "") : value <= 3 ? Loc.Game("Sympathy", "")
				: Loc.Game("Friendship", "");
		}

		private static bool Gone(CharacterStatus s)
		{
			string n = s.ToString();
			return n.StartsWith("Dead", StringComparison.Ordinal) || s == CharacterStatus.Left
				|| s == CharacterStatus.Exile;
		}

		private static bool Usual(CharacterStatus s)
		{
			return s == CharacterStatus.Good || s == CharacterStatus.Not;
		}

		private const float Header = 50f;     // a tab's title, and a locked tab's note under it
		private const float ColumnGap = 20f;  // between two columns' drawn rows

		// The game's row as the panel lays it out: its box, the part of it that
		// draws (icon to slider end, text top to slider foot) and the distance
		// between two stacked rows. Measured once from the prefab.
		private struct Metrics
		{
			public Vector2 Natural;
			public float Top, Bottom, Left, Right, Pitch;
		}

		private static Metrics? _metrics;

		private static Metrics Measure(GameObject template)
		{
			if (_metrics.HasValue)
			{
				return _metrics.Value;
			}
			Metrics m = new Metrics();
			m.Natural = template.GetComponent<RectTransform>().rect.size;
			if (m.Natural.x < 1f || m.Natural.y < 1f)
			{
				m.Natural = new Vector2(375f, 103f);
			}
			// The row's box is taller than what it shows (icon and text fill
			// 70 of its 103 units, 10 down from the top): rows are stacked
			// by their content, so they can be drawn larger.
			m.Top = 0f;
			m.Bottom = m.Natural.y;
			m.Left = 0f;
			m.Right = m.Natural.x;
			ContentSpan(template, ref m.Top, ref m.Bottom, ref m.Left, ref m.Right);
			m.Pitch = m.Bottom - m.Top + 6f;
			_metrics = m;
			return m;
		}

		// Whether the tabs, `rows` stats to a column, fit side by side in
		// `width` at `scale`: what a row draws (not its wider box) plus a gap.
		private static bool FitsWidth(List<Tab> tabs, Metrics m, float scale, int rows, float width)
		{
			int columns = 0;
			foreach (Tab t in tabs)
			{
				int c;
				Place(t, rows, out c);
				columns += c;
			}
			return (m.Right - m.Left) * scale <= width / Mathf.Max(1, columns) - ColumnGap;
		}

		// From the top of the row's box, where what it draws starts and ends:
		// every graphic that shows (all its parents switched on), the slider
		// included, which hangs below its layout group (left as they are when
		// nothing is found).
		private static void ContentSpan(GameObject template, ref float top, ref float bottom,
			ref float left, ref float right)
		{
			RectTransform root = template.GetComponent<RectTransform>();
			float t = float.MaxValue, b = float.MinValue, l = float.MaxValue, r = float.MinValue;
			foreach (Graphic g in template.GetComponentsInChildren<Graphic>(true))
			{
				if (g.gameObject == template || !Shows(g.transform, root.transform)
					|| g.rectTransform.rect.height < 1f || g.rectTransform.rect.width < 1f)
				{
					continue;
				}
				Vector3[] c = new Vector3[4];
				g.rectTransform.GetWorldCorners(c);
				float y0 = root.rect.yMax - root.InverseTransformPoint(c[1]).y;
				float y1 = root.rect.yMax - root.InverseTransformPoint(c[0]).y;
				t = Mathf.Min(t, y0);
				b = Mathf.Max(b, y1);
				if (!(g is TextMeshProUGUI))   // a text's box is wider than its words
				{
					l = Mathf.Min(l, root.InverseTransformPoint(c[0]).x - root.rect.xMin);
					r = Mathf.Max(r, root.InverseTransformPoint(c[2]).x - root.rect.xMin);
				}
			}
			if (t < b && b - t > 10f)
			{
				top = Mathf.Max(0f, t);
				bottom = Mathf.Min(root.rect.height, b);
			}
			if (l < r && r - l > 10f)
			{
				left = Mathf.Max(0f, l);
				right = Mathf.Min(root.rect.width, r);
			}
		}

		private static bool Shows(Transform t, Transform root)
		{
			for (; t != null && t != root; t = t.parent)
			{
				if (!t.gameObject.activeSelf)
				{
					return false;
				}
			}
			return true;
		}

		private static Vector2 _hoverInset;   // (top, bottom) of the row box left out of its hover area
		// (left, right) of what a row draws (icon to slider end) in its box:
		// the box is wider, and reaches into the next column
		private static Vector2 _drawnX;

		// Fits `rt` (a child of a row) over what the row draws, `pad` wider on
		// each side, from the row box's left edge.
		private static void OverDrawn(RectTransform rt, float pad)
		{
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = new Vector2(0f, 1f);
			rt.offsetMin = new Vector2(_drawnX.x - pad, _hoverInset.y);
			rt.offsetMax = new Vector2(_drawnX.y + pad, -_hoverInset.x);
		}

		private static readonly Dictionary<Row, RectTransform> _roots = new Dictionary<Row, RectTransform>();

		private static RectTransform RowRoot(Row row)
		{
			RectTransform rt;
			return _roots.TryGetValue(row, out rt) ? rt : null;
		}

		// A clone of the game's row for one stat, its scripts removed.
		private static Row MakeRow(Parameter p, GameObject template, Character person = null)
		{
			if (_holder == null)
			{
				_holder = new GameObject("BranteSaveLoad_StatRows");
				_holder.SetActive(false);
				UnityEngine.Object.DontDestroyOnLoad(_holder);
			}
			GameObject go = UnityEngine.Object.Instantiate(template, _holder.transform, false);
			ParameterComponent pc = go.GetComponent<ParameterComponent>();
			if (pc == null)
			{
				UnityEngine.Object.Destroy(go);
				return null;
			}
			bool twoSided = person != null || (p != null && p.MinValue < 0f && Math.Abs(p.MinValue) == p.MaxValue);
			GameObject slider = twoSided ? pc.TwoSideSlider : pc.SimpleSlider;
			GameObject other = twoSided ? pc.SimpleSlider : pc.TwoSideSlider;
			if (other != null)
			{
				other.SetActive(false);
			}
			if (slider != null)
			{
				slider.SetActive(true);
				ParameterSliderComponent sc = slider.GetComponent<ParameterSliderComponent>();
				if (p != null && p.ScalePrefab != null && sc != null && sc.ScaleContainer != null)
				{
					UnityEngine.Object.Instantiate(p.ScalePrefab, sc.ScaleContainer, false);
				}
			}
			if (pc.ArrowImg != null)
			{
				pc.ArrowImg.gameObject.SetActive(false);
			}
			if (pc.Icon != null)
			{
				pc.Icon.sprite = person != null ? Portrait(person, 1) : p.Icon;
				pc.Icon.preserveAspect = true;
			}
			if (pc.Name != null)
			{
				pc.Name.text = person != null ? PersonName(person) : Loc.Game(p.ParameterName.ToString(), p.name);
			}
			// the row is drawn at about half size in the tree: its words a
			// little larger than the game's, to stay readable
			foreach (TextMeshProUGUI t in new[] { pc.Name, pc.TextValue, pc.Descr })
			{
				if (t != null)
				{
					float size = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;
					t.enableWordWrapping = false;   // a long level name stays on its line
					t.enableAutoSizing = t == pc.Descr;   // the level word shrinks to its box
					t.fontSize = size * 1.15f;
					t.fontSizeMax = size * 1.15f;
					t.fontSizeMin = size * 0.6f;
				}
			}
			Row row = new Row();
			row.Param = person != null ? null : p;
			row.Person = person;
			row.Icon = pc.Icon;
			row.Name = pc.Name;
			if (pc.Descr != null)
			{
				// the level word's box ends where the slider does, not 30
				// units past it: it shrinks to that, and stays off the rule
				// beside it
				RectTransform drt = pc.Descr.rectTransform;
				float w = 250f;
				drt.sizeDelta = new Vector2(w, drt.sizeDelta.y);
				LayoutElement le = pc.Descr.GetComponent<LayoutElement>();
				if (le != null)
				{
					le.preferredWidth = w;
					le.minWidth = -1f;
				}
			}
			row.Value = pc.TextValue;
			row.Descr = pc.Descr;
			row.NameSize = pc.Name != null ? pc.Name.fontSize : 0f;
			row.ValueSize = pc.TextValue != null ? pc.TextValue.fontSize : 0f;
			row.Slider = slider != null ? slider.GetComponent<Slider>() : null;
			if (row.Slider != null)
			{
				row.Slider.minValue = person != null ? -5f : p.MinValue;
				row.Slider.maxValue = person != null ? 5f : p.MaxValue;
				row.Slider.interactable = false;
			}
			// the game's scripts would refill the row from the live hero
			foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
			{
				string ns = mb.GetType().Namespace ?? "";
				if (!ns.StartsWith("UnityEngine", StringComparison.Ordinal) && !ns.StartsWith("TMPro", StringComparison.Ordinal))
				{
					UnityEngine.Object.DestroyImmediate(mb);
				}
			}
			// the row's Animator would play the game's slider animation over our
			// values (by name: the animation module is not referenced)
			foreach (Behaviour b in go.GetComponentsInChildren<Behaviour>(true))
			{
				if (b.GetType().Name == "Animator")
				{
					b.enabled = false;
				}
			}
			foreach (Graphic g in go.GetComponentsInChildren<Graphic>(true))
			{
				g.raycastTarget = false;
			}
			_roots[row] = go.GetComponent<RectTransform>();
			return row;
		}

		// A category title in the row's own font.
		private static TextMeshProUGUI Label(Transform parent, string text, GameObject template)
		{
			GameObject go = new GameObject("Category", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
			ParameterComponent pc = template.GetComponent<ParameterComponent>();
			if (pc != null && pc.Name != null)
			{
				t.font = pc.Name.font;
			}
			t.text = text;
			t.fontSize = 22f;
			t.color = Palette.Accent;
			t.alignment = TextAlignmentOptions.Center;
			t.enableWordWrapping = false;
			t.overflowMode = TextOverflowModes.Ellipsis;
			t.raycastTarget = false;
			return t;
		}

		// The values of `shown` (the hero along the path); a value above
		// `now` (the hero today) is green, below red. Before chapter 3 the
		// six skills are 0; their rows show what the chapter-3 start would
		// make of the stats shown (faded, "Will unlock in Chapter III"), so a
		// pick in chapters 1-2 shows its effect there.
		public static void Show(SceneEffects.Hero shown, SceneEffects.Hero now, int chapter)
		{
			if (shown == null || now == null)
			{
				return;
			}
			_lastShown = shown;
			_lastNow = now;
			_lastChapter = chapter;
			if (Showing != Mode.Stats)
			{
				ShowPeople(shown, now, chapter);
				return;
			}
			bool early = chapter > 0 && chapter < 3;
			shown = SceneEffects.WithSkillsProjected(shown);
			now = SceneEffects.WithSkillsProjected(now);
			// tabs the game has not opened yet at that chapter: greyed, with
			// the game's own "opens in chapter N" under the title
			foreach (Tab tab in _tabs)
			{
				if (tab.Title == null)
				{
					continue;
				}
				bool locked = chapter > 0 && tab.Chapter > chapter;
				string name = TabTitle(tab);
				tab.Title.text = locked
					? name + "\n<size=58%>" + Loc.Game("HUD.WillOpen" + tab.Chapter, "") + "</size>"
					: name;
				tab.Title.color = locked ? Palette.Fade(Palette.Accent, 0.45f) : Palette.Accent;
			}
			foreach (Row row in _rows)
			{
				bool projected = early && SceneEffects.IsSkill(row.Param.ParameterName);
				CanvasGroup cg = RowRoot(row) != null ? RowRoot(row).GetComponent<CanvasGroup>() : null;
				if (cg != null)
				{
					cg.alpha = projected || (chapter > 0 && row.Tab != null && row.Tab.Chapter > chapter) ? 0.35f : 1f;
				}
				int value = shown.Value(row.Param);
				int today = now.Value(row.Param);
				row.Shown = value;
				if (row.Value != null)
				{
					row.Value.text = row.Param.ParameterName == ParametersList.Death ? "" : value.ToString();
					row.Value.color = value > today ? Palette.Good
						: value < today ? Palette.Bad : Palette.Ink;
				}
				if (row.Slider != null)
				{
					row.Slider.value = value;
				}
				if (row.Descr != null)
				{
					row.Descr.text = projected ? Loc.Game("HUD.WillOpen3", Segment(row.Param, value)) : Segment(row.Param, value);
				}
				FitText(row, _fitLimit);
			}
		}

		// The characters' relations along the path; the rows are rebuilt when
		// the hero shown has met someone else.
		private static void ShowPeople(SceneEffects.Hero shown, SceneEffects.Hero now, int chapter)
		{
			List<Character> people = People(shown, now);
			if (PeopleKey(people) != _peopleKey && _content != null)
			{
				ClearContent();
				BuildPeople(_content, people);
			}
			foreach (Row row in _rows)
			{
				if (row.Person == null)
				{
					continue;
				}
				CharacterModel m = ModelOf(shown, row.Person.Name);
				CharacterModel t = ModelOf(now, row.Person.Name);
				int value = m != null ? m.Relation : 0;
				int today = t != null ? t.Relation : value;
				row.Shown = value;
				row.Status = m != null ? m.Status : CharacterStatus.Good;
				bool met = m != null && (m.IsUnlocked || m.Relation != 0);
				if (row.Icon != null)
				{
					row.Icon.sprite = Portrait(row.Person, chapter);
				}
				CanvasGroup cg = RowRoot(row) != null ? RowRoot(row).GetComponent<CanvasGroup>() : null;
				if (cg != null)
				{
					cg.alpha = !met || Gone(row.Status) ? 0.4f : 1f;
				}
				if (row.Value != null)
				{
					row.Value.text = (value > 0 ? "+" : "") + value;
					row.Value.color = value > today ? Palette.Good : value < today ? Palette.Bad : Palette.Ink;
				}
				if (row.Slider != null)
				{
					row.Slider.value = value;
				}
				if (row.Descr != null)
				{
					row.Descr.text = Usual(row.Status) ? RelationWord(value)
						: Loc.Game(row.Person.Name + "." + row.Status, RelationWord(value));
				}
				FitText(row, _fitLimit);
			}
		}

		private static int Delta(Row row, SceneEffects.Hero before, SceneEffects.Hero after)
		{
			if (before == null || after == null)
			{
				return 0;
			}
			if (row.Person == null)
			{
				return after.Value(row.Param) - before.Value(row.Param);
			}
			CharacterModel a = ModelOf(before, row.Person.Name), b = ModelOf(after, row.Person.Name);
			if (a == null || b == null)
			{
				return 0;
			}
			if (b.Relation != a.Relation)
			{
				return b.Relation - a.Relation;
			}
			// a status change: red when they are lost, green otherwise
			return b.Status == a.Status ? 0 : Gone(b.Status) ? -1 : 1;
		}

		// While an answer is hovered in the tree: the stats it changes get a
		// background, green for a rise and red for a fall (the colours of the
		// game's own consequence rows), over what the row shows.
		public static void HighlightChanges(SceneEffects.Hero before, SceneEffects.Hero after)
		{
			// in chapters 1-2 the skills the answer will feed are marked too
			before = SceneEffects.WithSkillsProjected(before);
			after = SceneEffects.WithSkillsProjected(after);
			foreach (Row row in _rows)
			{
				int d = Delta(row, before, after);
				if (d == 0)
				{
					if (row.Mark != null)
					{
						row.Mark.enabled = false;
					}
					continue;
				}
				RectTransform root = RowRoot(row);
				if (root == null)
				{
					continue;
				}
				if (row.Mark == null)
				{
					GameObject go = new GameObject("ChangeMark", typeof(RectTransform));
					go.transform.SetParent(root, false);
					go.transform.SetAsFirstSibling();   // behind the icon and the text
					RectTransform rt = go.GetComponent<RectTransform>();
					OverDrawn(rt, 4f);   // the stat only, not the rest of its box
					go.AddComponent<LayoutElement>().ignoreLayout = true;
					row.Mark = go.AddComponent<Image>();
					row.Mark.raycastTarget = false;
				}
				row.Mark.color = Palette.Fade(d > 0 ? Palette.Good : Palette.Bad, 0.28f);
				row.Mark.enabled = true;
			}
		}

		public static void ClearHighlight()
		{
			HighlightChanges(null, null);
		}

		// A light vertical rule between two categories, in the colour of the
		// tree's answer boxes.
		private static void Separator(RectTransform area, float x)
		{
			GameObject go = new GameObject("Separator", typeof(RectTransform));
			go.transform.SetParent(area, false);
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0f, 0f);
			rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = new Vector2(x, 0f);
			rt.sizeDelta = new Vector2(3f, -8f);
			Image img = go.AddComponent<Image>();
			img.color = Palette.Card;
			img.raycastTarget = false;
		}

		// An invisible hit area over the row: hovering it opens the game's own
		// stat tooltip.
		private static void AddHover(Row row, RectTransform root)
		{
			GameObject go = new GameObject("Hover", typeof(RectTransform));
			go.transform.SetParent(root, false);
			RectTransform rt = go.GetComponent<RectTransform>();
			// over what the row shows only: the boxes of stacked rows overlap,
			// and a box reaches into the next column
			OverDrawn(rt, 0f);
			// the row lays its children out in a row: keep this one out of it,
			// or it is squeezed to nothing and never hovered
			go.AddComponent<LayoutElement>().ignoreLayout = true;
			Image img = go.AddComponent<Image>();
			img.color = Color.clear;
			TreeHover hover = go.AddComponent<TreeHover>();
			hover.OnHover = delegate(bool on)
			{
				if (on)
				{
					ShowTooltip(row, rt);
				}
				else
				{
					HideTooltip();
				}
			};
		}

		// The tooltip the game shows over a stat in its own tabs
		// ("ParameterValueTooltip"): icon, name, description and the scale's
		// steps, the step of the value marked. Cloned once into `TooltipParent`
		// (the tree's box), since the game's own one lies under the mod's
		// window; its registration script is removed so the game never finds
		// the clone instead of its own.
		public static Transform TooltipParent;
		private static GameObject _tip;
		private static ParameterValueTooltipComponent _tipComp;

		private static bool EnsureTooltip()
		{
			if (_tip != null && _tipComp != null)
			{
				return true;
			}
			if (TooltipParent == null)
			{
				return false;
			}
			GameObject source = RegisterGO.Find("ParameterValueTooltip");
			if (source == null)
			{
				foreach (ParameterValueTooltipComponent c in Resources.FindObjectsOfTypeAll<ParameterValueTooltipComponent>())
				{
					if (c != null)
					{
						source = c.gameObject;
						break;
					}
				}
			}
			if (source == null)
			{
				return false;
			}
			if (_holder == null)
			{
				_holder = new GameObject("BranteSaveLoad_StatRows");
				_holder.SetActive(false);
				UnityEngine.Object.DontDestroyOnLoad(_holder);
			}
			_tip = UnityEngine.Object.Instantiate(source, _holder.transform, false);
			_tip.name = "BranteSaveLoad_StatTooltip";
			foreach (MonoBehaviour mb in _tip.GetComponentsInChildren<MonoBehaviour>(true))
			{
				if (mb.GetType().Name == "Registration")
				{
					UnityEngine.Object.DestroyImmediate(mb);
				}
			}
			foreach (Graphic g in _tip.GetComponentsInChildren<Graphic>(true))
			{
				g.raycastTarget = false;
			}
			// the game fades its tooltips in with an Animator on a CanvasGroup;
			// the clone shows at once
			foreach (CanvasGroup cg in _tip.GetComponentsInChildren<CanvasGroup>(true))
			{
				cg.alpha = 1f;
			}
			foreach (Behaviour b in _tip.GetComponentsInChildren<Behaviour>(true))
			{
				if (b.GetType().Name == "Animator")
				{
					b.enabled = false;
				}
			}
			// the prefab carries its own Canvas sorted at 9; the mod window draws at
			// 8000, so lift it above that or it renders underneath the tree
			foreach (Canvas cv in _tip.GetComponentsInChildren<Canvas>(true))
			{
				cv.overrideSorting = true;
				cv.sortingOrder = 8100;
			}
			_tipComp = _tip.GetComponent<ParameterValueTooltipComponent>();
			_tip.SetActive(false);
			_tip.transform.SetParent(TooltipParent, false);
			return _tipComp != null;
		}

		private static void ShowTooltip(Row row, RectTransform over)
		{
			try
			{
				if (!EnsureTooltip())
				{
					return;
				}
				if (row.Person != null)
				{
					ShowPersonTooltip(row, over);
					return;
				}
				Parameter p = row.Param;
				_tipComp.ClearTooltipWindow();
				if (p.Scales != null)
				{
					for (int i = 0; i < p.Scales.Length; i++)
					{
						Scale sc = p.Scales[i];
						_tipComp.SetRow(sc.MinValue == sc.MaxValue ? sc.MinValue.ToString() : sc.MinValue + " ... " + sc.MaxValue,
							Loc.Game(p.ParameterName + ".Segment" + (i + 1), ""),
							row.Shown >= sc.MinValue && row.Shown <= sc.MaxValue);
					}
				}
				_tipComp.Title.text = Loc.Game(p.ParameterName.ToString(), p.name);
				_tipComp.Icon.sprite = p.Icon;
				// some stats (Love, Cruelty, ...) the game never describes: say so
				// rather than leave the pop-up looking broken
				string descr = Loc.Game(p.ParameterName + ".Description", "");
				_tipComp.Description.text = descr.Length > 0 ? descr : "<i>" + Loc.T(Loc.K.NoStatText) + "</i>";
				PlaceTooltip(over);
			}
			catch (Exception e)
			{
				ModLog.Error("StatPanel: tooltip failed:\n" + e);
			}
		}

		// The game's stat pop-up for a character: portrait, name, who they
		// are (or what their status means), and the relation scale with the
		// value's step marked.
		private static void ShowPersonTooltip(Row row, RectTransform over)
		{
			Character c = row.Person;
			_tipComp.ClearTooltipWindow();
			int[][] steps = { new[] { -5, -4 }, new[] { -3, -2 }, new[] { -1, 1 }, new[] { 2, 3 }, new[] { 4, 5 } };
			foreach (int[] st in steps)
			{
				_tipComp.SetRow(st[0] + " ... " + st[1], RelationWord(st[0]), row.Shown >= st[0] && row.Shown <= st[1]);
			}
			_tipComp.Title.text = PersonName(c);
			_tipComp.Icon.sprite = row.Icon != null ? row.Icon.sprite : c.DefaultSprite;
			string who = Loc.Game(c.WhoIs.ToString(), "");
			string descr = Usual(row.Status) ? who
				: Loc.Game(c.Name + "." + row.Status, "") + "\n" + Loc.Game(c.Name + "." + row.Status + ".Description", "");
			_tipComp.Description.text = descr.Trim().Length > 0 ? descr.Trim() : "<i>" + Loc.T(Loc.K.NoStatText) + "</i>";
			PlaceTooltip(over);
		}

		private static void PlaceTooltip(RectTransform over)
		{
			// It is taller than the space above the stats, so it goes beside the
			// row - right of it, or left where there is no room - centred
			// on the screen's height, and pushed inside the window.
			RectTransform rt = _tip.GetComponent<RectTransform>();
			rt.pivot = new Vector2(0f, 0f);
			_tip.SetActive(true);
			_tip.transform.SetAsLastSibling();
			LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
			RectTransform box = (RectTransform)TooltipParent;
			Vector3[] c = new Vector3[4];
			over.GetWorldCorners(c);
			Vector2 lo = box.InverseTransformPoint(c[0]);
			Vector2 hi = box.InverseTransformPoint(c[2]);
			Vector2 size = rt.rect.size;
			Rect area = box.rect;
			const float gap = 12f;
			float x = hi.x + gap;
			if (x + size.x > area.xMax - gap)
			{
				x = lo.x - gap - size.x;
			}
			x = Mathf.Clamp(x, area.xMin + gap, area.xMax - gap - size.x);
			// every stat's pop-up at the same height: centred on the screen
			float y = Mathf.Clamp(area.center.y - size.y * 0.5f, area.yMin + gap, area.yMax - gap - size.y);
			_tip.transform.position = box.TransformPoint(new Vector3(x, y, 0f));
		}

		public static void HideTooltip()
		{
			if (_tip != null)
			{
				if (_tipComp != null)
				{
					_tipComp.ClearTooltipWindow();
				}
				_tip.SetActive(false);
			}
		}

		// The game's word for where a value sits on the stat's scale.
		private static string Segment(Parameter p, int value)
		{
			if (p.Scales == null)
			{
				return "";
			}
			for (int i = 0; i < p.Scales.Length; i++)
			{
				if (value >= p.Scales[i].MinValue && value <= p.Scales[i].MaxValue)
				{
					return Loc.Game(p.ParameterName + ".Segment" + (i + 1), "");
				}
			}
			return "";
		}
	}
}
