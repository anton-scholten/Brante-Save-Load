#!/usr/bin/env python3
"""Extract the key chapter parameters a scene or an answer changes.

The game stores story-wide switches in `KeyChapterParametersController`
(`Post`, `TriggerBlock`, `PeaceTree`, ...). The chapter flow branches on them,
so a path prediction must know when a scene or an answer changes one.

Two MonoBehaviours do this, both with an inline `KeyChapterParameterModel`
(`Name`, `Value` strings):

  KeyChapterParametersOnEnableChanger   sets the parameter when its object is
                                        enabled in the scene (button 0 below)
  KeyChapterParametersChanger           sets it when its Button is pressed; the
                                        answer's button index is read from the
                                        ParameterButtonChanger on the same
                                        GameObject (button > 0)

The release build ships no type trees, so the fields are read raw, in the
decompiled order:

    m_GameObject PPtr | m_Enabled (4) | m_Script PPtr | m_Name string
    Parameter { Name string | Value string }

  python3 tools/scene_params.py     # -> data/scene-params.json + mod TSV

TSV shipped with the mod (one line per change):
    <scene>\\t<button>\\t<parameter name>\\t<value>
button 0 = when the scene opens; N = answer with ParameterButtonChanger
ButtonIndex N; -1 = a changer not attached to an answer button.
"""

import argparse
import json
import pathlib
import struct
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import gamedir  # noqa: E402


def string_at(data, pos):
    n = struct.unpack_from("<i", data, pos)[0]
    text = data[pos + 4:pos + 4 + n].decode("utf-8", "replace")
    return text, (pos + 4 + n + 3) & ~3


def script_ids(data_dir):
    """class name -> MonoScript path id in globalgamemanagers.assets."""
    import UnityPy
    env = UnityPy.load(str(data_dir / "globalgamemanagers.assets"))
    wanted = {"KeyChapterParametersChanger", "KeyChapterParametersOnEnableChanger",
              "ParameterButtonChanger"}
    found = {}
    for obj in env.objects:
        if obj.type.name != "MonoScript":
            continue
        try:
            name = obj.read().m_ClassName
        except Exception:
            continue
        if name in wanted:
            found[name] = obj.path_id
    return found


def scene_levels(data_dir):
    """[(scene name, level file)] for story scenes (01.05.01, 051.0...)."""
    import UnityPy
    gg = UnityPy.load(str(data_dir / "globalgamemanagers"))
    build = [o.read_typetree() for o in gg.objects
             if o.type.name == "BuildSettings"][0]["scenes"]
    out = []
    for index, path in enumerate(build):
        scene = path.rsplit("/", 1)[-1].rsplit(".", 1)[0]
        if not scene[:1].isdigit():
            continue
        level = data_dir / ("level%d" % index)
        if level.is_file():
            out.append((scene, level))
    return out


def read_button_index(data):
    """ParameterButtonChanger: its ButtonIndex, after m_Name and the strings."""
    p = 28
    _, p = string_at(data, p)      # m_Name
    p += 12                        # MainContainer PPtr
    _, p = string_at(data, p)      # ButtonTextKey
    return struct.unpack_from("<i", data, p)[0]


def read_change(data):
    """KeyChapterParameterModel inline after m_Name."""
    p = 28
    _, p = string_at(data, p)      # m_Name
    name, p = string_at(data, p)
    value, p = string_at(data, p)
    return name, value


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game", help="game folder (default: auto-detect)")
    args = ap.parse_args()
    game = gamedir.find_game(args.game)
    if not game:
        print("Game folder not found; pass --game DIR.", file=sys.stderr)
        return 1
    import UnityPy
    data_dir = game / "The Life and Suffering of Sir Brante_Data"
    ids = script_ids(data_dir)
    for need in ("KeyChapterParametersChanger", "KeyChapterParametersOnEnableChanger",
                 "ParameterButtonChanger"):
        if need not in ids:
            print("script not found: %s" % need, file=sys.stderr)
            return 1
    by_script = {ids["KeyChapterParametersChanger"]: "button",
                 ids["KeyChapterParametersOnEnableChanger"]: "scene"}

    rows = []
    for scene, level in scene_levels(data_dir):
        env = UnityPy.load(str(level))
        f = list(env.files.values())[0]
        externals = [e.path for e in f.externals]
        # GameObject path id -> answer ButtonIndex, in this scene file
        buttons = {}
        for obj in env.objects:
            if obj.type.name != "MonoBehaviour":
                continue
            data = obj.get_raw_data()
            fid, pid = struct.unpack_from("<iq", data, 16)
            if fid <= 0 or not externals[fid - 1].endswith("globalgamemanagers.assets"):
                continue
            if pid != ids["ParameterButtonChanger"]:
                continue
            try:
                go_f, go_p = struct.unpack_from("<iq", data, 0)
                buttons[go_p] = read_button_index(data)
            except (struct.error, ValueError, IndexError):
                continue
        for obj in env.objects:
            if obj.type.name != "MonoBehaviour":
                continue
            data = obj.get_raw_data()
            fid, pid = struct.unpack_from("<iq", data, 16)
            if fid <= 0 or not externals[fid - 1].endswith("globalgamemanagers.assets"):
                continue
            kind = by_script.get(pid)
            if not kind:
                continue
            try:
                go_f, go_p = struct.unpack_from("<iq", data, 0)
                name, value = read_change(data)
            except (struct.error, ValueError, IndexError):
                continue
            button = 0 if kind == "scene" else buttons.get(go_p, -1)
            if name and value:
                rows.append({"scene": scene, "button": button,
                             "name": name, "value": value})

    rows.sort(key=lambda r: (r["scene"], r["button"], r["name"]))
    doc = {"schema": 1,
           "source": "KeyChapterParameters(OnEnable)Changer components (tools/scene_params.py)",
           "changes": rows}
    out = ROOT / "data" / "scene-params.json"
    out.write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")
    tsv = "".join("%s\t%d\t%s\t%s\n" % (r["scene"], r["button"], r["name"], r["value"])
                  for r in rows)
    for target in (ROOT / "data" / "scene-params.tsv",
                   ROOT / "Brante-Save-Load" / "scene-params.tsv"):
        target.write_text(tsv, encoding="utf-8")
    print("scene parameter changes: %d (%d scenes)" % (len(rows), len({r['scene'] for r in rows})))
    for target in (ROOT / "data" / "scene-params.tsv",
                   ROOT / "Brante-Save-Load" / "scene-params.tsv", out):
        print("wrote %s" % target)
    return 0


if __name__ == "__main__":
    sys.exit(main())
