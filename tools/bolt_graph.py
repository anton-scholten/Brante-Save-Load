#!/usr/bin/env python3
"""Resolve Bolt macro references and walk the story graph.

Every Bolt machine/macro is a MonoBehaviour whose serialised data is

    header | m_Name | <graph JSON string> | <macro PPtr array>

The graph's `SuperUnit` elements reference a macro by its **index** in that
trailing array (`"nest":{"source":"Macro","macro":N}`), which is what makes the
story graph walkable without type trees.

`tools/bolt_extract.py` only dumps the JSON; this tool rebuilds the object graph:

  python3 tools/bolt_graph.py             # summary + data/bolt-edges.json
  python3 tools/bolt_graph.py --graph GameManager --verbose

Output (data/bolt-edges.json): for every `Click` event, the chain of
switch/branch labels and the `LoadScene` targets reachable from it, plus the
macro names it passes through.
"""

import argparse
import json
import pathlib
import re
import struct
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
GRAPH_START = b'{"graph":{"variables"'
MAX_DEPTH = 40


# ---------------------------------------------------------------- asset layer
def load_objects(asset_path, names=None):
    """Bolt graphs by path id. With `names` (a dict), also fills in the name
    of every MonoBehaviour in the file - the ScriptableObjects (objectives,
    parameters, characters) that graphs reference through their PPtr array."""
    import UnityPy  # imported lazily so --help works without it
    env = UnityPy.load(str(asset_path))
    objects = {}
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            data = obj.get_raw_data()
        except Exception:
            continue
        # header(12) + enabled(4) + script(12) + name length + name
        try:
            name_len = struct.unpack_from("<i", data, 28)[0]
            name = data[32:32 + name_len].decode("utf-8", "replace")
        except Exception:
            name = ""
        if names is not None and name:
            names[obj.path_id] = name
        start = data.find(GRAPH_START)
        if start < 0:
            continue
        graph_end = _json_end(data, start)
        if graph_end < 0:
            continue
        try:
            graph = json.loads(data[start:graph_end + 1])
        except ValueError:
            continue
        macros = _pptr_array(data, graph_end + 1)
        objects[obj.path_id] = {"id": obj.path_id, "name": name, "graph": graph,
                                "macros": macros}
    return objects


def _json_end(buf, start):
    depth = 0
    i = start
    instr = False
    esc = False
    while i < len(buf):
        c = buf[i:i + 1]
        if esc:
            esc = False
        elif c == b"\\":
            esc = True
        elif c == b'"':
            instr = not instr
        elif not instr:
            if c == b"{":
                depth += 1
            elif c == b"}":
                depth -= 1
                if depth == 0:
                    return i
        i += 1
    return -1


def _pptr_array(buf, pos):
    """Reads the trailing `count | count * (fileID int32, pathID int64)` array."""
    pos = (pos + 3) & ~3
    if pos + 4 > len(buf):
        return []
    count = struct.unpack_from("<i", buf, pos)[0]
    if count < 0 or count > 4096 or pos + 4 + count * 12 > len(buf):
        return []
    out = []
    for k in range(count):
        base = pos + 4 + k * 12
        file_id, path_id = struct.unpack_from("<iq", buf, base)
        out.append(path_id if file_id == 0 else (file_id, path_id))
    return out


# ---------------------------------------------------------------- graph model
class Graph:
    def __init__(self, entry, objects):
        self.entry = entry
        self.objects = objects
        self.g = entry["graph"]["graph"]
        self.units = {}
        self.control = {}
        self.value = {}
        for el in self.g.get("elements", []):
            kind = el.get("$type", "")
            if kind == "Bolt.ControlConnection":
                src = el.get("sourceUnit", {}).get("$ref")
                self.control.setdefault((src, el.get("sourceKey")), []).append(
                    (el.get("destinationUnit", {}).get("$ref"), el.get("destinationKey")))
            elif kind == "Bolt.ValueConnection":
                src = el.get("sourceUnit", {}).get("$ref")
                self.value.setdefault(
                    (el.get("destinationUnit", {}).get("$ref"), el.get("destinationKey")),
                    []).append((src, el.get("sourceKey")))
            elif "$id" in el:
                self.units[el["$id"]] = el

    def macro_for(self, unit):
        nest = unit.get("nest") or {}
        idx = nest.get("macro")
        if not isinstance(idx, int) or idx < 0 or idx >= len(self.entry["macros"]):
            return None
        ref = self.entry["macros"][idx]
        return self.objects.get(ref)

    def describe(self, unit):
        kind = unit.get("$type", "")
        member = (unit.get("member") or {}).get("name", "")
        values = []
        for key, val in (unit.get("defaultValues") or {}).items():
            text = _text(val)
            if text is not None:
                values.append("%s=%s" % (key, text))
        return kind.replace("Bolt.", ""), member, values

    def value_source(self, unit_id, port):
        """The unit feeding a value input, plus its description."""
        for (dst, key), sources in self.value.items():
            if dst == unit_id and key == port:
                for src, _ in sources:
                    other = self.units.get(src)
                    if other:
                        return other
        return None


def _text(value):
    if isinstance(value, dict):
        if "$content" in value:
            return _text(value["$content"])
        return None
    if isinstance(value, (str, int, float, bool)):
        return str(value)
    return None


def walk(graph, unit_id, depth=0, trail=None, seen=None):
    """Depth-first walk of control flow; yields (labels, unit, graph) at each
    LoadScene, plus the path of branch/switch labels taken to get there."""
    if depth > MAX_DEPTH or unit_id is None:
        return
    seen = seen or set()
    if (graph.entry["id"], unit_id) in seen:
        return
    seen = seen | {(graph.entry["id"], unit_id)}
    trail = trail or []
    unit = graph.units.get(unit_id)
    if not unit:
        return
    kind = unit.get("$type", "")
    member = (unit.get("member") or {}).get("name", "")

    if member == "LoadScene" or kind.endswith("LoadScene"):
        scene = (unit.get("defaultValues") or {}).get("%sceneName")
        scene = _text(scene)
        if scene:
            yield trail, scene, graph, unit
        return

    if kind == "Bolt.SuperUnit":
        macro = graph.macro_for(unit)
        if macro:
            sub = Graph(macro, graph.objects)
            entry = None
            for u in sub.units.values():
                if u.get("$type") == "Bolt.GraphInput":
                    entry = u["$id"]
                    break
            if entry is not None:
                for item in walk(sub, entry, depth + 1, trail + ["macro:" + (macro.get("name") or "?")], seen):
                    yield item
        return

    if kind == "Bolt.SwitchOnString" or kind == "Bolt.SwitchOnInteger":
        # value input feeds the switch; label the branches with it
        source = graph.value_source(unit_id, "%value") or graph.value_source(unit_id, "value")
        key = ""
        if source:
            _, sm, svals = graph.describe(source)
            key = sm or (svals[0] if svals else source.get("$type", "").replace("Bolt.", ""))
        for (dst, port), _ in sorted(graph.control.items(), key=lambda kv: str(kv[0])):
            pass
        branches = [(port, dst) for (src, port), dsts in graph.control.items()
                    if src == unit_id for dst in dsts]
        for port, (dst, _) in branches:
            if port in ("enter",):
                continue
            for item in walk(graph, dst, depth + 1, trail + ["%s=%s" % (key or "switch", port)], seen):
                yield item
        return

    if kind == "Bolt.Branch":
        for port in ("ifTrue", "ifFalse"):
            for (dst, _) in graph.control.get((unit_id, port), []):
                for item in walk(graph, dst, depth + 1, trail + [port], seen):
                    yield item
        return

    # default: follow every control output
    for (src, port), dsts in graph.control.items():
        if src != unit_id:
            continue
        for dst, _ in dsts:
            for item in walk(graph, dst, depth + 1, trail, seen):
                yield item


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game", help="game folder (default: auto-detect)")
    ap.add_argument("--graph", help="only this machine/macro name")
    ap.add_argument("--verbose", action="store_true")
    ap.add_argument("--out", default=str(ROOT / "data" / "bolt-edges.json"))
    args = ap.parse_args()

    game = args.game
    if not game:
        home = pathlib.Path.home()
        for vdf in (home / ".steam/steam/steamapps/libraryfolders.vdf",
                    home / ".local/share/Steam/steamapps/libraryfolders.vdf"):
            if not vdf.is_file():
                continue
            for lib in re.findall(r'"path"\s+"([^"]+)"', vdf.read_text(encoding="utf-8", errors="replace")):
                cand = pathlib.Path(lib) / "steamapps/common/The Life and Suffering of Sir Brante"
                if cand.is_dir():
                    game = cand
                    break
            if game:
                break
    if not game:
        print("Game folder not found; pass --game DIR.", file=sys.stderr)
        return 1
    asset = pathlib.Path(game) / "The Life and Suffering of Sir Brante_Data/resources.assets"
    objects = load_objects(asset)
    print("graph objects: %d" % len(objects))

    by_name = {}
    for entry in objects.values():
        by_name.setdefault(entry["name"], []).append(entry)

    targets = [e for e in objects.values() if args.graph in (None, e["name"])]
    results = []
    for entry in targets:
        graph = Graph(entry, objects)
        clicks = [u["$id"] for u in graph.units.values()
                  if u.get("$type") == "Bolt.CustomEvent"
                  and "Click" in json.dumps(u.get("defaultValues") or {})]
        for click in clicks:
            for trail, scene, g, unit in walk(graph, click):
                results.append({"machine": entry["name"], "event": "Click",
                                "trail": trail, "scene": scene})
    print("reachable LoadScene calls from Click events: %d" % len(results))
    for row in results[:25]:
        print("  %-18s %-28s -> %s" % (row["machine"], " / ".join(row["trail"]) or "(direct)", row["scene"]))
    out = pathlib.Path(args.out)
    out.write_text(json.dumps({"schema": 1, "edges": results}, ensure_ascii=False, indent=1),
                   encoding="utf-8")
    print("wrote %s" % out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
