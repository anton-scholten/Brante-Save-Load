#!/usr/bin/env python3
"""Extract the game's localisation table and the per-scene text set.

The I2 localisation data is a plain CSV inside `resources.assets`, preceded by
its byte length and starting with `Key,Type,Desc,Russian,English,Chinese`:

    01.05.01_Title,Text,,"Плач младенца",The Newborn’s Cry,
    01.06.01_ButtonDescription1,Text,,"...",You quietly say that it was an accident.,

So every scene's title and every answer's text is available **without playing**.
This tool writes:

  data/localization.csv   the raw table (as extracted)
  data/scene-texts.json   scene id -> {title, buttons{name: text}, consequences[]}

Usage:  python3 tools/localization.py [--game DIR]
"""

import argparse
import csv
import io
import json
import pathlib
import re
import struct
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import gamedir  # noqa: E402
HEADER = b"Key,Type,Desc,Russian,English,Chinese"
KEY = re.compile(r"^(\d{2,3}\.\d{2,3}\.\d{2,3}(?:_\d+)?)(?:_(Title|ButtonDescription\d+|ConsequenceText_\d+_\d+))?$")


def extract_csv(asset):
    data = asset.read_bytes()
    at = data.find(HEADER)
    if at < 0:
        raise SystemExit("localisation header not found")
    length = struct.unpack_from("<i", data, at - 4)[0]
    if length <= 0 or at + length > len(data):
        raise SystemExit("localisation length looks wrong: %d" % length)
    return data[at:at + length].decode("utf-8", "replace")


def parse(csv_text):
    """key -> english text (column 4)."""
    table = {}
    for row in csv.reader(io.StringIO(csv_text, newline="")):
        if len(row) < 5 or row[0] in ("Key", "---"):
            continue
        table[row[0]] = row[4]
    return table


def catalogue():
    """Scene names from data/scene-flow.json (tools/bolt_story.py), longest
    first so "051.000.020_Arrive_Inquisitor" wins over a shorter prefix."""
    path = ROOT / "data" / "scene-flow.json"
    if not path.is_file():
        return []
    doc = json.loads(path.read_text(encoding="utf-8"))
    names = {r["scene"] for r in doc.get("scenes", [])}
    return sorted(names, key=len, reverse=True)


def build_scenes(table, known):
    scenes = {}
    for key, text in table.items():
        scene, part = "", ""
        for name in known:                       # catalogue names win
            if key.startswith(name + "_"):
                scene, part = name, key[len(name) + 1:]
                break
        if not scene:
            m = KEY.match(key)
            if not m:
                continue
            scene, part = m.group(1), m.group(2) or ""
        entry = scenes.setdefault(scene, {"title": "", "buttons": {}, "consequences": []})
        if part == "Title":
            entry["title"] = text
        elif part.startswith("ButtonDescription"):
            # a few keys are "ButtonDescription_1"; the button object in the
            # scene is always "ButtonDescription1", and that is the name the
            # mod matches answers by
            part = re.sub(r"^ButtonDescription_(\d+)$", r"ButtonDescription\1", part)
            entry["buttons"].setdefault(part, text)
        elif part.startswith("ConsequenceText"):
            if text and text not in entry["consequences"]:
                entry["consequences"].append(text)
    return scenes


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game", help="game folder (default: auto-detect)")
    args = ap.parse_args()

    game = gamedir.find_game(args.game)
    if not game:
        print("Game folder not found; pass --game DIR.", file=sys.stderr)
        return 1
    asset = game / "The Life and Suffering of Sir Brante_Data/resources.assets"

    csv_text = extract_csv(asset)
    (ROOT / "data" / "localization.csv").write_text(csv_text, encoding="utf-8")
    table = parse(csv_text)
    scenes = build_scenes(table, catalogue())

    out = {
        "schema": 1,
        "source": "I2 localisation CSV embedded in resources.assets",
        "terms": len(table),
        "scenes": dict(sorted(scenes.items())),
    }
    path = ROOT / "data" / "scene-texts.json"
    path.write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")

    with_buttons = sum(1 for s in scenes.values() if s["buttons"])
    print("terms: %d, scenes: %d (with answers: %d)"
          % (len(table), len(scenes), with_buttons))
    for scene in list(sorted(scenes))[:3]:
        s = scenes[scene]
        print("  %-14s %-28s %d answer(s)" % (scene, s["title"][:28], len(s["buttons"])))
    # The mod ships a flat TSV: Unity's JsonUtility cannot do dictionaries, and
    # this is trivial to parse line by line.
    def esc(text):
        return (text or "").replace("\\", "\\\\").replace("\t", "\\t").replace("\n", "\\n").replace("\r", "")

    lines = []
    for scene in sorted(scenes):
        entry = scenes[scene]
        if not entry["title"] and not entry["buttons"]:
            continue
        lines.append("S\t%s\t%s" % (scene, esc(entry["title"])))
        for name in sorted(entry["buttons"]):
            lines.append("B\t%s\t%s\t%s" % (scene, name, esc(entry["buttons"][name])))
    tsv = "\n".join(lines) + "\n"
    for target in (ROOT / "data" / "scene-texts.tsv", game / "Brante-Save-Load" / "scene-texts.tsv"):
        try:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(tsv, encoding="utf-8")
            print("wrote %s" % target)
        except OSError as exc:
            print("could not write %s: %s" % (target, exc))
    print("wrote %s" % path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
