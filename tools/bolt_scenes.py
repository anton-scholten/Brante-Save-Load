#!/usr/bin/env python3
"""List the game's scene catalogue per chapter, from the Bolt chapter macros.

Each chapter macro (`ChildhoodChapter`, `TeenChapter`, `YouthChapter`,
`PeaceChapter`, …) has one `SuperUnit` per scene, and the referenced macro is
named after the scene (`01.05.01`, `030.040.030_Preparation`,
`ChildhoodFinishScreen`, …). That gives the game's own scene list, including the
non-numeric scenes the ids alone would miss.

  python3 tools/bolt_scenes.py           # -> data/scene-catalog.json

Note: the catalogue is a SET, not an order. Scene ids mostly follow the story
(Birth `01.01.01` …), but variants such as `01.02.01_2` (Hide and Seek) come
*before* `01.02.01` (The Great Descent), so sorting by id is wrong there. The
true order has to come from traces or from the story data.
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


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game", help="game folder (default: auto-detect)")
    ap.add_argument("--out", default=str(ROOT / "data" / "scene-catalog.json"))
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

    objects = bg.load_objects(find_asset(game))
    chapters = {}
    for entry in objects.values():
        m = CHAPTER.match(entry["name"])
        if not m:
            continue
        names = []
        for pid in entry["macros"]:
            name = objects.get(pid, {}).get("name")
            if name:
                names.append(name)
        chapters[m.group(1)] = sorted(set(names))

    doc = {
        "schema": 1,
        "source": "Bolt chapter macros: one SuperUnit per scene (tools/bolt_graph.py)",
        "note": "Sets, not order: ids are a hint, but variants (01.02.01_2) break id order.",
        "chapters": chapters,
    }
    out = pathlib.Path(args.out)
    out.write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")

    for name, scenes in chapters.items():
        print("%-10s %2d scene macro(s)" % (name, len(scenes)))
        print("   " + ", ".join(scenes[:8]) + (" …" if len(scenes) > 8 else ""))
    print("wrote %s" % out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
