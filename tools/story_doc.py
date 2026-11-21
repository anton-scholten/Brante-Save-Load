#!/usr/bin/env python3
"""A readable guide to the whole story, made from scene-list.tsv.

scene-list.tsv is written by the mod inside the game (dev-pick `scenelist`),
with the game's own names for scenes, answers, stats, people and destinies.
This tool turns it into Markdown that GitHub renders:

  docs/story/README.md        how to read it, and one flow chart per chapter
                              (per post where the chapter splits)
  docs/story/chapter-N.md     every scene of the chapter in flow order: when it
                              plays, what it needs, its text, what its opening
                              changes, and each choice with its requirements,
                              effects, destinies and the switches it sets
  docs/story/finale.md        before the story, the true death, the outcome, the
                              fates, the cutscenes: the scenes outside the chapter
                              flows (from data/scene-terms.json, tools/scene_terms.py)

Usage:  python3 tools/story_doc.py [--list Brante-Save-Load/scene-list.tsv]
"""

import argparse
import collections
import csv
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "story"

ROMAN = {1: "I", 2: "II", 3: "III", 4: "IV", 5: "V"}
POSTS = {"Judge": "Judge (Noble track)", "Inquisitor": "Inquisitor (Priest track)",
         "Rebel": "Rebel (Lotless track)"}
# switches the flow uses for its own bookkeeping: they say nothing to a reader
INTERNAL = {"TriggerBlock"}


def load(path):
    rows = list(csv.reader(open(path, encoding="utf-8"), delimiter="\t"))
    head = rows[0]
    col = {name: i for i, name in enumerate(head)}
    scenes = []
    for r in rows[1:]:
        r = r + [""] * (len(head) - len(r))
        get = lambda name: r[col[name]] if name in col else ""
        if get("Scene"):
            scenes.append({
                "chapter": int(get("Chapter")) if get("Chapter").isdigit() else 0,
                "id": get("Scene"), "kind": get("Kind"), "title": get("Title") or get("Scene"),
                "text": get("Text"), "gate": get("Opens when"), "opening": get("On opening"),
                "destinies": get("Destinies"), "keys": get("Key parameters"),
                "when": get("When"), "track": get("Track"),
                "order": int(get("Order")) if get("Order").isdigit() else 10 ** 6,
                "answers": [],
            })
        elif scenes and get("Answer"):
            scenes[-1]["answers"].append({
                "n": get("Answer"), "name": get("Name"), "text": get("Answer text"),
                "requires": get("Requires"), "changes": get("Changes"),
                "destinies": get("Destinies"), "keys": get("Key parameters"),
            })
    return scenes


def setters(scenes):
    """'Name=Value' -> ['Scene: answer', ...] (or the scene, for its opening)."""
    out = collections.defaultdict(list)
    for s in scenes:
        for kv in filter(None, (x.strip() for x in s["keys"].split(","))):
            out[kv].append(s["title"])
        for a in s["answers"]:
            for kv in filter(None, (x.strip() for x in a["keys"].split(","))):
                out[kv].append("%s: %s" % (s["title"], a["name"] or "answer " + a["n"]))
    return out


def track_text(track, set_by):
    """The switches a scene needs, in words: only those every flow branch
    it sits on requires with a listed value (a branch without the switch,
    or under its default "none of" output, leaves it free); values that
    differ between branches are joined by "or"."""
    if not track:
        return ""
    alts = []
    for alt in track.split(" | "):
        labels = {}
        for label in alt.split("; "):
            name, _, value = label.partition("=")
            if name and name not in INTERNAL:
                labels[name] = value
        alts.append(labels)
    names = []
    for labels in alts:
        for name in labels:
            if name not in names:
                names.append(name)
    parts = []
    for name in names:
        values = [labels.get(name) for labels in alts]
        if any(v is None or v.startswith("!") for v in values):
            continue                            # free on some branch
        distinct = sorted(set(values))
        if name == "Post":
            if len(distinct) < len(POSTS):
                parts.append(" or ".join(POSTS.get(v, v) for v in distinct))
            continue
        shown = []
        for v in distinct:
            by = sorted(set(set_by.get(name + "=" + v, [])))
            shown.append(v + (" (set by " + "; ".join(by[:4]) + ("; ..." if len(by) > 4 else "") + ")" if by else ""))
        parts.append("%s = %s" % (name, " or ".join(shown)))
    return "; ".join(parts)


def posts_of(scene):
    return sorted(set(re.findall(r"Post=(\w+)", scene["track"])))


def cell(text):
    return (text or "").replace("|", "\\|").replace("\n", " ").strip() or "-"


def anchor(scene):
    return re.sub(r"[^a-z0-9-]", "", scene["id"].lower().replace(".", "-").replace("_", "-"))


def triggered(scene):
    """Opens any time up to a turn, as soon as its condition holds (the
    flow's trigger scenes), rather than in a time slot of its own."""
    return scene["when"].startswith("until turn") and not scene["id"].endswith("ChapterStart")


def slot_key(scene):
    """Sort and group key: first turn, season, flow order; chapter starts
    first, triggered scenes after the timed ones, a chapter's end last."""
    if scene.get("place") == "start":
        return (-2, 0)
    if scene.get("place") == "end":
        return (300, 0)
    if scene["id"].endswith("ChapterStart"):
        return (-1, 0)
    m = re.search(r"(?:turns? |until turn )(\d+)", scene["when"])
    turn = int(m.group(1)) if m else 99
    if triggered(scene):
        turn = 200
    m = re.search(r"season (\d)", scene["when"])
    return (turn, int(m.group(1)) if m else 0)


def slot_name(scene):
    if scene.get("place") == "end":
        return "chapter end"
    if scene["id"].endswith("ChapterStart") or scene.get("place") == "start":
        return "chapter start"
    if triggered(scene):
        return "triggered: any time " + scene["when"].split(" / ")[0]
    return scene["when"].split(" / ")[0] if scene["when"] else "in flow order"


def mermaid(scenes, title):
    """Scenes grouped by time slot (a box each), the slots chained in time
    order; a scene with a condition is marked with a diamond."""
    groups = collections.OrderedDict()
    for s in sorted(scenes, key=lambda s: (slot_key(s), s["order"])):
        groups.setdefault(slot_name(s), []).append(s)
    lines = ["```mermaid", "flowchart TD"]
    ids = []
    for gi, (name, members) in enumerate(groups.items()):
        gid = "G%d" % gi
        ids.append(gid)
        lines.append('  subgraph %s["%s"]' % (gid, name.capitalize()))
        lines.append("    direction LR")
        for s in members:
            label = s["title"].replace('"', "'")
            if s["gate"] or (s["track"] and track_text(s["track"], {}) and "Post" not in s["track"]):
                label = "◇ " + label
            shape = '("%s")' % label if s["kind"] == "event" else '["%s"]' % label
            lines.append("    %s%s" % (anchor(s).replace("-", "_") or "x", shape))
        lines.append("  end")
    chained = [g for g, name in zip(ids, groups) if not name.startswith("triggered")]
    for a, b in zip(chained, chained[1:]):
        lines.append("  %s --> %s" % (a, b))
    lines.append("```")
    return "\n".join(lines)


def folded(summary, body):
    """A block shown when its summary is clicked (the charts are tall)."""
    summary = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', summary)
    return "<details><summary>%s</summary>\n\n%s\n\n</details>\n" % (summary, body)


def scene_md(s, set_by):
    out = ['<a id="%s"></a>' % anchor(s), "### %s" % s["title"], ""]
    facts = ["`%s` · %s" % (s["id"], {"choice": "choice", "event": "event", "page": "page"}.get(s["kind"], s["kind"]))]
    if s["when"] and not s["id"].endswith("ChapterStart"):
        facts.append("**When:** " + ("triggered, any time " + s["when"] if triggered(s) else s["when"]))
    track = track_text(s["track"], set_by)
    if track:
        facts.append("**Track:** " + track)
    if s["gate"]:
        facts.append("**Opens when:** " + s["gate"])
    if s["opening"]:
        facts.append("**On opening:** " + s["opening"])
    if s["destinies"]:
        facts.append("**Earns:** " + s["destinies"])
    if s["keys"]:
        facts.append("**Sets:** " + s["keys"])
    out.append("  \n".join(facts))
    out.append("")
    if s.get("note"):
        out.append("*" + s["note"] + "*\n")
    pages = [p.strip() for p in s["text"].split(" | ") if p.strip()]
    if pages:
        lead = pages[-1] if s["kind"] == "choice" else pages[0]
        out.append("> " + lead.replace(" / ", "  \n> "))
        out.append("")
        if len(pages) > 1:
            out.append("<details><summary>All %d text pages</summary>\n" % len(pages))
            for p in pages:
                out.append(p.replace(" / ", "  \n") + "\n")
            out.append("</details>\n")
    if s["answers"]:
        out.append("| # | Choice | Requires | Effects | Destinies | Sets |")
        out.append("|---|---|---|---|---|---|")
        for a in s["answers"]:
            choice = "**%s**<br>%s" % (cell(a["name"]), cell(a["text"])) if a["name"] else cell(a["text"])
            out.append("| %s | %s | %s | %s | %s | %s |" % (
                a["n"], choice, cell(a["requires"]), cell(a["changes"]),
                cell(a["destinies"]), cell(a["keys"])))
        out.append("")
    return "\n".join(out)


# Scenes no chapter flow opens but that belong to a chapter: its picture and
# start pages, its finish screen, its cutscenes. (chapter, place, post)
CHAPTER_SCREENS = {
    "ChildhoodPicture": (1, "start", ""), "ChildhoodFinishScreen": (1, "end", ""),
    "TeenagerPicture": (2, "start", ""), "TeenFinishScreen": (2, "end", ""),
    "YouthPicture": (3, "start", ""), "YouthFinishScreen": (3, "end", ""),
    "Cutscene_RoadToUniversity": (3, "start", ""),
    "PeacePicture": (4, "start", ""),
    "PeaceChapterStart_Prefect": (4, "start", "Judge"), "PeaceFinishScreen_Prefect": (4, "end", "Judge"),
    "PeaceChapterStart_Inquisitor": (4, "start", "Inquisitor"), "PeaceFinishScreen_Inquisitor": (4, "end", "Inquisitor"),
    "PeaceChapterStart_Rebel": (4, "start", "Rebel"), "PeaceFinishScreen_Rebel": (4, "end", "Rebel"),
    "InsurrectionPicture": (5, "start", ""), "Cutscene_6_RevoltBegins": (5, "start", ""),
}
# the game's machinery, not the story
TECHNICAL = {"GamePreparationScene", "GameLoadingScreenGameOver", "BootScreen", "Credits",
             "Credits_GameOver", "Settings", "MainMenu", "LoadingScreen", "GameLoadingScreen",
             "LoadWindow", "LoadingScreen_Child", "ChildhoodDemoFinishScreen", "GameOver"}
# the finale file's sections, by scene id
FINALE_GROUPS = [
    ("Before the story", lambda i: i in ("Disclaimer", "Intro") or i == "Cutscene_1"),
    ("The true death", lambda i: i.startswith("FourthDeath") or "TrueDeath" in i),
    ("The outcome of the revolt", lambda i: i.startswith("070.090.")),
    ("The provinces", lambda i: i == "Final_Provinces"),
    ("The family's fate", lambda i: "FateOfTheFamily" in i or i.startswith("070.240.")),
    ("Fates of the people", lambda i: i.startswith("070.") or i.startswith("Final_Characters")),
    ("Cutscenes", lambda i: i.startswith("Cutscene")),
    ("Scenes with no text of their own", lambda i: True),
]


# titles for scenes whose own is missing or only an id
TITLES = {
    "Cutscene_1": "Cutscene: the opening", "Cutscene_RoadToUniversity": "Cutscene: the road to the capital",
    "Cutscene_6_RevoltBegins": "Cutscene: the Revolt begins",
    "Cutscene_7_RazgromVosstaniya": "Cutscene: The Revolt is Defeated",
    "Cutscene_8_Razgrom_Legiona": "Cutscene: The Legion is Defeated",
    "Cutscene_9_Gorod_Uderjan": "Cutscene: The City is Defended",
    "Cutscene_10_Reznya": "Cutscene: The Anizotte Massacre",
    "Cutscene_11_Smutnoe_Vremya": "Cutscene: Dark Times",
    "Final_Provinces": "What Became of the Provinces",
    "Final_Characters_Jeanne": "What Became of Jeanne (page)",
    "Final_Characters_Octavia": "What Became of Octavia (page)",
    "Final_Characters_Sofja": "What Became of Sophia (page)",
}


def placeholders(text):
    """The game's placeholders kept visible: the hero's name, the "El" of a
    family ennobled by the sword."""
    return re.sub(r"  +", " ", (text or "").replace("<username>", "[name]").replace("<el>", "[El]")).strip()


def natural(text):
    return [int(t) if t.isdigit() else t for t in re.split(r"(\d+)", text)]


def add_term_scenes(scenes):
    """The game's scenes scene-list.tsv lacks, from data/scene-terms.json
    (tools/scene_terms.py: every term each scene's objects name) and the
    English of data/localization.csv: their title, text pages and, where
    they have them, their answers (the true-death questions)."""
    terms_path = ROOT / "data" / "scene-terms.json"
    loc_path = ROOT / "data" / "localization.csv"
    if not terms_path.is_file() or not loc_path.is_file():
        print("data/scene-terms.json or data/localization.csv missing: only scene-list.tsv's scenes",
              file=sys.stderr)
        return
    import json
    english = {r[0]: r[4] for r in csv.reader(open(loc_path, encoding="utf-8")) if len(r) > 4}
    build = json.load(open(terms_path, encoding="utf-8"))["scenes"]
    uses = collections.Counter(t for b in build for t in set(b["terms"]))
    have = {s["id"] for s in scenes}
    for b in build:
        sid = b["scene"]
        if sid in have or sid in TECHNICAL:
            continue
        own = [t for t in b["terms"] if uses[t] <= 6 and english.get(t, "").strip()]
        title, pages, buttons = "", [], {}
        for t in own:
            m = re.search(r"_Button_?(\d+)$|_ButtonDescription_?(\d+)$", t)
            if m:
                n = m.group(1) or m.group(2)
                kind = "name" if m.group(1) else "text"
                buttons.setdefault(n, {})[kind] = english[t].strip()
            elif re.search(r"(\.|_)Title$", t) and not title:
                title = english[t].strip()
            else:
                pages.append(t)
        pages.sort(key=natural)
        if not title:
            # a title the scene shares with others ("TrueDeath_Title"),
            # never the general "Chapter"/"Next" words
            for t in b["terms"]:
                if re.search(r"(\.|_)Title$", t) and english.get(t, "").strip() and uses[t] <= 60:
                    title = english[t].strip()
                    break
        chapter, place, post = CHAPTER_SCREENS.get(sid, (0, "", ""))
        scenes.append({
            "chapter": chapter, "id": sid, "kind": "choice" if buttons else "page",
            "title": placeholders(TITLES.get(sid) or title or re.sub(r"^Cutscene_(\d+)$", r"Cutscene \1", sid)),
            "text": " | ".join(placeholders(english[t].replace("\n", " / ")) for t in pages),
            "gate": "", "opening": "", "destinies": "", "keys": "", "when": "",
            "track": "Post=" + post if post else "", "order": 10 ** 6 + b["index"], "place": place,
            "answers": [{"n": n, "name": placeholders(a.get("name", "")), "text": placeholders(a.get("text", "")), "requires": "",
                         "changes": "", "destinies": "", "keys": ""}
                        for n, a in sorted(buttons.items(), key=lambda kv: int(kv[0]))],
            "note": "" if pages or buttons else
                    "No text of its own: the game fills it in as it plays, or does not use the scene.",
        })


def chapter_names():
    """The game's chapter titles ("Chapter IV. Peace Time"), from its
    localisation terms (data/localization.csv, tools/localization.py)."""
    keys = {1: "Chapter1.Childhood", 2: "Chapter2.Teen", 3: "Chapter3.Youth",
            4: "Chapter4.Peace", 5: "Chapter5.War"}
    names = {}
    path = ROOT / "data" / "localization.csv"
    if path.is_file():
        terms = {r[0]: r for r in csv.reader(open(path, encoding="utf-8")) if r}
        for ch, key in keys.items():
            if key in terms and len(terms[key]) > 4 and terms[key][4].strip():
                names[ch] = terms[key][4].strip()
    return names


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--list", default=str(ROOT / "Brante-Save-Load" / "scene-list.tsv"))
    args = ap.parse_args()
    scenes = load(args.list)
    add_term_scenes(scenes)
    set_by = setters(scenes)
    OUT.mkdir(parents=True, exist_ok=True)

    names = chapter_names()
    for s in scenes:
        if s["id"].endswith("ChapterStart") and s["title"] == s["id"] and s["chapter"] in names:
            s["title"] = names[s["chapter"]]       # an untitled chapter start: the chapter's name
    by_chapter = collections.defaultdict(list)
    for s in scenes:
        by_chapter[s["chapter"]].append(s)

    index = ["# The story, scene by scene", "",
             "Every scene built into *The Life and Suffering of Sir Brante* - all 465 story scenes",
             "of the game's 478 (the other 13 are menus and loading screens) - with when it plays,",
             "what it needs, everything it says, and every choice with what it requires, changes",
             "and earns. Generated from the game's own data, in the game's English",
             "(`tools/story_doc.py`, from the mod's `scene-list.tsv` and `data/scene-terms.json`).",
             "`[name]` is the hero's name and `[El]` the particle of a family ennobled by the",
             "sword: the game fills both in as it plays.", "",
             "How to read a scene:", "",
             "- **When**: the time slot the chapter's flow opens it in (turn, season). No time:",
             "  it follows in flow order, or is triggered as soon as its condition holds.",
             "- **Track**: the story switches it needs - the post in chapters 4 and 5, and the",
             "  switches a choice sets (with the choices that set them).",
             "- **Opens when**: the scene's own condition (stats, people, destinies, scenes seen).",
             "- **On opening / Earns**: what the scene does by itself, whatever is chosen.",
             "- Each choice's **Requires** must hold for it to be picked; **Effects** and",
             "  **Destinies** follow when it is; **Sets** are the switches it turns.", "",
             "Click a chapter's 'Flow chart' to open it. In the charts each box is a time slot, top to bottom in time; ◇ marks a scene with a condition,",
             "rounded boxes are events (no choice).", ""]
    for ch in sorted(c for c in by_chapter if c):
        title = names.get(ch, "Chapter %s" % ROMAN.get(ch, ch))
        file = "chapter-%d.md" % ch
        members = sorted(by_chapter[ch], key=lambda s: (slot_key(s), s["order"]))
        index.append("## [%s](%s)\n" % (title, file))
        posts = sorted({p for s in members for p in posts_of(s)})
        # a scene of every post is the chapter's, not a post's
        own = lambda s: [] if set(posts_of(s)) >= set(posts) else posts_of(s)
        if posts:
            common = [s for s in members if not own(s)]
            if common:
                index.append(folded("Flow chart: every post (%d scenes)" % len(common), mermaid(common, title)))
            for post in posts:
                group = [s for s in members if post in own(s)]
                index.append(folded("Flow chart: %s (%d scenes) - [its scenes](chapter-%d-%s.md)"
                                    % (POSTS.get(post, post), len(group), ch, post.lower()), mermaid(group, title)))
        else:
            index.append(folded("Flow chart (%d scenes)" % len(members), mermaid(members, title)))
        # one file for the scenes every post sees, one per post
        body = ["# %s" % title, "", "[All chapters](README.md)", ""]
        if posts:
            body.append("The scenes of every post are below; each post's own: "
                        + ", ".join("[%s](chapter-%d-%s.md)" % (POSTS.get(p, p), ch, p.lower()) for p in posts) + ".\n")
        body += [scene_md(s, set_by) for s in members if not own(s)]
        (OUT / file).write_text("\n".join(body), encoding="utf-8")
        for post in posts:
            group = [s for s in members if post in own(s)]
            body = ["# %s - %s" % (title, POSTS.get(post, post)), "",
                    "[All chapters](README.md) · [the chapter's other scenes](%s)" % file, ""]
            body += [scene_md(s, set_by) for s in group]
            (OUT / ("chapter-%d-%s.md" % (ch, post.lower()))).write_text("\n".join(body), encoding="utf-8")

    rest = sorted(by_chapter.get(0, []), key=lambda s: natural(s["id"]))
    if rest:
        index.append("## [The finale and other pages](finale.md)\n")
        index.append("Before the story, the true death, the outcome of the revolt, the provinces' and")
        index.append("everyone's fates, the cutscenes: the scenes the chapter flows do not open themselves.\n")
        body = ["# The finale and other pages", "", "[All chapters](README.md)", ""]
        placed = set()
        for head, test in FINALE_GROUPS:
            group = [s for s in rest if s["id"] not in placed and test(s["id"])
                     and (head != "Scenes with no text of their own" or s.get("note"))]
            if head != "Scenes with no text of their own":
                group = [s for s in group if not s.get("note")]
            if not group:
                continue
            placed.update(s["id"] for s in group)
            body.append("## %s\n" % head)
            body += [scene_md(s, set_by) for s in group]
        others = [s for s in rest if s["id"] not in placed]
        if others:
            body.append("## Other pages\n")
            body += [scene_md(s, set_by) for s in others]
        (OUT / "finale.md").write_text("\n".join(body), encoding="utf-8")
    (OUT / "README.md").write_text("\n".join(index), encoding="utf-8")
    total = sum(len(s["answers"]) for s in scenes)
    print("%d scenes, %d choices -> %s" % (len(scenes), total, OUT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
