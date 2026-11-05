using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
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
	//  - opened from its tab on the book (see EnsureBookTab),
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

	// A book tab of ours, lit on hover as the game lights its own (its "_on"
	// pictures are its "_off" ones brighter), with the game's tooltip saying
	// what it opens: the tab's Notification opens that tooltip, and its text
	// is put in once the game has filled in its own (it knows only its tabs).
	public sealed class TabHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		public Image Medallion;
		public Image Icon;
		public Color Ink;
		public string Tip;
		private bool _over;

		public void Light(bool on)
		{
			float k = on ? 1f : 0.8f;
			if (Medallion != null)
			{
				Medallion.color = new Color(k, k, k, 1f);
			}
			if (Icon != null)
			{
				Icon.color = on ? Color.white : new Color(Ink.r * 0.9f, Ink.g * 0.9f, Ink.b * 0.9f, Ink.a);
			}
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			_over = true;
			Light(true);
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			_over = false;
			Light(false);
		}

		private void OnDisable()
		{
			_over = false;
		}

		private void LateUpdate()
		{
			if (!_over)
			{
				return;
			}
			GameObject tip = RegisterGO.Find("Simple_tooltip");
			TextMeshProUGUI text = tip != null && tip.activeInHierarchy ? tip.GetComponentInChildren<TextMeshProUGUI>() : null;
			if (text != null && text.text != Tip)
			{
				text.text = Tip;
			}
		}
	}

	// The tree view fills the screen; when the window changes size while it is
	// open, the tree and the stats are laid out again (once, the next frame).
	public sealed class TreeResize : MonoBehaviour
	{
		private bool _dirty;

		private void OnRectTransformDimensionsChange()
		{
			_dirty = true;
		}

		private void Update()
		{
			if (_dirty)
			{
				_dirty = false;
				ChoiceWindow.Relayout();
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

	// Turns a cross's bars (or a delete icon) red while the pointer is on its
	// button.
	public sealed class CrossHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		public Image[] Bars;

		public void OnPointerEnter(PointerEventData eventData)
		{
			Paint(Palette.Red);
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			Paint(Palette.Ink);
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

		private enum Pending { Load, Delete, SaveNow, LoadPath }

		// Table columns, left to right. Choice is the flexible one. The hero
		// is not a column: the list only shows the current main save, so the
		// name is the same on every row.
		private enum Col { Type, Saved, Chapter, Age, Year, Season, Turn, Scene, Choice }

		// A localised scene title, or the scene id when the game has no key for
		// it (machinery/unnamed scenes otherwise showed an empty message).
		private static string SceneLabel(string scene)
		{
			string title = Loc.SceneTitle(scene);
			if (!string.IsNullOrEmpty(title))
			{
				return title;
			}
			return string.IsNullOrEmpty(scene) ? "?" : scene;
		}

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
		{ 220f, 180f, 160f, 80f, 110f, 120f, 100f, 300f, 240f };   // the choice column takes the rest

		private const float RowHeight = 44f;
		private const float HeaderHeight = 36f;
		private const float DeleteWidth = 64f;

		private sealed class SaveEntry
		{
			public string Dat;
			public string Kind;
			public string Hash;
			public List<string> Copies = new List<string>();
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
			public string Implied = "";        // ahead and unpicked: the answer the checks assume (first open)
			public SceneEffects.Hero Hero;     // the hero at this scene along the path (null: unknown)
			public SceneEffects.Hero AfterHero; // and after the answer taken/picked here
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
		private static RectTransform _statsArea;
		private static RectTransform _historyScrollRt;
		private static RectTransform _popup;
		private static TextMeshProUGUI _popupText;
		private static TextMeshProUGUI _popupNote;
		// A separate pop-up for the save table: the tree's pop-up lives in
		// the history panel, which is hidden while the table is open.
		private static RectTransform _tablePopup;
		private static TextMeshProUGUI _tablePopupText;
		private static RectTransform _tableBox;
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
		private static Button _confirmQuickBtn, _confirmCancelBtn;
		private static SaveLoadHelper _quick;   // the estimated state for the pending Load scene
		private static string _quickNote;
		private static string _pendingPath;
		private static string _pendingTitle;
		private static List<string> _pendingCopies;
		private static GameObject _markObj;
		private static int _lastItemCount;
		private static string _firstItemPath;
		private static string _firstItemTitle;

		// ------------------------------------------------------------------
		// The book tabs: the way into the save table and the tree view
		// ------------------------------------------------------------------

		// Two tabs on the book's left page, right of the year and chapter tabs,
		// made like the settings tab (MakeBookTab): one opens the save table,
		// one the tree view. Every story scene brings its own HUD, so this runs
		// after each scene load (ModHost) and does nothing when they are there.
		public static void EnsureBookTab()
		{
			try
			{
				Button settings = AutoPlay.FindNamed("HUD.Settings");
				if (settings == null)
				{
					return;
				}
				Transform hud = settings.transform.parent;
				Transform chapter = hud.Find("HUD.Chapter");
				if (chapter == null || hud.Find("HUD.SaveLoad") != null)
				{
					return;
				}
				// both on the left page, right of the chapter tab, spaced as
				// the year and chapter tabs are (their wood just apart)
				if (_saveLoadIcon == null)
				{
					_saveLoadIcon = MakeSaveLoadIcon();
					_treeTabIcon = MakeTreeTabIcon();
				}
				MakeBookTab(settings, hud, chapter, "HUD.SaveLoad", _saveLoadIcon, new Vector2(56f, 56f), 150f,
					Loc.T(Loc.K.Title), OpenSaveLoad);
				MakeBookTab(settings, hud, chapter, "HUD.TreeView", _treeTabIcon, new Vector2(62f, 48f), 280f,
					Loc.T(Loc.K.History), OpenTreeView);
				ModLog.Info("ChoiceWindow: book tabs added next to the chapter tab");
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: book tab failed:\n" + e);
			}
		}

		private static Sprite _saveLoadIcon, _treeTabIcon;

		// One tab, the size and shape of the settings tab: its button and
		// wood, a plain medallion, `icon` on it (`size` units, in the chapter
		// word's colour), `dx` to the right of the chapter tab; on hover it
		// lights up and the game's tooltip says `tip`.
		private static void MakeBookTab(Button settings, Transform hud, Transform chapter, string name,
			Sprite icon, Vector2 size, float dx, string tip, UnityEngine.Events.UnityAction onClick)
		{
			{
				GameObject tab = UnityEngine.Object.Instantiate(settings.gameObject, hud, false);
				tab.name = name;
				// HudButtonBehavior would swap the gear sprites in on hover; it
				// goes (TabHover lights the tab instead). Notification stays: it
				// is what opens the game's tooltip. So does the hover sound.
				foreach (MonoBehaviour mb in tab.GetComponents<MonoBehaviour>())
				{
					if (mb.GetType().Name == "HudButtonBehavior")
					{
						UnityEngine.Object.DestroyImmediate(mb);
					}
				}
				foreach (Behaviour bh in tab.GetComponents<Behaviour>())
				{
					if (bh.GetType().Name == "Animator")
					{
						bh.enabled = false;
					}
				}
				RectTransform crt = (RectTransform)chapter;
				RectTransform srt = (RectTransform)settings.transform;
				RectTransform rt = tab.GetComponent<RectTransform>();
				rt.anchorMin = crt.anchorMin;
				rt.anchorMax = crt.anchorMax;
				rt.pivot = srt.pivot;
				rt.sizeDelta = srt.sizeDelta;
				rt.anchoredPosition = new Vector2(crt.anchoredPosition.x + dx, srt.anchoredPosition.y);
				Image timg = tab.GetComponent<Image>();
				timg.sprite = chapter.GetComponent<Image>().sprite;

				// the wooden strips behind the tabs are painted into the book's
				// background images, not the buttons: cut the patch under the
				// settings medallion (its strip is centred there, and no rope
				// crosses it) out of each layer, and lay the copies behind this
				// tab, as big as the settings tab's
				Vector3[] sc = new Vector3[4];
				srt.GetWorldCorners(sc);
				Vector3[] tc = new Vector3[4];
				rt.GetWorldCorners(tc);
				Vector3 woodAt = new Vector3((tc[0].x + tc[2].x) * 0.5f, (sc[0].y + sc[2].y) * 0.5f, tc[0].z);
				Vector2 woodSize = srt.rect.size;
				int at = tab.transform.GetSiblingIndex();
				foreach (Image layer in hud.root.GetComponentsInChildren<Image>(true))
				{
					if (layer.sprite != null && (layer.sprite.name == "background" || layer.sprite.name == "book_new"))
					{
						Image patch = CopyPatch(layer, srt, hud, woodAt, woodSize);
						if (patch != null)
						{
							patch.transform.SetSiblingIndex(at++);
						}
					}
				}

				Color ink = Palette.TextLight;
				foreach (TextMeshProUGUI t in chapter.GetComponentsInChildren<TextMeshProUGUI>(true))
				{
					if (t.name != "Value")
					{
						ink = t.color;   // the chapter word's colour on the same medallion
						break;
					}
				}
				GameObject mark = new GameObject("Icon", typeof(RectTransform));
				mark.transform.SetParent(tab.transform, false);
				Image mimg = mark.AddComponent<Image>();
				mimg.sprite = icon;
				mimg.color = ink;
				mimg.preserveAspect = true;
				mimg.raycastTarget = false;
				RectTransform mrt = mark.GetComponent<RectTransform>();
				mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0.5f);
				mrt.sizeDelta = size;
				mrt.anchoredPosition = Vector2.zero;
				// the tooltip hangs below the tab, centred (the settings tab's
				// opens to its left - here that is over our other tabs): pivot
				// at the top centre, pushed down one tooltip height
				Component note = tab.GetComponent("Notification");
				if (note != null)
				{
					FieldInfo pos = note.GetType().GetField("TooltipPosition");
					FieldInfo off = note.GetType().GetField("Offset");
					if (pos != null && off != null)
					{
						pos.SetValue(note, Enum.Parse(pos.FieldType, "UpperCenter"));
						off.SetValue(note, new Vector2(0f, 1.1f));
					}
				}
				TabHover hover = tab.AddComponent<TabHover>();
				hover.Medallion = timg;
				hover.Icon = mimg;
				hover.Ink = ink;
				hover.Tip = tip;
				hover.Light(false);

				Button b = tab.GetComponent<Button>();
				// the settings button swaps in its gear sprite on hover; a slight
				// tint instead
				b.transition = Selectable.Transition.ColorTint;
				b.spriteState = new SpriteState();
				// a fresh event: the copy would still carry the settings tab's
				// persistent onClick
				b.onClick = new Button.ButtonClickedEvent();
				b.onClick.AddListener(onClick);
			}
		}

		// The part of `layer`'s sprite under `over`, as an Image under `parent`
		// centred on the world point `at`, `size` units big.
		private static Image CopyPatch(Image layer, RectTransform over, Transform parent, Vector3 at, Vector2 size)
		{
			RectTransform lrt = layer.rectTransform;
			Rect lr = lrt.rect;
			if (layer.preserveAspect && layer.sprite.rect.height > 0f && lr.height > 0f)
			{
				// drawn fitted inside its rect, centred: the book is narrower
				// than a 16:9 screen
				float aspect = layer.sprite.rect.width / layer.sprite.rect.height;
				if (aspect < lr.width / lr.height)
				{
					float w = lr.height * aspect;
					lr = new Rect(lr.center.x - w * 0.5f, lr.yMin, w, lr.height);
				}
				else
				{
					float h = lr.width / aspect;
					lr = new Rect(lr.xMin, lr.center.y - h * 0.5f, lr.width, h);
				}
			}
			Vector3[] c = new Vector3[4];
			over.GetWorldCorners(c);
			Vector2 lo = lrt.InverseTransformPoint(c[0]);
			Vector2 hi = lrt.InverseTransformPoint(c[2]);
			Sprite sp = layer.sprite;
			Rect tr = sp.textureRect;
			Rect cut = new Rect(
				tr.x + (lo.x - lr.xMin) / lr.width * tr.width,
				tr.y + (lo.y - lr.yMin) / lr.height * tr.height,
				(hi.x - lo.x) / lr.width * tr.width,
				(hi.y - lo.y) / lr.height * tr.height);
			if (cut.width <= 0f || cut.height <= 0f || cut.xMin < tr.xMin || cut.yMin < tr.yMin
				|| cut.xMax > tr.xMax || cut.yMax > tr.yMax)
			{
				return null;
			}
			GameObject go = new GameObject("HUD.SaveLoadWood", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			Image img = go.AddComponent<Image>();
			img.sprite = Sprite.Create(sp.texture, cut, new Vector2(0.5f, 0.5f), sp.pixelsPerUnit);
			img.raycastTarget = false;
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.sizeDelta = size;
			rt.position = at;
			return img;
		}

		// The tree view straight away, from the book tab.
		public static void OpenTreeView()
		{
			Open();
			OpenHistory();
			_treeOnly = true;
		}

		// The save table, from the book tab.
		public static void OpenSaveLoad()
		{
			Open();
		}

		private static bool _treeOnly;   // opened from its book tab: closing it closes the window

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
			// The type filter is per opening (no persisted setting).
			_filter = SaveFilter.All;
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
			_treeOnly = false;
			HideTablePopup();
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
			dimImg.color = Palette.Shade;
			Button dimBtn = dim.AddComponent<Button>();
			dimBtn.targetGraphic = dimImg;
			dimBtn.transition = Selectable.Transition.None;
			dimBtn.onClick.AddListener(Close);

			// Panel: the whole screen, as the tree view and the game's pages.
			GameObject panel = CreateUiObject("Panel", _canvas.transform);
			RectTransform prt = panel.GetComponent<RectTransform>();
			Stretch(prt);
			Image pimg = panel.AddComponent<Image>();
			pimg.color = Palette.Paper;

			// Title.
			TextMeshProUGUI title = AddLabel(panel.transform, "Title",
				Loc.T(Loc.K.Title), 34f, Palette.Ink, false);
			title.alignment = TextAlignmentOptions.MidlineLeft;
			RectTransform trt = title.rectTransform;
			trt.anchorMin = new Vector2(0f, 1f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.pivot = new Vector2(0.5f, 1f);
			trt.anchoredPosition = new Vector2(0f, -20f);
			trt.sizeDelta = new Vector2(-120f, 52f);

			// Close [x].
			GameObject closeBtn = CreateUiObject("Close", panel.transform);
			RectTransform crt = closeBtn.GetComponent<RectTransform>();
			crt.anchorMin = new Vector2(1f, 1f);
			crt.anchorMax = new Vector2(1f, 1f);
			crt.pivot = new Vector2(1f, 1f);
			crt.anchoredPosition = new Vector2(-24f, -20f);
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
			BuildTablePopup(panel.transform);

			// Save now (bottom centre).
			AddButton(panel.transform, "SaveNow", new Vector2(0.5f, 0f), new Vector2(0f, 20f),
				new Vector2(260f, 52f), Palette.Accent, Loc.T(Loc.K.SaveNow), 24f,
				delegate { Ask(Pending.SaveNow, "", "", null); });

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
			_historyPanel = null;
			_historyList = null;
			_historyScroll = null;
			_historyInfo = null;
			_historyBox = null;
			_historyProgress = null;
			_statsArea = null;
			_historyScrollRt = null;
			_popup = null;
			_popupText = null;
			_popupNote = null;
			_popupEffects = null;
			_tablePopup = null;
			_tablePopupText = null;
			_tableBox = null;
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
			overlay.color = Palette.Shade;

			// The whole screen, as the game's own pages: the canvas is always
			// 1080 units tall and as wide as the window's aspect makes it, so
			// the tree and the stats below are laid out from this box's size.
			GameObject box = CreateUiObject("Box", _historyPanel.transform);
			RectTransform brt = box.GetComponent<RectTransform>();
			Stretch(brt);
			box.AddComponent<TreeResize>();
			Image bimg = box.AddComponent<Image>();
			bimg.color = Palette.Paper;
			_historyBox = brt;

			TextMeshProUGUI title = AddLabel(box.transform, "Title",
				Loc.T(Loc.K.Playthrough), 32f, Palette.Ink, false);
			title.alignment = TextAlignmentOptions.MidlineLeft;
			RectTransform trt = title.rectTransform;
			trt.anchorMin = new Vector2(0f, 1f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.pivot = new Vector2(0.5f, 1f);
			trt.anchoredPosition = new Vector2(0f, -20f);
			trt.sizeDelta = new Vector2(-120f, 46f);

			// Scrollable list.
			GameObject scroll = CreateUiObject("Scroll", box.transform);
			RectTransform srt = scroll.GetComponent<RectTransform>();
			// placed by LayoutTreeArea: as tall as the answers, the stats below
			_historyScrollRt = srt;
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
			htimg.color = Palette.Track;
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
			pimg2.color = Palette.Fade(Palette.Accent, 0.75f);
			pimg2.raycastTarget = false;
			GameObject handle = CreateUiObject("Handle", track.transform);
			Stretch(handle.GetComponent<RectTransform>());
			Image hhimg = handle.AddComponent<Image>();
			hhimg.color = Palette.Dark;
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

			// Hint line (why a load could not start), under the header row.
			_historyInfo = AddLabel(box.transform, "Info", "", 18f,
				Palette.Dark, false);
			RectTransform irt2 = _historyInfo.rectTransform;
			irt2.anchorMin = new Vector2(0f, 1f);
			irt2.anchorMax = new Vector2(1f, 1f);
			irt2.pivot = new Vector2(0.5f, 1f);
			irt2.anchoredPosition = new Vector2(0f, -68f);
			irt2.sizeDelta = new Vector2(-52f, 26f);
			_historyInfo.overflowMode = TextOverflowModes.Ellipsis;
			_historyInfo.enableWordWrapping = false;

			// Close: an x in the top right corner, as on the save window.
			GameObject closeX = CreateUiObject("Close", box.transform);
			RectTransform cxrt = closeX.GetComponent<RectTransform>();
			cxrt.anchorMin = new Vector2(1f, 1f);
			cxrt.anchorMax = new Vector2(1f, 1f);
			cxrt.pivot = new Vector2(1f, 1f);
			cxrt.anchoredPosition = new Vector2(-24f, -20f);
			cxrt.sizeDelta = new Vector2(46f, 46f);
			Image cximg = closeX.AddComponent<Image>();
			cximg.color = Color.clear;
			Button cxbtn = closeX.AddComponent<Button>();
			cxbtn.targetGraphic = cximg;
			cxbtn.onClick.AddListener(CloseHistory);
			AddCross(closeX.transform, 26f, 4f);

			// The hero's stats along the path (StatPanel), between the tree's
			// scrollbar and the hint line; placed by LayoutTreeArea.
			_statsArea = CreateUiObject("Stats", box.transform).GetComponent<RectTransform>();
			_statsArea.anchorMin = Vector2.zero;
			_statsArea.anchorMax = Vector2.one;

			// One header row: the title on the left, Load scene in the middle,
			// reset (back to the answers actually played) left of the x.
			// just the arrow, in the x's colour (and, unlike the x, not red
			// under the pointer: it deletes nothing)
			Button undoBtn = AddButton(box.transform, "Undo", new Vector2(1f, 1f), new Vector2(-84f, -20f),
				new Vector2(58f, 46f), Color.clear, null, 0f, UndoSelections);
			undoBtn.transition = Selectable.Transition.None;
			_undoBtn = undoBtn.gameObject;
			TreeHover undoHover = _undoBtn.AddComponent<TreeHover>();
			RectTransform undoRt = _undoBtn.GetComponent<RectTransform>();
			undoHover.OnHover = delegate(bool on)
			{
				if (on)
				{
					ShowPopup(undoRt, Loc.T(Loc.K.ResetPath), "", new List<SceneEffects.Effect>(), null, true);
				}
				else
				{
					HidePopup();
				}
			};
			GameObject uicon = CreateUiObject("Icon", _undoBtn.transform);
			RectTransform uirt = uicon.GetComponent<RectTransform>();
			Stretch(uirt);
			uirt.offsetMin = new Vector2(12f, 7f);
			uirt.offsetMax = new Vector2(-12f, -7f);
			Image uiImg = uicon.AddComponent<Image>();
			uiImg.sprite = MakeUndoIcon();
			uiImg.color = Palette.Ink;
			uiImg.preserveAspect = true;
			uiImg.raycastTarget = false;

			// Load scene: goes to the selected answer's scene along the selected
			// path. Just wider than its label.
			Button loadBtn = AddButton(box.transform, "LoadHere", new Vector2(0.5f, 1f), new Vector2(0f, -20f),
				new Vector2(200f, 46f), Palette.Accent, Loc.T(Loc.K.LoadScene), 22f, LoadSelected);
			ColorBlock lcb = loadBtn.colors;
			lcb.disabledColor = Color.white;   // greyed by UpdateLoadButton's own colour
			loadBtn.colors = lcb;
			_loadHereBtn = loadBtn.gameObject;
			_loadSized = false;
			// greyed out, it says why on hover
			TreeHover loadHover = _loadHereBtn.AddComponent<TreeHover>();
			RectTransform loadRt = (RectTransform)loadBtn.transform;
			loadHover.OnHover = delegate(bool on)
			{
				string why = on ? LoadBlockedReason() : null;
				if (!string.IsNullOrEmpty(why))
				{
					ShowPopup(loadRt, why, "", new List<SceneEffects.Effect>(), null, true);
				}
				else
				{
					HidePopup();
				}
			};

			BuildPopup(box.transform);
			StatPanel.TooltipParent = box.transform;
			_historyPanel.SetActive(false);
		}

		// Back to the answers actually played: drops every pick.
		private static void UndoSelections()
		{
			_overrides.Clear();
			_selScene = null;
			_selChoice = null;
			HidePopup();
			RefreshKeepingScroll(true);
			ModLog.Info("ChoiceWindow: tree selections reset");
		}

		// Clicking an answer picks it: the path follows it. Clicking the answer
		// picked there again takes the pick back. The dashed mark - and the
		// scene Load scene goes to - is always the furthest pick along the
		// tree, since a scene can only be loaded with every answer before it
		// given. Nothing loads yet.
		private static void SelectChoice(string scene, string choice)
		{
			string had;
			if (_overrides.TryGetValue(scene, out had) && had == choice)
			{
				_overrides.Remove(scene);
			}
			else
			{
				_overrides[scene] = choice;
			}
			_selScene = null;
			_selChoice = null;
			for (int i = _tree.Count - 1; i >= 0; i--)
			{
				if (_overrides.TryGetValue(_tree[i].Scene, out had))
				{
					_selScene = _tree[i].Scene;
					_selChoice = had;
					break;
				}
			}
			TreeCol picked = _tree.Find(c => c.Scene == scene);
			// A pick in a played/current scene rebuilds everything (the hero
			// and the scenes ahead start from it); a pick ahead re-steps the
			// scenes after the scene on screen, and redraws only if they
			// changed.
			if (picked == null || !picked.Upcoming)
			{
				RefreshKeepingScroll(true);
			}
			else
			{
				_redrawAhead = ResimulateAhead();
				RefreshKeepingScroll(false);
			}
		}

		// Rebuilds the tree without moving the view.
		private static void RefreshKeepingScroll(bool rebuild = false)
		{
			RectTransform content = _historyList as RectTransform;
			Vector2 at = content != null ? content.anchoredPosition : Vector2.zero;
			RefreshHistory(rebuild);
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
			// The choice scenes from the start to the target, in order. The
			// replay checks every scene it reaches against this: a scene that
			// is skipped means the story took another way, and its picked
			// answer could not be used.
			public List<string> Order = new List<string>();
			// An intermediate scene is still ahead (a conditional scene): the
			// path is a guess beyond the played part.
			public bool Speculative;
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
			// The start: the nearest known state at or before that point - a
			// mod save of a scene, or the start of a chapter (the game keeps
			// Saves/<ID>/Chapter_N.dat, the state as the chapter began; from
			// there every answer of the chapter is given, picked or played).
			int start = -1;
			string startPath = null;
			for (int i = diverge; i >= 0 && start < 0; i--)
			{
				if (_tree[i].Upcoming)
				{
					continue;
				}
				if (_tree[i].HasSnapshot && !DevFromChapterStart)
				{
					start = i;
					startPath = _tree[i].SnapshotPath;
				}
				else
				{
					string chapterSave = ChapterStartSave(_tree, i);
					if (chapterSave != null)
					{
						start = i;
						startPath = chapterSave;
					}
				}
			}
			if (start < 0)
			{
				why = Loc.T(Loc.K.NoSaveBefore);
				return null;
			}
			if (DevFromChapterStart && SceneFlow.ChapterOf(_tree[start].Scene) != SceneFlow.ChapterOf(_tree[diverge].Scene))
			{
				// dev: the changed chapter's own start save is from another
				// timeline - an earlier chapter's start is not what was asked
				why = Loc.T(Loc.K.StartNotOnPath);
				return null;
			}
			LoadPlan plan = new LoadPlan();
			plan.StartPath = startPath;
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
				// every scene on the way needs its answer: picked, or played and
				// recorded - the replay plays exactly those, nothing guessed
				if (string.IsNullOrEmpty(pick))
				{
					why = string.Format(Loc.T(Loc.K.MissingPick), _tree[i].Title);
					return null;
				}
				if (_tree[i].Blocked.Contains(pick))
				{
					why = string.Format(Loc.T(Loc.K.PickBlocked), _tree[i].Title);
					return null;
				}
				bool knownChoice = false;
				foreach (History.Choice c in _tree[i].Choices)
				{
					if (c.Name == pick)
					{
						knownChoice = true;
						break;
					}
				}
				if (!knownChoice)
				{
					why = string.Format(Loc.T(Loc.K.PickBlocked), _tree[i].Title);
					return null;
				}
				plan.Picks[_tree[i].Scene] = pick;
			}
			for (int i = start; i <= target; i++)
			{
				plan.Order.Add(_tree[i].Scene);
			}
			for (int i = start + 1; i < target; i++)
			{
				if (_tree[i].Upcoming)
				{
					plan.Speculative = true;
				}
			}
			// The start snapshot must be this path's, or the replay would
			// begin in another timeline and a selected scene would never come.
			if (!StartLooksOnPath(startPath, start))
			{
				why = Loc.T(Loc.K.StartNotOnPath);
				return null;
			}
			string branchBad, expected;
			bool? sim = SimulatePlan(plan, startPath, out branchBad, out expected);
			if (sim == true && plan.Order.Count > 1)
			{
				ModLog.Info("ChoiceWindow: plan simulation: the game opens the path's " + plan.Order.Count
					+ " choice scenes in this order");
			}
			if (sim == false)
			{
				plan.Speculative = true;
				ModLog.Info("ChoiceWindow: plan simulation: after the picked answers the game would open '"
					+ expected + "' where the path has '" + branchBad + "'; the replay will stop there");
			}
			else if (sim == null && !SceneParams.Simulate(plan.Order, plan.Picks, startPath, out branchBad))
			{
				plan.Speculative = true;
				ModLog.Info("ChoiceWindow: plan simulation: the picked answers change a key parameter,"
					+ " so '" + branchBad + "' leaves the branch; the replay will stop there");
			}
			ModLog.Info("ChoiceWindow: plan " + plan.StartScene + " -> " + plan.Target
				+ (plan.Speculative ? " (through scenes still ahead)" : "")
				+ ", order " + string.Join(" > ", plan.Order.ToArray()));
			StringBuilder planPicks = new StringBuilder();
			foreach (string scene in plan.Order)
			{
				string pick;
				if (plan.Picks.TryGetValue(scene, out pick))
				{
					planPicks.Append(scene + "=" + pick + " ");
				}
			}
			ModLog.Info("ChoiceWindow: plan answers " + planPicks.ToString().Trim());
			return plan;
		}

		// Steps a simulated game (Sim: stats, characters, objectives, key
		// parameters, scenes shown, turn, season - every scene condition)
		// from the start save through the plan's answers, and checks that
		// each choice scene the game would open next is the plan's next one.
		// False with the two scenes when they differ, null when the start
		// save cannot be simulated (the key-parameter check is used then).
		private static bool? SimulatePlan(LoadPlan plan, string startPath, out string bad, out string expected)
		{
			bad = "";
			expected = "";
			if (plan.Order.Count < 2 || string.IsNullOrEmpty(startPath) || !File.Exists(startPath))
			{
				return null;
			}
			SaveLoadHelper h;
			SceneEffects.Hero hero;
			try
			{
				h = Recorder.ReadHelper(startPath);
				hero = SceneEffects.FromSnapshot(startPath);
			}
			catch
			{
				return null;
			}
			int chapter = SceneFlow.ChapterOf(plan.Order[0]);
			if (h == null || hero == null || h.ShowedScenesList == null || chapter <= 0)
			{
				return null;
			}
			List<ShowedScenes> list = new List<ShowedScenes>();
			foreach (ShowedScenes sc in h.ShowedScenesList)
			{
				if (sc != null)
				{
					list.Add(new ShowedScenes { SceneName = sc.SceneName, IsShowed = sc.IsShowed || sc.SceneName == plan.Order[0] });
				}
			}
			if (!list.Exists(sc => sc.SceneName == plan.Order[0]))
			{
				// a chapter start save: the chapter's first scene is not in
				// its list yet
				list.Add(new ShowedScenes { SceneName = plan.Order[0], IsShowed = true });
			}
			Sim sim = new Sim(hero, SceneParams.FromSave(h), list, chapter, h.Turn, h.Season);
			for (int i = 0; i + 1 < plan.Order.Count; i++)
			{
				string pick;
				plan.Picks.TryGetValue(plan.Order[i], out pick);
				sim.Answer(plan.Order[i], pick);
				// the scenes in between that offer no answer are stepped over
				for (int guard = 0; guard < 100; guard++)
				{
					SceneFlow.Ahead a = sim.Advance();
					if (a == null)
					{
						bad = plan.Order[i + 1];
						expected = "(the end of the story)";
						return false;
					}
					if (VisibleChoices(SceneCol(a.Row.Scene)).Count == 0)
					{
						sim.Answer(a.Row.Scene, "");
						continue;
					}
					if (a.Row.Scene != plan.Order[i + 1])
					{
						bad = plan.Order[i + 1];
						expected = a.Row.Scene;
						return false;
					}
					break;
				}
			}
			return true;
		}

		// Whether a start save is on the tree's path up to `start`: its
		// LastOpenScene is that scene and its own scene list contains every
		// choice scene before it in order. A chapter-start file is checked by
		// ChapterStartSave's timeline test instead.
		private static bool StartLooksOnPath(string startPath, int start)
		{
			try
			{
				if (startPath == null)
				{
					return false;
				}
				// A start built from the game's defaults, or another run's
				// chapter 1 save used as a stand-in, is not this path's save.
				if (startPath == NewGameStart || _standIns.Contains(Path.GetFullPath(startPath)))
				{
					return true;
				}
				if (Path.GetFileName(startPath).StartsWith("Chapter_", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
				SaveLoadHelper h = Recorder.ReadHelper(startPath);
				if (h == null || h.LastOpenScene != _tree[start].Scene)
				{
					return false;
				}
				int k = 0;
				if (h.ShowedScenesList != null)
				{
					foreach (ShowedScenes s in h.ShowedScenesList)
					{
						if (s != null && k <= start && s.SceneName == _tree[k].Scene)
						{
							k++;
						}
					}
				}
				return k >= start;   // the start scene itself may be the pending one
			}
			catch (Exception e)
			{
				ModLog.Info("ChoiceWindow: start-save check failed: " + e.Message);
				return false;
			}
		}

		private static LoadPlan _pendingPlan;

		public static bool DevFromChapterStart;
		private static bool DevStandIn;
		private static bool DevNoChapter1;   // dev-loadpath "new": as if no Chapter_1.dat existed anywhere
		private static string DevDropPick;   // dev-loadpath "offpath": this scene is taken out of the plan

		// The game's own save of the chapter's start, when column `i` is the
		// chapter's first scene, the file is there, and it is this timeline's:
		// the game keeps one per chapter and overwrites it whenever a chapter
		// begins, so after a rewind it can be from another path - its scene
		// list must be the start of the live game's. Else null.
		private static string ChapterStartSave(List<TreeCol> cols, int i)
		{
			int chapter = SceneFlow.ChapterOf(cols[i].Scene);
			if (chapter <= 0 || (i > 0 && SceneFlow.ChapterOf(cols[i - 1].Scene) == chapter))
			{
				return null;
			}
			string saves = Path.Combine(Application.persistentDataPath, "Saves");
			string id = Recorder.CurrentRunId();
			string path = Path.Combine(saves, Path.Combine(id ?? "", "Chapter_" + chapter + ".dat"));
			if (File.Exists(path) && OnThisTimeline(path) && !DevStandIn)
			{
				return path;
			}
			// The story's very start is the same for every run - a new game,
			// only the hero's name differs - so a run without its own chapter 1
			// save (one with no main-save id keeps none) starts from another
			// run's, loaded as this run (see StandInStart).
			return chapter == 1 && i == 0 ? StandInStart() : null;
		}

		// The newest Chapter_1.dat of any run that opens the story as this one
		// does, or null. Loaded with the live run's name, mode and main-save id
		// (LoadFlow, _standIn), so the game keeps saving where it saves now.
		private static string StandInStart()
		{
			try
			{
				string best = null;
				DateTime newest = DateTime.MinValue;
				foreach (string f in Directory.GetFiles(Path.Combine(Application.persistentDataPath, "Saves"),
					"Chapter_1.dat", SearchOption.AllDirectories))
				{
					if (DevNoChapter1)
					{
						break;
					}
					DateTime t = File.GetLastWriteTimeUtc(f);
					if (DevStandIn && Path.GetFullPath(f).StartsWith(Path.GetFullPath(Path.GetDirectoryName(
						Path.Combine(Path.Combine(Application.persistentDataPath, "Saves"),
						Path.Combine(Recorder.CurrentRunId() ?? "", "x")))), StringComparison.OrdinalIgnoreCase))
					{
						continue;   // dev: as if this run kept no chapter 1 save
					}
					if (t > newest && OnThisTimeline(f))
					{
						newest = t;
						best = f;
					}
				}
				if (best != null)
				{
					_standIns.Add(Path.GetFullPath(best));
					return best;
				}
				return NewGameStart;
			}
			catch (Exception e)
			{
				ModLog.Info("ChoiceWindow: no stand-in start: " + e.Message);
				return null;
			}
		}

		// No chapter 1 save anywhere: the start is built from the game's own
		// defaults instead (NewGameHelper). Stands for that in a load plan.
		private const string NewGameStart = "new-game-start";   // path-safe: it goes through Path calls

		// The state a chapter 1 save holds, built the way the game builds a
		// new game (SaveLoadManager.ResetToDefault: stats, relations, story
		// parameters and objectives from its default assets) plus what the
		// opening adds before chapter 1 is saved - compared field by field
		// with a real Chapter_1.dat: chapter 1, resuming at
		// ChildhoodChapterStart, the opening scenes shown, no history events.
		// The name, modes and save slot are the live run's.
		private static SaveLoadHelper NewGameHelper()
		{
			// (Instance can be a destroyed object, which ?? does not treat as null)
			SaveLoadManager slm = SaveLoadManager.Instance;
			if (slm == null)
			{
				slm = UnityEngine.Object.FindObjectOfType<SaveLoadManager>();
			}
			if (slm == null || GameManager.Instance == null)
			{
				return null;
			}
			// ResetToDefault also switches off the live game's objectives;
			// they are put back, as this only builds a save
			List<KeyValuePair<_Scripts.AMVCC.Views.Windows.Destiny.Objective, bool>> live = new List<KeyValuePair<_Scripts.AMVCC.Views.Windows.Destiny.Objective, bool>>();
			foreach (_Scripts.AMVCC.Views.Windows.Destiny.Objective o in Resources.FindObjectsOfTypeAll<_Scripts.AMVCC.Views.Windows.Destiny.Objective>())
			{
				live.Add(new KeyValuePair<_Scripts.AMVCC.Views.Windows.Destiny.Objective, bool>(o, o.Unlocked));
			}
			SaveLoadHelper h = new SaveLoadHelper();
			try
			{
				slm.ResetToDefault(h);
			}
			finally
			{
				foreach (KeyValuePair<_Scripts.AMVCC.Views.Windows.Destiny.Objective, bool> kv in live)
				{
					kv.Key.Unlocked = kv.Value;
				}
			}
			// its history events are copied from the live game, not reset
			foreach (GameEventStatus ev in h.AllGameEvents.GameEventsList)
			{
				ev.Unlocked = false;
				ev.UnlockDate = string.Empty;
				ev.UnlockYear = 0;
			}
			h.Chapter = 1;
			h.ChapterName = "Chapter1.Childhood";
			h.LastOpenScene = "ChildhoodChapterStart";
			h.CurrentConsequence = "ButtonDescription1";
			h.SceneState = Assets._Scripts.AMVCC.Core.SceneStateMachine.SceneStates.SceneState_PREGAME;
			foreach (string opening in new[] { "Disclaimer", "Cutscene_1", "Intro", "LoadingScreen_Child", "ChildhoodPicture" })
			{
				h.ShowedScenesList.Add(new ShowedScenes { SceneName = opening, IsShowed = true });
			}
			h.ShowedScenesList.Add(new ShowedScenes { SceneName = "ChildhoodChapterStart", IsShowed = false });
			ParametersManager pm = ParametersManager.Instance;
			h.HeroName = pm != null ? pm.HeroName : "";
			h.StoryMode = GameManager.Instance.StoryMode;
			h.IronManMode = GameManager.Instance.IronManMode;
			h.SlotInfo = new SaveSlotInfo { Id = "", Date = DateTime.Now.ToString() };
			return h;
		}

		// Chapter 1 saves handed out as stand-ins: LoadFlow loads them as the
		// live run, never as the run they belong to.
		private static readonly HashSet<string> _standIns = new HashSet<string>();

		private static readonly Dictionary<string, KeyValuePair<DateTime, bool>> _timelineChecks =
			new Dictionary<string, KeyValuePair<DateTime, bool>>();

		private static bool OnThisTimeline(string path)
		{
			try
			{
				DateTime t = File.GetLastWriteTimeUtc(path);
				KeyValuePair<DateTime, bool> known;
				if (_timelineChecks.TryGetValue(path, out known) && known.Key == t)
				{
					return known.Value;
				}
				SaveLoadHelper h = Recorder.ReadHelper(path);
				List<ShowedScenes> live = GameManager.Instance != null ? GameManager.Instance.ShowedScenesList : null;
				List<ShowedScenes> then = h != null ? h.ShowedScenesList : null;
				bool ok = live != null && then != null && then.Count <= live.Count;
				for (int k = 0; ok && k < then.Count; k++)
				{
					ok = then[k] != null && live[k] != null && then[k].SceneName == live[k].SceneName;
				}
				_timelineChecks[path] = new KeyValuePair<DateTime, bool>(t, ok);
				if (!ok)
				{
					ModLog.Info("ChoiceWindow: " + path + " is from another timeline - not used");
				}
				return ok;
			}
			catch (Exception e)
			{
				ModLog.Info("ChoiceWindow: cannot check " + path + ": " + e.Message);
				return false;
			}
		}

		// Load scene is greyed out while it would bring back exactly what is on
		// screen: nothing picked, or a pick in the scene on screen with every
		// earlier answer as played. A different earlier answer, or a pick in
		// another scene, enables it.
		private static bool PathChanged()
		{
			if (string.IsNullOrEmpty(_selScene))
			{
				return false;
			}
			TreeCol target = _tree.Find(c => c.Scene == _selScene);
			if (target == null)
			{
				return false;
			}
			if (!target.Current)
			{
				return true;
			}
			foreach (TreeCol col in _tree)
			{
				if (col == target)
				{
					break;
				}
				string pick;
				if (_overrides.TryGetValue(col.Scene, out pick) && pick != col.PlayedChoice)
				{
					return true;
				}
			}
			return false;
		}

		// An answer on the path - picked, or played and kept - that the hero
		// can no longer take there, now that an earlier pick changed his
		// stats: the first such column up to the selected scene, or null.
		private static TreeCol FirstConflict()
		{
			foreach (TreeCol col in _tree)
			{
				if (IsConflict(col))
				{
					return col;
				}
			}
			return null;
		}

		private static bool IsConflict(TreeCol col)
		{
			if (string.IsNullOrEmpty(_selScene))
			{
				return false;
			}
			string pick;
			if (!_overrides.TryGetValue(col.Scene, out pick))
			{
				pick = col.PlayedChoice;
			}
			if (string.IsNullOrEmpty(pick) || !col.Blocked.Contains(pick))
			{
				return false;
			}
			// an answer you picked counts wherever it is; a played one you
			// kept, up to the furthest pick (past it, the path is not replayed)
			int at = _tree.IndexOf(col);
			int furthest = _tree.FindIndex(c => c.Scene == _selScene);
			for (int k = _tree.Count - 1; k > furthest; k--)
			{
				if (_overrides.ContainsKey(_tree[k].Scene))
				{
					furthest = k;
					break;
				}
			}
			return at >= 0 && (_overrides.ContainsKey(col.Scene) || at <= furthest);
		}

		// Why Load scene is greyed out (shown on hover), or null when it is not.
		private static string LoadBlockedReason()
		{
			TreeCol conflict = FirstConflict();
			if (conflict != null)
			{
				return string.Format(Loc.T(Loc.K.PickConflict), conflict.Title);
			}
			if (!PathChanged())
			{
				return Loc.T(Loc.K.NothingToLoad);
			}
			string why;
			return PlanLoad(out why) == null ? why : null;   // a scene with no answer, no save to start from
		}

		private static bool _loadSized;

		private static void UpdateLoadButton()
		{
			if (_loadHereBtn == null)
			{
				return;
			}
			bool on = LoadBlockedReason() == null;
			// just wider than its label: measured once, the first time it is
			// on screen, and kept
			if (!_loadSized)
			{
				TextMeshProUGUI text = _loadHereBtn.GetComponentInChildren<TextMeshProUGUI>();
				float tw = text != null ? LineWidth(text) : 0f;
				if (tw > 10f)
				{
					((RectTransform)_loadHereBtn.transform).sizeDelta = new Vector2(tw + 44f, 46f);
					_loadSized = true;
				}
			}
			_loadHereBtn.GetComponent<Button>().interactable = on;
			_loadHereBtn.GetComponent<Image>().color = on ? Palette.Accent : Palette.Grey;
			TextMeshProUGUI label = _loadHereBtn.GetComponentInChildren<TextMeshProUGUI>();
			if (label != null)
			{
				label.color = on ? Palette.TextLight : Palette.Fade(Palette.TextLight, 0.55f);
			}
		}

		private static void LoadSelected()
		{
			string why;
			LoadPlan plan = PlanLoad(out why);
			if (plan == null)
			{
				SetHistoryInfo(why, true);
				return;
			}
			HidePopup();
			_pendingPlan = plan;
			int recorded = 0, total = 0;
			_quick = plan.StartScene != plan.Target ? Estimate(plan, out recorded, out total) : null;
			_quickNote = _quick != null ? string.Format(Loc.T(Loc.K.QuickNote), recorded, total) : null;
			Ask(Pending.LoadPath, plan.StartPath, plan.TargetTitle, null);
		}

		// The state at the plan's target, estimated instead of played: from
		// the start save, each step to the next choice scene is what the game
		// did there in a recorded run (Transitions), else what the tables say
		// (a Sim through the scenes in between). Null when it cannot be
		// estimated: the start is not a choice point of the first scene, the
		// path leaves the chapter, or a step by the tables leads elsewhere.
		private static SaveLoadHelper Estimate(LoadPlan plan, out int recorded, out int total)
		{
			recorded = 0;
			total = plan.Order.Count - 1;
			try
			{
				SaveLoadHelper h = Recorder.ReadHelper(plan.StartPath);
				if (h == null || h.LastOpenScene != plan.Order[0] || h.ShowedScenesList == null)
				{
					ModLog.Info("ChoiceWindow: quick load not offered - the start save is not a choice point of "
						+ plan.Order[0] + " (" + (h != null ? h.LastOpenScene : "unreadable") + ")");
					return null;
				}
				// within one chapter: the game writes Chapter_N.dat as a chapter
				// starts, and Restart chapter needs it - a jump past a chapter's
				// start is played instead
				int startChapter = SceneFlow.ChapterOf(plan.Order[0]);
				if (plan.Order.Exists(o => SceneFlow.ChapterOf(o) != 0 && SceneFlow.ChapterOf(o) != startChapter))
				{
					ModLog.Info("ChoiceWindow: quick load not offered - the path leaves chapter " + startChapter);
					return null;
				}
				for (int i = 0; i + 1 < plan.Order.Count; i++)
				{
					string from = plan.Order[i], to = plan.Order[i + 1], pick;
					plan.Picks.TryGetValue(from, out pick);
					Transitions.Step step = Transitions.Find(from, pick, to);
					if (step != null)
					{
						Transitions.Apply(h, step);
						recorded++;
						continue;
					}
					// by the tables
					SceneEffects.Hero hero = new SceneEffects.Hero { Stats = h.ModelParameters, Characters = h.CharacterModel };
					hero = hero.Clone();
					hero.Objectives = new HashSet<_Scripts.AMVCC.Views.Windows.ObjectiveEnum>();
					if (h.AllGameObjectives != null && h.AllGameObjectives.Objectives != null)
					{
						foreach (ObjectiveStatuses os in h.AllGameObjectives.Objectives)
						{
							if (os != null && os.Status)
							{
								hero.Objectives.Add(os.Name);
							}
						}
					}
					List<ShowedScenes> list = new List<ShowedScenes>();
					foreach (ShowedScenes sc in h.ShowedScenesList)
					{
						if (sc != null)
						{
							list.Add(new ShowedScenes { SceneName = sc.SceneName, IsShowed = sc.IsShowed || sc.SceneName == from });
						}
					}
					int chapter = SceneFlow.ChapterOf(from);
					Sim sim = new Sim(hero, SceneParams.FromSave(h), list, chapter, h.Turn, h.Season);
					sim.Answer(from, pick);
					List<string> between = new List<string>();
					string reached = null;
					for (int guard = 0; guard < 100 && reached == null; guard++)
					{
						SceneFlow.Ahead a = sim.Advance();
						if (a == null || sim.Chapter != chapter)
						{
							break;
						}
						if (VisibleChoices(SceneCol(a.Row.Scene)).Count == 0)
						{
							between.Add(a.Row.Scene);
							sim.Answer(a.Row.Scene, "");
							continue;
						}
						reached = a.Row.Scene;
					}
					if (reached != to)
					{
						ModLog.Info("ChoiceWindow: quick load not offered - by the tables " + from + " leads to "
							+ (reached ?? "another chapter") + ", not " + to);
						return null;
					}
					int years = sim.Turn - h.Turn;
					h.ModelParameters = sim.Hero.Stats;
					h.CharacterModel = sim.Hero.Characters;
					Transitions.SetObjectives(h, sim.Hero.Objectives);
					Transitions.SetKeys(h, sim.Keys);
					Transitions.MoveOn(h, from, between, to);
					h.Turn = sim.Turn;
					h.Season = sim.Season;
					h.PreviousYear = years > 0 ? h.Year + years - 1 : h.PreviousYear;
					h.Year += years;
					h.HeroAge += years;
				}
				ModLog.Info("ChoiceWindow: quick load estimate for " + plan.Target + ": " + recorded + " of " + total
					+ " step(s) recorded, the rest by the tables");
				return h;
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: quick load estimate failed:\n" + e);
				return null;
			}
		}

		// The confirm dialog's Quick load: the estimate is written next to the
		// run's stores (Runs/<run>/Estimated/, not in the save table) and
		// loaded like any save; what follows it is marked estimated.
		private static void QuickLoadNow()
		{
			SaveLoadHelper h = _quick;
			LoadPlan plan = _pendingPlan;
			CancelConfirm();
			if (h == null || plan == null)
			{
				return;
			}
			try
			{
				string run = Path.GetDirectoryName(Path.GetDirectoryName(plan.StartPath));
				string dir = Path.Combine(run, "Estimated");
				Directory.CreateDirectory(dir);
				string path = Path.Combine(dir, plan.Target + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".dat");
				using (FileStream fs = File.Create(path))
				{
					new BinaryFormatter().Serialize(fs, h);
				}
				ModLog.Info("ChoiceWindow: quick load " + plan.Target + " from the estimate " + path);
				LoadSnapshot(path);
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: quick load failed:\n" + e);
			}
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
			if (DevDropPick != null)
			{
				plan.Picks.Remove(DevDropPick);   // dev: a scene left off the path
				plan.Order.Remove(DevDropPick);
				DevDropPick = null;
			}
			string offPath = null;
			string skipped = null;
			int next = 0;
			yield return AutoPlay.Run(delegate(string scene)
			{
				string pick;
				return plan.Picks.TryGetValue(scene, out pick) ? pick : null;
			}, delegate(string scene)
			{
				int at = plan.Order.IndexOf(scene);
				if (at < 0)
				{
					// The story went to a scene not on the picked path.
					offPath = scene;
					return false;
				}
				if (at > next)
				{
					// One or more selected scenes were skipped: their answers
					// never got a chance to be used. Stop rather than carry on
					// and pretend the path was followed.
					skipped = plan.Order[next];
					offPath = scene;
					return false;
				}
				if (at < next)
				{
					return true;   // a repeat of a scene already handled
				}
				if (scene == plan.Target)
				{
					return false;
				}
				next = at + 1;
				return true;
			}, 60f, true);
			// One autosave for the scene the replay reached (the per-scene
			// mirror is suppressed while AutoPlay runs).
			Recorder.MirrorGameAutosave();
			string at = AutoPlay.Story();
			if (at != plan.Target)
			{
				string why = skipped != null
					? string.Format(Loc.T(Loc.K.ReplaySkipped),
						SceneLabel(at), SceneLabel(skipped))
					: offPath != null ? string.Format(Loc.T(Loc.K.ReplayOffPath), SceneLabel(offPath))
					: AutoPlay.LastStop == AutoPlay.Stop.Death ? string.Format(Loc.T(Loc.K.ReplayDeath), SceneLabel(at))
					: AutoPlay.LastStop == AutoPlay.Stop.Blocked ? string.Format(Loc.T(Loc.K.PickBlocked), SceneLabel(at))
					: string.Format(Loc.T(Loc.K.ReplayStopped), SceneLabel(at),
						string.IsNullOrEmpty(plan.TargetTitle) ? SceneLabel(plan.Target) : plan.TargetTitle);
				ModLog.Info("ChoiceWindow: load scene stopped at " + at + " - " + why
					+ (skipped != null ? " (skipped " + skipped + ")" : ""));
				// the tree, on the scene the replay stopped at, saying why
				OpenTreeView();
				SetHistoryInfo(why, true);
			}
		}

		private static void OpenHistory()
		{
			if (_historyPanel == null)
			{
				return;
			}
			// on screen first: texts are measured and fitted as they are built
			// (stat names, the Load scene label), so nothing changes size
			// later when a pick refreshes the values
			_historyPanel.SetActive(true);
			_historyPanel.transform.SetAsLastSibling();
			int n = RefreshHistory(true);
			UpdateLoadButton();
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
			if (_treeOnly)
			{
				_treeOnly = false;
				Close();   // back to the game, not to the save table it was not opened from
				return;
			}
			HidePopup();
			StatPanel.HideTooltip();
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
		private static bool _redrawAhead;   // the scenes ahead changed after a pick: draw them again

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
			ApplyPicks(cols);
			long tData = sw.ElapsedMilliseconds;
			WorkOutOpenAnswers(cols);
			long tHero = sw.ElapsedMilliseconds;
			_tree = cols;
			if (rebuild || _redrawAhead || _nodes.Count == 0 || _pathLayer == null)
			{
				_redrawAhead = false;
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
			StatPanel.Show(StatsHero(), SceneEffects.Now(), StatsChapter());
			UpdateLoadButton();
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
					Loc.T(Loc.K.NothingYet), 20f, Palette.Ink, true);
				RectTransform ert = empty.rectTransform;
				ert.anchorMin = new Vector2(0f, 1f);
				ert.anchorMax = new Vector2(0f, 1f);
				ert.pivot = new Vector2(0f, 1f);
				ert.sizeDelta = new Vector2(400f, 40f);
				SetContentSize(420f, 80f);
				SetStoryProgress(0f);
				LayoutTreeArea(80f);
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
			// room under the answers for a column's foot note (Current,
			// Upcoming, Possible depending on ...: up to three lines)
			float height = TreeTop + rows * TreeRowH + 20f + 52f;
			SetContentSize(width, height);
			SetStoryProgress(_focusCol >= 0 ? (30f + (_focusCol + 0.5f) * TreeColW) / width : 0f);
			LayoutTreeArea(height);

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
					AddLine(_historyList, x + 2f, 0f, x + 2f, height, Palette.Fade(Palette.Accent, 0.55f), 3f);
					TextMeshProUGUI ch = AddLabel(_historyList, "Chapter", ChapterTitle(chapter),
						20f, Palette.Accent, false);
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
					bandImg.color = Palette.Fade(Palette.Accent, 0.07f);
					bandImg.raycastTarget = false;
					if (!e.Recorded && !e.Current)
					{
						TextMeshProUGUI nr = AddLabel(band.transform, "NotRecorded",
							Loc.T(Loc.K.NotRecorded), 15f, Palette.Muted, true);
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
				// The first scene ahead says so where played scenes from before
				// the mod say theirs: at the bottom of the column.
				// The scene on screen, the next one, and scenes that may not come
				// at all (and on what that depends) say so at their foot.
				List<string> foot = new List<string>();
				if (e.Current || (e.Upcoming && (i == 0 || !cols[i - 1].Upcoming)))
				{
					string word = Loc.T(e.Current ? Loc.K.Current : Loc.K.Upcoming);
					foot.Add(word.Length > 0 ? char.ToUpper(word[0]) + word.Substring(1) : word);
				}
				if (e.Possible)
				{
					foot.Add(PossibleReason(e.Scene));
				}
				if (foot.Count > 0)
				{
					TextMeshProUGUI up = AddLabel(_historyList, "Foot", string.Join("\n", foot.ToArray()),
						15f, Palette.Muted, true);
					up.enableWordWrapping = true;
					up.overflowMode = TextOverflowModes.Overflow;
					// centred in a two-line band at the column's foot
					up.alignment = TextAlignmentOptions.Center;
					Wrap(up, x + 12f, height - 60f, TreeColW - 24f, 46f);
				}
				float barEnd = e.Current ? x + TreeNodeX + TreeNodeW * 0.5f : x + TreeColW;
				AddLine(_historyList, x + 6f, TreeBarY, barEnd, TreeBarY,
					played ? Palette.Accent : Palette.Fade(Palette.Muted, 0.55f), played ? 6f : 3f);

				// Column header: the scene's title.
				string headText = e.Title;
				TextMeshProUGUI head = AddLabel(_historyList, "Head", headText,
					18f, e.Upcoming ? Palette.Muted : Palette.Ink, false);
				Wrap(head, x + 14f, TreeHeadY, TreeNodeW + TreeNodeX - 14f, 44f);
				head.enableWordWrapping = true;
				head.overflowMode = TextOverflowModes.Ellipsis;

				// The spine joins the node centres of this column.
				if (shown.Count > 1)
				{
					AddLine(_historyList, x + 16f, RowCentre(0), x + 16f, RowCentre(shown.Count - 1), Palette.Fade(Palette.Muted, 0.55f), 3f);
				}

				// Answers, top to bottom, each on a pale connector (the picked
				// one gets the path's red one on top, in DrawPath).
				for (int r = 0; r < shown.Count; r++)
				{
					History.Choice c = e.Choices[shown[r]];
					float y = TreeTop + r * TreeRowH;
					AddLine(_historyList, x + 16f, RowCentre(r), x + TreeNodeX, RowCentre(r), Palette.Fade(Palette.Muted, 0.55f), 3f);
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

			n.Label = AddLabel(node.transform, "Text", SceneEffects.NobleParticle(c.Text), 17f, Palette.Ink, false);
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
			// an answer on the path the hero can no longer take (an earlier
			// pick changed his stats) turns red; another answer there can
			// replace it
			bool conflict = blocked && n.Choice.Chosen && IsConflict(n.Col);
			n.Img.color = conflict ? Palette.ConflictBg : n.Choice.Chosen ? Palette.Highlight : blocked ? Palette.Fade(Palette.Grey, 0.45f)
				: n.Col.Upcoming ? Palette.Fade(Palette.Card, 0.45f) : Palette.Card;
			n.Label.color = conflict ? Palette.Ink : blocked ? Palette.Fade(Palette.Ink, 0.35f)
				: !n.Col.Upcoming ? Palette.Ink : Palette.Fade(Palette.Ink, 0.6f);
			n.Btn.interactable = !blocked || conflict;   // a red pick can be clicked again to take it back
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
					h.ApplyObjectives(_selScene, SceneEffects.ButtonIndex(_selChoice));
					h.ApplyObjectives(_selScene, 0);
					return h;
				}
			}
			return SceneEffects.Now();
		}

		// The chapter of the hero the stats show: the selected scene's, else
		// the scene on screen's.
		private static int StatsChapter()
		{
			int ch = SceneFlow.ChapterOf(_selScene);
			if (ch <= 0)
			{
				TreeCol cur = _tree != null ? _tree.Find(c => c.Current) : null;
				ch = cur != null ? SceneFlow.ChapterOf(cur.Scene) : 0;
			}
			if (ch <= 0 && GameManager.Instance != null)
			{
				ch = GameManager.Instance.Chapter;
			}
			return ch;
		}

		private const float TreeAreaTop = 98f;     // below the header row and the hint line
		private const float TreeAreaBottom = 18f;  // the stats reach down to the box's foot

		// The tree takes the height its answers need (its scrollbar right
		// under them); the stat columns get the rest, down to the box's foot.
		// Called whenever the tree is drawn, which also rebuilds the stats.
		private static float _contentHeight = -1f;

		// After a change of window size: the tree's height and the stats again.
		// Opening the window reports a "change" too; with the size as it was
		// laid out, nothing is redone (that would rebuild every stat row).
		public static void Relayout()
		{
			if (_contentHeight < 0f || _historyPanel == null || !_historyPanel.activeInHierarchy
				|| _historyBox.rect.size == _laidOutFor)
			{
				return;
			}
			HidePopup();
			StatPanel.HideTooltip();
			LayoutTreeArea(_contentHeight);
			StatPanel.Show(StatsHero(), SceneEffects.Now(), StatsChapter());
			UpdateLoadButton();
			ModLog.Info("ChoiceWindow: tree view laid out again for " + _historyBox.rect.size);
		}

		private static Vector2 _laidOutFor;   // the box size the tree and stats were laid out for

		private static void LayoutTreeArea(float contentHeight)
		{
			_contentHeight = contentHeight;
			if (_historyBox != null)
			{
				_laidOutFor = _historyBox.rect.size;
			}
			if (_historyScrollRt == null || _historyBox == null)
			{
				return;
			}
			float box = _historyBox.rect.height;
			float treeH = Mathf.Clamp(contentHeight + 22f, 200f,
				Mathf.Max(200f, box - TreeAreaTop - TreeAreaBottom - 180f));
			_historyScrollRt.anchorMin = new Vector2(0f, 1f);
			_historyScrollRt.anchorMax = new Vector2(1f, 1f);
			_historyScrollRt.pivot = new Vector2(0.5f, 1f);
			_historyScrollRt.offsetMin = new Vector2(32f, -(TreeAreaTop + treeH));
			_historyScrollRt.offsetMax = new Vector2(-32f, -TreeAreaTop);
			if (_statsArea != null)
			{
				_statsArea.offsetMin = new Vector2(32f, TreeAreaBottom);
				_statsArea.offsetMax = new Vector2(-32f, -(TreeAreaTop + treeH + 12f));
				StatPanel.Build(_statsArea);
			}
		}

		// The taken or picked path: the red connector to each chosen answer, and
		// from its right edge to the next column's spine, then along it to that
		// column's chosen answer (none chosen there: the path waits at the
		// spine). A scene without an answer, played or picked, is joined to
		// nothing.
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
					AddLine(_pathLayer, x + 16f, RowCentre(fromRow), x + TreeNodeX, RowCentre(fromRow), Palette.Accent, 5f);
				}
				if (i + 1 >= cols.Count || _visible[i + 1].Count == 0)
				{
					continue;
				}
				float nextSpine = x + TreeColW + 16f;
				if (fromRow < 0)
				{
					continue;   // no answer here (yet): nothing joins it to the next scene
				}
				float fromY = RowCentre(fromRow);
				AddLine(_pathLayer, x + TreeNodeX + TreeNodeW, fromY, nextSpine, fromY, Palette.Accent, 5f);
				int toRow = ChosenRow(cols[i + 1], _visible[i + 1]);
				if (toRow >= 0 && Mathf.Abs(RowCentre(toRow) - fromY) > 0.5f)
				{
					AddLine(_pathLayer, nextSpine, fromY, nextSpine, RowCentre(toRow), Palette.Accent, 5f);
				}
			}
		}

		private static int _focusCol = -1;     // the column to show on open (the current scene)

		// Every played scene with a known answer list becomes a column, then
		// every scene still ahead (AddUpcoming).
		private static List<TreeCol> BuildColumns()
		{
			System.Diagnostics.Stopwatch st = System.Diagnostics.Stopwatch.StartNew();
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
				// Entries after the scene on screen are left over from an
				// abandoned attempt (a save's list can hold them): the game
				// counts them as shown and will not open them again, but they
				// are not on this path - no columns for them.
				if (pendingCol + 1 < cols.Count)
				{
					ModLog.Info("ChoiceWindow: " + (cols.Count - pendingCol - 1)
						+ " scene(s) after the scene on screen are from another attempt - left out of the path");
					cols.RemoveRange(pendingCol + 1, cols.Count - pendingCol - 1);
				}
			}
			_focusCol = pendingCol >= 0 ? pendingCol : cols.Count - 1;
			long tHistory = st.ElapsedMilliseconds;
			ResolveTimeline(cols, _focusCol);
			_recordedAnswers.Clear();
			foreach (TreeCol c in cols)
			{
				if (!string.IsNullOrEmpty(c.PlayedChoice))
				{
					_recordedAnswers[c.Scene] = c.PlayedChoice;
				}
			}
			long tTimeline = st.ElapsedMilliseconds;
			_played = all;
			// The picks decide the branch of the scenes ahead; work out the
			// hero after the current answer before building the upcoming
			// columns (a scene condition can turn true only after it).
			ApplyPicks(cols);
			WorkOutOpenAnswers(cols);
			long tOpen = st.ElapsedMilliseconds;
			if (all.Count > 0)
			{
				AddUpcoming(cols, all);
			}
			ModLog.Info("ChoiceWindow: columns: history " + tHistory + " ms, timeline " + (tTimeline - tHistory)
				+ " ms, open answers " + (tOpen - tTimeline) + " ms, ahead " + (st.ElapsedMilliseconds - tOpen) + " ms");
			return cols;
		}

		private static List<History.Entry> _played = new List<History.Entry>();
		private static readonly Dictionary<string, string> _recordedAnswers = new Dictionary<string, string>();

		// After a pick in a scene ahead: the played columns stay, the scenes
		// ahead are stepped through again from the scene on screen (the pick
		// changes what follows it). True if the columns ahead changed.
		private static bool ResimulateAhead()
		{
			if (_baseCols == null || _played.Count == 0)
			{
				return false;
			}
			List<string> before = new List<string>();
			foreach (TreeCol c in _baseCols)
			{
				if (c.Upcoming)
				{
					before.Add(c.Scene);
				}
			}
			List<TreeCol> cols = _baseCols.FindAll(c => !c.Upcoming);
			ApplyPicks(cols);
			WorkOutOpenAnswers(cols);
			AddUpcoming(cols, _played);
			_baseCols = cols;
			List<string> after = new List<string>();
			foreach (TreeCol c in cols)
			{
				if (c.Upcoming)
				{
					after.Add(c.Scene);
				}
			}
			return string.Join("|", before.ToArray()) != string.Join("|", after.ToArray());
		}

		// The player's picks replace what was played (or nothing, ahead).
		private static void ApplyPicks(List<TreeCol> cols)
		{
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
		}

		// Which answers the hero could take at each scene of the shown path.
		// A scene the mod has a save of, before any changed pick, uses the hero
		// exactly as saved there; from the first changed pick on (and for the
		// scenes ahead) the hero of the scene before is carried forward with
		// the picked answer's effects applied by the game's own rules. Stat
		// changes between scenes and objectives (unlocked by the story) are not
		// simulated, so past a changed pick this is a close estimate.
		// A scene reached on other paths too has several snapshots (a Load
		// scene replay with other answers before it records its own), and the
		// newest need not be this path's: its stats - and its "chosen" answer -
		// can be another timeline's. The live game is the one sure point, so
		// this walks back from the scene on screen: for each scene before, the
		// snapshot that, with the answer played there, leads to the state of
		// the scene after it (already settled). That snapshot becomes the
		// column's - the hero the tree starts from, and where Load scene
		// starts - and its answer the one played.
		private static void ResolveTimeline(List<TreeCol> cols, int last)
		{
			SceneEffects.Hero next = SceneEffects.Now();
			GameManager gm = GameManager.Instance;
			string nextAnswer = gm != null ? gm.CurrentConsequene ?? "" : "";
			int older = 0, corrected = 0;
			for (int i = last; i >= 0; i--)
			{
				TreeCol col = cols[i];
				List<string> paths = History.SnapshotsFor(col.Scene);
				if (paths.Count == 0)
				{
					next = null;   // a scene from before the mod: the chain breaks here
					nextAnswer = "";
					continue;
				}
				string best = paths[0];
				string bestAnswer = "";
				if (next != null && paths.Count > 1)
				{
					int bestD = int.MaxValue;
					foreach (string path in paths)
					{
						// every answer this save was given (it may have been
						// loaded and answered more than once), else the one the
						// next scene's save remembers
						List<string> answers = col.Current ? new List<string> { "" } : History.ChosenAllIn(path);
						answers.Reverse();   // newest first: a tie keeps the newer
						if (answers.Count == 0)
						{
							answers.Add(nextAnswer);
						}
						foreach (string answer in answers)
						{
							SceneEffects.Hero h = SceneEffects.FromSnapshot(path);
							if (h == null)
							{
								break;
							}
							if (answer.Length > 0)
							{
								h.Apply(SceneEffects.For(col.Scene, answer));
							}
							int d = SceneEffects.Distance(h, next);
							if (d < bestD)
							{
								bestD = d;
								best = path;
								bestAnswer = answer;
							}
						}
					}
				}
				else if (!col.Current)
				{
					bestAnswer = History.ChosenIn(best);
					if (bestAnswer.Length == 0)
					{
						bestAnswer = nextAnswer;
					}
				}
				if (best != paths[0])
				{
					older++;
				}
				col.SnapshotPath = best;
				if (!col.Current && bestAnswer.Length > 0 && col.Choices.Exists(c => c.Name == bestAnswer))
				{
					if (col.PlayedChoice != bestAnswer)
					{
						corrected++;
					}
					col.PlayedChoice = bestAnswer;
					col.Recorded = true;
					foreach (History.Choice c in col.Choices)
					{
						c.Chosen = c.Name == bestAnswer;
					}
				}
				next = SceneEffects.FromSnapshot(best);
				nextAnswer = History.ConsequenceOf(best);
			}
			if (older > 0 || corrected > 0)
			{
				ModLog.Info("ChoiceWindow: this path uses " + older + " snapshot(s) older than the scene's newest, "
					+ corrected + " played answer(s) taken from them");
			}
		}

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
				if (at == null && !col.Upcoming)
				{
					// a chapter played before the mod: its start save is the
					// hero as the chapter began - close, and better than nothing
					string chapterSave = ChapterStartSave(cols, cols.IndexOf(col));
					if (chapterSave != null)
					{
						at = SceneEffects.FromSnapshot(chapterSave);
					}
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
				// A scene ahead (or the one on screen) with no pick counts as
				// its first open answer for the checks further on, so they are
				// not left blank; Load scene itself asks for a pick there.
				col.Implied = "";
				if (chosen == null && at != null && (col.Upcoming || col.Current))
				{
					foreach (int k in VisibleChoices(col))
					{
						if (!col.Blocked.Contains(col.Choices[k].Name))
						{
							col.Implied = col.Choices[k].Name;
							break;
						}
					}
				}
				carry = null;
				string taken = chosen != null ? chosen.Name : col.Implied;
				SceneEffects.Hero afterBase = at;
				if (col.Current && col.HasSnapshot)
				{
					// The live state may already include the answer (the
					// consequence is on screen); the snapshot is the state the
					// scene opened with, before it.
					SceneEffects.Hero snap = SceneEffects.FromSnapshot(col.SnapshotPath);
					if (snap != null)
					{
						afterBase = snap;
					}
				}
				col.AfterHero = afterBase;
				if (afterBase != null && taken.Length > 0)
				{
					carry = afterBase.Clone();
					carry.Apply(SceneEffects.For(col.Scene, taken));
					// The objectives this answer and the scene itself earn
					// (the game unlocks them before the next scene loads).
					carry.ApplyObjectives(col.Scene, SceneEffects.ButtonIndex(taken));
					carry.ApplyObjectives(col.Scene, 0);
					col.AfterHero = carry;
				}
				else if (afterBase != null && col.Choices.Count == 0)
				{
					// A scene with no answers earns its own objectives when it
					// ends, so the next scene's checks see them.
					carry = afterBase.Clone();
					carry.ApplyObjectives(col.Scene, 0);
					col.AfterHero = carry;
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

		// The scenes still ahead, in the order the game would take them on the
		// picked path. The story is stepped forward from the scene on screen:
		// the next scene the flow would open (SceneFlow.Step, conditions
		// evaluated with the simulated game), then its answer - the pick, or
		// the first answer open to the hero - and everything that answer and
		// the scene change (stats, relations, fates, objectives, key chapter
		// parameters, scenes shown, the time slot), then the next scene, up to
		// the end of the story. So a pick ahead changes what follows it, and a
		// later chapter shows the track the path leads to. If the simulation
		// stops early (step or time budget), the rest is listed as before:
		// every scene that can still come. Scenes without answers (chapter
		// screens) are stepped through but not shown.
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
			GameManager gm = GameManager.Instance;
			_Scripts.AMVCC.Models.Static.KeyChapterParametersController kc = _Scripts.AMVCC.Models.Static.KeyChapterParametersController.Initiate;
			// Where the path leaves what is known: the first played scene whose
			// answer was changed, else the scene on screen.
			int curIndex = cols.FindIndex(c => c.Current);
			int at = curIndex;
			// (after a changed answer the columns past it - the scene on screen
			// among them - are predicted ones: a re-prediction has no current
			// column and looks through every played one)
			for (int i = 0; i < (curIndex >= 0 ? curIndex : cols.Count); i++)
			{
				string pk;
				if (_overrides.TryGetValue(cols[i].Scene, out pk) && pk != cols[i].PlayedChoice)
				{
					at = i;
					break;
				}
			}
			TreeCol cur = at >= 0 ? cols[at] : null;
			int startChapter = cur != null ? SceneFlow.ChapterOf(cur.Scene) : 0;
			if (cur == null || cur.AfterHero == null || gm == null || kc == null || startChapter <= 0)
			{
				AddUpcomingListed(cols, from, shown, cur != null ? cur.AfterHero : null, startChapter);
				return;
			}
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			// the answers played on the old path: kept where the new path meets
			// the same scene and the answer is still open
			// the answers recorded on the played path (kept from the full build:
			// a re-prediction after a pick ahead no longer has those columns, and
			// without them the path before the pick changed under it)
			Dictionary<string, string> playedAnswer = new Dictionary<string, string>(_recordedAnswers);
			for (int i = at + 1; i < cols.Count; i++)
			{
				if (!string.IsNullOrEmpty(cols[i].PlayedChoice))
				{
					playedAnswer[cols[i].Scene] = cols[i].PlayedChoice;
				}
			}
			SceneEffects.Hero hero = cur.AfterHero.Clone();
			Dictionary<string, string> keys = new Dictionary<string, string>();
			List<ShowedScenes> list = new List<ShowedScenes>();
			int turn = gm.Turn, season = gm.Season;
			SaveLoadHelper snap = null;
			if (at != curIndex && cur.HasSnapshot)
			{
				try
				{
					snap = Recorder.ReadHelper(cur.SnapshotPath);
				}
				catch
				{
					snap = null;
				}
			}
			if (snap != null)
			{
				// the game as it was at the changed scene
				keys = SceneParams.FromSave(snap);
				foreach (ShowedScenes sc in snap.ShowedScenesList)
				{
					if (sc != null)
					{
						list.Add(new ShowedScenes { SceneName = sc.SceneName, IsShowed = sc.IsShowed });
					}
				}
				turn = snap.Turn;
				season = snap.Season;
			}
			else
			{
				foreach (_Scripts.KeyParams.KeyChapterParameterModel p in kc.AllKeyParameters)
				{
					if (p != null && !string.IsNullOrEmpty(p.Name))
					{
						keys[p.Name] = p.Value ?? "";
					}
				}
				// up to the start scene: later entries are the old path's
				foreach (ShowedScenes sc in gm.ShowedScenesList)
				{
					if (sc == null)
					{
						continue;
					}
					list.Add(new ShowedScenes { SceneName = sc.SceneName, IsShowed = sc.IsShowed });
					if (sc.SceneName == cur.Scene)
					{
						break;
					}
				}
			}
			if (!list.Exists(sc => sc.SceneName == cur.Scene))
			{
				list.Add(new ShowedScenes { SceneName = cur.Scene, IsShowed = false });
			}
			list.Find(sc => sc.SceneName == cur.Scene).IsShowed = true;   // it ends with the answer
			if (at != curIndex)
			{
				// the old path after the changed answer is replaced by the
				// predicted one
				int dropped = cols.Count - at - 1;
				cols.RemoveRange(at + 1, dropped);
				shown = new HashSet<string>();
				foreach (ShowedScenes sc in list)
				{
					if (sc.IsShowed)
					{
						shown.Add(sc.SceneName);
					}
				}
				ModLog.Info("ChoiceWindow: the answer changed at " + cur.Scene + " - " + dropped
					+ " column(s) after it predicted again");
			}
			string curPick;
			if (!_overrides.TryGetValue(cur.Scene, out curPick))
			{
				curPick = string.IsNullOrEmpty(cur.PlayedChoice) ? cur.Implied : cur.PlayedChoice;
			}
			SceneParams.Apply(cur.Scene, SceneParams.ButtonOf(curPick), keys);
			int chapter = startChapter, steps = 0;
			bool finished = false;
			string last = cur.Scene;
			Sim sim = new Sim(hero, keys, list, chapter, turn, season);
			while (steps < 400 && sw.ElapsedMilliseconds < 1500)
			{
				SceneFlow.Ahead a = sim.Advance();
				if (a == null)
				{
					finished = true;
					break;
				}
				steps++;
				chapter = sim.Chapter;
				string scene = a.Row.Scene;
				last = scene;
				shown.Add(scene);
				TreeCol col = SceneCol(scene);
				col.Upcoming = true;
				// a prediction: a scene whose condition the simulated game
				// meets still depends on it
				col.Possible = a.Condition != SceneFlow.Tri.True
					|| (chapter != startChapter && !string.IsNullOrEmpty(a.Row.Condition));
				string pick;
				string old;
				if (playedAnswer.TryGetValue(scene, out old) && hero.Allows(SceneEffects.For(scene, old)))
				{
					col.PlayedChoice = old;   // the same scene on the new path: the old answer by default
				}
				if (!_overrides.TryGetValue(scene, out pick) && !string.IsNullOrEmpty(col.PlayedChoice))
				{
					pick = col.PlayedChoice;
				}
				if (pick == null)
				{
					pick = "";
					foreach (int k in VisibleChoices(col))
					{
						if (hero.Allows(SceneEffects.For(scene, col.Choices[k].Name)))
						{
							pick = col.Choices[k].Name;
							break;
						}
					}
				}
				sim.Answer(scene, pick);
				if (VisibleChoices(col).Count > 0)
				{
					cols.Add(col);
				}
			}
			ModLog.Info("ChoiceWindow: simulated " + steps + " scene(s) ahead in " + sw.ElapsedMilliseconds + " ms (flow "
				+ sim.FlowMs + " ms)"
				+ (finished ? " (to the end of the story)" : " - the rest listed"));
			if (!finished)
			{
				AddUpcomingListed(cols, last, shown, hero, chapter);
			}
		}

		// A simulated game stepped through the story: its hero, key chapter
		// parameters, scenes shown, chapter, turn and season. Advance() opens
		// the next scene the flow would (SceneFlow.Step, then the next
		// chapter), Answer() applies an answer and what the scene earns.
		private sealed class Sim
		{
			public readonly SceneEffects.Hero Hero;
			public readonly Dictionary<string, string> Keys;
			public readonly List<ShowedScenes> Shown;
			public int Chapter, Turn, Season;
			public long FlowMs;

			public Sim(SceneEffects.Hero hero, Dictionary<string, string> keys, List<ShowedScenes> shown,
				int chapter, int turn, int season)
			{
				Hero = hero;
				Keys = keys;
				Shown = shown;
				Chapter = chapter;
				Turn = turn;
				Season = season;
			}

			// The scene that opens next, already marked shown with its
			// opening changes applied; null at the end of the story.
			public SceneFlow.Ahead Advance()
			{
				while (true)
				{
					System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
					List<SceneFlow.Ahead> next = SceneFlow.Step(Chapter, Hero, Keys, Shown, Turn, Season);
					FlowMs += sw.ElapsedMilliseconds;
					if (next.Count == 0)
					{
						if (++Chapter > 5)
						{
							return null;
						}
						SceneFlow.FirstSlot(Chapter, out Turn, out Season);
						continue;
					}
					SceneFlow.Ahead a = next[0];
					if (!a.Immediate && a.Row.Timed)
					{
						// the game moves on to the scene's slot; a scene of any
						// season (Season 0) in a new turn opens it at its first season
						int nextTurn = Math.Max(Turn, a.Row.Turn);
						if (a.Row.Season > 0 && nextTurn == Turn && a.Row.Season < Season)
						{
							nextTurn++;   // its season of this turn is over
						}
						Season = a.Row.Season > 0 ? a.Row.Season : (nextTurn != Turn ? 1 : Season);
						Turn = nextTurn;
					}
					Shown.Add(new ShowedScenes { SceneName = a.Row.Scene, IsShowed = true });
					SceneParams.Apply(a.Row.Scene, 0, Keys);
					return a;
				}
			}

			public void Answer(string scene, string pick)
			{
				if (!string.IsNullOrEmpty(pick))
				{
					int button = SceneParams.ButtonOf(pick);
					Hero.Apply(SceneEffects.For(scene, pick));
					Hero.ApplyObjectives(scene, button);
					SceneParams.Apply(scene, button, Keys);
				}
				Hero.ApplyObjectives(scene, 0);   // what the scene itself earns, once it ends
			}
		}

		// Every scene that can still come after `from` (the rest of the
		// chapter whose conditions hold for `hero`, then every later chapter),
		// for when the path cannot be stepped through.
		private static void AddUpcomingListed(List<TreeCol> cols, string from, HashSet<string> shown,
			SceneEffects.Hero hero, int chapter)
		{
			foreach (SceneFlow.Ahead a in SceneFlow.After(from, shown, true, hero))
			{
				if (cols.Exists(c => c.Scene == a.Row.Scene))
				{
					continue;
				}
				TreeCol col = SceneCol(a.Row.Scene);
				col.Upcoming = true;
				col.Possible = a.Condition != SceneFlow.Tri.True;
				if (VisibleChoices(col).Count > 0)
				{
					cols.Add(col);
				}
			}
		}

		// "Possible depending on ...": what the scene's condition looks at -
		// the hero's stats, relationships, the fates of characters, or story
		// events (objectives the story sets, scenes already shown).
		private static string PossibleReason(string scene)
		{
			SceneFlow.Row row = SceneFlow.Find(scene);
			string cond = row != null ? row.Condition ?? "" : "";
			List<string> on = new List<string>();
			if (cond.Contains("GetParameterValue"))
			{
				on.Add(Loc.T(Loc.K.OnStats));
			}
			if (cond.Contains("GetCharacterRelation"))
			{
				on.Add(Loc.T(Loc.K.OnRelations));
			}
			if (cond.Contains("GetCharacterStatus"))
			{
				on.Add(Loc.T(Loc.K.OnFates));
			}
			if (cond.Contains("GetObjectiveStatus") || cond.Contains("GetSceneShowStatus")
				|| cond.Contains("GetConditionBySceneName") || on.Count == 0)
			{
				on.Add(Loc.T(Loc.K.OnEvents));
			}
			string list = on[0];
			for (int k = 1; k < on.Count; k++)
			{
				list += (k == on.Count - 1 ? " " + Loc.T(Loc.K.AndWord) + " " : ", ") + on[k];
			}
			return string.Format(Loc.T(Loc.K.PossibleOn), list);
		}

		// The game's own chapter name ("Chapter II. Adolescence").
		private static string ChapterTitle(int chapter)
		{
			string[] keys = { "", "Chapter1.Childhood", "Chapter2.Teen", "Chapter3.Youth",
				"Chapter4.Peace", "Chapter5.War" };
			return chapter > 0 && chapter < keys.Length ? Loc.Game(keys[chapter], keys[chapter]) : "";
		}

		private const float PopupWidth = 470f;

		// The save table's hover pop-up, in the same paper/outline style as
		// the tree's. The tree pop-up is a child of the history panel, which
		// is not visible while the table is, so the table gets its own.
		private static void BuildTablePopup(Transform panel)
		{
			_tableBox = panel as RectTransform;
			GameObject go = CreateUiObject("TablePopup", panel);
			_tablePopup = go.GetComponent<RectTransform>();
			_tablePopup.anchorMin = new Vector2(0f, 1f);
			_tablePopup.anchorMax = new Vector2(0f, 1f);
			_tablePopup.pivot = new Vector2(0f, 1f);
			_tablePopup.sizeDelta = new Vector2(380f, 60f);
			Image bg = go.AddComponent<Image>();
			bg.color = Palette.PaperLight;
			bg.raycastTarget = false;
			Outline outline = go.AddComponent<Outline>();
			outline.effectColor = Palette.Dark;
			outline.effectDistance = new Vector2(2f, -2f);
			VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
			v.padding = new RectOffset(14, 14, 10, 12);
			v.childControlWidth = true;
			v.childControlHeight = true;
			v.childForceExpandWidth = true;
			v.childForceExpandHeight = false;
			ContentSizeFitter fit = go.AddComponent<ContentSizeFitter>();
			fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			_tablePopupText = AddLabel(go.transform, "Text", "", 18f, Palette.Ink, false);
			_tablePopupText.enableWordWrapping = true;
			_tablePopupText.overflowMode = TextOverflowModes.Overflow;
			_tablePopupText.alignment = TextAlignmentOptions.TopLeft;
			go.SetActive(false);
		}

		private static void ShowTablePopup(RectTransform node, string text)
		{
			if (_tablePopup == null || _tableBox == null)
			{
				return;
			}
			_tablePopupText.text = text;
			_tablePopup.gameObject.SetActive(true);
			_tablePopup.sizeDelta = new Vector2(380f, _tablePopup.sizeDelta.y);
			LayoutRebuilder.ForceRebuildLayoutImmediate(_tablePopup);
			Vector3[] c = new Vector3[4];
			node.GetWorldCorners(c);
			Vector2 bottomLeft = _tableBox.InverseTransformPoint(c[0]);
			Vector2 bottomRight = _tableBox.InverseTransformPoint(c[3]);
			Rect box = _tableBox.rect;
			float w = _tablePopup.rect.width;
			float h = _tablePopup.rect.height;
			float x = Mathf.Clamp((bottomLeft.x + bottomRight.x - w) * 0.5f,
				box.xMin + 10f, box.xMax - 10f - w);
			float y = bottomLeft.y - 8f;
			y = Mathf.Clamp(y, box.yMin + 10f + h, box.yMax - 10f);
			_tablePopup.anchoredPosition = new Vector2(x - box.xMin, y - box.yMax);
			_tablePopup.SetAsLastSibling();
		}

		private static void HideTablePopup()
		{
			if (_tablePopup != null)
			{
				_tablePopup.gameObject.SetActive(false);
			}
		}

		// What each save type means, shown when hovering its cell.
		private static string KindHelp(string kind)
		{
			switch (kind)
			{
				case Recorder.KindAutosaves: return Loc.T(Loc.K.HelpAutosave);
				case Recorder.KindCheckpoints: return Loc.T(Loc.K.HelpCheckpoint);
				case Recorder.KindManual: return Loc.T(Loc.K.HelpManual);
				case "Original": return Loc.T(Loc.K.HelpOriginal);
				default: return "";
			}
		}

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
			bg.color = Palette.PaperLight;
			bg.raycastTarget = false;
			Outline outline = go.AddComponent<Outline>();
			outline.effectColor = Palette.Dark;
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

			_popupText = AddLabel(go.transform, "Text", "", 19f, Palette.Ink, false);
			_popupText.enableWordWrapping = true;
			_popupText.overflowMode = TextOverflowModes.Overflow;
			_popupText.alignment = TextAlignmentOptions.TopLeft;
			_popupNote = AddLabel(go.transform, "Note", "", 15f, Palette.Muted, false);
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
			List<SceneEffects.Effect> effects, SceneEffects.Hero live, bool below = false)
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
			// a bare line of text gets a pop-up just wider than it
			float width = PopupWidth;
			if (effects.Count == 0 && string.IsNullOrEmpty(note))
			{
				float tw = LineWidth(_popupText);
				if (tw > 10f)
				{
					width = Mathf.Min(PopupWidth, tw + 36f);
				}
			}
			_popup.sizeDelta = new Vector2(width, _popup.sizeDelta.y);
			_popup.SetAsLastSibling();
			LayoutRebuilder.ForceRebuildLayoutImmediate(_popup);

			// Left of the node, or right of it where the left has no room;
			// kept inside the box.
			Vector3[] c = new Vector3[4];
			node.GetWorldCorners(c);
			Vector2 topLeft = _historyBox.InverseTransformPoint(c[1]);
			Vector2 topRight = _historyBox.InverseTransformPoint(c[2]);
			Rect box = _historyBox.rect;
			float w = _popup.rect.width;
			float h = _popup.rect.height;
			float x = topLeft.x - 14f - w;
			if (x < box.xMin + 10f)
			{
				x = topRight.x + 14f;
			}
			float y = topRight.y;
			if (below)
			{
				// under a header button, centred on it
				x = (topLeft.x + topRight.x - w) * 0.5f;
				y = _historyBox.InverseTransformPoint(c[0]).y - 10f;
			}
			x = Mathf.Clamp(x, box.xMin + 10f, box.xMax - 10f - w);
			y = Mathf.Clamp(y, box.yMin + 10f + h, box.yMax - 10f);
			// anchored at the box's top-left corner
			_popup.anchoredPosition = new Vector2(x - box.xMin, y - box.yMax);
		}

		private static void AddEffectRow(SceneEffects.Effect fx)
		{
			Sprite icon;
			string name = SceneEffects.Label(fx, out icon);
			GameObject row = PopupRow("Effect", icon, 34f);
			TextMeshProUGUI label = AddLabel(row.transform, "Name", name, 18f, Palette.Ink, false);
			label.enableWordWrapping = true;
			LayoutElement nle = label.gameObject.AddComponent<LayoutElement>();
			nle.flexibleWidth = 1f;
			if (fx.Kind != 'S')
			{
				string sign = fx.Value > 0 ? "+" + fx.Value : "-" + (-fx.Value);   // the font has no U+2212
				TextMeshProUGUI val = AddLabel(row.transform, "Value", sign, 19f,
					fx.Value > 0 ? Palette.Good : Palette.Bad, false);
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
			TextMeshProUGUI head = AddLabel(_popupEffects, "Heading", text, 16f, Palette.Muted, false);
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
			Color colour = met == null ? Palette.Ink
				: met.Value ? Palette.Good : Palette.Bad;
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

		private static void SetHistoryInfo(string text, bool error = false)
		{
			if (_historyInfo != null)
			{
				_historyInfo.text = string.IsNullOrEmpty(text) ? "" : text;
				// A failed pre-check / replay stop is an error: the same red
				// as the delete marks. Normal hints stay dark.
				_historyInfo.color = error ? Palette.Red : Palette.Dark;
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
			timg.color = Palette.Track;
			Scrollbar sb = track.AddComponent<Scrollbar>();
			sb.direction = Scrollbar.Direction.BottomToTop;

			GameObject handle = CreateUiObject("Handle", track.transform);
			Stretch(handle.GetComponent<RectTransform>());
			Image himg = handle.AddComponent<Image>();
			himg.color = Palette.Dark;
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
				img.color = Palette.Dark;
				Button btn = cell.AddComponent<Button>();
				btn.targetGraphic = img;
				if (col == Col.Type)
				{
					// The type column is the filter cycle, not a sort column.
					btn.onClick.AddListener(CycleFilter);
				}
				else
				{
					btn.onClick.AddListener(delegate { SetSort(col); });
				}
				LayoutElement le = cell.AddComponent<LayoutElement>();
				le.preferredWidth = ColWidths[i];
				if (col == Col.Choice)
				{
					le.flexibleWidth = 1f;
				}
				TextMeshProUGUI label = AddLabel(cell.transform, "Label", ColName(col), 19f, Palette.TextLight,
					col != Col.Choice);
				label.overflowMode = TextOverflowModes.Ellipsis;
				label.enableWordWrapping = false;
				RectTransform lrt = label.rectTransform;
				Stretch(lrt);
				// room for the triangle on the right; centred titles get the
				// same on the left, so they sit over the centred cells below
				lrt.offsetMin = new Vector2(col == Col.Choice ? 8f : 22f, 0f);
				lrt.offsetMax = new Vector2(-22f, 0f);
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
				iimg.color = Palette.TextLight;
				iimg.raycastTarget = false;
				iimg.enabled = false;
				_sortIcons[i] = iimg;
			}
			// Blank cell over the delete column, so headers line up with rows.
			GameObject spacer = CreateUiObject("Head_Delete", header.transform);
			Image simg = spacer.AddComponent<Image>();
			simg.color = Palette.Dark;
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
				bool type = (Col)i == Col.Type;
				bool active = !type && (Col)i == _sortCol;
				_sortIcons[i].enabled = active;
				_sortIcons[i].sprite = _sortAsc ? _triUp : _triDown;
			}
			if (_headerLabels.Length > (int)Col.Type && _headerLabels[(int)Col.Type] != null)
			{
				_headerLabels[(int)Col.Type].text = Loc.T(Loc.K.ColType) + ": "
					+ FilterName(_filter);
			}
		}

		// Confirm dialog: covers the panel (blocks row clicks), shows the
		// pending save's title, Load / Delete / Save.
		private static void BuildConfirm(Transform panel)
		{
			_confirmPanel = CreateUiObject("Confirm", panel);
			Stretch(_confirmPanel.GetComponent<RectTransform>());
			Image overlay = _confirmPanel.AddComponent<Image>();
			overlay.color = Palette.Shade;
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
			bimg.color = Palette.PaperLight;

			_confirmText = AddLabel(box.transform, "Msg",
				"Are you sure?", 24f, Palette.Ink, false);
			_confirmText.alignment = TextAlignmentOptions.TopLeft;
			_confirmText.enableWordWrapping = true;
			RectTransform mrt = _confirmText.rectTransform;
			mrt.anchorMin = new Vector2(0f, 0f);
			mrt.anchorMax = new Vector2(1f, 1f);
			mrt.offsetMin = new Vector2(30f, 92f);
			mrt.offsetMax = new Vector2(-30f, -26f);

			_confirmLoadBtn = AddButton(box.transform, "Load", new Vector2(0f, 0f), new Vector2(30f, 26f),
				new Vector2(250f, 56f), Palette.Accent, Loc.T(Loc.K.Load), 24f, delegate { RunPending(); });
			_confirmLoadLabel = _confirmLoadBtn.GetComponentInChildren<TextMeshProUGUI>();
			_confirmCancelBtn = AddButton(box.transform, "Cancel", new Vector2(1f, 0f), new Vector2(-30f, 26f),
				new Vector2(250f, 56f), Palette.Grey, Loc.T(Loc.K.Cancel), 24f, CancelConfirm);
			// Load scene only: the estimated state instead of the replay
			_confirmQuickBtn = AddButton(box.transform, "Quick", new Vector2(0.5f, 0f), new Vector2(0f, 26f),
				new Vector2(180f, 56f), Palette.Dark, Loc.T(Loc.K.QuickLoad), 22f, QuickLoadNow);
			_confirmQuickBtn.gameObject.SetActive(false);

			_confirmPanel.SetActive(false);
		}

		// Opens the confirm dialog for one action. path/title describe the
		// save; row is the table row it came from (marked while the dialog
		// is open), or null.
		private static void Ask(Pending action, string path, string title, GameObject row,
			List<string> copies = null)
		{
			ClearMark();
			_pendingAction = action;
			_pendingPath = path;
			_pendingTitle = title;
			_pendingCopies = copies;
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
			bool quick = action == Pending.LoadPath && _quick != null && _confirmQuickBtn != null;
			if (_confirmQuickBtn != null)
			{
				_confirmQuickBtn.gameObject.SetActive(quick);
				float w = quick ? 180f : 250f;
				((RectTransform)_confirmLoadBtn.transform).sizeDelta = new Vector2(w, 56f);
				((RectTransform)_confirmCancelBtn.transform).sizeDelta = new Vector2(w, 56f);
			}
			if (quick && _confirmText != null)
			{
				_confirmText.text = body + "\n\n" + _quickNote;
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
				if (Recorder.IsBackupPath(datPath))
				{
					ModLog.Info("ChoiceWindow: not deleting the original backup " + datPath);
					_pendingCopies = null;
					CancelConfirm();
					return;
				}
				DeleteFile(datPath);
				// Rows collapse saves of the same state; delete every file
				// behind the row, not just the one shown.
				if (_pendingCopies != null)
				{
					foreach (string copy in _pendingCopies)
					{
						if (!string.Equals(copy, datPath, StringComparison.OrdinalIgnoreCase))
						{
							DeleteFile(copy);
						}
					}
				}
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: deleting " + datPath + " failed:\n" + e);
			}
			_pendingCopies = null;
			CancelConfirm();
			_lastItemCount = RefreshItems();
			Canvas.ForceUpdateCanvases();
			ModLog.Info("ChoiceWindow: " + _lastItemCount + " save(s) remain");
		}

		private static void DeleteFile(string datPath)
		{
			if (Recorder.IsBackupPath(datPath))
			{
				return;
			}
			File.Delete(datPath);
			string sidecar = Path.Combine(Path.GetDirectoryName(datPath),
				Path.GetFileNameWithoutExtension(datPath) + ".meta.txt");
			if (File.Exists(sidecar))
			{
				File.Delete(sidecar);
			}
			ModLog.Info("ChoiceWindow: removed " + Path.GetFileName(datPath));
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
			img.color = Palette.Red;
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
		// Returns the number of saves shown.
		private static int RefreshItems()
		{
			if (_itemContainer == null)
			{
				return 0;
			}
			_entries = ApplyFilter(LoadAllEntries());
			SortEntries();
			_shown = PageSize;
			UpdateHeader();
			BuildRows();
			return _entries.Count;
		}

		// Every save of the current run, with saves of the same game state
		// collapsed to one row (a "Save now" pressed twice, the game saving
		// the same scene again, the same checkpoint in two folders).
		private static List<SaveEntry> LoadAllEntries()
		{
			string run = Recorder.CurrentRunId();
			List<SaveEntry> all = new List<SaveEntry>();
			all.AddRange(ReadEntries(Recorder.RunDir(run, Recorder.KindAutosaves),
				Recorder.KindAutosaves));
			all.AddRange(ReadEntries(Recorder.RunDir(run, Recorder.KindCheckpoints),
				Recorder.KindCheckpoints));
			all.AddRange(ReadEntries(Recorder.RunDir(run, Recorder.KindManual),
				Recorder.KindManual));
			SaveEntry original = ReadOriginal(run);
			if (original != null)
			{
				all.Add(original);
			}
			return Dedupe(all);
		}

		private static List<SaveEntry> Dedupe(List<SaveEntry> all)
		{
			List<SaveEntry> keep = new List<SaveEntry>();
			Dictionary<string, SaveEntry> byState = new Dictionary<string, SaveEntry>();
			int hidden = 0;
			foreach (SaveEntry e in all)
			{
				if (string.IsNullOrEmpty(e.Hash))
				{
					keep.Add(e);
					continue;
				}
				string key = e.Kind + "\u0001" + e.Hash;
				SaveEntry first;
				if (!byState.TryGetValue(key, out first))
				{
					byState[key] = e;
					keep.Add(e);
					continue;
				}
				// Keep the newest row; the other file(s) become its copies, so
				// deleting the row removes every file of that state.
				if (e.Saved > first.Saved)
				{
					first.Copies.Add(first.Dat);
					first.Copies.AddRange(e.Copies);
					first.Dat = e.Dat;
					first.Saved = e.Saved;
					first.Title = e.Title;
				}
				else
				{
					first.Copies.Add(e.Dat);
					first.Copies.AddRange(e.Copies);
				}
				hidden++;
			}
			if (hidden > 0)
			{
				ModLog.Info("ChoiceWindow: " + hidden + " duplicate save row(s) collapsed");
			}
			return keep;
		}

		private enum SaveFilter { All, Autosaves, Checkpoints, Manual, Original }

		private static SaveFilter _filter = SaveFilter.All;
		private const int PageSize = 120;
		private static int _shown = PageSize;

		// Clicking the Type header cycles the filter: All, then one category
		// at a time, and back to All.
		private static void CycleFilter()
		{
			switch (_filter)
			{
				case SaveFilter.All: _filter = SaveFilter.Autosaves; break;
				case SaveFilter.Autosaves: _filter = SaveFilter.Checkpoints; break;
				case SaveFilter.Checkpoints: _filter = SaveFilter.Manual; break;
				case SaveFilter.Manual: _filter = SaveFilter.Original; break;
				default: _filter = SaveFilter.All; break;
			}
			if (_itemContainer == null)
			{
				return;
			}
			_entries = ApplyFilter(LoadAllEntries());
			SortEntries();
			_shown = PageSize;
			UpdateHeader();
			BuildRows();
			Canvas.ForceUpdateCanvases();
			MeasureMarquees();
		}

		private static List<SaveEntry> ApplyFilter(List<SaveEntry> all)
		{
			if (_filter == SaveFilter.All)
			{
				return all;
			}
			string kind = FilterKind(_filter);
			List<SaveEntry> list = new List<SaveEntry>();
			foreach (SaveEntry e in all)
			{
				if (e.Kind == kind)
				{
					list.Add(e);
				}
			}
			return list;
		}

		private static string FilterKind(SaveFilter f)
		{
			switch (f)
			{
				case SaveFilter.Autosaves: return Recorder.KindAutosaves;
				case SaveFilter.Checkpoints: return Recorder.KindCheckpoints;
				case SaveFilter.Manual: return Recorder.KindManual;
				case SaveFilter.Original: return "Original";
				default: return "";
			}
		}

		private static string FilterName(SaveFilter f)
		{
			switch (f)
			{
				case SaveFilter.Autosaves: return Loc.T(Loc.K.KindAutosave);
				case SaveFilter.Checkpoints: return Loc.T(Loc.K.KindCheckpoint);
				case SaveFilter.Manual: return Loc.T(Loc.K.TypeManual);
				case SaveFilter.Original: return Loc.T(Loc.K.KindOriginal);
				default: return Loc.T(Loc.K.KindAll);
			}
		}

		private static string KindLabel(string kind)
		{
			switch (kind)
			{
				case Recorder.KindAutosaves: return Loc.T(Loc.K.KindAutosave);
				case Recorder.KindCheckpoints: return Loc.T(Loc.K.KindCheckpoint);
				case Recorder.KindManual: return Loc.T(Loc.K.TypeManual);
				case "Original": return Loc.T(Loc.K.KindOriginal);
				default: return Loc.T(Loc.K.TypeAuto);
			}
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
				SaveEntry entry = ReadEntry(f, kind);
				if (entry != null)
				{
					list.Add(entry);
				}
			}
			return list;
		}

		// The one-time original save of the run, from Backups/<id>/.
		private static SaveEntry ReadOriginal(string run)
		{
			try
			{
				string dir = Path.Combine(Recorder.BackupsDir,
					Recorder.SafeName(string.IsNullOrEmpty(run) ? Recorder.NoRun : run));
				string dat = Path.Combine(dir, "Autosave.dat");
				if (!File.Exists(dat))
				{
					return null;
				}
				return ReadEntry(dat, "Original");
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: cannot read the original backup:\n" + e);
				return null;
			}
		}

		private static SaveEntry ReadEntry(string f, string kind)
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
				entry.Hash = Recorder.StateSignature(h);
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
				return entry;
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: cannot read save " + f + ":\n" + e);
				return null;
			}
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
			HideTablePopup();
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
					Loc.T(Loc.K.EmptySaves), 22f, Palette.Ink, true);
				LayoutElement le = empty.gameObject.AddComponent<LayoutElement>();
				le.preferredHeight = 60f;
				return;
			}
			int show = Math.Min(_shown, _entries.Count);
			for (int i = 0; i < show; i++)
			{
				CreateRow(_entries[i], i);
			}
			if (show < _entries.Count)
			{
				GameObject more = CreateUiObject("More", _itemContainer);
				LayoutElement mle = more.AddComponent<LayoutElement>();
				mle.preferredHeight = RowHeight;
				Image mimg = more.AddComponent<Image>();
				mimg.color = Palette.Dark;
				Button mbtn = more.AddComponent<Button>();
				mbtn.targetGraphic = mimg;
				mbtn.onClick.AddListener(delegate
				{
					_shown += PageSize;
					BuildRows();
					Canvas.ForceUpdateCanvases();
					MeasureMarquees();
				});
				TextMeshProUGUI label = AddLabel(more.transform, "Label",
					Loc.T(Loc.K.ShowMore) + " (" + (_entries.Count - show) + ")", 19f,
					Palette.TextLight, true);
				Stretch(label.rectTransform);
			}
		}

		private static void CreateRow(SaveEntry e, int index)
		{
			bool manual = e.Kind == Recorder.KindManual;
			bool original = e.Kind == "Original";
			Color bg = original
				? (index % 2 == 0 ? Palette.Highlight : Palette.HighlightAlt)
				: manual
					? (index % 2 == 0 ? Palette.Highlight : Palette.HighlightAlt)
					: (index % 2 == 0 ? Palette.Card : Palette.CardAlt);
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
				KindLabel(e.Kind),
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
				TextMeshProUGUI cell = AddLabel(cellParent, "Cell", values[i], 19f, Palette.Ink,
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
				if (col == Col.Type)
				{
					// Hovering the type cell explains what kind of save it is.
					GameObject hit = CreateUiObject("KindHint", cell.transform);
					Stretch(hit.GetComponent<RectTransform>());
					Image himg = hit.AddComponent<Image>();
					himg.color = Color.clear;
					himg.raycastTarget = true;
					TreeHover th = hit.AddComponent<TreeHover>();
					string kind = e.Kind;
					RectTransform crt2 = cell.rectTransform;
					th.OnHover = delegate(bool on)
					{
						if (on)
						{
							ShowTablePopup(crt2, KindHelp(kind));
						}
						else
						{
							HideTablePopup();
						}
					};
				}
			}

			// Delete at the far right: a red cell with a drawn x. As a child
			// of the row it sits above the row's own button, so a click
			// reaches only this one. The original backup is read-only.
			if (!original)
			{
				string dp = e.Dat;
				string dt = e.Choice;
				GameObject del = CreateUiObject("Delete", row.transform);
				LayoutElement dle = del.AddComponent<LayoutElement>();
				dle.preferredWidth = DeleteWidth;
				Image dimg = del.AddComponent<Image>();
				dimg.color = Color.clear;
				Button dbtn = del.AddComponent<Button>();
				dbtn.targetGraphic = dimg;
				dbtn.onClick.AddListener(delegate { Ask(Pending.Delete, dp, dt, row, e.Copies); });
				// a bin, red under the pointer as the x's are
				GameObject bin = CreateUiObject("Bin", del.transform);
				RectTransform binRt = bin.GetComponent<RectTransform>();
				binRt.anchorMin = binRt.anchorMax = new Vector2(0.5f, 0.5f);
				binRt.sizeDelta = new Vector2(30f, 30f);
				Image binImg = bin.AddComponent<Image>();
				if (_binIcon == null)
				{
					_binIcon = MakeBinIcon();
				}
				binImg.sprite = _binIcon;
				binImg.color = Palette.Ink;
				binImg.raycastTarget = false;
				del.AddComponent<CrossHover>().Bars = new[] { binImg };
			}

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

		// The game's pop-ups (interlude, event, trigger, objective, info) live
		// on UIManager's DialogCanvas, which survives scene changes; only its
		// own return to the main menu destroys them. A mod load from a scene
		// with one open (or queued) carried it into the loaded scene, where
		// closing it applied its changes to the wrong state: Case6's turn-7
		// interlude (State +1, Prosperity +1) landed on a replay from Milanid.
		// Every pop-up is destroyed and the queue emptied before the load; the
		// loaded scene opens its own again.
		private static void ClosePopups()
		{
			try
			{
				UIManager ui = UIManager.Initiate;
				if (ui == null)
				{
					return;
				}
				List<string> closed = new List<string>();
				if (ui.DialogCanvas != null)
				{
					foreach (Transform child in ui.DialogCanvas.transform)
					{
						string n = child.name;
						if (n.IndexOf("Popup", StringComparison.OrdinalIgnoreCase) >= 0
							&& n.EndsWith("(Clone)") && n.IndexOf("Achievement", StringComparison.Ordinal) < 0)
						{
							closed.Add(n);
							UnityEngine.Object.Destroy(child.gameObject);
						}
					}
				}
				if (ui.OpenedPopup != null && !closed.Contains(ui.OpenedPopup.name))
				{
					closed.Add(ui.OpenedPopup.name);
					UnityEngine.Object.Destroy(ui.OpenedPopup);
				}
				ui.OpenedPopup = null;
				FieldInfo queue = typeof(UIManager).GetField("_showPopupAction", BindingFlags.Instance | BindingFlags.NonPublic);
				System.Collections.IDictionary pending = queue == null ? null : queue.GetValue(ui) as System.Collections.IDictionary;
				int queued = pending == null ? 0 : pending.Count;
				if (pending != null)
				{
					pending.Clear();
				}
				ui.IsPopupOpened = false;
				if (closed.Count > 0 || queued > 0)
				{
					ModLog.Info("ChoiceWindow: closed the game's pop-ups before the load: "
						+ (closed.Count > 0 ? string.Join(", ", closed.ToArray()) : "none open")
						+ (queued > 0 ? " (+" + queued + " queued)" : ""));
				}
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: closing the game's pop-ups failed:\n" + e);
			}
		}

		private static IEnumerator LoadFlow(string datPath)
		{
			Recorder.Estimated = datPath != NewGameStart && Recorder.IsEstimated(datPath);
			// Read the save first: if it fails, the window stays open
			// and the player can pick another one.
			SaveLoadHelper helper = null;
			try
			{
				if (datPath == NewGameStart)
				{
					helper = NewGameHelper();
					if (helper == null)
					{
						throw new InvalidOperationException("the game is not up to build a new game");
					}
				}
				else
				{
					using (FileStream fs = File.OpenRead(datPath))
					{
						helper = (SaveLoadHelper)new BinaryFormatter().Deserialize(fs);
					}
				}
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: cannot read save " + datPath + ":\n" + e);
				yield break;
			}

			// saves recorded while the game had no main-save id may carry the
			// mod's placeholder folder name as their id: never the game's
			if (helper.SlotInfo != null && helper.SlotInfo.Id == Recorder.NoRun)
			{
				helper.SlotInfo.Id = "";
			}
			if (Recorder.EnsureSceneListed(helper))
			{
				ModLog.Info("ChoiceWindow: " + helper.LastOpenScene + " was missing from the save's scene list (recorded"
					+ " before its scene finished loading) - added, as the game would have");
			}
			ModLog.Info("ChoiceWindow: loading " + Path.GetFileName(datPath)
				+ " (scene=" + helper.LastOpenScene
				+ ", main save=" + (helper.SlotInfo != null ? helper.SlotInfo.Id : "")
				+ ", ironman=" + helper.IronManMode + ")");
			// A stand-in start (another run's chapter 1 save) becomes this run:
			// its hero's name and mode, and the live main-save id - else the
			// game would save on into the other run's folder.
			bool standIn = datPath == NewGameStart || _standIns.Contains(Path.GetFullPath(datPath));
			string liveRun = Recorder.CurrentRunId();
			string liveReal = Recorder.LiveRealId();
			if (string.IsNullOrEmpty(liveReal))
			{
				liveReal = Recorder.RealRunId(liveRun);   // the live slot can be blank after a mod load
			}
			if (string.IsNullOrEmpty(liveReal) && !string.IsNullOrEmpty(liveRun)
				&& Directory.Exists(Path.Combine(Path.Combine(Application.persistentDataPath, "Saves"), liveRun)))
			{
				liveReal = liveRun.Replace('\\', '/');   // pinned from a save's own folder: the id, slashes and all
			}
			if (standIn)
			{
				ParametersManager pm = ParametersManager.Instance;
				if (pm != null && !string.IsNullOrEmpty(pm.HeroName))
				{
					helper.HeroName = pm.HeroName;
				}
				if (GameManager.Instance != null)
				{
					helper.IronManMode = GameManager.Instance.IronManMode;
				}
				if (helper.SlotInfo == null)
				{
					helper.SlotInfo = new SaveSlotInfo();
				}
				helper.SlotInfo.Id = liveReal ?? "";
				ModLog.Info("ChoiceWindow: starting from the story's start (" + datPath + ") as this run, hero '"
					+ helper.HeroName + "', main save '" + helper.SlotInfo.Id + "'");
			}
			// The blob has no ID (for stable dedup), so the run comes from
			// the folder the file sits in; the game needs the REAL id (it
			// contains slashes) to find its own save folder.
			string runId = standIn ? liveRun : Recorder.RunIdForSavePath(datPath);
			if (string.IsNullOrEmpty(runId) && helper.SlotInfo != null)
			{
				runId = helper.SlotInfo.Id;
			}
			// A raw id keeps its slashes; a folder name from RunsDir is
			// sanitised. The save's own id (stored in the blob) wins over the
			// folder it is filed in: a cross-run pre-rewind checkpoint is
			// filed with the target run so that run's table can show it.
			string blobId = helper.SlotInfo != null ? Recorder.RealOrEmpty(helper.SlotInfo.Id) : "";
			string realId = "";
			if (standIn)
			{
				realId = liveReal;
			}
			else if (!string.IsNullOrEmpty(blobId) && blobId.IndexOf('/') >= 0)
			{
				realId = Directory.Exists(Path.Combine(Recorder.SavesDir, blobId))
					? blobId : Recorder.RealRunId(blobId);
			}
			if (string.IsNullOrEmpty(realId) && !standIn)
			{
				realId = Recorder.RealRunId(runId);
			}
			if (string.IsNullOrEmpty(realId) && !standIn && !string.IsNullOrEmpty(blobId)
				&& blobId.IndexOf('/') >= 0)
			{
				realId = blobId;
			}
			if (string.IsNullOrEmpty(realId) && !standIn && Recorder.IsBackupPath(datPath))
			{
				// The original backup belongs to the run being played.
				realId = Recorder.CurrentRunId();
			}
			if (!string.IsNullOrEmpty(realId))
			{
				if (helper.SlotInfo == null)
				{
					helper.SlotInfo = new SaveSlotInfo();
				}
				helper.SlotInfo.Id = realId;
			}
			// Keep the state the load is about to replace, and make sure the
			// original save (and the target run's) has its one-time backup.
			Recorder.BeforeModLoad(realId, helper.LastOpenScene);
			if (File.Exists(Path.Combine(Recorder.SnapshotDir, "dev-ironman.txt")))
			{
				helper.IronManMode = true;
				ModLog.Info("DEV IRONMAN: loading the save as an Iron Man game");
			}
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
			ClosePopups();

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
			// The game unloads `SceneManager.GetActiveScene()` after loading the
			// next scene. The mod loads from inside a story scene, where the
			// active scene can be the persistent one, leaving the old story
			// scene behind (two copies, a game TextController exception when
			// the target is the one already on screen). Unload every copy of
			// the scene on screen before the transition; the game's own unload
			// then removes the loading screen only.
			string previousScene = PreviousStoryScene();
			int unloadTries = 0;
			while (!string.IsNullOrEmpty(previousScene) && unloadTries++ < 8)
			{
				Scene copy = FindLoadedScene(previousScene);
				if (!copy.IsValid())
				{
					break;
				}
				ModLog.Info("ChoiceWindow: unloading the copy of " + previousScene
					+ " already on screen before the transition");
				AsyncOperation unloading = UnloadStoryScene(copy);
				if (unloading == null)
				{
					break;
				}
				while (!unloading.isDone)
				{
					yield return null;
				}
			}
			ActivateLoadingScreen();
			AllowAchievements();
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

		// The game unlocks achievements only while GameManager.BlockAchievements
		// is false. Its own loads pass the loading screen, whose
		// AchievementsBlocker clears it (the main menu's sets it); a mod load
		// does the same, so a game continued through the mod earns them like
		// any other - unless a scene that blocks them is loaded.
		private static void AllowAchievements()
		{
			try
			{
				GameManager gm = GameManager.Instance;
				if (gm == null || !gm.BlockAchievements)
				{
					return;
				}
				foreach (AchievementsBlocker b in Resources.FindObjectsOfTypeAll<AchievementsBlocker>())
				{
					if (b.BlockAchievements && b.gameObject.scene.isLoaded
						&& b.gameObject.scene.name != "GameLoadingScreen")
					{
						return;   // a scene that blocks them on purpose
					}
				}
				gm.BlockAchievements = false;
				ModLog.Info("ChoiceWindow: achievements allowed for the loaded game");
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: achievement flag not set:\n" + e);
			}
		}

		private static string PreviousStoryScene()
		{
			try
			{
				GameManager gm = GameManager.Instance;
				string current = gm != null ? gm.OpenedSceneName : "";
				if (string.IsNullOrEmpty(current) || current == "GameLoadingScreen"
					|| current == "MainMenu")
				{
					return "";
				}
				return current;
			}
			catch
			{
				return "";
			}
		}

		private static Scene FindLoadedScene(string name)
		{
			for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
			{
				Scene s = SceneManager.GetSceneAt(i);
				if (s.IsValid() && s.isLoaded && s.name == name)
				{
					return s;
				}
			}
			return default(Scene);
		}

		private static AsyncOperation UnloadStoryScene(Scene scene)
		{
			try
			{
				return SceneManager.UnloadSceneAsync(scene);
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: unloading " + scene.name + " failed:\n" + e);
				return null;
			}
		}

		private static void ActivateLoadingScreen()
		{
			try
			{
				Scene loading = SceneManager.GetSceneByName("GameLoadingScreen");
				if (loading.IsValid() && loading.isLoaded)
				{
					SceneManager.SetActiveScene(loading);
				}
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: activating the loading screen failed:\n" + e);
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

		// Dev: finds two nearby scenes where picking another answer in the
		// first shuts the answer taken (or picked) in the second, makes both
		// picks and scrolls to them.
		private static bool DevMakeConflict()
		{
			List<TreeCol> cols = _baseCols;
			int current = cols.FindIndex(c => c.Current);
			int tries = 0;
			for (int j = Math.Min(cols.Count - 1, current + 3); j > 0 && tries < 400; j--)
			{
				TreeCol cj = cols[j];
				foreach (History.Choice later in cj.Choices)
				{
					if (cj.Blocked.Contains(later.Name)
						|| !SceneEffects.For(cj.Scene, later.Name).Exists(f => f.IsRequirement))
					{
						continue;
					}
					for (int i = j - 1; i >= Math.Max(0, j - 3); i--)
					{
						foreach (History.Choice earlier in cols[i].Choices)
						{
							if (earlier.Name == cols[i].PlayedChoice)
							{
								continue;
							}
							tries++;
							_overrides.Clear();
							_overrides[cj.Scene] = later.Name;
							_overrides[cols[i].Scene] = earlier.Name;
							ApplyPicks(cols);
							WorkOutOpenAnswers(cols);
							if (cj.Blocked.Contains(later.Name))
							{
								_selScene = cj.Scene;
								_selChoice = later.Name;
								RefreshHistory(false);
								Canvas.ForceUpdateCanvases();
								float span = ((RectTransform)_historyList).rect.width - _historyScroll.viewport.rect.width;
								_historyScroll.horizontalNormalizedPosition = Mathf.Clamp01((30f + (i - 1) * TreeColW) / span);
								ModLog.Info("ChoiceWindow: dev conflict: '" + earlier.Name + "' in " + cols[i].Scene
									+ " shuts '" + later.Name + "' in " + cj.Scene + " (" + tries + " tries)");
								return true;
							}
						}
					}
				}
			}
			_overrides.Clear();
			RefreshHistory(false);
			ModLog.Info("ChoiceWindow: dev conflict: none found (" + tries + " tries)");
			return false;
		}

		// Dev (dev-table.txt): open the save table, cycle the type filter
		// through every category, and show/hide the first type cell's pop-up.
		public static void DevTable()
		{
			try
			{
				int n = Open();
				ModLog.Info("DEV TABLE: opened " + n + " row(s), filter " + FilterName(_filter)
					+ ", shown " + _entries.Count);
				for (int i = 0; i < 5; i++)
				{
					CycleFilter();
					ModLog.Info("DEV TABLE: filter " + FilterName(_filter)
						+ ", rows " + _entries.Count + ", shown "
						+ Math.Min(_shown, _entries.Count));
				}
				foreach (TreeHover h in _itemContainer.GetComponentsInChildren<TreeHover>(true))
				{
					if (h.gameObject.name == "KindHint")
					{
						h.OnHover(true);
						ModLog.Info("DEV TABLE: type pop-up active "
							+ (_tablePopup != null && _tablePopup.gameObject.activeSelf));
						h.OnHover(false);
						break;
					}
				}
				// "Show more": the next page of rows (a first page of 10 when
				// the run has fewer rows than a page)
				if (ActiveChild("More") == null && _entries.Count > 10)
				{
					_shown = 10;
					BuildRows();
				}
				Transform more = ActiveChild("More");
				ModLog.Info("DEV TABLE: filter " + FilterName(_filter) + ", rows built " + RowCount()
					+ " of " + _entries.Count + ", Show more " + (more != null ? "present" : "absent"));
				if (more != null)
				{
					more.GetComponent<Button>().onClick.Invoke();
					ModLog.Info("DEV TABLE: after Show more, rows built " + RowCount() + " of " + _entries.Count
						+ ", Show more " + (ActiveChild("More") != null ? "present" : "absent"));
				}
				// the Original row: read-only (no delete cell), loads like any row
				while (_filter != SaveFilter.Original)
				{
					CycleFilter();
				}
				Transform orow = ActiveChild("Row");
				ModLog.Info("DEV TABLE: Original rows " + _entries.Count
					+ (orow != null ? ", delete cell " + (orow.Find("Delete") != null ? "PRESENT" : "absent") : ""));
				if (orow != null && File.Exists(Path.Combine(Recorder.SnapshotDir, "dev-table-load.txt")))
				{
					try { File.Delete(Path.Combine(Recorder.SnapshotDir, "dev-table-load.txt")); }
					catch (Exception) { }
					orow.GetComponent<Button>().onClick.Invoke();
					ModLog.Info("DEV TABLE: Original row clicked - asks " + _pendingAction + " of " + _pendingPath
						+ ", confirm shown " + (_confirmPanel != null && _confirmPanel.activeSelf));
					_confirmLoadBtn.onClick.Invoke();
					return;
				}
				Close();
				// screenshots: the table left open on All
				string keep = Path.Combine(Recorder.SnapshotDir, "dev-table-open.txt");
				if (File.Exists(keep))
				{
					try { File.Delete(keep); }
					catch (Exception) { }
					Open();
				}
			}
			catch (Exception e)
			{
				ModLog.Error("DEV TABLE failed:\n" + e);
			}
		}

		private static IEnumerator DevQuickCheck(int k)
		{
			TreeCol at = null;
			History.Choice other = null;
			for (int j = k; j >= 0 && j > k - 8 && other == null; j--)   // the nearest scene with another open answer
			{
				TreeCol col = _tree[j];
				at = col;
				other = col.Choices.Find(c => c.Name != col.PlayedChoice && !col.Blocked.Contains(c.Name)
					&& VisibleChoices(col).Contains(col.Choices.IndexOf(c)));
			}
			if (other == null)
			{
				ModLog.Info("ChoiceWindow: dev quick: no other open answer in " + at.Scene);
				yield break;
			}
			_overrides.Clear();
			SelectChoice(at.Scene, other.Name);
			yield return null;
			string last = at.Scene;
			for (int step = 0; step < 2; step++)
			{
				// each pick rebuilds the tree: the next column is found again
				int li = _tree.FindIndex(c => c.Scene == last);
				if (li < 0 || li + 1 >= _tree.Count)
				{
					StringBuilder tb = new StringBuilder();
					foreach (TreeCol c in _tree)
					{
						tb.Append(" " + c.Scene + (c.Upcoming ? "^" : "") + (c.Current ? "*" : ""));
					}
					ModLog.Info("ChoiceWindow: dev quick: tree" + tb);
					ModLog.Info("ChoiceWindow: dev quick: no scene after " + last);
					yield break;
				}
				TreeCol col = _tree[li + 1];
				last = col.Scene;
				string pick = !string.IsNullOrEmpty(col.PlayedChoice) && !col.Blocked.Contains(col.PlayedChoice)
					? col.PlayedChoice : FirstOpen(col);
				if (pick == null)
				{
					ModLog.Info("ChoiceWindow: dev quick: no open answer in " + col.Scene);
					yield break;
				}
				SelectChoice(col.Scene, pick);
				yield return null;
			}
			string target = last;
			string pw;
			if (PlanLoad(out pw) == null)
			{
				ModLog.Info("ChoiceWindow: dev quick: no plan to " + target + " (selected " + _selScene + "): " + pw);
				yield break;
			}
			_pendingPlan = null;
			LoadSelected();
			LoadPlan qp = _pendingPlan;
			ModLog.Info("ChoiceWindow: dev quick: '" + other.Name + "' in " + at.Scene + " to " + target
				+ ", estimate " + (_quick != null ? "offered - " + _quickNote : "NOT offered"));
			if (_quick == null || qp == null)
			{
				CancelConfirm();
				yield break;
			}
			float qt = Time.realtimeSinceStartup;
			QuickLoadNow();
			yield return new WaitForSeconds(2f);
			yield return AutoPlay.WaitForLoad();
			while (!AutoPlay.AnswerReady() && Time.realtimeSinceStartup - qt < 60f)
			{
				yield return new WaitForSeconds(0.5f);
			}
			ModLog.Info("ChoiceWindow: dev quick: now at " + AutoPlay.Story() + " after "
				+ (Time.realtimeSinceStartup - qt).ToString("0.0") + " s"
				+ (AutoPlay.Story() == target ? " (the selected scene)" : " (NOT the selected scene)"));
			yield return new WaitForSeconds(2f);
			string estimated = Recorder.ShownSnapshot;
			float rt = Time.realtimeSinceStartup;
			ModHost.Current.StartCoroutine(ReplayTo(qp));
			yield return new WaitForSeconds(5f);
			while (AutoPlay.Running && Time.realtimeSinceStartup - rt < 400f)
			{
				yield return new WaitForSeconds(1f);
			}
			yield return new WaitForSeconds(2f);
			ModLog.Info("ChoiceWindow: dev quick: the replay reached " + AutoPlay.Story() + " after "
				+ (Time.realtimeSinceStartup - rt).ToString("0.0") + " s");
			string real = Recorder.ShownSnapshot;
			ModLog.Info("ChoiceWindow: dev quick vs replay: " + Path.GetFileName(estimated) + " / " + Path.GetFileName(real) + ": "
				+ (estimated == real ? "the same state" : Accuracy.Diff(Recorder.ReadHelper(estimated), Recorder.ReadHelper(real))));
		}

		private static int RowCount()
		{
			int n = 0;
			foreach (Transform t in _itemContainer)
			{
				if (t.name == "Row" && t.gameObject.activeSelf)
				{
					n++;
				}
			}
			return n;
		}

		// A live child (rows being replaced are inactive until destroyed).
		private static Transform ActiveChild(string name)
		{
			foreach (Transform t in _itemContainer)
			{
				if (t.name == name && t.gameObject.activeSelf)
				{
					return t;
				}
			}
			return null;
		}

		public static void DevBook()
		{
			if (ModHost.Current != null)
			{
				ModHost.Current.StartCoroutine(DevBookDump());
			}
		}

		// Dev (dev-plan.txt): build a path from the scene on screen to
		// `target` by picking the first open answer everywhere, and log the
		// plan and its pre-checks. No replay; used to check StartLooksOnPath
		// and the ordered plan before a fast-forward.
		public static void DevPlan(string target, bool replay = false)
		{
			try
			{
				OpenTreeView();
				_overrides.Clear();
				// Pick the current scene's first open answer first: that can
				// change the scenes ahead, which the tree rebuilds.
				TreeCol cur = _tree.Find(c => c.Current);
				if (cur != null)
				{
					string first = FirstOpen(cur);
					if (first != null)
					{
						SelectChoice(cur.Scene, first);
					}
				}
				int ti = _tree.FindIndex(c => c.Scene == target);
				int ci = _tree.FindIndex(c => c.Current);
				ModLog.Info("DEV PLAN: target " + target + " index " + ti + ", current " + ci);
				if (ci < 0 || ti < 0 || ti <= ci)
				{
					return;
				}
				StringBuilder ahead = new StringBuilder();
				for (int k = ci; k <= Math.Min(_tree.Count - 1, ci + 6); k++)
				{
					ahead.Append(_tree[k].Scene + (k == ti ? "* " : " "));
				}
				ModLog.Info("DEV PLAN: scenes from current: " + ahead.ToString().Trim());
				for (int i = ci + 1; i < ti; i++)
				{
					TreeCol col = _tree[i];
					string pick = FirstOpen(col);
					if (pick == null)
					{
						ModLog.Info("DEV PLAN: no open answer in " + col.Scene);
						return;
					}
					SelectChoice(col.Scene, pick);
				}
				_selScene = target;
				string why;
				LoadPlan p = PlanLoad(out why);
				if (p == null)
				{
					ModLog.Info("DEV PLAN: no plan: " + why);
					return;
				}
				ModLog.Info("DEV PLAN: start " + p.StartScene + " target " + p.Target
					+ " speculative=" + p.Speculative + " order "
					+ string.Join(" > ", p.Order.ToArray()));
				if (replay && ModHost.Current != null)
				{
					ModLog.Info("DEV PLAN: replaying the plan");
					ModHost.Current.StartCoroutine(ReplayTo(p));
				}
			}
			catch (Exception e)
			{
				ModLog.Error("DEV PLAN failed:\n" + e);
			}
		}

		private static string FirstOpen(TreeCol col)
		{
			foreach (History.Choice c in col.Choices)
			{
				if (!col.Blocked.Contains(c.Name))
				{
					return c.Name;
				}
			}
			return null;
		}

		private static IEnumerator DevBookDump()
		{
			// Dev: the book tabs - the tree view's opens the tree, and its x
			// goes back to the game; the save tab opens the save table.
			Button treeTab = AutoPlay.FindNamed("HUD.TreeView");
			Button saveTab = AutoPlay.FindNamed("HUD.SaveLoad");
			if (treeTab != null && saveTab != null)
			{
				// hover each tab as the pointer would: the game's tooltip opens,
				// with our text in it, and the tab lights up
				foreach (Button tabBtn in new[] { saveTab, treeTab })
				{
					PointerEventData hp = new PointerEventData(EventSystem.current);
					ExecuteEvents.Execute(tabBtn.gameObject, hp, ExecuteEvents.pointerEnterHandler);
					ModLog.Info("ChoiceWindow: dev hovering " + tabBtn.name);
					yield return new WaitForSeconds(2.5f);
					GameObject tipGo = RegisterGO.Find("Simple_tooltip");
					TextMeshProUGUI tipText = tipGo != null ? tipGo.GetComponentInChildren<TextMeshProUGUI>() : null;
					ModLog.Info("ChoiceWindow: dev hover " + tabBtn.name + ": tooltip "
						+ (tipGo != null && tipGo.activeInHierarchy ? "'" + (tipText != null ? tipText.text : "") + "'" : "not shown"));
					ExecuteEvents.Execute(tabBtn.gameObject, hp, ExecuteEvents.pointerExitHandler);
					yield return new WaitForSeconds(0.5f);
				}
				treeTab.onClick.Invoke();
				yield return new WaitForSeconds(3f);
				bool treeOpen = _historyList != null && _historyList.gameObject.activeInHierarchy;
				CloseHistory();
				ModLog.Info("ChoiceWindow: dev tree tab -> tree view " + (treeOpen ? "open" : "NOT open")
					+ ", after its x the window is " + (_canvas != null && _canvas.activeSelf ? "STILL open" : "closed"));
				saveTab.onClick.Invoke();
				yield return new WaitForSeconds(2f);
				ModLog.Info("ChoiceWindow: dev save tab -> save table " + (_canvas != null && _canvas.activeSelf
					&& (_historyPanel == null || !_historyPanel.activeSelf) ? "open" : "NOT open"));
				Close();
			}
			yield return new WaitForSeconds(2f);
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
			// Dev: the game's tooltip over a stat (the first row's hit area).
			Transform statsHover = _statsArea != null ? _statsArea.Find("Parameter_Horizontal(Clone)/Hover") : null;
			if (statsHover == null && _statsArea != null)
			{
				foreach (TreeHover th in _statsArea.GetComponentsInChildren<TreeHover>())
				{
					statsHover = th.transform;
					break;
				}
			}
			if (statsHover != null)
			{
				statsHover.GetComponent<TreeHover>().OnHover(true);
				ModLog.Info("ChoiceWindow: dev stat tooltip shown");
				yield return new WaitForSeconds(3f);
				statsHover.GetComponent<TreeHover>().OnHover(false);
			}
			// Dev: a pick two scenes ahead with none between - Load greyed with
			// the gap named; the same answer clicked again takes the pick back.
			{
				int cur = _tree.FindIndex(c => c.Current);
				if (cur >= 0 && cur + 2 < _tree.Count)
				{
					TreeCol far = _tree[cur + 2];
					SelectChoice(far.Scene, far.Choices[0].Name);
					ModLog.Info("ChoiceWindow: dev gap pick: load interactable=" + _loadHereBtn.GetComponent<Button>().interactable
						+ ", why '" + LoadBlockedReason() + "'");
					TreeCol near = _tree[cur + 1];
					SelectChoice(near.Scene, near.Choices[0].Name);
					ModLog.Info("ChoiceWindow: dev nearer pick after a far one: selected "
						+ (_selScene == far.Scene ? "still the far one" : "'" + _selScene + "' (WRONG)"));
					SelectChoice(near.Scene, near.Choices[0].Name);
					SelectChoice(far.Scene, far.Choices[0].Name);
					ModLog.Info("ChoiceWindow: dev unpick: picks left " + _overrides.Count + ", selected '" + (_selScene ?? "") + "'");
				}
				for (int i = 0; i < _tree.Count; i++)
				{
					string cs = _tree[i].Upcoming ? null : ChapterStartSave(_tree, i);
					if (cs != null)
					{
						ModLog.Info("ChoiceWindow: dev chapter start save for " + _tree[i].Scene + ": " + cs);
					}
				}
			}
			// Dev: a pick made impossible by an earlier one - red, Load greyed.
			if (DevMakeConflict())
			{
				yield return new WaitForSeconds(1f);
				_loadHereBtn.GetComponent<TreeHover>().OnHover(true);
				ModLog.Info("ChoiceWindow: dev conflict: load interactable="
					+ _loadHereBtn.GetComponent<Button>().interactable + ", hover '" + _popupText.text + "'");
				yield return new WaitForSeconds(5f);
				_loadHereBtn.GetComponent<TreeHover>().OnHover(false);
				UndoSelections();
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
			// Dev: the first scene that may not come, and what it depends on.
			int possibleAt = _tree.FindIndex(c => c.Possible);
			if (possibleAt >= 0)
			{
				float range = contentRt.rect.width - _historyScroll.viewport.rect.width;
				_historyScroll.horizontalNormalizedPosition = Mathf.Clamp01((30f + (possibleAt - 1) * TreeColW) / range);
				ModLog.Info("ChoiceWindow: dev possible scene " + _tree[possibleAt].Scene + ": '"
					+ PossibleReason(_tree[possibleAt].Scene) + "'");
				yield return new WaitForSeconds(3f);
			}
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
				string lpArgs = "";
				try { lpArgs = File.ReadAllText(lp).Trim(); File.Delete(lp); }
				catch (Exception) { }
				TreeCol cur = _tree.Find(c => c.Current);
				int ci = cur != null ? _tree.IndexOf(cur) : -1;
				// "start": no mod saves; another answer in the story's first
				// scene, every played answer after it, to two scenes before the
				// one on screen - a replay from the game's chapter 1 save.
				// "ahead": the first open answer from the scene on screen to the
				// first scene of the next chapter.
				// "death": in the latest played scene with an answer that adds a
				// death, that answer, then played (or first open) answers for two
				// more scenes - the replay must go through the death page.
				// "first": the first open answer in the story's first two scenes,
				// to the third - a replay from the very start of the story.
				// "file <name>": load that mod save (any run) as the save table
				// does, and log what the game shows afterwards
				if (lpArgs.StartsWith("file ", StringComparison.Ordinal))
				{
					string want = lpArgs.Substring(5).Replace(" norun", "").Trim();
					string found = null;
					foreach (string f in Directory.GetFiles(Recorder.RunsDir, want, SearchOption.AllDirectories))
					{
						found = f;
					}
					ModLog.Info("ChoiceWindow: dev load-path file: " + (found ?? "not found: " + want));
					if (found != null)
					{
						Close();
						if (lpArgs.EndsWith(" norun", StringComparison.Ordinal))
						{
							Recorder.DevForgetRun();
						}
						LoadSnapshot(found);
						yield return new WaitForSeconds(14f);
						GameManager g = GameManager.Instance;
						ModLog.Info("ChoiceWindow: dev load-path file: now at " + AutoPlay.Story() + ", chapter "
							+ (g != null ? g.Chapter.ToString() : "?") + ", answers up " + AutoPlay.AnswerReady());
					}
					yield break;
				}
				if ((lpArgs == "first" || lpArgs == "new") && _tree.Count > 3)
				{
					DevFromChapterStart = true;
					DevStandIn = true;
					DevNoChapter1 = lpArgs == "new";
					_overrides.Clear();
					for (int k = 0; k <= 2; k++)
					{
						TreeCol col = _tree[k];
						SelectChoice(col.Scene, col.Choices.Find(c => !col.Blocked.Contains(c.Name)).Name);
					}
					string fWhy;
					LoadPlan fp = PlanLoad(out fWhy);
					ModLog.Info("ChoiceWindow: dev load-path " + lpArgs + ": to " + _tree[2].Scene + ", plan "
						+ (fp != null ? fp.StartPath + " with " + fp.Picks.Count + " answer(s)" : "none: " + fWhy)
						+ ", Load scene " + (_loadHereBtn.GetComponent<Button>().interactable ? "enabled" : "greyed: " + LoadBlockedReason()));
					if (fp != null)
					{
						LoadSelected();
						yield return new WaitForSeconds(1f);
						RunPending();
						yield return new WaitForSeconds(5f);
						float until7 = Time.realtimeSinceStartup + 400f;
						while (AutoPlay.Running && Time.realtimeSinceStartup < until7)
						{
							yield return new WaitForSeconds(1f);
						}
						yield return new WaitForSeconds(2f);
						ModLog.Info("ChoiceWindow: dev load-path: now at " + AutoPlay.Story()
							+ (AutoPlay.Story() == _tree[2].Scene ? " (the selected scene)" : " (NOT the selected scene)")
							+ ", main save now '" + Recorder.CurrentRunId() + "', hero '" + (ParametersManager.Instance != null ? ParametersManager.Instance.HeroName : "") + "'");
						// the state reached, against the scene's other saves (same answers, other starts)
						List<string> same = History.SnapshotsFor(_tree[2].Scene);
						if (same.Count > 1)
						{
							SceneEffects.Hero mine = SceneEffects.FromSnapshot(same[0]);
							SaveLoadHelper mineH = Recorder.ReadHelper(same[0]);
							for (int k = 1; k < same.Count; k++)
							{
								SaveLoadHelper other = Recorder.ReadHelper(same[k]);
								string shown = "", shownO = "";
								foreach (ShowedScenes sc in mineH.ShowedScenesList) shown += sc.SceneName + (sc.IsShowed ? "+" : "-") + ",";
								foreach (ShowedScenes sc in other.ShowedScenesList) shownO += sc.SceneName + (sc.IsShowed ? "+" : "-") + ",";
								int objs = 0;
								for (int o = 0; o < mineH.AllGameObjectives.Objectives.Count && o < other.AllGameObjectives.Objectives.Count; o++)
								{
									if (mineH.AllGameObjectives.Objectives[o].Status != other.AllGameObjectives.Objectives[o].Status) objs++;
								}
								ModLog.Info("ChoiceWindow: dev compare with " + Path.GetFileName(same[k]) + ": stat/relation distance "
									+ SceneEffects.Distance(mine, SceneEffects.FromSnapshot(same[k])) + ", objectives differing " + objs
									+ ", scene list " + (shown == shownO ? "same" : "DIFFERENT\n  " + shown + "\n  " + shownO)
									+ ", consequence '" + mineH.CurrentConsequence + "'/'" + other.CurrentConsequence + "'");
							}
						}
					}
					DevFromChapterStart = false;
					DevStandIn = false;
					DevNoChapter1 = false;
					yield break;
				}
				if (lpArgs == "offpath" && ci + 2 < _tree.Count)
				{
					_overrides.Clear();
					for (int k = ci; k <= ci + 2; k++)
					{
						TreeCol col = _tree[k];
						SelectChoice(col.Scene, col.Choices.Find(c => !col.Blocked.Contains(c.Name)).Name);
					}
					DevDropPick = _tree[ci + 1].Scene;
					LoadSelected();
					yield return new WaitForSeconds(1f);
					RunPending();
					yield return new WaitForSeconds(5f);
					float until6 = Time.realtimeSinceStartup + 300f;
					while (AutoPlay.Running && Time.realtimeSinceStartup < until6)
					{
						yield return new WaitForSeconds(1f);
					}
					yield return new WaitForSeconds(3f);
					ModLog.Info("ChoiceWindow: dev load-path offpath: now at " + AutoPlay.Story() + ", tree "
						+ (_historyPanel != null && _historyPanel.activeInHierarchy ? "open" : "closed")
						+ ", info '" + (_historyInfo != null ? _historyInfo.text : "") + "'");
					yield break;
				}
				if (lpArgs == "death")
				{
					_overrides.Clear();
					int dk = -1;
					string dAnswer = null;
					for (int k = ci - 1; k >= 0 && dk < 0; k--)
					{
						foreach (History.Choice c in _tree[k].Choices)
						{
							if (!_tree[k].Blocked.Contains(c.Name) && _tree[k].HasSnapshot
								&& SceneEffects.For(_tree[k].Scene, c.Name).Exists(f => f.Kind == 'P' && f.Name == "Death" && f.Value > 0 && !f.IsRequirement))
							{
								dk = k;
								dAnswer = c.Name;
								break;
							}
						}
					}
					if (dk >= 0 && dk + 2 < _tree.Count)
					{
						SelectChoice(_tree[dk].Scene, dAnswer);
						for (int k = dk + 1; k <= dk + 2; k++)
						{
							TreeCol col = _tree[k];
							string pick = !string.IsNullOrEmpty(col.PlayedChoice) && !col.Blocked.Contains(col.PlayedChoice) ? col.PlayedChoice
								: col.Choices.Find(c => !col.Blocked.Contains(c.Name)).Name;
							SelectChoice(col.Scene, pick);
						}
						TreeCol dGoal = _tree[dk + 2];
						string dWhy;
						LoadPlan dp = PlanLoad(out dWhy);
						ModLog.Info("ChoiceWindow: dev load-path death: " + _tree[dk].Scene + " -> " + dAnswer + ", to " + dGoal.Scene
							+ ", plan " + (dp != null ? dp.Picks.Count + " answer(s)" : "none: " + dWhy));
						if (dp != null)
						{
							LoadSelected();
							yield return new WaitForSeconds(1f);
							RunPending();
							yield return new WaitForSeconds(5f);
							float until5 = Time.realtimeSinceStartup + 600f;
							while (AutoPlay.Running && Time.realtimeSinceStartup < until5)
							{
								yield return new WaitForSeconds(1f);
							}
							yield return new WaitForSeconds(2f);
							ModLog.Info("ChoiceWindow: dev load-path: now at " + AutoPlay.Story()
								+ (AutoPlay.Story() == dGoal.Scene ? " (the selected scene)" : " (NOT the selected scene)"));
						}
					}
					else
					{
						ModLog.Info("ChoiceWindow: dev load-path death: no played scene with a death answer");
					}
					yield break;
				}
				if (lpArgs == "start" || lpArgs == "ahead")
				{
					DevFromChapterStart = lpArgs == "start";
					_overrides.Clear();
					TreeCol goal = null;
					if (lpArgs == "start" && ci >= 3)
					{
						goal = _tree[ci - 2];
						for (int k = 0; k < _tree.IndexOf(goal); k++)
						{
							if (!string.IsNullOrEmpty(_tree[k].PlayedChoice))
							{
								_overrides[_tree[k].Scene] = _tree[k].PlayedChoice;
							}
						}
						SelectChoice(goal.Scene, goal.Choices.Find(c => !goal.Blocked.Contains(c.Name)).Name);
						// the earliest chapter 1 change that leaves the path open
						bool found = false;
						for (int k = 0; k < _tree.Count && !found && SceneFlow.ChapterOf(_tree[k].Scene) == 1; k++)
						{
							string played = _tree[k].PlayedChoice;
							foreach (History.Choice c in _tree[k].Choices)
							{
								if (c.Name == played || _tree[k].Blocked.Contains(c.Name))
								{
									continue;
								}
								_overrides[_tree[k].Scene] = c.Name;
								RefreshHistory(false);
								string w;
								if (FirstConflict() == null && PlanLoad(out w) != null)
								{
									found = true;
									ModLog.Info("ChoiceWindow: dev load-path start: changing " + _tree[k].Scene + " to " + c.Name);
									break;
								}
							}
							if (!found)
							{
								_overrides[_tree[k].Scene] = played;
								RefreshHistory(false);
							}
						}
					}
					else if (lpArgs == "ahead")
					{
						int ch = SceneFlow.ChapterOf(cur.Scene);
						int first = _tree.FindIndex(ci, c => SceneFlow.ChapterOf(c.Scene) > ch);
						if (first > 0)
						{
							goal = _tree[first];
							for (int k = ci; k <= first; k++)
							{
								TreeCol col = _tree[k];
								History.Choice open = null;
								foreach (int v in VisibleChoices(col))
								{
									if (!col.Blocked.Contains(col.Choices[v].Name))
									{
										open = col.Choices[v];
										break;
									}
								}
								if (open != null && !_overrides.ContainsKey(col.Scene))
								{
									SelectChoice(col.Scene, open.Name);
								}
							}
						}
					}
					string planWhy = "";
					LoadPlan sp = goal != null ? PlanLoad(out planWhy) : null;
					if (goal == null)
					{
						planWhy = "no goal";
					}
					ModLog.Info("ChoiceWindow: dev load-path " + lpArgs + ": to " + (goal != null ? goal.Scene : "-") + ", plan "
						+ (sp != null ? sp.StartPath + " with " + sp.Picks.Count + " answer(s)" : "none: " + planWhy)
						+ ", first answer '" + (_overrides.ContainsKey(_tree[0].Scene) ? _overrides[_tree[0].Scene] : "") + "' (played '" + _tree[0].PlayedChoice + "')");
					if (sp != null)
					{
						LoadSelected();
						yield return new WaitForSeconds(1f);
						RunPending();
						yield return new WaitForSeconds(5f);
						float until4 = Time.realtimeSinceStartup + 900f;
						while (AutoPlay.Running && Time.realtimeSinceStartup < until4)
						{
							yield return new WaitForSeconds(1f);
						}
						yield return new WaitForSeconds(2f);
						ModLog.Info("ChoiceWindow: dev load-path: now at " + AutoPlay.Story()
							+ (AutoPlay.Story() == goal.Scene ? " (the selected scene)" : " (NOT the selected scene)"));
					}
					DevFromChapterStart = false;
					yield break;
				}
				// "chapter": from the start of the chapter on screen (its game
				// save), every played answer again, to the scene on screen with
				// another answer there selected
				if (lpArgs == "chapter" && ci > 0)
				{
					int first = ci;
					while (first > 0 && SceneFlow.ChapterOf(_tree[first - 1].Scene) == SceneFlow.ChapterOf(cur.Scene))
					{
						first--;
					}
					DevFromChapterStart = true;
					History.Choice other = _tree[first].Choices.Find(c => c.Name == _tree[first].PlayedChoice);
					SelectChoice(_tree[first].Scene, other != null ? other.Name : _tree[first].Choices[0].Name);
					SelectChoice(cur.Scene, cur.Choices.Find(c => !cur.Blocked.Contains(c.Name)).Name);
					string planWhy;
					LoadPlan cp = PlanLoad(out planWhy);
					ModLog.Info("ChoiceWindow: dev load-path: from the chapter start - plan "
						+ (cp != null ? cp.StartPath + " with " + cp.Picks.Count + " answer(s)" : "none: " + planWhy));
					if (cp != null)
					{
						LoadSelected();
						yield return new WaitForSeconds(1f);
						RunPending();
						yield return new WaitForSeconds(5f);
						float until3 = Time.realtimeSinceStartup + 500f;
						while (AutoPlay.Running && Time.realtimeSinceStartup < until3)
						{
							yield return new WaitForSeconds(1f);
						}
						ModLog.Info("ChoiceWindow: dev load-path: now at " + AutoPlay.Story()
							+ (AutoPlay.Story() == cur.Scene ? " (the selected scene)" : " (NOT the selected scene)"));
					}
					DevFromChapterStart = false;
					yield break;
				}
				int back;
				// "back N": another answer N scenes back, then on to the scene on
				// screen - a replay across several scenes (and years)
				// "quick N": another answer N scenes back, the scene two columns
				// later on the new path as the target; Quick load there, then the
				// exact replay of the same plan, and the two states compared
				int qn;
				if (lpArgs.StartsWith("quick ", StringComparison.Ordinal) && int.TryParse(lpArgs.Substring(6), out qn)
					&& ci - qn >= 0)
				{
					yield return DevQuickCheck(ci - qn);
					yield break;
				}
				if (lpArgs.StartsWith("back ", StringComparison.Ordinal) && int.TryParse(lpArgs.Substring(5), out back)
					&& ci - back >= 0)
				{
					TreeCol from = null;
					History.Choice other = null;
					// the first change N or more scenes back that leaves the path
					// open all the way to the scene on screen
					for (int k = ci - back; k >= 0 && other == null; k--)
					{
						foreach (History.Choice c in _tree[k].Choices)
						{
							if (c.Name == _tree[k].PlayedChoice || _tree[k].Blocked.Contains(c.Name))
							{
								continue;
							}
							_overrides.Clear();
							SelectChoice(_tree[k].Scene, c.Name);
							// a frame after every pick, before any `continue`: each
							// pick rebuilds the tree, and the old one is destroyed only
							// when the frame ends (dozens of rebuilds in one frame kept
							// ~100k objects alive and made each draw take 30 s)
							yield return null;
							// the change re-predicts the path: it must still reach
							// the scene on screen
							TreeCol curNow = _tree.Find(x => x.Scene == cur.Scene);
							History.Choice open = curNow != null ? curNow.Choices.Find(x => !curNow.Blocked.Contains(x.Name)) : null;
							if (open == null)
							{
								continue;
							}
							SelectChoice(cur.Scene, open.Name);
							string w;
							bool fits = FirstConflict() == null && PlanLoad(out w) != null;
							yield return null;
							if (fits)
							{
								from = _tree[k];
								other = c;
								break;
							}
						}
					}
					if (other != null)
					{
						yield return new WaitForSeconds(1f);
						string planWhy;
						if (PlanLoad(out planWhy) == null)
						{
							ModLog.Info("ChoiceWindow: dev load-path: no plan: " + planWhy);
						}
						LoadSelected();
						yield return new WaitForSeconds(1f);
						ModLog.Info("ChoiceWindow: dev load-path: '" + other.Name + "' in " + from.Scene + ", replaying to " + cur.Scene);
						RunPending();
						yield return new WaitForSeconds(5f);
						float until2 = Time.realtimeSinceStartup + 400f;
						while (AutoPlay.Running && Time.realtimeSinceStartup < until2)
						{
							yield return new WaitForSeconds(1f);
						}
						ModLog.Info("ChoiceWindow: dev load-path: now at " + AutoPlay.Story()
							+ (AutoPlay.Story() == cur.Scene ? " (the selected scene)" : " (NOT the selected scene)"));
					}
					yield break;
				}
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

			// A straight stroke `t` wide from (x1, y1) to (x2, y2), square ends.
			public void Line(float x1, float y1, float x2, float y2, float t)
			{
				float dx = x2 - x1, dy = y2 - y1;
				float len2 = dx * dx + dy * dy;
				int x0 = Mathf.FloorToInt(Mathf.Min(x1, x2) - t), xe = Mathf.CeilToInt(Mathf.Max(x1, x2) + t);
				int y0 = Mathf.FloorToInt(Mathf.Min(y1, y2) - t), ye = Mathf.CeilToInt(Mathf.Max(y1, y2) + t);
				for (int yy = y0; yy < ye; yy++)
				{
					for (int xx = x0; xx < xe; xx++)
					{
						float px = xx + 0.5f - x1, py = yy + 0.5f - y1;
						float u = len2 > 0f ? (px * dx + py * dy) / len2 : 0f;
						float half = t * 0.5f / Mathf.Max(1f, Mathf.Sqrt(len2));
						if (u < -half || u > 1f + half)
						{
							continue;
						}
						float cx = px - u * dx, cy = py - u * dy;
						if (cx * cx + cy * cy <= t * t * 0.25f)
						{
							Rect(xx, yy, 1, 1);
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

		// The save tab's icon: a download arrow at the top left and an upload
		// arrow at the bottom right, split by a diagonal from the bottom left
		// to the top right. The arrows follow Up.png and
		// Down.png (drawn on a 1000-unit grid, here scaled into each corner).
		private static Sprite MakeSaveLoadIcon()
		{
			const int n = 128;
			Canvas32 c = new Canvas32(n, n);
			// (the drawings keep a sixth of their square empty all round)
			DrawTrayArrow(c, -6f, -6f, 72f, false);   // pulled up and left: its tray clear of the line
			DrawTrayArrow(c, 56f, 56f, 72f, true);
			c.Line(14f, 114f, 114f, 14f, 6f);
			return c.ToSprite();
		}

		// One arrow over its tray, in the square at (ox, oy), `size` across.
		private static void DrawTrayArrow(Canvas32 c, float ox, float oy, float size, bool up)
		{
			float k = size / 1000f;
			const float t = 100f;   // the drawings' 62 units, thickened for the small size
			System.Action<float, float, float, float> seg = delegate(float x1, float y1, float x2, float y2)
			{
				c.Line(ox + x1 * k, oy + y1 * k, ox + x2 * k, oy + y2 * k, t * k);
			};
			if (up)
			{
				seg(500f, 200f, 500f, 670f);
				seg(320f, 380f, 500f, 200f);
				seg(500f, 200f, 680f, 380f);
			}
			else
			{
				seg(500f, 170f, 500f, 640f);
				seg(320f, 460f, 500f, 640f);
				seg(500f, 640f, 680f, 460f);
			}
			// the tray: two posts and the floor
			seg(198f, 620f, 198f, 800f);
			seg(802f, 620f, 802f, 800f);
			seg(198f, 800f, 802f, 800f);
		}

		// The tree tab's icon, after Flow.png and drawn as the tree view draws
		// a path: three answer boxes on the left, two on the right with the top
		// ones level; each column has its spine on its left with a stub to
		// every box, and the path runs from the middle left box across to the
		// right spine, which leads up to the top right box. One stroke width
		// throughout, every line ending flush where it meets another.
		private static Sprite MakeTreeTabIcon()
		{
			Canvas32 c = new Canvas32(168, 128);
			const float t = 6f;                     // every stroke
			const float bw = 40f, bh = 26f, gap = 37f, top = 14f;
			const float lSpine = 6f, lBox = 22f;    // left column: spine, boxes
			const float rSpine = 96f, rBox = 112f;  // right column
			float[] mid = { top + bh * 0.5f, top + gap + bh * 0.5f, top + 2f * gap + bh * 0.5f };
			for (int r = 0; r < 3; r++)
			{
				c.Frame(lBox, top + r * gap, bw, bh, t);
				c.Rect(lSpine, mid[r] - t * 0.5f, lBox - lSpine, t);      // stub, spine to box
			}
			c.Rect(lSpine, mid[0] - t * 0.5f, t, mid[2] - mid[0] + t);   // left spine
			for (int r = 0; r < 2; r++)
			{
				c.Frame(rBox, top + r * gap, bw, bh, t);
				c.Rect(rSpine, mid[r] - t * 0.5f, rBox - rSpine, t);
			}
			c.Rect(rSpine, mid[0] - t * 0.5f, t, mid[1] - mid[0] + t);   // right spine
			c.Rect(lBox + bw, mid[1] - t * 0.5f, rSpine - (lBox + bw), t);   // the path across
			return c.ToSprite();
		}

		// The delete icon, after Delete.png: a bin with its lid and handle and
		// two slats, on the drawing's 1000-unit grid.
		private static Sprite MakeBinIcon()
		{
			const int n = 100;
			Canvas32 c = new Canvas32(n, n);
			const float k = n / 1000f;
			System.Action<float, float, float, float> box = delegate(float x0, float y0, float x1, float y1)
			{
				c.Rect(x0 * k, y0 * k, (x1 - x0) * k, (y1 - y0) * k);
			};
			box(362f, 125f, 638f, 160f);   // handle
			box(167f, 157f, 833f, 218f);   // lid
			box(210f, 218f, 272f, 873f);   // sides
			box(728f, 218f, 790f, 873f);
			box(210f, 812f, 790f, 873f);   // bottom
			box(382f, 307f, 445f, 722f);   // slats
			box(555f, 307f, 618f, 722f);
			return c.ToSprite();
		}

		private static Sprite _binIcon;

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
				bars[i].color = Palette.Ink;
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
				Stretch(AddLabel(go.transform, "Label", label, fontSize, Palette.TextLight, true).rectTransform);
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
		// The width of a label's text on one line. TMP's GetPreferredValues
		// gives nonsense in this game's TMP (11 units for "Load scene"), so this
		// lays the text out and reads its bounds; the label must be on screen.
		private static float LineWidth(TextMeshProUGUI t)
		{
			if (!t.isActiveAndEnabled)
			{
				return 0f;
			}
			bool wrap = t.enableWordWrapping;
			TextOverflowModes overflow = t.overflowMode;
			t.enableWordWrapping = false;
			t.overflowMode = TextOverflowModes.Overflow;
			t.ForceMeshUpdate();
			float w = t.textBounds.size.x;
			t.enableWordWrapping = wrap;
			t.overflowMode = overflow;
			t.ForceMeshUpdate();
			return w;
		}

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
