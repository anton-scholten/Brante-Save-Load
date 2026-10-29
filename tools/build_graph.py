#!/usr/bin/env python3
"""Build the choice graph from the mod's traces and the community event sheet.

Inputs
  Runs/<save>/trace.log        written by the mod: every scene load and click
  Runs/<save>/Auto/*.meta.txt  snapshots: scene id, title, offered choices
  data/events.json             the community sheet (175 events, all options)

Output
  data/graph.json
    scenes  : scene id -> {title, options, event}
    events  : event title -> scene id
    edges   : [{from, option, to, count}]  (option -> next story scene)

An edge is the first STORY scene loaded after a click. Machinery scenes
(GameLoadingScreen, cutscenes, windows) are skipped: story scenes are named
`NN.NN.NN` or `NN.NN.NN_k`.

Usage
  python3 tools/build_graph.py                  # auto-detects the Proton prefix
  python3 tools/build_graph.py --runs PATH      # explicit Runs/ directory
  python3 tools/build_graph.py --fixture DIR    # quiet, for tests
"""

import argparse
import json
import pathlib
import re
import sys
from datetime import datetime

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import gamedir  # noqa: E402
STORY = re.compile(r"^\d{2}\.\d{2}\.\d{2}(_\d+)?$")
LINE = re.compile(r"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\t(\w+)\t([^\t]*)\t(.*)$")
SIDE_PREFIX = "<data>/Brante-Save-Load/Runs"
CLICK_WINDOW_S = 180  # a story scene further away than this is not the target


# ---------------------------------------------------------------- utilities
def norm(text):
    """Lowercase, keep letters/digits only - the sheet and the game differ in
    capitalisation and punctuation ("Reach Out To The Palm" / "Reach out to the Palm")."""
    return re.sub(r"[^a-z0-9]", "", (text or "").lower())


def parse_trace(path):
    """[(datetime, kind, a, b)] in file order."""
    out = []
    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as exc:
        print("  ! cannot read %s: %s" % (path, exc), file=sys.stderr)
        return out
    for raw in lines:
        m = LINE.match(raw)
        if not m:
            continue
        try:
            when = datetime.strptime(m.group(1), "%Y-%m-%d %H:%M:%S.%f")
        except ValueError:
            continue
        out.append((when, m.group(2), m.group(3), m.group(4)))
    return out


def parse_sidecar(path):
    """scene=, title=, choices list and chosen= from a .meta.txt."""
    info = {"scene": "", "title": "", "titleKey": "", "keyScene": "", "options": [], "chosen": ""}
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return info
    for raw in text.splitlines():
        line = raw.strip()
        if line.startswith("scene="):
            info["scene"] = line[6:].strip()
        elif line.startswith("title="):
            title = line[6:]
            cut = title.find("   [key:")
            if cut >= 0:
                key = title[title.index(":", cut) + 1:].strip().rstrip("]").strip()
                info["titleKey"] = key
                m = re.match(r"^(\d{2}\.\d{2}\.\d{2}(?:_\d+)?)_Title$", key)
                if m:
                    info["keyScene"] = m.group(1)
                title = title[:cut]
            info["title"] = title.strip()
        elif line.startswith("chosen="):
            info["chosen"] = line[7:].strip()
        elif line.startswith("[") and "]" in line:
            body = line[line.index("]") + 1:].strip()
            name_at = body.find("(name:")
            if name_at >= 0:
                body = body[:name_at].strip()
            if body:
                info["options"].append(body)
    return info


# ---------------------------------------------------------------- discovery
def collect(runs_dir, verbose=True):
    trace_lines = []
    sidecars = {}
    key_titles = {}   # scene id -> title taken from the title key, not the controller
    traces = sorted(runs_dir.glob("*/trace.log"))
    if verbose:
        print("traces: %d" % len(traces))
    for path in traces:
        lines = parse_trace(path)
        if verbose and lines:
            print("  %s: %d line(s)" % (path.parent.name, len(lines)))
        trace_lines += lines
    for path in sorted(runs_dir.glob("*/Auto/*.meta.txt")):
        info = parse_sidecar(path)
        scene = info["scene"]
        if not scene:
            continue
        # The title key names the scene it belongs to, so it is authoritative
        # even when an older sidecar recorded the previous scene's title.
        if info["keyScene"] and info["title"]:
            key_titles.setdefault(info["keyScene"], info["title"])
        # several snapshots can share a scene; merge their options
        cur = sidecars.setdefault(scene, {"title": "", "options": [], "chosen": set()})
        if info["title"] and not cur["title"]:
            cur["title"] = info["title"]
        for opt in info["options"]:
            if norm(opt) not in {norm(o) for o in cur["options"]}:
                cur["options"].append(opt)
        if info["chosen"]:
            cur["chosen"].add(info["chosen"])
    if verbose:
        print("sidecars: %d scene(s) with choices, %d title(s) from keys"
              % (len(sidecars), len(key_titles)))
    return trace_lines, sidecars, key_titles


# ---------------------------------------------------------------- matching
def match_events(sidecars, key_titles, events):
    """Event title -> scene id.

    Titles are compared first (the snapshot stores the scene's localised
    title), because the option lists can contain buttons left over from other
    additive scenes. Option text overlap is only a fallback.
    """
    scored = []
    for scene, info in sidecars.items():
        scene_title = norm(key_titles.get(scene) or info["title"])
        scene_opts = {norm(o) for o in info["options"]}
        for event in events:
            title = norm(event["title"])
            score = 0.0
            if scene_title and title:
                if scene_title == title:
                    score = 1.0
                elif scene_title in title or title in scene_title:
                    short, long_ = sorted((len(scene_title), len(title)))
                    score = 0.8 * (short / long_)
            opts = [norm(o["text"]) for o in event["options"] if o["text"]]
            if scene_opts and opts:
                hits = sum(1 for t in opts if t in scene_opts)
                dice = 2.0 * hits / (len(opts) + len(scene_opts))
                score = max(score, 0.6 * dice + (0.4 if hits == len(opts) else 0.0))
            if score >= 0.5:
                scored.append((score, event["title"], scene))
    # one-to-one, best first
    mapping, used_scenes = {}, set()
    for score, title, scene in sorted(scored, reverse=True):
        if title in mapping or scene in used_scenes:
            continue
        mapping[title] = scene
        used_scenes.add(scene)
    return mapping


def find_click_targets(trace_lines):
    """[(scene, button, button_text, target_scene)] from click/scene pairs."""
    out = []
    for i, (when, kind, a, b) in enumerate(trace_lines):
        if kind != "click":
            continue
        scene = (a or "").strip()
        button, _, button_text = (b or "").partition(" | ")
        target = ""
        for when2, kind2, name2, _ in trace_lines[i + 1:]:
            if kind2 != "scene":
                continue
            delta = (when2 - when).total_seconds()
            if delta > CLICK_WINDOW_S:
                break
            if not STORY.match(name2 or ""):
                continue          # loading screens, windows, cutscenes
            if name2 == scene:
                continue          # the scene itself re-opening is not an edge
            target = name2
            break
        out.append((scene, button, button_text.strip(), target))
    return out


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--runs", help="the mod's Runs directory (default: auto-detect)")
    ap.add_argument("--out", default=str(ROOT / "data" / "graph.json"))
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args()

    runs = pathlib.Path(args.runs) if args.runs else gamedir.find_runs_dir()
    if runs is None or not runs.is_dir():
        print("No Runs directory found. Pass --runs PATH "
              "(<persistentDataPath>/Brante-Save-Load/Runs).", file=sys.stderr)
        return 1
    if not args.quiet:
        print("runs: %s" % runs)

    trace_lines, sidecars, key_titles = collect(runs, verbose=not args.quiet)
    events_doc = json.loads((ROOT / "data" / "events.json").read_text(encoding="utf-8"))
    events = [e for g in events_doc["groups"] for e in g["events"]]

    mapping = match_events(sidecars, key_titles, events)
    scene_to_event = {scene: title for title, scene in mapping.items()}

    edges = []
    seen = {}
    for scene, button, button_text, target in find_click_targets(trace_lines):
        key = (scene, button, button_text, target)
        seen[key] = seen.get(key, 0) + 1
    for (scene, button, text, target), count in sorted(seen.items()):
        edges.append({
            "from": scene,
            "fromEvent": scene_to_event.get(scene, ""),
            "button": button,
            "text": text,
            "to": target,
            "toEvent": scene_to_event.get(target, ""),
            "count": count,
        })

    # Per-scene successors: choices rarely change the destination (a scene had
    # 8 consequence texts and one next scene), so one observed transition can
    # stand in for every option of that scene. Two different targets observed
    # from the same scene means it really branches (death/final scenes).
    successors = {}
    for edge in edges:
        if not edge["to"]:
            continue
        targets = successors.setdefault(edge["from"], {})
        targets[edge["to"]] = targets.get(edge["to"], 0) + edge["count"]

    scenes = {}
    for scene, info in sorted(sidecars.items()):
        scenes[scene] = {
            "title": key_titles.get(scene, info["title"]),
            "event": scene_to_event.get(scene, ""),
            "options": info["options"],
        }

    doc = {
        "schema": 1,
        "source": {
            "events": "data/events.json (community sheet by Brian Sirith)",
            "traces": "Runs/*/trace.log written by Brante-Save-Load",
        },
        "scenes": scenes,
        "events": mapping,
        "edges": edges,
        "sceneSuccessors": {k: sorted(v) for k, v in successors.items()},
    }
    out = pathlib.Path(args.out)
    out.write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")

    if not args.quiet:
        for scene, targets in sorted(successors.items()):
            print("successor: %s -> %s%s" % (scene, ", ".join(sorted(targets)),
                  "  (branches)" if len(targets) > 1 else ""))
        clicks = sum(1 for l in trace_lines if l[1] == "click")
        print("clicks: %d, edges resolved: %d, scenes mapped to sheet events: %d"
              % (clicks, len(edges), len(mapping)))
        try:
            shown = out.relative_to(ROOT)
        except ValueError:
            shown = out
        print("wrote %s" % shown)
        if mapping:
            print("mapped:")
            for title, scene in sorted(mapping.items()):
                print("  %-28s %s" % (scene, title))
        for edge in edges:
            print("edge: %s (%s) --%s--> %s" % (
                edge["from"], edge["fromEvent"] or "?", edge["text"] or edge["button"],
                edge["to"]))
        if len(mapping) < len(events):
            print("note: %d of %d sheet events are not mapped to a scene yet - "
                  "they need a sidecar (the scene must be played/captured once)."
                  % (len(events) - len(mapping), len(events)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
