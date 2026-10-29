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
	// Opens a tree node's pop-up (full answer text and what it changes) while
	// the pointer is over the node: OnHover(true) on enter, (false) on exit.
	public sealed class TreeHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		public Action<bool> OnHover;
		public bool HasEffects;
		public bool HasChoiceOfRequirements;   // dev: an "one of" group

		public void OnPointerEnter(PointerEventData eventData)
		{
			if (OnHover != null)
			{
				OnHover(true);
			}
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			if (OnHover != null)
			{
				OnHover(false);
			}
		}
	}

	// The history tree is wide, not tall: while it fits vertically, the mouse
	// wheel scrolls it sideways (wheel down = towards the newest column).
	// Unity's ScrollRect only does that when vertical scrolling is switched
	// off, and then the wrong way round. Sits on the viewport, so it gets the
	// event before the ScrollRect above it.
	public sealed class TreeWheel : MonoBehaviour, IScrollHandler
	{
		public ScrollRect Scroll;
		private const float WheelColumns = 4f;

		public void OnScroll(PointerEventData eventData)
		{
			if (Scroll == null || Scroll.content == null || Scroll.viewport == null)
			{
				return;
			}
			bool fitsVertically = Scroll.content.rect.height <= Scroll.viewport.rect.height + 0.5f;
			if (fitsVertically && Mathf.Abs(eventData.scrollDelta.y) > Mathf.Abs(eventData.scrollDelta.x))
			{
				// Columns are 360 wide: a notch moves a third of one.
				eventData.scrollDelta = new Vector2(eventData.scrollDelta.y * WheelColumns, 0f);
			}
			Scroll.OnScroll(eventData);
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
		private static readonly Color ColTextLight = new Color(0.95f, 0.92f, 0.82f);
		private static readonly Color ColTextDark = new Color(0.12f, 0.10f, 0.08f);
		private static readonly Color ColDashRed = new Color(0.85f, 0.10f, 0.10f);
		private static readonly Color ColCrossNormal = new Color(0.05f, 0.04f, 0.03f);
		private static readonly Color ColTreeLine = new Color(0.55f, 0.50f, 0.42f, 0.55f);
		private static readonly Color ColTreeChosen = new Color(0.55f, 0.30f, 0.25f);
		private static readonly Color ColTreeChosenBg = new Color(1f, 0.93f, 0.72f);
		private static readonly Color ColTreeUpcomingBg = new Color(0.84f, 0.79f, 0.68f, 0.45f);
		private static readonly Color ColTreeMuted = new Color(0.45f, 0.40f, 0.32f);
		private static readonly Color ColTreeBlocked = new Color(0.72f, 0.70f, 0.66f, 0.55f);
		private static readonly Color ColTreeChapter = new Color(0.55f, 0.30f, 0.25f, 0.55f);
		private static readonly Color ColTreePlayedBand = new Color(0.55f, 0.30f, 0.25f, 0.07f);

		private enum Pending { Load, Delete, SaveNow, LoadPath }

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
					string live = Loc.SceneTitle(Scene);
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
		private static GameObject _undoBtn;
		private static GameObject _loadHereBtn;
		// The answer the player last clicked in the tree (the scene "Load
		// scene" goes to); every click is also kept in _overrides by scene.
		private static string _selScene;
		private static string _selChoice;

		// One tree column: a scene, the answers it offered, the one taken and
		// (for captured scenes) the snapshot to load.
		private sealed class TreeCol
		{
			public string Scene = "";
			public string Title = "";
			public string SnapshotPath = "";
			public bool Current;    // the scene on screen (the pending entry)
			public bool Recorded;   // the answer played here is known
			public string PlayedChoice = "";   // that answer's name
			public SceneEffects.Hero Hero;     // the hero at this scene along the path (null: unknown)
			public HashSet<string> Blocked = new HashSet<string>();   // answers he could not take
			public bool Upcoming;   // ahead of the current scene, from scene-flow.tsv
			public bool Possible;   // upcoming, but its conditions are not decided yet
			public List<History.Choice> Choices = new List<History.Choice>();

			public bool HasSnapshot
			{
				get { return !string.IsNullOrEmpty(SnapshotPath); }
			}
		}

		private static List<TreeCol> _tree = new List<TreeCol>();
		private static RectTransform _historyBox;
		private static RectTransform _historyProgress;
		private static Transform _statsRow;
		private static RectTransform _popup;
		private static TextMeshProUGUI _popupText;
		private static TextMeshProUGUI _popupNote;
		private static Transform _popupEffects;
		private static ScrollRect _historyScroll;
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
			CloseHistory();
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
			_saveNowBtn = AddButton(panel.transform, "SaveNow", new Vector2(0.5f, 0f), new Vector2(0f, 20f),
				new Vector2(260f, 52f), ColLoadButton, Loc.T(Loc.K.SaveNow), 24f,
				delegate { Ask(Pending.SaveNow, "", "", null); }).gameObject;

			// History (bottom right): the scene path this playthrough took,
			// as recorded by the game itself (Phase 6).
			_historyBtn = CreateUiObject("HistoryBtn", panel.transform);
			RectTransform hrt2 = _historyBtn.GetComponent<RectTransform>();
			hrt2.anchorMin = new Vector2(0.5f, 1f);
			hrt2.anchorMax = new Vector2(0.5f, 1f);
			hrt2.pivot = new Vector2(0.5f, 1f);
			hrt2.anchoredPosition = new Vector2(0f, -14f);
			hrt2.sizeDelta = new Vector2(250f, 56f);
			Image himg2 = _historyBtn.AddComponent<Image>();
			himg2.color = ColButtonDark;
			Button hbtn2 = _historyBtn.AddComponent<Button>();
			hbtn2.targetGraphic = himg2;
			hbtn2.onClick.AddListener(OpenHistory);
			GameObject hicon = CreateUiObject("Icon", _historyBtn.transform);
			RectTransform hirt = hicon.GetComponent<RectTransform>();
			hirt.anchorMin = new Vector2(0f, 0.5f);
			hirt.anchorMax = new Vector2(0f, 0.5f);
			hirt.pivot = new Vector2(0f, 0.5f);
			hirt.anchoredPosition = new Vector2(14f, 0f);
			hirt.sizeDelta = new Vector2(64f, 38f);
			Image hiImg = hicon.AddComponent<Image>();
			hiImg.sprite = MakeTreeIcon();
			hiImg.color = ColTextLight;
			hiImg.preserveAspect = true;
			hiImg.raycastTarget = false;
			TextMeshProUGUI hlabel = AddLabel(_historyBtn.transform, "Label",
				Loc.T(Loc.K.History), 23f, ColTextLight, true);
			Stretch(hlabel.rectTransform);
			hlabel.rectTransform.offsetMin = new Vector2(84f, 0f);
			hlabel.alignment = TextAlignmentOptions.MidlineLeft;

			// Both on the canvas itself: the tree is wider than the save window,
			// and the confirm dialog must be able to sit on top of the tree.
			BuildConfirm(_canvas.transform);
			BuildHistory(_canvas.transform);
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
			_historyScroll = null;
			_historyInfo = null;
			_historyBox = null;
			_historyProgress = null;
			_statsRow = null;
			_popup = null;
			_popupText = null;
			_popupNote = null;
			_popupEffects = null;
			_undoBtn = null;
			_loadHereBtn = null;
			_selScene = null;
			_selChoice = null;
			_overrides.Clear();
			_tree = new List<TreeCol>();
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

			// Nearly the whole screen: the more columns fit, the more of the
			// story is in view.
			GameObject box = CreateUiObject("Box", _historyPanel.transform);
			RectTransform brt = box.GetComponent<RectTransform>();
			Stretch(brt);
			brt.offsetMin = new Vector2(36f, 30f);
			brt.offsetMax = new Vector2(-36f, -30f);
			Image bimg = box.AddComponent<Image>();
			bimg.color = ColPanel;
			_historyBox = brt;

			TextMeshProUGUI title = AddLabel(box.transform, "Title",
				Loc.T(Loc.K.Playthrough), 32f, ColTextDark, false);
			title.alignment = TextAlignmentOptions.MidlineLeft;
			RectTransform trt = title.rectTransform;
			trt.anchorMin = new Vector2(0f, 1f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.pivot = new Vector2(0.5f, 1f);
			trt.anchoredPosition = new Vector2(0f, -12f);
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
			// Bottom edge clears the stats row (116..174) and the hint line
			// (82..108) below it.
			srt.offsetMin = new Vector2(26f, 182f);
			srt.offsetMax = new Vector2(-26f, -96f);
			ScrollRect sc = scroll.AddComponent<ScrollRect>();
			sc.horizontal = true;
			sc.vertical = true;
			sc.movementType = ScrollRect.MovementType.Clamped;
			sc.scrollSensitivity = 30f;

			GameObject viewport = CreateUiObject("Viewport", scroll.transform);
			RectTransform vrt = viewport.GetComponent<RectTransform>();
			Stretch(vrt);
			vrt.offsetMin = new Vector2(0f, 18f); // room for the scrollbar
			viewport.AddComponent<RectMask2D>();
			// Invisible, but a raycast target: without it the wheel only
			// reaches the tree while the pointer is on an answer.
			viewport.AddComponent<Image>().color = Color.clear;
			sc.viewport = vrt;
			viewport.AddComponent<TreeWheel>().Scroll = sc;

			// Horizontal scrollbar: the tree grows to the right, and without
			// one only dragging reveals the older columns.
			GameObject track = CreateUiObject("HScrollbar", scroll.transform);
			RectTransform hst = track.GetComponent<RectTransform>();
			hst.anchorMin = new Vector2(0f, 0f);
			hst.anchorMax = new Vector2(1f, 0f);
			hst.pivot = new Vector2(0.5f, 0f);
			hst.offsetMin = new Vector2(0f, 0f);
			hst.offsetMax = new Vector2(0f, 12f);
			Image htimg = track.AddComponent<Image>();
			htimg.color = new Color(0f, 0f, 0f, 0.12f);
			Scrollbar hsb = track.AddComponent<Scrollbar>();
			hsb.direction = Scrollbar.Direction.LeftToRight;
			// Progress through the story: the track filled in the path's red
			// from the start up to the scene on screen (SetStoryProgress),
			// under the handle.
			GameObject progress = CreateUiObject("Progress", track.transform);
			_historyProgress = progress.GetComponent<RectTransform>();
			_historyProgress.anchorMin = Vector2.zero;
			_historyProgress.anchorMax = new Vector2(0f, 1f);
			_historyProgress.offsetMin = Vector2.zero;
			_historyProgress.offsetMax = Vector2.zero;
			Image pimg2 = progress.AddComponent<Image>();
			pimg2.color = new Color(ColTreeChosen.r, ColTreeChosen.g, ColTreeChosen.b, 0.75f);
			pimg2.raycastTarget = false;
			GameObject handle = CreateUiObject("Handle", track.transform);
			Stretch(handle.GetComponent<RectTransform>());
			Image hhimg = handle.AddComponent<Image>();
			hhimg.color = ColButtonDark;
			hsb.targetGraphic = hhimg;
			hsb.handleRect = handle.GetComponent<RectTransform>();
			sc.horizontalScrollbar = hsb;
			sc.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

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

			// Hint line (explore mode), above the Close button.
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

			// Close: an x in the top right corner, as on the save window.
			GameObject closeX = CreateUiObject("Close", box.transform);
			RectTransform cxrt = closeX.GetComponent<RectTransform>();
			cxrt.anchorMin = new Vector2(1f, 1f);
			cxrt.anchorMax = new Vector2(1f, 1f);
			cxrt.pivot = new Vector2(1f, 1f);
			cxrt.anchoredPosition = new Vector2(-14f, -14f);
			cxrt.sizeDelta = new Vector2(46f, 46f);
			Image cximg = closeX.AddComponent<Image>();
			cximg.color = Color.clear;
			Button cxbtn = closeX.AddComponent<Button>();
			cxbtn.targetGraphic = cximg;
			cxbtn.onClick.AddListener(CloseHistory);
			AddCross(closeX.transform, 26f, 4f);

			// The hero's stats along the path (UpdateStats), above the hint line.
			GameObject stats = CreateUiObject("Stats", box.transform);
			RectTransform strt = stats.GetComponent<RectTransform>();
			strt.anchorMin = new Vector2(0f, 0f);
			strt.anchorMax = new Vector2(1f, 0f);
			strt.pivot = new Vector2(0.5f, 0f);
			strt.anchoredPosition = new Vector2(0f, 116f);
			strt.sizeDelta = new Vector2(-52f, 58f);
			Image stimg = stats.AddComponent<Image>();
			stimg.color = new Color(0f, 0f, 0f, 0.05f);
			HorizontalLayoutGroup sth = stats.AddComponent<HorizontalLayoutGroup>();
			sth.padding = new RectOffset(10, 10, 4, 4);
			sth.spacing = 2f;
			sth.childAlignment = TextAnchor.MiddleCenter;
			sth.childControlWidth = true;
			sth.childControlHeight = true;
			sth.childForceExpandWidth = true;
			sth.childForceExpandHeight = true;
			_statsRow = stats.transform;

			// Undo (bottom left): back to the answers actually played.
			_undoBtn = AddButton(box.transform, "Undo", new Vector2(0f, 0f), new Vector2(24f, 24f),
				new Vector2(64f, 52f), ColCancelButton, null, 0f, UndoSelections).gameObject;
			GameObject uicon = CreateUiObject("Icon", _undoBtn.transform);
			RectTransform uirt = uicon.GetComponent<RectTransform>();
			Stretch(uirt);
			uirt.offsetMin = new Vector2(14f, 8f);
			uirt.offsetMax = new Vector2(-14f, -8f);
			Image uiImg = uicon.AddComponent<Image>();
			uiImg.sprite = MakeUndoIcon();
			uiImg.color = ColTextLight;
			uiImg.preserveAspect = true;
			uiImg.raycastTarget = false;

			// Load scene (bottom right): goes to the selected answer's scene
			// along the selected path.
			_loadHereBtn = AddButton(box.transform, "LoadHere", new Vector2(1f, 0f), new Vector2(-24f, 24f),
				new Vector2(260f, 52f), ColLoadButton, Loc.T(Loc.K.LoadScene), 22f, LoadSelected).gameObject;
			_loadHereBtn.SetActive(false);

			BuildPopup(box.transform);
			_historyPanel.SetActive(false);
		}

		// Back to the answers actually played: drops every pick.
		private static void UndoSelections()
		{
			_overrides.Clear();
			_selScene = null;
			_selChoice = null;
			HidePopup();
			RefreshKeepingScroll();
			ModLog.Info("ChoiceWindow: tree selections reset");
		}

		// Clicking an answer selects it: the path follows it, the dashed mark
		// moves to it, and "Load scene" will go there. Nothing loads yet.
		private static void SelectChoice(string scene, string choice)
		{
			_overrides[scene] = choice;
			_selScene = scene;
			_selChoice = choice;
			RefreshKeepingScroll();
		}

		// Rebuilds the tree without moving the view.
		private static void RefreshKeepingScroll()
		{
			RectTransform content = _historyList as RectTransform;
			Vector2 at = content != null ? content.anchoredPosition : Vector2.zero;
			RefreshHistory(false);
			Canvas.ForceUpdateCanvases();
			if (content != null)
			{
				content.anchoredPosition = at;
			}
		}

		// How to reach the selected scene: the save to start from and the
		// answers to give on the way. The start is the newest scene with a
		// save at or before the first point where the path leaves what was
		// played (or the scene on screen, for scenes still ahead).
		private sealed class LoadPlan
		{
			public string StartPath;
			public string StartScene;
			public string Target;
			public string TargetTitle;
			public Dictionary<string, string> Picks = new Dictionary<string, string>();
		}

		private static LoadPlan PlanLoad(out string why)
		{
			why = "";
			int target = -1;
			int current = -1;
			for (int i = 0; i < _tree.Count; i++)
			{
				if (_tree[i].Scene == _selScene)
				{
					target = i;
				}
				if (_tree[i].Current)
				{
					current = i;
				}
			}
			if (target < 0)
			{
				return null;
			}
			int diverge = target;
			if (current >= 0 && current < diverge)
			{
				diverge = current;   // scenes ahead have to be played to
			}
			for (int i = 0; i < target; i++)
			{
				string pick;
				if (_overrides.TryGetValue(_tree[i].Scene, out pick) && pick != _tree[i].PlayedChoice)
				{
					diverge = Math.Min(diverge, i);
					break;
				}
			}
			int start = -1;
			for (int i = diverge; i >= 0; i--)
			{
				if (_tree[i].HasSnapshot && !_tree[i].Upcoming)
				{
					start = i;
					break;
				}
			}
			if (start < 0)
			{
				why = Loc.T(Loc.K.NoSaveBefore);
				return null;
			}
			LoadPlan plan = new LoadPlan();
			plan.StartPath = _tree[start].SnapshotPath;
			plan.StartScene = _tree[start].Scene;
			plan.Target = _tree[target].Scene;
			plan.TargetTitle = _tree[target].Title;
			for (int i = start; i < target; i++)
			{
				string pick;
				if (!_overrides.TryGetValue(_tree[i].Scene, out pick))
				{
					pick = _tree[i].PlayedChoice;
				}
				if (!string.IsNullOrEmpty(pick) && _tree[i].Blocked.Contains(pick))
				{
					why = string.Format(Loc.T(Loc.K.PickBlocked), _tree[i].Title);
					return null;
				}
				if (!string.IsNullOrEmpty(pick))
				{
					plan.Picks[_tree[i].Scene] = pick;
				}
			}
			return plan;
		}

		private static LoadPlan _pendingPlan;

		private static void LoadSelected()
		{
			string why;
			LoadPlan plan = PlanLoad(out why);
			if (plan == null)
			{
				SetHistoryInfo(why);
				return;
			}
			HidePopup();
			_pendingPlan = plan;
			Ask(Pending.LoadPath, plan.StartPath, plan.TargetTitle, null);
		}

		// Loads the plan's start and lets AutoPlay give the picked answers
		// until the selected scene is on screen (left unanswered).
		private static IEnumerator ReplayTo(LoadPlan plan)
		{
			ModLog.Info("ChoiceWindow: load scene " + plan.Target + " from " + plan.StartScene
				+ " with " + plan.Picks.Count + " answer(s) to give");
			LoadSnapshot(plan.StartPath);
			if (plan.StartScene == plan.Target)
			{
				yield break;
			}
			yield return AutoPlay.WaitForLoad();
			yield return AutoPlay.Run(delegate(string scene)
			{
				string pick;
				return plan.Picks.TryGetValue(scene, out pick) ? pick : null;
			}, delegate(string scene) { return scene != plan.Target; }, 60f, true);
		}

		private static void OpenHistory()
		{
			if (_historyPanel == null)
			{
				return;
			}
			int n = RefreshHistory(true);
			_historyPanel.SetActive(true);
			_historyPanel.transform.SetAsLastSibling();
			Canvas.ForceUpdateCanvases();
			ScrollHistoryToFocus();
			int ahead = 0, possible = 0;
			foreach (TreeCol c in _tree)
			{
				if (c.Upcoming)
				{
					ahead++;
					if (c.Possible)
					{
						possible++;
					}
				}
			}
			ModLog.Info("ChoiceWindow: history opened (" + n + " scene(s), "
				+ _historyLoadable + " loadable, " + ahead + " upcoming of which "
				+ possible + " possible)");
		}

		private static void CloseHistory()
		{
			HidePopup();
			if (_historyPanel != null)
			{
				_historyPanel.SetActive(false);
			}
			_overrides.Clear();
			_selScene = null;
			_selChoice = null;
		}

		// (Re)builds the tree; returns how many columns it shows. Left to
		// right, one column per scene - played, then ahead - its answers
		// stacked, the taken or picked one on a darker path. Hovering an answer
		// opens its pop-up; clicking picks it for the path (SelectChoice).
		private const float TreeColW = 360f;
		private const float TreeNodeW = 290f;
		private const float TreeNodeH = 48f;
		private const float TreeRowH = 58f;
		private const float TreeNodeX = 46f;
		private const float TreeTop = 102f;     // first answer row
		private const float TreeHeadY = 34f;    // column titles (chapter names above)
		private const float TreeBarY = 88f;     // the timeline bar under the titles

		// The columns as built when the tree opened: reading the history and
		// working out the scenes ahead is the slow part, and neither depends
		// on the player's picks, so a click only re-applies them.
		private static List<TreeCol> _baseCols = new List<TreeCol>();

		// One drawn answer, kept so a click can restyle it instead of the
		// whole tree being built again.
		private sealed class NodeRef
		{
			public TreeCol Col;
			public History.Choice Choice;
			public Image Img;
			public TextMeshProUGUI Label;
			public Button Btn;
			public GameObject Mark;
		}

		private static readonly List<NodeRef> _nodes = new List<NodeRef>();
		private static List<List<int>> _visible = new List<List<int>>();
		private static Transform _pathLayer;   // the lines that follow the picks

		// `rebuild` (on open): read the history and the scenes ahead and draw
		// everything. Otherwise (a pick, undo) re-apply the picks, restyle the
		// drawn answers and redraw only the path.
		private static int RefreshHistory(bool rebuild)
		{
			if (_historyList == null)
			{
				return 0;
			}
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			SetHistoryInfo(null);
			if (rebuild)
			{
				_baseCols = BuildColumns();
			}
			List<TreeCol> cols = _baseCols;
			// The player's picks replace what was played (or nothing, ahead).
			foreach (TreeCol col in cols)
			{
				string picked;
				if (!_overrides.TryGetValue(col.Scene, out picked))
				{
					picked = col.PlayedChoice;
				}
				foreach (History.Choice c in col.Choices)
				{
					c.Chosen = !string.IsNullOrEmpty(picked) && c.Name == picked;
				}
			}
			long tData = sw.ElapsedMilliseconds;
			WorkOutOpenAnswers(cols);
			long tHero = sw.ElapsedMilliseconds;
			_tree = cols;
			if (rebuild || _nodes.Count == 0 || _pathLayer == null)
			{
				DrawTree(cols);
			}
			else
			{
				foreach (NodeRef n in _nodes)
				{
					StyleNode(n);
				}
			}
			DrawPath(cols);
			UpdateStats();
			if (_loadHereBtn != null)
			{
				_loadHereBtn.SetActive(!string.IsNullOrEmpty(_selScene));
			}
			ModLog.Info("ChoiceWindow: tree " + (rebuild ? "built" : "restyled") + " in "
				+ sw.ElapsedMilliseconds + " ms (columns " + tData + ", path " + (tHero - tData)
				+ ", drawing " + (sw.ElapsedMilliseconds - tHero) + ")");
			return cols.Count;
		}

		// Everything that does not change with the picks: chapter rules, bands,
		// bars, titles, spines, pale connectors and the answer boxes.
		private static void DrawTree(List<TreeCol> cols)
		{
			for (int i = _historyList.childCount - 1; i >= 0; i--)
			{
				GameObject old = _historyList.GetChild(i).gameObject;
				old.SetActive(false);
				UnityEngine.Object.Destroy(old);
			}
			_nodes.Clear();
			_pathLayer = null;
			_historyLoadable = 0;
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
				SetStoryProgress(0f);
				return;
			}

			// Blank answers are never drawn, and the rows close up around them
			// (a gap would look like a missing answer).
			_visible = new List<List<int>>();
			int rows = 0;
			foreach (TreeCol c in cols)
			{
				List<int> v = VisibleChoices(c);
				_visible.Add(v);
				rows = Mathf.Max(rows, v.Count);
			}
			float width = 30f + cols.Count * TreeColW;
			float height = TreeTop + rows * TreeRowH + 20f;
			SetContentSize(width, height);
			SetStoryProgress(_focusCol >= 0 ? (30f + (_focusCol + 0.5f) * TreeColW) / width : 0f);

			int prevChapter = 0;
			for (int i = 0; i < cols.Count; i++)
			{
				TreeCol e = cols[i];
				List<int> shown = _visible[i];
				float x = 30f + i * TreeColW;
				if (e.HasSnapshot)
				{
					_historyLoadable++;
				}

				// A new chapter starts here: a vertical rule, its name at the top.
				int chapter = SceneFlow.ChapterOf(e.Scene);
				if (chapter > 0 && chapter != prevChapter)
				{
					AddLine(_historyList, x + 2f, 0f, x + 2f, height, ColTreeChapter, 3f);
					TextMeshProUGUI ch = AddLabel(_historyList, "Chapter", ChapterTitle(chapter),
						20f, ColTreeChosen, false);
					Wrap(ch, x + 14f, 2f, TreeColW * 2f, 28f);
					ch.enableWordWrapping = false;
				}
				if (chapter > 0)
				{
					prevChapter = chapter;
				}

				// Played scenes - all of them, whether or not the mod saw the
				// answer - sit on a tinted band with the path's bar above them;
				// the bar ends at the scene on screen, and goes on pale through
				// what is still ahead.
				bool played = !e.Upcoming;
				if (played)
				{
					GameObject band = CreateUiObject("Played", _historyList);
					RectTransform brt2 = band.GetComponent<RectTransform>();
					brt2.anchorMin = new Vector2(0f, 1f);
					brt2.anchorMax = new Vector2(0f, 1f);
					brt2.pivot = new Vector2(0f, 1f);
					brt2.anchoredPosition = new Vector2(x + 6f, -(TreeHeadY - 4f));
					brt2.sizeDelta = new Vector2(TreeColW - 8f, height - TreeHeadY - 6f);
					Image bandImg = band.AddComponent<Image>();
					bandImg.color = ColTreePlayedBand;
					bandImg.raycastTarget = false;
					if (!e.Recorded && !e.Current)
					{
						TextMeshProUGUI nr = AddLabel(band.transform, "NotRecorded",
							Loc.T(Loc.K.NotRecorded), 15f, ColTreeMuted, true);
						RectTransform nrt2 = nr.rectTransform;
						nrt2.anchorMin = new Vector2(0f, 0f);
						nrt2.anchorMax = new Vector2(1f, 0f);
						nrt2.pivot = new Vector2(0.5f, 0f);
						nrt2.anchoredPosition = new Vector2(0f, 8f);
						nrt2.sizeDelta = new Vector2(-24f, 44f);
						nr.enableWordWrapping = true;
						nr.overflowMode = TextOverflowModes.Overflow;
					}
				}
				float barEnd = e.Current ? x + TreeNodeX + TreeNodeW * 0.5f : x + TreeColW;
				AddLine(_historyList, x + 6f, TreeBarY, barEnd, TreeBarY,
					played ? ColTreeChosen : ColTreeLine, played ? 6f : 3f);

				// Column header: the scene's title.
				string headText = e.Title;
				if (e.Current)
				{
					headText += "  \u2022 " + Loc.T(Loc.K.Current);
				}
				else if (e.Upcoming && (i == 0 || !cols[i - 1].Upcoming))
				{
					headText += "  \u2022 " + Loc.T(Loc.K.Upcoming);
				}
				if (e.Possible)
				{
					headText += "  \u2022 " + Loc.T(Loc.K.Possible);
				}
				TextMeshProUGUI head = AddLabel(_historyList, "Head", headText,
					18f, e.Upcoming ? ColTreeMuted : ColTextDark, false);
				Wrap(head, x + 14f, TreeHeadY, TreeNodeW + TreeNodeX - 14f, 44f);
				head.enableWordWrapping = true;
				head.overflowMode = TextOverflowModes.Ellipsis;

				// The spine joins the node centres of this column.
				if (shown.Count > 1)
				{
					AddLine(_historyList, x + 16f, RowCentre(0), x + 16f, RowCentre(shown.Count - 1), ColTreeLine, 3f);
				}

				// Answers, top to bottom, each on a pale connector (the picked
				// one gets the path's red one on top, in DrawPath).
				for (int r = 0; r < shown.Count; r++)
				{
					History.Choice c = e.Choices[shown[r]];
					float y = TreeTop + r * TreeRowH;
					AddLine(_historyList, x + 16f, RowCentre(r), x + TreeNodeX, RowCentre(r), ColTreeLine, 3f);
					_nodes.Add(MakeNode(e, c, x + TreeNodeX, y));
				}
			}
			// The path's lines go on top of everything else.
			GameObject layer = CreateUiObject("Path", _historyList);
			RectTransform lrt = layer.GetComponent<RectTransform>();
			lrt.anchorMin = new Vector2(0f, 1f);
			lrt.anchorMax = new Vector2(0f, 1f);
			lrt.pivot = new Vector2(0f, 1f);
			lrt.anchoredPosition = Vector2.zero;
			lrt.sizeDelta = Vector2.zero;
			_pathLayer = layer.transform;
		}

		// One answer box: clicking picks it, hovering opens its pop-up.
		private static NodeRef MakeNode(TreeCol col, History.Choice c, float x, float y)
		{
			GameObject node = CreateUiObject("Choice", _historyList);
			RectTransform nrt = node.GetComponent<RectTransform>();
			nrt.anchorMin = new Vector2(0f, 1f);
			nrt.anchorMax = new Vector2(0f, 1f);
			nrt.pivot = new Vector2(0f, 1f);
			nrt.anchoredPosition = new Vector2(x, -y);
			nrt.sizeDelta = new Vector2(TreeNodeW, TreeNodeH);
			NodeRef n = new NodeRef();
			n.Col = col;
			n.Choice = c;
			n.Img = node.AddComponent<Image>();
			n.Btn = node.AddComponent<Button>();
			n.Btn.targetGraphic = n.Img;
			ColorBlock cb = n.Btn.colors;
			cb.disabledColor = Color.white;   // a greyed answer keeps StyleNode's colour
			n.Btn.colors = cb;
			string sceneId = col.Scene;
			string choiceName = c.Name;
			n.Btn.onClick.AddListener(delegate { SelectChoice(sceneId, choiceName); });

			n.Label = AddLabel(node.transform, "Text", SceneEffects.NobleParticle(c.Text), 17f, ColTextDark, false);
			RectTransform lrt = n.Label.rectTransform;
			lrt.anchorMin = Vector2.zero;
			lrt.anchorMax = Vector2.one;
			lrt.offsetMin = new Vector2(10f, 2f);
			lrt.offsetMax = new Vector2(-10f, -2f);
			n.Label.enableWordWrapping = false;

			List<SceneEffects.Effect> effects = SceneEffects.For(col.Scene, c.Name);
			TreeHover hover = node.AddComponent<TreeHover>();
			hover.HasEffects = effects.Count > 0;
			hover.HasChoiceOfRequirements = effects.Exists(f => f.Or) && col.Upcoming;
			hover.OnHover = delegate(bool on)
			{
				if (on)
				{
					// the hero at that scene as of now (it changes with the picks)
					ShowPopup(nrt, c.Text, "", effects, col.Hero);
				}
				else
				{
					HidePopup();
				}
			};
			StyleNode(n);
			return n;
		}

		// Colours and state that follow the picks: picked, greyed (the hero
		// could not take it there), ahead, or played; the dashed mark on the
		// last pick.
		private static void StyleNode(NodeRef n)
		{
			bool blocked = n.Col.Blocked.Contains(n.Choice.Name);
			n.Img.color = n.Choice.Chosen ? ColTreeChosenBg : blocked ? ColTreeBlocked
				: n.Col.Upcoming ? ColTreeUpcomingBg : ColAuto;
			n.Label.color = blocked ? new Color(0.12f, 0.10f, 0.08f, 0.35f)
				: !n.Col.Upcoming ? ColTextDark : new Color(0.12f, 0.10f, 0.08f, 0.6f);
			n.Btn.interactable = !blocked;
			bool selected = n.Col.Scene == _selScene && n.Choice.Name == _selChoice;
			if (selected && n.Mark == null)
			{
				n.Mark = MarkNode(n.Img.gameObject);
			}
			else if (!selected && n.Mark != null)
			{
				UnityEngine.Object.Destroy(n.Mark);
				n.Mark = null;
			}
		}

		// The hero whose stats the row shows: after the last pick along the
		// path when there is one (and the tree knows the hero there), else as
		// he is now - which is also what Undo brings back.
		private static SceneEffects.Hero StatsHero()
		{
			if (!string.IsNullOrEmpty(_selScene))
			{
				TreeCol col = _tree.Find(c => c.Scene == _selScene);
				if (col != null && col.Hero != null)
				{
					SceneEffects.Hero h = col.Hero.Clone();
					h.Apply(SceneEffects.For(_selScene, _selChoice));
					return h;
				}
			}
			return SceneEffects.Now();
		}

		// Order of the stats row: will, the five traits and the skills built
		// from them, then the family, then whatever else the game tracks.
		private static readonly string[] StatOrder =
		{
			"Will", "Curiosity", "Activity", "Discipline", "Cunning", "Spirituality",
			"Diplomacy", "Valor", "Manipulation", "Sabotage", "Theology", "Preaching",
			"Unity", "Money", "Reputation",
		};

		// One column per stat, with the game's icon: the always-present ones
		// (StatOrder) and any other stat that is in play (not zero now or along
		// the path). A value above today's is green, below red; hovering
		// names the stat on the hint line.
		private static void UpdateStats()
		{
			if (_statsRow == null)
			{
				return;
			}
			for (int i = _statsRow.childCount - 1; i >= 0; i--)
			{
				GameObject old = _statsRow.GetChild(i).gameObject;
				old.SetActive(false);
				UnityEngine.Object.Destroy(old);
			}
			SceneEffects.Hero now = SceneEffects.Now();
			SceneEffects.Hero shown = StatsHero();
			if (now == null || shown == null)
			{
				return;
			}
			List<_Scripts.AMVCC.Models.Static.Parameter> stats = SceneEffects.StatsToShow(StatOrder,
				p => now.Value(p) != 0 || shown.Value(p) != 0);
			foreach (_Scripts.AMVCC.Models.Static.Parameter p in stats)
			{
				int value = shown.Value(p);
				int today = now.Value(p);
				GameObject cell = CreateUiObject("Stat", _statsRow);
				VerticalLayoutGroup v = cell.AddComponent<VerticalLayoutGroup>();
				v.childAlignment = TextAnchor.MiddleCenter;
				v.childControlWidth = true;
				v.childControlHeight = true;
				v.childForceExpandWidth = true;
				v.childForceExpandHeight = false;
				GameObject ig = CreateUiObject("Icon", cell.transform);
				LayoutElement le = ig.AddComponent<LayoutElement>();
				le.preferredHeight = le.minHeight = 26f;
				Image img = ig.AddComponent<Image>();
				img.sprite = p.Icon;
				img.preserveAspect = true;
				img.raycastTarget = true;   // for the hover name
				Color colour = value > today ? new Color(0.18f, 0.42f, 0.18f)
					: value < today ? new Color(0.62f, 0.16f, 0.12f) : ColTextDark;
				TextMeshProUGUI t = AddLabel(cell.transform, "Value", value.ToString(), 18f, colour, true);
				// a line taller than its box would vanish under Ellipsis
				t.overflowMode = TextOverflowModes.Overflow;
				LayoutElement tle = t.gameObject.AddComponent<LayoutElement>();
				tle.preferredHeight = tle.minHeight = 24f;
				string name = SceneEffects.StatName(p) + ": " + value
					+ (value != today ? " (" + today + ")" : "");
				TreeHover hover = ig.AddComponent<TreeHover>();
				hover.OnHover = delegate(bool on) { SetHistoryInfo(on ? name : null); };
			}
		}

		// The taken or picked path: the red connector to each chosen answer, and
		// from its right edge to the next column's spine, then along it to that
		// column's chosen answer (none chosen there: the path waits at the
		// spine). Into the scenes ahead after an unanswered one, a pale line.
		private static void DrawPath(List<TreeCol> cols)
		{
			if (_pathLayer == null)
			{
				return;
			}
			for (int i = _pathLayer.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.Destroy(_pathLayer.GetChild(i).gameObject);
			}
			for (int i = 0; i < cols.Count; i++)
			{
				float x = 30f + i * TreeColW;
				int fromRow = ChosenRow(cols[i], _visible[i]);
				if (fromRow >= 0)
				{
					AddLine(_pathLayer, x + 16f, RowCentre(fromRow), x + TreeNodeX, RowCentre(fromRow), ColTreeChosen, 5f);
				}
				if (i + 1 >= cols.Count || _visible[i + 1].Count == 0)
				{
					continue;
				}
				float nextSpine = x + TreeColW + 16f;
				if (fromRow < 0)
				{
					if (cols[i + 1].Upcoming && _visible[i].Count > 0)
					{
						AddLine(_pathLayer, x + TreeNodeX + TreeNodeW, RowCentre(0), nextSpine,
							RowCentre(0), ColTreeLine, 3f);
					}
					continue;
				}
				float fromY = RowCentre(fromRow);
				AddLine(_pathLayer, x + TreeNodeX + TreeNodeW, fromY, nextSpine, fromY, ColTreeChosen, 5f);
				int toRow = ChosenRow(cols[i + 1], _visible[i + 1]);
				if (toRow >= 0 && Mathf.Abs(RowCentre(toRow) - fromY) > 0.5f)
				{
					AddLine(_pathLayer, nextSpine, fromY, nextSpine, RowCentre(toRow), ColTreeChosen, 5f);
				}
			}
		}

		private static int _focusCol = -1;     // the column to show on open (the current scene)

		// Every played scene with a known answer list becomes a column, then
		// every scene still ahead (AddUpcoming).
		private static List<TreeCol> BuildColumns()
		{
			List<History.Entry> all = History.FromCurrentGame();
			if (all.Count == 0)
			{
				all = History.FromNewestSave();
			}
			List<TreeCol> cols = new List<TreeCol>();
			int pendingCol = -1;
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
				if (!e.IsShowed)
				{
					pendingCol = cols.Count;
				}
				foreach (History.Choice c in col.Choices)
				{
					if (c.Chosen)
					{
						col.Recorded = true;
						col.PlayedChoice = c.Name;
					}
				}
				cols.Add(col);
			}
			if (pendingCol >= 0)
			{
				cols[pendingCol].Current = true;
			}
			_focusCol = pendingCol >= 0 ? pendingCol : cols.Count - 1;
			if (all.Count > 0)
			{
				AddUpcoming(cols, all);
			}
			return cols;
		}

		// Which answers the hero could take at each scene of the shown path.
		// A scene the mod has a save of, before any changed pick, uses the hero
		// exactly as saved there; from the first changed pick on (and for the
		// scenes ahead) the hero of the scene before is carried forward with
		// the picked answer's effects applied by the game's own rules. Stat
		// changes between scenes and objectives (unlocked by the story) are not
		// simulated, so past a changed pick this is a close estimate.
		private static void WorkOutOpenAnswers(List<TreeCol> cols)
		{
			SceneEffects.Hero carry = null;
			bool changed = false;
			foreach (TreeCol col in cols)
			{
				SceneEffects.Hero at = null;
				if (!col.Upcoming && !changed && col.HasSnapshot)
				{
					at = SceneEffects.FromSnapshot(col.SnapshotPath);
				}
				if (at == null && col.Current && !changed)
				{
					at = SceneEffects.Now();
				}
				if (at != null && col.Current && !changed)
				{
					at.Live = true;   // the scene on screen: objectives are as they are
				}
				if (at == null)
				{
					at = carry;
				}
				col.Hero = at;
				col.Blocked.Clear();
				History.Choice chosen = null;
				foreach (History.Choice c in col.Choices)
				{
					if (at != null && !at.Allows(SceneEffects.For(col.Scene, c.Name)))
					{
						col.Blocked.Add(c.Name);
					}
					if (c.Chosen)
					{
						chosen = c;
					}
				}
				carry = null;
				if (at != null && chosen != null)
				{
					carry = at.Clone();
					carry.Apply(SceneEffects.For(col.Scene, chosen.Name));
				}
				string pick;
				if (_overrides.TryGetValue(col.Scene, out pick) && pick != col.PlayedChoice)
				{
					changed = true;
				}
			}
		}

		// Indices of the answers that have text, in display order.
		private static List<int> VisibleChoices(TreeCol col)
		{
			List<int> shown = new List<int>();
			for (int k = 0; k < col.Choices.Count; k++)
			{
				string t = col.Choices[k].Text;
				if (!string.IsNullOrEmpty(t) && t.Trim().Length > 0)
				{
					shown.Add(k);
				}
			}
			return shown;
		}

		// Display row of the chosen answer, -1 when none is chosen.
		private static int ChosenRow(TreeCol col, List<int> shown)
		{
			for (int r = 0; r < shown.Count; r++)
			{
				if (col.Choices[shown[r]].Chosen)
				{
					return r;
				}
			}
			return -1;
		}

		// Vertical centre of a node row in the tree's content.
		private static float RowCentre(int row)
		{
			return TreeTop + row * TreeRowH + TreeNodeH * 0.5f;
		}

		// The scenes still ahead (scene-flow.tsv), in the order the game
		// reaches them, each with every answer it will offer: the rest of the
		// current chapter on this run's track, minus scenes whose conditions
		// fail now, then every later chapter (see SceneFlow.After). Scenes
		// whose conditions cannot be decided yet are marked as possible.
		// Scenes without answers (chapter screens) are left out.
		private static void AddUpcoming(List<TreeCol> cols, List<History.Entry> played)
		{
			HashSet<string> shown = new HashSet<string>();
			string from = "";
			foreach (History.Entry e in played)
			{
				shown.Add(e.SceneName);
				if (SceneFlow.Find(e.SceneName) != null)
				{
					from = e.SceneName;
				}
			}
			foreach (SceneFlow.Ahead a in SceneFlow.After(from, shown, true))
			{
				TreeCol col = SceneCol(a.Row.Scene);
				col.Upcoming = true;
				col.Possible = a.Condition != SceneFlow.Tri.True;
				if (VisibleChoices(col).Count > 0)
				{
					cols.Add(col);
				}
			}
		}

		// The game's own chapter name ("Chapter II. Adolescence").
		private static string ChapterTitle(int chapter)
		{
			string[] keys = { "", "Chapter1.Childhood", "Chapter2.Teen", "Chapter3.Youth",
				"Chapter4.Peace", "Chapter5.War" };
			return chapter > 0 && chapter < keys.Length ? Loc.Game(keys[chapter], keys[chapter]) : "";
		}

		private const float PopupWidth = 470f;

		// The hover pop-up: an answer's full text, what the tree knows about it,
		// and what it changes - stats with the game's own stat icons, relations
		// and fates with the character's portrait. Never a raycast target, so it
		// cannot steal the hover from the node under it.
		private static void BuildPopup(Transform box)
		{
			GameObject go = CreateUiObject("Popup", box);
			_popup = go.GetComponent<RectTransform>();
			_popup.anchorMin = new Vector2(0f, 1f);
			_popup.anchorMax = new Vector2(0f, 1f);
			_popup.pivot = new Vector2(0f, 1f);
			_popup.sizeDelta = new Vector2(PopupWidth, 100f);
			Image bg = go.AddComponent<Image>();
			bg.color = new Color(0.98f, 0.96f, 0.90f, 0.98f);
			bg.raycastTarget = false;
			Outline outline = go.AddComponent<Outline>();
			outline.effectColor = new Color(0.30f, 0.26f, 0.20f, 0.9f);
			outline.effectDistance = new Vector2(2f, -2f);
			VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
			v.padding = new RectOffset(16, 16, 12, 14);
			v.spacing = 6f;
			v.childControlWidth = true;
			v.childControlHeight = true;
			v.childForceExpandWidth = true;
			v.childForceExpandHeight = false;
			ContentSizeFitter fit = go.AddComponent<ContentSizeFitter>();
			fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			_popupText = AddLabel(go.transform, "Text", "", 19f, ColTextDark, false);
			_popupText.enableWordWrapping = true;
			_popupText.overflowMode = TextOverflowModes.Overflow;
			_popupText.alignment = TextAlignmentOptions.TopLeft;
			_popupNote = AddLabel(go.transform, "Note", "", 15f, ColTreeMuted, false);
			_popupNote.enableWordWrapping = true;
			_popupNote.alignment = TextAlignmentOptions.TopLeft;
			GameObject list = CreateUiObject("Effects", go.transform);
			VerticalLayoutGroup lv = list.AddComponent<VerticalLayoutGroup>();
			lv.spacing = 4f;
			lv.childControlWidth = true;
			lv.childControlHeight = true;
			lv.childForceExpandWidth = true;
			lv.childForceExpandHeight = false;
			_popupEffects = list.transform;
			go.SetActive(false);
		}

		private static void ShowPopup(RectTransform node, string text, string note,
			List<SceneEffects.Effect> effects, SceneEffects.Hero live)
		{
			if (_popup == null || _historyBox == null)
			{
				return;
			}
			_popupText.text = SceneEffects.NobleParticle(text);
			_popupNote.text = note;
			_popupNote.gameObject.SetActive(!string.IsNullOrEmpty(note));
			for (int i = _popupEffects.childCount - 1; i >= 0; i--)
			{
				GameObject old = _popupEffects.GetChild(i).gameObject;
				old.SetActive(false);
				UnityEngine.Object.Destroy(old);
			}
			// What it changes, then what it requires: all of the "and" ones,
			// and one of the "or" group, checked against the hero at that scene
			// along the path (`live`, null when unknown): met green, unmet red.
			bool reqHead = false, orHead = false;
			foreach (SceneEffects.Effect fx in effects)
			{
				if (!fx.IsRequirement)
				{
					AddEffectRow(fx);
				}
			}
			foreach (bool orGroup in new[] { false, true })
			{
				foreach (SceneEffects.Effect fx in effects)
				{
					if (!fx.IsRequirement || fx.Or != orGroup)
					{
						continue;
					}
					if (!reqHead)
					{
						AddPopupHeading(Loc.T(Loc.K.Requires));
						reqHead = true;
					}
					if (orGroup && !orHead)
					{
						AddPopupHeading(Loc.T(Loc.K.OneOf) + ":");
						orHead = true;
					}
					AddRequirementRow(fx, live);
				}
			}
			_popupEffects.gameObject.SetActive(effects.Count > 0);
			_popup.gameObject.SetActive(true);
			_popup.SetAsLastSibling();
			LayoutRebuilder.ForceRebuildLayoutImmediate(_popup);

			// Beside the node, flipped to its left near the right edge, and
			// kept inside the box.
			Vector3[] c = new Vector3[4];
			node.GetWorldCorners(c);
			Vector2 topLeft = _historyBox.InverseTransformPoint(c[1]);
			Vector2 topRight = _historyBox.InverseTransformPoint(c[2]);
			Rect box = _historyBox.rect;
			float w = _popup.rect.width;
			float h = _popup.rect.height;
			float x = topRight.x + 14f;
			if (x + w > box.xMax - 10f)
			{
				x = topLeft.x - 14f - w;
			}
			x = Mathf.Clamp(x, box.xMin + 10f, box.xMax - 10f - w);
			float y = Mathf.Clamp(topRight.y, box.yMin + 10f + h, box.yMax - 10f);
			// anchored at the box's top-left corner
			_popup.anchoredPosition = new Vector2(x - box.xMin, y - box.yMax);
		}

		private static void AddEffectRow(SceneEffects.Effect fx)
		{
			Sprite icon;
			string name = SceneEffects.Label(fx, out icon);
			GameObject row = PopupRow("Effect", icon, 34f);
			TextMeshProUGUI label = AddLabel(row.transform, "Name", name, 18f, ColTextDark, false);
			label.enableWordWrapping = true;
			LayoutElement nle = label.gameObject.AddComponent<LayoutElement>();
			nle.flexibleWidth = 1f;
			if (fx.Kind != 'S')
			{
				string sign = fx.Value > 0 ? "+" + fx.Value : "-" + (-fx.Value);   // the font has no U+2212
				TextMeshProUGUI val = AddLabel(row.transform, "Value", sign, 19f,
					fx.Value > 0 ? new Color(0.18f, 0.42f, 0.18f) : new Color(0.62f, 0.16f, 0.12f), false);
				val.alignment = TextAlignmentOptions.MidlineRight;
				LayoutElement vle = val.gameObject.AddComponent<LayoutElement>();
				vle.minWidth = 48f;
				vle.preferredWidth = 48f;
			}
		}

		// A pop-up line: the icon (a fixed square, empty without a sprite),
		// then whatever the caller adds.
		private static GameObject PopupRow(string name, Sprite icon, float iconSize)
		{
			GameObject row = CreateUiObject(name, _popupEffects);
			HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
			h.spacing = 10f;
			h.childAlignment = TextAnchor.MiddleLeft;
			h.childControlWidth = true;
			h.childControlHeight = true;
			h.childForceExpandWidth = false;
			h.childForceExpandHeight = false;
			GameObject ig = CreateUiObject("Icon", row.transform);
			LayoutElement ile = ig.AddComponent<LayoutElement>();
			ile.preferredWidth = ile.minWidth = iconSize;
			ile.preferredHeight = ile.minHeight = iconSize;
			if (icon != null)
			{
				Image img = ig.AddComponent<Image>();
				img.sprite = icon;
				img.preserveAspect = true;
				img.raycastTarget = false;
			}
			return row;
		}

		private static void AddPopupHeading(string text)
		{
			TextMeshProUGUI head = AddLabel(_popupEffects, "Heading", text, 16f, ColTreeMuted, false);
			head.fontStyle = FontStyles.Bold;
			LayoutElement le = head.gameObject.AddComponent<LayoutElement>();
			le.minHeight = 26f;
		}

		// A requirement, coloured against the hero at that scene (`hero`, from
		// WorkOutOpenAnswers; null leaves it uncoloured).
		private static void AddRequirementRow(SceneEffects.Effect req, SceneEffects.Hero hero)
		{
			Sprite icon;
			string text = SceneEffects.Label(req, out icon);
			bool? met = hero != null ? hero.Meets(req) : null;
			GameObject row = PopupRow("Requirement", icon, 30f);
			Color colour = met == null ? ColTextDark
				: met.Value ? new Color(0.18f, 0.42f, 0.18f) : new Color(0.62f, 0.16f, 0.12f);
			TextMeshProUGUI label = AddLabel(row.transform, "Text", text, 17f, colour, false);
			label.enableWordWrapping = true;
			LayoutElement le = label.gameObject.AddComponent<LayoutElement>();
			le.flexibleWidth = 1f;
		}

		private static void HidePopup()
		{
			if (_popup != null)
			{
				_popup.gameObject.SetActive(false);
			}
		}

		// A column for a scene by id, with every answer it offers.
		private static TreeCol SceneCol(string scene)
		{
			TreeCol col = new TreeCol();
			col.Scene = scene;
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

		// Fills the scrollbar track up to `fraction` of the story (0..1): where
		// the scene on screen sits in the whole width of the tree.
		private static void SetStoryProgress(float fraction)
		{
			if (_historyProgress != null)
			{
				_historyProgress.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
			}
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
		private static void AddLine(Transform parent, float x1, float y1, float x2, float y2, Color color, float thickness)
		{
			GameObject go = CreateUiObject("Line", parent);
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
				// Top pivot: the bar hangs down from the upper end.
				rt.anchoredPosition = new Vector2(x1 - thickness * 0.5f, -Mathf.Min(y1, y2));
				rt.sizeDelta = new Vector2(thickness, Mathf.Abs(y2 - y1));
			}
			Image img = go.AddComponent<Image>();
			img.color = color;
			img.raycastTarget = false;
		}

		// On open: the scene on screen, with one played column to its left
		// and the upcoming ones to its right.
		private static void ScrollHistoryToFocus()
		{
			if (_historyScroll == null || _historyScroll.viewport == null)
			{
				return;
			}
			float content = (_historyList as RectTransform).rect.width;
			float view = _historyScroll.viewport.rect.width;
			if (content <= view + 0.5f || _focusCol < 0)
			{
				_historyScroll.horizontalNormalizedPosition = 0f;
				return;
			}
			float left = 30f + Mathf.Max(0, _focusCol - 1) * TreeColW - 10f;
			_historyScroll.horizontalNormalizedPosition = Mathf.Clamp01(left / (content - view));
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

			_confirmLoadBtn = AddButton(box.transform, "Load", new Vector2(0f, 0f), new Vector2(30f, 26f),
				new Vector2(250f, 56f), ColLoadButton, Loc.T(Loc.K.Load), 24f, delegate { RunPending(); });
			_confirmLoadLabel = _confirmLoadBtn.GetComponentInChildren<TextMeshProUGUI>();
			AddButton(box.transform, "Cancel", new Vector2(1f, 0f), new Vector2(-30f, 26f),
				new Vector2(250f, 56f), ColCancelButton, Loc.T(Loc.K.Cancel), 24f, CancelConfirm);

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
				case Pending.LoadPath:
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
				_confirmPanel.transform.SetAsLastSibling();   // above the tree too
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
				case Pending.LoadPath:
					if (_pendingPlan != null && ModHost.Current != null)
					{
						CancelConfirm();
						ModHost.Current.StartCoroutine(ReplayTo(_pendingPlan));
					}
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
		// The same red dashed outline, around the tree's selected answer.
		private static GameObject MarkNode(GameObject node)
		{
			const float t = 3f;
			GameObject mark = CreateUiObject("SelectedMark", node.transform);
			RectTransform rt = mark.GetComponent<RectTransform>();
			Stretch(rt);
			rt.offsetMin = new Vector2(-5f, -5f);
			rt.offsetMax = new Vector2(5f, 5f);
			AddDashStrip(mark.transform, "Top", true, 1f, t, _dashH);
			AddDashStrip(mark.transform, "Bottom", true, 0f, t, _dashH);
			AddDashStrip(mark.transform, "Left", false, 0f, t, _dashV);
			AddDashStrip(mark.transform, "Right", false, 1f, t, _dashV);
			return mark;
		}

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

		// "Chapter I. Childhood" -> "Childhood" (and "Глава I. Детство" ->
		// "Детство"): the game's chapter names carry a numbered prefix ending
		// in ". ". Names without one are returned as they are.
		private static string WithoutChapterNumber(string name)
		{
			int dot = name.IndexOf(". ", StringComparison.Ordinal);
			if (dot <= 0 || dot > 24 || dot + 2 >= name.Length)
			{
				return name;
			}
			return name.Substring(dot + 2).Trim();
		}

		// The chapter, translated from its key ("Chapter1.Childhood"), with
		// the stripped name and then the number as fallbacks.
		private static string ChapterText(SaveEntry e)
		{
			string t = Loc.Game(e.ChapterKey, e.ChapterKey);
			if (!string.IsNullOrEmpty(t) && t != e.ChapterKey)
			{
				return WithoutChapterNumber(t);
			}
			if (e.ChapterName.Length > 0)
			{
				return e.ChapterName;
			}
			return e.Chapter.ToString();
		}

		// A game localisation key in the current language, key as fallback.

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
			SceneFlow.DevCheck();
			OpenHistory();
			yield return new WaitForSeconds(3f);
			DevWheel();
			// Dev: the pop-up of an answer with known effects.
			yield return new WaitForSeconds(2f);
			TreeHover withFx = null;
			foreach (TreeHover th in _historyList.GetComponentsInChildren<TreeHover>())
			{
				if (th.HasChoiceOfRequirements || (withFx == null && th.HasEffects))
				{
					if (withFx == null || !withFx.HasChoiceOfRequirements)
					{
						withFx = th;
					}
				}
			}
			if (withFx != null)
			{
				RectTransform nodeRt = (RectTransform)withFx.transform;
				float span = ((RectTransform)_historyList).rect.width - _historyScroll.viewport.rect.width;
				_historyScroll.horizontalNormalizedPosition =
					Mathf.Clamp01((nodeRt.anchoredPosition.x - 400f) / span);
				Canvas.ForceUpdateCanvases();
				withFx.OnHover(true);
				ModLog.Info("ChoiceWindow: dev pop-up shown for '" + withFx.name + "' ("
					+ _popupEffects.childCount + " effect row(s))");
				yield return new WaitForSeconds(4f);
				withFx.OnHover(false);
			}
			// Dev: show the first chapter boundary after the current scene.
			RectTransform contentRt = _historyList as RectTransform;
			int seen = 0;
			foreach (Transform t in _historyList)
			{
				if (t.name == "Chapter" && ++seen == 2)
				{
					float cx = ((RectTransform)t).anchoredPosition.x;
					float range = contentRt.rect.width - _historyScroll.viewport.rect.width;
					_historyScroll.horizontalNormalizedPosition = Mathf.Clamp01((cx - 700f) / range);
					ModLog.Info("ChoiceWindow: dev chapter boundary at x " + cx + ": '"
						+ t.GetComponent<TextMeshProUGUI>().text + "'");
					break;
				}
			}
			yield return new WaitForSeconds(3f);
			// Dev: pick the second answer of the first upcoming scene, show the
			// mark, and log the load plan; then undo.
			ScrollHistoryToFocus();
			TreeCol ahead = _tree.Find(c => c.Current && c.Choices.Count > 1)
				?? _tree.Find(c => c.Upcoming && c.Choices.Count > 1);
			if (ahead != null)
			{
				SelectChoice(ahead.Scene, ahead.Choices[1].Name);
				string why;
				LoadPlan plan = PlanLoad(out why);
				ModLog.Info("ChoiceWindow: dev selected " + ahead.Scene + " [" + ahead.Choices[1].Name
					+ "]: " + (plan == null ? "no plan (" + why + ")" : "start " + plan.StartScene
					+ ", " + plan.Picks.Count + " answer(s) to give"));
				yield return new WaitForSeconds(4f);
				UndoSelections();
			}
			// Dev (dev-loadpath.txt): Load scene end to end - the current
			// scene's second answer, then the next scene's second answer, and
			// select the scene after that; replay and report where it stopped.
			string lp = Path.Combine(Recorder.SnapshotDir, "dev-loadpath.txt");
			if (File.Exists(lp))
			{
				try { File.Delete(lp); }
				catch (Exception) { }
				TreeCol cur = _tree.Find(c => c.Current);
				int ci = cur != null ? _tree.IndexOf(cur) : -1;
				if (ci >= 0 && ci + 2 < _tree.Count)
				{
					SelectChoice(cur.Scene, cur.Choices[Math.Min(1, cur.Choices.Count - 1)].Name);
					TreeCol nx = _tree[ci + 1];
					SelectChoice(nx.Scene, nx.Choices[Math.Min(1, nx.Choices.Count - 1)].Name);
					TreeCol goal = _tree[ci + 2];
					SelectChoice(goal.Scene, goal.Choices[0].Name);
					yield return new WaitForSeconds(1f);
					LoadSelected();
					yield return new WaitForSeconds(1f);
					ModLog.Info("ChoiceWindow: dev load-path: replaying to " + goal.Scene);
					RunPending();
					yield return new WaitForSeconds(5f);
					float until = Time.realtimeSinceStartup + 240f;
					while (AutoPlay.Running && Time.realtimeSinceStartup < until)
					{
						yield return new WaitForSeconds(1f);
					}
					ModLog.Info("ChoiceWindow: dev load-path: now at " + AutoPlay.Story()
						+ (AutoPlay.Story() == goal.Scene ? " (the selected scene)" : " (NOT the selected scene)"));
				}
				yield break;
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
		// Dev: one wheel notch over an empty spot of the tree (bottom-left
		// of the viewport, below the answers), through the real event system:
		// the raycast must land in the tree and the tree must move sideways.
		private static void DevWheel()
		{
			try
			{
				EventSystem es = EventSystem.current;
				if (es == null || _historyScroll == null)
				{
					ModLog.Info("ChoiceWindow: dev wheel skipped (no event system)");
					return;
				}
				Vector3[] corners = new Vector3[4];
				_historyScroll.viewport.GetWorldCorners(corners);
				Vector2 point = new Vector2(corners[0].x + 40f, corners[0].y + 30f);
				PointerEventData data = new PointerEventData(es);
				data.position = point;
				List<RaycastResult> hits = new List<RaycastResult>();
				es.RaycastAll(data, hits);
				GameObject target = hits.Count > 0 ? hits[0].gameObject : null;
				float before = _historyScroll.horizontalNormalizedPosition;
				data.scrollDelta = new Vector2(0f, -1f);
				GameObject handled = target != null
					? ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.scrollHandler)
					: null;
				ModLog.Info("ChoiceWindow: dev wheel at " + point + ": hit '"
					+ (target != null ? target.name : "(nothing)") + "', handled by '"
					+ (handled != null ? handled.name : "(nobody)") + "', x "
					+ before.ToString("0.000") + " -> "
					+ _historyScroll.horizontalNormalizedPosition.ToString("0.000"));
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: dev wheel failed:\n" + e);
			}
		}

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

		// ------------------------------------------------------------------
		// Small UI helpers
		// ------------------------------------------------------------------

		// Pixel canvas for the icons below: white shapes on transparent, tinted
		// by the Image that shows them.
		private sealed class Canvas32
		{
			public readonly int W, H;
			public readonly Color32[] Px;

			public Canvas32(int w, int h)
			{
				W = w;
				H = h;
				Px = new Color32[w * h];
			}

			// y measured from the top, like the drawings it copies.
			public void Rect(float x, float y, float w, float h)
			{
				for (int yy = Mathf.FloorToInt(y); yy < Mathf.CeilToInt(y + h); yy++)
				{
					for (int xx = Mathf.FloorToInt(x); xx < Mathf.CeilToInt(x + w); xx++)
					{
						if (xx >= 0 && xx < W && yy >= 0 && yy < H)
						{
							Px[(H - 1 - yy) * W + xx] = new Color32(255, 255, 255, 255);
						}
					}
				}
			}

			public void Frame(float x, float y, float w, float h, float t)
			{
				Rect(x, y, w, t);
				Rect(x, y + h - t, w, t);
				Rect(x, y, t, h);
				Rect(x + w - t, y, t, h);
			}

			public Sprite ToSprite()
			{
				Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
				tex.SetPixels32(Px);
				tex.filterMode = FilterMode.Bilinear;
				tex.wrapMode = TextureWrapMode.Clamp;
				tex.Apply();
				return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
			}
		}

		// The tree view's own icon: three scenes, answers as boxes, the picked
		// ones filled, the path joining them (3 answers, the first picked; 4,
		// the third picked; 3, none picked yet).
		private static Sprite MakeTreeIcon()
		{
			Canvas32 c = new Canvas32(240, 144);
			float[] colX = { 30f, 102f, 174f };
			int[] counts = { 3, 4, 3 };
			int[] picked = { 0, 2, -1 };
			const float bw = 50f, bh = 18f, gap = 32f, line = 4f;
			float[] lastY = new float[3];
			for (int k = 0; k < 3; k++)
			{
				float top = (144f - ((counts[k] - 1) * gap + bh)) * 0.5f;
				float spine = colX[k] - 12f;
				c.Rect(spine - line * 0.5f, top + bh * 0.5f, line, (counts[k] - 1) * gap);
				for (int r = 0; r < counts[k]; r++)
				{
					float y = top + r * gap;
					c.Rect(spine, y + bh * 0.5f - line * 0.5f, 12f, line);
					if (r == picked[k])
					{
						c.Rect(colX[k], y, bw, bh);
						lastY[k] = y + bh * 0.5f;
					}
					else
					{
						c.Frame(colX[k], y, bw, bh, 4f);
					}
				}
			}
			// the path: from each picked box to the next scene's spine
			for (int k = 0; k < 2; k++)
			{
				float from = colX[k] + bw;
				float to = colX[k + 1] - 12f;
				c.Rect(from, lastY[k] - line * 0.75f, to - from, line * 1.5f);
			}
			return c.ToSprite();
		}

		// An undo arrow: three quarters of a ring, open at the top left, with
		// the arrowhead at its top end pointing left.
		private static Sprite MakeUndoIcon()
		{
			const int n = 128;
			Canvas32 c = new Canvas32(n, n);
			float cx = 66f, cy = 72f, rOut = 46f, rIn = 33f;
			for (int y = 0; y < n; y++)
			{
				for (int x = 0; x < n; x++)
				{
					float dx = x + 0.5f - cx;
					float dy = cy - (y + 0.5f);      // up is positive
					float r = Mathf.Sqrt(dx * dx + dy * dy);
					float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
					bool openQuarter = a > 90f && a < 180f;
					if (r >= rIn && r <= rOut && !openQuarter)
					{
						c.Rect(x, y, 1, 1);
					}
				}
			}
			// arrowhead at the ring's top end, pointing left
			float mid = (rIn + rOut) * 0.5f;
			float tipX = cx - 34f, baseX = cx + 2f, ay = cy - mid;
			for (int y = 0; y < n; y++)
			{
				for (int x = 0; x < n; x++)
				{
					float px = x + 0.5f, py = y + 0.5f;
					if (px < tipX || px > baseX)
					{
						continue;
					}
					float half = (px - tipX) / (baseX - tipX) * 28f;
					if (Mathf.Abs(py - ay) <= half)
					{
						c.Rect(x, y, 1, 1);
					}
				}
			}
			return c.ToSprite();
		}

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

		// A flat button anchored (and pivoted) at `anchor` of its parent, `pos`
		// from there, with a centred label unless `label` is null.
		private static Button AddButton(Transform parent, string name, Vector2 anchor, Vector2 pos,
			Vector2 size, Color color, string label, float fontSize, UnityEngine.Events.UnityAction onClick)
		{
			GameObject go = CreateUiObject(name, parent);
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.anchorMin = anchor;
			rt.anchorMax = anchor;
			rt.pivot = anchor;
			rt.anchoredPosition = pos;
			rt.sizeDelta = size;
			Image img = go.AddComponent<Image>();
			img.color = color;
			Button b = go.AddComponent<Button>();
			b.targetGraphic = img;
			b.onClick.AddListener(onClick);
			if (label != null)
			{
				Stretch(AddLabel(go.transform, "Label", label, fontSize, ColTextLight, true).rectTransform);
			}
			return b;
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
