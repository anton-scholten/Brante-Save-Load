using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using _Scripts.AMVCC.Views;
using _Scripts.Helpers;
using _Scripts.Managers;

namespace Doorstop
{
	// Plays the game by itself: pages through a scene's text, answers, and
	// presses whatever "continue" the game puts up next, until a goal is met.
	// Used to replay a path the player built in the tree view, and (dev) to
	// play a run forward to make debug saves.
	public static class AutoPlay
	{
		// Picks the answer for a scene: the button name ("ButtonDescription2")
		// or null for the first one the hero can take.
		public delegate string Picker(string scene);

		public static bool Running { get; private set; }
		private const float FastScale = 40f;
		// The driver's own waits were tuned at a 10x clock; they guard game
		// animations (a fade, a page turn), which run on the scaled clock, so
		// they shrink with it.
		private static float Guard
		{
			get { return _fast ? 10f / FastScale : 1f; }
		}
		private static float _sceneSince;
		private static bool _fast;
		private static float _lastPress;
		private static bool _lastWasAnswerScreen;
		private static readonly Dictionary<int, float> _alphaSeen = new Dictionary<int, float>();
		private static readonly System.Reflection.FieldInfo PageIndex = typeof(_Scripts.AMVCC.Controllers.TextController)
			.GetField("_pageIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		private static readonly System.Reflection.FieldInfo PageCount = typeof(_Scripts.AMVCC.Controllers.TextController)
			.GetField("_pageCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

		// Fast replay: a scene's text pages are turned in one go through the
		// game's own TextController.NextPage (each page still fires its
		// events: music, picture, HUD, title), one press each at the scene's
		// pace cost 5-35 presses a scene. It stops after a page that triggers
		// a death or opens a stat/character pop-up, which then wants the
		// player. -1 when the button is not a text pager.
		private static int TurnPages(GameObject nextButton)
		{
			if (PageIndex == null || PageCount == null)
			{
				return -1;
			}
			foreach (_Scripts.AMVCC.Controllers.TextController tc in
				UnityEngine.Object.FindObjectsOfType<_Scripts.AMVCC.Controllers.TextController>())
			{
				if (tc.NextButton != nextButton || tc.TextBlock == null)
				{
					continue;
				}
				int turned = 0;
				while (turned < 100)
				{
					int index = (int)PageIndex.GetValue(tc);
					int count = (int)PageCount.GetValue(tc);
					if (index >= count || index + 1 >= tc.TextBlock.Count)
					{
						break;
					}
					_Scripts.AMVCC.Controllers.TextBlock page = tc.TextBlock[index + 1];
					bool stops = page != null && page.UseTextBlockSpecialSceneries && page.SpecialSceneries != null
						&& (page.SpecialSceneries.IsDeathTrigger || page.SpecialSceneries.TextBlockShowParameter
							|| page.SpecialSceneries.TextBlockShowCharacter);
					tc.NextPage();
					turned++;
					if (stops || !nextButton.activeInHierarchy)
					{
						break;
					}
				}
				return turned;
			}
			return -1;
		}
		private static int _pageTurns;
		private static float _readySince = -1f;
		private static readonly Dictionary<int, float> _pressedAt = new Dictionary<int, float>();

		// Dev probe: page and answer one scene, then log every interactable
		// button (with its click handlers) whenever the set changes.
		public static IEnumerator Probe(float seconds)
		{
			string last = "";
			float end = Time.realtimeSinceStartup + seconds;
			bool answered = false;
			string probeScene = "";
			while (Time.realtimeSinceStartup < end)
			{
				if (Story() != probeScene)
				{
					probeScene = Story();
					answered = false;
				}
				if (!answered && AnswerReady())
				{
					ParameterButtonChanger b = Pick(null, Story());
					if (b != null)
					{
						ModLog.Info("AUTOPLAY probe: answering " + b.gameObject.name + " in " + Story());
						b.Button_Click();
						answered = true;
					}
				}
				else
				{
					UnityEngine.UI.Button next = FindNamed("Next");
					if (next != null)
					{
						Click(next.gameObject);
					}
				}

				string now = DescribeButtons();
				if (now != last)
				{
					ModLog.Info("AUTOPLAY probe @" + Story() + ":\n" + now);
					last = now;
				}
				yield return new WaitForSecondsRealtime(0.5f);
			}
		}

		// Plays until `done` says so (checked between steps) or nothing moves
		// for `stuckAfter` seconds. Each choice scene is answered once, with
		// `pick`'s answer (null = the first one open to the hero). `onScene`
		// runs once whenever a new choice scene is ready, before answering;
		// returning false stops the run there (the scene stays unanswered).
		public static IEnumerator Run(Picker pick, Func<string, bool> onScene, float stuckAfter, bool fast)
		{
			Running = true;
			LastStop = Stop.None;
			_deathsAtAnswer = Deaths();
			_pressedAt.Clear();
			_unlocked.Clear();
			_pageTurns = 0;
			_sceneSince = Time.realtimeSinceStartup;
			_fast = fast;
			// Fast: the game's clock runs FastScale times faster (animations,
			// page turns and timed waits follow it), the sound is off, and the
			// driver looks for the next button every 50 ms. Scene loading is
			// disk-bound and does not speed up.
			float oldScale = Time.timeScale;
			float oldVolume = AudioListener.volume;
			float oldFixed = Time.fixedDeltaTime;
			float oldMaxDelta = Time.maximumDeltaTime;
			ThreadPriority oldLoading = Application.backgroundLoadingPriority;
			int oldVSync = QualitySettings.vSyncCount;
			int oldFrameRate = Application.targetFrameRate;
			if (fast)
			{
				// Unity's own pace, not the game's logic: async scene loads get
				// more of each frame (BelowNormal gives them ~4 ms), and frames
				// are not held to the screen's refresh, so the scaled clock and
				// the driver both get more steps a second
				Application.backgroundLoadingPriority = ThreadPriority.High;
				QualitySettings.vSyncCount = 0;
				Application.targetFrameRate = 240;
				Time.timeScale = FastScale;
				AudioListener.volume = 0f;
				// The fixed step grows with the clock, so a frame still runs
				// the same few FixedUpdates; left as it is, a slow frame (the
				// year interlude) asks for ten times as many, which makes the
				// next frame slower still - the game sank to 1 fps there.
				Time.fixedDeltaTime = oldFixed * FastScale;
				// A frame may move the scaled clock by up to half a second: with
				// the old 0.1 s cap a 60 fps frame moved it 0.1 s instead of
				// 0.33, so the 20x clock ran at 6x. The scaled fixed step keeps
				// that to one or two FixedUpdates a frame.
				Time.maximumDeltaTime = 0.5f;
			}
			string answered = "";
			string seen = "";
			float lastMove = Time.realtimeSinceStartup;
			string lastButtons = "";
			float fpsSince = Time.realtimeSinceStartup;
			int fpsFrames = Time.frameCount;
			ModLog.Info("AUTOPLAY: started at " + Story());
			float runSince = Time.realtimeSinceStartup;
			int runFrames = Time.frameCount;
			System.Diagnostics.Stopwatch driver = new System.Diagnostics.Stopwatch();
			while (true)
			{
				string scene = Story();
				bool moved = false;
				string stopWhy = null;
				// A death comes from an answer (the Deaths stat it raises is in
				// its effects, so the tree counts it): one the path's answer
				// brought is accepted - the run goes on, as the player chose that
				// answer - by the game's own "go on" choice, never "restart the
				// chapter". A death no answer explains, or the fourth (the end
				// of the story), stops the run there.
				if (DeathOnScreen())
				{
					int deaths = Deaths();
					if (deaths >= 4 ? !_throughLastDeath : deaths <= _deathsAtAnswer)
					{
						LastStop = Stop.Death;
						ModLog.Info("AUTOPLAY: " + (deaths >= 4 ? "the hero's last death" : "a death no answer explains")
							+ " at " + scene + " - stopping");
						break;
					}
					if (AcceptDeath())
					{
						ModLog.Info("AUTOPLAY: death " + deaths + " at " + scene + " (from the answer given) - going on");
						lastMove = Time.realtimeSinceStartup;
						yield return new WaitForSecondsRealtime(0.3f);
						continue;
					}
				}
				// One step, guarded: an exception inside a coroutine would end
				// it without a word (seen right after a scene opened twice).
				bool failed = false;
				driver.Start();
				try
				{
					moved = Step(pick, onScene, scene, ref answered, ref seen, out stopWhy);
				}
				catch (Exception e)
				{
					ModLog.Error("AUTOPLAY: step failed at " + scene + " (retrying):\n" + e);
					failed = true;
				}
				driver.Stop();
				if (stopWhy != null)
				{
					ModLog.Info("AUTOPLAY: " + stopWhy);
					break;
				}
				if (moved)
				{
					lastMove = Time.realtimeSinceStartup;
				}
				else if (Time.realtimeSinceStartup - lastMove > 2f && fast)
				{
					UnlockPopups();
				}
				if (!moved && Time.realtimeSinceStartup - lastMove > 4f)
				{
					string now = DescribeButtons();
					if (now != lastButtons)
					{
						ModLog.Info("AUTOPLAY: waiting @" + scene + ", buttons:\n" + now);
						lastButtons = now;
					}
					if (Time.realtimeSinceStartup - lastMove > stuckAfter)
					{
						ModLog.Info("AUTOPLAY: stuck at " + scene + " - stopping");
						break;
					}
				}
				// a frame rate that sinks is worth knowing about
				if (Time.realtimeSinceStartup - fpsSince > 5f)
				{
					float fps = (Time.frameCount - fpsFrames) / (Time.realtimeSinceStartup - fpsSince);
					if (fps < 10f)
					{
						ModLog.Info("AUTOPLAY: slow, " + fps.ToString("0.0") + " fps at " + scene);
					}
					fpsSince = Time.realtimeSinceStartup;
					fpsFrames = Time.frameCount;
				}
				// after an error give the scene time to settle before retrying
				yield return new WaitForSecondsRealtime(failed ? 0.5f : fast ? 0.05f : 0.35f);
			}
			if (fast)
			{
				Application.backgroundLoadingPriority = oldLoading;
				QualitySettings.vSyncCount = oldVSync;
				Application.targetFrameRate = oldFrameRate;
				Time.timeScale = oldScale;
				AudioListener.volume = oldVolume;
				Time.fixedDeltaTime = oldFixed;
				Time.maximumDeltaTime = oldMaxDelta;
			}
			Running = false;
			float runTime = Time.realtimeSinceStartup - runSince;
			ModLog.Info("AUTOPLAY: finished at " + Story() + " (" + runTime.ToString("0") + " s, "
				+ ((Time.frameCount - runFrames) / Math.Max(runTime, 0.01f)).ToString("0") + " fps, driver "
				+ (driver.ElapsedMilliseconds / 1000f).ToString("0.0") + " s)");
		}

		// One move: answer, or press the next continue button. Returns whether
		// anything was pressed; `stopWhy` is set when the run should end.
		private static bool Step(Picker pick, Func<string, bool> onScene, string scene,
			ref string answered, ref string seen, out string stopWhy)
		{
			stopWhy = null;
			bool moved = false;
			{
				// Answer only once the panel has been up a moment: right after
				// it appears the scene is not fully set up, and the game's own
				// GoToConsequence throws (seen at ten times speed).
				bool ready = AnswerReady() && scene != answered;
				if (!ready)
				{
					_readySince = -1f;
				}
				else if (_readySince < 0f)
				{
					_readySince = Time.realtimeSinceStartup;
				}
				if (ready && Time.realtimeSinceStartup - _readySince < 0.25f * Guard)
				{
					return false;
				}
				if (ready)
				{
					if (scene != seen)
					{
						seen = scene;
						if (onScene != null && !onScene(scene))
						{
							stopWhy = "stopping at " + scene;
							return false;
						}
					}
					string wanted = pick != null ? pick(scene) : null;
					ParameterButtonChanger b = Pick(wanted, scene);
					if (b == null)
					{
						LastStop = Stop.Blocked;
						stopWhy = "no open answer" + (wanted != null ? " '" + wanted + "'" : "")
							+ " in " + scene + " - stopping";
						return false;
					}
					ModLog.Info("AUTOPLAY: " + scene + " -> " + b.gameObject.name + " (" + _pageTurns + " page turn(s), "
						+ (Time.realtimeSinceStartup - _sceneSince).ToString("0.0") + " s since the last answer)");
					_pageTurns = 0;
					_sceneSince = Time.realtimeSinceStartup;
					_deathsAtAnswer = Deaths();
					b.Button_Click();
					answered = scene;
					moved = true;
				}
				else
				{
					UnityEngine.UI.Button next = FindContinue();
					// The same button again only after a pause: the answer
					// screen's continue starts a fade on its last press and only
					// turns itself off when the fade is over, and a press in
					// between loads the next scene once more - two to four
					// copies, whose answer clicks then throw in the game's
					// GoToConsequence and never register. Kept per button, as
					// other buttons (an event pop-up) come up in between.
					// Pagers turn a page per press and may go again soon. The
					// fade itself is excluded by Reachable; this pause covers
					// the frames before it starts.
					float at;
					if (next != null && _pressedAt.TryGetValue(next.GetInstanceID(), out at)
						&& Time.realtimeSinceStartup - at
							< (next.gameObject.name == "Next" || next.gameObject.name == "RightArrow" ? 0.3f : 0.3f) * Guard)
					{
						next = null;
					}
					bool pager = next != null && (next.gameObject.name == "Next" || next.gameObject.name == "RightArrow");
					if (next != null && !pager
						&& Time.realtimeSinceStartup - _lastPress < (_lastWasAnswerScreen ? 0.25f : 0.12f * Guard))
					{
						// two presses that each start a scene change in quick
						// succession (an answer screen's continue, then an event
						// pop-up's) crashed the game in native code at 20x: after
						// an answer screen's continue the next press waits 0.25 s
						// of real time (scene loading does not follow the clock)
						next = null;
					}
					if (next != null)
					{
						if (!pager)
						{
							_lastPress = Time.realtimeSinceStartup;
							_lastWasAnswerScreen = PathOf(next.transform).IndexOf("AnswerSelectionControlPanel", StringComparison.Ordinal) >= 0;
						}
						_pressedAt[next.GetInstanceID()] = Time.realtimeSinceStartup;
						if (next.gameObject.name == "Next" || next.gameObject.name == "RightArrow")
						{
							_pageTurns++;
						}
						if (next.gameObject.name != "Next")
						{
							ModLog.Info("AUTOPLAY: continue via " + PathOf(next.transform));
						}
						int turned = _fast && next.gameObject.name == "Next" ? TurnPages(next.gameObject) : -1;
						if (turned < 0)
						{
							Click(next.gameObject);
						}
						else
						{
							_pageTurns += Math.Max(0, turned - 1);
						}
						moved = true;
					}
				}
			}
			return moved;
		}

		// Dev (dev-autoplay.txt): a debug run from the start of chapter 1 of
		// the newest game save, under its own main-save id so the player's run
		// is never touched, played forward with the first open answer; a mod
		// save ("Save now") is made when each of `chapters` starts.
		// Debug runs play on through the hero's last death into the finals
		// (the epilogue); a replay stops there.
		private static bool _throughLastDeath;
		public static IEnumerator DebugRun(int[] chapters)
		{
			string start = MakeDebugSave();
			if (start == null)
			{
				yield break;
			}
			ChoiceWindow.LoadSnapshot(start);
			yield return WaitForLoad();
			int last = 0;
			foreach (int c in chapters)
			{
				last = Math.Max(last, c);
			}
			HashSet<int> saved = new HashSet<int>();
			_throughLastDeath = true;
			yield return Run(SafePick, delegate(string scene)
			{
				int chapter = SceneFlow.ChapterOf(scene);
				foreach (int c in chapters)
				{
					if (chapter == c && saved.Add(c))
					{
						try
						{
							Recorder.SaveManual();
							ModLog.Info("AUTOPLAY: debug save for chapter " + c + " at " + scene);
						}
						catch (Exception e)
						{
							ModLog.Error("AUTOPLAY: debug save failed:\n" + e);
						}
					}
				}
				return chapter <= last && saved.Count < chapters.Length || chapter < last;
			}, 90f, true);
			_throughLastDeath = false;
		}

		// Dev (`dev-autoplay.txt` = `on`): play on from the scene on screen with
		// the survival picker, through the hero's last death, to the story's end.
		public static IEnumerator PlayOn()
		{
			_throughLastDeath = true;
			yield return Run(SafePick, delegate(string scene) { return true; }, 90f, true);
			_throughLastDeath = false;
			ModLog.Info("AUTOPLAY: play-on stopped at " + Story() + " (" + LastStop + ")");
		}

		// After LoadSnapshot: the loading screen has come and gone.
		public static IEnumerator WaitForLoad()
		{
			float t = Time.realtimeSinceStartup;
			yield return new WaitForSecondsRealtime(2f);
			while (Time.realtimeSinceStartup - t < 30f && SceneManager.GetSceneByName("GameLoadingScreen").isLoaded)
			{
				yield return new WaitForSecondsRealtime(0.5f);
			}
			yield return new WaitForSecondsRealtime(1.5f);
		}

		// Copies the newest game save's Chapter_1.dat into a new save folder
		// with its own id; returns the copy's Autosave.dat path.
		private static string MakeDebugSave()
		{
			try
			{
				string saves = System.IO.Path.Combine(Application.persistentDataPath, "Saves");
				string source = null;
				DateTime newest = DateTime.MinValue;
				foreach (string f in System.IO.Directory.GetFiles(saves, "Chapter_1.dat", System.IO.SearchOption.AllDirectories))
				{
					DateTime w = System.IO.File.GetLastWriteTimeUtc(f);
					if (w > newest && f.IndexOf("Debug_", StringComparison.Ordinal) < 0)
					{
						newest = w;
						source = f;
					}
				}
				if (source == null)
				{
					ModLog.Info("AUTOPLAY: no Chapter_1.dat to start a debug run from");
					return null;
				}
				SaveLoadHelper h = Recorder.ReadHelper(source);
				string id = "Debug_" + DateTime.Now.ToString("MM/dd/yyyyhhmmsstt",
					System.Globalization.CultureInfo.InvariantCulture);
				if (h.SlotInfo == null)
				{
					h.SlotInfo = new SaveSlotInfo();
				}
				h.SlotInfo.Id = id;
				string dir = System.IO.Path.Combine(saves, id.Replace('/', System.IO.Path.DirectorySeparatorChar));
				System.IO.Directory.CreateDirectory(dir);
				foreach (string name in new[] { "Chapter_1.dat", "Autosave.dat" })
				{
					using (System.IO.FileStream fs = System.IO.File.Create(System.IO.Path.Combine(dir, name)))
					{
						new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter().Serialize(fs, h);
					}
				}
				ModLog.Info("AUTOPLAY: debug run '" + id + "' from " + source + " (scene "
					+ h.LastOpenScene + ")");
				return System.IO.Path.Combine(dir, "Autosave.dat");
			}
			catch (Exception e)
			{
				ModLog.Error("AUTOPLAY: making the debug save failed:\n" + e);
				return null;
			}
		}

		// The game's pop-ups (an objective earned, an interlude) switch their
		// continue button off in Start and back on from an animation event at
		// the end of their show animation (SetButtonInteractable). At ten times
		// speed that animation can end before Start runs, so the button stays
		// off for good and the replay waits forever. Once nothing has moved
		// for a while, each such pop-up gets that call made for it, once.
		private static readonly HashSet<int> _unlocked = new HashSet<int>();

		private static void UnlockPopups()
		{
			foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
			{
				if (mb == null || !mb.isActiveAndEnabled || _unlocked.Contains(mb.GetInstanceID()))
				{
					continue;
				}
				System.Reflection.MethodInfo m = mb.GetType().GetMethod("SetButtonInteractable",
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, Type.EmptyTypes, null);
				if (m == null)
				{
					continue;
				}
				_unlocked.Add(mb.GetInstanceID());
				try
				{
					m.Invoke(mb, null);
					ModLog.Info("AUTOPLAY: switched on the continue button of " + PathOf(mb.transform));
				}
				catch (Exception e)
				{
					ModLog.Info("AUTOPLAY: " + mb.GetType().Name + ".SetButtonInteractable failed: " + e.Message);
				}
			}
		}

		public enum Stop { None, Blocked, Death }

		// Why the last run stopped early, when it is one of these.
		public static Stop LastStop { get; private set; }

		private static int _deathsAtAnswer;

		// The hero's deaths so far (the game's Deaths stat, 4 = the last).
		private static int Deaths()
		{
			SceneEffects.Hero h = SceneEffects.Now();
			return h != null ? h.Value(SceneEffects.ParameterAsset("Death")) : 0;
		}

		// On the death page: once its choices are up, the one that goes on
		// (not "restart the chapter"); its consequence's continue button is
		// then pressed like any other. False while there is nothing to choose.
		private static bool AcceptDeath()
		{
			foreach (_Scripts.AMVCC.Views.Windows.Death.DeathWindowBehaviour w in
				Resources.FindObjectsOfTypeAll<_Scripts.AMVCC.Views.Windows.Death.DeathWindowBehaviour>())
			{
				if (w == null || !w.gameObject.activeInHierarchy || w.SelectedButton != null)
				{
					continue;
				}
				foreach (_Scripts.AMVCC.Views.Windows.Death.DeathChoiceButtonBehaviour c in
					w.GetComponentsInChildren<_Scripts.AMVCC.Views.Windows.Death.DeathChoiceButtonBehaviour>(false))
				{
					if (!c.RestartChapter && c.gameObject.activeInHierarchy)
					{
						c.OnButton_Click();
						return true;
					}
				}
			}
			return false;
		}

		// The game's death page (restart the chapter or go on) is up.
		public static bool DeathOnScreen()
		{
			foreach (_Scripts.AMVCC.Views.Windows.Death.DeathWindowBehaviour w in
				Resources.FindObjectsOfTypeAll<_Scripts.AMVCC.Views.Windows.Death.DeathWindowBehaviour>())
			{
				if (w != null && w.gameObject.scene.IsValid() && w.gameObject.activeInHierarchy)
				{
					return true;
				}
			}
			return false;
		}

		// The story scene on screen ("" between scenes).
		public static string Story()
		{
			GameManager gm = GameManager.Instance;
			return gm != null ? gm.OpenedSceneName ?? "" : "";
		}

		// Answer buttons are up (the game's own "ChoiseDescription" panel
		// exists and a choice button is visible).
		public static bool AnswerReady()
		{
			return GameObject.Find("ChoiseDescription") != null && VisibleChoices().Count > 0;
		}

		public static List<ParameterButtonChanger> VisibleChoices()
		{
			List<ParameterButtonChanger> list = new List<ParameterButtonChanger>();
			foreach (ParameterButtonChanger b in Resources.FindObjectsOfTypeAll<ParameterButtonChanger>())
			{
				if (b != null && b.gameObject.scene.IsValid() && b.gameObject.activeInHierarchy)
				{
					list.Add(b);
				}
			}
			return list;
		}

		// The forced answer when it is offered and open to the hero, else the
		// first open one.
		// Debug runs: an answer that keeps the hero alive. Answers that add
		// Death are left out. Two things ended the hero for good at the end
		// of chapter 4 with Death still 0: Will running out (the first open
		// answers kept spending it, 30 -> -10, and some scenes drain it
		// whatever is answered), and the track's last scene opening its
		// surviving answers only to a stat built over the chapter (the
		// rebel's 053.080.010_SecretMeet: Protest or Repression >= 8, else
		// only the answer that sets Death to 4). So each open answer scores
		// the Will it leaves (up to 30) plus five per point of the one stat
		// the track's requirements ask most of and the hero is closest to;
		// the first of equals wins.
		private static string SafePick(string scene)
		{
			List<ParameterButtonChanger> buttons = VisibleChoices();
			buttons.Sort((a, b) => ButtonNumber(a.gameObject.name).CompareTo(ButtonNumber(b.gameObject.name)));
			SceneEffects.Hero now = SceneEffects.Now();
			int will = StatOf(now, "Will", 100);
			string focus = FocusStat(scene, now);
			string best = null;
			int bestScore = int.MinValue;
			foreach (ParameterButtonChanger b in buttons)
			{
				if (!b.GetButtonState())
				{
					continue;
				}
				string name = b.gameObject.name;
				List<SceneEffects.Effect> fx = SceneEffects.For(scene, name);
				if (fx.Exists(f => f.Kind == 'P' && f.Name == "Death" && f.Value > 0))
				{
					continue;
				}
				int after = will, push = 0;
				foreach (SceneEffects.Effect f in fx)
				{
					if (f.Kind == 'P' && f.Name == "Will")
					{
						after += f.Value;
					}
					else if (f.Kind == 'P' && f.Name == focus)
					{
						push += f.Value;
					}
				}
				int score = Math.Min(after, 30) + 5 * push;
				if (score > bestScore)
				{
					bestScore = score;
					best = name;
				}
			}
			return best;
		}

		private static int StatOf(SceneEffects.Hero h, string stat, int unknown)
		{
			var asset = SceneEffects.ParameterAsset(stat);
			return h != null && asset != null ? h.Value(asset) : unknown;
		}

		// The stat of the scene's track (its first four characters, "053.")
		// whose highest requirement the hero is nearest to at the track's
		// first decision, Will left out - then kept for the whole track:
		// switching between two close stats (Justice and Carier on the judge's
		// track) let both fall short of the last scene's gate.
		private static readonly Dictionary<string, string> _focus = new Dictionary<string, string>();
		private static readonly Dictionary<string, Dictionary<string, int>> _thresholds =
			new Dictionary<string, Dictionary<string, int>>();

		private static string FocusStat(string scene, SceneEffects.Hero now)
		{
			if (scene == null || scene.Length < 4)
			{
				return null;
			}
			string prefix = scene.Substring(0, 4);
			string kept;
			if (_focus.TryGetValue(prefix, out kept))
			{
				return kept;
			}
			Dictionary<string, int> need;
			if (!_thresholds.TryGetValue(prefix, out need))
			{
				need = SceneEffects.ThresholdsIn(prefix);
				_thresholds[prefix] = need;
			}
			string focus = null;
			float bestShare = -1f;
			foreach (KeyValuePair<string, int> kv in need)
			{
				if (kv.Key == "Will" || kv.Key == "Death" || kv.Value <= 0)
				{
					continue;
				}
				float share = (float)StatOf(now, kv.Key, 0) / kv.Value;
				if (share > bestShare)
				{
					bestShare = share;
					focus = kv.Key;
				}
			}
			_focus[prefix] = focus;
			ModLog.Info("AUTOPLAY: debug picks on track " + prefix + " build " + (focus ?? "(none)") + " and keep Will");
			return focus;
		}

		public static ParameterButtonChanger Pick(string wanted, string scene)
		{
			ParameterButtonChanger first = null;
			// in button order, so "first open" is the same answer the tree
			// counts for a scene without a pick
			List<ParameterButtonChanger> buttons = VisibleChoices();
			buttons.Sort((a, b) => ButtonNumber(a.gameObject.name).CompareTo(ButtonNumber(b.gameObject.name)));
			foreach (ParameterButtonChanger b in buttons)
			{
				bool open = b.GetButtonState();
				if (!string.IsNullOrEmpty(wanted) && b.gameObject.name == wanted)
				{
					return open ? b : null;
				}
				if (open && first == null)
				{
					first = b;
				}
			}
			return string.IsNullOrEmpty(wanted) ? first : null;
		}

		private static int ButtonNumber(string name)
		{
			int i = name.Length;
			while (i > 0 && char.IsDigit(name[i - 1]))
			{
				i--;
			}
			int n;
			return i < name.Length && int.TryParse(name.Substring(i), out n) ? n : int.MaxValue;
		}

		private static readonly System.Text.RegularExpressions.Regex ContinueWord =
			new System.Text.RegularExpressions.Regex("Next|Continue|Close|Skip|Accept|Confirm|Ok\\b|OK\\b|Hide|Finish|StartPicture");
		private static readonly System.Text.RegularExpressions.Regex NeverWord =
			new System.Text.RegularExpressions.Regex("Prev|Back|Setting|Exit|Quit|Menu|Load|Save|Delete|Restart|Language|Window");

		// The button a player would press to go on: a "Next" pager or page
		// arrow first, else any button whose name or click handler says
		// continue/close/skip. Never the HUD, the pause menu, or our own UI.
		// Whether a player's click could reach the button, on a settled
		// screen: no parent CanvasGroup stops raycasts or is still fading. The answer screen's fade
		// (SceneAnimationController.ConsequenceAlphaAnim) turns them off on
		// both pages as it starts, so its continue is not pressed again while
		// it fades - a press then loaded the next scene twice.
		private static bool Reachable(UnityEngine.UI.Button b)
		{
			for (Transform t = b.transform; t != null; t = t.parent)
			{
				CanvasGroup g = t.GetComponent<CanvasGroup>();
				if (g != null)
				{
					if (!g.blocksRaycasts || g.alpha < 0.01f)
					{
						return false;
					}
					// still fading in or out: its alpha moved since the last
					// look (a group may rest at any alpha, so no fixed value)
					float before;
					int id = g.GetInstanceID();
					bool moving = !_alphaSeen.TryGetValue(id, out before) || Math.Abs(before - g.alpha) > 0.001f;
					_alphaSeen[id] = g.alpha;
					if (moving)
					{
						return false;
					}
					if (g.ignoreParentGroups)
					{
						break;
					}
				}
			}
			return true;
		}

		public static UnityEngine.UI.Button FindContinue()
		{
			UnityEngine.UI.Button byHandler = null;
			foreach (UnityEngine.UI.Button b in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
			{
				if (b == null || !b.gameObject.scene.IsValid() || !b.gameObject.activeInHierarchy
					|| !b.interactable || IsOurs(b) || !Reachable(b))
				{
					continue;
				}
				string path = PathOf(b.transform);
				if (path.IndexOf("/HUD/", StringComparison.Ordinal) >= 0
					|| path.IndexOf("Window_Pause", StringComparison.Ordinal) >= 0
					|| b.GetComponentInParent<ParameterButtonChanger>() != null)
				{
					continue;
				}
				string name = b.gameObject.name;
				if (name == "Next" || name == "RightArrow")
				{
					return b;
				}
				if (byHandler != null)
				{
					continue;
				}
				string text = name;
				string owners = "";
				for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
				{
					UnityEngine.Object target = b.onClick.GetPersistentTarget(i);
					text += " " + b.onClick.GetPersistentMethodName(i);
					// the handler's class only ever argues for pressing (a
					// chapter's title picture is StartPictureHelper.OnPointerClick);
					// class names like ChapterStartWindowController would trip
					// the never-list
					owners += " " + (target != null ? target.GetType().Name : "");
				}
				if (ContinueWord.IsMatch(text + owners) && !NeverWord.IsMatch(text))
				{
					byHandler = b;
				}
			}
			return byHandler;
		}

		// An active, interactable button with this GameObject name.
		public static UnityEngine.UI.Button FindNamed(string name)
		{
			foreach (UnityEngine.UI.Button b in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
			{
				if (b != null && b.gameObject.scene.IsValid() && b.gameObject.activeInHierarchy
					&& b.interactable && b.gameObject.name == name && !IsOurs(b))
				{
					return b;
				}
			}
			return null;
		}

		// A real pointer click (enter, down, up, click), so the game's own
		// handlers and animations run as for the mouse.
		public static void Click(GameObject go)
		{
			UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
			UnityEngine.EventSystems.PointerEventData d = new UnityEngine.EventSystems.PointerEventData(es);
			UnityEngine.EventSystems.ExecuteEvents.Execute(go, d, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
			UnityEngine.EventSystems.ExecuteEvents.Execute(go, d, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
			UnityEngine.EventSystems.ExecuteEvents.Execute(go, d, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
			UnityEngine.EventSystems.ExecuteEvents.Execute(go, d, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
			UnityEngine.EventSystems.ExecuteEvents.Execute(go, d, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
		}

		public static string DescribeButtons()
		{
			StringBuilder sb = new StringBuilder();
			foreach (UnityEngine.UI.Button b in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
			{
				if (b == null || !b.gameObject.scene.IsValid() || !b.gameObject.activeInHierarchy
					|| !b.interactable || b.gameObject.scene.name == "DontDestroyOnLoad" && IsOurs(b))
				{
					continue;
				}
				sb.Append("  ").Append(b.gameObject.scene.name).Append(" : ").Append(PathOf(b.transform));
				for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
				{
					UnityEngine.Object t = b.onClick.GetPersistentTarget(i);
					sb.Append("  -> ").Append(t != null ? t.GetType().Name : "null").Append('.')
						.Append(b.onClick.GetPersistentMethodName(i));
				}
				sb.Append('\n');
			}
			return sb.ToString();
		}

		private static bool IsOurs(UnityEngine.UI.Button b)
		{
			return b.transform.root.name.StartsWith("BranteSaveLoad", StringComparison.Ordinal);
		}

		private static string PathOf(Transform t)
		{
			string p = t.name;
			for (Transform x = t.parent; x != null; x = x.parent)
			{
				p = x.name + "/" + p;
			}
			return p;
		}
	}
}
