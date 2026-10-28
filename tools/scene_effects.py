#!/usr/bin/env python3
"""Extract what every answer does to the hero: stat changes, relation changes,
and character status changes, for every story scene.

Each answer is a `ParameterButtonChanger` MonoBehaviour inside the scene file
(`levelN`, N = the scene's index in the build list). The release build ships
no type trees, so the fields are read raw, in the order of the decompiled
class:

    m_GameObject PPtr | m_Enabled (4) | m_Script PPtr | m_Name string
    MainContainer PPtr | ButtonTextKey string | ButtonIndex int
    Parameters  [ {Parameter PPtr, int Value} ]      stat changes
    Relations   [ {Character PPtr, int Value} ]      relation changes
    Statuses    [ {Character PPtr, int CharacterStatus} ]

(a PPtr is fileID int32 + pathID int64; strings are length-prefixed and
4-byte aligned). The referenced Parameter/Character ScriptableObjects live in
resources.assets and are written out by name; the mod looks them up at
runtime for their icon and localised name.

  python3 tools/scene_effects.py      # -> data/scene-effects.json + mod TSV

TSV shipped with the mod (one line per effect):
    <scene>\\t<button index>\\t<P|R|S>\\t<object name>\\t<value>
P = parameter (stat) change, R = relation change, S = status (CharacterStatus
as its number).
"""

import argparse
import json
import pathlib
import struct
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import bolt_graph as bg  # noqa: E402
import bolt_story  # noqa: E402


def string_at(data, pos):
    n = struct.unpack_from("<i", data, pos)[0]
    text = data[pos + 4:pos + 4 + n].decode("utf-8", "replace")
    pos = (pos + 4 + n + 3) & ~3
    return text, pos


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game", help="game folder (default: auto-detect)")
    args = ap.parse_args()
    game = pathlib.Path(args.game) if args.game else bolt_story.detect_game()
    if not game:
        print("Game folder not found; pass --game DIR.", file=sys.stderr)
        return 1
    import UnityPy
    data_dir = game / "The Life and Suffering of Sir Brante_Data"

    gg = UnityPy.load(str(data_dir / "globalgamemanagers"))
    scenes = [o.read_typetree() for o in gg.objects if o.type.name == "BuildSettings"][0]["scenes"]
    scripts = UnityPy.load(str(data_dir / "globalgamemanagers.assets"))
    button_script = None
    for o in scripts.objects:
        if o.type.name == "MonoScript":
            try:
                if o.read().m_ClassName == "ParameterButtonChanger":
                    button_script = o.path_id
            except Exception:
                pass
    if button_script is None:
        print("ParameterButtonChanger script not found", file=sys.stderr)
        return 1
    names = {}
    bg.load_objects(data_dir / "resources.assets", names)

    effects = {}
    for index, path in enumerate(scenes):
        scene = path.rsplit("/", 1)[-1].rsplit(".", 1)[0]
        if not scene[:1].isdigit():
            continue                    # story scenes only (01.05.01, 051.0...)
        level = data_dir / ("level%d" % index)
        if not level.is_file():
            continue
        env = UnityPy.load(str(level))
        f = list(env.files.values())[0]
        externals = [e.path for e in f.externals]

        def ref(file_id, path_id):
            if file_id > 0 and externals[file_id - 1].endswith("resources.assets"):
                return names.get(path_id, str(path_id))
            return str(path_id)

        for o in env.objects:
            if o.type.name != "MonoBehaviour":
                continue
            data = o.get_raw_data()
            fid, pid = struct.unpack_from("<iq", data, 16)
            if fid <= 0 or not externals[fid - 1].endswith("globalgamemanagers.assets") \
                    or pid != button_script:
                continue
            try:
                _, pos = string_at(data, 28)        # m_Name
                pos += 12                           # MainContainer
                _, pos = string_at(data, pos)       # ButtonTextKey
                button = struct.unpack_from("<i", data, pos)[0]
                pos += 4
                lists = []
                for _ in range(3):                  # Parameters, Relations, Statuses
                    n = struct.unpack_from("<i", data, pos)[0]
                    pos += 4
                    if n < 0 or n > 64:
                        raise ValueError("implausible array length %d" % n)
                    items = []
                    for _ in range(n):
                        f2, p2, v = struct.unpack_from("<iqi", data, pos)
                        pos += 16
                        items.append((ref(f2, p2), v))
                    lists.append(items)
            except (struct.error, ValueError, IndexError) as exc:
                print("  %s: unreadable button (%s)" % (scene, exc))
                continue
            if not any(lists):
                continue
            entry = effects.setdefault(scene, {}).setdefault(str(button), {})
            for kind, items in zip(("P", "R", "S"), lists):
                if items:
                    entry[kind] = items

    doc = {"schema": 1,
           "source": "ParameterButtonChanger in the scene files (tools/scene_effects.py)",
           "scenes": dict(sorted(effects.items()))}
    out = ROOT / "data" / "scene-effects.json"
    out.write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")
    lines = []
    for scene, buttons in sorted(effects.items()):
        for button, kinds in sorted(buttons.items(), key=lambda kv: int(kv[0])):
            for kind in ("P", "R", "S"):
                for name, value in kinds.get(kind, []):
                    lines.append("%s\t%s\t%s\t%s\t%d\n" % (scene, button, kind, name, value))
    tsv = "".join(lines)
    for target in (ROOT / "data" / "scene-effects.tsv",
                   ROOT / "Brante-Save-Load" / "scene-effects.tsv"):
        target.write_text(tsv, encoding="utf-8")
        print("wrote %s" % target)
    print("scenes with effects: %d, effects: %d" % (len(effects), len(lines)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
