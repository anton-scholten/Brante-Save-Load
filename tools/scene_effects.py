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

A scene can change the hero by itself when it opens, before any answer
(button 0 in the TSV):

- its `ConsequenceSceneController` (the answer-less event and final scenes:
  trigger events such as `052.N5.010_Trigger5`, `Insurrection_Incident_N`,
  the chapter finals) applies `ParameterValue`/`RelationValue`/`StatusValue`
  lists, each item {PPtr, int}, when the scene is shown (`CalculateParams`);
  a `ConsequenceScript` does the same on `Start`;
- an interlude: the scene's `SceneStateMachine` has events whose UnityEvent
  calls `PopupEvent.ShowPopup(<Interlude asset>)` (all of them in the
  PREGAME list, i.e. before the answers). The `Interlude` ScriptableObject
  (resources.assets) carries the same three lists, applied when the pop-up
  is closed. One with `EnableConvertationParams` and no lists (the chapter-3
  start's) converts the earlier stats into the chapter-3 skills
  (`ParametersManager.CalculateChapterParams`) if they are all still 0: a
  `C Convert 0` row.

An `N <key> 0` row gives the key of an answer's name (the button's short
text, `ParameterButtonChanger.ButtonTextKey`) where it is not
`<scene>_Button_<N>` (13 of 661: another scene's key, or `Button4`).

A `K <key> 0` row (button 0) gives the title key of an answer-less event
scene whose `ConsequenceSceneController.TitleKey` is not `<scene>_Title`
(an alternative version showing another scene's title).

`Case` assets (the "case of the year" window) also carry such lists, but
nothing in the shipped game opens that window (only the developer window).

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
import re
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


def read_interludes(data_dir, script_id, names):
    """path id -> (asset name, [params, relations, statuses], convert) for
    every Interlude asset in resources.assets."""
    import UnityPy
    env = UnityPy.load(str(data_dir / "resources.assets"))
    out = {}
    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        data = o.get_raw_data()
        if struct.unpack_from("<iq", data, 16)[1] != script_id:
            continue
        rd = Reader(data, 28)
        name = rd.string()
        rd.string()                             # TitleKey
        for _ in range(rd.count()):             # TextBlocks
            rd.pptr()                           # Character
            rd.string()                         # SpeechByKey
            rd.boolean()                        # UseTextBlockSpecialSceneries
            # TextBlockSpecialSceneries: IsDeathTrigger, IsShowed, UseSFX + SFX,
            # ChangeSceneMusic + Music, ChangePicture + ScenePicture,
            # ContainAPoem, ShowChangeHUD + HudVisualEffect, HideChangeHUD,
            # ShowParameter + Parameter, ChangeTitle + Key, ShowCharacter + Character
            rd.boolean(); rd.boolean(); rd.boolean(); rd.pptr()
            rd.boolean(); rd.pptr(); rd.boolean(); rd.pptr()
            rd.boolean(); rd.boolean(); rd.pptr(); rd.boolean()
            rd.boolean(); rd.pptr(); rd.boolean(); rd.string(); rd.boolean(); rd.pptr()
        rd.pptr()                               # Image
        rd.boolean()                            # IsOpened
        rd.pptr()                               # InterludeAudioClip
        lists = []
        for _ in range(3):
            items = []
            for _ in range(rd.count()):
                f2, p2 = rd.pptr()
                items.append((names.get(p2, str(p2)) if f2 == 0 else str(p2), rd.i32()))
            lists.append(items)
        convert = rd.boolean()                  # EnableConvertationParams
        if rd.p != len(data):
            print("  interlude %s: %d byte(s) left over" % (name, len(data) - rd.p))
        out[o.path_id] = (name, lists, convert)
    return out


def interludes_shown(data, externals, interludes):
    """The interludes a SceneStateMachine shows: every event of its six state
    lists whose UnityEvent calls ShowPopup with an Interlude asset."""
    rd = Reader(data, 28)
    rd.string()                                 # m_Name
    shown = []
    for _ in range(6):                          # ShowAnimation ... HideAnimation
        for _ in range(rd.count()):
            for _ in range(6):
                rd.boolean()                    # ItsPopupEvent ... ItsCustomEvent
            rd.i32()                            # PopupType
            rd.pptr(); rd.pptr(); rd.pptr()     # Character, NewPortret, SceneAudioClip
            for _ in range(rd.count()):         # Action.m_PersistentCalls.m_Calls
                rd.pptr()                       # m_Target
                method = rd.string()
                rd.i32()                        # m_Mode
                af, ap = rd.pptr()              # m_ObjectArgument
                rd.string(); rd.i32(); rd.i32(); rd.string(); rd.boolean()
                rd.i32()                        # m_CallState
                if method == "ShowPopup" and af > 0 \
                        and externals[af - 1].endswith("resources.assets") and ap in interludes:
                    shown.append(interludes[ap])
            rd.string()                         # m_TypeName
    return shown


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
    scene_scripts = set()               # stat changes of the scene itself (button 0)
    scripts_names = {}
    state_machine = None                # SceneStateMachine: the interludes it shows
    interlude_script = None
    for o in scripts.objects:
        if o.type.name == "MonoScript":
            try:
                name = o.read().m_ClassName
            except Exception:
                continue
            if name == "ParameterButtonChanger":
                button_script = o.path_id
            elif name in ("ConsequenceSceneController", "ConsequenceScript"):
                scene_scripts.add(o.path_id)
                scripts_names[o.path_id] = name
            elif name == "SceneStateMachine":
                state_machine = o.path_id
            elif name == "Interlude":
                interlude_script = o.path_id
    if button_script is None:
        print("ParameterButtonChanger script not found", file=sys.stderr)
        return 1
    names = {}
    bg.load_objects(data_dir / "resources.assets", names)
    interludes = read_interludes(data_dir, interlude_script, names)

    effects = {}
    title_keys = {}
    name_keys = {}
    for index, path in enumerate(scenes):
        scene = path.rsplit("/", 1)[-1].rsplit(".", 1)[0]
        # answers: story scenes only (01.05.01, 051.0...); a scene's own
        # changes: any scene (chapter starts, incidents, finals)
        story = scene[:1].isdigit()
        level = data_dir / ("level%d" % index)
        if not level.is_file():
            continue
        env = UnityPy.load(str(level))
        f = list(env.files.values())[0]
        externals = [e.path for e in f.externals]
        game_objects = {}
        for o in env.objects:
            if o.type.name == "GameObject":
                game_objects[o.path_id] = o.read_typetree()

        def ref(file_id, path_id):
            if file_id > 0 and externals[file_id - 1].endswith("resources.assets"):
                return names.get(path_id, str(path_id))
            return str(path_id)

        for o in env.objects:
            if o.type.name != "MonoBehaviour":
                continue
            data = o.get_raw_data()
            fid, pid = struct.unpack_from("<iq", data, 16)
            if fid <= 0 or not externals[fid - 1].endswith("globalgamemanagers.assets"):
                continue
            if pid == state_machine:
                try:
                    shown = interludes_shown(data, externals, interludes)
                except (struct.error, ValueError, IndexError) as exc:
                    print("  %s: unreadable scene state machine (%s)" % (scene, exc))
                    continue
                for name, lists, convert in shown:
                    entry = effects.setdefault(scene, {}).setdefault("0", {})
                    for kind, items in zip(("P", "R", "S"), lists):
                        if items:
                            entry.setdefault(kind, []).extend(items)
                    if convert and not any(lists):
                        entry["C"] = [("Convert", 0)]
                continue
            if pid in scene_scripts:
                try:
                    rd = Reader(data, 28)
                    rd.string()                     # m_Name
                    lists = []
                    for _ in range(3):              # parameters, relations, statuses
                        items = []
                        for _ in range(rd.count()):
                            f2, p2 = rd.pptr()
                            items.append((ref(f2, p2), rd.i32()))
                        lists.append(items)
                    title_key = ""
                    if scripts_names.get(pid) == "ConsequenceSceneController":
                        rd.pptr()                   # ParamsGenerator
                        title_key = rd.string()     # TitleKey
                except (struct.error, ValueError, IndexError) as exc:
                    print("  %s: unreadable scene changes (%s)" % (scene, exc))
                    continue
                if title_key and title_key != scene + "_Title":
                    title_keys[scene] = title_key
                if any(lists):
                    entry = effects.setdefault(scene, {}).setdefault("0", {})
                    for kind, items in zip(("P", "R", "S"), lists):
                        if items:
                            entry.setdefault(kind, []).extend(items)
                continue
            if pid != button_script or not story:
                continue
            try:
                rd = Reader(data, 28)
                rd.string()                         # m_Name
                rd.pptr()                           # MainContainer
                text_key = rd.string()              # ButtonTextKey: the answer's name
                button = rd.i32()
                # The serialized ButtonIndex is 0 on a few buttons whose
                # ButtonDescriptionN name still has the right number (nine
                # buttons in six scenes), so the name wins when it has one.
                go_id = struct.unpack_from("<q", data, 4)[0]
                go_name = game_objects.get(go_id, {}).get("m_Name", "") or ""
                match = re.match(r"ButtonDescription(\d+)$", go_name)
                if match:
                    button = int(match.group(1))
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
            if text_key:
                name_keys[(scene, button)] = text_key
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

    # the title of an event scene that changes something, where it is another
    # scene's (an alternative version): the tree looks it up by this key
    for scene, key in title_keys.items():
        own = effects.get(scene, {}).get("0", {})
        if any(own.get(k) for k in ("P", "R", "S")):
            own["K"] = [(key, 0)]

    # an answer's name (the button's short text) whose key is not
    # <scene>_Button_<N>: another scene's, or "Button4" without the "_"
    odd = sorted((k, v) for k, v in name_keys.items() if v != "%s_Button_%d" % k)
    for (scene, button), key in odd:
        effects.setdefault(scene, {}).setdefault(str(button), {})["N"] = [(key, 0)]
    print("answer name keys: %d, %d not <scene>_Button_N" % (len(name_keys), len(odd)))

    doc = {"schema": 1,
           "source": "ParameterButtonChanger (button 0: ConsequenceSceneController/"
                     "ConsequenceScript) in the scene files (tools/scene_effects.py)",
           "scenes": dict(sorted(effects.items()))}
    out = ROOT / "data" / "scene-effects.json"
    out.write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")
    lines = []
    for scene, buttons in sorted(effects.items()):
        for button, kinds in sorted(buttons.items(), key=lambda kv: int(kv[0])):
            for kind in ("P", "R", "S", "C", "K", "N"):
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
