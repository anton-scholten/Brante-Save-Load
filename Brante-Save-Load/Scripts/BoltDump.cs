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
		// The story's chapter flows (ChildhoodChapter ... Insurrection, on the
		// persistent TalismanFlowMachine) each have a Bolt `Once` unit: the first
		// time a chapter's end is reached it shows the loading bar and loads the
		// chapter's finish screen; every later time it takes `after` ->
		// GameManager.ClickTriggerEvent, which runs the flow again: endless
		// recursion in one frame, then the game dies in native code. A Once
		// unit's flag lives in the machine's graph data, not in a save, so after
		// a mod load back into a chapter whose end was already reached in this
		// session, playing that end again crashed (a replay across the chapter
		// 3 -> 4 change, twice). A fresh session has every flag clear; so does
		// the game after any mod load, by this. Returns the flags cleared.
		public static int ResetOnceUnits()
		{
			int cleared = 0;
			try
			{
				Type machine = Type.GetType("Bolt.FlowMachine, Bolt.Flow.Runtime");
				if (machine == null)
				{
					return 0;
				}
				foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(machine))
				{
					Component c = o as Component;
					if (c == null || !c.gameObject.scene.IsValid())
					{
						continue;
					}
					object reference = FindProperty(machine, "reference").GetValue(c, null);
					if (reference == null)
					{
						continue;
					}
					object data = FindProperty(reference.GetType(), "data").GetValue(reference, null);
					cleared += ClearOnce(data, 0);
				}
			}
			catch (Exception e)
			{
				ModLog.Error("BoltDump: resetting the flows' Once units failed:\n" + e);
			}
			return cleared;
		}

		private static PropertyInfo FindProperty(Type t, string name)
		{
			for (; t != null; t = t.BaseType)
			{
				PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
				if (p != null)
				{
					return p;
				}
			}
			return null;
		}

		private static int ClearOnce(object graphData, int depth)
		{
			if (graphData == null || depth > 20)
			{
				return 0;
			}
			int cleared = 0;
			IDictionary elements = FindProperty(graphData.GetType(), "elementsData").GetValue(graphData, null) as IDictionary;
			if (elements != null)
			{
				foreach (object d in elements.Values)
				{
					if (d != null && d.GetType().FullName == "Bolt.Once+Data")
					{
						FieldInfo f = d.GetType().GetField("executed");
						if ((bool)f.GetValue(d))
						{
							f.SetValue(d, false);
							cleared++;
						}
					}
				}
			}
			IDictionary children = FindProperty(graphData.GetType(), "childrenGraphsData").GetValue(graphData, null) as IDictionary;
			if (children != null)
			{
				foreach (object child in children.Values)
				{
					cleared += ClearOnce(child, depth + 1);
				}
			}
			return cleared;
		}

		// Dev: every Bolt variable the game holds outside a save - application,
		// saved (PlayerPrefs), and each Variables component (object and scene
		// variables) - as sorted "scope.name=value" lines.
		public static List<string> VariableLines()
		{
			List<string> lines = new List<string>();
			try
			{
				Type vars = Type.GetType("Bolt.Variables, Bolt.Core.Runtime");
				if (vars == null)
				{
					lines.Add("(Bolt.Variables not found)");
					return lines;
				}
				AddDecl(lines, "app", vars.GetProperty("Application", BindingFlags.Static | BindingFlags.Public).GetValue(null, null));
				AddDecl(lines, "saved", vars.GetProperty("Saved", BindingFlags.Static | BindingFlags.Public).GetValue(null, null));
				PropertyInfo decl = vars.GetProperty("declarations", BindingFlags.Instance | BindingFlags.Public);
				foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(vars))
				{
					Component c = o as Component;
					if (c == null || !c.gameObject.scene.IsValid())
					{
						continue;
					}
					AddDecl(lines, c.gameObject.scene.name + "/" + c.gameObject.name, decl.GetValue(c, null));
				}
			}
			catch (Exception e)
			{
				lines.Add("(dump failed: " + e.Message + ")");
			}
			lines.Sort(StringComparer.Ordinal);
			return lines;
		}

		private static void AddDecl(List<string> lines, string scope, object declarations)
		{
			IEnumerable items = declarations as IEnumerable;
			if (items == null)
			{
				return;
			}
			foreach (object d in items)
			{
				Type t = d.GetType();
				object name = t.GetProperty("name").GetValue(d, null);
				object value = t.GetProperty("value").GetValue(d, null);
				string v = value == null ? "null" : value is UnityEngine.Object ? (((UnityEngine.Object)value) == null ? "destroyed" : ((UnityEngine.Object)value).name) : value.ToString();
				lines.Add(scope + "." + name + "=" + v);
			}
		}

		public static void DumpFor(string scene)
		{
			try
			{
				string path = Path.Combine(Recorder.ChoiceDir(Recorder.CurrentRunId()),
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
