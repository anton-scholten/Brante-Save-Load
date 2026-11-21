#!/usr/bin/env python3
"""Every localisation term each of the game's scenes uses.

The story guide (tools/story_doc.py) is built from the mod's scene-list.tsv,
which knows the scenes of the chapter flows and those with a title in
scene-texts.tsv. The rest - the true-death pages, the outcome of the revolt,
the provinces' and characters' fates, the cutscenes, the chapter screens -
name their texts in other ways ("FourthDeath_1.Text_1",
"Cutscene_6_RevoltBegins_Text1", "PeaceChapterStart_Prefect_Text") or only
through the terms their components hold. This reads every build scene's
MonoBehaviours and keeps each length-prefixed string that is a term of the
game's I2 table, in the order met.

  python3 tools/scene_terms.py     # -> data/scene-terms.json (needs UnityPy)

    {"scenes": [{"scene": "...", "index": N, "terms": ["...", ...]}, ...]}
"""

import json
import pathlib
import struct
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import gamedir  # noqa: E402
import localization  # noqa: E402
import scene_objectives  # noqa: E402


def strings(data):
    """Every 4-aligned, length-prefixed UTF-8 string in a raw object."""
    out = []
    p = 0
    n = len(data)
    while p + 4 <= n:
        length = struct.unpack_from("<i", data, p)[0]
        if 3 <= length <= 200 and p + 4 + length <= n:
            raw = data[p + 4:p + 4 + length]
            try:
                text = raw.decode("utf-8")
            except UnicodeDecodeError:
                text = None
            if text and text.isprintable():
                out.append(text)
                p = (p + 4 + length + 3) & ~3
                continue
        p += 4
    return out


def main():
    import UnityPy
    game = gamedir.find_game(None)
    if not game:
        print("Game folder not found.", file=sys.stderr)
        return 1
    data_dir = game / "The Life and Suffering of Sir Brante_Data"
    terms = {key for key, _ in localization.extract_terms(data_dir / "resources.assets")}
    result = []
    for index, path in enumerate(scene_objectives.scene_list(data_dir)):
        scene = path.rsplit("/", 1)[-1].rsplit(".", 1)[0]
        level = data_dir / ("level%d" % index)
        found = []
        if level.is_file():
            env = UnityPy.load(str(level))
            for obj in env.objects:
                if obj.type.name != "MonoBehaviour":
                    continue
                for text in strings(obj.get_raw_data()):
                    if text in terms and text not in found:
                        found.append(text)
        result.append({"scene": scene, "index": index, "terms": found})
    out = ROOT / "data" / "scene-terms.json"
    out.write_text(json.dumps({"schema": 1, "scenes": result}, ensure_ascii=False, indent=1),
                   encoding="utf-8")
    print("%d scenes, %d with terms -> %s" % (len(result), sum(1 for r in result if r["terms"]), out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
