using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using _Scripts.AMVCC.Views;
using _Scripts.Helpers;
using _Scripts.Managers;

namespace Doorstop
{
	// The player-facing save list.
	//
	//  - a "Save / Load" button in the settings menu, a copy of the menu's own
	//    rows so it looks and behaves like them; see AddPauseButton,
	//  - one table with every save: automatic choice-point snapshots and
	//    manual "Save now" saves, one row each. Click a header to sort by it
	//    (click again to reverse). Manual rows have their own colour.
	//    The x on the far right deletes a row; while its confirmation is
	//    open the row carries a red dashed outline.
	//  - confirm -> the game's own load sequence, exactly what clicking
	//    Continue on the loading screen does:
	//      LoadGame(helper) -> GameLoadingScreen (additive) ->
	//      Broadcast("StartSceneTransitionEvent") -> dissolve ->
	//      AutomaticSceneLoad -> LoadScene(LastOpenScene)
	//    (the sequence documented in docs/FINDINGS.md, "Phase 3 capture
	//    facts"; the dev autoload harness replays the same dance).
	//
	// Loading a save backs up the game's own save folder once and then lets
	// the game save normally again (autosaves, chapter files), exactly as it
	// would after loading an older save from its own menu - so a chapter
	// restart after a death continues the rewound timeline.
	// Shows a node's full text in the history panel's info line while the
	// pointer is over it (the tree nodes are narrow, so the text is clipped).
	public sealed class TreeHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		public string Info;
		public Action<string> OnShow;

		public void OnPointerEnter(PointerEventData eventData)
		{
			if (OnShow != null)
			{
				OnShow(Info);
			}
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			if (OnShow != null)
			{
				OnShow(null);
			}
		}
	}

	// Scrolls a too-long row text (a "marquee"): it first rests showing as
	// much as fits plus TMP's ellipsis, then slides right-to-left (the text
	// moves left) until the end is visible, pauses, and starts over.
	public sealed class Marquee : MonoBehaviour
	{
		public RectTransform Viewport;
		public TextMeshProUGUI Text;
		public float Padding = 8f;
		public float Speed = 28f;
		public float Delay = 2f;      // rest before the first move
		public float EndPause = 1.6f; // pause with the end in view

		private float _full;
		private float _view;
		private float _offset;
		private float _timer;
		private int _phase;           // 0 rest, 1 scrolling, 2 end pause
		private bool _measured;

		private void OnEnable()
		{
			MeasureNow();
		}

		// Re-measures and returns to the resting (ellipsised) look. Safe to
		// call right after a layout rebuild, while the canvas is still
		// inactive or before the first frame is drawn.
		public void MeasureNow()
		{
			_measured = false;
			_offset = 0f;
			_phase = 0;
			_timer = -Delay;
			if (Text == null || Viewport == null)
			{
				return;
			}
			Measure();
		}

		private void Update()
		{
			if (Text == null || Viewport == null)
			{
				return;
			}
			if (!_measured)
			{
				Measure();
				if (!_measured)
				{
					return; // layout not built yet
				}
				if (_full <= Visible() + 0.5f)
				{
					return; // fits: plain text, no animation
				}
			}
			if (_full <= Visible() + 0.5f)
			{
				return;
			}
			float dt = Time.unscaledDeltaTime;
			switch (_phase)
			{
				case 0:
					_timer += dt;
					if (_timer >= 0f)
					{
						_phase = 1;
						ShowFull();
					}
					break;
				case 1:
					_offset += Speed * dt;
					if (_offset >= _full - Visible())
					{
						_offset = _full - Visible();
						_phase = 2;
						_timer = 0f;
					}
					Place();
					break;
				default:
					_timer += dt;
					if (_timer >= EndPause)
					{
						_phase = 0;
						_timer = -Delay;
						_offset = 0f;
						ShowEllipsis();
					}
					break;
			}
		}

		private float Visible()
		{
			return Mathf.Max(1f, _view - 2f * Padding);
		}

		private void Measure()
		{
			_view = Viewport.rect.width;
			if (_view <= 1f)
			{
				return;
			}
			Text.enableWordWrapping = false;
			Text.overflowMode = TextOverflowModes.Ellipsis;
			ShowEllipsis();
			Text.ForceMeshUpdate();
			_full = Text.GetPreferredValues(Text.text).x;
			_measured = true;
		}

		// Resting look: clipped at the cell, the rest replaced by "…".
		private void ShowEllipsis()
		{
			RectTransform rt = Text.rectTransform;
			Text.overflowMode = TextOverflowModes.Ellipsis;
			rt.sizeDelta = new Vector2(Visible(), rt.sizeDelta.y);
			rt.anchoredPosition = new Vector2(Padding, rt.anchoredPosition.y);
		}

		// Whole text, scrolled by _offset.
		private void ShowFull()
		{
			RectTransform rt = Text.rectTransform;
			Text.overflowMode = TextOverflowModes.Overflow;
			rt.sizeDelta = new Vector2(_full, rt.sizeDelta.y);
			Place();
		}

		private void Place()
		{
			RectTransform rt = Text.rectTransform;
			rt.anchoredPosition = new Vector2(Padding - _offset, rt.anchoredPosition.y);
		}
	}

	// Turns a cross's bars red while the pointer is on its button.
	public sealed class CrossHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		public Image[] Bars;

		public void OnPointerEnter(PointerEventData eventData)
		{
			Paint(new Color(0.85f, 0.10f, 0.10f));
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			Paint(new Color(0.05f, 0.04f, 0.03f));
		}

		private void Paint(Color c)
		{
			if (Bars == null)
			{
				return;
			}
			foreach (Image bar in Bars)
			{
				if (bar != null)
				{
					bar.color = c;
				}
			}
		}
	}

	public static class ChoiceWindow
	{
		// Colours - parchment style of the game.
		private static readonly Color ColDim = new Color(0f, 0f, 0f, 0.65f);
		private static readonly Color ColPanel = new Color(0.93f, 0.89f, 0.79f);
		private static readonly Color ColAuto = new Color(0.84f, 0.79f, 0.68f);
		private static readonly Color ColAutoAlt = new Color(0.88f, 0.84f, 0.75f);
		private static readonly Color ColManual = new Color(1f, 0.96f, 0.66f);
		private static readonly Color ColManualAlt = new Color(1f, 0.93f, 0.55f);
		private static readonly Color ColButtonDark = new Color(0.30f, 0.26f, 0.20f, 0.95f);
		private static readonly Color ColLoadButton = new Color(0.55f, 0.30f, 0.25f);
		private static readonly Color ColCancelButton = new Color(0.55f, 0.53f, 0.48f);
		private static readonly Color ColDeleteButton = new Color(0.45f, 0.20f, 0.18f);
		private static readonly Color ColTextLight = new Color(0.95f, 0.92f, 0.82f);
		private static readonly Color ColTextDark = new Color(0.12f, 0.10f, 0.08f);
		private static readonly Color ColDashRed = new Color(0.85f, 0.10f, 0.10f);
		private static readonly Color ColCrossNormal = new Color(0.05f, 0.04f, 0.03f);
		private static readonly Color ColTreeLine = new Color(0.55f, 0.50f, 0.42f, 0.55f);
		private static readonly Color ColTreeChosen = new Color(0.55f, 0.30f, 0.25f);
		private static readonly Color ColTreeChosenBg = new Color(1f, 0.93f, 0.72f);

		private enum Pending { Load, Delete, SaveNow }

		// Table columns, left to right. Choice is the flexible one. The hero
		// is not a column: the list only shows the current main save, so the
		// name is the same on every row.
		private enum Col { Type, Saved, Chapter, Age, Year, Season, Turn, Scene, Choice }

		private static string ColName(Col c)
		{
			switch (c)
			{
				case Col.Type: return Loc.T(Loc.K.ColType);
				case Col.Saved: return Loc.T(Loc.K.ColSaved);
				case Col.Chapter: return Loc.T(Loc.K.ColChapter);
				case Col.Age: return Loc.T(Loc.K.ColAge);
				case Col.Year: return Loc.T(Loc.K.ColYear);
				case Col.Season: return Loc.T(Loc.K.ColSeason);
				case Col.Turn: return Loc.T(Loc.K.ColTurn);
				case Col.Scene: return Loc.T(Loc.K.ColScene);
				default: return Loc.T(Loc.K.ColChoice);
			}
		}

		// Preferred widths in reference px; Choice takes whatever is left.
		private static readonly float[] ColWidths =
		{ 130f, 170f, 150f, 115f, 80f, 100f, 80f, 120f, 200f };

		private const float RowHeight = 44f;
		private const float HeaderHeight = 36f;
		private const float DeleteWidth = 64f;
		private const float PanelWidth = 1360f;

		private sealed class SaveEntry
		{
			public string Dat;
			public string Kind;
			public string Title;
			public DateTime Saved;
			public int Chapter;
			public string ChapterName;
			public string ChapterKey;
			public string Hero;
			public int Age;
			public int Year;
			public int Season;
			public int Turn;
			public string Scene;

			public string Choice
			{
				get
				{
					// Titles are localised at display time, so a language
					// switch also changes them (they were captured in whatever
					// language was active then).
					string live = TitleOf(Scene);
					if (!string.IsNullOrEmpty(live))
					{
						return live;
					}
					return string.IsNullOrEmpty(Title) ? Scene : Title;
				}
			}
		}

		private static GameObject _canvas;
		private static ScrollRect _scrollRect;
		private static Transform _itemContainer;
		private static TextMeshProUGUI[] _headerLabels;
		private static Image[] _sortIcons;
		private static Sprite _triUp;
		private static Sprite _triDown;
		private static Sprite _dashH;
		private static Sprite _dashV;
		private static GameObject _saveNowBtn;
		private static GameObject _historyBtn;
		private static GameObject _historyPanel;
		private static int _historyLoadable;
		private static TextMeshProUGUI _historyInfo;
		private static GameObject _exploreBtn;
		private static TextMeshProUGUI _exploreLabel;
		private static GameObject _loadHereBtn;
		private static bool _explore;

		// One tree column: a scene, the answers it offered, the one taken and
		// (for captured scenes) the snapshot to load.
		private sealed class TreeCol
		{
			public string Scene = "";
			public string Title = "";
			public string SnapshotPath = "";
			public bool Preview;
			public List<History.Choice> Choices = new List<History.Choice>();

			public bool HasSnapshot
			{
				get { return !string.IsNullOrEmpty(SnapshotPath); }
			}
		}

		private static List<TreeCol> _tree = new List<TreeCol>();
		private static ScrollRect _historyScroll;
		private static List<TreeCol> _preview = new List<TreeCol>();
		private static Dictionary<string, string> _overrides =
			new Dictionary<string, string>();

		private static Transform _historyList;
		private static Col _sortCol = Col.Saved;
		private static bool _sortAsc;
		private static List<SaveEntry> _entries = new List<SaveEntry>();
		private static string _builtLanguage;
		public static bool DevLanguageSwitch;
		private static GameObject _confirmPanel;
		private static TextMeshProUGUI _confirmText;
		private static TextMeshProUGUI _confirmLoadLabel;
		private static Button _confirmLoadBtn;
		private static Pending _pendingAction;
		private static string _pendingPath;
		private static string _pendingTitle;
		private static GameObject _markObj;
		private static int _lastItemCount;
		private static string _firstItemPath;
		private static string _firstItemTitle;

		// ------------------------------------------------------------------
		// Settings (pause) menu button
		// ------------------------------------------------------------------

		private static GameObject _pauseBtn;

		// Called whenever the pause/settings menu is toggled. Adds the "Load any
		// choice" button as a copy of the lowest existing menu button (same
		// look, same font, same hover), with the menu lifted half a step so the
		// new button sits at the bottom. Idempotent.
		public static void EnsurePauseButton()
		{
			try
			{
				if (_pauseBtn != null)
				{
					return;
				}
				UIManager ui = UIManager.Initiate;
				if (ui == null || ui.PauseWindow == null || !ui.PauseWindow.scene.IsValid())
				{
					return;
				}
				AddPauseButton(ui.PauseWindow);
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: settings button failed:\n" + e);
			}
		}

		// The settings menu is a VerticalLayoutGroup of rows; each row is a
		// horizontal wrapper around the black frame that holds one button. The
		// new button is a copy of the lowest row, placed by the same layout.
		private static bool IsMenuList(Transform t)
		{
			return t.GetComponent<VerticalLayoutGroup>() != null && t.GetComponent<Image>() == null;
		}

		private static void AddPauseButton(GameObject pause)
		{
			// The window is usually still inactive when this runs the first
			// time (the button must exist before the player ever opens it), so
			// lay it out now and judge buttons by their own active flag.
			RectTransform prt = pause.GetComponent<RectTransform>();
			if (prt != null)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(prt);
			}

			// The menu is the vertical list of button rows: the smallest
			// layout whose children all carry a labelled button (Quit to main
			// menu, Resume). Picking by geometry alone goes wrong while the
			// window has never been laid out.
			Transform menu = null;
			int bestCount = int.MaxValue;
			foreach (VerticalLayoutGroup v in pause.GetComponentsInChildren<VerticalLayoutGroup>(true))
			{
				Transform t = v.transform;
				if (t == pause.transform || t.childCount < 2)
				{
					continue;
				}
				bool rows = true;
				for (int i = 0; i < t.childCount && rows; i++)
				{
					Transform c = t.GetChild(i);
					Button b = c.GetComponentInChildren<Button>(true);
					RectTransform r = c as RectTransform;
					rows = b != null && b.GetComponentInChildren<TMP_Text>(true) != null
						&& r != null && r.rect.width >= 300f;
				}
				if (rows && t.childCount < bestCount)
				{
					bestCount = t.childCount;
					menu = t;
				}
			}
			if (menu == null)
			{
				ModLog.Error("ChoiceWindow: menu list not found - settings button not added");
				return;
			}
			// The bottom row is the template (Resume).
			Transform row = menu.GetChild(menu.childCount - 1);


			GameObject clone = UnityEngine.Object.Instantiate(row.gameObject, menu);
			clone.name = "BranteSaveLoad_LoadAnyRow";
			clone.transform.SetSiblingIndex(row.GetSiblingIndex() + 1);
			foreach (Behaviour bh in clone.GetComponentsInChildren<Behaviour>(true))
			{
				if (bh.GetType().Name.IndexOf("ocaliz", StringComparison.Ordinal) >= 0)
				{
					UnityEngine.Object.Destroy(bh);
				}
			}
			Button cb = clone.GetComponentInChildren<Button>(true);
			cb.onClick = new Button.ButtonClickedEvent();
			cb.onClick.AddListener(delegate { Open(); });
			foreach (TMP_Text t in clone.GetComponentsInChildren<TMP_Text>(true))
			{
				t.text = Loc.T(Loc.K.Title);
			}
			_pauseBtn = clone;

			// Lift the whole settings sheet (its parchment and contents) so the
			// new bottom row fits inside it. The container has no layout group of
			// its own, so the move sticks.
			Transform sheet = menu;
			while (sheet.parent != null && sheet.parent != pause.transform)
			{
				sheet = sheet.parent;
			}
			RectTransform srt = sheet.GetComponent<RectTransform>();
			srt.anchoredPosition = new Vector2(srt.anchoredPosition.x, srt.anchoredPosition.y + 80f);
			// One row taller, top edge kept: the sheet is a fixed-size panel.
			srt.sizeDelta = new Vector2(srt.sizeDelta.x, srt.sizeDelta.y + 110f);
			srt.anchoredPosition = new Vector2(srt.anchoredPosition.x, srt.anchoredPosition.y - 55f);
			Canvas.ForceUpdateCanvases();
			ModLog.Info("ChoiceWindow: settings button added as a copy of row '" + row.name
				+ "' (" + menu.childCount + " rows; sheet '" + sheet.name + "' lifted and extended)");
		}

		// ------------------------------------------------------------------
		// The save window
		// ------------------------------------------------------------------

		// Returns the number of saves shown (used by the dev UI test).
		public static int Open()
		{
			// The labels are built from the game's localisation (and with its
			// font), so a language change makes them stale: rebuild the window.
			if (_canvas != null && _builtLanguage.Length > 0 && CurrentLanguage() != _builtLanguage)
			{
				ModLog.Info("ChoiceWindow: language changed ('" + _builtLanguage
					+ "' -> '" + CurrentLanguage() + "'), rebuilding the window");
				DestroyWindow();
			}
			if (_canvas == null)
			{
				BuildCanvas();
			}
			int n = RefreshItems();
			_lastItemCount = n;
			if (_scrollRect != null)
			{
				_scrollRect.verticalNormalizedPosition = 1f;
			}
			_canvas.SetActive(true);
			// Build the layout and TMP meshes NOW: rows are created at zero
			// width and only sized by the layout groups on the first canvas
			// rebuild; without this flush the text paints seconds late.
			Canvas.ForceUpdateCanvases();
			MeasureMarquees();
			ModLog.Info("ChoiceWindow: opened (" + n + " save(s))");
			return n;
		}

		public static void Close()
		{
			CancelConfirm();
			if (_canvas != null)
			{
				_canvas.SetActive(false);
			}
		}

		private static void BuildCanvas()
		{
			_canvas = CreateUiObject("BranteSaveLoad_ChoiceWindow", null);
			// Our own overlay canvas: independent of the game's window
			// layout, destroyed only by us (survives scene transitions).
			UnityEngine.Object.DontDestroyOnLoad(_canvas);
			Canvas canvas = _canvas.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 8000; // above the game's UI (see dump log)
			// Width is always the reference width (1920 units), whatever the
			// display's resolution or aspect ratio; height follows the screen.
			CanvasScaler scaler = _canvas.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 1f;
			_canvas.AddComponent<GraphicRaycaster>();
			_canvas.SetActive(false);

			_triUp = MakeTriangle(true);
			_triDown = MakeTriangle(false);
			_dashH = MakeDash(true);
			_dashV = MakeDash(false);

			// Dim backdrop: blocks clicks to the game; click = close.
			GameObject dim = CreateUiObject("Dim", _canvas.transform);
			Stretch(dim.GetComponent<RectTransform>());
			Image dimImg = dim.AddComponent<Image>();
			dimImg.color = ColDim;
			Button dimBtn = dim.AddComponent<Button>();
			dimBtn.targetGraphic = dimImg;
			dimBtn.transition = Selectable.Transition.None;
			dimBtn.onClick.AddListener(Close);

			// Panel: fixed reference width, height from the screen.
			GameObject panel = CreateUiObject("Panel", _canvas.transform);
			RectTransform prt = panel.GetComponent<RectTransform>();
			prt.anchorMin = new Vector2(0.5f, 0.03f);
			prt.anchorMax = new Vector2(0.5f, 0.97f);
			prt.pivot = new Vector2(0.5f, 0.5f);
			prt.anchoredPosition = Vector2.zero;
			prt.sizeDelta = new Vector2(PanelWidth, 0f);
			Image pimg = panel.AddComponent<Image>();
			pimg.color = ColPanel;

			// Title.
			TextMeshProUGUI title = AddLabel(panel.transform, "Title",
				Loc.T(Loc.K.Title), 34f, ColTextDark, false);
			title.alignment = TextAlignmentOptions.MidlineLeft;
			RectTransform trt = title.rectTransform;
			trt.anchorMin = new Vector2(0f, 1f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.pivot = new Vector2(0.5f, 1f);
			trt.anchoredPosition = new Vector2(0f, -14f);
			trt.sizeDelta = new Vector2(-120f, 52f);

			// Close [x].
			GameObject closeBtn = CreateUiObject("Close", panel.transform);
			RectTransform crt = closeBtn.GetComponent<RectTransform>();
			crt.anchorMin = new Vector2(1f, 1f);
			crt.anchorMax = new Vector2(1f, 1f);
			crt.pivot = new Vector2(1f, 1f);
			crt.anchoredPosition = new Vector2(-14f, -14f);
			crt.sizeDelta = new Vector2(46f, 46f);
			Image cimg = closeBtn.AddComponent<Image>();
			cimg.color = Color.clear;
			Button cbtn = closeBtn.AddComponent<Button>();
			cbtn.targetGraphic = cimg;
			cbtn.onClick.AddListener(Close);
			AddCross(closeBtn.transform, 26f, 4f);

			BuildHeader(panel.transform);

			// Scroll area: viewport + scroll rect + content + scrollbar.
			GameObject scroll = CreateUiObject("Scroll", panel.transform);
			RectTransform srt = scroll.GetComponent<RectTransform>();
			srt.anchorMin = new Vector2(0f, 0f);
			srt.anchorMax = new Vector2(1f, 1f);
			srt.offsetMin = new Vector2(22f, 86f);
			srt.offsetMax = new Vector2(-22f, -162f);
			_scrollRect = scroll.AddComponent<ScrollRect>();
			_scrollRect.horizontal = false;
			_scrollRect.vertical = true;
			_scrollRect.movementType = ScrollRect.MovementType.Clamped;
			_scrollRect.scrollSensitivity = 30f;

			GameObject viewport = CreateUiObject("Viewport", scroll.transform);
			RectTransform vrt = viewport.GetComponent<RectTransform>();
			Stretch(vrt);
			vrt.offsetMax = new Vector2(-24f, 0f); // room for the scrollbar
			viewport.AddComponent<RectMask2D>();
			_scrollRect.viewport = vrt;

			GameObject content = CreateUiObject("Content", viewport.transform);
			RectTransform cont = content.GetComponent<RectTransform>();
			cont.anchorMin = new Vector2(0f, 1f);
			cont.anchorMax = new Vector2(1f, 1f);
			cont.pivot = new Vector2(0.5f, 1f);
			cont.anchoredPosition = Vector2.zero;
			cont.sizeDelta = Vector2.zero;
			VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
			vlg.padding = new RectOffset(6, 6, 4, 4);
			vlg.spacing = 4f;
			vlg.childAlignment = TextAnchor.UpperCenter;
			vlg.childControlWidth = true;
			vlg.childControlHeight = true;
			vlg.childForceExpandWidth = true;
			vlg.childForceExpandHeight = false;
			ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
			fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			_scrollRect.content = cont;
			_itemContainer = content.transform;

			BuildScrollbar(scroll.transform);

			// Save now (bottom centre).
			_saveNowBtn = CreateUiObject("SaveNow", panel.transform);
			RectTransform nrt = _saveNowBtn.GetComponent<RectTransform>();
			nrt.anchorMin = new Vector2(0.5f, 0f);
			nrt.anchorMax = new Vector2(0.5f, 0f);
			nrt.pivot = new Vector2(0.5f, 0f);
			nrt.anchoredPosition = new Vector2(0f, 20f);
			nrt.sizeDelta = new Vector2(260f, 52f);
			Image nimg = _saveNowBtn.AddComponent<Image>();
			nimg.color = ColLoadButton;
			Button nbtn = _saveNowBtn.AddComponent<Button>();
			nbtn.targetGraphic = nimg;
			nbtn.onClick.AddListener(delegate { Ask(Pending.SaveNow, "", "", null); });
			TextMeshProUGUI nlabel = AddLabel(_saveNowBtn.transform, "Label",
				Loc.T(Loc.K.SaveNow), 24f, ColTextLight, true);
			Stretch(nlabel.rectTransform);

			// History (bottom right): the scene path this playthrough took,
			// as recorded by the game itself (Phase 6).
			_historyBtn = CreateUiObject("HistoryBtn", panel.transform);
			RectTransform hrt2 = _historyBtn.GetComponent<RectTransform>();
			hrt2.anchorMin = new Vector2(1f, 0f);
			hrt2.anchorMax = new Vector2(1f, 0f);
			hrt2.pivot = new Vector2(1f, 0f);
			hrt2.anchoredPosition = new Vector2(-22f, 20f);
			hrt2.sizeDelta = new Vector2(220f, 52f);
			Image himg2 = _historyBtn.AddComponent<Image>();
			himg2.color = ColButtonDark;
			Button hbtn2 = _historyBtn.AddComponent<Button>();
			hbtn2.targetGraphic = himg2;
			hbtn2.onClick.AddListener(OpenHistory);
			TextMeshProUGUI hlabel = AddLabel(_historyBtn.transform, "Label",
				Loc.T(Loc.K.History), 24f, ColTextLight, true);
			Stretch(hlabel.rectTransform);

			BuildConfirm(panel.transform);
			BuildHistory(panel.transform);
			_builtLanguage = CurrentLanguage();
		}

		// I2's current language code ("" if the manager is not up yet).
		private static string CurrentLanguage()
		{
			try
			{
				return I2.Loc.LocalizationManager.CurrentLanguage ?? "";
			}
			catch
			{
				return "";
			}
		}

		// Drops the window and every reference into it, so the next Open()
		// builds a fresh one in the current language.
		private static void DestroyWindow()
		{
			Close();
			if (_canvas != null)
			{
				UnityEngine.Object.Destroy(_canvas);
			}
			_canvas = null;
			_scrollRect = null;
			_itemContainer = null;
			_headerLabels = null;
			_sortIcons = null;
			_saveNowBtn = null;
			_historyBtn = null;
			_historyPanel = null;
			_historyList = null;
			_confirmPanel = null;
			_confirmText = null;
			_confirmLoadLabel = null;
			_confirmLoadBtn = null;
			_markObj = null;
			_entries = new List<SaveEntry>();
			_lastItemCount = 0;
			_firstItemPath = null;
			_firstItemTitle = null;
		}

		// ------------------------------------------------------------------
		// History panel (Phase 6)
		// ------------------------------------------------------------------

		// The game records the scenes a playthrough has passed through, in
		// order (ShowedScenesList). This shows that path; it is read-only,
		// because the save does not record which answer was clicked.
		private static void BuildHistory(Transform panel)
		{
			_historyPanel = CreateUiObject("HistoryPanel", panel);
			Stretch(_historyPanel.GetComponent<RectTransform>());
			Image overlay = _historyPanel.AddComponent<Image>();
			overlay.color = new Color(0f, 0f, 0f, 0.55f);

			GameObject box = CreateUiObject("Box", _historyPanel.transform);
			RectTransform brt = box.GetComponent<RectTransform>();
			brt.anchorMin = new Vector2(0.5f, 0.5f);
			brt.anchorMax = new Vector2(0.5f, 0.5f);
			brt.pivot = new Vector2(0.5f, 0.5f);
			brt.anchoredPosition = Vector2.zero;
			brt.sizeDelta = new Vector2(980f, 700f);
			Image bimg = box.AddComponent<Image>();
			bimg.color = ColPanel;

			TextMeshProUGUI title = AddLabel(box.transform, "Title",
				Loc.T(Loc.K.Playthrough), 32f, ColTextDark, false);
			title.alignment = TextAlignmentOptions.MidlineLeft;
			RectTransform trt = title.rectTransform;
			trt.anchorMin = new Vector2(0f, 1f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.pivot = new Vector2(0.5f, 1f);
			trt.anchoredPosition = new Vector2(-60f, -12f);
			trt.sizeDelta = new Vector2(-120f, 46f);

			TextMeshProUGUI sub = AddLabel(box.transform, "Sub",
				Loc.T(Loc.K.PlaythroughSub), 19f, ColTextDark, false);
			RectTransform srt2 = sub.rectTransform;
			srt2.anchorMin = new Vector2(0f, 1f);
			srt2.anchorMax = new Vector2(1f, 1f);
			srt2.pivot = new Vector2(0.5f, 1f);
			srt2.anchoredPosition = new Vector2(0f, -58f);
			srt2.sizeDelta = new Vector2(-120f, 30f);

			// Scrollable list.
			GameObject scroll = CreateUiObject("Scroll", box.transform);
			RectTransform srt = scroll.GetComponent<RectTransform>();
			srt.anchorMin = new Vector2(0f, 0f);
			srt.anchorMax = new Vector2(1f, 1f);
			srt.offsetMin = new Vector2(26f, 96f);
			srt.offsetMax = new Vector2(-26f, -96f);
			ScrollRect sc = scroll.AddComponent<ScrollRect>();
			sc.horizontal = true;
			sc.vertical = true;
			sc.movementType = ScrollRect.MovementType.Clamped;
			sc.scrollSensitivity = 30f;

			GameObject viewport = CreateUiObject("Viewport", scroll.transform);
			Stretch(viewport.GetComponent<RectTransform>());
			viewport.AddComponent<RectMask2D>();
			sc.viewport = viewport.GetComponent<RectTransform>();

			// The tree is positioned by hand (columns and lines), so no layout
			// group and no size fitter here.
			GameObject content = CreateUiObject("Content", viewport.transform);
			RectTransform cont = content.GetComponent<RectTransform>();
			cont.anchorMin = new Vector2(0f, 1f);
			cont.anchorMax = new Vector2(0f, 1f);
			cont.pivot = new Vector2(0f, 1f);
			cont.anchoredPosition = Vector2.zero;
			cont.sizeDelta = new Vector2(100f, 100f);
			sc.content = cont;
			_historyList = content.transform;
			_historyScroll = sc;

			// Hover info line, above the Close button.
			_historyInfo = AddLabel(box.transform, "Info", "", 18f,
				new Color(0.30f, 0.26f, 0.20f), false);
			RectTransform irt2 = _historyInfo.rectTransform;
			irt2.anchorMin = new Vector2(0f, 0f);
			irt2.anchorMax = new Vector2(1f, 0f);
			irt2.pivot = new Vector2(0.5f, 0f);
			irt2.anchoredPosition = new Vector2(0f, 82f);
			irt2.sizeDelta = new Vector2(-52f, 26f);
			_historyInfo.overflowMode = TextOverflowModes.Ellipsis;
			_historyInfo.enableWordWrapping = false;

			GameObject close = CreateUiObject("Close", box.transform);
			RectTransform crt = close.GetComponent<RectTransform>();
			crt.anchorMin = new Vector2(0.5f, 0f);
			crt.anchorMax = new Vector2(0.5f, 0f);
			crt.pivot = new Vector2(0.5f, 0f);
			crt.anchoredPosition = new Vector2(0f, 24f);
			crt.sizeDelta = new Vector2(240f, 52f);
			Image cimg = close.AddComponent<Image>();
			cimg.color = ColCancelButton;
			Button cbtn = close.AddComponent<Button>();
			cbtn.targetGraphic = cimg;
			cbtn.onClick.AddListener(CloseHistory);
			TextMeshProUGUI cl = AddLabel(close.transform, "Label", Loc.T(Loc.K.Close), 24f, ColTextLight, true);
			Stretch(cl.rectTransform);

			// Explore toggle (bottom left) and "Load this scene" (bottom right).
			_exploreBtn = CreateUiObject("ExploreBtn", box.transform);
			RectTransform ert = _exploreBtn.GetComponent<RectTransform>();
			ert.anchorMin = new Vector2(0f, 0f);
			ert.anchorMax = new Vector2(0f, 0f);
			ert.pivot = new Vector2(0f, 0f);
			ert.anchoredPosition = new Vector2(24f, 24f);
			ert.sizeDelta = new Vector2(220f, 52f);
			Image eimg = _exploreBtn.AddComponent<Image>();
			eimg.color = ColCancelButton;
			Button ebtn = _exploreBtn.AddComponent<Button>();
			ebtn.targetGraphic = eimg;
			ebtn.onClick.AddListener(ToggleExplore);
			_exploreLabel = AddLabel(_exploreBtn.transform, "Label",
				Loc.T(Loc.K.Explore), 22f, ColTextLight, true);
			Stretch(_exploreLabel.rectTransform);

			_loadHereBtn = CreateUiObject("LoadHere", box.transform);
			RectTransform lrt2 = _loadHereBtn.GetComponent<RectTransform>();
			lrt2.anchorMin = new Vector2(1f, 0f);
			lrt2.anchorMax = new Vector2(1f, 0f);
			lrt2.pivot = new Vector2(1f, 0f);
			lrt2.anchoredPosition = new Vector2(-24f, 24f);
			lrt2.sizeDelta = new Vector2(260f, 52f);
			Image limg2 = _loadHereBtn.AddComponent<Image>();
			limg2.color = ColLoadButton;
			Button lbtn2 = _loadHereBtn.AddComponent<Button>();
			lbtn2.targetGraphic = limg2;
			lbtn2.onClick.AddListener(LoadPreviewed);
			TextMeshProUGUI llabel2 = AddLabel(_loadHereBtn.transform, "Label",
				Loc.T(Loc.K.LoadScene), 22f, ColTextLight, true);
			Stretch(llabel2.rectTransform);
			_loadHereBtn.SetActive(false);

			_historyPanel.SetActive(false);
		}

		private static void ToggleExplore()
		{
			_explore = !_explore;
			_preview.Clear();
			_overrides.Clear();
			ModLog.Info("ChoiceWindow: explore mode " + (_explore ? "on" : "off"));
			RefreshHistory();
			Canvas.ForceUpdateCanvases();
		}

		// Loads the scene the explore path currently ends on, when the mod has
		// a save of it.
		private static void LoadPreviewed()
		{
			if (string.IsNullOrEmpty(_lastLoadablePath) || _tree.Count == 0)
			{
				return;
			}
			TreeCol last = _tree[_tree.Count - 1];
			CloseHistory();
			Ask(Pending.Load, _lastLoadablePath, last.Title, null);
		}

		private static void OpenHistory()
		{
			if (_historyPanel == null)
			{
				return;
			}
			int n = RefreshHistory();
			_historyPanel.SetActive(true);
			_historyPanel.transform.SetAsLastSibling();
			Canvas.ForceUpdateCanvases();
			ScrollHistoryToEnd();
			ModLog.Info("ChoiceWindow: history opened (" + n + " scene(s), "
				+ _historyLoadable + " loadable)");
		}

		private static void CloseHistory()
		{
			if (_historyPanel != null)
			{
				_historyPanel.SetActive(false);
			}
			_explore = false;
			_preview.Clear();
			_overrides.Clear();
		}

		// (Re)builds the history rows; returns how many are shown.
		// The history as a left-to-right tree: one column per captured
		// decision, its offered choices stacked vertically, the answer taken
		// on a darker path. Hovering a choice shows its full text; clicking
		// one loads that decision's save.
		private const float TreeColW = 360f;
		private const float TreeNodeW = 290f;
		private const float TreeNodeH = 48f;
		private const float TreeRowH = 58f;
		private const float TreeNodeX = 46f;
		private const float TreeTop = 58f;

		private static int RefreshHistory()
		{
			if (_historyList == null)
			{
				return 0;
			}
			for (int i = _historyList.childCount - 1; i >= 0; i--)
			{
				GameObject old = _historyList.GetChild(i).gameObject;
				old.SetActive(false);
				UnityEngine.Object.Destroy(old);
			}
			_historyLoadable = 0;
			SetHistoryInfo(null);

			List<History.Entry> all = History.FromCurrentGame();
			if (all.Count == 0)
			{
				all = History.FromNewestSave();
			}
			// Every played scene with a known answer list becomes a column; only
			// the ones the mod captured can be loaded (the rest are pre-mod).
			// Columns with Preview=true are the explore-mode ones and survive a
			// re-render until the player closes or toggles the mode.
			List<TreeCol> cols = new List<TreeCol>();
			foreach (History.Entry e in all)
			{
				if (e.Choices.Count == 0)
				{
					continue;
				}
				TreeCol col = new TreeCol();
				col.Scene = e.SceneName;
				col.Title = e.Title;
				col.SnapshotPath = e.SnapshotPath;
				col.Choices = e.Choices;
				string picked;
				if (_overrides.TryGetValue(e.SceneName, out picked))
				{
					foreach (History.Choice c in col.Choices)
					{
						c.Chosen = c.Name == picked;
					}
				}
				cols.Add(col);
			}
			foreach (TreeCol preview in _preview)
			{
				cols.Add(preview);
			}
			_tree = cols;
			if (cols.Count == 0)
			{
				TextMeshProUGUI empty = AddLabel(_historyList, "Empty",
					Loc.T(Loc.K.NothingYet), 20f, ColTextDark, true);
				RectTransform ert = empty.rectTransform;
				ert.anchorMin = new Vector2(0f, 1f);
				ert.anchorMax = new Vector2(0f, 1f);
				ert.pivot = new Vector2(0f, 1f);
				ert.sizeDelta = new Vector2(400f, 40f);
				SetContentSize(420f, 80f);
				return 0;
			}

			int rows = 0;
			foreach (TreeCol c in cols)
			{
				rows = Mathf.Max(rows, c.Choices.Count);
			}
			float width = 30f + cols.Count * TreeColW;
			float height = TreeTop + rows * TreeRowH + 20f;
			SetContentSize(width, height);

			for (int i = 0; i < cols.Count; i++)
			{
				TreeCol e = cols[i];
				float x = 30f + i * TreeColW;

				// Column header: the scene's title.
				TextMeshProUGUI head = AddLabel(_historyList, "Head", e.Title,
					18f, e.Preview ? new Color(0.45f, 0.40f, 0.32f) : ColTextDark, false);
				Wrap(head, x, 6f, TreeNodeW + TreeNodeX, 44f);
				head.enableWordWrapping = true;
				head.overflowMode = TextOverflowModes.Ellipsis;

				// Choices, top to bottom; the chosen one carries the dark path.
				int chosenY = -1;
				float spineTop = TreeTop + (TreeRowH - TreeNodeH) * 0.5f;
				float spineBottom = spineTop + (e.Choices.Count - 1) * TreeRowH;
				AddLine(x + 16f, spineTop + TreeNodeH * 0.5f, x + 16f,
					spineBottom + TreeNodeH * 0.5f, ColTreeLine, 3f);

				for (int k = 0; k < e.Choices.Count; k++)
				{
					History.Choice c = e.Choices[k];
					if (string.IsNullOrEmpty(c.Text) || c.Text.Trim().Length == 0)
					{
						continue;   // never draw a blank answer row
					}
					float y = TreeTop + k * TreeRowH;
					float cy = y + TreeNodeH * 0.5f;
					Color line = c.Chosen ? ColTreeChosen : ColTreeLine;
					AddLine(x + 16f, cy, x + TreeNodeX, cy, line, c.Chosen ? 5f : 3f);

					GameObject node = CreateUiObject("Choice", _historyList);
					RectTransform nrt = node.GetComponent<RectTransform>();
					nrt.anchorMin = new Vector2(0f, 1f);
					nrt.anchorMax = new Vector2(0f, 1f);
					nrt.pivot = new Vector2(0f, 1f);
					nrt.anchoredPosition = new Vector2(x + TreeNodeX, -y);
					nrt.sizeDelta = new Vector2(TreeNodeW, TreeNodeH);
					Image nimg = node.AddComponent<Image>();
					nimg.color = c.Chosen ? ColTreeChosenBg : ColAuto;
					// Explore mode: picking any answer extends the path. Otherwise
					// clicking loads that decision's save (captured scenes only).
					Button nb = node.AddComponent<Button>();
					nb.targetGraphic = nimg;
					int colIndex = i;
					int choiceIndex = k;
					string path = e.SnapshotPath;
					string title = e.Title;
					if (_explore)
					{
						nb.onClick.AddListener(delegate { Explore(colIndex, choiceIndex); });
					}
					else if (e.HasSnapshot)
					{
						nb.onClick.AddListener(delegate
						{
							CloseHistory();
							Ask(Pending.Load, path, title, null);
						});
						_historyLoadable++;
					}

					TextMeshProUGUI label = AddLabel(node.transform, "Text",
						c.Text, 17f, ColTextDark, false);
					RectTransform lrt = label.rectTransform;
					lrt.anchorMin = Vector2.zero;
					lrt.anchorMax = Vector2.one;
					lrt.offsetMin = new Vector2(10f, 2f);
					lrt.offsetMax = new Vector2(-10f, -2f);
					label.overflowMode = TextOverflowModes.Ellipsis;
					label.enableWordWrapping = false;

					TreeHover hover = node.AddComponent<TreeHover>();
					hover.Info = c.Text + (c.Chosen ? "   \u2022 " + Loc.T(Loc.K.TreeChosen)
						: "   \u2022 " + Loc.T(Loc.K.TreeOffered));
					hover.OnShow = SetHistoryInfo;

					if (c.Chosen)
					{
						chosenY = k;
					}
				}

				// Link this column's answer to the next column.
				if (chosenY >= 0 && i + 1 < cols.Count)
				{
					float fromY = TreeTop + chosenY * TreeRowH + TreeNodeH * 0.5f;
					int nextChosen = -1;
					for (int k = 0; k < cols[i + 1].Choices.Count; k++)
					{
						if (cols[i + 1].Choices[k].Chosen)
						{
							nextChosen = k;
							break;
						}
					}
					float toY = nextChosen >= 0
						? TreeTop + nextChosen * TreeRowH + TreeNodeH * 0.5f
						: TreeTop + TreeNodeH * 0.5f;
					float nextSpine = x + TreeColW + 16f;
					AddLine(x + 16f, fromY, nextSpine, fromY, ColTreeChosen, 5f);
					if (Mathf.Abs(toY - fromY) > 0.5f)
					{
						AddLine(nextSpine, fromY, nextSpine, toY, ColTreeChosen, 5f);
					}
				}
			}
			UpdateExploreButtons();
			// In explore mode the newest column waits for a pick; say so softly.
			if (_explore && _tree.Count > 0)
			{
				TreeCol last = _tree[_tree.Count - 1];
				bool picked = false;
				foreach (History.Choice c in last.Choices)
				{
					picked = picked || c.Chosen;
				}
				if (!picked)
				{
					SetHistoryInfo(Loc.T(Loc.K.PickAnswer));
				}
			}
			return cols.Count;
		}

		// The Explore toggle, and "Load this scene" for the last column (only
		// enabled when the mod has a save of that scene).
		private static void UpdateExploreButtons()
		{
			if (_exploreLabel != null)
			{
				_exploreLabel.text = Loc.T(Loc.K.Explore) + (_explore ? "  \u2713" : "");
				Image img = _exploreBtn != null ? _exploreBtn.GetComponent<Image>() : null;
				if (img != null)
				{
					img.color = _explore ? ColLoadButton : ColCancelButton;
				}
			}
			if (_loadHereBtn != null)
			{
				bool canLoad = false;
				string path = "";
				if (_explore && _tree.Count > 0)
				{
					TreeCol last = _tree[_tree.Count - 1];
					canLoad = last.HasSnapshot;
					path = last.SnapshotPath;
				}
				_loadHereBtn.SetActive(canLoad);
				_lastLoadablePath = canLoad ? path : "";
			}
		}

		private static string _lastLoadablePath = "";

		// Explore mode: picking an answer moves the preview to that answer's
		// scene, so the player can walk a path without playing it.
		private static void Explore(int colIndex, int choiceIndex)
		{
			try
			{
				if (colIndex < 0 || colIndex >= _tree.Count)
				{
					return;
				}
				TreeCol col = _tree[colIndex];
				if (choiceIndex < 0 || choiceIndex >= col.Choices.Count)
				{
					return;
				}
				History.Choice picked = col.Choices[choiceIndex];

				// Remember the choice for every column (played ones survive the
				// rebuild through _overrides).
				if (!col.Preview)
				{
					_overrides[col.Scene] = picked.Name;
				}
				for (int i = 0; i < col.Choices.Count; i++)
				{
					col.Choices[i].Chosen = i == choiceIndex;
				}

				// Everything after the clicked column is replaced by the new path.
				if (col.Preview)
				{
					int keep = colIndex - PlayedCount() + 1;
					_preview.RemoveRange(keep, _preview.Count - keep);
				}
				else
				{
					_preview.Clear();
				}

				string next = SceneEdges.Next(col.Scene);
				ModLog.Info("ChoiceWindow: explore " + col.Scene + " [" + picked.Name + "] -> "
					+ (string.IsNullOrEmpty(next) ? "(no edge)" : next));
				if (!string.IsNullOrEmpty(next))
				{
					_preview.Add(PreviewCol(next));
				}
				RefreshHistory();
				Canvas.ForceUpdateCanvases();
				ScrollHistoryToEnd();
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: explore failed:\n" + e);
			}
		}

		private static int PlayedCount()
		{
			int n = 0;
			foreach (TreeCol c in _tree)
			{
				if (!c.Preview)
				{
					n++;
				}
			}
			return n;
		}

		private static TreeCol PreviewCol(string scene)
		{
			TreeCol col = new TreeCol();
			col.Scene = scene;
			col.Preview = true;
			col.Title = SceneTexts.Title(scene);
			if (string.IsNullOrEmpty(col.Title))
			{
				col.Title = scene;
			}
			col.SnapshotPath = History.SnapshotFor(scene);
			int index = 1;
			foreach (KeyValuePair<string, string> b in SceneTexts.Buttons(scene))
			{
				History.Choice c = new History.Choice();
				c.Index = index++;
				c.Name = b.Key;
				c.Text = b.Value;
				col.Choices.Add(c);
			}
			return col;
		}

		// Sets the content rect of a scroll view that is laid out by hand.
		private static void SetContentSize(float width, float height)
		{
			RectTransform rt = _historyList as RectTransform;
			if (rt != null)
			{
				rt.sizeDelta = new Vector2(width, height);
			}
		}

		// Positions a label at the top-left of the history content.
		private static void Wrap(TextMeshProUGUI label, float x, float y, float width, float height)
		{
			RectTransform rt = label.rectTransform;
			rt.anchorMin = new Vector2(0f, 1f);
			rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0f, 1f);
			rt.anchoredPosition = new Vector2(x, -y);
			rt.sizeDelta = new Vector2(width, height);
		}

		// An axis-aligned connector line in the tree.
		private static void AddLine(float x1, float y1, float x2, float y2, Color color, float thickness)
		{
			GameObject go = CreateUiObject("Line", _historyList);
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0f, 1f);
			rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0f, 1f);
			if (Mathf.Abs(y2 - y1) < 0.5f)
			{
				rt.anchoredPosition = new Vector2(Mathf.Min(x1, x2), -y1 + thickness * 0.5f);
				rt.sizeDelta = new Vector2(Mathf.Abs(x2 - x1), thickness);
			}
			else
			{
				rt.anchoredPosition = new Vector2(x1 - thickness * 0.5f, -Mathf.Max(y1, y2));
				rt.sizeDelta = new Vector2(thickness, Mathf.Abs(y2 - y1));
			}
			Image img = go.AddComponent<Image>();
			img.color = color;
			img.raycastTarget = false;
		}

		// The newest column is the interesting one (the end of the path, or the
		// scene explore mode just reached), so show the right-hand end.
		private static void ScrollHistoryToEnd()
		{
			if (_historyScroll != null)
			{
				_historyScroll.horizontalNormalizedPosition = 1f;
			}
		}

		private static void SetHistoryInfo(string text)
		{
			if (_historyInfo != null)
			{
				_historyInfo.text = string.IsNullOrEmpty(text) ? "" : text;
			}
		}

		private static void BuildScrollbar(Transform scroll)
		{
			GameObject track = CreateUiObject("Scrollbar", scroll);
			RectTransform trt = track.GetComponent<RectTransform>();
			trt.anchorMin = new Vector2(1f, 0f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.pivot = new Vector2(1f, 0.5f);
			trt.offsetMin = new Vector2(-18f, 0f);
			trt.offsetMax = new Vector2(-6f, 0f);
			Image timg = track.AddComponent<Image>();
			timg.color = new Color(0f, 0f, 0f, 0.12f);
			Scrollbar sb = track.AddComponent<Scrollbar>();
			sb.direction = Scrollbar.Direction.BottomToTop;

			GameObject handle = CreateUiObject("Handle", track.transform);
			Stretch(handle.GetComponent<RectTransform>());
			Image himg = handle.AddComponent<Image>();
			himg.color = ColButtonDark;
			sb.targetGraphic = himg;
			sb.handleRect = handle.GetComponent<RectTransform>();

			_scrollRect.verticalScrollbar = sb;
			_scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
		}

		// Column header: fixed above the scroll area, same cell widths as
		// the rows. Clicking a header sorts by that column; the triangle
		// shows the direction.
		private static void BuildHeader(Transform panel)
		{
			GameObject header = CreateUiObject("Header", panel);
			RectTransform hrt = header.GetComponent<RectTransform>();
			hrt.anchorMin = new Vector2(0f, 1f);
			hrt.anchorMax = new Vector2(1f, 1f);
			hrt.pivot = new Vector2(0.5f, 1f);
			hrt.offsetMin = new Vector2(22f, -(122f + HeaderHeight));
			hrt.offsetMax = new Vector2(-46f, -122f);
			HorizontalLayoutGroup hlg = header.AddComponent<HorizontalLayoutGroup>();
			hlg.padding = new RectOffset(6, 6, 0, 0);
			hlg.spacing = 0f;
			hlg.childControlWidth = true;
			hlg.childControlHeight = true;
			hlg.childForceExpandWidth = false;
			hlg.childForceExpandHeight = true;

			_headerLabels = new TextMeshProUGUI[ColWidths.Length];
			_sortIcons = new Image[ColWidths.Length];
			for (int i = 0; i < ColWidths.Length; i++)
			{
				Col col = (Col)i;
				GameObject cell = CreateUiObject("Head_" + col, header.transform);
				Image img = cell.AddComponent<Image>();
				img.color = ColButtonDark;
				Button btn = cell.AddComponent<Button>();
				btn.targetGraphic = img;
				btn.onClick.AddListener(delegate { SetSort(col); });
				LayoutElement le = cell.AddComponent<LayoutElement>();
				le.preferredWidth = ColWidths[i];
				if (col == Col.Choice)
				{
					le.flexibleWidth = 1f;
				}
				TextMeshProUGUI label = AddLabel(cell.transform, "Label", ColName(col), 19f, ColTextLight,
					col != Col.Choice);
				label.overflowMode = TextOverflowModes.Ellipsis;
				label.enableWordWrapping = false;
				RectTransform lrt = label.rectTransform;
				Stretch(lrt);
				lrt.offsetMin = new Vector2(8f, 0f);
				lrt.offsetMax = new Vector2(-22f, 0f); // room for the triangle
				_headerLabels[i] = label;

				// Filled triangle, full text height, centred vertically.
				GameObject icon = CreateUiObject("Sort", cell.transform);
				RectTransform irt = icon.GetComponent<RectTransform>();
				irt.anchorMin = new Vector2(1f, 0.5f);
				irt.anchorMax = new Vector2(1f, 0.5f);
				irt.pivot = new Vector2(1f, 0.5f);
				irt.anchoredPosition = new Vector2(-8f, 0f);
				irt.sizeDelta = new Vector2(14f, 14f);
				Image iimg = icon.AddComponent<Image>();
				iimg.color = ColTextLight;
				iimg.raycastTarget = false;
				iimg.enabled = false;
				_sortIcons[i] = iimg;
			}
			// Blank cell over the delete column, so headers line up with rows.
			GameObject spacer = CreateUiObject("Head_Delete", header.transform);
			Image simg = spacer.AddComponent<Image>();
			simg.color = ColButtonDark;
			LayoutElement sle = spacer.AddComponent<LayoutElement>();
			sle.preferredWidth = DeleteWidth;
		}

		private static void SetSort(Col col)
		{
			if (_sortCol == col)
			{
				_sortAsc = !_sortAsc;
			}
			else
			{
				_sortCol = col;
				_sortAsc = true;
			}
			SortEntries();
			UpdateHeader();
			BuildRows();
			Canvas.ForceUpdateCanvases();
			// The marquee measures in Update; without this the choice text is
			// painted at zero width for one frame (the "blink" on sorting).
			MeasureMarquees();
		}

		private static void UpdateHeader()
		{
			if (_headerLabels == null)
			{
				return;
			}
			for (int i = 0; i < _sortIcons.Length; i++)
			{
				bool active = (Col)i == _sortCol;
				_sortIcons[i].enabled = active;
				_sortIcons[i].sprite = _sortAsc ? _triUp : _triDown;
			}
		}

		// Confirm dialog: covers the panel (blocks row clicks), shows the
		// pending save's title, Load / Delete / Save.
		private static void BuildConfirm(Transform panel)
		{
			_confirmPanel = CreateUiObject("Confirm", panel);
			Stretch(_confirmPanel.GetComponent<RectTransform>());
			Image overlay = _confirmPanel.AddComponent<Image>();
			overlay.color = new Color(0f, 0f, 0f, 0.45f);
			Button overlayBtn = _confirmPanel.AddComponent<Button>();
			overlayBtn.targetGraphic = overlay;
			overlayBtn.transition = Selectable.Transition.None;
			overlayBtn.onClick.AddListener(CancelConfirm); // click outside = cancel

			GameObject box = CreateUiObject("Box", _confirmPanel.transform);
			RectTransform brt = box.GetComponent<RectTransform>();
			brt.anchorMin = new Vector2(0.5f, 0.5f);
			brt.anchorMax = new Vector2(0.5f, 0.5f);
			brt.pivot = new Vector2(0.5f, 0.5f);
			brt.anchoredPosition = Vector2.zero;
			// Tall enough for title + blank + choice + blank + note
			// without ellipsizing.
			brt.sizeDelta = new Vector2(640f, 380f);
			Image bimg = box.AddComponent<Image>();
			bimg.color = new Color(0.96f, 0.94f, 0.88f);

			_confirmText = AddLabel(box.transform, "Msg",
				"Are you sure?", 24f, ColTextDark, false);
			_confirmText.alignment = TextAlignmentOptions.TopLeft;
			_confirmText.enableWordWrapping = true;
			RectTransform mrt = _confirmText.rectTransform;
			mrt.anchorMin = new Vector2(0f, 0f);
			mrt.anchorMax = new Vector2(1f, 1f);
			mrt.offsetMin = new Vector2(30f, 92f);
			mrt.offsetMax = new Vector2(-30f, -26f);

			GameObject loadBtn = CreateUiObject("Load", box.transform);
			RectTransform lrt = loadBtn.GetComponent<RectTransform>();
			lrt.anchorMin = new Vector2(0f, 0f);
			lrt.anchorMax = new Vector2(0f, 0f);
			lrt.pivot = new Vector2(0f, 0f);
			lrt.anchoredPosition = new Vector2(30f, 26f);
			lrt.sizeDelta = new Vector2(250f, 56f);
			Image limg = loadBtn.AddComponent<Image>();
			limg.color = ColLoadButton;
			_confirmLoadBtn = loadBtn.AddComponent<Button>();
			_confirmLoadBtn.targetGraphic = limg;
			_confirmLoadBtn.onClick.AddListener(delegate { RunPending(); });
			TextMeshProUGUI lt = AddLabel(loadBtn.transform, "Label", Loc.T(Loc.K.Load), 24f, ColTextLight, true);
			Stretch(lt.rectTransform);
			_confirmLoadLabel = lt;

			GameObject cancelBtn = CreateUiObject("Cancel", box.transform);
			RectTransform xrt = cancelBtn.GetComponent<RectTransform>();
			xrt.anchorMin = new Vector2(1f, 0f);
			xrt.anchorMax = new Vector2(1f, 0f);
			xrt.pivot = new Vector2(1f, 0f);
			xrt.anchoredPosition = new Vector2(-30f, 26f);
			xrt.sizeDelta = new Vector2(250f, 56f);
			Image ximg = cancelBtn.AddComponent<Image>();
			ximg.color = ColCancelButton;
			Button xbtn = cancelBtn.AddComponent<Button>();
			xbtn.targetGraphic = ximg;
			xbtn.onClick.AddListener(CancelConfirm);
			TextMeshProUGUI xt = AddLabel(cancelBtn.transform, "Label", Loc.T(Loc.K.Cancel), 24f, ColTextLight, true);
			Stretch(xt.rectTransform);

			_confirmPanel.SetActive(false);
		}

		// Opens the confirm dialog for one action. path/title describe the
		// save; row is the table row it came from (marked while the dialog
		// is open), or null.
		private static void Ask(Pending action, string path, string title, GameObject row)
		{
			ClearMark();
			_pendingAction = action;
			_pendingPath = path;
			_pendingTitle = title;
			string body;
			string label;
			switch (action)
			{
				case Pending.Load:
					body = string.Format(Loc.T(Loc.K.ConfirmLoad), title);
					label = Loc.T(Loc.K.Load);
					break;
				case Pending.Delete:
					body = string.Format(Loc.T(Loc.K.ConfirmDelete), title);
					label = Loc.T(Loc.K.Delete);
					break;
				default:
					body = Loc.T(Loc.K.ConfirmSave);
					label = Loc.T(Loc.K.Save);
					break;
			}
			if (_confirmText != null)
			{
				_confirmText.text = body;
			}
			if (_confirmLoadLabel != null)
			{
				_confirmLoadLabel.text = label;
			}
			if (row != null)
			{
				MarkRow(row);
			}
			if (_confirmPanel != null)
			{
				_confirmPanel.SetActive(true);
			}
			// Paint the new text immediately (see Open's flush note).
			Canvas.ForceUpdateCanvases();
			ModLog.Info("ChoiceWindow: " + action + " requested"
				+ (string.IsNullOrEmpty(path) ? "" : " for " + Path.GetFileName(path)));
		}

		private static void RunPending()
		{
			switch (_pendingAction)
			{
				case Pending.Load:
					LoadSnapshot(_pendingPath);
					break;
				case Pending.Delete:
					DeleteSnapshot(_pendingPath);
					break;
				case Pending.SaveNow:
					SaveNow();
					break;
			}
		}

		private static void SaveNow()
		{
			CancelConfirm();
			try
			{
				Recorder.SaveManual();
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: manual save failed:\n" + e);
			}
			_lastItemCount = RefreshItems();
			Canvas.ForceUpdateCanvases();
		}

		// Removes a save file + sidecar and refreshes the list. Only ever
		// touches the mod's own folders.
		private static void DeleteSnapshot(string datPath)
		{
			try
			{
				File.Delete(datPath);
				string sidecar = Path.Combine(Path.GetDirectoryName(datPath),
					Path.GetFileNameWithoutExtension(datPath) + ".meta.txt");
				if (File.Exists(sidecar))
				{
					File.Delete(sidecar);
				}
				ModLog.Info("ChoiceWindow: removed " + Path.GetFileName(datPath));
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: deleting " + datPath + " failed:\n" + e);
			}
			CancelConfirm();
			_lastItemCount = RefreshItems();
			Canvas.ForceUpdateCanvases();
			ModLog.Info("ChoiceWindow: " + _lastItemCount + " save(s) remain");
		}

		private static void CancelConfirm()
		{
			ClearMark();
			if (_confirmPanel != null)
			{
				_confirmPanel.SetActive(false);
			}
		}

		// Red dashed outline around the row whose delete is being confirmed.
		// Four tiled strips, layout-ignored, drawn on top of the row.
		private static void MarkRow(GameObject row)
		{
			const float t = 3f;
			GameObject mark = CreateUiObject("DeleteMark", row.transform);
			Stretch(mark.GetComponent<RectTransform>());
			LayoutElement le = mark.AddComponent<LayoutElement>();
			le.ignoreLayout = true;
			AddDashStrip(mark.transform, "Top", true, 1f, t, _dashH);
			AddDashStrip(mark.transform, "Bottom", true, 0f, t, _dashH);
			AddDashStrip(mark.transform, "Left", false, 0f, t, _dashV);
			AddDashStrip(mark.transform, "Right", false, 1f, t, _dashV);
			_markObj = mark;
		}

		// A full-length strip along one edge: horizontal ones sit at the top
		// (edge 1) or bottom (edge 0), vertical ones at the right (1) or left (0).
		private static void AddDashStrip(Transform parent, string name, bool horizontal, float edge,
			float thick, Sprite sprite)
		{
			GameObject go = CreateUiObject(name, parent);
			RectTransform rt = go.GetComponent<RectTransform>();
			if (horizontal)
			{
				rt.anchorMin = new Vector2(0f, edge);
				rt.anchorMax = new Vector2(1f, edge);
				rt.pivot = new Vector2(0.5f, edge);
				rt.sizeDelta = new Vector2(0f, thick);
			}
			else
			{
				rt.anchorMin = new Vector2(edge, 0f);
				rt.anchorMax = new Vector2(edge, 1f);
				rt.pivot = new Vector2(edge, 0.5f);
				rt.sizeDelta = new Vector2(thick, 0f);
			}
			rt.anchoredPosition = Vector2.zero;
			Image img = go.AddComponent<Image>();
			img.sprite = sprite;
			img.type = Image.Type.Tiled;
			img.color = ColDashRed;
			img.raycastTarget = false;
		}

		private static void ClearMark()
		{
			if (_markObj != null)
			{
				_markObj.SetActive(false);
				UnityEngine.Object.Destroy(_markObj);
			}
			_markObj = null;
		}

		// ------------------------------------------------------------------
		// Table data
		// ------------------------------------------------------------------

		// Reloads every save from disk, re-sorts and rebuilds the rows.
		// Returns the number of saves.
		private static int RefreshItems()
		{
			if (_itemContainer == null)
			{
				return 0;
			}
			string run = Recorder.CurrentRunId();
			_entries = ReadEntries(Recorder.RunDir(run, "Auto"), "Auto");
			_entries.AddRange(ReadEntries(Recorder.RunDir(run, "Manual"), "Manual"));
			SortEntries();
			UpdateHeader();
			BuildRows();
			return _entries.Count;
		}

		// Reads every save in one folder. The save file itself is the
		// source of truth for the table values (same deserialiser the load
		// uses); the sidecar only supplies the localised choice title.
		private static List<SaveEntry> ReadEntries(string dir, string kind)
		{
			List<SaveEntry> list = new List<SaveEntry>();
			if (!Directory.Exists(dir))
			{
				return list;
			}
			string[] files;
			try
			{
				files = Directory.GetFiles(dir, "*.dat");
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: cannot list saves:\n" + e);
				return list;
			}
			foreach (string f in files)
			{
				try
				{
					SaveLoadHelper h;
					using (FileStream fs = File.OpenRead(f))
					{
						h = (SaveLoadHelper)new BinaryFormatter().Deserialize(fs);
					}
					SaveEntry entry = new SaveEntry();
					entry.Dat = f;
					entry.Kind = kind;
					entry.Title = ReadTitle(f);
					// The sidecar holds the real capture time; the file's own
					// timestamp can be newer (e.g. after the run-folder move).
					DateTime recorded = ReadRecorded(f);
					entry.Saved = recorded != DateTime.MinValue ? recorded : File.GetLastWriteTime(f);
					entry.Chapter = h.Chapter;
					entry.ChapterKey = h.ChapterName ?? "";
					entry.ChapterName = StageName(h.ChapterName);
					entry.Hero = h.HeroName ?? "";
					entry.Age = h.HeroAge;
					entry.Year = h.Year;
					entry.Season = h.Season;
					entry.Turn = h.Turn;
					entry.Scene = h.LastOpenScene ?? "";
					list.Add(entry);
				}
				catch (Exception e)
				{
					ModLog.Error("ChoiceWindow: cannot read save " + f + ":\n" + e);
				}
			}
			return list;
		}

		// "Chapter1.Childhood" -> "Childhood".
		private static string StageName(string chapterKey)
		{
			if (string.IsNullOrEmpty(chapterKey))
			{
				return "";
			}
			int dot = chapterKey.IndexOf('.');
			return dot >= 0 ? chapterKey.Substring(dot + 1) : chapterKey;
		}

		// The chapter, translated from its key ("Chapter1.Childhood"), with
		// the stripped name and then the number as fallbacks.
		private static string ChapterText(SaveEntry e)
		{
			string t = Translate(e.ChapterKey);
			if (!string.IsNullOrEmpty(t) && t != e.ChapterKey)
			{
				return t;
			}
			if (e.ChapterName.Length > 0)
			{
				return e.ChapterName;
			}
			return e.Chapter.ToString();
		}

		// A game localisation key in the current language, key as fallback.
		private static string Translate(string key)
		{
			try
			{
				string t = I2.Loc.LocalizationManager.GetTranslation(key);
				return string.IsNullOrEmpty(t) ? key : t;
			}
			catch
			{
				return key;
			}
		}

		// The scene's title in the current language, "" if the key is missing.
		private static string TitleOf(string scene)
		{
			if (string.IsNullOrEmpty(scene))
			{
				return "";
			}
			try
			{
				string t = I2.Loc.LocalizationManager.GetTranslation(scene + "_Title");
				return string.IsNullOrEmpty(t) || t == scene + "_Title" ? "" : t;
			}
			catch
			{
				return "";
			}
		}

		// The sidecar's "recorded=" timestamp, or DateTime.MinValue.
		private static DateTime ReadRecorded(string datPath)
		{
			string sidecar = Path.Combine(Path.GetDirectoryName(datPath),
				Path.GetFileNameWithoutExtension(datPath) + ".meta.txt");
			try
			{
				if (!File.Exists(sidecar))
				{
					return DateTime.MinValue;
				}
				foreach (string raw in File.ReadAllLines(sidecar))
				{
					string line = raw.Trim();
					if (line.StartsWith("recorded=", StringComparison.Ordinal))
					{
						return DateTime.ParseExact(line.Substring(9), "yyyy-MM-dd HH:mm:ss",
							System.Globalization.CultureInfo.InvariantCulture);
					}
				}
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: recorded-time read failed for " + datPath + ":\n" + e);
			}
			return DateTime.MinValue;
		}

		// The localised scene title from the sidecar, without the key tag.
		private static string ReadTitle(string datPath)
		{
			string sidecar = Path.Combine(Path.GetDirectoryName(datPath),
				Path.GetFileNameWithoutExtension(datPath) + ".meta.txt");
			try
			{
				if (!File.Exists(sidecar))
				{
					return "";
				}
				foreach (string raw in File.ReadAllLines(sidecar))
				{
					string line = raw.Trim();
					if (line.StartsWith("title=", StringComparison.Ordinal))
					{
						string v = line.Substring(6);
						int key = v.IndexOf("   [key:", StringComparison.Ordinal);
						if (key >= 0)
						{
							v = v.Substring(0, key);
						}
						return v.Trim();
					}
				}
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: title read failed for " + datPath + ":\n" + e);
			}
			return "";
		}

		private static void SortEntries()
		{
			Col col = _sortCol;
			bool asc = _sortAsc;
			_entries.Sort(delegate (SaveEntry a, SaveEntry b)
			{
				int r = SortKey(a, col).CompareTo(SortKey(b, col));
				if (r == 0)
				{
					r = string.CompareOrdinal(a.Dat, b.Dat);
				}
				return asc ? r : -r;
			});
		}

		private static IComparable SortKey(SaveEntry e, Col col)
		{
			switch (col)
			{
				case Col.Type: return e.Kind;
				case Col.Saved: return e.Saved;
				case Col.Chapter: return e.Chapter;
				case Col.Age: return e.Age;
				case Col.Year: return e.Year;
				case Col.Season: return e.Season;
				case Col.Turn: return e.Turn;
				case Col.Scene: return e.Scene;
				default: return e.Choice.ToLowerInvariant();
			}
		}

		// (Re)builds the table rows from _entries, in their current order.
		// Gives every scrolling cell its width/ellipsis before the frame is
		// drawn, instead of one frame later on the marquee's first Update.
		private static void MeasureMarquees()
		{
			if (_itemContainer == null)
			{
				return;
			}
			foreach (Marquee m in _itemContainer.GetComponentsInChildren<Marquee>(true))
			{
				m.MeasureNow();
			}
		}

		private static void BuildRows()
		{
			ClearMark();
			for (int i = _itemContainer.childCount - 1; i >= 0; i--)
			{
				GameObject old = _itemContainer.GetChild(i).gameObject;
				old.SetActive(false);
				UnityEngine.Object.Destroy(old);
			}
			_firstItemPath = null;
			_firstItemTitle = null;

			if (_entries.Count == 0)
			{
				TextMeshProUGUI empty = AddLabel(_itemContainer, "Empty",
					Loc.T(Loc.K.EmptySaves), 22f, ColTextDark, true);
				LayoutElement le = empty.gameObject.AddComponent<LayoutElement>();
				le.preferredHeight = 60f;
				return;
			}
			for (int i = 0; i < _entries.Count; i++)
			{
				CreateRow(_entries[i], i);
			}
		}

		private static void CreateRow(SaveEntry e, int index)
		{
			bool manual = e.Kind == "Manual";
			Color bg = manual
				? (index % 2 == 0 ? ColManual : ColManualAlt)
				: (index % 2 == 0 ? ColAuto : ColAutoAlt);
			string p = e.Dat;
			string t = e.Choice;
			GameObject row = CreateUiObject("Row", _itemContainer);
			LayoutElement rle = row.AddComponent<LayoutElement>();
			rle.preferredHeight = RowHeight;
			Image img = row.AddComponent<Image>();
			img.color = bg;
			Button btn = row.AddComponent<Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(delegate { Ask(Pending.Load, p, t, null); });
			HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
			hlg.padding = new RectOffset(6, 6, 6, 6);
			hlg.spacing = 0f;
			hlg.childControlWidth = true;
			hlg.childControlHeight = true;
			hlg.childForceExpandWidth = false;
			hlg.childForceExpandHeight = true;

			string[] values =
			{
				e.Kind == "Manual" ? Loc.T(Loc.K.TypeManual) : Loc.T(Loc.K.TypeAuto),
				e.Saved.ToString("yyyy-MM-dd HH:mm"),
				// Shown by name (translated); the column still sorts on the
				// chapter number.
				ChapterText(e),
				e.Age.ToString(),
				e.Year.ToString(),
				e.Season.ToString(),
				e.Turn.ToString(),
				e.Scene,
				e.Choice
			};
			for (int i = 0; i < values.Length; i++)
			{
				Col col = (Col)i;
				bool scrolls = col == Col.Choice;
				Transform cellParent = row.transform;
				GameObject mask = null;
				if (scrolls)
				{
					// The choice name scrolls when it is too long: a clipped
					// cell holding the full text (see Marquee).
					mask = CreateUiObject("ChoiceCell", row.transform);
					LayoutElement mle = mask.AddComponent<LayoutElement>();
					mle.preferredWidth = ColWidths[i];
					mle.flexibleWidth = 1f;
					mask.AddComponent<RectMask2D>();
					cellParent = mask.transform;
				}
				TextMeshProUGUI cell = AddLabel(cellParent, "Cell", values[i], 19f, ColTextDark,
					!scrolls);
				cell.overflowMode = TextOverflowModes.Ellipsis;
				cell.enableWordWrapping = false;
				if (scrolls)
				{
					RectTransform crt = cell.rectTransform;
					crt.anchorMin = new Vector2(0f, 0f);
					crt.anchorMax = new Vector2(0f, 1f);
					crt.pivot = new Vector2(0f, 0.5f);
					crt.anchoredPosition = Vector2.zero;
					crt.sizeDelta = Vector2.zero;
					Marquee mq = mask.AddComponent<Marquee>();
					mq.Viewport = mask.GetComponent<RectTransform>();
					mq.Text = cell;
					// Stagger the rows so the table does not move in lockstep.
					mq.Delay = 1.2f + 0.35f * (index % 5);
				}
				else
				{
					LayoutElement le = cell.gameObject.AddComponent<LayoutElement>();
					le.preferredWidth = ColWidths[i];
				}
			}

			// Delete at the far right: a red cell with a drawn x. As a child
			// of the row it sits above the row's own button, so a click
			// reaches only this one.
			string dp = e.Dat;
			string dt = e.Choice;
			GameObject del = CreateUiObject("Delete", row.transform);
			LayoutElement dle = del.AddComponent<LayoutElement>();
			dle.preferredWidth = DeleteWidth;
			Image dimg = del.AddComponent<Image>();
			dimg.color = Color.clear;
			Button dbtn = del.AddComponent<Button>();
			dbtn.targetGraphic = dimg;
			dbtn.onClick.AddListener(delegate { Ask(Pending.Delete, dp, dt, row); });
			AddCross(del.transform, 24f, 4f);

			if (_firstItemPath == null)
			{
				_firstItemPath = e.Dat;
				_firstItemTitle = e.Choice;
			}
		}

		// ------------------------------------------------------------------
		// Load flow (the vanilla sequence, see class comment)
		// ------------------------------------------------------------------

		public static void LoadSnapshot(string datPath)
		{
			if (string.IsNullOrEmpty(datPath))
			{
				ModLog.Error("ChoiceWindow: no save selected");
				return;
			}
			if (ModHost.Current == null)
			{
				ModLog.Error("ChoiceWindow: no host to run the load on");
				return;
			}
			ModHost.Current.StartCoroutine(LoadFlow(datPath));
		}

		private static IEnumerator LoadFlow(string datPath)
		{
			// Read the save first: if it fails, the window stays open
			// and the player can pick another one.
			SaveLoadHelper helper = null;
			try
			{
				using (FileStream fs = File.OpenRead(datPath))
				{
					helper = (SaveLoadHelper)new BinaryFormatter().Deserialize(fs);
				}
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: cannot read save " + datPath + ":\n" + e);
				yield break;
			}

			ModLog.Info("ChoiceWindow: loading " + Path.GetFileName(datPath)
				+ " (scene=" + helper.LastOpenScene
				+ ", main save=" + (helper.SlotInfo != null ? helper.SlotInfo.Id : "")
				+ ", ironman=" + helper.IronManMode + ")");
			// The blob has no ID (for stable dedup), so the run comes from
			// the folder the file sits in; the game needs the REAL id (it
			// contains slashes) to find its own save folder.
			string runId = Recorder.RunIdFromPath(datPath);
			if (string.IsNullOrEmpty(runId) && helper.SlotInfo != null)
			{
				runId = helper.SlotInfo.Id;
			}
			// A raw id keeps its slashes; a folder name from RunsDir is
			// sanitised. Only ever adopt a raw one - otherwise the game would
			// create a second save folder under a mangled name.
			string realId = Recorder.RealRunId(runId);
			if (string.IsNullOrEmpty(realId))
			{
				realId = Recorder.LiveRealId();
			}
			if (string.IsNullOrEmpty(realId))
			{
				string blobId = helper.SlotInfo != null ? helper.SlotInfo.Id : "";
				realId = blobId != null && blobId.IndexOf('/') >= 0 ? blobId : "";
			}
			if (!string.IsNullOrEmpty(realId))
			{
				if (helper.SlotInfo == null)
				{
					helper.SlotInfo = new SaveSlotInfo();
				}
				helper.SlotInfo.Id = realId;
			}
			// Keep the pre-mod timeline instead of letting the game's saves
			// overwrite it in place (see Recorder.BackupOriginalSave).
			Recorder.BackupOriginalSave(realId);
			if (File.Exists(Path.Combine(Recorder.SnapshotDir, "dev-ironman.txt")))
			{
				helper.IronManMode = true;
				ModLog.Info("DEV IRONMAN: loading the save as an Iron Man game");
			}
			Recorder.PinRunId(runId);
			Close();
			try
			{
				UIManager ui = UIManager.Initiate;
				if (ui != null && ui.PauseWindow != null)
				{
					ui.PauseWindow.SetActive(false);
				}
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: closing pause window failed:\n" + e);
			}

			// The Instance static can be dead mid-game - the AGENTS.md rule.
			SaveLoadManager slm = SaveLoadManager.Instance;
			if (slm == null)
			{
				slm = UnityEngine.Object.FindObjectOfType<SaveLoadManager>();
			}
			if (slm == null)
			{
				ModLog.Error("ChoiceWindow: SaveLoadManager not found - load aborted");
				yield break;
			}
			Recorder.AdoptAsCurrentSave(slm, helper);
			try
			{
				slm.LoadGame(helper);
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: LoadGame failed:\n" + e);
				yield break;
			}

			// The vanilla transition dance, exactly as the loading screen
			// does it (FINDINGS, "Phase 3 capture facts").
			yield return new WaitForSeconds(0.5f);
			AsyncOperation ao = null;
			try
			{
				ao = SceneManager.LoadSceneAsync("GameLoadingScreen", LoadSceneMode.Additive);
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: GameLoadingScreen failed to open:\n" + e);
			}
			while (ao != null && !ao.isDone)
			{
				yield return null;
			}
			ModLog.Info("ChoiceWindow: broadcasting StartSceneTransitionEvent");
			try
			{
				Messenger.Broadcast("StartSceneTransitionEvent");
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: transition broadcast failed:\n" + e);
			}
		}

		// ------------------------------------------------------------------
		// Dev UI test (marker: Snapshots/dev-uitest.txt)
		// ------------------------------------------------------------------

		// Driven by ModHost after the dev autoload has opened a choice
		// scene: opens the list, then simulates the clicks a player would
		// make (row -> Load, x -> Delete) through the same UnityEvent
		// handlers the real buttons use. Pauses between steps so a
		// screenshot can catch each state.
		public static void DevTest()
		{
			try
			{
				if (ModHost.Current != null)
				{
					ModHost.Current.StartCoroutine(DevTestRoutine());
				}
			}
			catch (Exception e)
			{
				ModLog.Error("DEV UITEST failed:\n" + e);
			}
		}

		private static IEnumerator DevTestRoutine()
		{
			int n = Open();
			ModLog.Info("DEV UITEST: window open with " + n + " item(s); first='"
				+ (_firstItemTitle ?? "<none>") + "'");
			if (_firstItemPath == null)
			{
				ModLog.Error("DEV UITEST: nothing to load");
				yield break;
			}
			// State A: the table.
			ModLog.Info("DEV UITEST: state A - table (3s)");
			yield return new WaitForSeconds(3f);

			// Simulated click 1: the row (its onClick -> Ask(Load)).
			Ask(Pending.Load, _firstItemPath, _firstItemTitle, null);
			ModLog.Info("DEV UITEST: state B - load confirm (3s)");
			yield return new WaitForSeconds(3f);

			// State C: the delete confirm. With 2+ rows, exercise the real
			// x button end-to-end (the seeded duplicate goes first, so the
			// original test save survives); with a single row just show the
			// dialog and cancel - the list must not end up empty.
			CancelConfirm();
			Ask(Pending.Delete, _firstItemPath, _firstItemTitle, null);
			ModLog.Info("DEV UITEST: state C - delete confirm (3s)");
			yield return new WaitForSeconds(3f);
			if (_lastItemCount >= 2 && _confirmLoadBtn != null)
			{
				ModLog.Info("DEV UITEST: clicking Delete on '" + _pendingTitle + "'");
				_confirmLoadBtn.onClick.Invoke(); // DeleteSnapshot + refresh
			}
			else
			{
				CancelConfirm();
			}
			Ask(Pending.Load, _firstItemPath, _firstItemTitle, null);

			// Simulated click 2: the confirm dialog's Load button (the real
			// Button component, so the mode-dispatching handler runs).
			ModLog.Info("DEV UITEST: clicking Load on '" + _pendingTitle + "'");
			if (_confirmLoadBtn != null)
			{
				_confirmLoadBtn.onClick.Invoke();
			}
			else
			{
				LoadSnapshot(_pendingPath);
			}
		}

		// Dev settings test (marker: Snapshots/dev-book.txt): opens the pause
		// menu, adds the button and logs the menu buttons' positions.
		public static void DevBook()
		{
			if (ModHost.Current != null)
			{
				ModHost.Current.StartCoroutine(DevBookDump());
			}
		}

		private static IEnumerator DevBookDump()
		{
			UIManager ui = UIManager.Initiate;
			if (ui != null)
			{
				ui.ShowPauseMenu();
			}
			yield return new WaitForSeconds(2f);
			EnsurePauseButton();
			if (ui != null && ui.PauseWindow != null)
			{
				DumpTree(ui.PauseWindow.transform, 0);
			}
			yield return null;
			Canvas.ForceUpdateCanvases();
			yield return new WaitForSeconds(12f);
			Open();
			// Dev: leave time for a screenshot of the saves table, then the
			// history panel on top of it.
			yield return new WaitForSeconds(8f);
			OpenHistory();
			// Dev: exercise explore mode - walk two answers forward.
			yield return new WaitForSeconds(2f);
			ToggleExplore();
			yield return new WaitForSeconds(1f);
			for (int step = 0; step < 2 && _tree.Count > 0; step++)
			{
				Explore(_tree.Count - 1, 0);
				yield return new WaitForSeconds(1f);
			}
			yield return new WaitForSeconds(6f);
			CloseHistory();
			if (DevLanguageSwitch)
			{
				DevSwitchLanguage();
				yield return new WaitForSeconds(1f);
				Open();
			}
		}

		// Dev: flip the game language, to exercise the window rebuild.
		private static void DevSwitchLanguage()
		{
			try
			{
				string to = I2.Loc.LocalizationManager.CurrentLanguage == "Russian" ? "English" : "Russian";
				ModLog.Info("DEV LANG: switching to " + to);
				I2.Loc.LocalizationManager.SetLanguageAndCode(to, to == "Russian" ? "ru" : "en", false, true);
			}
			catch (Exception e)
			{
				ModLog.Error("DEV LANG: switch failed:\n" + e);
			}
		}

		private static void DumpTree(Transform t, int depth)
		{
			if (depth > 7)
			{
				return;
			}
			string comps = "";
			foreach (Component c in t.GetComponents<Component>())
			{
				if (c is Transform)
				{
					continue;
				}
				comps += " " + c.GetType().Name;
			}
			TMP_Text tt = t.GetComponent<TMP_Text>();
			RectTransform rt = t as RectTransform;
			ModLog.Info("DEV TREE: " + new string('-', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " (off)")
				+ (tt != null ? " text='" + tt.text + "'" : "")
				+ (rt != null ? " pos=" + rt.anchoredPosition + " size=" + rt.rect.size : "")
				+ " [" + comps.Trim() + "]");
			for (int i = 0; i < t.childCount; i++)
			{
				DumpTree(t.GetChild(i), depth + 1);
			}
		}

		// Logs a UI object's screen rect and its parent chain (name, active,
		// canvas), so placement problems show up in the log.
		private static void DumpWorld(string label, GameObject go)
		{
			RectTransform rt = go.GetComponent<RectTransform>();
			Vector3[] c = new Vector3[4];
			if (rt != null)
			{
				rt.GetWorldCorners(c);
			}
			ModLog.Info("DEV BOOK: " + label + " '" + go.name + "' active=" + go.activeInHierarchy
				+ " world bl=" + c[0] + " tr=" + c[2] + " size=" + (rt != null ? rt.rect.size.ToString() : "-"));
			Transform t = go.transform.parent;
			string chain = "";
			int depth = 0;
			while (t != null && depth < 12)
			{
				Canvas cv = t.GetComponent<Canvas>();
				chain += " > " + t.name + (t.gameObject.activeSelf ? "" : "(OFF)")
					+ (cv != null ? "[canvas order=" + cv.sortingOrder + " " + cv.renderMode + "]" : "")
					+ (t.GetComponent<RectMask2D>() != null ? "[mask]" : "")
					+ (t.GetComponent<ScrollRect>() != null ? "[scroll]" : "");
				t = t.parent;
				depth++;
			}
			ModLog.Info("DEV BOOK:    chain" + chain);
		}

		// ------------------------------------------------------------------
		// Small UI helpers
		// ------------------------------------------------------------------

		// Filled triangle, pointing up or down, sized for the header text.
		private static Sprite MakeTriangle(bool up)
		{
			const int w = 56;
			const int h = 56;
			Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
			Color32[] px = new Color32[w * h];
			for (int y = 0; y < h; y++)
			{
				float frac = (y + 0.5f) / h;
				float half = (up ? frac : 1f - frac) * w * 0.5f;
				for (int x = 0; x < w; x++)
				{
					bool on = Mathf.Abs(x + 0.5f - w * 0.5f) <= half;
					px[y * w + x] = on ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
				}
			}
			tex.SetPixels32(px);
			tex.filterMode = FilterMode.Bilinear;
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.Apply();
			return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
		}

		// One repeating dash pattern: 10px on, 6px off, 3px thick.
		private static Sprite MakeDash(bool horizontal)
		{
			const int period = 16;
			const int thick = 3;
			int w = horizontal ? period : thick;
			int h = horizontal ? thick : period;
			Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
			Color32[] px = new Color32[w * h];
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					int along = horizontal ? x : y;
					bool on = along % period < 10;
					px[y * w + x] = on ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
				}
			}
			tex.SetPixels32(px);
			tex.filterMode = FilterMode.Point;
			tex.wrapMode = TextureWrapMode.Repeat;
			tex.Apply();
			return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
		}

		// A drawn x (two bars), centred in the parent, so it does not depend
		// on any font glyph. Black; the parent turns it red on hover.
		private static void AddCross(Transform parent, float size, float thick)
		{
			GameObject cross = CreateUiObject("Cross", parent);
			RectTransform crt = cross.GetComponent<RectTransform>();
			crt.anchorMin = new Vector2(0.5f, 0.5f);
			crt.anchorMax = new Vector2(0.5f, 0.5f);
			crt.pivot = new Vector2(0.5f, 0.5f);
			crt.anchoredPosition = Vector2.zero;
			crt.sizeDelta = new Vector2(size, size);
			Image[] bars = new Image[2];
			for (int i = 0; i < 2; i++)
			{
				GameObject bar = CreateUiObject("Bar" + i, cross.transform);
				RectTransform brt = bar.GetComponent<RectTransform>();
				brt.anchorMin = new Vector2(0.5f, 0.5f);
				brt.anchorMax = new Vector2(0.5f, 0.5f);
				brt.pivot = new Vector2(0.5f, 0.5f);
				brt.anchoredPosition = Vector2.zero;
				brt.sizeDelta = new Vector2(size * 1.4f, thick);
				brt.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
				bars[i] = bar.AddComponent<Image>();
				bars[i].color = ColCrossNormal;
				bars[i].raycastTarget = false;
			}
			CrossHover hover = parent.gameObject.AddComponent<CrossHover>();
			hover.Bars = bars;
		}

		private static GameObject CreateUiObject(string name, Transform parent)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			if (parent != null)
			{
				go.GetComponent<RectTransform>().SetParent(parent, false);
			}
			return go;
		}

		private static void Stretch(RectTransform rt)
		{
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
		}

		// Creates a TMP label, applying the game's language font (the game
		// calls ChangeFontAsset the same way). Font first, style after.
		private static TextMeshProUGUI AddLabel(Transform parent, string name,
			string text, float size, Color color, bool centered)
		{
			GameObject go = CreateUiObject(name, parent);
			TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
			try
			{
				if (GameManager.Instance != null)
				{
					GameManager.Instance.ChangeFontAsset(tmp);
				}
			}
			catch (Exception e)
			{
				ModLog.Info("ChoiceWindow: font assignment skipped: " + e.Message);
			}
			tmp.text = text;
			tmp.fontSize = size;
			tmp.color = color;
			tmp.alignment = centered ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
			tmp.raycastTarget = false;
			tmp.overflowMode = TextOverflowModes.Ellipsis;
			return tmp;
		}
	}
}
