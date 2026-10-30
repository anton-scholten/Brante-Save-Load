#!/usr/bin/env python3
"""Flatten the Bolt chapter macros into the story's scene order, all chapters.

How the game picks the next scene (read from the macros, see docs/BRANCHES.md):
every chapter macro is a long flow of scene sub-macros, switches and branches.
Each scene macro is a gate

    if (Turn == T && Season == S && !GetSceneShowStatus(scene)) LoadScene(scene)
    else Exit                       // fall through to whatever comes next

and the chapter flow is entered again after every scene. So the next scene is
the first one, in flow order, whose gate and whose enclosing branches pass.
Chapters 1-3 are mostly straight chains; Peace Time (chapter 4) alternates a
Family track with a job track picked by the key parameter `Post` (Judge /
Inquisitor / Rebel), and The Revolt (chapter 5, macro `Insurrection`) is one
long flow full of conditions on characters, objectives and parameters.

This tool walks every chapter macro depth-first from its entry, descends into
non-scene macros (switch helpers, incidents, start/finish screens), and writes
for every scene: its chapter, position in flow order, time slot, the switch
labels on the way (`Post=Judge`, `PeaceTree=Family`), and the branch conditions
as readable text.

  python3 tools/bolt_story.py      # -> data/scene-flow.json + scene-flow.tsv

TSV shipped with the mod (flow order; one row per scene and switch label set,
because a scene can sit at different positions under different switches):
    <chapter>\\t<order>\\t<scene>\\t<turn>\\t<season>\\t<labels>\\t<conditions>
labels: alternatives joined by `|`, each a `;`-joined set of `Param=Value`
switch labels (key chapter parameters; PeaceTree left out); conditions: the
branch conditions of one path to the scene, joined by `&&` (empty when some
path has none), in the expression form of `Expr`. Empty turn/season = not
gated on time.
"""

import argparse
import json
import re
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import bolt_graph as bg  # noqa: E402
import gamedir  # noqa: E402

# Chapter macros, in story order, with the chapter number the game uses.
CHAPTERS = [
    ("ChildhoodChapter", 1),
    ("TeenChapter", 2),
    ("YouthChapter", 3),
    ("PeaceChapter", 4),
    ("Insurrection", 5),
    ("GameFinals", 6),       # the epilogue: every character's fate (070.*)
]
MAX_STACK = 12


def find_asset(game):
    return pathlib.Path(game) / "The Life and Suffering of Sir Brante_Data/resources.assets"


def short(type_name):
    """'_Scripts.AMVCC.Views.Windows.Destiny.Objective' -> 'Objective'."""
    return (type_name or "").split(",")[0].split(".")[-1]


def quote(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


class Expr:
    """Bolt value ports as expression text the mod can evaluate (SceneFlow.cs):

        expr  := or ; or := and ('||' and)* ; and := unary ('&&' unary)*
        unary := '!' unary | cmp ; cmp := atom (op atom)?
        atom  := '(' expr ')' | Type.Member | Type.Method(args) | number
                 | "string" | @Type'Name' (a Unity object by type and name)
                 | word (an enum value)

    Unity object arguments (objectives, parameters, characters) are stored by
    Bolt as an index into the graph's object-reference array; they come out
    as @Type'Name' so the mod can look the live object up by name."""

    def __init__(self, graph, names, gate=False):
        self.g = graph
        self.names = names
        # gate=True: a scene's own gate without its time slot and "not shown
        # yet" tests (the mod checks those itself), leaving what else the
        # scene requires - e.g. a triggered scene's GetConditionBySceneName.
        self.gate = gate

    def value(self, val):
        """A serialised default value or literal."""
        if not isinstance(val, dict):
            text = bg._text(val)
            return text if text is not None else "?"
        content = val.get("$content")
        kind = val.get("$type", "")
        if kind == "System.String":
            return quote(content or "")
        if kind in ("System.Single", "System.Double", "System.Int32"):
            return repr(float(content)) if kind != "System.Int32" else str(content)
        if kind == "System.Boolean":
            return "true" if content else "false"
        if kind == "System.RuntimeType":
            return short(content)
        if isinstance(content, int) and not kind.startswith("System."):
            refs = self.g.entry["macros"]
            if 0 <= content < len(refs) and isinstance(refs[content], int):
                name = self.names.get(refs[content])
                if name:
                    return "@%s'%s'" % (short(kind), name)
        text = bg._text(content)
        return text if text is not None else "?"

    def port(self, unit_id, port, depth=0):
        """Text of the value flowing into (unit_id, port)."""
        for src, src_port in self.g.value.get((unit_id, port), []):
            return self.unit(src, src_port, depth + 1)
        unit = self.g.units.get(unit_id, {})
        val = (unit.get("defaultValues") or {}).get(port)
        return "?" if val is None else self.value(val)

    def unit(self, uid, out_port, depth):
        if depth > 25:
            return "?"
        u = self.g.units.get(uid, {})
        kind = u.get("$type", "").replace("Bolt.", "")
        if kind == "Literal":
            return self.value(u.get("value"))
        if kind == "GraphInput":
            return "$in:" + out_port       # resolved by the caller
        if kind == "Negate":
            return "!(" + self.port(uid, "input", depth) + ")"
        if kind in ("And", "Or"):
            op = " && " if kind == "And" else " || "
            return "(" + self.port(uid, "a", depth) + op + self.port(uid, "b", depth) + ")"
        if kind in ("Equal", "NotEqual"):
            op = " == " if kind == "Equal" else " != "
            return "(" + self.port(uid, "a", depth) + op + self.port(uid, "b", depth) + ")"
        if kind == "Comparison" and self.gate:
            for src, _ in self.g.value.get((uid, "a"), []):
                m = (self.g.units.get(src, {}).get("member") or {}).get("name")
                if m in ("Turn", "Season"):
                    return "true"
        if kind == "Comparison":
            ops = {"aLessThanB": "<", "aLessThanOrEqualToB": "<=", "aEqualToB": "==",
                   "aNotEqualToB": "!=", "aGreaterThanOrEqualToB": ">=",
                   "aGreaterThanB": ">",
                   "aGreatherThanB": ">"}     # sic: Bolt's own spelling
            return "(%s %s %s)" % (self.port(uid, "a", depth), ops.get(out_port, out_port),
                                   self.port(uid, "b", depth))
        member = u.get("member") or {}
        name = member.get("name", "")
        owner = short(member.get("targetType"))
        if kind == "GetMember":
            return "%s.%s" % (owner, name)
        if kind == "InvokeMember":
            if name in ("FindObjectOfType", "GetComponent", "GetComponentInChildren"):
                return name
            if self.gate and name == "GetSceneShowStatus":
                return "false"
            params = [k for k in (u.get("defaultValues") or {}) if k.startswith("%")]
            args = [self.port(uid, p, depth) for p in params]
            return "%s.%s(%s)" % (owner, name, ", ".join(args))
        return "?"


def unquote(text):
    if len(text) >= 2 and text[0] == '"' and text[-1] == '"':
        return text[1:-1].replace('\\"', '"').replace("\\\\", "\\")
    return text


def norm(labels):
    """Switch labels with only the innermost value per parameter: the walk
    passes the chapter-start switch on Post before the track's own one."""
    last = {}
    for lab in labels:
        name, _, value = lab.partition("=")
        last.pop(name, None)
        last[name] = value
    return tuple("%s=%s" % kv for kv in last.items())


class Walker:
    """Depth-first flow walk across macros; records scene gates in order."""

    def __init__(self, objects, names):
        self.objects = objects
        self.graphs = {}
        self.scene_of = {}           # macro object id -> scene info or None
        self.names = names

    def graph(self, entry):
        key = id(entry)
        if key not in self.graphs:
            self.graphs[key] = bg.Graph(entry, self.objects)
        return self.graphs[key]

    def scene_info(self, entry):
        """For a macro that loads a scene: {scene, turn, season, gate}."""
        key = id(entry)
        if key in self.scene_of:
            return self.scene_of[key]
        g = self.graph(entry)
        info = None
        loads = [u for u in g.units.values()
                 if (u.get("member") or {}).get("name") == "LoadScene"]
        if loads:
            ex = Expr(g, self.names)
            scene = unquote(ex.port(loads[0]["$id"], "%sceneName"))
            gate = ""
            for u in g.units.values():
                if u.get("$type") == "Bolt.Branch":
                    gate = Expr(g, self.names, gate=True).port(u["$id"], "condition")
                    break
            info = {"scene": scene, "gate": gate,
                    "turn": self.slot(g, "Turn"), "season": self.slot(g, "Season")}
        self.scene_of[key] = info
        return info

    @staticmethod
    def slot(g, member):
        """The constant a time member is compared to with ==, if any."""
        for uid, u in g.units.items():
            if u.get("$type") != "Bolt.Comparison":
                continue
            for src, _ in g.value.get((uid, "a"), []):
                m = (g.units.get(src, {}).get("member") or {}).get("name")
                if m == member:
                    b = bg._text((u.get("defaultValues") or {}).get("b"))
                    if b is not None:
                        return str(int(float(b)))
        return ""

    def walk_chapter(self, entry, number):
        """The chapter's scene gates in flow order, each with the condition
        under which the flow reaches it.

        Pass 1 flattens the macros into one graph of (macro, unit, call stack,
        switch labels) nodes whose edges carry the branch outcome taken.
        Pass 2 propagates conditions in topological order as a disjunction of
        conjunctions, simplified at every join: a guard `if (X) Special;` that
        rejoins the main line contributes X || !X there, which cancels, so the
        scenes after it stay unconditional."""
        g = self.graph(entry)
        starts = []
        for s in [u["$id"] for u in g.units.values() if u.get("$type") == "Bolt.GraphInput"]:
            for port in ("Enter", "Flow"):
                for dst, _ in g.control.get((s, port), []):
                    starts.append(self.node(entry, dst, [], []))
        nodes = {}          # key -> (entry, uid, stack, labels)
        succ = {}           # key -> [(key2, literal or None)]
        scenes = {}         # key -> scene info (for scene gates)
        order = []          # scene keys in discovery order
        todo = list(reversed(starts))
        for n in starts:
            nodes[n[0]] = n[1]
        while todo:
            key, state = todo.pop()
            if key in succ:
                continue
            out, info = self.successors(state)
            succ[key] = []
            if info is not None:
                scenes[key] = info
                order.append(key)
            for (k2, st2), lit in reversed(out):
                succ[key].append((k2, lit))
                if k2 not in succ:
                    nodes[k2] = st2
                    todo.append((k2, st2))
            succ[key].reverse()

        # topological order (reverse post-order; back edges, if any, ignored)
        post, mark = [], set()
        for root in [n[0] for n in starts]:
            stack = [(root, iter(succ.get(root, [])))]
            mark.add(root)
            while stack:
                k, it = stack[-1]
                for k2, _ in it:
                    if k2 not in mark:
                        mark.add(k2)
                        stack.append((k2, iter(succ.get(k2, []))))
                        break
                else:
                    stack.pop()
                    post.append(k)
        topo = list(reversed(post))
        rank = {k: i for i, k in enumerate(topo)}
        preds = {}
        for k, outs in succ.items():
            for k2, lit in outs:
                if rank.get(k, -1) < rank.get(k2, -1):
                    preds.setdefault(k2, []).append((k, lit))
        dnf = {}
        for k in topo:
            if k in [n[0] for n in starts] and k not in preds:
                dnf[k] = {frozenset()}
                continue
            terms = set()
            for p, lit in preds.get(k, []):
                for t in dnf.get(p, ()):
                    terms.add(t if lit is None else t | {lit})
            dnf[k] = simplify(terms)

        out = []
        for key in order:
            info = scenes[key]
            st = nodes[key]
            out.append({"chapter": number, "macro": entry["name"], "order": len(out),
                        "scene": info["scene"], "turn": info["turn"],
                        "season": info["season"], "labels": list(st[3]),
                        "dnf": dnf.get(key, set()), "gate": info["gate"]})
        return out

    @staticmethod
    def node(entry, uid, stack, labels):
        labels = list(norm(labels))
        key = (id(entry), uid, tuple((id(e), u) for e, u in stack), tuple(labels))
        return key, (entry, uid, stack, labels)

    def successors(self, state):
        """[(node, literal)] after a unit, plus scene info when it is a scene
        gate. A literal is (condition text, outcome) for a branch edge."""
        entry, uid, stack, labels = state
        g = self.graph(entry)
        u = g.units.get(uid, {})
        kind = u.get("$type", "")
        out = []

        def go(port, lab=None, lit=None):
            for dst, _ in g.control.get((uid, port), []):
                out.append((self.node(entry, dst, stack, labels if lab is None else lab), lit))

        if uid is None:
            return out, None
        if kind == "Bolt.GraphOutput":
            if stack:
                parent, sid = stack[-1]
                pg = self.graph(parent)
                for port in ("Exit", "exit"):
                    for dst, _ in pg.control.get((sid, port), []):
                        out.append((self.node(parent, dst, stack[:-1], labels), None))
            return out, None
        if kind == "Bolt.SuperUnit":
            macro = g.macro_for(u)
            if macro is None:
                go("Exit")
                return out, None
            info = self.scene_info(macro)
            if info is not None:
                scene = info["scene"]
                # A shared macro gets the scene name from its caller.
                frames = stack + [(entry, uid)]
                while scene.startswith("$in:") and frames:
                    caller, cid = frames.pop()
                    scene = unquote(Expr(self.graph(caller), self.names).port(cid, scene[4:]))
                go("Exit")
                return out, dict(info, scene=scene)
            if len(stack) >= MAX_STACK:
                go("Exit")
                return out, None
            mg = self.graph(macro)
            frame = stack + [(entry, uid)]
            for s in [x["$id"] for x in mg.units.values() if x.get("$type") == "Bolt.GraphInput"]:
                for port in ("Enter", "Flow", "enter"):
                    for dst, _ in mg.control.get((s, port), []):
                        out.append((self.node(macro, dst, frame, labels), None))
            return out, None
        ex = Expr(g, self.names)
        if kind == "Bolt.Branch":
            cond = ex.port(uid, "condition")
            go("ifTrue", lit=(cond, True))
            go("ifFalse", lit=(cond, False))
            return out, None
        if kind.startswith("Bolt.SwitchOn"):
            subject = ex.port(uid, "selector")
            name = subject
            prefix = "KeyChapterParametersController.GetKeyChapterParameterValue("
            if subject.startswith(prefix):
                name = unquote(subject[len(prefix):-1])
            for opt in u.get("options") or []:
                go("%" + str(opt), lab=labels + ["%s=%s" % (name, opt)])
            go("default", lab=labels + ["%s=(other)" % name])
            return out, None
        # Everything else (InvokeMember, Once, SelectOnFlow, Sequence, ...):
        # follow every control output in port order.
        for p in sorted({p for (src, p) in g.control if src == uid}):
            go(p)
        return out, None


MAX_TERMS = 48
UNKNOWN = frozenset({("?", True)})


def simplify(terms):
    """A disjunction of conjunctions (sets of (text, outcome) literals),
    reduced: contradictions dropped, X&A || !X&A merged to A, supersets
    absorbed. Past MAX_TERMS it becomes the single unknown term "?"."""
    terms = {t for t in terms if not any((x, not v) in t for x, v in t)}
    changed = True
    while changed:
        changed = False
        if frozenset() in terms:
            return {frozenset()}
        lst = sorted(terms, key=len)
        keep = set(lst)
        for i, a in enumerate(lst):
            if a not in keep:
                continue
            for b in lst[i + 1:]:
                if b in keep and a < b:
                    keep.discard(b)
        merged = set()
        for a in keep:
            for x, v in a:
                b = (a - {(x, v)}) | {(x, not v)}
                if b in keep:
                    merged.add(a - {(x, v)})
                    changed = True
        if merged:
            keep |= merged
        if keep != terms:
            changed = True
        terms = keep
        if len(terms) > MAX_TERMS:
            return {UNKNOWN}
    return terms


def render(dnf):
    """Expression text of a disjunction; "" when unconditional."""
    if not dnf or frozenset() in dnf:
        return ""
    parts = []
    for term in sorted(dnf, key=lambda t: sorted(t)):
        lits = [x if v else "!(" + x + ")" for x, v in sorted(term)]
        parts.append(lits[0] if len(lits) == 1 else "(" + " && ".join(lits) + ")")
    return parts[0] if len(parts) == 1 else "(" + " || ".join(parts) + ")"


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game", help="game folder (default: auto-detect)")
    ap.add_argument("--out", default=str(ROOT / "data" / "scene-flow.json"))
    args = ap.parse_args()

    game = gamedir.find_game(args.game)
    if not game:
        print("Game folder not found; pass --game DIR.", file=sys.stderr)
        return 1
    names = {}
    objects = bg.load_objects(find_asset(game), names)
    by_name = {}
    for entry in objects.values():
        by_name.setdefault(entry["name"], entry)

    walker = Walker(objects, names)
    flow = []
    for name, number in CHAPTERS:
        entry = by_name.get(name)
        if entry is None:
            print("chapter macro %s not found" % name, file=sys.stderr)
            continue
        rows = []
        index = {}
        for r in walker.walk_chapter(entry, number):
            # one row per (scene, label set); the first occurrence keeps its
            # place in flow order
            k = (r["scene"], tuple(r["labels"]))
            if k in index:
                rows[index[k]]["dnf"] = rows[index[k]]["dnf"] | r["dnf"]
                continue
            index[k] = len(rows)
            rows.append(r)
        # The TSV keeps one row per flow occurrence (scene + switch label
        # set): under a switch a scene can sit at different flow positions,
        # and the mod filters rows by the live label set. The JSON below
        # stays one entry per scene for readability.
        print("  %-17s chapter %d: %3d scene gate(s), %3d distinct scene(s)"
              % (name, number, len(rows), len({r["scene"] for r in rows})))
        flow.extend(rows)

    def esc(text):
        return text.replace("\t", " ").replace("\n", " ").replace("|", "/")

    # One entry per scene: label sets as alternatives, and the branch
    # conditions of one path that reaches it (none when any path is free).
    # PeaceTree and "(other)" defaults are dropped from the mod's copy: the
    # track flips every turn, so it cannot narrow anything down.
    merged = {}
    for r in flow:
        k = (r["chapter"], r["scene"])
        m = merged.get(k)
        if m is None:
            m = merged[k] = {"chapter": r["chapter"], "macro": r["macro"],
                             "order": r["order"], "scene": r["scene"],
                             "turn": r["turn"], "season": r["season"],
                             "labels": [], "dnf": set(), "gate": r["gate"]}
        if r["labels"] not in m["labels"]:
            m["labels"].append(r["labels"])
        m["dnf"] |= r["dnf"]
    for m in merged.values():
        cond = render(simplify(m.pop("dnf")))
        # A gate that is only time and "not shown" says nothing more.
        gate = m["gate"] if re.search(r"[A-Za-z]\w*\.\w+", m["gate"]) else ""
        m["conditions"] = [c for c in (cond, gate) if c]
    scenes = list(merged.values())

    doc = {"schema": 2,
           "source": "Bolt chapter macros, walked in flow order (tools/bolt_story.py)",
           "scenes": scenes}
    path = pathlib.Path(args.out)
    path.write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")

    lines = []
    seen = set()
    for r in flow:
        if r["chapter"] > 5:
            continue      # epilogue pages: no answers, never a tree column
        key = (r["chapter"], r["scene"], tuple(r["labels"]))
        if key in seen:
            continue
        seen.add(key)
        alts = ";".join(esc(l) for l in r["labels"]
                        if not l.startswith("PeaceTree=") and not l.endswith("=(other)"))
        cond = render(simplify(r["dnf"]))
        gate = r["gate"] if re.search(r"[A-Za-z]\w*\.\w+", r["gate"]) else ""
        conditions = [c for c in (cond, gate) if c]
        lines.append("%d\t%d\t%s\t%s\t%s\t%s\t%s\n" % (
            r["chapter"], r["order"], r["scene"], r["turn"], r["season"],
            alts, " && ".join(conditions).replace("\t", " ").replace("\n", " ")))
    tsv = "".join(lines)
    for target in (ROOT / "data" / "scene-flow.tsv", ROOT / "Brante-Save-Load" / "scene-flow.tsv"):
        target.write_text(tsv, encoding="utf-8")
        print("wrote %s" % target)
    print("wrote %s" % path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
