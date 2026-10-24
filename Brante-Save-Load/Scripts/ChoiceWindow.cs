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
	//  - a "Load any choice" tab injected into the book (the Destiny window),
	//    next to the chapter I tab (copy of it, so it looks like the rest of
	//    the book); see InjectBookTab,
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
	// After a save load, Recorder.BlockGameSaves stays on for the rest of
	// the session: the game's own Saves/ (the player's original timeline)
	// must never be touched by a rewind; progress past the rewind point is
	// captured as new choice-point snapshots instead.
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

		private enum Pending { Load, Delete, SaveNow }

		// Table columns, left to right. Choice is the flexible one.
		private enum Col { Type, Saved, Chapter, Hero, Age, Year, Season, Turn, Scene, Choice }

		private static readonly string[] ColNames =
		{ "Save type", "Saved", "Chapter", "Hero", "Age", "Year", "Season", "Turn", "Scene", "Choice" };

		// Preferred widths in reference px; Choice takes whatever is left.
		private static readonly float[] ColWidths =
		{ 130f, 170f, 150f, 130f, 70f, 80f, 100f, 80f, 120f, 200f };

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
			public string Hero;
			public int Age;
			public int Year;
			public int Season;
			public int Turn;
			public string Scene;

			public string Choice
			{
				get { return string.IsNullOrEmpty(Title) ? Scene : Title; }
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
		private static Col _sortCol = Col.Saved;
		private static bool _sortAsc;
		private static List<SaveEntry> _entries = new List<SaveEntry>();
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
			// Visible, labelled, full menu width buttons.
			List<Button> cands = new List<Button>();
			foreach (Button b in pause.GetComponentsInChildren<Button>(true))
			{
				if (!b.gameObject.activeInHierarchy || b.name.StartsWith("BranteSaveLoad_", StringComparison.Ordinal)
					|| b.GetComponentInChildren<TMP_Text>(true) == null)
				{
					continue;
				}
				RectTransform r = b.GetComponent<RectTransform>();
				if (r == null || r.rect.width < 300f)
				{
					continue;
				}
				cands.Add(b);
			}
			if (cands.Count == 0)
			{
				ModLog.Error("ChoiceWindow: no menu buttons found in the pause window");
				return;
			}

			// The lowest one is the template.
			Button template = cands[0];
			foreach (Button b in cands)
			{
				if (b.transform.position.y < template.transform.position.y)
				{
					template = b;
				}
			}

			// Its row: the ancestor directly under a VerticalLayoutGroup.
			// (A black frame is also a vertical layout, but it has its own Image.)
			Transform row = template.transform;
			for (int i = 0; i < 6 && row.parent != null && !IsMenuList(row.parent); i++)
			{
				row = row.parent;
			}
			if (row.parent == null || !IsMenuList(row.parent))
			{
				ModLog.Error("ChoiceWindow: menu row layout not found - settings button not added");
				return;
			}
			Transform menu = row.parent;

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
				t.text = "Load any choice";
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
				"Load any choice", 34f, ColTextDark, false);
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

			// Save now (bottom left).
			_saveNowBtn = CreateUiObject("SaveNow", panel.transform);
			RectTransform nrt = _saveNowBtn.GetComponent<RectTransform>();
			nrt.anchorMin = new Vector2(0f, 0f);
			nrt.anchorMax = new Vector2(0f, 0f);
			nrt.pivot = new Vector2(0f, 0f);
			nrt.anchoredPosition = new Vector2(22f, 20f);
			nrt.sizeDelta = new Vector2(260f, 52f);
			Image nimg = _saveNowBtn.AddComponent<Image>();
			nimg.color = ColLoadButton;
			Button nbtn = _saveNowBtn.AddComponent<Button>();
			nbtn.targetGraphic = nimg;
			nbtn.onClick.AddListener(delegate { Ask(Pending.SaveNow, "", "", null); });
			TextMeshProUGUI nlabel = AddLabel(_saveNowBtn.transform, "Label",
				"Save now", 24f, ColTextLight, true);
			Stretch(nlabel.rectTransform);

			BuildConfirm(panel.transform);
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

			_headerLabels = new TextMeshProUGUI[ColNames.Length];
			_sortIcons = new Image[ColNames.Length];
			for (int i = 0; i < ColNames.Length; i++)
			{
				Col col = (Col)i;
				GameObject cell = CreateUiObject("Head_" + ColNames[i], header.transform);
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
				TextMeshProUGUI label = AddLabel(cell.transform, "Label", ColNames[i], 19f, ColTextLight,
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
			TextMeshProUGUI lt = AddLabel(loadBtn.transform, "Label", "Load", 24f, ColTextLight, true);
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
			TextMeshProUGUI xt = AddLabel(cancelBtn.transform, "Label", "Cancel", 24f, ColTextLight, true);
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
					body = "Load this save?\n\n" + title
						+ "\n\nYour original save will not be modified.";
					label = "Load";
					break;
				case Pending.Delete:
					body = "Remove this save from the list?\n\n" + title
						+ "\n\nThe file is deleted. Your game saves are not touched.";
					label = "Delete";
					break;
				default:
					body = "Save the current game now?\n\n"
						+ "It is added to the list as a new row. Your game saves are not touched.";
					label = "Save";
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
				case Col.Hero: return e.Hero.ToLowerInvariant();
				case Col.Age: return e.Age;
				case Col.Year: return e.Year;
				case Col.Season: return e.Season;
				case Col.Turn: return e.Turn;
				case Col.Scene: return e.Scene;
				default: return e.Choice.ToLowerInvariant();
			}
		}

		// (Re)builds the table rows from _entries, in their current order.
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
					"No saves yet. Choice points are recorded automatically; use \"Save now\" below.",
					22f, ColTextDark, true);
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
				e.Kind,
				e.Saved.ToString("yyyy-MM-dd HH:mm"),
				e.Chapter + (e.ChapterName.Length > 0 ? " " + e.ChapterName : ""),
				e.Hero,
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
				TextMeshProUGUI cell = AddLabel(row.transform, "Cell", values[i], 19f, ColTextDark,
					col != Col.Hero && col != Col.Choice);
				cell.overflowMode = TextOverflowModes.Ellipsis;
				cell.enableWordWrapping = false;
				LayoutElement le = cell.gameObject.AddComponent<LayoutElement>();
				le.preferredWidth = ColWidths[i];
				if (col == Col.Choice)
				{
					le.flexibleWidth = 1f;
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
				+ " (scene=" + helper.LastOpenScene + ")");
			// The blob has no ID (for stable dedup), so the run comes from
			// the folder the file sits in.
			string runId = Recorder.RunIdFromPath(datPath);
			if (string.IsNullOrEmpty(runId) && helper.SlotInfo != null)
			{
				runId = helper.SlotInfo.Id;
			}
			Recorder.PinRunId(runId);
			// From here on the game's own Saves/ stays frozen for the rest
			// of the session: a rewind must never overwrite the original
			// timeline. (See Entrypoint.PatchSaveGate.)
			Recorder.BlockGameSaves = true;

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
