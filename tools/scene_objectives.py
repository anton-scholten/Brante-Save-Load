#!/usr/bin/env python3
"""Extract the story objectives a scene or an answer unlocks.

`KeyChapterParametersController.EarnObjective` is the only gameplay write to
`Objective.Unlocked`. It is called from three scene MonoBehaviours:

  SceneConsequenceGenerator   list of objectives earned after an answer
  UniversalParametersGenerator list earned by a scene with no answers
  ConsequenceNextButton       one objective earned by a consequence "Next"

The release build ships no type trees for MonoBehaviours, so the fields are read
raw in the decompiled declaration order (see docs/FINDINGS.md). The owning
answer is found by walking the generator's GameObject ancestors and matching a
ParameterButtonChanger's `Consequence` PPtr; the button number comes from the
answer GameObject's `ButtonDescriptionN` name (the serialized ButtonIndex is 0
on nine buttons).

  python3 tools/scene_objectives.py   # -> data/scene-objectives.{json,tsv}

TSV shipped with the mod (objective enum is what SaveLoadHelper stores):
    <scene>\\t<button>\\t<order>\\t<objective asset name>\\t<enum value>
button 0 = the scene itself; N = answer ButtonDescriptionN.
"""

import argparse
import json
import pathlib
import re
import struct
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import gamedir  # noqa: E402


class Reader:
    def __init__(self, data, pos):
        self.d = data
        self.p = pos

    def i32(self):
        value = struct.unpack_from("<i", self.d, self.p)[0]
        self.p += 4
        return value

    def boolean(self):
        value = self.d[self.p] != 0
        self.p = (self.p + 1 + 3) & ~3
        return value

    def pptr(self):
        file_id, path_id = struct.unpack_from("<iq", self.d, self.p)
        self.p += 12
        return file_id, path_id

    def string(self):
        n = struct.unpack_from("<i", self.d, self.p)[0]
        text = self.d[self.p + 4:self.p + 4 + n].decode("utf-8", "replace")
        self.p = (self.p + 4 + n + 3) & ~3
        return text

    def count(self):
        n = self.i32()
        if n < 0 or n > 400:
            raise ValueError("implausible array length %d" % n)
        return n

    def bools(self):
        n = self.count()
        self.p = (self.p + n + 3) & ~3


def scene_list(data_dir):
    import UnityPy
    gg = UnityPy.load(str(data_dir / "globalgamemanagers"))
    return [o.read_typetree() for o in gg.objects
            if o.type.name == "BuildSettings"][0]["scenes"]


def script_names(data_dir):
    import UnityPy
    env = UnityPy.load(str(data_dir / "globalgamemanagers.assets"))
    names = {}
    for obj in env.objects:
        if obj.type.name == "MonoScript":
            try:
                names[obj.path_id] = obj.read().m_ClassName
            except Exception:
                pass
    return names


def objective_assets(data_dir, scripts):
    """path id -> (asset name, ObjectiveEnum int)."""
    import UnityPy
    env = UnityPy.load(str(data_dir / "resources.assets"))
    f = list(env.files.values())[0]
    externals = [e.path for e in f.externals]
    assets = {}
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        data = obj.get_raw_data()
        file_id, path_id = struct.unpack_from("<iq", data, 16)
        if not (file_id > 0 and file_id <= len(externals)
                and externals[file_id - 1].endswith("globalgamemanagers.assets")):
            continue
        if scripts.get(path_id) != "Objective":
            continue
        n = struct.unpack_from("<i", data, 28)[0]
        name = data[32:32 + n].decode("utf-8", "replace")
        p = (28 + 4 + n + 3) & ~3
        p += 4                                  # _chapter
        p = (p + 1 + 3) & ~3                    # _peaceOrWar
        n = struct.unpack_from("<i", data, p)[0]  # _post
        p = (p + 4 + n + 3) & ~3
        p += 4                                  # _order
        p += 4                                  # _category
        p += 12 + 12                            # _picture, _icon
        enum_value = struct.unpack_from("<i", data, p)[0]
        assets[obj.path_id] = (name, enum_value)
    return assets


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
    scripts = script_names(data_dir)
    assets = objective_assets(data_dir, scripts)
    classes = {"SceneConsequenceGenerator", "UniversalParametersGenerator",
               "ConsequenceNextButton", "ParameterButtonChanger"}

    rows = []
    for index, path in enumerate(scene_list(data_dir)):
        scene = path.rsplit("/", 1)[-1].rsplit(".", 1)[0]
        level = data_dir / ("level%d" % index)
        if not level.is_file():
            continue
        env = UnityPy.load(str(level))
        f = list(env.files.values())[0]
        externals = [e.path for e in f.externals]
        game_objects = {}
        transforms = {}
        for obj in env.objects:
            if obj.type.name == "GameObject":
                game_objects[obj.path_id] = obj.read_typetree()
            elif obj.type.name in ("Transform", "RectTransform"):
                transforms[obj.path_id] = obj.read_typetree()

        def transform_of(go_id):
            for c in game_objects.get(go_id, {}).get("m_Component", []):
                t = c["component"]["m_PathID"]
                if t in transforms:
                    return t
            return None

        def chain(go_id):
            out, tr, seen = [], transform_of(go_id), set()
            while tr and tr not in seen:
                seen.add(tr)
                out.append(transforms[tr]["m_GameObject"]["m_PathID"])
                tr = transforms[tr]["m_Father"]["m_PathID"] or None
            return out

        def objective(file_id, path_id):
            if file_id > 0 and file_id <= len(externals) \
                    and externals[file_id - 1].endswith("resources.assets"):
                return assets.get(path_id)
            return None

        generators, nexts, answers = [], [], []
        for obj in env.objects:
            if obj.type.name != "MonoBehaviour":
                continue
            data = obj.get_raw_data()
            file_id, path_id = struct.unpack_from("<iq", data, 16)
            if not (file_id > 0 and file_id <= len(externals)
                    and externals[file_id - 1].endswith("globalgamemanagers.assets")):
                continue
            cls = scripts.get(path_id)
            if cls not in classes:
                continue
            go_id = struct.unpack_from("<iq", data, 0)[1]
            rd = Reader(data, 16)
            rd.pptr()                          # m_Script
            rd.string()                        # m_Name
            if cls == "ConsequenceNextButton":
                show = rd.boolean()
                target = objective(*rd.pptr())
                nexts.append({"go": go_id, "show": show, "objective": target})
                continue
            if cls in ("SceneConsequenceGenerator", "UniversalParametersGenerator"):
                for _ in range(6 if cls == "UniversalParametersGenerator" else 8):
                    rd.pptr()
                show = rd.boolean()
                target = objective(*rd.pptr())
                targets = [objective(*rd.pptr()) for _ in range(rd.count())]
                generators.append({"go": go_id, "show": show,
                                   "objective": target, "objectives": targets})
                continue
            rd.pptr()                          # MainContainer
            key = rd.string()                  # ButtonTextKey
            button = rd.i32()                  # ButtonIndex (0 on some!)
            for _ in range(3):                 # Parameters, Relations, Statuses
                for _ in range(rd.count()):
                    rd.pptr()
                    rd.i32()
            for _ in range(4):                 # sfx + condition icons
                rd.pptr()
            rd.boolean()                       # DifficultConditions
            for _ in range(rd.count()):        # OR by parameter
                rd.string(); rd.pptr(); rd.i32(); rd.i32(); rd.i32(); rd.boolean()
            for _ in range(rd.count()):        # OR by relation
                rd.string(); rd.pptr(); rd.i32(); rd.i32()
            for _ in range(rd.count()):        # OR by status
                rd.string(); rd.pptr(); rd.boolean(); rd.i32()
            for _ in range(rd.count()):        # OR by objective
                rd.string(); rd.boolean(); rd.pptr()
            rd.string()                        # Expression
            for _ in range(rd.count()):        # Condition
                rd.pptr(); rd.i32(); rd.i32()
            for _ in range(rd.count()):        # ConditionByRelations
                rd.pptr(); rd.i32(); rd.i32()
            for _ in range(rd.count()):        # ConditionsByStatus
                rd.pptr(); rd.boolean(); rd.i32()
            for _ in range(rd.count()):        # ConditionByObjectives
                rd.pptr(); rd.boolean()
            for _ in range(4):
                rd.bools()
            rd.boolean(); rd.boolean()          # ChecksByDifficultConditions, NeedCheckPossible
            rd.pptr(); rd.pptr(); rd.boolean(); rd.boolean()
            consequence_f, consequence_p = rd.pptr()
            answers.append({"go": go_id, "button": button, "key": key,
                            "consequence": consequence_p if consequence_f == 0 else None})

        def button_name(go_id):
            for ancestor in chain(go_id):
                name = game_objects.get(ancestor, {}).get("m_Name", "") or ""
                match = re.match(r"ButtonDescription(\d+)$", name)
                if match:
                    return int(match.group(1))
            return None

        def owner(go_id):
            ancestry = set(chain(go_id))
            for answer in answers:
                if answer["consequence"] and answer["consequence"] in ancestry:
                    found = dict(answer)
                    found["button_name"] = button_name(found["go"])
                    return found
            return None

        for generator in generators:
            if not generator["show"]:
                continue
            targets = [generator["objective"]] + generator["objectives"]
            targets = [t for t in targets if t]
            if not targets:
                continue
            answer = owner(generator["go"])
            button = (answer.get("button_name") or answer["button"]) if answer else 0
            rows.append({"scene": scene, "button": button,
                         "key": answer["key"] if answer else "",
                         "objectives": targets})
        for nxt in nexts:
            if not nxt["show"] or not nxt["objective"]:
                continue
            answer = owner(nxt["go"])
            button = (answer.get("button_name") or answer["button"]) if answer else 0
            rows.append({"scene": scene, "button": button,
                         "key": answer["key"] if answer else "",
                         "objectives": [nxt["objective"]]})

    merged = {}
    for row in rows:
        key = (row["scene"], row["button"])
        current = merged.setdefault(key, [])
        for objective in row["objectives"]:
            if objective not in current:
                current.append(objective)

    tsv_rows = []
    json_rows = []
    for (scene, button), objectives in sorted(merged.items()):
        for order, (name, value) in enumerate(objectives):
            tsv_rows.append("%s\t%d\t%d\t%s\t%d\n" % (scene, button, order, name, value))
            json_rows.append({"scene": scene, "button": button, "order": order,
                              "objective": name, "enum": value})
    tsv = "".join(tsv_rows)
    doc = {"schema": 1,
           "source": "Scene objective generators (tools/scene_objectives.py)",
           "objectives": json_rows}
    out_json = ROOT / "data" / "scene-objectives.json"
    out_json.write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")
    for target in (ROOT / "data" / "scene-objectives.tsv",
                   ROOT / "Brante-Save-Load" / "scene-objectives.tsv"):
        target.write_text(tsv, encoding="utf-8")
    print("objective assets: %d, rows: %d, scenes: %d (answer keys: %d, scene-level: %d)"
          % (len(assets), len(tsv_rows), len({k[0] for k in merged}),
             sum(1 for k in merged if k[1] > 0),
             sum(1 for k in merged if k[1] == 0)))
    for target in (out_json, ROOT / "data" / "scene-objectives.tsv",
                   ROOT / "Brante-Save-Load" / "scene-objectives.tsv"):
        print("wrote %s" % target)
    return 0


if __name__ == "__main__":
    sys.exit(main())
