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
		private static bool _stop;

		public static void Stop()
		{
			_stop = true;
		}

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
		public static IEnumerator Run(Picker pick, Func<string, bool> onScene, float stuckAfter)
		{
			Running = true;
			_stop = false;
			string answered = "";
			string seen = "";
			float lastMove = Time.realtimeSinceStartup;
			string lastButtons = "";
			ModLog.Info("AUTOPLAY: started at " + Story());
			while (!_stop)
			{
				string scene = Story();
				bool moved = false;
				string stopWhy = null;
				// One step, guarded: an exception inside a coroutine would end
				// it without a word (seen right after a scene opened twice).
				try
				{
					moved = Step(pick, onScene, scene, ref answered, ref seen, out stopWhy);
				}
				catch (Exception e)
				{
					ModLog.Error("AUTOPLAY: step failed at " + scene + " (retrying):\n" + e);
				}
				if (stopWhy != null)
				{
					ModLog.Info("AUTOPLAY: " + stopWhy);
					break;
				}
				if (moved)
				{
					lastMove = Time.realtimeSinceStartup;
				}
				else if (Time.realtimeSinceStartup - lastMove > 4f)
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
				yield return new WaitForSecondsRealtime(0.35f);
			}
			Running = false;
			ModLog.Info("AUTOPLAY: finished at " + Story());
		}

		// One move: answer, or press the next continue button. Returns whether
		// anything was pressed; `stopWhy` is set when the run should end.
		private static bool Step(Picker pick, Func<string, bool> onScene, string scene,
			ref string answered, ref string seen, out string stopWhy)
		{
			stopWhy = null;
			bool moved = false;
			{
				if (AnswerReady() && scene != answered)
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
						stopWhy = "no open answer" + (wanted != null ? " '" + wanted + "'" : "")
							+ " in " + scene + " - stopping";
						return false;
					}
					ModLog.Info("AUTOPLAY: " + scene + " -> " + b.gameObject.name);
					b.Button_Click();
					answered = scene;
					moved = true;
				}
				else
				{
					UnityEngine.UI.Button next = FindContinue();
					if (next != null)
					{
						if (next.gameObject.name != "Next")
						{
							ModLog.Info("AUTOPLAY: continue via " + PathOf(next.transform));
						}
						Click(next.gameObject);
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
			yield return Run(null, delegate(string scene)
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
			}, 90f);
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
		public static ParameterButtonChanger Pick(string wanted, string scene)
		{
			ParameterButtonChanger first = null;
			foreach (ParameterButtonChanger b in VisibleChoices())
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

		// One page further in the scene text, when there is one.
		public static bool PageOn()
		{
			foreach (_Scripts.AMVCC.Controllers.TextController c in
				Resources.FindObjectsOfTypeAll<_Scripts.AMVCC.Controllers.TextController>())
			{
				if (c == null || !c.gameObject.scene.IsValid() || !c.gameObject.activeInHierarchy
					|| c.ItsConsequence || c.NextButton == null)
				{
					continue;
				}
				UnityEngine.UI.Button b = c.NextButton.GetComponent<UnityEngine.UI.Button>();
				if (b != null && !b.interactable)
				{
					continue;
				}
				try
				{
					c.NextPage();
					return true;
				}
				catch (Exception)
				{
					return false;
				}
			}
			return false;
		}

		// What the pointer would hit at a point given as a fraction of the screen.
		public static string HitsAt(float fx, float fy)
		{
			UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
			if (es == null)
			{
				return "(no event system)";
			}
			UnityEngine.EventSystems.PointerEventData d = new UnityEngine.EventSystems.PointerEventData(es);
			d.position = new Vector2(fx * Screen.width, fy * Screen.height);
			List<UnityEngine.EventSystems.RaycastResult> hits = new List<UnityEngine.EventSystems.RaycastResult>();
			es.RaycastAll(d, hits);
			StringBuilder sb = new StringBuilder();
			foreach (UnityEngine.EventSystems.RaycastResult h in hits)
			{
				sb.Append("\n    ").Append(PathOf(h.gameObject.transform)).Append(" {");
				foreach (Component c in h.gameObject.GetComponents<Component>())
				{
					sb.Append(c != null ? c.GetType().Name : "null").Append(' ');
				}
				sb.Append('}');
			}
			return sb.ToString();
		}

		private static readonly System.Text.RegularExpressions.Regex ContinueWord =
			new System.Text.RegularExpressions.Regex("Next|Continue|Close|Skip|Accept|Confirm|Ok\\b|OK\\b|Hide|Finish|StartPicture");
		private static readonly System.Text.RegularExpressions.Regex NeverWord =
			new System.Text.RegularExpressions.Regex("Prev|Back|Setting|Exit|Quit|Menu|Load|Save|Delete|Restart|Language|Window");

		// The button a player would press to go on: a "Next" pager or page
		// arrow first, else any button whose name or click handler says
		// continue/close/skip. Never the HUD, the pause menu, or our own UI.
		public static UnityEngine.UI.Button FindContinue()
		{
			UnityEngine.UI.Button byHandler = null;
			foreach (UnityEngine.UI.Button b in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
			{
				if (b == null || !b.gameObject.scene.IsValid() || !b.gameObject.activeInHierarchy
					|| !b.interactable || IsOurs(b))
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

		public static string Diagnose()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("timeScale=").Append(Time.timeScale)
				.Append(" choiceDesc=").Append(GameObject.Find("ChoiseDescription") != null)
				.Append(" choices=").Append(VisibleChoices().Count).Append(" pagers:");
			foreach (_Scripts.AMVCC.Controllers.TextController c in
				Resources.FindObjectsOfTypeAll<_Scripts.AMVCC.Controllers.TextController>())
			{
				if (c == null || !c.gameObject.scene.IsValid())
				{
					continue;
				}
				UnityEngine.UI.Button b = c.NextButton != null ? c.NextButton.GetComponent<UnityEngine.UI.Button>() : null;
				sb.Append(" [").Append(c.gameObject.name).Append(" active=").Append(c.gameObject.activeInHierarchy)
					.Append(" cons=").Append(c.ItsConsequence)
					.Append(" next=").Append(c.NextButton != null ? c.NextButton.name : "null")
					.Append(" nextActive=").Append(c.NextButton != null && c.NextButton.activeInHierarchy)
					.Append(" interactable=").Append(b != null && b.interactable).Append(']');
			}
			return sb.ToString();
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
