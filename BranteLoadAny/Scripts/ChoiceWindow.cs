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
	// Phase 4: the player-facing "load any choice" feature.
	//
	//  - a button injected into the game's own pause window (WindowPause,
	//    opened with Esc / UIManager.ShowPauseMenu),
	//  - a scrollable list of the recorded choice points (Recorder's
	//    snapshot dir, newest first, title + meta from the .meta.txt sidecar),
	//  - Phase 5: a second tab with the five manual save slots (Recorder's
	//    slot dir), same confirm dialog and same load flow,
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
		private static readonly Color ColButtonDark = new Color(0.30f, 0.26f, 0.20f, 0.95f);
		private static readonly Color ColLoadButton = new Color(0.55f, 0.30f, 0.25f);
		private static readonly Color ColCancelButton = new Color(0.55f, 0.53f, 0.48f);
		private static readonly Color ColDeleteButton = new Color(0.45f, 0.20f, 0.18f);
		private static readonly Color ColTextLight = new Color(0.95f, 0.92f, 0.82f);
		private static readonly Color ColTextDark = new Color(0.12f, 0.10f, 0.08f);

		private enum ListMode { Choices, Slots }

		private enum Pending { LoadChoice, DeleteChoice, SaveSlot, LoadSlot, DeleteSlot }

		private static GameObject _injectedButton;
		private static GameObject _canvas;
		private static Transform _itemContainer;
		private static Image _tabChoicesImg;
		private static Image _tabSlotsImg;
		private static ListMode _mode = ListMode.Choices;
		private static GameObject _confirmPanel;
		private static TextMeshProUGUI _confirmText;
		private static TextMeshProUGUI _confirmLoadLabel;
		private static Button _confirmLoadBtn;
		private static Pending _pendingAction;
		private static string _pendingPath;
		private static string _pendingTitle;
		private static int _pendingSlot;
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
		// The choice list window
		// ------------------------------------------------------------------

		// Returns the number of items shown (used by the dev UI test).
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
			_canvas.SetActive(true);
			// Build the layout and TMP meshes NOW: items are created with a
			// zero-width rect (the VerticalLayoutGroup sizes them on the
			// first canvas rebuild), and without this flush the item text
			// painted seconds late in practice (observed: row image visible,
			// its text only after a later SetActive forced a rebuild).
			Canvas.ForceUpdateCanvases();
			ModLog.Info("ChoiceWindow: opened (" + n + " snapshot(s))");
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
			_tabSlotsImg = BuildTab(panel.transform, "TabSlots", "Save slots", 146f, ListMode.Slots);
			UpdateTabs();

			// Scroll area (viewport + scroll rect + content).
			GameObject scroll = CreateUiObject("Scroll", panel.transform);
			RectTransform srt = scroll.GetComponent<RectTransform>();
			srt.anchorMin = new Vector2(0f, 0f);
			srt.anchorMax = new Vector2(1f, 1f);
			srt.offsetMin = new Vector2(22f, 86f);
			srt.offsetMax = new Vector2(-22f, -124f);
			ScrollRect scrollRect = scroll.AddComponent<ScrollRect>();
			scrollRect.horizontal = false;
			scrollRect.vertical = true;
			scrollRect.movementType = ScrollRect.MovementType.Clamped;
			scrollRect.scrollSensitivity = 30f;

			GameObject viewport = CreateUiObject("Viewport", scroll.transform);
			Stretch(viewport.GetComponent<RectTransform>());
			viewport.AddComponent<RectMask2D>();
			scrollRect.viewport = viewport.GetComponent<RectTransform>();

			GameObject content = CreateUiObject("Content", viewport.transform);
			RectTransform cont = content.GetComponent<RectTransform>();
			cont.anchorMin = new Vector2(0f, 1f);
			cont.anchorMax = new Vector2(1f, 1f);
			cont.pivot = new Vector2(0.5f, 1f);
			cont.anchoredPosition = Vector2.zero;
			cont.sizeDelta = Vector2.zero;
			VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
			vlg.padding = new RectOffset(8, 8, 8, 8);
			vlg.spacing = 8f;
			vlg.childAlignment = TextAnchor.UpperCenter;
			vlg.childControlWidth = true;
			vlg.childControlHeight = false;
			vlg.childForceExpandWidth = true;
			vlg.childForceExpandHeight = false;
			ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
			fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			scrollRect.content = cont;
			_itemContainer = content.transform;

			BuildConfirm(panel.transform);
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
			Canvas.ForceUpdateCanvases();
		}

		private static void UpdateTabs()
		{
			if (_tabChoicesImg == null || _tabSlotsImg == null)
			{
				return;
			}
			_tabChoicesImg.color = _mode == ListMode.Choices ? ColLoadButton : ColCancelButton;
			_tabSlotsImg.color = _mode == ListMode.Slots ? ColLoadButton : ColCancelButton;
		}

		// Confirm dialog: covers the panel (blocks item clicks), shows the
		// pending choice point's title, Load / Cancel.
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
			// without ellipsizing (see the delete dialog's long note).
			brt.sizeDelta = new Vector2(640f, 380f);
			Image bimg = box.AddComponent<Image>();
			bimg.color = new Color(0.96f, 0.94f, 0.88f);

			_confirmText = AddLabel(box.transform, "Msg",
				"Load this choice point?", 24f, ColTextDark, false);
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
		// entry; slot is the slot number for the Slot actions.
		private static void Ask(Pending action, string path, string title, int slot)
		{
			_pendingAction = action;
			_pendingPath = path;
			_pendingTitle = title;
			_pendingSlot = slot;
			string body;
			string label;
			switch (action)
			{
				case Pending.LoadChoice:
					body = "Load this choice point?\n\n" + title
						+ "\n\nYour original save will not be modified.";
					label = "Load";
					break;
				case Pending.DeleteChoice:
					body = "Remove this choice point from the list?\n\n" + title
						+ "\n\nThe snapshot file is deleted. Your game saves are not touched.";
					label = "Delete";
					break;
				case Pending.SaveSlot:
					body = "Save the current game into Slot " + slot + "?\n\n"
						+ "Anything already in this slot is replaced. Your game saves are not touched.";
					label = "Save";
					break;
				case Pending.LoadSlot:
					body = "Load Slot " + slot + "?\n\n" + title
						+ "\n\nYour original save will not be modified.";
					label = "Load";
					break;
				default:
					body = "Remove Slot " + slot + " from the list?\n\n"
						+ "The slot's save file is deleted. Your game saves are not touched.";
					label = "Delete";
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
			ModLog.Info("ChoiceWindow: " + action + " requested for " + Path.GetFileName(path));
		}

		private static void RunPending()
		{
			switch (_pendingAction)
			{
				case Pending.LoadChoice:
				case Pending.LoadSlot:
					LoadSnapshot(_pendingPath);
					break;
				case Pending.DeleteChoice:
				case Pending.DeleteSlot:
					DeleteSnapshot(_pendingPath);
					break;
				case Pending.SaveSlot:
					SaveToSlot(_pendingSlot);
					break;
			}
		}

		private static void SaveToSlot(int slot)
		{
			CancelConfirm();
			try
			{
				Recorder.SaveSlot(slot);
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: saving slot " + slot + " failed:\n" + e);
			}
			_lastItemCount = RefreshItems();
			Canvas.ForceUpdateCanvases();
		}

		// Removes a snapshot or slot file + sidecar and refreshes the list.
		// Only ever touches the mod's own folders.
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
				ModLog.Info("ChoiceWindow: removed snapshot " + Path.GetFileName(datPath));
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: deleting " + datPath + " failed:\n" + e);
			}
			CancelConfirm();
			_lastItemCount = RefreshItems();
			Canvas.ForceUpdateCanvases();
			ModLog.Info("ChoiceWindow: " + _lastItemCount + " snapshot(s) remain");
		}

		private static void CancelConfirm()
		{
			if (_confirmPanel != null)
			{
				_confirmPanel.SetActive(false);
			}
		}

		// ------------------------------------------------------------------
		// Snapshot list
		// ------------------------------------------------------------------

		// (Re)builds the item list from the snapshot dir, newest first.
		// Returns the item count.
		private static int RefreshItems()
		{
			if (_itemContainer == null)
			{
				return 0;
			}
			for (int i = _itemContainer.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.Destroy(_itemContainer.GetChild(i).gameObject);
			}
			_firstItemPath = null;
			_firstItemTitle = null;

			if (_mode == ListMode.Slots)
			{
				return RefreshSlots();
			}

			string dir = Recorder.SnapshotDir;
			List<string> files = new List<string>();
			try
			{
				if (Directory.Exists(dir))
				{
					files.AddRange(Directory.GetFiles(dir, "*.dat"));
				}
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: cannot list snapshots:\n" + e);
			}
			files.Sort(delegate (string a, string b)
			{
				return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
			});

			if (files.Count == 0)
			{
				TextMeshProUGUI empty = AddLabel(_itemContainer, "Empty",
					"No choice points recorded yet.", 22f, ColTextDark, false);
				// Stretch width from the top (default centre anchors would
				// give a zero-width rect, see CreateItem).
				RectTransform ert = empty.rectTransform;
				ert.anchorMin = new Vector2(0f, 1f);
				ert.anchorMax = new Vector2(1f, 1f);
				ert.pivot = new Vector2(0.5f, 1f);
				ert.sizeDelta = new Vector2(0f, 60f);
				return 0;
			}
			foreach (string f in files)
			{
				string title;
				string meta;
				ReadMeta(f, out title, out meta);
				CreateItem(f, title, title + "\n" + meta);
			}
			return files.Count;
		}

		// Lists the manual save slots: one row per slot, empty ones too.
		// Returns the number of occupied slots.
		private static int RefreshSlots()
		{
			int used = 0;
			for (int i = 1; i <= Recorder.SlotCount; i++)
			{
				string path = Recorder.SlotPath(i);
				if (!File.Exists(path))
				{
					CreateEmptySlotRow(i, path);
					continue;
				}
				string title;
				string meta;
				ReadMeta(path, out title, out meta);
				CreateSlotRow(i, path, title, "Slot " + i + " \u00b7 " + title + "\n" + meta);
				used++;
			}
			return used;
		}

		// Parses the Recorder's .meta.txt sidecar for display purposes.
		private static void ReadMeta(string datPath, out string title, out string meta)
		{
			title = Path.GetFileNameWithoutExtension(datPath);
			meta = "";
			string sidecar = Path.Combine(Path.GetDirectoryName(datPath),
				Path.GetFileNameWithoutExtension(datPath) + ".meta.txt");
			try
			{
				if (!File.Exists(sidecar))
				{
					return;
				}
				string scene = "";
				string recorded = "";
				string hero = "";
				string chapter = "";
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
						title = v.Trim();
					}
					else if (line.StartsWith("scene=", StringComparison.Ordinal))
					{
						scene = line.Substring(6);
					}
					else if (line.StartsWith("recorded=", StringComparison.Ordinal))
					{
						recorded = line.Substring(9);
					}
					else if (line.StartsWith("hero=", StringComparison.Ordinal))
					{
						hero = line.Substring(5).Replace("  ", " \u00b7 ");
					}
					else if (line.StartsWith("chapter=", StringComparison.Ordinal))
					{
						string v = line.Substring(8);
						int paren = v.IndexOf(" (", StringComparison.Ordinal);
						chapter = (paren > 0 ? "Ch." + v.Substring(0, paren) : v);
					}
				}
				List<string> parts = new List<string>();
				if (chapter.Length > 0)
				{
					parts.Add(chapter);
				}
				if (hero.Length > 0)
				{
					parts.Add(hero);
				}
				if (recorded.Length > 0)
				{
					parts.Add(recorded);
				}
				if (parts.Count == 0 && scene.Length > 0)
				{
					parts.Add(scene);
				}
				meta = string.Join(" \u00b7 ", parts.ToArray());
			}
			catch (Exception e)
			{
				ModLog.Error("ChoiceWindow: meta read failed for " + datPath + ":\n" + e);
			}
		}

		private static void CreateItem(string path, string title, string display)
		{
			string p = path;
			string t = title;
			GameObject item = CreateRow(display, delegate { Ask(Pending.LoadChoice, p, t, 0); }, -80f);
			AddSideButton(item.transform, -8f, 60f, "\u00d7", 30f, ColDeleteButton,
				delegate { Ask(Pending.DeleteChoice, p, t, 0); });

			if (_firstItemPath == null)
			{
				_firstItemPath = path;
				_firstItemTitle = title;
			}
		}

		private static void CreateSlotRow(int slot, string path, string title, string display)
		{
			int s = slot;
			string p = path;
			string t = title;
			GameObject item = CreateRow(display, delegate { Ask(Pending.LoadSlot, p, t, s); }, -150f);
			AddSideButton(item.transform, -76f, 70f, "Save", 20f, ColLoadButton,
				delegate { Ask(Pending.SaveSlot, p, t, s); });
			AddSideButton(item.transform, -8f, 60f, "\u00d7", 30f, ColDeleteButton,
				delegate { Ask(Pending.DeleteSlot, p, t, s); });
		}

		private static void CreateEmptySlotRow(int slot, string path)
		{
			int s = slot;
			string p = path;
			CreateRow("Slot " + slot + " \u00b7 empty\nClick to save the current game here",
				delegate { Ask(Pending.SaveSlot, p, "", s); }, -80f);
		}

		// A clickable list row. textRight is how far the text stops short of
		// the row's right edge (room for the side buttons).
		private static GameObject CreateRow(string display, UnityEngine.Events.UnityAction onClick, float textRight)
		{
			GameObject item = CreateUiObject("Item", _itemContainer);
			item.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 84f);
			Image img = item.AddComponent<Image>();
			img.color = ColItem;
			Button btn = item.AddComponent<Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(onClick);

			TextMeshProUGUI label = AddLabel(item.transform, "Text", display, 20f, ColTextDark, false);
			label.alignment = TextAlignmentOptions.Center;
			label.enableWordWrapping = true;
			label.overflowMode = TextOverflowModes.Ellipsis;
			// Stretch FIRST: offsets are relative to the anchors, and with the
			// default centre anchors offsetMin/Max would collapse the rect to
			// nothing (observed: item row rendered, text invisible).
			RectTransform lrt = label.rectTransform;
			lrt.anchorMin = Vector2.zero;
			lrt.anchorMax = Vector2.one;
			lrt.offsetMin = new Vector2(16f, 6f);
			lrt.offsetMax = new Vector2(textRight, -6f);
			return item;
		}

		// A small button at the row's right edge. As a child of the row it
		// renders (and raycasts) on top, so a click on it reaches only this
		// button, never the row's own handler.
		private static void AddSideButton(Transform row, float right, float width, string text,
			float fontSize, Color color, UnityEngine.Events.UnityAction onClick)
		{
			GameObject go = CreateUiObject("Side", row);
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(1f, 0f);
			rt.anchorMax = new Vector2(1f, 1f);
			rt.pivot = new Vector2(1f, 0.5f);
			rt.anchoredPosition = new Vector2(right, 0f);
			rt.sizeDelta = new Vector2(width, -14f);
			Image img = go.AddComponent<Image>();
			img.color = color;
			Button btn = go.AddComponent<Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(onClick);
			TextMeshProUGUI label = AddLabel(go.transform, "Label", text, fontSize, ColTextLight, true);
			Stretch(label.rectTransform);
		}

		// ------------------------------------------------------------------
		// Load flow (the vanilla sequence, see class comment)
		// ------------------------------------------------------------------

		public static void LoadSnapshot(string datPath)
		{
			if (string.IsNullOrEmpty(datPath))
			{
				ModLog.Error("ChoiceWindow: no snapshot selected");
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
			// Read the snapshot first: if it fails, the window stays open
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
				ModLog.Error("ChoiceWindow: cannot read snapshot " + datPath + ":\n" + e);
				yield break;
			}

			ModLog.Info("ChoiceWindow: loading snapshot " + Path.GetFileName(datPath)
				+ " (scene=" + helper.LastOpenScene + ")");
			// From here on the game's own Saves/ stays frozen for the rest of
			// the session: a rewind must never overwrite the original
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
		// then simulates the two clicks a player would make (item -> Load)
		// through the same UnityEvent handlers the real buttons use.
		// Pauses between steps so a screenshotper can catch each state.
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
			// State B: the choice list.
			ModLog.Info("DEV UITEST: state B - choice list (3s)");
			yield return new WaitForSeconds(3f);

			// Simulated click 1: the item (its onClick -> Ask(LoadChoice)).
			Ask(Pending.LoadChoice, _firstItemPath, _firstItemTitle, 0);
			// State C: the confirm dialog (load).
			ModLog.Info("DEV UITEST: state C - confirm dialog (3s)");
			yield return new WaitForSeconds(3f);

			// State D: the delete confirm. With 2+ items, exercise the real
			// Delete button end-to-end (the seeded duplicate goes first, so
			// the original test snapshot survives); with a single item just
			// show the dialog and cancel - the list must not end up empty.
			CancelConfirm();
			Ask(Pending.DeleteChoice, _firstItemPath, _firstItemTitle, 0);
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
			Ask(Pending.LoadChoice, _firstItemPath, _firstItemTitle, 0);

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
