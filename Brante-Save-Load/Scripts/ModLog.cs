using System;
using System.IO;

namespace Doorstop
{
	// File logger usable from the Doorstop entry point, before any Unity
	// system exists. Appends to Brante-Save-Load.log next to the assembly.
	public static class ModLog
	{
		private static readonly object _lock = new object();
		private static string _path;

		public static string Path
		{
			get
			{
				if (_path == null)
				{
					string dir = System.IO.Path.GetDirectoryName(typeof(ModLog).Assembly.Location);
					_path = System.IO.Path.Combine(dir ?? ".", "Brante-Save-Load.log");
				}
				return _path;
			}
		}

		public static void Info(string message)
		{
			Write("INFO ", message);
		}

		public static void Error(string message)
		{
			Write("ERROR", message);
		}

		public static void Unsafe(string message)
		{
			Write("INFO ", message);
		}

		private static void Write(string level, string message)
		{
			try
			{
				lock (_lock)
				{
					File.AppendAllText(Path, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] [" + level + "] " + message + Environment.NewLine);
				}
			}
			catch
			{
			}
		}
	}
}
