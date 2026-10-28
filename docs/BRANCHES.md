# Branch data for the full choice tree

This folder holds what is known about the game's choices, gathered from public
guides so the history view can eventually show the *whole* tree, with the path of
a given save highlighted. It is a starting point, not a complete graph — see
"what is missing" below.

- `data/events.csv` — the community spreadsheet as downloaded (raw source).
- `data/events.json` — the same data parsed (`tools/csv_to_events.py`): 19 groups,
  **175 events**, 3.35 options per event on average, covering all five chapters
  including Peace Time and The Revolt. Each option has its text, its
  requirement and its outcome.
- `data/branches.json` — hand-made digest: chapter 1 in full detail, chapter 2–3
  scenes, destinies, endings, and the in-game scene ids we confirmed from the
  mod's own logs.
- `data/trace.log` (per main save, created at runtime) — see "Getting the
  missing edges".

## Coverage

| Chapter | Scenes | Choice texts | Conditions | Effects | Edges (choice → next scene) |
|---|---|---|---|---|---|
| 1. Childhood | all 13 + 2 optional | complete | complete | complete | only the optional-scene windows |
| 2. Adolescence | all 14 + 3 optional | 1 of the options per scene (one playthrough) | partial | complete per guide | no |
| 3. Youth | first 5 (up to the Lots) | no | partial (the Lots) | complete per guide | path choice → chapter 4 path |
| 4. Peace Time | none | no | no | no | no |
| 5. The Revolt | none | no | no | no | no; endings only |

Confidence is highest for chapter 1: a wiki page lists every scene, the exact
button text, the condition, and the stat/relationship effect.

## Sources

- Fandom wiki, *Chapter 1. Childhood* — scene chronology, exact choice texts,
  conditions, effects (also the special scenes and the destinies).
  <https://the-life-and-suffering-of-sir-brante.fandom.com/wiki/Chapter_1._Childhood>
- GamePretty, *All Choices Guide (Chapters 1–3)* — scene order and the effects of
  each answer for chapters 1–3; states that chapters 4–5 have "too many branches"
  and are not described.
  <https://gamepretty.com/the-life-and-suffering-of-sir-brante-all-choices-guide-chapters-1-3/>
- Steam guide, *Selection of Fate: Rebel Lotless Path* — a full playthrough with
  the exact choice text picked at each scene of chapters 1–2.
  <https://steamcommunity.com/sharedfiles/filedetails/?id=2825258690>
- TheGamer, *Chapter Three: Youth* — unlock conditions for the three Lots and for
  several destinies.
  <https://www.thegamer.com/life-suffering-brante-chapter-three-youth-arrival-three-lots-decisions/>
- **Steam guide, *Spreadsheet with Events, Conditions and Consequences* by
  Brian Sirith** — the community Google Sheet this repository now ships as
  `data/events.csv` / `data/events.json`: 175 events with every option, its
  requirement and its outcome, across all five chapters.
  Guide: <https://steamcommunity.com/sharedfiles/filedetails/?id=2577499714>
  Sheet: <https://docs.google.com/spreadsheets/d/1TEZ2C3KZdPFUxLOg_pEkNWZOjNUKGVX-LbbeeX5ahmY/edit?usp=sharing>
  Credit: Brian Sirith transcribed the events and options by hand, with
  corrections from Gowther32 and other commenters. The guide asks for credit if
  the data is reused; this file and `data/events.json` carry it.
- Fandom path pages (*Path of the Nobleman / Priest / Lotless*) — chapter 4–5
  route descriptions and the ending names.

## What is missing

1. **Edges: which event follows which option.** The sheet gives order by year
   and path, not transitions. This is the one structural gap (see the trace
   system below).
2. **Scene id ↔ event title mapping.** The mod sees ids like `01.05.01`; the
   sheet uses titles. Six are confirmed from our own logs; the rest must come
   from traces (a scene line right after a click names the destination).
3. **Which answer was picked in old saves.** The game stores only the most recent
   click (`CurrentConsequence`), so pre-mod decisions cannot be recovered.
4. **Condition semantics** are listed per option in the sheet (e.g.
   `Willpower ≥ 0`, `Sophia: Trusting`) but not in the game's own data format;
   matching the two is only needed if the mod wanted to predict available options.

## Getting the missing edges (the system)

The mod now writes, per main save:

```
<data>/Brante-Save-Load/Runs/<save>/trace.log
2026-10-27 21:12:03.114  scene   01.05.01          Additive
2026-10-27 21:12:03.512  scene   Window_Destiny    Additive
2026-10-27 21:12:41.900  click   01.05.01          ButtonDescription2 | Hug mother
```

The edge is the **first `scene` line that follows a `click` line**: that is the
scene the choice leads to. Merging traces from several playthroughs accumulates
edges over exactly the branches that were played.

To make the graph complete, edges must be *exercised*:

- **Cheap now:** let players (and our dev runs) accumulate traces; merge them.
  Coverage grows with play, and the tree can already show "options whose
  destination is known".
- **Systematic:** a dev harness that, at each captured choice, restores the
  snapshot and clicks each offered button in turn, recording the resulting scene
  from the trace. That is one run per option — feasible per scene the player can
  reach, but the option set itself depends on stats, so covering all state
  combinations is combinatorial.
- **Static:** parse the Bolt assets (the flow machines) offline. That is a
  reverse-engineering project of its own; the Bolt graph format would have to be
  decoded, and the "next scene" often comes from a Bolt event, not a field.

## Building the graph (automated)

`tools/build_graph.py` joins the three sources and writes `data/graph.json`:

```sh
python3 tools/build_graph.py                 # auto-detects the Proton prefix
python3 tools/build_graph.py --runs PATH     # or point at .../Brante-Save-Load/Runs
```

What it does:

1. reads every `Runs/*/trace.log` (scene loads and clicks);
2. pairs each `click` with the **first story scene** loaded after it
   (`GameLoadingScreen`, cutscenes and windows are skipped; story scenes match
   `NN.NN.NN`), which is the edge: choice → next scene;
3. reads every snapshot sidecar for the scene's id, title and options, and
   takes the title from the title **key** (`<scene>_Title`), because an older
   sidecar could record the previous scene's controller;
4. matches the community sheet's events to scene ids by title (exact or
   containment, with option-text overlap as a fallback), one scene per event;
5. writes `scenes`, `events` (title → scene id) and `edges`
   (`from`, `fromEvent`, `button`, `text`, `to`, `toEvent`, `count`).

Current result on the test save: **3 events mapped** (`01.03.01` The Lot of
Suffering, `01.04.01` Gloria's Rhymes, `01.05.01` Newborn's Cry) and **0 edges**,
because no choice has been clicked since tracing was added.

A synthetic trace (click → loading screen → next story scene) was used to test
the pairing: both edges resolved with the right option text.

## Reading the graph straight out of the game (Bolt)

This turned out to be much more open than expected. Every Bolt machine stores its
graph as a **JSON string** inside the Unity asset, starting with
`{"graph":{"variables"`. It contains:

- `elements[]` — units (`"$type":"Bolt.InvokeMember"`, `"Bolt.SwitchOnString"`,
  `"Bolt.Branch"`, `"Bolt.SuperUnit"`, …) and connections
  (`Bolt.ControlConnection`, `Bolt.ValueConnection`) that reference units by
  `$id`/`$ref`;
- `defaultValues` and `Bolt.Literal` units — the actual strings, including scene
  names (`LoadingScreen_Child`, `070.240.130_FateOfTheFamily13`, …);
- `SuperUnit` entries with `nest.macro = N`, i.e. references to macro assets
  that hold further graphs.

`tools/bolt_extract.py` extracts all of it in under a second:

```sh
python3 tools/bolt_extract.py            # -> data/bolt-graphs.json (~1.1 MB)
```

Current result: **481 graphs**, 445 of them calling
`GameManager.LoadScene(sceneName)` with a literal name, and 9 handling a `Click`
custom event. The story flow is: `ParameterButtonChanger.Button_Click` →
`CustomEvent("Click")` on `TalismanFlowMachine` → the `GameManager` graph's
`SwitchOnString`/`SwitchOnInteger` dispatcher → per-chapter `SuperUnit` macros →
`LoadScene`.

**Blocker found while wiring the interpreter:** a `SuperUnit` refers to its macro
by a small index (`"nest":{"source":"Macro","macro":0}`), and that index is *not*
inside the graph JSON — it points into the owning machine's serialised macro
list. UnityPy cannot decode the typetrees of the game's own scripts (no type
trees are shipped), so the index cannot be resolved from the asset structure.
Port-signature matching is ambiguous (233 graphs expose `Enter`/`Exit`), and the
SuperUnit's value ports (`CutsceneName`, `Turn`, …) come from the macro, so they
cannot be used to disambiguate either. Resolving this needs either:
- parsing the FlowMachine MonoBehaviour bytes by hand (its macro PPtr array), or
- Unity type trees (not shipped in a release build).

**Solved.** The MonoBehaviour's serialised layout is

```
header(12) | m_Enabled(4) | m_Script(12) | m_Name(len+bytes) | graph JSON | macro PPtr array
```

and the trailing array is `count | count * (fileID int32, pathID int64)`. For the
`GameManager` machine it holds exactly **16 entries — one per `SuperUnit`** — so
`nest.macro = N` is the N-th pathID in that array. `tools/bolt_graph.py` reads the
layout, resolves macros, and walks control flow from the `Click` event through
switches/branches and nested macros to `LoadScene`:

```
GameManager  switch=%Yes / macro:GameFinals / ... -> LiveTimeline
GameManager  switch=%No / switch=%1 / macro:LoadScreen -> LoadingScreen_Child
```

**Where the graph stops.** The chapter macros (`ChildhoodChapter`, `TeenChapter`,
…) reference **one macro per scene, named after the scene id** (`01.05.01`,
`01.06.01`, `01.N.01`, `ChildhoodFinishScreen`, …). That catalogue is exactly the
scene list of the chapter. But a scene macro's own graph only loads *itself*
(`01.05.01 → LoadScene("01.05.01")`), and no switch anywhere uses scene ids as
ports (0 of 482 graphs). Scene files do not carry a successor table either: only
one of the 25 scene files with a consequence key also names another scene
(`level34` → `01.06.01`); the rest name only themselves.

So the per-choice transition is decided elsewhere. Two more things were ruled out
or narrowed:

- `SceneStateMachine` is a **C# MonoBehaviour**, not a Bolt state machine: the
  Bolt state-graph types (`Bolt.OnEnterState`, `StateGraph`, …) do not appear
  anywhere in the game files.
- The `GameManager` `Click` dispatch switches on `GetKeyChapterParameterValue
  ("GoToFinals")` (`%Yes`/`%No`) and then on `GetMember Chapter` (`%0`…`%5`) —
  that routes to the chapter macros, i.e. chapter flow and finals, not choices.

The chapter macros do give the game's own **scene catalogue** (one macro per
scene), which `tools/bolt_scenes.py` exports:

```sh
python3 tools/bolt_scenes.py            # -> data/scene-catalog.json
```

75 distinct scenes: Childhood 18, Teen 21, Youth 29, Peace 12, plus the finals
handled outside the chapter macros. It is a set, not an order — ids mostly
follow the story, but variants break that (`01.02.01_2` Hide and Seek comes
*before* `01.02.01` The Great Descent in an actual playthrough).

**Scene titles and answers for every scene (solved).** The I2 localisation table
is a plain CSV inside `resources.assets` (header
`Key,Type,Desc,Russian,English,Chinese`), so the game's own text is extractable
offline:

```sh
python3 tools/localization.py     # -> data/localization.csv, data/scene-texts.json
```

17 259 terms; **61 scenes with a title, 50 with their full answer list**, e.g.

```
01.05.01  The Newborn’s Cry
          ButtonDescription1  You run to Father’s study as fast as you can …
030.040.030_Preparation  Before the March
```

The mod ships this as `scene-texts.tsv` (loaded by `SceneTexts.cs`), which is what
lets the history list answers for scenes that were played before the mod existed
and therefore have no snapshot.

That means the tree does not need the unplayed-branch edges at all for the
"current playthrough" view: the played path comes from `ShowedScenesList` (exact,
in order), the alternatives at each step come from this table, and the answer
actually taken comes from the recorded `chosen=`/next snapshot.

Remaining unknown (only for *unplayed* branches beyond one step): the transition
order itself. It is not in the flow graphs,
not in the scene files as a table (1 of 25), and not in a Bolt state machine. The
most likely holder is the C# `SceneStateMachine` per scene (its serialised event
fields) or the story `TextBlock` data. Traces already record the true order for
every path that is played.

Everything extracted so far is committed (`data/bolt-graphs.json`,
`data/bolt-edges.json`).

What would then remain is an interpreter for the dispatcher: follow control flow
from the `Click` event, evaluate the switches/branches, descend into `SuperUnit`
macros (resolving `nest.macro` to its graph) and record the `LoadScene` literal.
The branch conditions (stats/relations) come along for free as strings.

Online references used for this:

- Bolt 1.4 API: `FlowGraph` (`controlConnections`, `elements`), `FlowGraphAsset`
  / macros, `ControlConnection` — <https://docs.unity3d.com/Packages/com.unity.bolt@1.4/api/Ludiq.Bolt.FlowGraph.html>
- Bolt manual: *Graphs, Machines and Macros* (embedded graphs vs macro assets) —
  <https://docs.unity3d.com/Packages/com.unity.bolt@1.4/manual/bolt-graphs-machines-macros.html>
- `EditorSerializationUtility.DeserializeYamlAsset(asset, "MonoBehaviour", "_data")`
  documents that the graph lives as serialised data on the component.
- Asset extractors, if a type-tree dump is ever needed: AssetRipper
  <https://assetripper.github.io/AssetRipper/> and UnityPy
  <https://github.com/K0lb3/UnityPy/> (not needed for this game: the JSON is
  plain text in `resources.assets`).

## Getting edges in practice

- **Normal play**: every click is traced, so edges accumulate as the game is
  played. That is the cheap, reliable path.
- **Dev harness**: the choice buttons are unreachable right after a load. The
  book and its `TextController` pager appear only after the scene's own show
  animation, and paging alone does not make the answer panel (`ControlPanel` /
  `ChoiseDescription`) appear in the harness — the scene state machine has to be
  driven too. Attempts so far: `SimpleTextController` (not present), then
  `TextController.NextPage()` in a 60 s loop (panel never appeared). The inputs
  gathered (scene hierarchy, components) are in the log; next step would be to
  drive `SceneStateMachine`/`AutoFlip` instead of only the text pager.
- **Static Bolt parsing**: still the only way to get *all* edges without
  playing; out of scope (see above).

## Recommended plan

1. Keep collecting `trace.log` from normal play and dev runs; a small merge
   script folds them into an `edges` map (event id → option → next event id).
2. Use the dev harness to click each option of the scenes we can capture, which
   fills most of the early graph — the part players rewind to. Chapters 4–5 need
   traces from those chapters (or save-editing to reach them).
3. When edges exist, render the full tree: event nodes from `events.json`, the
   current save's path highlighted by matching scene ids and recorded clicks.
