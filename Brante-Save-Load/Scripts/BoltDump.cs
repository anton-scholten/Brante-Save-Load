using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Doorstop
{
	// Dumps the Bolt graph that handles a choice click (the machine on the
	// "TalismanFlowMachine" object) at runtime. Embedded machine graphs are
	// serialised natively, so they cannot be read from the assets offline; the
	// game can hand us the live graph instead.
	//
	// Output: <Runs>/<save>/bolt-<scene>.txt, one line per unit/connection:
	//   U <guid> <type> <member> <literal...>
	//   C <fromGuid> <fromPort> <toGuid> <toPort>
	//   V <fromGuid> <fromPort> <toGuid> <toPort>
	public static class BoltDump
	{
		public static void DumpFor(string scene)
		{
			try
			{
				string path = Path.Combine(Recorder.RunDir(Recorder.CurrentRunId(), "Auto"),
					"bolt-" + Recorder.SafeName(scene) + ".txt");
				Directory.CreateDirectory(Path.GetDirectoryName(path));

				GameObject go = FindTalisman();
				if (go == null)
				{
					ModLog.Error("BoltDump: TalismanFlowMachine not found");
					return;
				}
				Component machine = null;
				foreach (Component c in go.GetComponents<Component>())
				{
					if (c != null && c.GetType().Name.IndexOf("FlowMachine", StringComparison.Ordinal) >= 0)
					{
						machine = c;
						break;
					}
				}
				if (machine == null)
				{
					ModLog.Error("BoltDump: no FlowMachine component on '" + go.name + "'");
					return;
				}

				object nest = Member(machine, "nest");
				object macro = Member(nest, "macro");
				if (macro == null)
				{
					macro = nest;
				}
				object graph = Member(macro, "graph");
				if (graph == null)
				{
					ModLog.Error("BoltDump: no graph on the machine");
					return;
				}

				StringBuilder sb = new StringBuilder();
				sb.AppendLine("# scene " + scene + " machine " + machine.GetType().FullName);
				int units = 0;
				foreach (object unit in AsEnumerable(Member(graph, "units")))
				{
					units++;
					sb.AppendLine("U\t" + Id(unit) + "\t" + TypeName(unit) + "\t"
						+ Describe(unit));
				}
				int conns = 0;
				conns += DumpConnections(sb, graph, "controlConnections", "C");
				conns += DumpConnections(sb, graph, "valueConnections", "V");
				File.WriteAllText(path, sb.ToString());
				ModLog.Info("BoltDump: " + units + " unit(s), " + conns
					+ " connection(s) written to " + Path.GetFileName(path));
			}
			catch (Exception e)
			{
				ModLog.Error("BoltDump failed:\n" + e);
			}
		}

		private static GameObject FindTalisman()
		{
			try
			{
				GameObject go = GameObject.FindGameObjectWithTag("TalismanFlowMachine");
				if (go != null)
				{
					return go;
				}
			}
			catch
			{
				// tag may not exist
			}
			foreach (Component c in Resources.FindObjectsOfTypeAll<Component>())
			{
				if (c != null && c.gameObject.scene.IsValid()
					&& c.gameObject.name.IndexOf("Talisman", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return c.gameObject;
				}
			}
			return null;
		}

		private static int DumpConnections(StringBuilder sb, object graph, string name, string tag)
		{
			int n = 0;
			foreach (object conn in AsEnumerable(Member(graph, name)))
			{
				object src = Member(conn, "source");
				object dst = Member(conn, "destination");
				if (src == null || dst == null)
				{
					continue;
				}
				object srcUnit = Member(src, "unit");
				object dstUnit = Member(dst, "unit");
				sb.AppendLine(tag + "\t" + Id(srcUnit) + "\t" + Key(src) + "\t"
					+ Id(dstUnit) + "\t" + Key(dst));
				n++;
			}
			return n;
		}

		private static string Describe(object unit)
		{
			StringBuilder sb = new StringBuilder();
			try
			{
				object member = Member(unit, "member");
				if (member != null)
				{
					sb.Append(Member(member, "name") + "(" + Member(member, "targetType") + "): ");
				}
				object value = Member(unit, "value");
				if (value != null)
				{
					sb.Append("value=" + value + " ");
				}
				object name = Member(unit, "defaultValues");
				if (name is IDictionary)
				{
					foreach (DictionaryEntry e in (IDictionary)name)
					{
						sb.Append(e.Key + "=" + e.Value + " ");
					}
				}
			}
			catch (Exception e)
			{
				sb.Append("(" + e.Message + ")");
			}
			return sb.ToString().Replace("\r", " ").Replace("\n", " ");
		}

		private static string Id(object unit)
		{
			object guid = Member(unit, "guid");
			return guid != null ? guid.ToString() : "?";
		}

		private static string Key(object port)
		{
			object key = Member(port, "key");
			return key != null ? key.ToString() : "?";
		}

		private static string TypeName(object o)
		{
			return o == null ? "?" : o.GetType().Name;
		}

		private static IEnumerable AsEnumerable(object o)
		{
			IEnumerable e = o as IEnumerable;
			return e ?? new object[0];
		}

		// Reflection over Bolt's own types, tolerant of the spelling of
		// properties vs fields across Bolt versions.
		private static object Member(object target, string name)
		{
			if (target == null)
			{
				return null;
			}
			Type t = target.GetType();
			PropertyInfo p = t.GetProperty(name,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (p != null)
			{
				try
				{
					return p.GetValue(target, null);
				}
				catch
				{
					// fall through to fields
				}
			}
			FieldInfo f = t.GetField(name,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (f != null)
			{
				try
				{
					return f.GetValue(target);
				}
				catch
				{
					return null;
				}
			}
			// base types (graph elements inherit the guid from a base class)
			Type b = t.BaseType;
			while (b != null)
			{
				p = b.GetProperty(name,
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (p != null)
				{
					try { return p.GetValue(target, null); }
					catch { return null; }
				}
				f = b.GetField(name,
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (f != null)
				{
					try { return f.GetValue(target); }
					catch { return null; }
				}
				b = b.BaseType;
			}
			return null;
		}
	}
}
