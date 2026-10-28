#!/usr/bin/env python3
"""Extract the story's scene-to-scene edges from the Bolt chapter macros.

The chapter macros (`ChildhoodChapter`, `TeenChapter`, …) wire their per-scene
sub-macros in sequence: each scene macro's `Exit` flows into the next scene
macro's `Enter`/`Flow`. That *is* the transition table the game uses - it even
explains why `01.02.01_2` (Hide and Seek) comes before `01.02.01` (The Great
Descent) although the build lists them the other way round.

  python3 tools/bolt_edges.py          # -> data/scene-edges.json + mod TSV

TSV shipped with the mod (one line per edge):
    <from scene>\\t<to scene>\\t<chapter macro>
"""

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import bolt_graph as bg  # noqa: E402

CHAPTER = re.compile(r"^(\w+)Chapter$")


def find_asset(game):
    return pathlib.Path(game) / "The Life and Suffering of Sir Brante_Data/resources.assets"


def detect_game():
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


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game", help="game folder (default: auto-detect)")
    ap.add_argument("--out", default=str(ROOT / "data" / "scene-edges.json"))
    args = ap.parse_args()

    game = args.game or detect_game()
    if not game:
        print("Game folder not found; pass --game DIR.", file=sys.stderr)
        return 1
    objects = bg.load_objects(find_asset(game))

    edges = []
    chapters = {}
    for entry in objects.values():
        m = CHAPTER.match(entry["name"])
        if not m:
            continue
        graph = bg.Graph(entry, objects)
        label = {}
        for uid, unit in graph.units.items():
            if unit.get("$type") != "Bolt.SuperUnit":
                continue
            idx = (unit.get("nest") or {}).get("macro")
            if isinstance(idx, int) and 0 <= idx < len(entry["macros"]):
                label[uid] = objects.get(entry["macros"][idx], {}).get("name") or ""
        # scene macro -> scene macro, where the source leaves through "Exit"
        chain = []
        for (src, port), dsts in graph.control.items():
            if src not in label or (port or "").lower() != "exit":
                continue
            for dst, _ in dsts:
                if dst in label and label[src] and label[dst]:
                    edges.append({"from": label[src], "to": label[dst],
                                  "chapter": entry["name"]})
                    chain.append((label[src], label[dst]))
        chapters[entry["name"]] = sorted({x for pair in chain for x in pair})

    # de-duplicate (a scene can be wired twice, e.g. switches joining up)
    seen = set()
    unique = []
    for e in edges:
        key = (e["from"], e["to"])
        if key in seen:
            continue
        seen.add(key)
        unique.append(e)

    doc = {
        "schema": 1,
        "source": "Bolt chapter macros: each scene SuperUnit's Exit -> next scene's Enter/Flow",
        "edges": unique,
    }
    path = pathlib.Path(args.out)
    path.write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")

    tsv = "".join("%s\t%s\t%s\n" % (e["from"], e["to"], e["chapter"]) for e in unique)
    # Superseded by tools/bolt_story.py (scene-flow.tsv, which the mod ships);
    # kept for the record of the direct scene -> scene links.
    for target in (ROOT / "data" / "scene-edges.tsv",):
        try:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(tsv, encoding="utf-8")
        except OSError as exc:
            print("could not write %s: %s" % (target, exc))

    print("edges: %d in %d chapter(s)" % (len(unique), len(chapters)))
    for chapter, scenes in sorted(chapters.items()):
        print("  %-18s %d scene(s)" % (chapter, len(scenes)))
    print("wrote %s" % path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
