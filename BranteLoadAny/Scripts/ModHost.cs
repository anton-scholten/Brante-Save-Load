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
		private void Awake()
		{
			try
			{
				ModLog.Info("=== BranteLoadAny probe ===");
				ModLog.Info("Unity " + Application.unityVersion + ", platform " + Application.platform);
				ModLog.Info("persistentDataPath: " + Application.persistentDataPath);
				ProbeSaves();
				ProbeChoiceLog();
				ProbeGameManagers();
			}
			catch (Exception e)
			{
				ModLog.Error("Awake failed: " + e);
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
					ModLog.Info("  " + Path.GetFileName(dir) + "/" + Path.GetFileName(f) + " (" + new FileInfo(f).Length + " bytes)");
				}
			}
		}

		private void ProbeChoiceLog()
		{
			string log = Path.Combine(Application.persistentDataPath, "log.txt");
			if (File.Exists(log))
			{
				string text = File.ReadAllText(log);
				ModLog.Info("log.txt (" + text.Length + " chars): " + (text.Length > 300 ? text.Substring(0, 300) + "..." : text));
			}
			else
			{
				ModLog.Info("log.txt does not exist yet (expected before first save)");
			}
		}

		private void ProbeGameManagers()
		{
			// GameManager/SaveLoadManager only exist once the game has booted
			// far enough; poll for a few seconds from Start instead of here.
			StartCoroutine(WaitForGame());
		}

		private System.Collections.IEnumerator WaitForGame()
		{
			for (int i = 0; i < 60; i++)
			{
				yield return new WaitForSeconds(1f);
				Type gm = Type.GetType("GameManager, Assembly-CSharp");
				Type slm = Type.GetType("SaveLoadManager, Assembly-CSharp");
				if (gm != null && slm != null)
				{
					UnityEngine.Object gmInst = UnityEngine.Object.FindObjectOfType(gm);
					UnityEngine.Object slmInst = UnityEngine.Object.FindObjectOfType(slm);
					ModLog.Info("GameManager found: " + (gmInst != null) + ", SaveLoadManager found: " + (slmInst != null));
					if (slmInst != null)
					{
						// Confirm the public LoadGame(SaveLoadHelper) overload exists as decompiled.
						var method = slm.GetType().GetMethod("LoadGame", new Type[] { Type.GetType("_Scripts.Helpers.SaveLoadHelper, Assembly-CSharp") });
						ModLog.Info("SaveLoadManager.LoadGame(SaveLoadHelper) present: " + (method != null));
					}
					yield break;
				}
			}
			ModLog.Info("GameManager/SaveLoadManager never appeared in 60s (still in main menu?)");
		}
	}
}
