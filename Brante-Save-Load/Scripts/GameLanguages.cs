using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using _Scripts.AMVCC.Views.Windows;
using _Scripts.AMVCC.Views.Windows.Settings;
using _Scripts.Managers;

namespace Doorstop
{
	// The game ships 14 complete translations, but the main menu's settings
	// window offers 11: its serialized list (SettingsWindow._languages, a
	// LanguagesSelection) leaves out Traditional Chinese, Polish and Turkish.
	// The pause menu's window (WindowPause) lists every Languages value, but
	// names those three by their I2 terms, which are only their codes ("Pl",
	// "Tr", "Zh_Tw"). So: the three are added to the settings window's list
	// before its Start reads it (Traditional after Simplified Chinese, Polish
	// and Turkish last), and both windows show their own names for them
	// (and Italian's: its term reads "Italien").
	public static class GameLanguages
	{
		private static readonly Dictionary<Languages, string> OwnName = new Dictionary<Languages, string>
		{
			{ Languages.Zh_Tw, "繁體中文" },
			{ Languages.Pl, "Polski" },
			{ Languages.Tr, "Türkçe" },
			{ Languages.It, "Italiano" },   // the game's own term reads "Italien"
		};

		public static void Patch(Harmony harmony)
		{
			try
			{
				Prefix(harmony, typeof(SettingsWindow), "Start", "AddMissing");
				foreach (string m in new[] { "Start", "LanguageSelectLeftButton_Click", "LanguageSelectRightButton_Click" })
				{
					Postfix(harmony, typeof(SettingsWindow), m, "FixName");
				}
				foreach (string m in new[] { "OnEnable", "LanguageSelectLeftButton_Click", "LanguageSelectRightButton_Click" })
				{
					Postfix(harmony, typeof(WindowPause), m, "FixPauseName");
				}
				ModLog.Info("Patched the settings windows: Traditional Chinese, Polish and Turkish selectable");
			}
			catch (Exception e)
			{
				ModLog.Error("Language patch failed:\n" + e);
			}
		}

		private static void Prefix(Harmony harmony, Type type, string method, string handler)
		{
			MethodInfo m = AccessTools.Method(type, method);
			if (m == null)
			{
				ModLog.Error(type.Name + "." + method + " not found");
				return;
			}
			harmony.Patch(m, prefix: new HarmonyMethod(typeof(GameLanguages), handler));
		}

		private static void Postfix(Harmony harmony, Type type, string method, string handler)
		{
			MethodInfo m = AccessTools.Method(type, method);
			if (m == null)
			{
				ModLog.Error(type.Name + "." + method + " not found");
				return;
			}
			harmony.Patch(m, postfix: new HarmonyMethod(typeof(GameLanguages), handler));
		}

		// dev-settings.txt: opens the main menu's settings window, presses the
		// language arrow once per language (logging the language set and the
		// name shown, a screenshot of each in Brante-Save-Load/Shots), and
		// goes back to the language it had.
		public static System.Collections.IEnumerator DevSettings()
		{
			// the main menu opens it as the additive scene "Settings"
			UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Settings", UnityEngine.SceneManagement.LoadSceneMode.Additive);
			yield return new UnityEngine.WaitForSecondsRealtime(3f);
			SettingsWindow w = null;
			foreach (SettingsWindow s in UnityEngine.Resources.FindObjectsOfTypeAll<SettingsWindow>())
			{
				if (s != null && s.gameObject.scene.IsValid())
				{
					w = s;
				}
			}
			if (w == null)
			{
				ModLog.Info("DEV SETTINGS: no settings window");
				yield break;
			}
			for (UnityEngine.Transform t = w.transform; t != null; t = t.parent)
			{
				t.gameObject.SetActive(true);
			}
			yield return new UnityEngine.WaitForSecondsRealtime(2f);   // its Start ran
			Languages had = GameManager.Instance.GameSettings.Language;
			ModLog.Info("DEV SETTINGS: " + w.GameLanguages.Count + " languages: "
				+ string.Join(", ", w.GameLanguages.ConvertAll(l => l.ToString()).ToArray()));
			string dir = System.IO.Path.Combine(System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "Brante-Save-Load"), "Shots");
			System.IO.Directory.CreateDirectory(dir);
			for (int i = 0; i < w.GameLanguages.Count; i++)
			{
				w.LanguageSelectRightButton_Click();
				yield return new UnityEngine.WaitForSecondsRealtime(1f);
				Languages now = GameManager.Instance.GameSettings.Language;
				ModLog.Info("DEV SETTINGS: " + now + " shown as '" + w.LanguageText.text + "' (font " + w.LanguageText.font.name + ")");
				UnityEngine.ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "settings_" + now + ".png"));
				yield return new UnityEngine.WaitForSecondsRealtime(0.5f);
			}
			while (GameManager.Instance.GameSettings.Language != had)
			{
				w.LanguageSelectRightButton_Click();
				yield return null;
			}
			ModLog.Info("DEV SETTINGS: done, back to " + had);
		}

		private static void AddMissing(SettingsWindow __instance)
		{
			try
			{
				List<Languages> list = __instance.GameLanguages;
				if (list == null)
				{
					return;
				}
				if (!list.Contains(Languages.Zh_Tw))
				{
					int chin = list.IndexOf(Languages.Chin);
					list.Insert(chin >= 0 ? chin + 1 : list.Count, Languages.Zh_Tw);
				}
				foreach (Languages l in new[] { Languages.Pl, Languages.Tr })
				{
					if (!list.Contains(l))
					{
						list.Add(l);
					}
				}
			}
			catch (Exception e)
			{
				ModLog.Error("Adding languages failed:\n" + e);
			}
		}

		private static void FixName(SettingsWindow __instance)
		{
			Rename(__instance.LanguageText);
		}

		private static void FixPauseName(WindowPause __instance)
		{
			Rename(__instance.LanguageText);
		}

		// The language shown is the one set: its own name where the game's
		// term is only the code.
		private static void Rename(TextMeshProUGUI text)
		{
			try
			{
				GameManager gm = GameManager.Instance;
				if (text == null || gm == null || gm.GameSettings == null)
				{
					return;
				}
				string name;
				if (OwnName.TryGetValue(gm.GameSettings.Language, out name))
				{
					text.text = name;
				}
				// the font of the language just set (Traditional Chinese has
				// its own), also when leaving it for a Latin one
				gm.ChangeFontAsset(text);
			}
			catch (Exception e)
			{
				ModLog.Error("Language name failed:\n" + e);
			}
		}
	}
}
