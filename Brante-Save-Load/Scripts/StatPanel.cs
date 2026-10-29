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
		// _Soul, _Empire, _Work, _Insurrection); stats no window shows join the
		// tab of their category, so every stat in the game has a row. A tab
		// still locked at the hero's chapter is greyed out, its stats shown
		// as the game already keeps them.
		private static List<Tab> Tabs()
		{
			List<Tab> tabs = new List<Tab>
			{
				new Tab { Hud = "HUD.Character", Chapter = 1, Takes = new[] { ParameterCategory.CharacterParameter },
					Order = new[] { "Will", "Curiosity", "Activity", "Discipline", "Cunning", "Spirituality",
						"Diplomacy", "Valor", "Manipulation", "Sabotage", "Theology", "Preaching", "Death" } },
				new Tab { Hud = "HUD.Home", Chapter = 1, Takes = new[] { ParameterCategory.HomeParameter },
					Order = new[] { "Unity", "Money", "Reputation", "Heir" } },
				new Tab { Hud = "HUD.Destiny", Chapter = 1,
					Takes = new[] { ParameterCategory.FamilyParameter, ParameterCategory.SoulParameter },
					Order = new[] { "Volition", "Love", "Rule" } },
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
					if (p != null && p.Icon != null && placed.Add(p))
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
				if (t != null && placed.Add(p))
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
				case "HUD.Destiny": return Loc.Game("HUD.Destiny", "Destiny");
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
			Vector2 natural = template.GetComponent<RectTransform>().rect.size;
			if (natural.x < 1f || natural.y < 1f)
			{
				natural = new Vector2(375f, 103f);
			}
			// The row's box is taller than what it shows (icon and text fill
			// 70 of its 103 units, 10 down from the top): rows are stacked
			// by their content, so they can be drawn larger.
			float contentTop = 0f, contentBottom = natural.y;
			ContentSpan(template, ref contentTop, ref contentBottom);
			_hoverInset = new Vector2(contentTop, natural.y - contentBottom);
			float pitch = contentBottom - contentTop + 6f;

			const float header = 50f;   // the title, and a locked tab's note under it
			float width = area.rect.width;
			float usable = Mathf.Max(40f, area.rect.height - header);
			// the largest scale at which every category fits
			float scale = 0.3f;
			int rowsFit = 1;
			for (float s = 1.3f; s >= 0.3f; s -= 0.02f)
			{
				int fit = Mathf.Max(1, Mathf.FloorToInt(usable / (pitch * s)));
				int subColumns = 0;
				foreach (Tab t in _tabs)
				{
					subColumns += Mathf.CeilToInt(t.Stats.Count / (float)fit);
				}
				if (natural.x * s <= width / subColumns - 8f)
				{
					scale = s;
					rowsFit = fit;
					break;
				}
				scale = s;
				rowsFit = fit;
			}
			int total = 0;
			foreach (Tab t in _tabs)
			{
				total += Mathf.CeilToInt(t.Stats.Count / (float)rowsFit);
			}
			float subW = width / Mathf.Max(1, total);

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
				trt.sizeDelta = new Vector2(subs * subW - 16f, header);
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
					rrt.anchoredPosition = new Vector2(x + sub * subW + (subW - natural.x * scale) * 0.5f,
						-(header + r * pitch * scale - contentTop * scale));
					AddHover(row, rrt);
					rrt.gameObject.SetActive(true);
					_rows.Add(row);
				}
				x += subs * subW;
			}
		}

		// From the top of the row's box, where what it draws starts and ends:
		// every graphic that shows (all its parents switched on), the slider
		// included, which hangs below its layout group (left as they are when
		// nothing is found).
		private static void ContentSpan(GameObject template, ref float top, ref float bottom)
		{
			RectTransform root = template.GetComponent<RectTransform>();
			float t = float.MaxValue, b = float.MinValue;
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
			}
			if (t < b && b - t > 10f)
			{
				top = Mathf.Max(0f, t);
				bottom = Mathf.Min(root.rect.height, b);
			}
			ModLog.Info("StatPanel: row content " + top.ToString("0") + ".." + bottom.ToString("0") + " of " + root.rect.height.ToString("0"));
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
					t.enableAutoSizing = false;
					t.fontSize *= 1.15f;
					t.enableWordWrapping = false;   // a long level name stays on its line
				}
			}
			Row row = new Row();
			row.Param = p;
			row.Value = pc.TextValue;
			row.Descr = pc.Descr;
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
			t.color = new Color(0.55f, 0.30f, 0.25f);
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
				tab.Title.color = locked ? new Color(0.55f, 0.30f, 0.25f, 0.45f) : new Color(0.55f, 0.30f, 0.25f);
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
					row.Value.color = value > today ? new Color(0.18f, 0.42f, 0.18f)
						: value < today ? new Color(0.62f, 0.16f, 0.12f) : new Color(0.12f, 0.10f, 0.08f);
				}
				if (row.Slider != null)
				{
					row.Slider.value = value;
				}
				if (row.Descr != null)
				{
					row.Descr.text = Segment(row.Param, value);
				}
			}
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
			img.color = new Color(0.84f, 0.79f, 0.68f);
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
