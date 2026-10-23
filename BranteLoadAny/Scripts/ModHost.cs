using System;
using System.IO;
using UnityEngine;

namespace Doorstop
{
	// The mod's host object. Phase 2 = runtime probe: logs what the
	// decompile says about the game so the two can be compared. Later
	// phases add the snapshot recorder and the choice-list UI here.
	public class ModHost : MonoBehaviour
	{
		// Start fires the frame after the boot-hook postfix (see Entrypoint)
		// created us, i.e. the engine is fully up by now.
		private void Start()
		{
			Probe("Start", RunProbes);
			ProbeGameManagers();
		}

		private void RunProbes()
		{
			ModLog.Info("=== BranteLoadAny probe ===");
			ModLog.Info("Unity " + Application.unityVersion + ", platform " + Application.platform);
			ModLog.Info("persistentDataPath: " + Application.persistentDataPath);
			ProbeSaves();
			ProbeChoiceLog();
		}

		// Wraps a probe so one failing call does not hide the others, and so
		// the log says exactly which call the game's Mono objected to.
		private static void Probe(string stage, Action body)
		{
			try
			{
				body();
			}
			catch (Exception e)
			{
				ModLog.Error("Probe " + stage + " failed:\n" + e.ToString());
			}
		}

		private void ProbeSaves()
		{
			string saves = Path.Combine(Application.persistentDataPath, "Saves");
			if (!Directory.Exists(saves))
			{
				ModLog.Info("Saves folder missing: " + saves);
				return;
			}
			string[] dirs = Directory.GetDirectories(saves);
			ModLog.Info("Saves: " + dirs.Length + " slot(s) in " + saves);
			foreach (string dir in dirs)
			{
				foreach (string f in Directory.GetFiles(dir))
				{
					ModLog.Info("  " + Path.GetFileName(dir) + "/" + Path.GetFileName(f) + " ("
						+ new FileInfo(f).Length + " bytes)");
				}
			}
		}

		private void ProbeChoiceLog()
		{
			string log = Path.Combine(Application.persistentDataPath, "log.txt");
			if (File.Exists(log))
			{
				string text = File.ReadAllText(log);
				ModLog.Info("log.txt (answer log, decompile said dead code): "
					+ (text.Length > 300 ? text.Substring(0, 300) + "..." : text));
			}
			else
			{
				ModLog.Info("log.txt does not exist yet (expected before first save)");
			}
		}

		// GameManager/SaveLoadManager only exist once the game has booted
		// far enough; poll from Start instead of Awake.
		private void ProbeGameManagers()
		{
			StartCoroutine(WaitForGame());
		}

		private System.Collections.IEnumerator WaitForGame()
		{
			for (int i = 0; i < 120; i++)
			{
				yield return new WaitForSeconds(1f);
				Type gm = Type.GetType("_Scripts.Managers.GameManager, Assembly-CSharp");
				Type slm = Type.GetType("_Scripts.Managers.SaveLoadManager, Assembly-CSharp");
				if (gm != null && slm != null)
				{
					UnityEngine.Object gmInst = UnityEngine.Object.FindObjectOfType(gm);
					UnityEngine.Object slmInst = UnityEngine.Object.FindObjectOfType(slm);
					ModLog.Info("GameManager found: " + (gmInst != null)
						+ ", SaveLoadManager found: " + (slmInst != null));
					// Confirm the public LoadGame(SaveLoadHelper) overload exists
					// as decompiled (by type walk; the instance may still be null
					// in the main menu).
					Type helper = Type.GetType("_Scripts.Helpers.SaveLoadHelper, Assembly-CSharp");
					var method = slm != null
						? slm.GetMethod("LoadGame", new Type[] { helper })
						: null;
					ModLog.Info("SaveLoadHelper type: " + (helper != null)
						+ ", SaveLoadManager.LoadGame(SaveLoadHelper) present: " + (method != null));
					// Also confirm the choice UI types we plan to hook.
					ModLog.Info("SceneController type: "
						+ (Type.GetType("_Scripts.AMVCC.Controllers.SceneController, Assembly-CSharp") != null)
						+ ", ParameterButtonChanger type: "
						+ (Type.GetType("_Scripts.AMVCC.Views.ParameterButtonChanger, Assembly-CSharp") != null)
						+ ", WindowPause type: "
						+ (Type.GetType("_Scripts.AMVCC.Views.Windows.WindowPause, Assembly-CSharp") != null));
					yield break;
				}
			}
			ModLog.Info("GameManager/SaveLoadManager never appeared in 120s (still in main menu?)");
		}
	}
}
