using System;
using System.Threading;
using UnityEngine;

namespace Doorstop
{
	// The class Doorstop runs before Unity's own main(). Doorstop invokes it
	// on the main thread with the engine already initialised (this is the
	// pattern BepInEx relies on), so creating a GameObject right here works.
	// If some game/update changes that, we retry inline for a few seconds
	// rather than touching Unity from a background thread, which Unity forbids.
	public static class Entrypoint
	{
		public static void Start()
		{
			for (int i = 0; i < 100; i++)
			{
				try
				{
					GameObject go = new GameObject("BranteLoadAny");
					UnityEngine.Object.DontDestroyOnLoad(go);
					go.AddComponent<ModHost>();
					ModLog.Info("Host created on attempt " + (i + 1));
					return;
				}
				catch (Exception e)
				{
					ModLog.Error("Host creation attempt " + (i + 1) + " failed: " + e.Message);
					Thread.Sleep(100);
				}
			}
			ModLog.Error("Giving up: could not create the host object.");
		}
	}
}
