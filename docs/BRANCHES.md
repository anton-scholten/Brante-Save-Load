# Branch data for the full choice tree

This folder holds what is known about the game's choices, gathered from public
guides so the history view can eventually show the *whole* tree, with the path of
a given save highlighted. It is a starting point, not a complete graph — see
"what is missing" below.

- `data/branches.json` — machine-readable: chapters, scenes, choices, conditions,
  effects, destinies, endings, plus the in-game scene ids we have confirmed from
  the mod's own logs.
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
- Steam guide, *Spreadsheet with Events, Conditions and Consequences* — a
  community Google Sheet with ~175 events (title, requirements, options,
  outcomes), kept in a link inside the guide; not machine-read here.
  <https://steamcommunity.com/sharedfiles/filedetails/?id=2577499714>
- Fandom path pages (*Path of the Nobleman / Priest / Lotless*) — chapter 4–5
  route descriptions and the ending names.

## What is missing

1. **Edges: which scene each choice leads to.** No guide lists them, and the game
   does not store them (branching lives in Bolt machines). Without edges a tree
   can show what was offered, but not where the *other* branches would have gone.
2. **Chapters 4 and 5**: no public scene-by-scene data at all — only the three
   route spines (judge, inquisitor, print-shop agent), a few named decisions and
   the ending list.
3. **Exact choice texts for chapters 2 (most options), 3, 4, 5.** Chapter 2 has
   one playthrough's wording; chapter 3 only has effects.
4. **Conditions (which options appear at all).** Known for chapter 1; partial
   elsewhere. In-game this is data on each `ParameterButtonChanger`
   (`Condition`, `ConditionsByRelations`, `ChecksByStatus`, …) and could be read
   per scene, but only for scenes the player can reach with the current state.
5. **Scene id ↔ title mapping.** The mod knows ids like `01.05.01`; the guides
   use titles. Four are confirmed (Birth, Hide and Seek, The Great Descent,
   The Lot of Suffering, Gloria's Rhymes, The Newborn's Cry).
6. **Which answer was picked in old saves.** The game stores only the most recent
   click (`CurrentConsequence`), so decisions made before the mod was installed
   cannot be recovered from the save.

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

## Recommended plan

1. Keep collecting `trace.log` from normal play and dev runs; add a merge step
   (small script) that folds them into `data/branches.json` as `edges`.
2. Use the dev harness to click each option of the chapters 1–2 scenes we can
   capture (the state is already valid there), which yields most of the early
   graph — the part most players rewind to.
3. Treat chapters 4–5 as "options only, destinations unknown" until traces from
   those chapters exist.
4. When edges exist, render the full tree: static nodes from `branches.json`,
   the current save's path highlighted by matching scene ids and recorded clicks.
