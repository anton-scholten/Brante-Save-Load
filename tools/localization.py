#!/usr/bin/env python3
"""Extract the game's localisation table and the per-scene text set.

The game translates through the I2 `LanguageSourceAsset` "I2Languages" in
`resources.assets`: its serialized `mTerms` list (17552 terms), each
`Term, TermType, Description, Languages[14], Flags[], Languages_Touch[]`.
The same file also embeds a CSV starting `Key,Type,Desc,Russian,English,
Chinese`, but that is an old import: it lacks terms the game shows
(`052.N12.010_Trigger12_Button_3`, `051.060.030_Mortally_ButtonDescription4`)
and spells `054.100.090_MIlanid` where the scene and the live terms have
`Milanid`. Only the serialized terms are read.

So every scene's title and every answer's text is available **without playing**.
This tool writes:

  data/localization.csv   the live terms as Key,Type,Desc,Russian,English,Chinese
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
KEY = re.compile(r"^(\d{2,3}\.\d{2,3}\.\d{2,3}(?:_\d+)?)(?:_(Title|ButtonDescription\d+|ConsequenceText_\d+_\d+))?$")


NAME = b"\x0b\x00\x00\x00I2Languages\x00"
# language order of the terms' Languages array (mLanguages, after the terms)
RUSSIAN, ENGLISH, CHINESE = 0, 1, 5


def extract_terms(asset):
    """[(key, [text per language])] from the serialized I2Languages source.

    Layout after the object's name: three ints, then mTerms (count, then per
    term: Term, TermType, Description, Languages[], Flags byte[],
    Languages_Touch[]). Unity aligns every string and byte array to 4."""
    data = asset.read_bytes()
    at = data.find(NAME)
    if at < 0:
        raise SystemExit("I2Languages source not found")
    pos = at + 16 + 3 * 4

    def i32():
        nonlocal pos
        value = struct.unpack_from("<i", data, pos)[0]
        pos += 4
        return value

    def string():
        nonlocal pos
        n = i32()
        if n < 0 or n > 1 << 20:
            raise SystemExit("I2 terms: bad string length %d at %d" % (n, pos))
        text = data[pos:pos + n].decode("utf-8", "replace")
        pos = (pos + n + 3) & ~3
        return text

    count = i32()
    if not 1000 < count < 200000:
        raise SystemExit("I2 terms: implausible count %d" % count)
    terms = []
    for _ in range(count):
        key = string()
        i32()                                   # TermType
        string()                                # Description
        langs = [string() for _ in range(i32())]
        flags = i32()
        pos = (pos + flags + 3) & ~3
        for _ in range(i32()):                  # Languages_Touch
            string()
        terms.append((key, langs))
    return terms


def to_csv(terms):
    out = io.StringIO()
    w = csv.writer(out, lineterminator="\n")
    w.writerow(["Key", "Type", "Desc", "Russian", "English", "Chinese"])
    for key, langs in terms:
        row = [langs[i] if i < len(langs) else "" for i in (RUSSIAN, ENGLISH, CHINESE)]
        w.writerow([key, "Text", ""] + row)
    return out.getvalue()


def parse_terms(terms):
    """key -> english text."""
    return {key: (langs[ENGLISH] if len(langs) > ENGLISH else "") for key, langs in terms}



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

    terms = extract_terms(asset)
    (ROOT / "data" / "localization.csv").write_text(to_csv(terms), encoding="utf-8")
    table = parse_terms(terms)
    scenes = build_scenes(table, catalogue())

    out = {
        "schema": 1,
        "source": "I2Languages terms (LanguageSourceAsset) in resources.assets",
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
