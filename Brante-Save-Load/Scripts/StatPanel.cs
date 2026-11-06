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
		}

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
			public string[] Order;        // the window's stats, in its order
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

		private static bool IsDead(Parameter p)
		{
			return Dead.Contains(p.name) || Dead.Contains(p.ParameterName.ToString());
		}

		private static List<Tab> Tabs()
		{
			List<Tab> tabs = new List<Tab>
			{
				new Tab { Hud = "HUD.Character", Chapter = 1, Takes = new[] { ParameterCategory.CharacterParameter },
					Order = new[] { "Will", "Curiosity", "Activity", "Discipline", "Cunning", "Spirituality",
						"Diplomacy", "Valor", "Manipulation", "Sabotage", "Theology", "Preaching", "Death" } },
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
				foreach (string name in t.Order)
				{
					Parameter p = SceneEffects.ParameterAsset(name);
					if (p != null && p.Icon != null && !IsDead(p) && placed.Add(p))
					{
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
			for (int i = area.childCount - 1; i >= 0; i--)
			{
				GameObject old = area.GetChild(i).gameObject;
				old.SetActive(false);
				UnityEngine.Object.Destroy(old);
			}
			_rows.Clear();
			_roots.Clear();
			_tabs.Clear();
			GameObject template = Template();
			if (template == null)
			{
				return;
			}
			_tabs.AddRange(Tabs());
			Metrics m = Measure(template);
			Vector2 natural = m.Natural;
			float contentTop = m.Top, contentLeft = m.Left, contentRight = m.Right;
			_hoverInset = new Vector2(m.Top, natural.y - m.Bottom);
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
				total += Mathf.CeilToInt(t.Stats.Count / (float)rowsFit);
			}
			float subW = width / Mathf.Max(1, total);
			// spare height goes under the titles, so a locked tab's note has
			// room before its first stat
			float gap = Mathf.Clamp(usable - rowsFit * pitch * scale, 0f, 20f);
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
				int subs = Mathf.CeilToInt(list.Count / (float)rowsFit);
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
					int sub = j / rowsFit;
					int r = j % rowsFit;
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
				columns += Mathf.CeilToInt(t.Stats.Count / (float)rows);
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

		private static readonly Dictionary<Row, RectTransform> _roots = new Dictionary<Row, RectTransform>();

		private static RectTransform RowRoot(Row row)
		{
			RectTransform rt;
			return _roots.TryGetValue(row, out rt) ? rt : null;
		}

		// A clone of the game's row for one stat, its scripts removed.
		private static Row MakeRow(Parameter p, GameObject template)
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
			bool twoSided = p.MinValue < 0f && Math.Abs(p.MinValue) == p.MaxValue;
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
				if (p.ScalePrefab != null && sc != null && sc.ScaleContainer != null)
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
				pc.Icon.sprite = p.Icon;
			}
			if (pc.Name != null)
			{
				pc.Name.text = Loc.Game(p.ParameterName.ToString(), p.name);
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
			row.Param = p;
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
				row.Slider.minValue = p.MinValue;
				row.Slider.maxValue = p.MaxValue;
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
		// `now` (the hero today) is green, below red.
		public static void Show(SceneEffects.Hero shown, SceneEffects.Hero now, int chapter)
		{
			if (shown == null || now == null)
			{
				return;
			}
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
				CanvasGroup cg = RowRoot(row) != null ? RowRoot(row).GetComponent<CanvasGroup>() : null;
				if (cg != null)
				{
					cg.alpha = chapter > 0 && row.Tab != null && row.Tab.Chapter > chapter ? 0.35f : 1f;
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
					row.Descr.text = Segment(row.Param, value);
				}
				FitText(row, _fitLimit);
			}
		}

		// While an answer is hovered in the tree: the stats it changes get a
		// background, green for a rise and red for a fall (the colours of the
		// game's own consequence rows), over what the row shows.
		public static void HighlightChanges(SceneEffects.Hero before, SceneEffects.Hero after)
		{
			foreach (Row row in _rows)
			{
				int d = before != null && after != null ? after.Value(row.Param) - before.Value(row.Param) : 0;
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
					rt.anchorMin = Vector2.zero;
					rt.anchorMax = Vector2.one;
					rt.offsetMin = new Vector2(-4f, _hoverInset.y);
					rt.offsetMax = new Vector2(4f, -_hoverInset.x);
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
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			// over what the row shows only: the boxes of stacked rows overlap
			rt.offsetMin = new Vector2(0f, _hoverInset.y);
			rt.offsetMax = new Vector2(0f, -_hoverInset.x);
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
			catch (Exception e)
			{
				ModLog.Error("StatPanel: tooltip failed:\n" + e);
			}
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
