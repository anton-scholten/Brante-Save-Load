using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using _Scripts.AMVCC.Models.Static;
using _Scripts.AMVCC.Views.Windows;
using _Scripts.AMVCC.Views.Windows.Components;
using _Scripts.AMVCC.Views.Windows.Destiny;

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
			public bool Ghost;                  // met only at the hovered answer or scene
		}

		// What the panel under the tree shows: the stats, the family or the
		// other characters (the game's Family and Relations windows), picked
		// by three tabs at its top.
		public enum Mode { Stats, Family, Relations, Destiny }

		public static Mode Showing = Mode.Stats;
		public static bool DevAllPeople;
		public static bool DevLogHide;     // dev: log who hides the stat pop-up   // dev-pick "allpeople": list everyone a scene can unlock (layout test)
		private const float TabStrip = 40f;   // the tabs' height
		private static RectTransform _area, _content;
		private static SceneEffects.Hero _lastShown, _lastNow;
		private static int _lastChapter;
		private static string _peopleKey;
		// While an answer or a scene title is hovered: the characters the hero
		// would have met by then and has not at the scene shown, drawn as
		// pale "ghost" rows (values from _ghostFrom).
		private static SceneEffects.Hero _ghostFrom;
		private static readonly List<CharacterList> _ghostList = new List<CharacterList>();
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
			// Personality: three groups laid out by chapter, left to right
			// (FlowPlace), with the chapter-2 -> chapter-3 forks drawn
			public bool Flow;
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
		private static List<Vector2> Place(Tab t, int rows, out float columns)
		{
			List<Vector2> flow = FlowPlace(t, rows, out columns);
			if (flow != null)
			{
				return flow;
			}
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

		// The width of the forks between Personality's chapter-2 and
		// chapter-3 columns, in columns.
		private const float Fork = 0.32f;

		// Personality by chapter (CalculateChapterParams): chapter 1's
		// Willpower, Deaths, Perception, Determination; chapter 2's three
		// occupations, each level with the middle of the two chapter-3 skills
		// it makes; then the skills, Perception's above Determination's
		// (Diplomacy = Perception + Nobility, Valor = Determination +
		// Nobility, ...). Null when the groups are not that shape or a column
		// cannot hold them.
		private static List<Vector2> FlowPlace(Tab t, int rows, out float columns)
		{
			columns = 0f;
			if (!t.Flow || t.Groups.Count != 3)
			{
				return null;
			}
			int a = t.Groups[1], b = t.Groups[2] - t.Groups[1], c = t.Stats.Count - t.Groups[2];
			if (c != 2 * b || a > rows || c > rows)
			{
				return null;
			}
			List<Vector2> at = new List<Vector2>(t.Stats.Count);
			for (int i = 0; i < a; i++)
			{
				at.Add(new Vector2(0f, i));
			}
			for (int i = 0; i < b; i++)
			{
				at.Add(new Vector2(1f, 2 * i + 0.5f));
			}
			for (int i = 0; i < c; i++)
			{
				at.Add(new Vector2(2f + Fork, i));
			}
			columns = 3f + Fork;
			return at;
		}

		// The chapter a Personality stat first matters in: the occupations
		// come with chapter 2 (its trigger scenes), the skills with chapter 3
		// (converted from both at its start). The rest: their tab's chapter.
		private static int StatChapter(Parameter p, Tab t)
		{
			int tab = t != null ? t.Chapter : 1;
			switch (p.ParameterName)
			{
				case ParametersList.Discipline:
				case ParametersList.Cunning:
				case ParametersList.Spirituality:
					return Math.Max(tab, 2);
				default:
					return SceneEffects.IsSkill(p.ParameterName) ? Math.Max(tab, 3) : tab;
			}
		}

		private static List<Tab> Tabs()
		{
			List<Tab> tabs = new List<Tab>
			{
				new Tab { Hud = "HUD.Character", Chapter = 1, Takes = new[] { ParameterCategory.CharacterParameter },
					// by chapter (FlowPlace): each occupation forks into the
					// two chapter-3 skills it feeds (CalculateChapterParams:
					// Diplomacy = Curiosity + Discipline, Valor = Activity +
					// Discipline, ...), Curiosity's first
					Flow = true,
					Order = new[] { "Will", "Death", "Curiosity", "Activity",
						"|", "Discipline", "Cunning", "Spirituality",
						"|", "Diplomacy", "Valor", "Manipulation", "Sabotage", "Theology", "Preaching" } },
				new Tab { Hud = "HUD.Home", Chapter = 1, Takes = new[] { ParameterCategory.HomeParameter },
					Order = new[] { "Unity", "Money", "Reputation", "Heir" } },
				new Tab { Hud = "HUD.Empire", Chapter = 2, Takes = new[] { ParameterCategory.EmpireParameter },
					Order = new[] { "State", "Church", "Prosperity", "Order" } },
				new Tab { Hud = "HUD.Inquisition", Chapter = 4, Takes = new[] { ParameterCategory.WorkParameter },
					Order = new[] { "Carier", "Justice", "Authority", "Toleration", "Protest", "Repression" } },
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
			_forks = null;
			_destRows.Clear();
			_destinyKey = null;
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
			else if (Showing == Mode.Destiny)
			{
				_destinyKey = null;   // built by ShowDestiny, for the chapter it shows
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
			_modeBacks.Clear();
			_hoverMode = null;
			Mode[] modes = { Mode.Stats, Mode.Family, Mode.Relations, Mode.Destiny };
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
				// lit under the pointer, so it reads as clickable
				TreeHover th = go.AddComponent<TreeHover>();
				th.OnHover = delegate(bool on)
				{
					_hoverMode = on ? (Mode?)mode : null;
					StyleModeTabs();
				};
				_modeBacks.Add(new KeyValuePair<Mode, Image>(mode, img));
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
				case Mode.Destiny: return Loc.Game("HUD.Destiny", "Destiny");
				default: return Loc.T(Loc.K.StatsTab);
			}
		}

		private static Mode? _hoverMode;
		private static readonly List<KeyValuePair<Mode, Image>> _modeBacks = new List<KeyValuePair<Mode, Image>>();

		private static void StyleModeTabs()
		{
			foreach (KeyValuePair<Mode, TextMeshProUGUI> kv in _modeLabels)
			{
				bool on = kv.Key == Showing;
				bool hover = _hoverMode == kv.Key;
				kv.Value.color = on || hover ? Palette.Accent : Palette.Fade(Palette.Ink, 0.6f);
				kv.Value.fontStyle = on ? FontStyles.Underline : FontStyles.Normal;
			}
			foreach (KeyValuePair<Mode, Image> kv in _modeBacks)
			{
				// the answer boxes' colour behind the tab under the pointer
				kv.Value.color = _hoverMode == kv.Key && kv.Key != Showing ? Palette.Card : Color.clear;
			}
		}

		// dev-pick's "tabhover": hold the pointer's highlight on a tab
		public static void DevHoverTab(Mode m)
		{
			_hoverMode = m;
			StyleModeTabs();
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
			float total = 0f;
			foreach (Tab t in _tabs)
			{
				float c;
				Place(t, rowsFit, out c);
				total += c;
			}
			float subW = width / Mathf.Max(1f, total);
			// spare height goes under the titles, so a locked tab's note has
			// room before its first stat
			float gap = Mathf.Clamp(usable - rowsFit * pitch * scale, 0f, 20f);
			// the rows spread down to the panel's foot (up to 1.6 times the
			// game's spacing) rather than leave the space under them empty
			int rowsUsed = 1;
			foreach (Tab t in _tabs)
			{
				float c;
				foreach (Vector2 v in Place(t, rowsFit, out c))
				{
					rowsUsed = Math.Max(rowsUsed, Mathf.CeilToInt(v.y + 0.01f));
				}
			}
			float step = pitch * scale;
			if (rowsUsed > 1)
			{
				float rowDrawn = (m.Bottom - m.Top) * scale;
				step = Mathf.Clamp((usable - gap - 8f - rowDrawn) / (rowsUsed - 1), pitch * scale, pitch * scale * 1.6f);
			}
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
				float subs;
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
					float sub = at[j].x;
					float r = at[j].y;
					// centred on what it draws (icon to slider end), so a rule
					// between tabs is as far from a slider as from an icon
					rrt.anchoredPosition = new Vector2(
						x + sub * subW + (subW - (contentRight - contentLeft) * scale) * 0.5f - contentLeft * scale,
						-(Header + gap + r * step - contentTop * scale));
					rrt.gameObject.SetActive(true);
					_fitLimit = contentRight;
					FitText(row, _fitLimit);
					AddHover(row, rrt);
					_rows.Add(row);
				}
				if (tab.Flow && FlowPlace(tab, rowsFit, out subs) != null)
				{
					float w = (contentRight - contentLeft) * scale;
					float rowH = (m.Bottom - m.Top) * scale;
					Forks(area, tab, x + subW + (subW + w) * 0.5f, x + (2f + Fork) * subW + (subW - w) * 0.5f,
						r => -(Header + gap + r * step) - rowH * 0.5f, scale);
				}
				x += subs * subW;
			}
		}


		// ---- Destiny ----
		//
		// The game's Destiny window (DestinyWindow): a chapter's story
		// objectives by category - personal life, family, occupation (sides
		// in chapter 5), the family's outcomes, the chapter's outcomes - and
		// for chapters 4-5 only those of the hero's post (Objective.Post).
		// Each one is coloured by where the path stands: happened by the
		// scene shown (green), happening later on the picked/predicted path
		// (pale green), still possible on another path (grey), or out of
		// reach (red): its chapter is over, or every scene/answer that earns
		// it (scene-objectives.tsv) is behind the scene shown or on another
		// post's track.

		// Set by the tree before Show: the hero at the end of the path, the
		// scenes passed by the scene shown (answered), and the post (key
		// parameter "Post") along the path, null when not known.
		public static SceneEffects.Hero DestinyEnd;
		public static HashSet<string> DestinyDone = new HashSet<string>();
		public static string DestinyPost;

		private enum Fate { Earned, Ahead, Open, Lost }

		private static int _destinyPick;      // the chapter picked with the I..V buttons (0: the hero's)
		private static string _destinyKey;    // what the content was built for
		private static readonly List<KeyValuePair<Objective, TextMeshProUGUI>> _destRows
			= new List<KeyValuePair<Objective, TextMeshProUGUI>>();
		private static readonly Dictionary<Objective, Fate> _fates = new Dictionary<Objective, Fate>();

		private static List<Objective> AllObjectives()
		{
			KeyChapterParametersController kc = KeyChapterParametersController.Initiate;
			List<Objective> all = new List<Objective>();
			if (kc != null && kc.AllGameObjectives != null)
			{
				foreach (Objective o in kc.AllGameObjectives)
				{
					if (o != null && !all.Contains(o))
					{
						all.Add(o);
					}
				}
			}
			return all;
		}

		// The objectives the Destiny window lists for `chapter`: the true
		// death sits with each chapter's outcomes from chapter 3 on.
		private static List<Objective> ChapterObjectives(int chapter, string post)
		{
			List<Objective> list = new List<Objective>();
			foreach (Objective o in AllObjectives())
			{
				bool death = o.Name == ObjectiveEnum.ИстиннаяСмерть;
				if (death ? chapter < 3 : o.Chapter != chapter)
				{
					continue;
				}
				if (!string.IsNullOrEmpty(post) && !string.IsNullOrEmpty(o.Post) && !o.Post.Contains(post))
				{
					continue;
				}
				list.Add(o);
			}
			list.Sort((a, b) =>
			{
				int c = CategoryOrder(a.Category).CompareTo(CategoryOrder(b.Category));
				if (c == 0)
				{
					c = a.Order.CompareTo(b.Order);
				}
				return c != 0 ? c : ((int)a.Name).CompareTo((int)b.Name);
			});
			return list;
		}

		private static int CategoryOrder(EventCategoryEnum c)
		{
			switch (c)
			{
				case EventCategoryEnum.Person: return 0;
				case EventCategoryEnum.Family: return 1;
				case EventCategoryEnum.Work: return 2;
				case EventCategoryEnum.FamilyFinal: return 3;
				default: return 4;
			}
		}

		private static string CategoryTitle(EventCategoryEnum c, int chapter)
		{
			switch (c)
			{
				case EventCategoryEnum.Person: return Loc.Game("ObjectivePanelPerson.Title", "Personal Life");
				case EventCategoryEnum.Family: return Loc.Game("ObjectivePanelFamily.Title", "Family");
				case EventCategoryEnum.Work:
					return chapter == 5 ? Loc.Game("ObjectivePanelSide.Title", "Sides") : Loc.Game("ObjectivePanelWork.Title", "Occupation");
				case EventCategoryEnum.FamilyFinal: return Loc.Game("ObjectivePanelFamilyFinals.Title", "Outcomes for the Brante family");
				default:
					return chapter == 3 ? Loc.Game("ObjectivePanelFinalYouth.Title", "Chapter Outcomes") : Loc.Game("ObjectivePanelFinal.Title", "Outcomes of the Chapter");
			}
		}

		private static string ObjectiveKey(Objective o)
		{
			return o.Name == ObjectiveEnum.None ? o.name : o.Name.ToString();
		}

		private static bool Has(SceneEffects.Hero h, Objective o)
		{
			if (o.Name == ObjectiveEnum.ИстиннаяСмерть && h != null && h.Stats != null && h.Stats.Death >= 4)
			{
				return true;   // the pop-up that sets it follows the fourth death
			}
			if (h == null || h.Objectives == null)
			{
				return h != null && h.Live && o.Unlocked;
			}
			return h.Objectives.Contains(o.Name);
		}

		private static Fate FateOf(Objective o, SceneEffects.Hero shown, int heroChapter)
		{
			if (Has(shown, o))
			{
				return Fate.Earned;
			}
			if (Has(DestinyEnd, o))
			{
				return Fate.Ahead;
			}
			bool death = o.Name == ObjectiveEnum.ИстиннаяСмерть;
			if (!death && heroChapter > 0 && o.Chapter < heroChapter)
			{
				return Fate.Lost;   // its chapter is over
			}
			List<KeyValuePair<string, int>> by = SceneObjectives.Earners((int)o.Name);
			if (death || by.Count == 0)
			{
				return Fate.Open;   // earned some other way: no telling
			}
			Dictionary<string, string> keys = null;
			if (!string.IsNullOrEmpty(DestinyPost))
			{
				keys = new Dictionary<string, string> { { "Post", DestinyPost } };
			}
			foreach (KeyValuePair<string, int> e in by)
			{
				int ch = SceneFlow.ChapterOf(e.Key);
				if (DestinyDone.Contains(e.Key) || (ch > 0 && heroChapter > 0 && ch < heroChapter))
				{
					continue;   // played already, with another answer
				}
				if (keys != null && !SceneFlow.SceneFits(e.Key, keys))
				{
					continue;   // another post's track
				}
				return Fate.Open;
			}
			return Fate.Lost;
		}

		private static void ShowDestiny(SceneEffects.Hero shown, int heroChapter)
		{
			int chapter = _destinyPick > 0 ? _destinyPick : Mathf.Clamp(heroChapter, 1, 5);
			List<Objective> list = ChapterObjectives(chapter, DestinyPost);
			string key = chapter + "|" + DestinyPost + "|" + list.Count + "|" + (_content != null ? _content.rect.size.ToString() : "");
			if (key != _destinyKey && _content != null)
			{
				ClearContent();
				BuildDestiny(_content, chapter, heroChapter, list);
				_destinyKey = key;
			}
			_fates.Clear();
			foreach (KeyValuePair<Objective, TextMeshProUGUI> kv in _destRows)
			{
				Fate f = FateOf(kv.Key, shown, heroChapter);
				_fates[kv.Key] = f;
				StyleObjective(kv.Value, f);
			}
		}

		private static Color FateColour(Fate f)
		{
			switch (f)
			{
				case Fate.Earned: return Palette.Good;
				case Fate.Ahead: return Palette.Fade(Palette.Good, 0.6f);
				case Fate.Lost: return Palette.Bad;
				default: return Palette.Grey;
			}
		}

		private static void StyleObjective(TextMeshProUGUI label, Fate f)
		{
			label.color = FateColour(f);
			label.fontStyle = f == Fate.Earned ? FontStyles.Bold : f == Fate.Lost ? FontStyles.Strikethrough : FontStyles.Normal;
			Transform row = label.transform.parent;
			Image back = row.GetComponent<Image>();
			if (back != null)
			{
				back.color = f == Fate.Earned ? Palette.Fade(Palette.Good, 0.22f)
					: f == Fate.Ahead ? Palette.Fade(Palette.Good, 0.10f) : Color.clear;
			}
			Transform icon = row.Find("Icon");
			Image img = icon != null ? icon.GetComponent<Image>() : null;
			if (img != null)
			{
				img.color = new Color(1f, 1f, 1f, f == Fate.Earned ? 1f : f == Fate.Ahead ? 0.8f : 0.4f);
			}
		}

		private const float DestinyBar = 34f;   // the chapter buttons and the legend

		private static void BuildDestiny(RectTransform area, int chapter, int heroChapter, List<Objective> list)
		{
			_destRows.Clear();
			GameObject template = Template();
			float width = area.rect.width, height = area.rect.height;

			// chapter buttons, left
			TextMeshProUGUI word = Label(area, Loc.T(Loc.K.ChapterWord), template);
			word.fontSize = 20f;
			word.color = Palette.Muted;
			word.alignment = TextAlignmentOptions.MidlineLeft;
			Pin(word.rectTransform, new Vector2(8f, 0f), new Vector2(110f, DestinyBar), new Vector2(0f, 1f));
			string[] roman = { "I", "II", "III", "IV", "V" };
			for (int i = 1; i <= 5; i++)
			{
				int ch = i;
				GameObject go = new GameObject("Chapter" + i, typeof(RectTransform));
				go.transform.SetParent(area, false);
				Image img = go.AddComponent<Image>();
				img.color = ch == chapter ? Palette.Card : Color.clear;
				Pin(img.rectTransform, new Vector2(118f + (i - 1) * 50f, -2f), new Vector2(44f, DestinyBar - 4f), new Vector2(0f, 1f));
				Button b = go.AddComponent<Button>();
				b.targetGraphic = img;
				b.transition = Selectable.Transition.None;
				b.onClick.AddListener(delegate
				{
					// the hero's own chapter again: follow it
					_destinyPick = ch == Mathf.Clamp(heroChapter, 1, 5) ? 0 : ch;
					_destinyKey = null;
					HideTooltip();
					if (_lastShown != null)
					{
						Show(_lastShown, _lastNow, _lastChapter);
					}
				});
				TextMeshProUGUI t = Label(go.transform, roman[i - 1], template);
				t.fontSize = 22f;
				t.color = ch == chapter ? Palette.Accent : ch == heroChapter ? Palette.Ink : Palette.Fade(Palette.Ink, 0.55f);
				t.fontStyle = ch == heroChapter ? FontStyles.Underline : FontStyles.Normal;
				Fill(t.rectTransform);
			}

			// the legend, right
			Fate[] fates = { Fate.Earned, Fate.Ahead, Fate.Open, Fate.Lost };
			Loc.K[] words = { Loc.K.DestEarned, Loc.K.DestAhead, Loc.K.DestOpen, Loc.K.DestLost };
			float lx = width - 8f;
			for (int i = fates.Length - 1; i >= 0; i--)
			{
				TextMeshProUGUI t = Label(area, Loc.T(words[i]), template);
				t.fontSize = 18f;
				t.alignment = TextAlignmentOptions.MidlineRight;
				t.overflowMode = TextOverflowModes.Overflow;
				StyleLegend(t, fates[i]);
				float tw = Mathf.Max(40f, t.GetPreferredValues(t.text, 2000f, DestinyBar).x) + 22f;
				Pin(t.rectTransform, new Vector2(lx, 0f), new Vector2(tw, DestinyBar), new Vector2(1f, 1f));
				lx -= tw + 6f;
			}

			if (list.Count == 0)
			{
				return;
			}
			// one column per category, as many rows as the longest needs
			List<EventCategoryEnum> cats = new List<EventCategoryEnum>();
			foreach (Objective o in list)
			{
				if (!cats.Contains(o.Category))
				{
					cats.Add(o.Category);
				}
			}
			int most = 0;
			foreach (EventCategoryEnum c in cats)
			{
				most = Math.Max(most, list.FindAll(o => o.Category == c).Count);
			}
			float top = DestinyBar + 6f;
			float colW = width / cats.Count;
			float rowH = Mathf.Clamp((height - top - 36f) / Math.Max(1, most), 18f, 42f);
			float font = Mathf.Clamp(rowH * 0.55f, 12f, 21f);
			for (int ci = 0; ci < cats.Count; ci++)
			{
				float x = ci * colW;
				if (ci > 0)
				{
					Separator(area, x, top);   // from the category titles down, not through the chapter bar
				}
				TextMeshProUGUI head = Label(area, CategoryTitle(cats[ci], chapter), template);
				head.fontSize = 21f;
				Pin(head.rectTransform, new Vector2(x + 8f, -top), new Vector2(colW - 16f, 32f), new Vector2(0f, 1f));
				int r = 0;
				foreach (Objective o in list)
				{
					if (o.Category != cats[ci])
					{
						continue;
					}
					GameObject row = new GameObject("Objective_" + ObjectiveKey(o), typeof(RectTransform));
					row.transform.SetParent(area, false);
					Image back = row.AddComponent<Image>();
					back.color = Color.clear;
					Pin(back.rectTransform, new Vector2(x + 10f, -(top + 34f + r * rowH)), new Vector2(colW - 20f, rowH - 3f), new Vector2(0f, 1f));
					GameObject ig = new GameObject("Icon", typeof(RectTransform));
					ig.transform.SetParent(row.transform, false);
					Image icon = ig.AddComponent<Image>();
					icon.sprite = o.Icon;
					icon.preserveAspect = true;
					icon.raycastTarget = false;
					icon.enabled = o.Icon != null;
					RectTransform irt = icon.rectTransform;
					irt.anchorMin = new Vector2(0f, 0f);
					irt.anchorMax = new Vector2(0f, 1f);
					irt.pivot = new Vector2(0f, 0.5f);
					irt.offsetMin = new Vector2(4f, 2f);
					irt.offsetMax = new Vector2(4f + rowH - 7f, -2f);
					TextMeshProUGUI name = Label(row.transform, Loc.Game(ObjectiveKey(o) + ".Title", ObjectiveKey(o)), template);
					name.fontSize = font;
					name.alignment = TextAlignmentOptions.MidlineLeft;
					RectTransform nrt = name.rectTransform;
					nrt.anchorMin = Vector2.zero;
					nrt.anchorMax = Vector2.one;
					nrt.offsetMin = new Vector2(rowH + 4f, 0f);
					nrt.offsetMax = new Vector2(-4f, 0f);
					Objective obj = o;
					RectTransform rrt = back.rectTransform;
					TreeHover hover = row.AddComponent<TreeHover>();
					hover.OnHover = delegate(bool on)
					{
						if (on)
						{
							ShowObjectiveTooltip(obj, rrt, name);
						}
						else
						{
							HideTooltip();
						}
					};
					_destRows.Add(new KeyValuePair<Objective, TextMeshProUGUI>(o, name));
					r++;
				}
			}
		}

		private static void StyleLegend(TextMeshProUGUI t, Fate f)
		{
			t.color = FateColour(f);
			t.fontStyle = f == Fate.Earned ? FontStyles.Bold : f == Fate.Lost ? FontStyles.Strikethrough : FontStyles.Normal;
		}

		private static void Fill(RectTransform rt)
		{
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
		}

		// The game's stat pop-up for an objective: icon, title, the game's
		// description, then its conditions as the Destiny window lists them,
		// where it stands on the path and the scenes that bring it.
		private static void ShowObjectiveTooltip(Objective o, RectTransform over, TextMeshProUGUI label)
		{
			try
			{
				if (!EnsureTooltip())
				{
					return;
				}
				_tipComp.ClearTooltipWindow();
				string k = ObjectiveKey(o);
				_tipComp.Title.text = Loc.Game(k + ".Title", k);
				_tipComp.Icon.sprite = o.Icon;
				// everything goes in the description: the pop-up's rows are
				// made for a stat's scale (a short range and a word) and
				// squeezed longer lines into a narrow column
				StringBuilder sb = new StringBuilder();
				Fate f;
				if (_fates.TryGetValue(o, out f))
				{
					sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(FateColour(f))).Append("><b>")
						.Append(Loc.T(f == Fate.Earned ? Loc.K.DestEarned : f == Fate.Ahead ? Loc.K.DestAhead
							: f == Fate.Lost ? Loc.K.DestLost : Loc.K.DestOpen)).Append("</b></color>\n");
				}
				string descr = Loc.Game(k + ".Description", "").Trim();
				sb.Append(descr.Length > 1 ? SceneEffects.NobleParticle(descr) : "<i>" + Loc.T(Loc.K.NoStatText) + "</i>");
				List<string> conds = new List<string>();
				if (o.Condition != null)
				{
					foreach (ObjectiveCondition c in o.Condition)
					{
						string line = (Operand(c.Operand1) + " " + (c.Operator ?? "") + " " + Operand(c.Operand2)).Trim();
						if (line.Length > 0)
						{
							conds.Add(line);
						}
					}
				}
				if (conds.Count > 0)
				{
					sb.Append("\n\n<size=85%>").Append(string.Join("\n", conds.ToArray())).Append("</size>");
				}
				List<string> from = new List<string>();
				foreach (KeyValuePair<string, int> e in SceneObjectives.Earners((int)o.Name))
				{
					string title = SceneTexts.Title(e.Key);
					if (string.IsNullOrEmpty(title))
					{
						continue;
					}
					string answer = e.Value > 0 ? SceneTexts.Name(e.Key, "ButtonDescription" + e.Value) : "";
					string line = "\u2022 " + title + (answer.Length > 0 ? ": " + SceneEffects.NobleParticle(answer) : "");
					if (DestinyDone.Contains(e.Key))
					{
						line = "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Grey) + ">" + line + "</color>";
					}
					if (!from.Contains(line))
					{
						from.Add(line);
					}
					if (from.Count == 6)
					{
						break;
					}
				}
				if (from.Count > 0)
				{
					sb.Append("\n\n<size=85%><b>").Append(Loc.T(Loc.K.DestEarnedBy)).Append(":</b>\n")
						.Append(string.Join("\n", from.ToArray())).Append("</size>");
				}
				_tipComp.Description.text = sb.ToString();
				// beside the entry's words (its row spans the column), level
				// with it, rather than over the next column
				float words = label != null ? label.GetPreferredValues(label.text).x : 0f;
				Vector3 end = label != null
					? label.rectTransform.TransformPoint(new Vector3(label.rectTransform.rect.xMin + words + 10f, 0f, 0f))
					: Vector3.zero;
				PlaceTooltip(over, label != null ? (Vector3?)end : null, true);
			}
			catch (Exception e)
			{
				ModLog.Error("StatPanel: objective tooltip failed:\n" + e);
			}
		}

		// A condition's operand as the Destiny window shows it: a key
		// ("ЗащитникНарода.Title", a stat's name) translated, else as it is.
		private static string Operand(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return "";
			}
			string t = Loc.Game(text, "");
			return t.Trim().Length > 0 && t != text ? SceneEffects.NobleParticle(t) : text;
		}

		// dev-pick's "destiny N": the chapter shown in the Destiny tab (0: the hero's)
		public static void DevDestinyChapter(int chapter)
		{
			_destinyPick = Mathf.Clamp(chapter, 0, 5);
			_destinyKey = null;
			if (_lastShown != null)
			{
				Show(_lastShown, _lastNow, _lastChapter);
			}
		}

		// dev-pick's "destinyhover <objective>": hold that entry's pop-up
		public static void DevHoverObjective(string key)
		{
			foreach (KeyValuePair<Objective, TextMeshProUGUI> kv in _destRows)
			{
				if (ObjectiveKey(kv.Key) == key || kv.Key.name == key)
				{
					DevLogHide = true;
					ShowObjectiveTooltip(kv.Key, (RectTransform)kv.Value.transform.parent, kv.Value);
					ModLog.Info("StatPanel: holding the pop-up of " + key);
					return;
				}
			}
			ModLog.Info("StatPanel: no Destiny entry " + key);
		}

		// dev-pick's "forkhover": hold the pop-up of the first "+" icon
		public static void DevHoverFork()
		{
			DevLogHide = true;
			Transform icon = _forks != null ? _forks.transform.Find("Adds") : null;
			TreeHover th = icon != null ? icon.GetComponent<TreeHover>() : null;
			if (th != null && th.OnHover != null)
			{
				th.OnHover(true);
			}
			ModLog.Info("StatPanel: fork hover " + (th != null ? "held" : "not found"));
		}

		// dev: the Destiny tab's entries and their colours, for the log
		public static string DevDestiny()
		{
			List<string> parts = new List<string>();
			foreach (KeyValuePair<Objective, TextMeshProUGUI> kv in _destRows)
			{
				Fate f;
				parts.Add(ObjectiveKey(kv.Key) + "=" + (_fates.TryGetValue(kv.Key, out f) ? f.ToString() : "?"));
			}
			return "post=" + (DestinyPost ?? "-") + " done=" + DestinyDone.Count + " " + string.Join(", ", parts.ToArray());
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
					Character had;
					if (c != null && (!_people.TryGetValue(c.Name, out had) || c.name.Length < had.name.Length))
					{
						_people[c.Name] = c;   // Gloria, not Gloria2
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
		// hero left out) or everyone else the hero has met (IsUnlocked: a
		// scene's UnlockCharacter, simulated as a `U` row for scenes ahead),
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
					Character c = PersonAsset(m == null ? CharacterList.Hero : m.Character);
					bool forced = DevAllPeople && c != null && SceneEffects.AllUnlockable().Contains(c.name);
					if (m == null || m.Character == CharacterList.Hero || seen.Contains(m.Character) || !(m.IsUnlocked || forced))
					{
						continue;
					}
					if (c == null || c.IsFamily != (Showing == Mode.Family) || (h == now && !forced && ModelOf(shown, m.Character) != null
						&& !ModelOf(shown, m.Character).IsUnlocked))
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
			List<Character> list = sorted.ConvertAll(kv => kv.Value);
			foreach (CharacterList g in _ghostList)
			{
				Character c = PersonAsset(g);
				if (c != null && !list.Contains(c))
				{
					list.Add(c);
				}
			}
			return list;
		}

		// The ghost rows for a hovered answer or scene: who `at` has met that
		// the panel does not list. Rebuilds the rows when that changes.
		public static void Ghosts(SceneEffects.Hero at)
		{
			// only the people tabs list characters (in Destiny this rebuilt
			// the panel as the Relations list under a lit Destiny tab)
			if ((Showing != Mode.Family && Showing != Mode.Relations) || _lastShown == null || at == null || at.Characters == null)
			{
				return;
			}
			List<CharacterList> before = new List<CharacterList>(_ghostList);
			_ghostList.Clear();
			List<Character> listed = People(_lastShown, _lastNow);
			foreach (CharacterModel m in at.Characters)
			{
				Character c = m != null && m.IsUnlocked && m.Character != CharacterList.Hero ? PersonAsset(m.Character) : null;
				if (c != null && c.IsFamily == (Showing == Mode.Family) && !listed.Contains(c))
				{
					_ghostList.Add(m.Character);
				}
			}
			_ghostFrom = at;
			if (_ghostList.Count > 0 || before.Count > 0)
			{
				ShowPeople(_lastShown, _lastNow, _lastChapter);
			}
		}

		private static string PeopleKey(List<Character> people)
		{
			return Showing + ":" + string.Join(",", people.ConvertAll(c => c.Name.ToString()).ToArray())
				+ (_ghostList.Count > 0 ? " ghosts:" + string.Join(",", _ghostList.ConvertAll(g => g.ToString()).ToArray()) : "");
		}

		// The family as the game's family tree (grandfather; father and
		// mother; the children), everyone else in three columns by estate
		// (nobles and the Arkn, clergy, commoners); rows as large as fits,
		// larger than the stat rows since there are fewer of them.
		private static readonly CharacterList[][] FamilyTree =
		{
			new[] { CharacterList.GregorBronte },
			new[] { CharacterList.RobertBronte, CharacterList.LidiaBronte },
			new[] { CharacterList.StefanElBronte, CharacterList.NatanBronte, CharacterList.GloriaMoretti },
		};

		private const float PeopleMaxScale = 1.25f;

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
			_fitLimit = m.Right;
			if (Showing == Mode.Family)
			{
				BuildFamily(area, people, template, m);
			}
			else
			{
				BuildRelations(area, people, template, m);
			}
		}

		// One character's row with what it draws centred on `cx`, its top at `top`.
		private static void PlacePerson(RectTransform area, Character c, GameObject template, Metrics m,
			float scale, float cx, float top)
		{
			Row row = MakePersonRow(c, template);
			if (row == null)
			{
				return;
			}
			RectTransform rrt = RowRoot(row);
			rrt.SetParent(area, false);
			rrt.anchorMin = new Vector2(0f, 1f);
			rrt.anchorMax = new Vector2(0f, 1f);
			rrt.pivot = new Vector2(0f, 1f);
			rrt.sizeDelta = m.Natural;
			rrt.localScale = new Vector3(scale, scale, 1f);
			rrt.anchoredPosition = new Vector2(cx - (m.Left + m.Right) * 0.5f * scale, -(top - m.Top * scale));
			rrt.gameObject.SetActive(true);
			FitText(row, _fitLimit);
			AddHover(row, rrt);
			_rows.Add(row);
		}

		private static void BuildFamily(RectTransform area, List<Character> people, GameObject template, Metrics m)
		{
			// the tree's rows, holding only those the hero has; a family member
			// not in the tree (none today) joins the children
			List<List<Character>> rows = new List<List<Character>>();
			List<Character> placed = new List<Character>();
			foreach (CharacterList[] line in FamilyTree)
			{
				List<Character> r = new List<Character>();
				foreach (CharacterList n in line)
				{
					Character c = people.Find(x => x.Name == n);
					if (c != null)
					{
						r.Add(c);
						placed.Add(c);
					}
				}
				rows.Add(r);
			}
			rows[rows.Count - 1].AddRange(people.FindAll(c => !placed.Contains(c)));
			rows.RemoveAll(r => r.Count == 0);
			int most = 1;
			foreach (List<Character> r in rows)
			{
				most = Mathf.Max(most, r.Count);
			}
			const float top = 6f;
			float width = area.rect.width;
			float drawn = m.Right - m.Left;
			float scale = Mathf.Min(PeopleMaxScale,
				(area.rect.height - top) / Mathf.Max(1, rows.Count) / m.Pitch,
				(width / most - ColumnGap * 3f) / drawn);
			scale = Mathf.Max(0.3f, scale);
			float cell = drawn * scale + ColumnGap * 3f;
			// the spare height shared out equally above, between and below
			// the generations
			float gap = Mathf.Max(0f, (area.rect.height - top - rows.Count * m.Pitch * scale) / (rows.Count + 1));
			for (int r = 0; r < rows.Count; r++)
			{
				int k = rows[r].Count;
				for (int i = 0; i < k; i++)
				{
					float cx = width * 0.5f + (i - (k - 1) * 0.5f) * cell;
					PlacePerson(area, rows[r][i], template, m, scale, cx, top + gap + r * (m.Pitch * scale + gap));
				}
			}
			ModLog.Info("StatPanel: Family " + people.Count + " in " + rows.Count + " generation(s), scale "
				+ scale.ToString("0.00"));
		}

		// Nobles (sword and mantle) and the Arkn; clergy; commoners and the La-Tari.
		private static int EstateColumn(Character c)
		{
			switch (c.Estates)
			{
				case Estates.Cleric: return 1;
				case Estates.Commoner: case Estates.CommonerWoman: case Estates.LaTari: case Estates.None: return 2;
				default: return 0;
			}
		}

		private static string EstateTitle(int column)
		{
			string stat = column == 0 ? "NoblesMans" : column == 1 ? "PriestsMans" : "CommonerMans";
			string fallback = column == 0 ? "Nobles" : column == 1 ? "Clergy" : "Common folk";
			Parameter p = SceneEffects.ParameterAsset(stat);
			return p != null ? Loc.Game(p.ParameterName.ToString(), fallback) : fallback;
		}

		private static void BuildRelations(RectTransform area, List<Character> people, GameObject template, Metrics m)
		{
			List<Character>[] cols = { new List<Character>(), new List<Character>(), new List<Character>() };
			foreach (Character c in people)
			{
				cols[EstateColumn(c)].Add(c);
			}
			const float header = 40f;
			float width = area.rect.width;
			float usable = Mathf.Max(40f, area.rect.height - header);
			float drawn = m.Right - m.Left;
			// the largest scale at which the estates fit side by side, a long
			// one in several sub-columns, each estate as wide as its columns
			float scale = 0.3f;
			int rowsFit = 1;
			for (float sc = PeopleMaxScale; sc >= 0.3f; sc -= 0.02f)
			{
				scale = sc;
				rowsFit = Mathf.Max(1, Mathf.FloorToInt(usable / (m.Pitch * sc)));
				int all = 0;
				foreach (List<Character> l in cols)
				{
					all += Mathf.Max(1, Mathf.CeilToInt(l.Count / (float)rowsFit));
				}
				if (all * (drawn * sc + ColumnGap) <= width)
				{
					break;
				}
			}
			int total = 0;
			foreach (List<Character> l in cols)
			{
				total += Mathf.Max(1, Mathf.CeilToInt(l.Count / (float)rowsFit));
			}
			float x = 0f;
			for (int g = 0; g < 3; g++)
			{
				int subs = Mathf.Max(1, Mathf.CeilToInt(cols[g].Count / (float)rowsFit));
				float third = width * subs / total;
				if (g > 0)
				{
					Separator(area, x);
				}
				TextMeshProUGUI title = Label(area, EstateTitle(g), template);
				RectTransform trt = title.rectTransform;
				trt.anchorMin = new Vector2(0f, 1f);
				trt.anchorMax = new Vector2(0f, 1f);
				trt.pivot = new Vector2(0f, 1f);
				trt.anchoredPosition = new Vector2(x + 8f, 0f);
				trt.sizeDelta = new Vector2(third - 16f, header);
				List<Character> l = cols[g];
				float subW = third / subs;
				for (int j = 0; j < l.Count; j++)
				{
					PlacePerson(area, l[j], template, m, scale, x + (j / rowsFit + 0.5f) * subW,
						header + (j % rowsFit) * m.Pitch * scale);
				}
				x += third;
			}
			ModLog.Info("StatPanel: Relations " + _peopleKey);
			ModLog.Info("StatPanel: Relations " + cols[0].Count + "/" + cols[1].Count + "/" + cols[2].Count
				+ ", " + rowsFit + " rows, scale " + scale.ToString("0.00"));
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
			float columns = 0f;
			foreach (Tab t in tabs)
			{
				float c;
				Place(t, rows, out c);
				if (t.Flow && FlowPlace(t, rows, out c) == null)
				{
					return false;   // Personality by chapter needs its six skills in one column
				}
				columns += c;
			}
			return (m.Right - m.Left) * scale <= width / Mathf.Max(1f, columns) - ColumnGap;
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
		// `Baseline` is green, below red. The baseline is what the shown
		// point would hold without the path's changes - the hero who really
		// played that scene, or today's hero for a scene still ahead - set by
		// the tree (null: `now`). Against today, a chapter-2 pick showed
		// Sophia's -2 green only because she is lower today. Before chapter 3 the
		// six skills are 0; their rows show what the chapter-3 start would
		// make of the stats shown (faded, "Will unlock in Chapter III"), so a
		// pick in chapters 1-2 shows its effect there.
		public static SceneEffects.Hero Baseline;

		public static void Show(SceneEffects.Hero shown, SceneEffects.Hero now, int chapter)
		{
			if (shown == null || now == null)
			{
				return;
			}
			SceneEffects.Hero against = Baseline ?? now;
			_lastShown = shown;
			_lastNow = now;
			_lastChapter = chapter;
			if (Showing == Mode.Destiny)
			{
				ShowDestiny(shown, chapter);
				return;
			}
			if (Showing != Mode.Stats)
			{
				ShowPeople(shown, now, chapter);
				return;
			}
			bool early = chapter > 0 && chapter < 3;
			if (_forks != null)
			{
				// the skills they lead to are a projection until chapter 3
				_forks.GetComponent<CanvasGroup>().alpha = early ? 0.45f : 1f;
			}
			shown = SceneEffects.WithSkillsProjected(shown);
			now = SceneEffects.WithSkillsProjected(against);
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
					? name + "\n<size=58%>" + UnlocksIn(tab.Chapter) + "</size>"
					: name;
				tab.Title.color = locked ? Palette.Fade(Palette.Accent, 0.45f) : Palette.Accent;
			}
			foreach (Row row in _rows)
			{
				bool projected = early && SceneEffects.IsSkill(row.Param.ParameterName);
				int opens = StatChapter(row.Param, row.Tab);
				bool locked = chapter > 0 && opens > chapter;
				CanvasGroup cg = RowRoot(row) != null ? RowRoot(row).GetComponent<CanvasGroup>() : null;
				if (cg != null)
				{
					cg.alpha = projected || locked ? 0.35f : 1f;
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
					// a stat the game has not opened at that chapter says when
					// it will (a whole locked tab says it under its title)
					bool note = projected || locked && (row.Tab == null || row.Tab.Chapter < opens);
					row.Descr.text = note ? UnlocksIn(opens) : Segment(row.Param, value);
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
				row.Ghost = _ghostList.Contains(row.Person.Name);
				CharacterModel m = ModelOf(row.Ghost ? _ghostFrom : shown, row.Person.Name);
				CharacterModel t = ModelOf(Baseline ?? now, row.Person.Name);   // the colour's reference
				int value = m != null ? m.Relation : 0;
				int today = t != null ? t.Relation : value;
				row.Shown = value;
				row.Status = m != null ? m.Status : CharacterStatus.Good;
				bool met = m != null && m.IsUnlocked;
				if (row.Icon != null)
				{
					row.Icon.sprite = Portrait(row.Person, chapter);
				}
				CanvasGroup cg = RowRoot(row) != null ? RowRoot(row).GetComponent<CanvasGroup>() : null;
				if (cg != null)
				{
					cg.alpha = row.Ghost ? 0.3f : !met || Gone(row.Status) ? 0.4f : 1f;
				}
				if (row.Value != null)
				{
					row.Value.text = (value > 0 ? "+" : "") + value;
					row.Value.color = row.Ghost ? Palette.Muted
						: value > today ? Palette.Good : value < today ? Palette.Bad : Palette.Ink;
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
			// a status change: red for a loss or a turn against the hero,
			// green for a gain, a neutral mark otherwise (it was green for
			// anything short of death, Hatred and Betrayed included)
			if (b.Status == a.Status)
			{
				return 0;
			}
			int mood = Mood(b.Status);
			return mood != 0 ? mood : NeutralChange;
		}

		private const int NeutralChange = 1000;

		private static readonly HashSet<CharacterStatus> BadStatus = new HashSet<CharacterStatus>
		{
			CharacterStatus.Dead, CharacterStatus.Dead2, CharacterStatus.Dead3, CharacterStatus.Danger,
			CharacterStatus.Arrested, CharacterStatus.Exile, CharacterStatus.Break, CharacterStatus.Opposition,
			CharacterStatus.Mockery, CharacterStatus.Hatred, CharacterStatus.Despair, CharacterStatus.Anger,
			CharacterStatus.Left, CharacterStatus.Accused, CharacterStatus.Betrayed, CharacterStatus.FriendshipBreak,
			CharacterStatus.Offended, CharacterStatus.Captivity, CharacterStatus.Enemy, CharacterStatus.Mistrust,
			CharacterStatus.Lose, CharacterStatus.Despotism,
		};

		private static readonly HashSet<CharacterStatus> GoodStatus = new HashSet<CharacterStatus>
		{
			CharacterStatus.Good, CharacterStatus.Support, CharacterStatus.Consent, CharacterStatus.Friendship,
			CharacterStatus.Healed, CharacterStatus.Saved, CharacterStatus.Free, CharacterStatus.Fidelity,
			CharacterStatus.Love, CharacterStatus.Union, CharacterStatus.Intimacy, CharacterStatus.Hope,
			CharacterStatus.Patronage, CharacterStatus.Promise, CharacterStatus.Engagement, CharacterStatus.Wedding,
		};

		// -1 a status that is bad for the hero's bond, +1 a good one, 0 neither.
		private static int Mood(CharacterStatus s)
		{
			return BadStatus.Contains(s) ? -1 : GoodStatus.Contains(s) ? 1 : 0;
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
				row.Mark.color = d == NeutralChange ? Palette.Fade(Palette.Muted, 0.28f)
					: Palette.Fade(d > 0 ? Palette.Good : Palette.Bad, 0.28f);
				row.Mark.enabled = true;
			}
		}

		// While an answer (or an event) is hovered, on the Destiny tab: the
		// objectives it earns get a green back, the ones it takes out of
		// reach a red one - every way to earn them is another answer of this
		// scene or is behind the scene shown (DestinyDone). `button` 0: the
		// scene itself (an event). Null scene: clear.
		public static void HighlightDestiny(string scene, int button, SceneEffects.Hero before)
		{
			if (Showing != Mode.Destiny)
			{
				return;
			}
			List<int> earns = scene != null ? new List<int>(SceneObjectives.For(scene, button)) : new List<int>();
			if (scene != null && button > 0)
			{
				earns.AddRange(SceneObjectives.For(scene, 0));   // what the scene earns whatever is answered
			}
			foreach (KeyValuePair<Objective, TextMeshProUGUI> kv in _destRows)
			{
				Objective o = kv.Key;
				Fate f;
				if (!_fates.TryGetValue(o, out f))
				{
					continue;
				}
				StyleObjective(kv.Value, f);
				if (scene == null || f == Fate.Earned || f == Fate.Lost || Has(before, o))
				{
					continue;
				}
				bool brings = earns.Contains((int)o.Name)
					|| (o.Name == ObjectiveEnum.ИстиннаяСмерть && TrueDeathAnswer(scene, button, before));
				bool rulesOut = false;
				if (!brings && button > 0 && o.Name != ObjectiveEnum.ИстиннаяСмерть)
				{
					bool here = false, elsewhere = false;
					foreach (KeyValuePair<string, int> e in SceneObjectives.Earners((int)o.Name))
					{
						if (e.Key == scene)
						{
							here |= e.Value != button && e.Value != 0;
						}
						else if (!DestinyDone.Contains(e.Key))
						{
							elsewhere = true;
						}
					}
					rulesOut = here && !elsewhere;
				}
				if (brings || rulesOut)
				{
					Image back = kv.Value.transform.parent.GetComponent<Image>();
					if (back != null)
					{
						back.color = Palette.Fade(brings ? Palette.Good : Palette.Bad, 0.32f);
					}
				}
			}
		}

		private static bool TrueDeathAnswer(string scene, int button, SceneEffects.Hero before)
		{
			if (button <= 0 || before == null || before.Stats == null)
			{
				return false;
			}
			SceneEffects.Hero after = before.Clone();
			after.Apply(SceneEffects.For(scene, "ButtonDescription" + button));
			return after.Stats.Death >= 4;
		}

		public static void ClearHighlight()
		{
			HighlightDestiny(null, 0, null);
			HighlightChanges(null, null);
			if (_ghostList.Count > 0)
			{
				_ghostList.Clear();
				_ghostFrom = null;
				if ((Showing == Mode.Family || Showing == Mode.Relations) && _lastShown != null)
				{
					ShowPeople(_lastShown, _lastNow, _lastChapter);
				}
			}
		}

		// Personality's forks: each chapter-2 occupation (its row's right end
		// at `xFrom`) splits into the two chapter-3 skills it makes (their
		// left end at `xTo`), the upper arm marked "+" Perception's icon, the
		// lower "+" Determination's - the chapter-1 stat each adds. `rowY`
		// gives a row's middle (rows may be fractional).
		private static GameObject _forks;

		private static void Forks(RectTransform area, Tab tab, float xFrom, float xTo, Func<float, float> rowY, float scale)
		{
			int b = tab.Groups[2] - tab.Groups[1];
			GameObject holder = new GameObject("Forks", typeof(RectTransform));
			holder.transform.SetParent(area, false);
			RectTransform hrt = holder.GetComponent<RectTransform>();
			hrt.anchorMin = Vector2.zero;
			hrt.anchorMax = Vector2.one;
			hrt.offsetMin = Vector2.zero;
			hrt.offsetMax = Vector2.zero;
			holder.AddComponent<CanvasGroup>();   // only the "+" icons take the pointer
			_forks = holder;
			Color ink = Palette.Fade(Palette.Muted, 0.85f);
			const float thick = 2.5f;
			float xs = xFrom + 6f, xe = xTo - 4f;
			float xm = xs + (xe - xs) * 0.12f;
			float head = Mathf.Clamp(14f * scale / 0.7f, 9f, 18f);
			Parameter[] adds = { SceneEffects.ParameterAsset("Curiosity"), SceneEffects.ParameterAsset("Activity") };
			for (int i = 0; i < b; i++)
			{
				float yMid = rowY(2 * i + 0.5f), yUp = rowY(2 * i), yDown = rowY(2 * i + 1);
				Bar(hrt, xs, yMid, xm + thick * 0.5f, yMid, thick, ink);
				Bar(hrt, xm, yUp, xm, yDown, thick, ink);
				for (int k = 0; k < 2; k++)
				{
					float y = k == 0 ? yUp : yDown;
					Bar(hrt, xm - thick * 0.5f, y, xe - head + 1f, y, thick, ink);
					Arrowhead(hrt, xe, y, head, ink);
					// "+ <icon>" over the arm
					float icon = Mathf.Clamp(42f * scale / 0.7f, 24f, 56f);
					// "+" then the icon, clear of the fork's upright
					float cx = xm + 8f + icon * 0.55f + icon * 0.5f;
					GameObject lg = new GameObject("Plus", typeof(RectTransform));
					lg.transform.SetParent(hrt, false);
					TextMeshProUGUI plus = lg.AddComponent<TextMeshProUGUI>();
					GameObject tpl = Template();
					ParameterComponent pc = tpl != null ? tpl.GetComponent<ParameterComponent>() : null;
					if (pc != null && pc.Name != null)
					{
						plus.font = pc.Name.font;
					}
					plus.text = "+";
					plus.fontSize = icon * 0.9f;
					plus.color = ink;
					plus.alignment = TextAlignmentOptions.MidlineRight;
					plus.raycastTarget = false;
					RectTransform prt = plus.rectTransform;
					Pin(prt, new Vector2(cx - icon * 0.5f - 2f, y + icon * 0.5f + 2f), new Vector2(icon * 0.55f, icon), new Vector2(1f, 0.5f));
					if (adds[k] != null && adds[k].Icon != null)
					{
						GameObject ig = new GameObject("Adds", typeof(RectTransform));
						ig.transform.SetParent(hrt, false);
						Image img = ig.AddComponent<Image>();
						img.sprite = adds[k].Icon;
						img.preserveAspect = true;
						img.raycastTarget = true;
						RectTransform irt = img.rectTransform;
						Pin(irt, new Vector2(cx - icon * 0.5f, y + icon * 0.5f + 2f), new Vector2(icon, icon), new Vector2(0f, 0.5f));
						// the stat's own pop-up (its name, scale, value) on hover
						Parameter added = adds[k];
						TreeHover th = ig.AddComponent<TreeHover>();
						th.OnHover = delegate(bool on)
						{
							Row r = _rows.Find(x => x.Param == added);
							if (on && r != null)
							{
								ShowTooltip(r, irt);
							}
							else
							{
								HideTooltip();
							}
						};
					}
				}
			}
		}

		// Anchored at the area's top-left, `pos` the pivot's place.
		private static void Pin(RectTransform rt, Vector2 pos, Vector2 size, Vector2 pivot)
		{
			rt.anchorMin = new Vector2(0f, 1f);
			rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = pivot;
			rt.sizeDelta = size;
			rt.anchoredPosition = pos;
		}

		// A straight line from (x0, y0) to (x1, y1), horizontal or vertical.
		private static void Bar(RectTransform parent, float x0, float y0, float x1, float y1, float thick, Color c)
		{
			GameObject go = new GameObject("Line", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			Image img = go.AddComponent<Image>();
			img.color = c;
			img.raycastTarget = false;
			float w = Mathf.Max(thick, Mathf.Abs(x1 - x0)), h = Mathf.Max(thick, Mathf.Abs(y1 - y0));
			Pin(img.rectTransform, new Vector2(Mathf.Min(x0, x1) - (x0 == x1 ? thick * 0.5f : 0f),
				Mathf.Max(y0, y1) + (y0 == y1 ? thick * 0.5f : 0f)), new Vector2(w, h), new Vector2(0f, 1f));
		}

		private static Sprite _arrow;

		// A right-pointing triangle, its tip at (x, y).
		private static void Arrowhead(RectTransform parent, float x, float y, float size, Color c)
		{
			if (_arrow == null)
			{
				const int n = 32;
				Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
				tex.wrapMode = TextureWrapMode.Clamp;
				Color32[] px = new Color32[n * n];
				for (int yy = 0; yy < n; yy++)
				{
					for (int xx = 0; xx < n; xx++)
					{
						// inside when |y - mid| <= (n - x) / 2, softened over a pixel
						float d = (n - xx - 0.5f) * 0.5f - Mathf.Abs(yy + 0.5f - n * 0.5f);
						byte a = (byte)(Mathf.Clamp01(d + 0.5f) * 255f);
						px[yy * n + xx] = new Color32(255, 255, 255, a);
					}
				}
				tex.SetPixels32(px);
				tex.Apply();
				_arrow = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
			}
			GameObject go = new GameObject("Arrow", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			Image img = go.AddComponent<Image>();
			img.sprite = _arrow;
			img.color = c;
			img.raycastTarget = false;
			Pin(img.rectTransform, new Vector2(x, y), new Vector2(size, size), new Vector2(1f, 0.5f));
		}

		// A light vertical rule between two categories, in the colour of the
		// tree's answer boxes.
		private static void Separator(RectTransform area, float x, float top = 0f)
		{
			GameObject go = new GameObject("Separator", typeof(RectTransform));
			go.transform.SetParent(area, false);
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0f, 0f);
			rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = new Vector2(x, -top * 0.5f);
			rt.sizeDelta = new Vector2(3f, -8f - top);
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
			PlaceTooltip(over, null, false);
		}

		// `rightEnd` (world): where what `over` shows ends, when its box is
		// wider; `level`: next to `over` vertically rather than mid-screen.
		private static void PlaceTooltip(RectTransform over, Vector3? rightEnd, bool level)
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
			if (rightEnd.HasValue)
			{
				hi.x = Mathf.Min(hi.x, ((Vector2)box.InverseTransformPoint(rightEnd.Value)).x);
			}
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
			float mid = level ? (lo.y + hi.y) * 0.5f : area.center.y;
			float y = Mathf.Clamp(mid - size.y * 0.5f, area.yMin + gap, area.yMax - gap - size.y);
			_tip.transform.position = box.TransformPoint(new Vector3(x, y, 0f));
		}

		public static void HideTooltip()
		{
			if (DevLogHide && _tip != null && _tip.activeSelf)
			{
				ModLog.Info("StatPanel: tooltip hidden by\n" + Environment.StackTrace);
			}
			if (_tip != null)
			{
				if (_tipComp != null)
				{
					_tipComp.ClearTooltipWindow();
				}
				_tip.SetActive(false);
			}
		}

		// "Unlocks in Chapter II": shorter than the game's "Will unlock in
		// Chapter II "Adolescence"" (HUD.WillOpenN).
		private static string UnlocksIn(int chapter)
		{
			string[] roman = { "I", "II", "III", "IV", "V" };
			return string.Format(Loc.T(Loc.K.UnlocksIn), chapter >= 1 && chapter <= 5 ? roman[chapter - 1] : chapter.ToString());
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
