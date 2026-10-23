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

		private static GameObject _injectedButton;
		private static GameObject _canvas;
		private static Transform _itemContainer;
		private static GameObject _confirmPanel;
		private static TextMeshProUGUI _confirmText;
		private static TextMeshProUGUI _confirmLoadLabel;
		private static Button _confirmLoadBtn;
		private static string _pendingPath;
		private static string _pendingTitle;
		private static bool _pendingIsDelete;
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

			// Scroll area (viewport + scroll rect + content).
			GameObject scroll = CreateUiObject("Scroll", panel.transform);
			RectTransform srt = scroll.GetComponent<RectTransform>();
			srt.anchorMin = new Vector2(0f, 0f);
			srt.anchorMax = new Vector2(1f, 1f);
			srt.offsetMin = new Vector2(22f, 86f);
			srt.offsetMax = new Vector2(-22f, -84f);
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
			// One button, two modes (see AskConfirm / AskConfirmDelete).
			_confirmLoadBtn.onClick.AddListener(delegate
			{
				if (_pendingIsDelete)
				{
					DeleteSnapshot(_pendingPath);
				}
				else
				{
					LoadSnapshot(_pendingPath);
				}
			});
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

		private static void AskConfirm(string path, string title)
		{
			_pendingPath = path;
			_pendingTitle = title;
			_pendingIsDelete = false;
			if (_confirmText != null)
			{
				_confirmText.text = "Load this choice point?\n\n" + title
					+ "\n\nYour original save will not be modified.";
			}
			if (_confirmLoadLabel != null)
			{
				_confirmLoadLabel.text = "Load";
			}
			if (_confirmPanel != null)
			{
				_confirmPanel.SetActive(true);
			}
			// Paint the new text immediately (see Open's flush note).
			Canvas.ForceUpdateCanvases();
			ModLog.Info("ChoiceWindow: confirm requested for " + Path.GetFileName(path));
		}

		// Same dialog, delete mode: removes the snapshot file + sidecar and
		// refreshes the list. Only ever touches the mod's own folder.
		private static void AskConfirmDelete(string path, string title)
		{
			_pendingPath = path;
			_pendingTitle = title;
			_pendingIsDelete = true;
			if (_confirmText != null)
			{
				_confirmText.text = "Remove this choice point from the list?\n\n" + title
					+ "\n\nThe snapshot file is deleted. Your game saves are not touched.";
			}
			if (_confirmLoadLabel != null)
			{
				_confirmLoadLabel.text = "Delete";
			}
			if (_confirmPanel != null)
			{
				_confirmPanel.SetActive(true);
			}
			// Paint the new text immediately (see Open's flush note).
			Canvas.ForceUpdateCanvases();
			ModLog.Info("ChoiceWindow: delete confirm requested for " + Path.GetFileName(path));
		}

		private static void DeleteSnapshot(string datPath)
		{
			try
			{
				File.Delete(datPath);
				string sidecar = Path.Combine(Recorder.SnapshotDir,
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

		// Parses the Recorder's .meta.txt sidecar for display purposes.
		private static void ReadMeta(string datPath, out string title, out string meta)
		{
			title = Path.GetFileNameWithoutExtension(datPath);
			meta = "";
			string sidecar = Path.Combine(Recorder.SnapshotDir,
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
			GameObject item = CreateUiObject("Item", _itemContainer);
			item.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 84f);
			Image img = item.AddComponent<Image>();
			img.color = ColItem;
			Button btn = item.AddComponent<Button>();
			btn.targetGraphic = img;
			string p = path;
			string t = title;
			btn.onClick.AddListener(delegate { AskConfirm(p, t); });

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
			lrt.offsetMax = new Vector2(-80f, -6f); // leave room for the [x]

			// Delete [x] at the row's right edge. As a child of the row it
			// renders (and raycasts) on top, so a click on it reaches only
			// this button, never the row's Load handler.
			GameObject del = CreateUiObject("Delete", item.transform);
			RectTransform drt = del.GetComponent<RectTransform>();
			drt.anchorMin = new Vector2(1f, 0f);
			drt.anchorMax = new Vector2(1f, 1f);
			drt.pivot = new Vector2(1f, 0.5f);
			drt.anchoredPosition = new Vector2(-8f, 0f);
			drt.sizeDelta = new Vector2(60f, -14f);
			Image dimg = del.AddComponent<Image>();
			dimg.color = ColDeleteButton;
			Button dbtn = del.AddComponent<Button>();
			dbtn.targetGraphic = dimg;
			string dp = path;
			string dt2 = title;
			dbtn.onClick.AddListener(delegate { AskConfirmDelete(dp, dt2); });
			TextMeshProUGUI dlabel = AddLabel(del.transform, "Label", "\u00d7", 30f, ColTextLight, true);
			Stretch(dlabel.rectTransform);

			if (_firstItemPath == null)
			{
				_firstItemPath = path;
				_firstItemTitle = title;
			}
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

			// Simulated click 1: the item (its onClick -> AskConfirm).
			AskConfirm(_firstItemPath, _firstItemTitle);
			// State C: the confirm dialog (load).
			ModLog.Info("DEV UITEST: state C - confirm dialog (3s)");
			yield return new WaitForSeconds(3f);

			// State D: the delete confirm. With 2+ items, exercise the real
			// Delete button end-to-end (the seeded duplicate goes first, so
			// the original test snapshot survives); with a single item just
			// show the dialog and cancel - the list must not end up empty.
			CancelConfirm();
			AskConfirmDelete(_firstItemPath, _firstItemTitle);
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
			AskConfirm(_firstItemPath, _firstItemTitle);

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
