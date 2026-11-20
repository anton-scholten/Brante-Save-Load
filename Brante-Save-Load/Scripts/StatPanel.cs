using System;
using System.Collections.Generic;
using System.Reflection;
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
					// Discipline, ...), Curiosity's first; the occupations in
					// the game's own order (Nobility, Spirituality, Ingenuity)
					Flow = true,
					Order = new[] { "Will", "Death", "Curiosity", "Activity",
						"|", "Discipline", "Spirituality", "Cunning",
						"|", "Diplomacy", "Valor", "Theology", "Preaching", "Manipulation", "Sabotage" } },
				new Tab { Hud = "HUD.Home", Chapter = 1, Takes = new[] { ParameterCategory.HomeParameter },
					Order = new[] { "Reputation", "Money", "Unity", "Heir" } },
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
		public static Dictionary<string, int> DestinyPlayed = new Dictionary<string, int>();   // passed scene -> answer played
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
				if (!string.IsNullOrEmpty(post) && !string.IsNullOrEmpty(o.Post) && !o.Post.Contains(post)
					&& !EarnableOn(o, post))
				{
					continue;   // another post's (kept when a scene of this post earns it: Tommas's New Life)
				}
				list.Add(o);
			}
			// in a category, in the order the story can first bring them (the
			// earliest scene that earns one, in flow order); the game's own
			// Order for ties and for those no scene earns, the true death last
			Dictionary<Objective, long> first = new Dictionary<Objective, long>();
			foreach (Objective o in list)
			{
				first[o] = FirstAppearance(o);
			}
			list.Sort((a, b) =>
			{
				int c = CategoryOrder(a.Category).CompareTo(CategoryOrder(b.Category));
				if (c == 0)
				{
					c = first[a].CompareTo(first[b]);
				}
				if (c == 0)
				{
					c = a.Order.CompareTo(b.Order);
				}
				return c != 0 ? c : ((int)a.Name).CompareTo((int)b.Name);
			});
			// the members of an exclusive group together, where the first comes
			List<Objective> grouped = new List<Objective>();
			foreach (Objective o in list)
			{
				if (grouped.Contains(o))
				{
					continue;
				}
				grouped.Add(o);
				int g = ExclusiveGroup(o);
				if (g >= 0)
				{
					grouped.AddRange(list.FindAll(x => x != o && x.Category == o.Category && ExclusiveGroup(x) == g));
				}
			}
			return grouped;
		}

		// Destinies of which at most one can happen, drawn bracketed together
		// (as a scene's answers in the tree). From the game's data:
		// - the chapter-3 paths: the Exam's answers 1/2/3 (030.020.010);
		// - the shields of the faiths: the Religious Dispute's answers 1/2;
		// - the Lotless: Recruitment's answers 1/3 (030.070.030);
		// - the family's fortune: Ruins (Wealth = 0) or Means (Wealth = 10),
		//   Branded by Dishonor (Reputation = 0) or Glorious Bloodline (= 10);
		// - the family in the Revolt: Fled (Escape from the City), or Perished
		//   / Saved (Hearth and Home's answers; it needs not Fled). In the
		//   City is not one of them: Fled can follow it;
		// - the sides: each side's answers need not the other.
		// The chapters' outcomes are one each too, but they are a whole
		// column already titled as such.
		private static readonly string[][] Exclusive =
		{
			new[] { "Noble", "Cleric", "Bourgeois" },
			new[] { "OldFaithDefend", "NewFaithDefend" },
			new[] { "BezudelSafe", "BezudelEnd" },
			new[] { "РазорениеСемьи", "БогатыйРод" },
			new[] { "КлеймоПозора", "СлавныйРод" },
			new[] { "СемьяСбежала", "СемьяПогибла", "СемьяСпасена" },
			new[] { "ЗащитникИмперии", "Мятежник" },
		};

		private static int ExclusiveGroup(Objective o)
		{
			string key = ObjectiveKey(o);
			string bare = key.EndsWith("Objective", StringComparison.Ordinal) ? key.Substring(0, key.Length - 9) : key;
			for (int g = 0; g < Exclusive.Length; g++)
			{
				if (Array.IndexOf(Exclusive[g], o.name) >= 0 || Array.IndexOf(Exclusive[g], key) >= 0
					|| Array.IndexOf(Exclusive[g], bare) >= 0)
				{
					return g;
				}
			}
			return -1;
		}

		private const float BracketW = 20f;
   // a bracketed destiny's indent: the spine and its stub

		// Where `o` can first come in the story: the flow position (chapter,
		// then the chapter's order) of the earliest scene that earns it;
		// long.MaxValue - 1 when no scene does, long.MaxValue for the true death.
		private static long FirstAppearance(Objective o)
		{
			if (o.Name == ObjectiveEnum.ИстиннаяСмерть)
			{
				return long.MaxValue;
			}
			long best = long.MaxValue - 1;
			foreach (KeyValuePair<string, int> e in SceneObjectives.Earners((int)o.Name))
			{
				SceneFlow.Row r = SceneFlow.Find(e.Key);
				if (r != null)
				{
					best = Math.Min(best, (long)r.Chapter * 100000L + r.Order);
				}
			}
			return best;
		}

		// Whether some scene of `post`'s track earns `o` (the game lists
		// Tommas's New Life for the inquisitor only, while the wedding that
		// brings it is on every track).
		private static bool EarnableOn(Objective o, string post)
		{
			Dictionary<string, string> keys = new Dictionary<string, string> { { "Post", post } };
			foreach (KeyValuePair<string, int> e in SceneObjectives.Earners((int)o.Name))
			{
				if (SceneFlow.ChapterOf(e.Key) > 0 && SceneFlow.SceneFits(e.Key, keys))
				{
					return true;
				}
			}
			return false;
		}

		// Whether the game's own condition list for `o` (the one its Destiny
		// window shows) has a part `h` can never meet again: a status for a
		// character who is dead, or an objective it must not have that `h`
		// has. Every part is required except those marked OrCondition, of
		// which one is enough (the window's "or" container: Blood Tide is the
		// ritual and the Nobleman's Sacrament or Gregor >= 2). Other parts
		// can still change.
		private static bool RuledOut(Objective o, SceneEffects.Hero h, int heroChapter, int depth)
		{
			if (o.Condition == null || h == null)
			{
				return false;
			}
			int ors = 0, orsOut = 0;
			foreach (ObjectiveCondition oc in o.Condition)
			{
				bool gone = PartRuledOut(oc.RealCondition, h, heroChapter, depth);
				if (oc.OrCondition)
				{
					ors++;
					orsOut += gone ? 1 : 0;
				}
				else if (gone)
				{
					return true;
				}
			}
			return ors > 0 && orsOut == ors;
		}

		private static bool PartRuledOut(ObjectiveConditions rc, SceneEffects.Hero h, int heroChapter, int depth)
		{
			if (rc == null)
			{
				return false;
			}
			if (rc.ByStatuses != null)
			{
				foreach (_Scripts.Helpers.ConditionByStatus c in rc.ByStatuses)
				{
					if (c == null || c.Character == null)
					{
						continue;
					}
					CharacterModel m = ModelOf(h, c.Character.Name);
					if (m == null || !m.Status.ToString().StartsWith("Dead", StringComparison.Ordinal))
					{
						continue;   // alive: the status can still change
					}
					bool isNow = m.Status == c.Status;
					if (c.Not ? isNow : !isNow)
					{
						return true;
					}
				}
			}
			if (rc.ByObjectives != null)
			{
				foreach (_Scripts.Helpers.ConditionByObjective c in rc.ByObjectives)
				{
					if (c == null || c.Objective == null)
					{
						continue;
					}
					if (c.Not ? Has(h, c.Objective)
						: depth < 1 && !Has(h, c.Objective) && FateOf(c.Objective, h, heroChapter, depth + 1) == Fate.Lost)
					{
						return true;   // must not have it and has it, or needs it and it is out of reach
					}
				}
			}
			return false;
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
			return FateOf(o, shown, heroChapter, 0);
		}

		// `depth`: a condition on another objective asks for that one's fate,
		// one level deep (no objective's list needs more, and a cycle would
		// never end)
		private static Fate FateOf(Objective o, SceneEffects.Hero shown, int heroChapter, int depth)
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
			if (!death && RuledOut(o, shown, heroChapter, depth))
			{
				return Fate.Lost;   // a condition that can no longer be met (a character is dead)
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

		// A destiny row's colour and outline reach this far past the row,
		// left/right and up/down (rows are 3 apart: they just meet).
		private const float RowPadX = 2f, RowPadY = 1.5f;

		private static void Around(RectTransform rt)
		{
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.offsetMin = new Vector2(-RowPadX, -RowPadY);
			rt.offsetMax = new Vector2(RowPadX, RowPadY);
		}

		private static Image RowFill(Transform row)
		{
			Transform f = row != null ? row.Find("Fill") : null;
			return f != null ? f.GetComponent<Image>() : null;
		}

		private static void StyleObjective(TextMeshProUGUI label, Fate f)
		{
			label.color = FateColour(f);
			label.fontStyle = f == Fate.Earned ? FontStyles.Bold : f == Fate.Lost ? FontStyles.Strikethrough : FontStyles.Normal;
			Transform row = label.transform.parent;
			Image back = RowFill(row);
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
				Dictionary<int, List<float>> brackets = new Dictionary<int, List<float>>();   // group -> row centres
				foreach (Objective o in list)
				{
					if (o.Category != cats[ci])
					{
						continue;
					}
					int g = ExclusiveGroup(o);
					if (g >= 0 && list.FindAll(q => q.Category == o.Category && ExclusiveGroup(q) == g).Count < 2)
					{
						g = -1;   // alone in this list (another post's are left out)
					}
					float indent = g >= 0 ? BracketW : 0f;
					if (g >= 0)
					{
						if (!brackets.ContainsKey(g))
						{
							brackets[g] = new List<float>();
						}
						brackets[g].Add(-(top + 34f + r * rowH + (rowH - 3f) * 0.5f));
					}
					GameObject row = new GameObject("Objective_" + ObjectiveKey(o), typeof(RectTransform));
					row.transform.SetParent(area, false);
					Image back = row.AddComponent<Image>();
					back.color = Color.clear;
					Pin(back.rectTransform, new Vector2(x + 10f + indent, -(top + 34f + r * rowH)), new Vector2(colW - 20f - indent, rowH - 3f), new Vector2(0f, 1f));
					// the fate/hover colour, a little bigger than the row (the
					// row's own image stays the clear hover target)
					GameObject fg = new GameObject("Fill", typeof(RectTransform));
					fg.transform.SetParent(row.transform, false);
					Image fill = fg.AddComponent<Image>();
					fill.color = Color.clear;
					fill.raycastTarget = false;
					Around(fill.rectTransform);
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
							MarkRequired(RequiredBy(obj));
						}
						else
						{
							HideTooltip();
							MarkRequired(null);
						}
					};
					_destRows.Add(new KeyValuePair<Objective, TextMeshProUGUI>(o, name));
					r++;
				}
				// each exclusive group's bracket: a spine down its rows' left,
				// a stub to each, in the tree's pale grey
				foreach (List<float> ys in brackets.Values)
				{
					float sx = x + 10f + 6f;
					Color pale = Palette.Fade(Palette.Muted, 0.55f);
					Bar(area, sx, ys[0], sx, ys[ys.Count - 1], 3f, pale);
					foreach (float y in ys)
					{
						Bar(area, sx - 1.5f, y, x + 10f + BracketW - 2f, y, 3f, pale);
					}
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
				string state = "";
				if (_fates.TryGetValue(o, out f))
				{
					state = "<color=#" + ColorUtility.ToHtmlStringRGB(FateColour(f)) + "><b>"
						+ Loc.T(f == Fate.Earned ? Loc.K.DestEarned : f == Fate.Ahead ? Loc.K.DestAhead
							: f == Fate.Lost ? Loc.K.DestLost : Loc.K.DestOpen) + "</b></color>";
					sb.Append(state).Append("\n");
				}
				string descr = Loc.Game(k + ".Description", "").Trim();
				descr = descr.Length > 1 ? SceneEffects.NobleParticle(descr) : "<i>" + Loc.T(Loc.K.NoStatText) + "</i>";
				sb.Append(descr);
				// as the Destiny window lists them: the required parts, then
				// the OrCondition ones under "One of"
				List<string> conds = new List<string>();
				List<string> ors = new List<string>();
				List<Sprite> condIcons = new List<Sprite>();
				List<Sprite> orIcons = new List<Sprite>();
				HashSet<string> required = new HashSet<string>();   // plain text of the required parts
				if (o.Condition != null)
				{
					foreach (ObjectiveCondition c in o.Condition)
					{
						if (LeftOut.Contains((c.Operand1 ?? "").Trim()))
						{
							continue;   // a stat the panel leaves out (Heir): nothing to look up
						}
						if (ShownByComesFrom(o, c.RealCondition))
						{
							continue;   // "Kiss the Sword": the answer is under Comes from
						}
						string op = (c.Operator ?? "").Trim();
						string line = (Operand(c.Operand1) + " " + op + " " + Operand(c.Operand2)).Trim();
						if (string.IsNullOrEmpty(c.Operand2) && (op == "!=" || op == "\u2260"))
						{
							// "The Family in Ruins !=": the objective must not be had
							line = Loc.T(Loc.K.NotWord) + " " + Operand(c.Operand1);
						}
						if (!c.OrCondition)
						{
							required.Add(Plain(line));
						}
						if (line.Length > 0 && f != Fate.Earned && PartRuledOut(c.RealCondition, _lastShown, _lastChapter, 0))
						{
							line = Struck(line);   // can no longer be met: the options left stand out
						}
						else if (line.Length > 0 && PartMet(c, _lastShown) == true)
						{
							line = Met(line);   // already true for the hero shown
						}
						if (line.Length > 0)
						{
							(c.OrCondition ? ors : conds).Add(line);
							(c.OrCondition ? orIcons : condIcons).Add(c.Icon ?? ConditionIcon(c.RealCondition) ?? NamedIcon(c.Operand1));
						}
					}
				}
				if (ors.Count == 1)
				{
					conds.AddRange(ors);   // a lone "or" is simply required
					condIcons.AddRange(orIcons);
					ors.Clear();
					orIcons.Clear();
				}
				List<string> wideConds = new List<string>(conds), wideOrs = new List<string>(ors);
				if (ors.Count > 0)
				{
					conds.Add("<i>" + Loc.T(Loc.K.OneOf) + ":</i> " + string.Join(" / ", ors.ToArray()));
				}
				string requires = "", comes = "";
				if (conds.Count > 0)
				{
					// headed like "Comes from:" below, so the two lists read apart
					requires = "<b>" + Loc.T(Loc.K.Requires) + ":</b>\n" + string.Join("\n", conds.ToArray());
					sb.Append("\n\n<size=85%>").Append(requires).Append("</size>");
				}
				List<FromLine> fromLines = EarnedBy(o, _fates.TryGetValue(o, out f) ? f : Fate.Open, required);
				List<string> from = fromLines.ConvertAll(l => (l.Option ? OptionMark : "") + l.Styled(l.Text()));
				if (from.Count > 0)
				{
					comes = "<b>" + Loc.T(Loc.K.DestEarnedBy) + ":</b>\n"
						+ string.Join("\n", from.ConvertAll(l => l.Replace(OptionMark, "    \u2013 ")).ToArray());
					sb.Append("\n\n<size=85%>").Append(comes).Append("</size>");
				}
				_lastTipText = sb.ToString();
				if (ShowWideTip(o, Loc.Game(k + ".Title", k), state, descr, wideConds, condIcons, wideOrs, orIcons, fromLines))
				{
					return;   // over the tree, out of the way of the Destiny list
				}
				_tipComp.Description.text = _lastTipText;
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

		// "Comes from": one line per scene that earns the objective, grouped by
		// the scene's title (two versions of a scene - 051.060.030 /
		// 051.070.035 To the Death - read the same). A scene that earns it
		// by itself or with every answer (Father's Sword, The Ant Farm) is
		// its title alone; listed per answer, both answers read as the same
		// line twice. Otherwise each earning answer, with what it needs
		// (Last Respects: the Will destiny, or Nobility >= 6 or Gregor >= 2).
		// Grey: the scene is behind the scene shown.
		private static List<FromLine> EarnedBy(Objective o, Fate fate, HashSet<string> required)
		{
			List<string> titles = new List<string>();
			Dictionary<string, List<KeyValuePair<string, int>>> byTitle = new Dictionary<string, List<KeyValuePair<string, int>>>();
			foreach (KeyValuePair<string, int> e in SceneObjectives.Earners((int)o.Name))
			{
				string title = SceneTexts.Title(e.Key);
				if (string.IsNullOrEmpty(title))
				{
					continue;
				}
				List<KeyValuePair<string, int>> list;
				if (!byTitle.TryGetValue(title, out list))
				{
					list = byTitle[title] = new List<KeyValuePair<string, int>>();
					titles.Add(title);
				}
				list.Add(e);
			}
			List<FromLine> lines = new List<FromLine>();
			foreach (string title in titles)
			{
				List<KeyValuePair<string, int>> list = byTitle[title];
				bool done = false;
				bool whole = false;
				bool open = false;
				foreach (KeyValuePair<string, int> e in list)
				{
					bool passed = DestinyDone.Contains(e.Key) || PastChapter(e.Key);
					done |= passed;
					whole |= e.Value <= 0 || WholeScene(e.Key, list);
					open |= !passed && OnTrack(e.Key);
				}
				// earned: the scenes behind are grey; not earned, a scene
				// passed (or on another post's track) is struck, so the ways
				// left to earn it stand out
				int style = fate == Fate.Earned ? (done ? 1 : 0) : open ? 0 : 2;
				// the way it came (or the scene that brought it by itself) on
				// the path played: green
				bool playedHere = false;
				foreach (KeyValuePair<string, int> e in list)
				{
					int b;
					playedHere |= DestinyDone.Contains(e.Key) && (e.Value <= 0
						|| (DestinyPlayed.TryGetValue(e.Key, out b) && (b == e.Value || WholeScene(e.Key, list))));
				}
				List<FromLine> mine = new List<FromLine>();
				if (whole)
				{
					mine.Add(new FromLine { Head = "\u2022 " + title });
				}
				else
				{
					// one answer: "Scene: answer (needs)"; several: the scene,
					// then each answer as an option (bracketed). A need the
					// destiny itself requires is left to Requires.
					List<FromLine> answers = new List<FromLine>();
					foreach (KeyValuePair<string, int> e in list)
					{
						string choice = "ButtonDescription" + e.Value;
						int took;
						FromLine a = new FromLine { Head = SceneEffects.NobleParticle(SceneTexts.Name(e.Key, choice)),
							Played = DestinyDone.Contains(e.Key) && DestinyPlayed.TryGetValue(e.Key, out took) && took == e.Value };
						NeedParts(SceneEffects.For(e.Key, choice), required, a);
						if (a.Text().Length > 0 && !answers.Exists(x => x.Text() == a.Text()))
						{
							answers.Add(a);
						}
					}
					if (answers.Count == 1)
					{
						answers[0].Head = "\u2022 " + title + ": " + answers[0].Head;
						mine.Add(answers[0]);
					}
					else
					{
						mine.Add(new FromLine { Head = "\u2022 " + title + ":", Played = answers.Exists(x => x.Played) });
						foreach (FromLine a in answers)
						{
							a.Option = true;
							mine.Add(a);
						}
					}
				}
				foreach (FromLine l in mine)
				{
					l.Style = style;
					if (l.Played)
					{
						l.Style = 3;
					}
					lines.Add(l);
				}
				if (whole && playedHere)
				{
					mine[0].Style = 3;
				}
				if (lines.Count >= 10)
				{
					break;
				}
			}
			return lines;
		}

		// One "Comes from" line: its head ("• Scene: answer"), what the
		// answer needs (icon, text, what follows it: ", " or " or "), its
		// style (0 plain, 1 grey: earned and behind, 2 struck: out of reach)
		// and whether it is one of several answers of the scene above.
		private sealed class FromLine
		{
			public bool Option;
			public bool Played;   // the answer taken on the path played (or its scene's head)
			public string Head = "";
			public readonly List<bool> NeedMet = new List<bool>();   // each need met by the hero shown
			public readonly List<Sprite> Icons = new List<Sprite>();
			public readonly List<string> Needs = new List<string>();
			public readonly List<string> Seps = new List<string>();
			public int Style;

			public string Text()
			{
				if (Needs.Count == 0)
				{
					return Head;
				}
				StringBuilder sb = new StringBuilder(Head).Append(" (");
				for (int i = 0; i < Needs.Count; i++)
				{
					sb.Append(Needs[i]).Append(i < Needs.Count - 1 ? Seps[i] : "");
				}
				return sb.Append(")").ToString();
			}

			public string Styled(string text)
			{
				return Style == 1 ? "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Grey) + ">" + text + "</color>"
					: Style == 2 ? Struck(text) : Style == 3 ? Met(text) : text;
			}
		}

		// A requirement's text without tags, the game's signs, lower case:
		// to compare an answer's need with the destiny's own requirements.
		private static string Plain(string text)
		{
			string t = System.Text.RegularExpressions.Regex.Replace(text ?? "", "<[^>]+>", "");
			return t.Replace(">=", "\u2265").Replace("<=", "\u2264").Trim().ToLowerInvariant();
		}

		// Marks a "Comes from" line that is one of several answers of the
		// scene above it (drawn under a bracket in the wide pop-up).
		private const string OptionMark = "\t";

		// Whether a condition part only names the answer that earns the
		// destiny - a key parameter ("KissTheSword") that only the earning
		// answers set (scene-params.tsv): Comes from lists them already.
		// One set elsewhere too ("GirlsMeet") is a requirement of its own.
		// "Accept True Death" is set by no scene; every answer that earns
		// the true death sets Deaths to 4, so "Deaths = 4" says it all.
		private static bool ShownByComesFrom(Objective o, ObjectiveConditions rc)
		{
			if (rc == null || rc.ByKeyParameters == null)
			{
				return false;
			}
			bool any = false;
			foreach (var kp in rc.ByKeyParameters)
			{
				string name = KeyParamName(kp);
				if (string.IsNullOrEmpty(name))
				{
					return false;
				}
				if (o.Name == ObjectiveEnum.ИстиннаяСмерть && name == "ПринятьИстиннуюСмерть")
				{
					any = true;
					continue;
				}
				List<KeyValuePair<string, int>> setters = SceneParams.Setters(name);
				if (setters.Count == 0)
				{
					return false;
				}
				List<KeyValuePair<string, int>> earners = SceneObjectives.Earners((int)o.Name);
				foreach (KeyValuePair<string, int> st in setters)
				{
					if (!earners.Contains(st))
					{
						return false;
					}
				}
				any = true;
			}
			return any;
		}

		// A ByKeyParameters entry's key parameter name (its KeyParameter field,
		// an enum or an asset).
		private static string KeyParamName(object kp)
		{
			if (kp == null)
			{
				return null;
			}
			FieldInfo fi = kp.GetType().GetField("KeyParameter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			object v = fi != null ? fi.GetValue(kp) : null;
			if (v == null)
			{
				return null;
			}
			UnityEngine.Object uo = v as UnityEngine.Object;
			return uo != null ? uo.name : v.ToString();
		}

		// A requirement already met (an answer chosen, a destiny earned): green.
		private static string Met(string line)
		{
			return "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Good) + ">" + line + "</color>";
		}

		// Whether `h` meets a condition part (its RealCondition, every test
		// in it; else its operands when they name a stat): null when it
		// cannot be told (a key parameter, nothing to test).
		private static bool? PartMet(ObjectiveCondition c, SceneEffects.Hero h)
		{
			if (h == null)
			{
				return null;
			}
			List<SceneEffects.Effect> tests = new List<SceneEffects.Effect>();
			ObjectiveConditions rc = c.RealCondition;
			bool unknown = false;
			if (rc != null)
			{
				if (rc.ByParameters != null)
				{
					foreach (var p in rc.ByParameters)
					{
						if (p == null || p.ParamName == null || p.ItsDiapason)
						{
							unknown = true;
							continue;
						}
						tests.Add(new SceneEffects.Effect { Kind = 'Q', Name = p.ParamName.name, Op = (int)p.Operations, Value = p.SecondValue });
					}
				}
				if (rc.ByRelations != null)
				{
					foreach (_Scripts.Helpers.ConditionByRelation r in rc.ByRelations)
					{
						if (r != null && r.Character != null)
						{
							tests.Add(new SceneEffects.Effect { Kind = 'W', Name = r.Character.name, Op = (int)r.Operations, Value = r.Value });
						}
					}
				}
				if (rc.ByStatuses != null)
				{
					foreach (_Scripts.Helpers.ConditionByStatus st in rc.ByStatuses)
					{
						if (st != null && st.Character != null)
						{
							tests.Add(new SceneEffects.Effect { Kind = 'T', Name = st.Character.name, Op = st.Not ? 1 : 0, Value = (int)st.Status });
						}
					}
				}
				if (rc.ByObjectives != null)
				{
					foreach (_Scripts.Helpers.ConditionByObjective ob in rc.ByObjectives)
					{
						if (ob != null && ob.Objective != null)
						{
							bool has = Has(h, ob.Objective);
							if (ob.Not ? has : !has)
							{
								return false;
							}
							tests.Add(null);   // counted as tested
						}
					}
				}
				if (rc.ByKeyParameters != null && rc.ByKeyParameters.Count > 0)
				{
					unknown = true;
				}
			}
			if (tests.Count == 0 && !unknown)
			{
				// no typed test (Sophia Saved's Willpower): the operands
				Parameter p = SceneEffects.ParameterAsset((c.Operand1 ?? "").Trim());
				int v;
				int op = OpOf((c.Operator ?? "").Trim());
				if (p != null && op >= 0 && int.TryParse((c.Operand2 ?? "").Trim(), out v))
				{
					tests.Add(new SceneEffects.Effect { Kind = 'Q', Name = p.name, Op = op, Value = v });
				}
			}
			if (tests.Count == 0)
			{
				return null;
			}
			foreach (SceneEffects.Effect e in tests)
			{
				if (e == null)
				{
					continue;
				}
				bool? m = h.Meets(e);
				if (m == false)
				{
					return false;
				}
				unknown |= m == null;
			}
			return unknown ? (bool?)null : true;
		}

		// A shown operator as the game's Operations number (-1: unknown).
		private static int OpOf(string op)
		{
			switch (op)
			{
				case ">": return 0;
				case "<": return 1;
				case "=": case "==": return 2;
				case ">=": case "\u2265": return 3;
				case "<=": case "\u2264": return 4;
				case "!=": case "\u2260": return 5;
				default: return -1;
			}
		}

		// A line that can no longer happen: struck, in the "out of reach" red.
		private static string Struck(string line)
		{
			// the colour first: TextMesh Pro draws the line in the colour
			// current at <s> (inside it, the line came out black)
			return "<color=#" + ColorUtility.ToHtmlStringRGB(FateColour(Fate.Lost)) + "><s>" + line + "</s></color>";
		}

		// Whether `scene`'s chapter is over for the hero shown.
		private static bool PastChapter(string scene)
		{
			int ch = SceneFlow.ChapterOf(scene);
			return ch > 0 && _lastChapter > 0 && ch < _lastChapter;
		}

		// Whether `scene` is on the post's track along the path (unknown post: yes).
		private static bool OnTrack(string scene)
		{
			return string.IsNullOrEmpty(DestinyPost)
				|| SceneFlow.SceneFits(scene, new Dictionary<string, string> { { "Post", DestinyPost } });
		}

		// Whether the earning answers of `scene` in `list` are all its answers.
		private static bool WholeScene(string scene, List<KeyValuePair<string, int>> list)
		{
			List<KeyValuePair<string, string>> buttons = SceneTexts.Buttons(scene);
			if (buttons.Count == 0)
			{
				return false;
			}
			foreach (KeyValuePair<string, string> b in buttons)
			{
				int n = SceneParams.ButtonOf(b.Key);
				if (!list.Exists(e => e.Key == scene && e.Value == n))
				{
					return false;
				}
			}
			return true;
		}

		// An answer's requirements into `into`: the "and" ones joined by
		// commas, then the "or" group joined by "or", each with its icon;
		// those in `skip` (the destiny's own requirements) left out.
		private static void NeedParts(List<SceneEffects.Effect> effects, HashSet<string> skip, FromLine into)
		{
			bool prevOr = false;
			foreach (bool orGroup in new[] { false, true })
			{
				foreach (SceneEffects.Effect e in effects)
				{
					if (!e.IsRequirement || e.Or != orGroup || (e.Kind == 'Q' && LeftOut.Contains(e.Name)))
					{
						continue;
					}
					Sprite icon;
					// the game's own signs, as in the condition lines
					string text = SceneEffects.Label(e, out icon).Replace(">=", "\u2265").Replace("<=", "\u2264");
					if (!orGroup && skip != null && skip.Contains(Plain(text)))
					{
						continue;   // Requires says it already
					}
					if (into.Needs.Count > 0)
					{
						// between two of the "or" group: "or"; else a comma
						into.Seps[into.Seps.Count - 1] = orGroup && prevOr ? " " + Loc.T(Loc.K.OrWord) + " " : ", ";
					}
					into.Icons.Add(icon);
					into.Needs.Add(text);
					into.Seps.Add("");
					into.NeedMet.Add(_lastShown != null && _lastShown.Meets(e) == true);
					prevOr = orGroup;
				}
			}
		}

		// dev-pick's "destinyall": every chapter's entries with their pop-up
		// text, tags stripped, for the log
		// A dev view of an object's fields, two levels deep, lists expanded.
		private static string DevDescribe(object o, int depth)
		{
			if (o == null)
			{
				return "null";
			}
			if (o is string || o.GetType().IsPrimitive || o.GetType().IsEnum)
			{
				return o.ToString();
			}
			if (o is UnityEngine.Object)
			{
				return ((UnityEngine.Object)o).name;
			}
			if (depth > 2)
			{
				return o.GetType().Name;
			}
			System.Collections.IEnumerable list = o as System.Collections.IEnumerable;
			if (list != null)
			{
				List<string> items = new List<string>();
				foreach (object x in list)
				{
					items.Add(DevDescribe(x, depth + 1));
				}
				return items.Count == 0 ? "" : "[" + string.Join("; ", items.ToArray()) + "]";
			}
			List<string> parts = new List<string>();
			foreach (FieldInfo fi in o.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
			{
				string v = DevDescribe(fi.GetValue(o), depth + 1);
				if (v.Length > 0 && v != "null")
				{
					parts.Add(fi.Name + "=" + v);
				}
			}
			return "{" + string.Join(", ", parts.ToArray()) + "}";
		}

		public static void DevDumpDestinies()
		{
			int keep = _destinyPick;
			foreach (Objective o in AllObjectives())
			{
				ModLog.Info("DEV DESTINY meta " + ObjectiveKey(o) + ": chapter " + o.Chapter + ", category " + o.Category
					+ ", post '" + o.Post + "', order " + o.Order);
				if (o.Condition == null)
				{
					continue;
				}
				foreach (ObjectiveCondition c in o.Condition)
				{
					ModLog.Info("DEV DESTINY cond " + ObjectiveKey(o) + ": '" + c.Operand1 + "' '" + c.Operator + "' '" + c.Operand2
						+ "'" + (c.OrCondition ? " OR" : "") + " | " + DevDescribe(c.RealCondition, 0));
				}
			}
			for (int ch = 1; ch <= 5; ch++)
			{
				DevDestinyChapter(ch);
				foreach (KeyValuePair<Objective, TextMeshProUGUI> kv in _destRows)
				{
					ShowObjectiveTooltip(kv.Key, (RectTransform)kv.Value.transform.parent, kv.Value);
					string text = _lastTipText ?? "";
					text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "").Replace("\n", " | ");
					ModLog.Info("DEV DESTINY ch" + ch + " " + ObjectiveKey(kv.Key) + ": " + text);
				}
			}
			HideTooltip();
			DevDestinyChapter(keep);
		}

		// A condition's operand as the Destiny window shows it: a key
		// ("ЗащитникНарода.Title", a stat's name) translated, else as it is.
		private static string Operand(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return "";
			}
			// an objective is named by its key ("WillObjective"): its title
			foreach (string key in new[] { text, text + ".Title" })
			{
				string t = Loc.Game(key, "");
				if (t.Trim().Length > 0 && t != key)
				{
					return SceneEffects.NobleParticle(t);
				}
			}
			return text;
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
					HashSet<Objective> req = RequiredBy(kv.Key);
					MarkRequired(req);
					ModLog.Info("StatPanel: holding the pop-up of " + key + "; needs "
						+ string.Join(", ", new List<Objective>(req).ConvertAll(ObjectiveKey).ToArray()));
					return;
				}
			}
			ModLog.Info("StatPanel: no Destiny entry " + key);
		}

		// dev-pick's "rowhover <name>": hold the pop-up of a stat or person
		// row (asset or enum name)
		public static void DevHoverRow(string name)
		{
			foreach (Row row in _rows)
			{
				string a = row.Person != null ? row.Person.Name.ToString() : row.Param != null ? row.Param.name : "";
				string b = row.Person != null ? row.Person.name : row.Param != null ? row.Param.ParameterName.ToString() : "";
				RectTransform root = RowRoot(row);
				if ((a == name || b == name) && root != null)
				{
					DevLogHide = true;
					ShowTooltip(row, root);
					ModLog.Info("StatPanel: holding the pop-up of row " + name + ": "
						+ (_tipComp != null ? _tipComp.Description.text.Replace("\n", " | ") : ""));
					return;
				}
			}
			ModLog.Info("StatPanel: no row " + name);
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
					if (!forced && !RelationCounts(c, shown, now))
					{
						continue;   // met, but nothing on this track changes how they see the hero
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

		// Whether the Relations tab lists `c` (the family always): a relation
		// that is not 0 for either hero, or a scene not yet passed on the
		// hero's track that changes it (scene-effects.tsv `R` rows). The game
		// meets Egmont on every track, but only the rebel's and the judge's
		// scenes change how he sees the hero.
		private static bool RelationCounts(Character c, SceneEffects.Hero shown, SceneEffects.Hero now)
		{
			if (c == null || c.IsFamily)
			{
				return true;
			}
			foreach (SceneEffects.Hero h in new[] { shown, now })
			{
				CharacterModel m = ModelOf(h, c.Name);
				if (m != null && m.Relation != 0)
				{
					return true;
				}
			}
			foreach (string scene in SceneEffects.RelationScenes(c.name))
			{
				if (!DestinyDone.Contains(scene) && OnTrack(scene))
				{
					return true;
				}
			}
			return false;
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
				if (c != null && c.IsFamily == (Showing == Mode.Family) && !listed.Contains(c)
					&& RelationCounts(c, at, null))
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

		private const float MarkPadX = 8f;      // a change mark past the row's drawn ends
		private const float MarkPadYAlone = 6f; // and above/below where no row is next to it
		private const float MarkPadYMax = 8f;   // never more than this

		// (below, above), in `root`'s units: half the space between what this
		// row draws and what the next row in its column draws.
		private static Vector2 MarkPadY(RectTransform root)
		{
			float ls = Mathf.Max(0.0001f, root.lossyScale.y);
			Vector3[] c = new Vector3[4];
			root.GetWorldCorners(c);
			float left = c[0].x;
			float bottom = c[0].y + _hoverInset.y * ls, top = c[1].y - _hoverInset.x * ls;
			float below = float.MaxValue, above = float.MaxValue;
			foreach (RectTransform other in _roots.Values)
			{
				if (other == null || other == root || !other.gameObject.activeInHierarchy)
				{
					continue;
				}
				other.GetWorldCorners(c);
				if (Mathf.Abs(c[0].x - left) > 2f * ls)
				{
					continue;   // another column
				}
				float oBottom = c[0].y + _hoverInset.y * ls, oTop = c[1].y - _hoverInset.x * ls;
				if (oTop <= bottom)
				{
					below = Mathf.Min(below, bottom - oTop);
				}
				else if (oBottom >= top)
				{
					above = Mathf.Min(above, oBottom - top);
				}
			}
			// at most MarkPadYMax: rows set far apart (Ingenuity under
			// Spirituality, Nathan under Gregor) are not stretched to meet
			return new Vector2(below == float.MaxValue ? MarkPadYAlone : Mathf.Min(MarkPadYMax, below * 0.5f / ls),
				above == float.MaxValue ? MarkPadYAlone : Mathf.Min(MarkPadYMax, above * 0.5f / ls));
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
					// the fourth death is the true death; the stat's scale
					// stops at the third ("Died thrice")
					bool trueDeath = row.Param.ParameterName == ParametersList.Death && value >= 4;
					row.Descr.text = note ? UnlocksIn(opens)
						: trueDeath ? Loc.Game("TrueDeath_Title", "True Death") : Segment(row.Param, value);
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
					OverDrawn(rt, MarkPadX);   // the stat only, not the rest of its box
					// down and up to halfway to the rows above and below, so
					// marked neighbours meet
					Vector2 pad = MarkPadY(root);
					rt.offsetMin -= new Vector2(0f, pad.x);
					rt.offsetMax += new Vector2(0f, pad.y);
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
					Image back = RowFill(kv.Value.transform.parent);
					if (back != null)
					{
						back.color = Palette.Fade(brings ? Palette.Good : Palette.Bad, 0.32f);
					}
				}
			}
			MarkRequired(scene != null && button > 0 ? Needed(SceneEffects.For(scene, "ButtonDescription" + button)) : null);
		}

		// The destinies listed that `effects` (an answer's) require to be
		// had (`O` rows without "not").
		private static HashSet<Objective> Needed(List<SceneEffects.Effect> effects)
		{
			HashSet<Objective> found = new HashSet<Objective>();
			foreach (SceneEffects.Effect e in effects)
			{
				Objective o;
				if (e.Kind == 'O' && e.Op == 0 && (o = ListedObjective(e.Name)) != null)
				{
					found.Add(o);
				}
			}
			return found;
		}

		// What a hovered destiny needs: the destinies in its condition list
		// (not the "not" ones) and those its earning answers require.
		private static HashSet<Objective> RequiredBy(Objective target)
		{
			HashSet<Objective> found = new HashSet<Objective>();
			if (target.Condition != null)
			{
				foreach (ObjectiveCondition c in target.Condition)
				{
					string op = (c.Operator ?? "").Trim();
					Objective o = ListedObjective((c.Operand1 ?? "").Trim());
					if (o != null && op != "!=" && op != "\u2260")
					{
						found.Add(o);
					}
				}
			}
			foreach (KeyValuePair<string, int> e in SceneObjectives.Earners((int)target.Name))
			{
				if (e.Value > 0)
				{
					found.UnionWith(Needed(SceneEffects.For(e.Key, "ButtonDescription" + e.Value)));
				}
			}
			found.Remove(target);
			return found;
		}

		// The listed destiny a key names: its asset name, its enum name, or
		// its title key.
		private static Objective ListedObjective(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				return null;
			}
			if (key.EndsWith(".Title", StringComparison.Ordinal))
			{
				key = key.Substring(0, key.Length - 6);
			}
			foreach (KeyValuePair<Objective, TextMeshProUGUI> kv in _destRows)
			{
				if (kv.Key.name == key || ObjectiveKey(kv.Key) == key)
				{
					return kv.Key;
				}
			}
			return null;
		}

		// An outline in Palette.Required round each listed destiny in `req`
		// (null: none): over its fate colour, so a destiny that happened or
		// is out of reach still shows that it is needed.
		private static void MarkRequired(ICollection<Objective> req)
		{
			foreach (KeyValuePair<Objective, TextMeshProUGUI> kv in _destRows)
			{
				Transform row = kv.Value != null ? kv.Value.transform.parent : null;
				if (row == null)
				{
					continue;
				}
				bool on = req != null && req.Contains(kv.Key);
				Transform box = row.Find("Required");
				if (box == null)
				{
					if (!on)
					{
						continue;
					}
					GameObject go = new GameObject("Required", typeof(RectTransform));
					go.transform.SetParent(row, false);
					go.AddComponent<LayoutElement>().ignoreLayout = true;
					Around(go.GetComponent<RectTransform>());
					const float w = 2.5f;
					// bottom, top, left, right edges
					Vector4[] edges =
					{
						new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 1f, 1f, 1f),
						new Vector4(0f, 0f, 0f, 1f), new Vector4(1f, 0f, 1f, 1f),
					};
					foreach (Vector4 ed in edges)
					{
						GameObject eg = new GameObject("Edge", typeof(RectTransform));
						eg.transform.SetParent(go.transform, false);
						Image img = eg.AddComponent<Image>();
						img.color = Palette.Required;
						img.raycastTarget = false;
						RectTransform ert = eg.GetComponent<RectTransform>();
						ert.anchorMin = new Vector2(ed.x, ed.y);
						ert.anchorMax = new Vector2(ed.z, ed.w);
						bool flat = ed.y == ed.w;
						ert.pivot = new Vector2(ed.x == 1f && !flat ? 1f : 0f, ed.y == 1f && flat ? 1f : 0f);
						ert.sizeDelta = flat ? new Vector2(0f, w) : new Vector2(w, 0f);
						ert.anchoredPosition = Vector2.zero;
					}
					box = go.transform;
				}
				box.gameObject.SetActive(on);
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
				// wide over the tree, the scale as a line
				List<int[]> ranges = new List<int[]>();
				List<string> words = new List<string>();
				string now = "";
				if (p.Scales != null)
				{
					for (int i = 0; i < p.Scales.Length; i++)
					{
						Scale sc = p.Scales[i];
						int a = Mathf.RoundToInt((float)sc.MinValue), b = Mathf.RoundToInt((float)sc.MaxValue);
						ranges.Add(new[] { a, b });
						string word = Loc.Game(p.ParameterName + ".Segment" + (i + 1), "");
						words.Add(word);
						if (row.Shown >= a && row.Shown <= b)
						{
							now = word;
						}
					}
				}
				string state = "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Accent) + "><b>" + row.Shown
					+ (now.Length > 0 ? "  \u00b7  " + now : "") + "</b></color>\n";
				if (ShowWideScale(p.Icon, _tipComp.Title.text, state + _tipComp.Description.text, ranges, words, row.Shown))
				{
					return;
				}
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
			// who they are (the game's Relations page: `<Name>.Description`,
			// "Head of the Brante family, ..."), then what an unusual status
			// means
			string who = Loc.Game(c.Name + ".Description", "").Trim();
			if (who.Length <= 1)
			{
				who = Loc.Game(c.WhoIs.ToString(), "").Trim();
			}
			// (the status's name is on the value line above)
			string descr = Usual(row.Status) ? who
				: who + "\n\n" + Loc.Game(c.Name + "." + row.Status + ".Description", "");
			_tipComp.Description.text = descr.Trim().Length > 0 ? descr.Trim() : "<i>" + Loc.T(Loc.K.NoStatText) + "</i>";
			List<int[]> ranges = new List<int[]>();
			List<string> words = new List<string>();
			foreach (int[] st in steps)
			{
				ranges.Add(st);
				words.Add(RelationWord(st[0]));
			}
			string state = "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Accent) + "><b>" + (row.Shown > 0 ? "+" : "") + row.Shown
				+ "  \u00b7  " + (Usual(row.Status) ? RelationWord(row.Shown) : Loc.Game(c.Name + "." + row.Status, row.Status.ToString()))
				+ "</b></color>\n";
			if (ShowWideScale(_tipComp.Icon.sprite, _tipComp.Title.text, state + _tipComp.Description.text, ranges, words, row.Shown))
			{
				return;
			}
			PlaceTooltip(over);
		}

		// ---- the wide destiny pop-up ----
		//
		// A destiny's pop-up is laid over the tree (TreeArea), as wide as it:
		// the icon, name, state and description on top, what it requires and
		// where it comes from side by side below. On the game's tooltip
		// parchment, in its font. The game's tall tooltip, beside the list,
		// covered the next columns of destinies and grew past the screen
		// with a long "Comes from".
		public static RectTransform TreeArea;
		private static GameObject _wide;
		private static Image _wideIcon;
		private static TextMeshProUGUI _wideTitle, _wideText;
		private static RectTransform _wideFromBox;   // "Comes from": one row per scene, options bracketed
		private static readonly List<RectTransform> _wideBrackets = new List<RectTransform>();
		private static RectTransform _wideReqBox;   // "Requires": one row per part
		private static TMP_FontAsset _wideFont;
		private const float WideLine = 20f, WideIcon = 26f;
		private static string _lastTipText;   // the destiny pop-up's text, for the dev dump
		private const float WideMax = 1500f;

		private static bool ShowWideTip(Objective o, string title, string state, string descr,
			List<string> conds, List<Sprite> condIcons, List<string> ors, List<Sprite> orIcons, List<FromLine> comes)
		{
			if (TreeArea == null || TooltipParent == null || !EnsureWide())
			{
				return false;
			}
			if (_tip != null)
			{
				_tip.SetActive(false);
			}
			_wideIcon.sprite = o.Icon;
			_wideIcon.enabled = o.Icon != null;
			_wideTitle.text = title;
			_wideText.text = (state.Length > 0 ? state + "\n" : "") + descr;
			SetPicture(o.Picture);
			_wideScale.gameObject.SetActive(false);
			bool requires = conds.Count + ors.Count > 0;
			_wideBrackets.Clear();
			FillRequires(conds, condIcons, ors, orIcons);
			_wideReqBox.gameObject.SetActive(requires);
			_wideFromBox.gameObject.SetActive(comes.Count > 0);
			Transform bottom = _wideReqBox.parent;
			bottom.gameObject.SetActive(requires || comes.Count > 0);

			RectTransform box = (RectTransform)TooltipParent;
			RectTransform rt = (RectTransform)_wide.transform;
			Vector3[] c = new Vector3[4];
			TreeArea.GetWorldCorners(c);
			Vector2 lo = box.InverseTransformPoint(c[0]);
			Vector2 hi = box.InverseTransformPoint(c[2]);
			float w = Mathf.Min(hi.x - lo.x, WideMax);
			rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
			_wide.SetActive(true);
			_wide.transform.SetAsLastSibling();
			// Requires keeps its lines whole: at least as wide as its longest
			// (up to half the box, past that its lines wrap); Comes from wraps
			// in the rest. Without a minimum the column was squeezed and its
			// unwrapped lines ran into Comes from.
			LayoutElement rl = _wideReqBox.GetComponent<LayoutElement>();
			rl.minWidth = -1f;
			SetWrap(_wideReqBox, false);
			LayoutRebuilder.ForceRebuildLayoutImmediate(_wideReqBox);
			float need = LayoutUtility.GetPreferredWidth(_wideReqBox);
			float cap = (w - 112f) * 0.5f;
			if (comes.Count > 0 && need > cap)
			{
				SetWrap(_wideReqBox, true);
				need = cap;
			}
			rl.minWidth = rl.preferredWidth = need;
			// Comes from gets the rest: its lines are laid out by hand to it
			float fromW = w - 112f - (requires ? need + 64f : 0f);
			FillComesFrom(comes, fromW);
			LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
			LayoutRebuilder.ForceRebuildLayoutImmediate(rt);   // wrapped heights settle on the second pass
			DrawSpines();
			PlaceWide(box, rt, lo, hi, w);
			return true;
		}

		// Centred over the tree, from its top (down over the stats when it
		// says more than the tree is tall).
		private static void PlaceWide(RectTransform box, RectTransform rt, Vector2 lo, Vector2 hi, float w)
		{
			Rect area = box.rect;
			float h = rt.rect.height;
			float x = (lo.x + hi.x - w) * 0.5f;
			float y = Mathf.Clamp(hi.y, area.yMin + 12f + h, area.yMax - 12f);
			rt.pivot = new Vector2(0f, 1f);
			rt.position = box.TransformPoint(new Vector3(x, y, 0f));
		}

		// The picture top right (none: hidden), as tall as PictureH.
		private const float PictureH = 150f;

		private static void SetPicture(Sprite pic)
		{
			_widePicture.sprite = pic;
			_widePicture.gameObject.SetActive(pic != null);
			LayoutElement le = _widePicture.GetComponent<LayoutElement>();
			float aspect = pic != null && pic.rect.height > 0f ? pic.rect.width / pic.rect.height : 1f;
			le.preferredHeight = le.minHeight = PictureH;
			le.preferredWidth = le.minWidth = PictureH * aspect;
		}

		// A stat's or a person's pop-up, wide over the tree as the
		// destinies': the icon or portrait, the name, the value (or status)
		// and the description on top; below, the scale as a line with marks
		// at its ends and between its ranges, each range's numbers above
		// and its word below, the range the value is in marked.
		private static bool ShowWideScale(Sprite icon, string title, string text, List<int[]> ranges, List<string> words, int value)
		{
			if (TreeArea == null || TooltipParent == null || !EnsureWide())
			{
				return false;
			}
			if (_tip != null)
			{
				_tip.SetActive(false);
			}
			_wideIcon.sprite = icon;
			_wideIcon.enabled = icon != null;
			_wideTitle.text = title;
			_wideText.text = text;
			SetPicture(null);
			_wideReqBox.parent.gameObject.SetActive(false);
			RectTransform box = (RectTransform)TooltipParent;
			RectTransform rt = (RectTransform)_wide.transform;
			Vector3[] c = new Vector3[4];
			TreeArea.GetWorldCorners(c);
			Vector2 lo = box.InverseTransformPoint(c[0]);
			Vector2 hi = box.InverseTransformPoint(c[2]);
			float w = Mathf.Min(hi.x - lo.x, WideMax);
			rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
			_wideScale.gameObject.SetActive(ranges.Count > 0);
			DrawScale(_wideScale, w - 112f, ranges, words, value);
			_wide.SetActive(true);
			_wide.transform.SetAsLastSibling();
			LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
			LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
			PlaceWide(box, rt, lo, hi, w);
			return true;
		}

		private static void DrawScale(RectTransform area, float width, List<int[]> ranges, List<string> words, int value)
		{
			Clear(area);
			if (ranges.Count == 0)
			{
				return;
			}
			int min = ranges[0][0], max = ranges[ranges.Count - 1][1];
			float cell = width / Mathf.Max(1, max - min + 1);   // each whole value a cell
			Color ink = Palette.Ink, pale = Palette.Fade(Palette.Ink, 0.55f);
			const float lineY = -30f;
			// the line, a mark at each end and between two ranges
			Bar(area, 0f, lineY, width, lineY, 2f, ink);
			Bar(area, 0f, lineY + 9f, 0f, lineY - 9f, 2f, ink);
			Bar(area, width, lineY + 9f, width, lineY - 9f, 2f, ink);
			for (int i = 0; i < ranges.Count; i++)
			{
				float x0 = (ranges[i][0] - min) * cell, x1 = (ranges[i][1] - min + 1) * cell;
				if (i > 0)
				{
					Bar(area, x0, lineY + 9f, x0, lineY - 9f, 2f, ink);
				}
				bool here = value >= ranges[i][0] && value <= ranges[i][1];
				if (here)
				{
					// the range the value is in: the line drawn thick there
					Bar(area, x0, lineY, x1, lineY, 6f, Palette.Accent);
				}
				TextMeshProUGUI nums = WideText(area, "Range", _wideFont, 16f, FontStyles.Normal);
				nums.text = ranges[i][0] == ranges[i][1] ? ranges[i][0].ToString() : ranges[i][0] + " \u2026 " + ranges[i][1];
				nums.color = here ? ink : pale;
				nums.alignment = TextAlignmentOptions.Bottom;
				nums.enableWordWrapping = false;
				Pin(nums.rectTransform, new Vector2(x0, 0f), new Vector2(x1 - x0, lineY * -1f - 10f), new Vector2(0f, 1f));
				TextMeshProUGUI word = WideText(area, "Word", _wideFont, 20f, here ? FontStyles.Bold : FontStyles.Normal);
				word.text = i < words.Count ? words[i] : "";
				word.color = here ? Palette.Accent : ink;
				word.alignment = TextAlignmentOptions.Top;
				word.enableWordWrapping = true;
				Pin(word.rectTransform, new Vector2(x0 + 4f, lineY - 14f), new Vector2(x1 - x0 - 8f, ScaleH + lineY + 10f), new Vector2(0f, 1f));
			}
			// the value: a diamond on the line
			GameObject dg = new GameObject("Value", typeof(RectTransform));
			dg.transform.SetParent(area, false);
			Image di = dg.AddComponent<Image>();
			di.color = Palette.Accent;
			di.raycastTarget = false;
			RectTransform drt = di.rectTransform;
			drt.anchorMin = drt.anchorMax = new Vector2(0f, 1f);
			drt.pivot = new Vector2(0.5f, 0.5f);
			drt.sizeDelta = new Vector2(14f, 14f);
			drt.anchoredPosition = new Vector2((Mathf.Clamp(value, min, max) - min + 0.5f) * cell, lineY);
			drt.localRotation = Quaternion.Euler(0f, 0f, 45f);
		}

		private static Image _widePicture;
		private static RectTransform _wideScale;
		private const float ScaleH = 92f;

		// The game tooltip's parchment: its biggest image (null before the
		// tooltip is cloned).
		private static Image PaperImage()
		{
			if (!EnsureTooltip())
			{
				return null;
			}
			Image paper = null;
			foreach (Image img in _tip.GetComponentsInChildren<Image>(true))
			{
				if (img.sprite != null && (paper == null
					|| img.rectTransform.rect.width * img.rectTransform.rect.height
						> paper.rectTransform.rect.width * paper.rectTransform.rect.height))
				{
					paper = img;
				}
			}
			return paper;
		}

		// Lays the parchment on `img` (the tree's own pop-ups); false when
		// it is not there yet.
		public static bool Paper(Image img)
		{
			Image paper = PaperImage();
			if (paper == null || img == null)
			{
				return false;
			}
			img.sprite = paper.sprite;
			img.type = paper.type;
			img.color = paper.color;
			return true;
		}

		private static bool EnsureWide()
		{
			if (_wide != null)
			{
				return true;
			}
			if (!EnsureTooltip())
			{
				return false;
			}
			Image paper = PaperImage();
			TMP_FontAsset font = _tipComp.Description != null ? _tipComp.Description.font : null;
			TMP_FontAsset titleFont = _tipComp.Title != null ? _tipComp.Title.font : font;

			_wide = new GameObject("BranteSaveLoad_DestinyTip", typeof(RectTransform));
			_wide.transform.SetParent(TooltipParent, false);
			Canvas cv = _wide.AddComponent<Canvas>();
			cv.overrideSorting = true;
			cv.sortingOrder = 8100;   // over the tree, as the stat tooltip
			Image back = _wide.AddComponent<Image>();
			back.raycastTarget = false;
			if (paper != null)
			{
				back.sprite = paper.sprite;
				back.type = paper.type;
				back.color = paper.color;
			}
			else
			{
				back.color = Palette.PaperLight;
			}
			VerticalLayoutGroup v = _wide.AddComponent<VerticalLayoutGroup>();
			v.padding = new RectOffset(56, 56, 30, 34);
			v.spacing = 14f;
			v.childControlWidth = v.childControlHeight = true;
			v.childForceExpandWidth = true;
			v.childForceExpandHeight = false;
			ContentSizeFitter fit = _wide.AddComponent<ContentSizeFitter>();
			fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			// top: icon | name, state, description
			GameObject top = WideGroup("Top", _wide.transform, true, 18f);
			GameObject ig = new GameObject("Icon", typeof(RectTransform));
			ig.transform.SetParent(top.transform, false);
			_wideIcon = ig.AddComponent<Image>();
			_wideIcon.preserveAspect = true;
			_wideIcon.raycastTarget = false;
			LayoutElement il = ig.AddComponent<LayoutElement>();
			il.preferredWidth = il.preferredHeight = il.minWidth = il.minHeight = 84f;
			GameObject words = WideGroup("Words", top.transform, false, 4f);
			words.AddComponent<LayoutElement>().flexibleWidth = 1f;
			_wideTitle = WideText(words.transform, "Title", titleFont, 30f, FontStyles.Bold);
			_wideText = WideText(words.transform, "Text", font, 22f, FontStyles.Normal);
			// the destiny's picture, as the game's Destiny window shows it, top right
			GameObject pg = new GameObject("Picture", typeof(RectTransform));
			pg.transform.SetParent(top.transform, false);
			_widePicture = pg.AddComponent<Image>();
			_widePicture.preserveAspect = true;
			_widePicture.raycastTarget = false;
			pg.AddComponent<LayoutElement>();

			// a rule between
			GameObject rule = new GameObject("Rule", typeof(RectTransform));
			rule.transform.SetParent(_wide.transform, false);
			Image ri = rule.AddComponent<Image>();
			ri.color = Palette.Fade(Palette.Muted, 0.5f);
			ri.raycastTarget = false;
			LayoutElement rle = rule.AddComponent<LayoutElement>();
			rle.preferredHeight = rle.minHeight = 1.5f;

			// bottom: requires | comes from
			GameObject bottom = WideGroup("Lists", _wide.transform, true, 64f);
			// Requires as wide as its longest line (one line each), Comes
			// from the rest
			GameObject req = WideGroup("Requires", bottom.transform, false, 2f);
			req.AddComponent<LayoutElement>().flexibleWidth = 0f;
			_wideReqBox = (RectTransform)req.transform;
			_wideFont = font;
			GameObject from = WideGroup("ComesFrom", bottom.transform, false, 0f);
			from.AddComponent<LayoutElement>().flexibleWidth = 1f;
			_wideFromBox = (RectTransform)from.transform;
			// a stat's or person's scale (ShowWideScale), in place of the lists
			GameObject scale = new GameObject("Scale", typeof(RectTransform));
			scale.transform.SetParent(_wide.transform, false);
			_wideScale = (RectTransform)scale.transform;
			LayoutElement sle = scale.AddComponent<LayoutElement>();
			sle.preferredHeight = sle.minHeight = ScaleH;
			_wide.SetActive(false);
			return true;
		}

		// The Requires column's rows: the heading, a bullet line per required
		// part (its stat's, person's or destiny's icon first), then "One
		// of:" and the options under a bracket, as a scene's answers.
		private static void FillRequires(List<string> conds, List<Sprite> condIcons, List<string> ors, List<Sprite> orIcons)
		{
			Clear(_wideReqBox);
			if (conds.Count + ors.Count == 0)
			{
				return;
			}
			WideLineRow(_wideReqBox, null, "<b>" + Loc.T(Loc.K.Requires) + ":</b>", false, false);
			for (int i = 0; i < conds.Count; i++)
			{
				WideLineRow(_wideReqBox, condIcons[i], conds[i], true, false);
			}
			if (ors.Count == 0)
			{
				return;
			}
			WideLineRow(_wideReqBox, null, "\u2022 <i>" + Loc.T(Loc.K.OneOf) + ":</i>", false, false);
			RectTransform box = BracketBox(_wideReqBox);
			for (int i = 0; i < ors.Count; i++)
			{
				WideLineRow(box, orIcons[i], ors[i], false, true);
			}
		}

		// "Comes from": a row per scene ("• Scene: answer (needs)", or the
		// scene alone), a scene's several earning answers under a bracket.
		// Each need has its icon, so the lines are laid out by hand
		// (FlowLine), wrapped to `width`.
		private static void FillComesFrom(List<FromLine> lines, float width)
		{
			Clear(_wideFromBox);
			if (lines.Count == 0)
			{
				return;
			}
			WideLineRow(_wideFromBox, null, "<b>" + Loc.T(Loc.K.DestEarnedBy) + ":</b>", false, false);
			RectTransform box = null;
			foreach (FromLine line in lines)
			{
				if (line.Option)
				{
					if (box == null)
					{
						box = BracketBox(_wideFromBox);
					}
					FlowLine(box, line, width - 14f, true);
					continue;
				}
				box = null;
				FlowLine(_wideFromBox, line, width, false);
			}
		}

		private const float FlowLineH = 28f;      // a "Comes from" line's height (an icon fits)
		private const float FlowIcon = 24f;
		private const float FlowStub = 18f;       // a bracketed line's room for its stub

		// One "Comes from" line, its words and need icons placed left to
		// right and wrapped at `width`; `stub`: a bracketed option, its stub
		// on the first line.
		private static void FlowLine(Transform parent, FromLine line, float width, bool stub)
		{
			GameObject go = new GameObject("Flow", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			RectTransform frt = (RectTransform)go.transform;
			LayoutElement le = go.AddComponent<LayoutElement>();
			le.flexibleWidth = 1f;
			le.minWidth = le.preferredWidth = width;

			// the tokens: (icon or null, word); a word glued to the next has no space
			List<KeyValuePair<Sprite, string>> tokens = new List<KeyValuePair<Sprite, string>>();
			List<bool> greens = new List<bool>();   // a need met by the hero shown (drawn green)
			foreach (string w in line.Head.Split(' '))
			{
				if (w.Length > 0)
				{
					tokens.Add(new KeyValuePair<Sprite, string>(null, w));
					greens.Add(false);
				}
			}
			for (int i = 0; i < line.Needs.Count; i++)
			{
				string text = (i == 0 ? "" : "") + line.Needs[i] + (i < line.Needs.Count - 1 ? line.Seps[i].TrimEnd() : ")");
				string[] words = text.Split(' ');
				for (int j = 0; j < words.Length; j++)
				{
					if (words[j].Length == 0)
					{
						continue;
					}
					// "(" opens the first need, before its icon
					string word = (i == 0 && j == 0 ? "" : "") + words[j];
					tokens.Add(new KeyValuePair<Sprite, string>(j == 0 ? line.Icons[i] : null,
						i == 0 && j == 0 ? "\u0001" + word : word));
					greens.Add(i < line.NeedMet.Count && line.NeedMet[i]);
				}
			}

			float x0 = stub ? FlowStub : 0f;
			float x = x0, y = 0f;
			StringBuilder run = new StringBuilder();
			float runX = x;
			bool runGreen = false;
			System.Action flush = delegate
			{
				if (run.Length > 0)
				{
					string text = run.ToString().TrimEnd();
					FlowText(frt, runGreen && line.Style == 0 ? Met(text) : line.Styled(text), runX, y);
					run.Length = 0;
				}
			};
			for (int ti = 0; ti < tokens.Count; ti++)
			{
				KeyValuePair<Sprite, string> t = tokens[ti];
				if (greens[ti] != runGreen)
				{
					flush();
					runGreen = greens[ti];
				}
				bool open = t.Value.StartsWith("\u0001", StringComparison.Ordinal);
				string word = open ? t.Value.Substring(1) : t.Value;
				float pre = open ? Measure("(") : 0f;
				float iw = t.Key != null ? FlowIcon + 3f : 0f;
				// TMP leaves trailing spaces out of a measure: the space apart
				float ww = Measure(word) + SpaceWidth();
				if (x > x0 && x + pre + iw + ww > width)
				{
					flush();
					x = x0;
					y -= FlowLineH;
				}
				if (run.Length == 0)
				{
					runX = x;
				}
				if (open)
				{
					run.Append("(");
					x += pre;
				}
				if (t.Key != null)
				{
					flush();
					FlowIconAt(frt, t.Key, x, y);
					x += iw;
					runX = x;
				}
				run.Append(word).Append(' ');
				x += ww;
			}
			flush();
			if (stub)
			{
				GameObject sg = new GameObject("Stub", typeof(RectTransform));
				sg.transform.SetParent(frt, false);
				Image si = sg.AddComponent<Image>();
				si.color = Palette.Fade(Palette.Muted, 0.55f);
				si.raycastTarget = false;
				Pin(si.rectTransform, new Vector2(0f, -FlowLineH * 0.5f + 1.5f), new Vector2(12f, 3f), new Vector2(0f, 1f));
			}
			le.minHeight = le.preferredHeight = -y + FlowLineH;
		}

		private static void FlowText(RectTransform parent, string text, float x, float y)
		{
			TextMeshProUGUI t = WideText(parent, "Text", _wideFont, WideLine, FontStyles.Normal);
			t.text = text;
			t.enableWordWrapping = false;
			t.alignment = TextAlignmentOptions.MidlineLeft;
			Pin(t.rectTransform, new Vector2(x, y), new Vector2(Measure(text) + 8f, FlowLineH), new Vector2(0f, 1f));
		}

		private static void FlowIconAt(RectTransform parent, Sprite icon, float x, float y)
		{
			GameObject ig = new GameObject("Icon", typeof(RectTransform));
			ig.transform.SetParent(parent, false);
			Image img = ig.AddComponent<Image>();
			img.sprite = icon;
			img.preserveAspect = true;
			img.raycastTarget = false;
			Pin(img.rectTransform, new Vector2(x, y - (FlowLineH - FlowIcon) * 0.5f), new Vector2(FlowIcon, FlowIcon), new Vector2(0f, 1f));
		}

		private static TextMeshProUGUI _measure;
		private static float _space = -1f;

		private static float SpaceWidth()
		{
			if (_space < 0f)
			{
				_space = Mathf.Max(0f, Measure("x x") - Measure("xx"));
			}
			return _space;
		}

		// The width of `text` in the pop-up's font and size (tags ignored by TMP).
		private static float Measure(string text)
		{
			if (_measure == null)
			{
				_measure = WideText(_wide.transform, "Measure", _wideFont, WideLine, FontStyles.Normal);
				_measure.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
				_measure.enableWordWrapping = false;
				_measure.color = Color.clear;
			}
			return _measure.GetPreferredValues(text).x;
		}

		private static void Clear(Transform t)
		{
			for (int i = t.childCount - 1; i >= 0; i--)
			{
				GameObject old = t.GetChild(i).gameObject;
				old.SetActive(false);
				UnityEngine.Object.Destroy(old);
			}
		}

		// A box for bracketed options, indented for the spine (DrawSpines).
		private static RectTransform BracketBox(Transform parent)
		{
			GameObject box = WideGroup("OneOf", parent, false, 2f);
			((VerticalLayoutGroup)box.GetComponent<HorizontalOrVerticalLayoutGroup>()).padding = new RectOffset(14, 0, 0, 0);
			_wideBrackets.Add((RectTransform)box.transform);
			return (RectTransform)box.transform;
		}

		private static void SetWrap(Transform t, bool wrap)
		{
			foreach (TextMeshProUGUI tm in t.GetComponentsInChildren<TextMeshProUGUI>(true))
			{
				if (tm.name == "Text")
				{
					tm.enableWordWrapping = wrap;
				}
			}
		}

		private static TextMeshProUGUI WideLineRow(Transform parent, Sprite icon, string text, bool bullet, bool stub)
		{
			GameObject row = WideGroup("Line", parent, true, 6f);
			row.GetComponent<HorizontalOrVerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
			if (stub)
			{
				// the bracket's stub, from the spine (DrawOneOfSpine) to the line
				GameObject sg = new GameObject("Stub", typeof(RectTransform));
				sg.transform.SetParent(row.transform, false);
				Image si = sg.AddComponent<Image>();
				si.color = Palette.Fade(Palette.Muted, 0.55f);
				si.raycastTarget = false;
				LayoutElement sl = sg.AddComponent<LayoutElement>();
				sl.preferredWidth = sl.minWidth = 12f;
				sl.preferredHeight = sl.minHeight = 3f;
			}
			if (bullet)
			{
				TextMeshProUGUI b = WideText(row.transform, "Bullet", _wideFont, WideLine, FontStyles.Normal);
				b.text = "\u2022";
				b.enableWordWrapping = false;
				b.alignment = TextAlignmentOptions.Center;
				LayoutElement bl = b.gameObject.AddComponent<LayoutElement>();
				bl.preferredWidth = bl.minWidth = 12f;   // a fixed slot: the glyph's own box let it lie over the icon
			}
			if (icon != null)
			{
				GameObject ig = new GameObject("Icon", typeof(RectTransform));
				ig.transform.SetParent(row.transform, false);
				Image img = ig.AddComponent<Image>();
				img.sprite = icon;
				img.preserveAspect = true;
				img.raycastTarget = false;
				LayoutElement il = ig.AddComponent<LayoutElement>();
				il.preferredWidth = il.minWidth = il.preferredHeight = il.minHeight = WideIcon;
			}
			TextMeshProUGUI t = WideText(row.transform, "Text", _wideFont, WideLine, FontStyles.Normal);
			t.text = text;
			t.enableWordWrapping = false;   // one line each
			t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
			return t;
		}

		// Each bracket's spine: from its first option's middle to its last's,
		// where their stubs start (after layout).
		private static void DrawSpines()
		{
			foreach (RectTransform box in _wideBrackets)
			{
				if (box == null)
				{
					continue;
				}
				// the stubs of its options, top to bottom
				List<RectTransform> stubs = new List<RectTransform>();
				foreach (Transform t in box.GetComponentsInChildren<Transform>(false))
				{
					if (t.name == "Stub")
					{
						stubs.Add((RectTransform)t);
					}
				}
				if (stubs.Count == 0)
				{
					continue;
				}
				float top = float.MinValue, bottom = float.MaxValue, x = 0f;
				foreach (RectTransform st in stubs)
				{
					Vector3 p = box.InverseTransformPoint(st.TransformPoint(st.rect.center));
					top = Mathf.Max(top, p.y);
					bottom = Mathf.Min(bottom, p.y);
					x = box.InverseTransformPoint(st.TransformPoint(new Vector3(st.rect.xMin, 0f, 0f))).x;
				}
				GameObject go = new GameObject("Spine", typeof(RectTransform));
				go.transform.SetParent(box, false);
				go.AddComponent<LayoutElement>().ignoreLayout = true;
				Image img = go.AddComponent<Image>();
				img.color = Palette.Fade(Palette.Muted, 0.55f);
				img.raycastTarget = false;
				RectTransform rt = img.rectTransform;
				rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
				rt.pivot = new Vector2(0f, 1f);
				rt.sizeDelta = new Vector2(3f, top - bottom + 3f);
				// the anchored position is from the box's centre
				Vector2 c = box.rect.center;
				rt.anchoredPosition = new Vector2(x - c.x, top + 1.5f - c.y);
			}
		}

		// The icon for a condition line, from what it tests: a stat's, a
		// destiny's or a person's portrait. Its operand is a translation key
		// ("GregorBronte", "WillObjective"), not an asset name. Key-parameter
		// tests ("Take part in the ritual") have none.
		// An operand that is an asset name ("Will"; Sophia Saved's
		// condition has an empty RealCondition): its stat's, person's or
		// destiny's icon.
		private static Sprite NamedIcon(string operand)
		{
			string key = (operand ?? "").Trim();
			if (key.Length == 0)
			{
				return null;
			}
			Parameter p = SceneEffects.ParameterAsset(key);
			if (p != null)
			{
				return p.Icon;
			}
			Character c = SceneEffects.CharacterAsset(key);
			if (c != null)
			{
				return c.TooltipPortret;
			}
			Objective o = SceneEffects.ObjectiveAsset(key);
			return o != null ? o.Icon : null;
		}

		private static Sprite ConditionIcon(ObjectiveConditions rc)
		{
			if (rc == null)
			{
				return null;
			}
			if (rc.ByParameters != null)
			{
				foreach (var c in rc.ByParameters)
				{
					if (c != null && c.ParamName != null && c.ParamName.Icon != null)
					{
						return c.ParamName.Icon;
					}
				}
			}
			if (rc.ByObjectives != null)
			{
				foreach (_Scripts.Helpers.ConditionByObjective c in rc.ByObjectives)
				{
					if (c != null && c.Objective != null && c.Objective.Icon != null)
					{
						return c.Objective.Icon;
					}
				}
			}
			if (rc.ByStatuses != null)
			{
				foreach (_Scripts.Helpers.ConditionByStatus c in rc.ByStatuses)
				{
					if (c != null && c.Character != null && c.Character.TooltipPortret != null)
					{
						return c.Character.TooltipPortret;
					}
				}
			}
			// relations: their element type is not used elsewhere; its
			// Character field by reflection
			System.Collections.IEnumerable rels = rc.ByRelations as System.Collections.IEnumerable;
			if (rels != null)
			{
				foreach (object c in rels)
				{
					if (c == null)
					{
						continue;
					}
					foreach (FieldInfo fi in c.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
					{
						Character ch = fi.GetValue(c) as Character;
						if (ch != null && ch.TooltipPortret != null)
						{
							return ch.TooltipPortret;
						}
					}
				}
			}
			return null;
		}

		private static GameObject WideGroup(string name, Transform parent, bool across, float spacing)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			HorizontalOrVerticalLayoutGroup g = across
				? (HorizontalOrVerticalLayoutGroup)go.AddComponent<HorizontalLayoutGroup>()
				: go.AddComponent<VerticalLayoutGroup>();
			g.spacing = spacing;
			g.childControlWidth = g.childControlHeight = true;
			g.childForceExpandWidth = false;
			g.childForceExpandHeight = false;
			g.childAlignment = TextAnchor.UpperLeft;
			return go;
		}

		private static TextMeshProUGUI WideText(Transform parent, string name, TMP_FontAsset font, float size, FontStyles style)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
			if (font != null)
			{
				t.font = font;
			}
			t.fontSize = size;
			t.fontStyle = style;
			t.color = Palette.Ink;
			t.alignment = TextAlignmentOptions.TopLeft;
			t.enableWordWrapping = true;
			t.overflowMode = TextOverflowModes.Overflow;
			t.raycastTarget = false;
			return t;
		}

		private const float TipIconOverhang = 60f;

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
			// the portrait/icon sits on the pop-up's top edge, half above it
			float y = Mathf.Clamp(mid - size.y * 0.5f, area.yMin + gap, area.yMax - gap - size.y - TipIconOverhang);
			_tip.transform.position = box.TransformPoint(new Vector3(x, y, 0f));
		}

		public static void HideTooltip()
		{
			if (_wide != null)
			{
				_wide.SetActive(false);
			}
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
