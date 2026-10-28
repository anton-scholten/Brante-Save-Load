#!/usr/bin/env python3
"""Extract the game's Bolt graphs from Unity assets into a readable JSON file.

Bolt (Ludiq) serialises a machine's graph as a **JSON string** inside the asset,
starting with `{"graph":{"variables"`. That makes the story logic readable
without a full Unity type-tree decoder:

  elements[]  units (`"$type":"Bolt.InvokeMember"`, `"Bolt.SwitchOnString"`, ...)
              and connections (`Bolt.ControlConnection`, `Bolt.ValueConnection`)
  $id / $ref  integer ids used by the connections
  defaultValues / literal units carry the actual scene names

This tool finds every such blob in the game's asset files, parses it and writes
a compact description per graph: which units it has, which scene it loads and
how they are wired. It does not yet interpret the branches - that is the next
step (see docs/BRANCHES.md).

Usage:
  python3 tools/bolt_extract.py [--game DIR] [--out data/bolt-graphs.json]
"""

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
START = b'{"graph":{"variables"'
SCENEISH = re.compile(r"^(?:\d{2,3}\.\d{2,3}\.\d{2,3}(?:_\d+)?|[A-Z][A-Za-z0-9_]+)$")


def find_game(explicit):
    if explicit:
        return pathlib.Path(explicit)
    home = pathlib.Path.home()
    for vdf in (home / ".steam/steam/steamapps/libraryfolders.vdf",
                home / ".local/share/Steam/steamapps/libraryfolders.vdf"):
        if not vdf.is_file():
            continue
        for lib in re.findall(r'"path"\s+"([^"]+)"', vdf.read_text(encoding="utf-8", errors="replace")):
            cand = pathlib.Path(lib) / "steamapps/common/The Life and Suffering of Sir Brante"
            if cand.is_dir():
                return cand
    return None


def iter_blobs(buf):
    """Yield (offset, parsed_json) for every graph blob, using brace matching
    that respects JSON strings/escapes."""
    i = buf.find(START)
    while i != -1:
        depth = 0
        j = i
        instr = False
        esc = False
        while j < len(buf):
            c = buf[j:j + 1]
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
                        break
            j += 1
        try:
            yield i, json.loads(buf[i:j + 1])
        except (ValueError, UnicodeDecodeError):
            pass
        i = buf.find(START, j)


def literal_text(value):
    if isinstance(value, dict):
        if "$content" in value:
            return literal_text(value["$content"])
        return None
    if isinstance(value, str):
        return value
    if isinstance(value, (int, float, bool)):
        return str(value)
    return None


def describe(offset, graph, owner):
    elements = graph.get("graph", {}).get("elements", [])
    units, control, value = {}, [], []
    for el in elements:
        kind = el.get("$type", "")
        if kind == "Bolt.ControlConnection":
            control.append({
                "from": el.get("sourceUnit", {}).get("$ref"),
                "fromPort": el.get("sourceKey"),
                "to": el.get("destinationUnit", {}).get("$ref"),
                "toPort": el.get("destinationKey"),
            })
        elif kind == "Bolt.ValueConnection":
            value.append({
                "from": el.get("sourceUnit", {}).get("$ref"),
                "to": el.get("destinationUnit", {}).get("$ref"),
                "toPort": el.get("destinationKey"),
            })
        elif "$id" in el:
            unit = {"id": el["$id"], "type": kind}
            member = el.get("member")
            if isinstance(member, dict) and member.get("name"):
                unit["member"] = member["name"]
                unit["target"] = member.get("targetTypeName") or member.get("targetType")
            defaults = el.get("defaultValues") or {}
            texts = []
            for key, val in defaults.items():
                text = literal_text(val)
                if text:
                    texts.append("%s=%s" % (key, text))
            if texts:
                unit["values"] = texts
            units[el["$id"]] = unit

    loads = []
    click = False
    for unit in units.values():
        if unit.get("type") in ("Bolt.CustomEvent",) or (
                unit.get("type") == "Bolt.TriggerCustomEvent"):
            joined = " ".join(unit.get("values", []))
            if "Click" in joined:
                click = True
        if unit.get("member") == "LoadScene" or unit.get("type", "").endswith("LoadScene"):
            for text in unit.get("values", []):
                scene = text.split("=", 1)[-1]
                if SCENEISH.match(scene):
                    loads.append(scene)
    if not units and not loads:
        return None
    return {
        "offset": offset,
        "owner": owner,
        "click": click,
        "units": len(units),
        "loads": sorted(set(loads)),
        "types": sorted({u.get("type", "") for u in units.values()}),
        "elements": list(units.values()),
        "control": control,
        "value": value,
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game", help="game folder (default: auto-detect)")
    ap.add_argument("--out", default=str(ROOT / "data" / "bolt-graphs.json"))
    args = ap.parse_args()

    game = find_game(args.game)
    if game is None:
        print("Game folder not found; pass --game DIR.", file=sys.stderr)
        return 1
    assets = sorted((game / "The Life and Suffering of Sir Brante_Data").glob("resources.assets"))
    if not assets:
        print("resources.assets not found under %s" % game, file=sys.stderr)
        return 1

    graphs = []
    for path in assets:
        buf = path.read_bytes()
        for offset, blob in iter_blobs(buf):
            # the object name precedes the JSON string in the asset
            tail = buf[max(0, offset - 64):offset]
            names = re.findall(rb"[\x20-\x7e]{4,}", tail)
            owner = names[-1].decode("ascii", "replace") if names else ""
            graph = describe(offset, blob, owner)
            if graph:
                graphs.append(graph)
        print("%s: %d graph(s)" % (path.name, len(graphs)))

    out = pathlib.Path(args.out)
    out.write_text(json.dumps({
        "schema": 1,
        "note": "Raw Bolt graphs: units, wiring and LoadScene targets. "
                "Branch interpretation is not done here.",
        "graphs": graphs,
    }, ensure_ascii=False), encoding="utf-8")

    with_click = sum(1 for g in graphs if g["click"])
    with_load = sum(1 for g in graphs if g["loads"])
    print("graphs: %d (with a Click event: %d, with LoadScene: %d)"
          % (len(graphs), with_click, with_load))
    print("wrote %s" % out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
