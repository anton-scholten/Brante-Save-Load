using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using I2.Loc;
using Assets._Scripts.AMVCC.Core.SceneStateMachine;
using _Scripts.AMVCC.Controllers;
using _Scripts.AMVCC.Models.Static;
using _Scripts.AMVCC.Views;
using _Scripts.AMVCC.Views.Windows.Destiny;
using _Scripts.Helpers;
using _Scripts.Managers;

namespace Doorstop
{
	// Records a snapshot of the full game state every time a choice scene
	// appears (Phase 3). The snapshot is a SaveLoadHelper serialised exactly
	// the way the game serialises its own saves (BinaryFormatter), so the
	// game's own LoadGame(SaveLoadHelper) can restore it unchanged in Phase 4.
	//
	// Deliberate differences from the game's SaveData():
	//  - no file I/O into the game's Saves/ folder (structural isolation:
	//    we only ever write into <persistentDataPath>/BranteLoadAny/)
	//  - SlotInfo kept empty and stable so identical states produce
	//    byte-identical blobs, which makes dedup exact
	public static class Recorder
	{
		private static bool _armed;

		// Set by the dev autoload harness: while true, the game's own
		// SaveGame/SaveChapterState are Harmony-blocked (see Entrypoint)
		// so the harness can never write into the game's Saves/ folder.
		public static bool DevBlockGameSaves;

		public static string SnapshotDir
		{
			get
			{
				return Path.Combine(Application.persistentDataPath, "BranteLoadAny", "Snapshots");
			}
		}

		// Called from ModHost.Start (engine fully up). Proves the plumbing
		// (state copy + BinaryFormatter round-trip) before the first real
		// capture; at the main menu some managers may not exist yet, which
		// is fine - capture only ever happens in-game.
		public static void Arm()
		{
			if (_armed)
			{
				return;
			}
			_armed = true;
			ModLog.Info("Recorder armed; snapshot dir " + SnapshotDir);
			try
			{
				Directory.CreateDirectory(SnapshotDir);
				SaveLoadHelper probe = BuildHelper();
				byte[] blob = Serialize(probe);
				SaveLoadHelper back = (SaveLoadHelper)Deserialize(blob);
				ModLog.Info("Round-trip self-test OK: " + blob.Length + " bytes, hero '"
					+ back.HeroName + "', scene '" + back.LastOpenScene + "', turn " + back.Turn);
			}
			catch (Exception e)
			{
				ModLog.Info("Self-test deferred (expected at the main menu): " + e.Message);
			}
		}

		// Harmony postfix on SceneController.Awake: a choice scene just
		// appeared. The state is final at this moment - the chosen answer's
		// effects are only applied when a button is clicked - and
		// ShowedScenesList still has this scene as NOT IsShowed (that is
		// flipped later by SceneShowChecker.OnEnable). The not-yet-showed
		// flag is exactly what makes the game's loader re-open this very
		// scene instead of continuing past it.
		public static void OnChoiceSceneShown()
		{
			try
			{
				Capture();
			}
			catch (Exception e)
			{
				ModLog.Error("Choice-point capture failed:\n" + e);
			}
		}

		private static void Capture()
		{
			SaveLoadHelper helper = BuildHelper();
			byte[] blob = Serialize(helper);
			string scene = GameManager.Instance.OpenedSceneName;
			if (string.IsNullOrEmpty(scene))
			{
				scene = "(unknown)";
			}
			string hash = Sha1(blob).Substring(0, 10);
			string baseName = Sanitize(scene) + "_t" + helper.Turn + "_" + hash;
			string dat = Path.Combine(SnapshotDir, baseName + ".dat");
			if (File.Exists(dat))
			{
				ModLog.Info("Choice point unchanged, skipped: " + baseName);
				// Idempotent: fill in choice texts if a previous run did not.
				TryFillChoices(baseName);
				return;
			}
			Directory.CreateDirectory(SnapshotDir);
			File.WriteAllBytes(dat, blob);
			WriteSidecar(baseName, scene, helper);
			TryFillChoices(baseName);
			ModLog.Info("Choice point recorded: " + baseName + " (" + blob.Length + " bytes)");
		}

		// Mirrors SaveLoadManager.SaveData(int, int, SceneStates) field by
		// field, minus every bit of file I/O. Names below match the game's
		// own code (including its "CurrentConsequene" typo).
		private static SaveLoadHelper BuildHelper()
		{
			// The Instance/Initiate statics can point at objects that died
			// with their scene (Unity fake-null), so fall back to a fresh
			// scene search - the state managers are alive whenever a choice
			// scene is on screen, whichever scene hosts them.
			GameManager gm = GameManager.Instance;
			if (gm == null) { gm = UnityEngine.Object.FindObjectOfType<GameManager>(); }
			ParametersManager pm = ParametersManager.Instance;
			if (pm == null) { pm = UnityEngine.Object.FindObjectOfType<ParametersManager>(); }
			KeyChapterParametersController kpc = KeyChapterParametersController.Initiate;
			if (kpc == null) { kpc = UnityEngine.Object.FindObjectOfType<KeyChapterParametersController>(); }
			CharacterParametersSerializeHelper csh = CharacterParametersSerializeHelper.Initiate;
			if (csh == null) { csh = UnityEngine.Object.FindObjectOfType<CharacterParametersSerializeHelper>(); }
			SaveLoadManager slm = SaveLoadManager.Instance;
			if (slm == null) { slm = UnityEngine.Object.FindObjectOfType<SaveLoadManager>(); }
			if (gm == null) throw new InvalidOperationException("GameManager not alive");
			if (pm == null) throw new InvalidOperationException("ParametersManager not alive");
			if (kpc == null) throw new InvalidOperationException("KeyChapterParametersController not alive");
			if (csh == null) throw new InvalidOperationException("CharacterParametersSerializeHelper not alive");

			SaveLoadHelper helper = new SaveLoadHelper();
			helper.SlotInfo = new SaveSlotInfo { Id = "", Date = "" };
			helper.ModelParameters = pm.Parameters;
			helper.CharacterModel = csh.Characters;
			helper.ChapterParameters = kpc.AllKeyParameters;
			helper.SceneState = SceneStates.SceneState_SHOW_ANIMATION;
			helper.GodMode = gm.GodMode;
			helper.CurrentConsequence = gm.CurrentConsequene;
			helper.LastOpenScene = gm.OpenedSceneName;
			helper.HeroName = pm.HeroName;
			helper.Year = gm.GameManagerSerializer.CurrentYear;
			helper.HeroAge = pm.Age;
			helper.PreviousYear = gm.GameManagerSerializer.PreviousYear;
			helper.Turn = gm.Turn;
			helper.InsurrectionDayStage = gm.InsurrectionDayStage;
			helper.InsurrectionPreviousDayStage = gm.InsurrectionPreviousDayStage;
			helper.Season = gm.Season;
			helper.Chapter = gm.Chapter;
			helper.StoryMode = gm.StoryMode;
			helper.IronManMode = gm.IronManMode;
			helper.ShowedScenesList = gm.ShowedScenesList;
			helper.SceneInSequence = gm.SceneInSequence;
			string chapterName;
			if (slm == null || !slm.ChapterName.TryGetValue(gm.Chapter, out chapterName))
			{
				// SaveLoadManager can die with its scene; its ChapterName
				// table is a constant, so fall back to a verbatim copy.
				chapterName = FallbackChapterName(gm.Chapter);
			}
			helper.ChapterName = chapterName;

			helper.AllGameObjectives = new GameObjectives();
			helper.AllGameObjectives.Objectives = new List<ObjectiveStatuses>();
			foreach (Objective o in kpc.AllGameObjectives)
			{
				helper.AllGameObjectives.Objectives.Add(new ObjectiveStatuses
				{
					Name = o.Name,
					Status = o.Unlocked,
					Chapter = o.Chapter,
					UnlockDate = o.UnlockInfo.UnlockDate,
					UnlockYear = o.UnlockInfo.UnlockYear
				});
			}
			helper.AllGameEvents = new GameEvents();
			helper.AllGameEvents.GameEventsList = new List<GameEventStatus>();
			foreach (GameEvent ev in kpc.AllGameEvents)
			{
				helper.AllGameEvents.GameEventsList.Add(new GameEventStatus
				{
					Name = ev.name,
					Unlocked = ev.Unlocked,
					Chapter = ev.Chapter,
					UnlockDate = ev.Info.UnlockDate,
					UnlockYear = ev.Info.UnlockYear
				});
			}
			return helper;
		}

		// Verbatim copy of SaveLoadManager.ChapterName's default table,
		// used only when the manager object itself is not around.
		private static string FallbackChapterName(int chapter)
		{
			switch (chapter)
			{
				case 0: return "Chapter0.Childhood";
				case 1: return "Chapter1.Childhood";
				case 2: return "Chapter2.Teen";
				case 3: return "Chapter3.Youth";
				case 4: return "Chapter4.Peace";
				case 5: return "Chapter5.War";
				default: return "";
			}
		}

		private static byte[] Serialize(SaveLoadHelper helper)
		{
			using (MemoryStream ms = new MemoryStream())
			{
				new BinaryFormatter().Serialize(ms, helper);
				return ms.ToArray();
			}
		}

		private static object Deserialize(byte[] blob)
		{
			using (MemoryStream ms = new MemoryStream(blob))
			{
				return new BinaryFormatter().Deserialize(ms);
			}
		}

		// The answer buttons are usually serialized in the scene file as
		// INACTIVE objects (the show animation switches them on), so read
		// them regardless of activity; only fall back to waiting for the
		// animation if they do not exist yet at all.
		private static void TryFillChoices(string baseName)
		{
			ParameterButtonChanger[] buttons = CollectButtons();
			if (buttons.Length > 0)
			{
				try
				{
					FillChoicesNow(baseName, buttons);
				}
				catch (Exception e)
				{
					ModLog.Error("Filling choice texts failed:\n" + e);
				}
			}
			else if (ModHost.Current != null)
			{
				ModHost.Current.StartCoroutine(FillChoicesWhenVisible(baseName));
			}
		}

		private static ParameterButtonChanger[] CollectButtons()
		{
			ParameterButtonChanger[] active =
				UnityEngine.Object.FindObjectsOfType<ParameterButtonChanger>();
			if (active.Length > 0)
			{
				return active;
			}
			// FindObjectsOfType only sees ACTIVE objects; the buttons may be
			// inactive scene objects at this moment. Their texts are plain
			// serialized fields, so they are readable either way. The scene
			// filter drops prefab assets that FindObjectsOfTypeAll also
			// returns.
			List<ParameterButtonChanger> inScenes = new List<ParameterButtonChanger>();
			foreach (ParameterButtonChanger b in
				Resources.FindObjectsOfTypeAll<ParameterButtonChanger>())
			{
				if (b != null && b.gameObject.scene.IsValid())
				{
					inScenes.Add(b);
				}
			}
			ModLog.Info("Buttons: 0 active, " + inScenes.Count + " inactive in loaded scenes");
			return inScenes.ToArray();
		}

		private static void WriteSidecar(string baseName, string scene, SaveLoadHelper helper)
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("scene=" + scene);
			sb.AppendLine("recorded=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			sb.AppendLine("hero=" + helper.HeroName
				+ "  age=" + helper.HeroAge
				+ "  year=" + helper.Year
				+ "  turn=" + helper.Turn
				+ "  season=" + helper.Season);
			sb.AppendLine("chapter=" + helper.Chapter + " (" + helper.ChapterName + ")");
			SceneController sc = UnityEngine.Object.FindObjectOfType<SceneController>();
			if (sc != null)
			{
				sb.AppendLine("title=" + T(sc.TitleKey) + "   [key: " + sc.TitleKey + "]");
			}
			sb.AppendLine("choices:");
			ParameterButtonChanger[] buttons =
				UnityEngine.Object.FindObjectsOfType<ParameterButtonChanger>();
			Array.Sort(buttons, (a, b) => a.ButtonIndex.CompareTo(b.ButtonIndex));
			foreach (ParameterButtonChanger b in buttons)
			{
				sb.AppendLine("  [" + b.ButtonIndex + "] " + T(b.ButtonTextKey));
			}
			File.WriteAllText(Path.Combine(SnapshotDir, baseName + ".meta.txt"), sb.ToString());
		}

		// Waits until the choice buttons are actually on screen, then appends
		// their texts to the sidecar written at capture time. Runs as a
		// coroutine on the host (see Capture).
		private static System.Collections.IEnumerator FillChoicesWhenVisible(string baseName)
		{
			for (int i = 0; i < 20; i++)
			{
				yield return new WaitForSeconds(0.5f);
				ParameterButtonChanger[] buttons =
					UnityEngine.Object.FindObjectsOfType<ParameterButtonChanger>();
				if (buttons.Length == 0)
				{
					continue;
				}
				try
				{
					FillChoicesNow(baseName, buttons);
				}
				catch (Exception e)
				{
					ModLog.Error("Filling choice texts failed:\n" + e);
				}
				yield break;
			}
			ModLog.Info("No choice buttons ever appeared for " + baseName
				+ " (0 active, " + InactiveButtonCount() + " inactive in loaded scenes)");
		}

		private static int InactiveButtonCount()
		{
			int n = 0;
			foreach (ParameterButtonChanger b in
				Resources.FindObjectsOfTypeAll<ParameterButtonChanger>())
			{
				if (b != null && b.gameObject.scene.IsValid())
				{
					n++;
				}
			}
			return n;
		}

		private static void FillChoicesNow(string baseName, ParameterButtonChanger[] buttons)
		{
			Array.Sort(buttons, (a, b) => a.ButtonIndex.CompareTo(b.ButtonIndex));
			StringBuilder sb = new StringBuilder();
			foreach (ParameterButtonChanger b in buttons)
			{
				sb.AppendLine("  [" + b.ButtonIndex + "] " + T(b.ButtonTextKey));
			}
			string path = Path.Combine(SnapshotDir, baseName + ".meta.txt");
			string text = File.ReadAllText(path);
			int cut = text.IndexOf("choices:", StringComparison.Ordinal);
			if (cut >= 0)
			{
				File.WriteAllText(path,
					text.Substring(0, cut + "choices:".Length) + "\n" + sb);
				ModLog.Info("Choice texts added for " + baseName + " ("
					+ buttons.Length + " button(s))");
			}
		}

		// I2 localisation lookup with the raw key as fallback (the game does
		// the same thing elsewhere).
		private static string T(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				return "";
			}
			try
			{
				string s = LocalizationManager.GetTranslation(key);
				return string.IsNullOrEmpty(s) ? key : s;
			}
			catch
			{
				return key;
			}
		}

		private static string Sha1(byte[] data)
		{
			using (SHA1 sha = SHA1.Create())
			{
				byte[] h = sha.ComputeHash(data);
				StringBuilder sb = new StringBuilder(h.Length * 2);
				foreach (byte b in h)
				{
					sb.Append(b.ToString("x2"));
				}
				return sb.ToString();
			}
		}

		private static string Sanitize(string name)
		{
			StringBuilder sb = new StringBuilder(name.Length);
			foreach (char c in name)
			{
				sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
			}
			return sb.ToString();
		}
	}
}
