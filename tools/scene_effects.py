#!/usr/bin/env python3
"""Extract what every answer does to the hero (stat, relation and character
status changes) and what it requires, for every story scene.

Each answer is a `ParameterButtonChanger` MonoBehaviour inside the scene file
(`levelN`, N = the scene's index in the build list). The release build ships
no type trees, so the fields are read raw, in the order of the decompiled
class:

    m_GameObject PPtr | m_Enabled (4) | m_Script PPtr | m_Name string
    MainContainer PPtr | ButtonTextKey string | ButtonIndex int
    Parameters  [ {Parameter PPtr, int Value} ]      stat changes
    Relations   [ {Character PPtr, int Value} ]      relation changes
    Statuses    [ {Character PPtr, int CharacterStatus} ]
    UnlockSfx, ConditionIcon, InactiveConditionIcon, ActiveConditionIcon PPtrs
    DifficultConditions bool                         the OR group is in use
    OR_Condition {                                   one of these must hold
        ByParameter [ {Letter, {Parameter, Operations, SecondValue,
                       MaxValue, bool ItsDiapason}} ]
        ByRelation  [ {Letter, {Character, Operations, int Value}} ]
        ByStatus    [ {Letter, {Character, bool Not, CharacterStatus}} ]
        ByObjective [ {Letter, {bool Not, Objective}} ]
        Expression string                            the letters, space-separated
    }
    Condition             [ {Parameter, Operations, int} ]   all must hold
    ConditionByRelations  [ {Character, Operations, int} ]
    ConditionsByStatus    [ {Character, bool Not, CharacterStatus} ]
    ConditionByObjectives [ {Objective, bool Not} ]
    Checks, ChecksByStatus, ChecksByRelation, ChecksByObjective  List<bool>
    ChecksByDifficultConditions bool
    NeedCheckPossible bool                           requirements apply at all

(bools are one byte, padded to 4.) Operations: 0 More, 1 Less, 2 Equal,
3 MoreEqual, 4 LessEqual, 5 Not (strict comparisons, ParametersManager.Compare).

(a PPtr is fileID int32 + pathID int64; strings are length-prefixed and
4-byte aligned). The referenced Parameter/Character ScriptableObjects live in
resources.assets and are written out by name; the mod looks them up at
runtime for their icon and localised name.

  python3 tools/scene_effects.py      # -> data/scene-effects.json + mod TSV

TSV shipped with the mod (one line per effect or requirement):
    <scene>\\t<button>\\t<kind>\\t<object name>\\t<value>[\\t<op>\\t<group>]
Effects: P = stat change, R = relation change, S = status change (value is
the CharacterStatus number). Requirements (only where NeedCheckPossible):
Q = stat, W = relation (op = Operations), T = status (value = CharacterStatus,
op = 1 for "not"), O = objective (op = 1 for "not"); group is "and" for the
plain lists, "or" for the OR group.
"""

import argparse
import json
import pathlib
import struct
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import bolt_graph as bg  # noqa: E402
import gamedir  # noqa: E402


class Reader:
    def __init__(self, data, pos):
        self.d = data
        self.p = pos

    def i32(self):
        v = struct.unpack_from("<i", self.d, self.p)[0]
        self.p += 4
        return v

    def boolean(self):
        v = self.d[self.p] != 0
        self.p = (self.p + 1 + 3) & ~3
        return v

    def pptr(self):
        f, pid = struct.unpack_from("<iq", self.d, self.p)
        self.p += 12
        return f, pid

    def string(self):
        text, self.p = string_at(self.d, self.p)
        return text

    def count(self):
        n = self.i32()
        if n < 0 or n > 256:
            raise ValueError("implausible array length %d" % n)
        return n

    def bools(self):
        n = self.count()
        self.p = (self.p + n + 3) & ~3


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
    game = gamedir.find_game(args.game)
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
                rd = Reader(data, 28)
                rd.string()                         # m_Name
                rd.pptr()                           # MainContainer
                rd.string()                         # ButtonTextKey
                button = rd.i32()
                lists = []
                for _ in range(3):                  # Parameters, Relations, Statuses
                    items = []
                    for _ in range(rd.count()):
                        f2, p2 = rd.pptr()
                        items.append((ref(f2, p2), rd.i32()))
                    lists.append(items)
                for _ in range(4):                  # sfx + three condition icons
                    rd.pptr()
                use_or = rd.boolean()
                ors = []
                for _ in range(rd.count()):         # OR: by parameter
                    letter = rd.string()
                    obj = ref(*rd.pptr())
                    op, value = rd.i32(), rd.i32()
                    rd.i32()                        # MaxValue (unused by the game)
                    rd.boolean()                    # ItsDiapason
                    ors.append((letter, ("Q", obj, value, op)))
                for _ in range(rd.count()):         # OR: by relation
                    letter = rd.string()
                    obj = ref(*rd.pptr())
                    op, value = rd.i32(), rd.i32()
                    ors.append((letter, ("W", obj, value, op)))
                for _ in range(rd.count()):         # OR: by status
                    letter = rd.string()
                    obj = ref(*rd.pptr())
                    neg = rd.boolean()
                    ors.append((letter, ("T", obj, rd.i32(), int(neg))))
                for _ in range(rd.count()):         # OR: by objective
                    letter = rd.string()
                    neg = rd.boolean()
                    ors.append((letter, ("O", ref(*rd.pptr()), 0, int(neg))))
                expression = rd.string().split()
                ands = []
                for _ in range(rd.count()):         # Condition
                    obj = ref(*rd.pptr())
                    op, value = rd.i32(), rd.i32()
                    ands.append(("Q", obj, value, op))
                for _ in range(rd.count()):         # ConditionByRelations
                    obj = ref(*rd.pptr())
                    op, value = rd.i32(), rd.i32()
                    ands.append(("W", obj, value, op))
                for _ in range(rd.count()):         # ConditionsByStatus
                    obj = ref(*rd.pptr())
                    neg = rd.boolean()
                    ands.append(("T", obj, rd.i32(), int(neg)))
                for _ in range(rd.count()):         # ConditionByObjectives
                    obj = ref(*rd.pptr())
                    ands.append(("O", obj, 0, int(rd.boolean())))
                for _ in range(4):
                    rd.bools()                      # Checks...
                rd.boolean()                        # ChecksByDifficultConditions
                need_check = rd.boolean()
            except (struct.error, ValueError, IndexError) as exc:
                print("  %s: unreadable button (%s)" % (scene, exc))
                continue
            reqs = []
            if need_check:
                reqs = [r + ("and",) for r in ands]
                if use_or:
                    reqs += [r + ("or",) for letter, r in ors if letter in expression]
            if not any(lists) and not reqs:
                continue
            entry = effects.setdefault(scene, {}).setdefault(str(button), {})
            for kind, items in zip(("P", "R", "S"), lists):
                if items:
                    entry[kind] = items
            if reqs:
                entry["requires"] = [list(r) for r in reqs]

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
            for kind, name, value, op, group in kinds.get("requires", []):
                lines.append("%s\t%s\t%s\t%s\t%d\t%d\t%s\n"
                             % (scene, button, kind, name, value, op, group))
    tsv = "".join(lines)
    for target in (ROOT / "data" / "scene-effects.tsv",
                   ROOT / "Brante-Save-Load" / "scene-effects.tsv"):
        target.write_text(tsv, encoding="utf-8")
        print("wrote %s" % target)
    reqs = sum(len(k.get("requires", [])) for b in effects.values() for k in b.values())
    print("scenes: %d, lines: %d (requirements: %d)" % (len(effects), len(lines), reqs))
    return 0


if __name__ == "__main__":
    sys.exit(main())
