using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using _Scripts.AMVCC.Views.Windows;
using _Scripts.Helpers;
using _Scripts.Managers;

namespace Doorstop
{
	// Phase 4/5: the player-facing save list.
	//
	//  - a button injected into the game's own pause window (WindowPause,
	//    opened with Esc / UIManager.ShowPauseMenu),
	//  - one window with two tabs, both shown as a sortable table:
	//      "Choice points" - the automatic snapshots taken at every choice,
	//      "Saved games"   - the manual saves made with "Save now",
	//    one row per save, a click on a header sorts by that column, the
	//    x on the far right deletes the row, the table scrolls,
	//  - confirm -> the game's own load sequence, exactly what clicking
	//    Continue on the loading screen does:
	//      LoadGame(helper) -> GameLoadingScreen (additive) ->
	//      Broadcast("StartSceneTransitionEvent") -> dissolve ->
	//      AutomaticSceneLoad -> LoadScene(LastOpenScene)
	//    (the sequence documented in docs/FINDINGS.md, "Phase 3 capture
	//    facts"; the dev autoload harness replays the same dance).
	//
	// After a snapshot load, Recorder.BlockGameSaves stays on for the rest
	// of the session: the game's own Saves/ (the player's original timeline)
	// must never be touched by a rewind; progress past the rewind point is
	// captured as new choice-point snapshots instead.
	public static class ChoiceWindow
	{
		// Layout/colour constants - parchment style of the game, values
		// chosen to be readable; polish is Phase 7.
		private static readonly Color ColDim = new Color(0f, 0f, 0f, 0.65f);
		private static readonly Color ColPanel = new Color(0.93f, 0.89f, 0.79f);
		private static readonly Color ColItem = new Color(0.84f, 0.79f, 0.68f);
		private static readonly Color ColItemAlt = new Color(0.88f, 0.84f, 0.74f);
		private static readonly Color ColButtonDark = new Color(0.30f, 0.26f, 0.20f, 0.95f);
		private static readonly Color ColLoadButton = new Color(0.55f, 0.30f, 0.25f);
		private static readonly Color ColCancelButton = new Color(0.55f, 0.53f, 0.48f);
		private static readonly Color ColDeleteButton = new Color(0.45f, 0.20f, 0.18f);
		private static readonly Color ColTextLight = new Color(0.95f, 0.92f, 0.82f);
		private static readonly Color ColTextDark = new Color(0.12f, 0.10f, 0.08f);

		private enum ListMode { Choices, Saved }

		private enum Pending { Load, Delete, SaveNow }

		// Table columns, left to right. Choice is the flexible one.
		private enum Col { Saved, Chapter, Hero, Age, Year, Season, Turn, Scene, Choice }

		private static readonly string[] ColNames =
		{ "Saved", "Chapter", "Hero", "Age", "Year", "Season", "Turn", "Scene", "Choice" };

		// Preferred widths in px; Choice takes whatever is left.
		private static readonly float[] ColWidths =
		{ 170f, 80f, 150f, 60f, 70f, 80f, 60f, 110f, 200f };

		private const float RowHeight = 44f;
		private const float HeaderHeight = 36f;
		private const float DeleteWidth = 52f;

		private sealed class SaveEntry
		{
			public string Dat;
			public string Title;
			public DateTime Saved;
			public int Chapter;
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

		private static GameObject _injectedButton;
		private static GameObject _canvas;
		private static ScrollRect _scrollRect;
		private static Transform _itemContainer;
		private static Transform _headerRow;
		private static TextMeshProUGUI[] _headerLabels;
		private static Image _tabChoicesImg;
		private static Image _tabSavedImg;
		private static GameObject _saveNowBtn;
		private static ListMode _mode = ListMode.Choices;
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
		private static int _lastItemCount;
		private static string _firstItemPath;
		private static string _firstItemTitle;

		// ------------------------------------------------------------------
		// Pause-window button
		// ------------------------------------------------------------------

		// Idempotent; called at host start and from the ShowPauseMenu
		// postfix. Safe to call while the pause window is inactive (children
		// of inactive objects can be created fine).
		public static void EnsureInjected()
		{
			try
			{
				if (_injectedButton != null)
				{
					return;
				}
				UIManager ui = UIManager.Initiate;
				if (ui == null || ui.PauseWindow == null)
				{
					// Not up yet - the ShowPauseMenu postfix retries.
					return;
				}
				GameObject pause = ui.PauseWindow;
				if (!pause.scene.IsValid())
				{
					ModLog.Error("ChoiceWindow: PauseWindow is a prefab asset, not a scene object ('"
						+ pause.name + "') - cannot inject");
					return;
				}
				DumpPauseLayout(pause);
				BuildButton(pause.transform);
				ModLog.Info("ChoiceWindow: button injected into pause window '" + pause.name + "'");
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: injection failed:\n" + e);
			}
		}

		// One-shot layout dump so placement decisions are based on what the
		// running game actually contains, not on decompiled field names.
		private static void DumpPauseLayout(GameObject pause)
		{
			WindowPause wp = pause.GetComponent<WindowPause>();
			ModLog.Info("ChoiceWindow: PauseWindow='" + pause.name + "' scene='" + pause.scene.name
				+ "' active=" + pause.activeSelf + " WindowPause=" + (wp != null)
				+ " children=" + pause.transform.childCount);
			for (int i = 0; i < pause.transform.childCount; i++)
			{
				Transform c = pause.transform.GetChild(i);
				RectTransform r = c as RectTransform;
				ModLog.Info("  child[" + i + "] '" + c.name + "' active=" + c.gameObject.activeSelf
					+ (r != null
						? (" anchor=" + r.anchorMin + ".." + r.anchorMax
							+ " pos=" + r.anchoredPosition + " size=" + r.sizeDelta)
						: ""));
			}
			// Canvas hierarchy the window draws in (for our sorting order).
			Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
			int shown = 0;
			for (int i = 0; i < canvases.Length && shown < 12; i++)
			{
				if (!canvases[i].gameObject.scene.IsValid())
				{
					continue;
				}
				ModLog.Info("  canvas '" + canvases[i].gameObject.name + "' order="
					+ canvases[i].sortingOrder + " mode=" + canvases[i].renderMode);
				shown++;
			}
		}

		private static void BuildButton(Transform parent)
		{
			_injectedButton = CreateUiObject("BranteLoadAny_ChoiceButton", parent);
			// Bottom centre of the pause window, 14px above its lower edge.
			// Adjusted after the first in-game layout dump if it collides.
			RectTransform rt = _injectedButton.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0.5f, 0f);
			rt.anchorMax = new Vector2(0.5f, 0f);
			rt.pivot = new Vector2(0.5f, 0f);
			rt.anchoredPosition = new Vector2(0f, 14f);
			rt.sizeDelta = new Vector2(340f, 46f);

			Image img = _injectedButton.AddComponent<Image>();
			img.color = ColButtonDark;
			Button btn = _injectedButton.AddComponent<Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(delegate { Open(); });

			TextMeshProUGUI label = AddLabel(_injectedButton.transform, "Label",
				"Load any choice", 21f, ColTextLight, true);
			Stretch(label.rectTransform);
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
			_mode = ListMode.Choices;
			UpdateTabs();
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
			if (_confirmPanel != null)
			{
				_confirmPanel.SetActive(false);
			}
			if (_canvas != null)
			{
				_canvas.SetActive(false);
			}
		}

		private static void BuildCanvas()
		{
			_canvas = CreateUiObject("BranteLoadAny_ChoiceWindow", null);
			// Our own overlay canvas: independent of the game's window
			// layout, destroyed only by us (survives scene transitions).
			UnityEngine.Object.DontDestroyOnLoad(_canvas);
			Canvas canvas = _canvas.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 8000; // above the game's UI (see dump log)
			CanvasScaler scaler = _canvas.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;
			_canvas.AddComponent<GraphicRaycaster>();
			_canvas.SetActive(false);

			// Dim backdrop: blocks clicks to the game; click = close.
			GameObject dim = CreateUiObject("Dim", _canvas.transform);
			Stretch(dim.GetComponent<RectTransform>());
			Image dimImg = dim.AddComponent<Image>();
			dimImg.color = ColDim;
			Button dimBtn = dim.AddComponent<Button>();
			dimBtn.targetGraphic = dimImg;
			dimBtn.transition = Selectable.Transition.None;
			dimBtn.onClick.AddListener(Close);

			// Panel.
			GameObject panel = CreateUiObject("Panel", _canvas.transform);
			RectTransform prt = panel.GetComponent<RectTransform>();
			prt.anchorMin = new Vector2(0.5f, 0.5f);
			prt.anchorMax = new Vector2(0.5f, 0.5f);
			prt.pivot = new Vector2(0.5f, 0.5f);
			prt.anchoredPosition = Vector2.zero;
			prt.sizeDelta = new Vector2(1120f, 760f);
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
			cimg.color = ColButtonDark;
			Button cbtn = closeBtn.AddComponent<Button>();
			cbtn.targetGraphic = cimg;
			cbtn.onClick.AddListener(Close);
			TextMeshProUGUI x = AddLabel(closeBtn.transform, "X", "\u00d7", 32f, ColTextLight, true);
			Stretch(x.rectTransform);

			_tabChoicesImg = BuildTab(panel.transform, "TabChoices", "Choice points", -146f, ListMode.Choices);
			_tabSavedImg = BuildTab(panel.transform, "TabSaved", "Saved games", 146f, ListMode.Saved);

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

			// Save now (saved-games tab only).
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
			nbtn.onClick.AddListener(delegate { Ask(Pending.SaveNow, "", ""); });
			TextMeshProUGUI nlabel = AddLabel(_saveNowBtn.transform, "Label",
				"Save now", 24f, ColTextLight, true);
			Stretch(nlabel.rectTransform);

			BuildConfirm(panel.transform);
			UpdateTabs();
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
		// the rows. Clicking a header sorts by that column.
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
			_headerRow = header.transform;

			_headerLabels = new TextMeshProUGUI[ColNames.Length];
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
				TextMeshProUGUI label = AddLabel(cell.transform, "Label", ColNames[i], 19f, ColTextLight, col != Col.Choice && col != Col.Hero && col != Col.Scene);
				label.overflowMode = TextOverflowModes.Ellipsis;
				label.enableWordWrapping = false;
				Stretch(label.rectTransform);
				label.rectTransform.offsetMin = new Vector2(8f, 0f);
				label.rectTransform.offsetMax = new Vector2(-8f, 0f);
				_headerLabels[i] = label;
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
			for (int i = 0; i < _headerLabels.Length; i++)
			{
				string text = ColNames[i];
				if ((Col)i == _sortCol)
				{
					text += _sortAsc ? " ^" : " v";
				}
				_headerLabels[i].text = text;
			}
		}

		private static Image BuildTab(Transform panel, string name, string text, float x, ListMode mode)
		{
			GameObject go = CreateUiObject(name, panel);
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0.5f, 1f);
			rt.anchorMax = new Vector2(0.5f, 1f);
			rt.pivot = new Vector2(0.5f, 1f);
			rt.anchoredPosition = new Vector2(x, -70f);
			rt.sizeDelta = new Vector2(260f, 44f);
			Image img = go.AddComponent<Image>();
			Button btn = go.AddComponent<Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(delegate { SetMode(mode); });
			TextMeshProUGUI label = AddLabel(go.transform, "Label", text, 22f, ColTextLight, true);
			Stretch(label.rectTransform);
			return img;
		}

		private static void SetMode(ListMode mode)
		{
			_mode = mode;
			CancelConfirm();
			UpdateTabs();
			_lastItemCount = RefreshItems();
			if (_scrollRect != null)
			{
				_scrollRect.verticalNormalizedPosition = 1f;
			}
			Canvas.ForceUpdateCanvases();
		}

		private static void UpdateTabs()
		{
			if (_tabChoicesImg == null || _tabSavedImg == null)
			{
				return;
			}
			_tabChoicesImg.color = _mode == ListMode.Choices ? ColLoadButton : ColCancelButton;
			_tabSavedImg.color = _mode == ListMode.Saved ? ColLoadButton : ColCancelButton;
			if (_saveNowBtn != null)
			{
				_saveNowBtn.SetActive(_mode == ListMode.Saved);
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
			// One button, several actions (see Ask).
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
		// save.
		private static void Ask(Pending action, string path, string title)
		{
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
			if (_confirmPanel != null)
			{
				_confirmPanel.SetActive(false);
			}
		}

		// ------------------------------------------------------------------
		// Table data
		// ------------------------------------------------------------------

		// Reloads the current tab's saves from disk, re-sorts and rebuilds
		// the rows. Returns the number of saves.
		private static int RefreshItems()
		{
			if (_itemContainer == null)
			{
				return 0;
			}
			string dir = _mode == ListMode.Saved ? Recorder.SlotDir : Recorder.SnapshotDir;
			_entries = ReadEntries(dir);
			SortEntries();
			UpdateHeader();
			BuildRows();
			return _entries.Count;
		}

		// Reads every save in the folder. The save file itself is the
		// source of truth for the table values (same deserialiser the load
		// uses); the sidecar only supplies the localised choice title.
		private static List<SaveEntry> ReadEntries(string dir)
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
					SaveEntry e = new SaveEntry();
					e.Dat = f;
					e.Title = ReadTitle(f);
					e.Saved = File.GetLastWriteTime(f);
					e.Chapter = h.Chapter;
					e.Hero = h.HeroName ?? "";
					e.Age = h.HeroAge;
					e.Year = h.Year;
					e.Season = h.Season;
					e.Turn = h.Turn;
					e.Scene = h.LastOpenScene ?? "";
					list.Add(e);
				}
				catch (Exception e)
				{
					ModLog.Error("ChoiceWindow: cannot read save " + f + ":\n" + e);
				}
			}
			return list;
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
				string text = _mode == ListMode.Saved
					? "No saved games yet. Use \"Save now\" below."
					: "No choice points recorded yet.";
				TextMeshProUGUI empty = AddLabel(_itemContainer, "Empty", text, 22f, ColTextDark, true);
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
			string p = e.Dat;
			string t = e.Choice;
			GameObject row = CreateUiObject("Row", _itemContainer);
			LayoutElement rle = row.AddComponent<LayoutElement>();
			rle.preferredHeight = RowHeight;
			Image img = row.AddComponent<Image>();
			img.color = index % 2 == 0 ? ColItem : ColItemAlt;
			Button btn = row.AddComponent<Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(delegate { Ask(Pending.Load, p, t); });
			HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
			hlg.padding = new RectOffset(6, 6, 6, 6);
			hlg.spacing = 0f;
			hlg.childControlWidth = true;
			hlg.childControlHeight = true;
			hlg.childForceExpandWidth = false;
			hlg.childForceExpandHeight = true;

			string[] values =
			{
				e.Saved.ToString("yyyy-MM-dd HH:mm"),
				e.Chapter.ToString(),
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
				bool centred = col != Col.Hero && col != Col.Scene && col != Col.Choice;
				TextMeshProUGUI cell = AddLabel(row.transform, "Cell", values[i], 19f, ColTextDark, centred);
				cell.overflowMode = TextOverflowModes.Ellipsis;
				cell.enableWordWrapping = false;
				cell.margin = new Vector4(6f, 0f, 6f, 0f);
				LayoutElement le = cell.gameObject.AddComponent<LayoutElement>();
				le.preferredWidth = ColWidths[i];
				if (col == Col.Choice)
				{
					le.flexibleWidth = 1f;
				}
			}

			// Delete x at the far right. As a child of the row it sits above
			// the row's own button, so a click reaches only this one.
			string dp = e.Dat;
			string dt = e.Choice;
			GameObject del = CreateUiObject("Delete", row.transform);
			LayoutElement dle = del.AddComponent<LayoutElement>();
			dle.preferredWidth = DeleteWidth;
			Image dimg = del.AddComponent<Image>();
			dimg.color = ColDeleteButton;
			Button dbtn = del.AddComponent<Button>();
			dbtn.targetGraphic = dimg;
			dbtn.onClick.AddListener(delegate { Ask(Pending.Delete, dp, dt); });
			TextMeshProUGUI dlabel = AddLabel(del.transform, "Label", "\u00d7", 30f, ColTextLight, true);
			Stretch(dlabel.rectTransform);

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
		// scene: opens the pause menu, injects the button, opens the list,
		// then simulates the clicks a player would make (row -> Load,
		// x -> Delete) through the same UnityEvent handlers the real
		// buttons use. Pauses between steps so a screenshot can catch each state.
		public static void DevTest()
		{
			try
			{
				ModLog.Info("DEV UITEST: opening pause menu");
				UIManager ui = UIManager.Initiate;
				if (ui == null)
				{
					ModLog.Error("DEV UITEST: no UIManager");
					return;
				}
				ui.ShowPauseMenu();
				EnsureInjected();
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
			// State A: pause window with the injected button.
			ModLog.Info("DEV UITEST: state A - pause menu with button (3s)");
			yield return new WaitForSeconds(3f);

			int n = Open();
			ModLog.Info("DEV UITEST: window open with " + n + " item(s); first='"
				+ (_firstItemTitle ?? "<none>") + "'");
			if (_firstItemPath == null)
			{
				ModLog.Error("DEV UITEST: nothing to load");
				yield break;
			}
			// State B: the choice table.
			ModLog.Info("DEV UITEST: state B - choice table (3s)");
			yield return new WaitForSeconds(3f);

			// Simulated click 1: the row (its onClick -> Ask(Load)).
			Ask(Pending.Load, _firstItemPath, _firstItemTitle);
			// State C: the confirm dialog (load).
			ModLog.Info("DEV UITEST: state C - confirm dialog (3s)");
			yield return new WaitForSeconds(3f);

			// State D: the delete confirm. With 2+ rows, exercise the real
			// x button end-to-end (the seeded duplicate goes first, so the
			// original test snapshot survives); with a single row just show
			// the dialog and cancel - the list must not end up empty.
			CancelConfirm();
			Ask(Pending.Delete, _firstItemPath, _firstItemTitle);
			ModLog.Info("DEV UITEST: state D - delete confirm (3s)");
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
			Ask(Pending.Load, _firstItemPath, _firstItemTitle);

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

		// ------------------------------------------------------------------
		// Small UI helpers
		// ------------------------------------------------------------------

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
