# Design: save/load, rewinds and the story tree

Engineering design of the mod's save side. Game facts come from
[docs/FINDINGS.md](FINDINGS.md), item status from [docs/TODO.md](TODO.md); code
references are to `Brante-Save-Load/Scripts/`.

## 1. Goals and non-goals

Goals: return the game to any choice point already reached, and to any scene the
tree can plan a path to (a "Load scene" replay); let the game keep its own save
system working (one rolling autosave, chapter files, Continue, death -> restart
chapter, all following the rewound timeline); never lose the player's pre-mod
state (one immutable copy of the save folder, a checkpoint before every rewind);
keep the mod's saves outside the game's, so its load window cannot see them.

Non-goals: not a story-graph mod (branching lives in Bolt machines; the mod knows
the offline-extracted flow plus what was played); not a save editor and not a
replacement save system (it never edits state inside a save, and the live save
stays the game's).

## 2. Save ownership model

One playthrough = one `SaveSlotInfo.Id` (contains slashes, e.g.
`Biggie_10/23/2026103816AM`). It is the game's, not the mod's.

| location | owner | written by | contents | retention |
|---|---|---|---|---|
| `Saves/<id>/Autosave.dat`, `Chapter_N.dat`, `testSave.txt` | game | the game | the live timeline | game's own |
| `Brante-Save-Load/Backups/<id>/` | mod | one-time baseline | copy of `Saves/<id>/` (loose root files -> `_no_save/`) | never re-copied or pruned |
| `Runs/<id>/Choice/` | mod | `Recorder.Capture` | one snapshot per decision, content-hashed | never pruned; tree only |
| `Runs/<id>/Checkpoints/` | mod | `BeforeModLoad`, `BeforeGameLoad`, `BeforeChapterRestart` | state the rewind replaced, `kind=checkpoint`, `reason=` | newest 100 |
| `Runs/<id>/Autosaves/` | mod | `MirrorGameAutosave` | copy of the game's `Autosave.dat`, `kind=autosave` | newest 100 |
| `Runs/<id>/Manual/` | mod | `SaveManual` ("Save now") | same blob format | never pruned |

`Backups/<id>/` is staged as `<id>.tmp-<pid>`, gets `MANIFEST.txt` (name, size,
SHA-1), `README.txt` (restore steps) and `.complete`, then is moved into place. A
folder from an older version without `.complete` is kept and only marked.
`Brante-Save-Load/.baseline` makes the pass one-shot; `EnsureBaseline` is the
fallback before a load.

The game's autosave (`SceneState_GAME_SETUP`) is a resume point; a choice
snapshot (`SceneState_SHOW_ANIMATION`, scene still pending) is a decision. They
are kept apart: identical bytes in `Autosaves/` and `Choice/` must not be
offered as the same thing.

Why no minted save folder: an earlier build minted one when the game had no id,
splitting a timeline into two folders that adopted each other's id and produced
saves the game could list but not delete. The folder is the id; a run without
one keeps an empty id.

## 3. Loading a mod save

`ChoiceWindow.LoadFlow(datPath)`:

1. Deserialise first. A read failure leaves the window open.
2. Normalise: `SlotInfo.Id == Recorder.NoRun` -> empty; `EnsureSceneListed`
   adds a scene the game would have listed a moment later.
3. Resolve the run: the blob's real id (with slashes) wins over the folder it is
   filed in, so a cross-run checkpoint loads as its own run; else
   `RunIdForSavePath`. A stand-in start (another run's `Chapter_1.dat`, or one
   built from the game's defaults) loads with the live hero name, mode and
   main-save id.
4. `Recorder.BeforeModLoad(realId, scene)`: `EnsureBaseline` for the live and
   target run, then one live checkpoint, filed with both runs when they differ.
5. Close the window and the pause window; `AdoptAsCurrentSave(slm, helper)`
   points the slot bookkeeping at this save on every `SaveLoadManager` the game
   may use (`Recorder.SaveManagers()`), then `BeginModLoad`.
6. `SaveLoadManager.LoadGame(helper)` — public, restores every singleton and
   resumes via `LastOpenScene`.
7. After 0.5 s: additive `GameLoadingScreen`. Unload every loaded copy of the
   story scene on screen (the game unloads `GetActiveScene()` only after the new
   copy's `Start`), then broadcast `StartSceneTransitionEvent`.
8. The game's own sequence takes over: dissolve -> `AutomaticSceneLoad` ->
   `LoadScene(LastOpenScene)`, because the scene is still pending.

Load scene replay: `PlanLoad` builds `StartPath`, `Picks` and ordered `Order`;
`ReplayTo` loads the start (same flow), then `AutoPlay.Run(pick, onScene)` gives
one answer per scene, presses the continue/page/pop-up buttons a player would,
and stops at the target scene, which is left unanswered. A death caused by the
path's own answer is accepted via the game's "go on" choice; any other death,
the fourth, a blocked pick, a scene off the path or a skipped scene stops the
run and opens the tree there with the reason in `Palette.Red`.

The game's autosave continues normally: the adopted slot makes `SaveData` and
`SaveChapterState` write into `Saves/<id>/`, and `_currentSaveFile` gives chapter
restart the right file. A replay produces one mirrored autosave (the per-scene
mirror is skipped while `AutoPlay.Running`).

## 4. Run identity

| trigger | hook | effect |
|---|---|---|
| game Load / Continue | postfix `LoadGame(int)` | `MarkGameSave`: run from the live slot, `_modTimeline = false`, plus a checkpoint of the run's `Autosave.dat` (the main menu has no live state) |
| new game | postfix `CreateSaveLoadSlot` | `MarkGameSave(true)` |
| death -> restart chapter | prefix/postfix `LoadGame(string,int)` | `BeforeChapterRestart` + `MarkChapterLoad` |
| mod load / replay | `Recorder.BeginModLoad` | run pinned from the save, `_modTimeline = true` |

Rules:

- Ids are recovered, never invented. Save files can carry an empty id (older
  builds); the folder name is the id. `RecoverId` fills it from the path
  (`IdFromSavePath`), a postfix on `LoadSaves()` matches helpers back to files
  by hero + save date only when that pair names exactly one file, and
  `RepairIds` writes the result back into `Saves/**/Autosave.dat` once at
  startup. `RepairLooseRoot` merges a loose `Saves/Autosave.dat` into the one
  run whose scene list it opens (same hero, shared prefix >= 5, unique best
  match) and quarantines what it replaces.
- Slot repair only on a mod timeline: `EnsureSlotId` fills a missing id and
  never changes a non-empty one; `FixSaveFolder` redirects only the bare
  `Saves/` root and creates the folder it redirects to. A save the player
  loaded from the game's menu is left alone.
- `_no_save` (`Recorder.NoRun`) is a folder name for saves recorded while the
  game had no id. It is never pinned, adopted or written into a save's
  `SlotInfo.Id`: doing so made the game autosave into a folder nothing creates,
  and the throw left the book blank. An empty id is a valid current run and is
  never replaced by a previous run's.

## 5. Story path model

Shipped data: `scene-texts.tsv` (titles, answers), `scene-flow.tsv`,
`scene-effects.tsv`, `scene-params.tsv`. At runtime the save's
`ShowedScenesList` is the played path; the tree adds columns ahead from the flow
table.

- Flow is per occurrence: `scene-flow.tsv` has one row per scene + switch label
  set, keeping each occurrence's discovery order, so two branches of a switch
  do not collapse into one linear order.
- The chapter flow restarts at the top after every scene: the game re-enters the
  chapter macro and takes the first gate whose time, labels and condition pass
  and whose scene was not shown. `SceneFlow.After` therefore returns every
  unshown scene of the chapter in flow order, not only scenes after the current
  one; a triggered scene placed earlier can be the next scene.
- Time gates: `Ahead.Immediate` marks a candidate whose `Turn`/`Season` gate
  matches the live game (or a Turn-0 trigger). Sorting puts immediate scenes
  (flow order), then later time slots, then later chapters; turn/season
  progression stays the game's.
- Conditions after the current answer: `WorkOutOpenAnswers` stores each
  column's `AfterHero`; `SceneFlow.After(..., hero)` briefly swaps that hero's
  stats, characters and unlocked objectives into the live managers, evaluates,
  then restores. A pick in a current or played scene rebuilds the columns.
- Key parameters: `SceneParams` applies `scene-params.tsv` — the
  `KeyChapterParametersOnEnableChanger` on scene open (button 0) and the
  `KeyChapterParametersChanger` on the answer's button index — from the start
  save's `ChapterParameters`. `Simulate` marks the plan speculative when the
  next scene no longer fits its switch labels.

`PlanLoad` pre-checks: target and current column known; divergence is the first
changed answer at or before the target; start = nearest snapshot (or
chapter-start save) at or before divergence; every scene from start to target
needs a known, open answer; `StartLooksOnPath` requires the start save's
`LastOpenScene` to be that scene and its scene list to be a prefix of the path
(chapter files use the timeline test; stand-ins and a built start are exempt).

The runtime check is the ordered replay: `ReplayTo`'s `onScene` compares the
scene on screen with `plan.Order[next]`. Off-path -> `ReplayOffPath`; a skipped
plan step -> `ReplaySkipped`; either stops and names the scene. Planning is
best-effort, replay is authoritative.

## 6. Table and HUD

One window, opened from the book; the tree view tab sits above the save table.

- Categories: `Game autosave`, `Before load`, `Manual`, `Original save`
  (`Backups/<id>/Autosave.dat`, read-only, no delete cell). The live
  `Saves/<id>/Autosave.dat` and choice points are not listed.
- The first header ("Save type") is a filter button, not a sort: it cycles
  `All -> Game autosave -> Before load -> Manual -> Original -> All`, reset to
  All when the window opens. Other headers sort (again to reverse).
- Columns: Type, Saved, Chapter, Age, Year, Season, Turn, Scene. Chapter drops
  the number prefix; Saved comes from the sidecar's `recorded=` line.
- State dedupe: rows are collapsed by `Recorder.StateSignature` (the blob hashed
  with `SlotInfo.Id`/`Date` cleared) within a kind, so repeated writes of one
  state are one row. The newest file is the row; the others become its copies
  and are deleted with it. `DedupeStores` prunes automatic stores by signature
  at startup; manual files are never removed automatically. Identical states
  are not merged across categories.
- Paging: rows are built 120 at a time, with a "Show more" row. Type cells show
  the `Loc.K.Help*` explanation on hover in the tree's paper/outline pop-up.
- Layout is flushed with `Canvas.ForceUpdateCanvases()` after building; the
  game's TMP build needs wrap off + `ForceMeshUpdate` to size text.
- The tree view is a separate tab: columns per scene (played, then ahead),
  answers stacked, path highlighted, hover pop-up with effects and live
  requirements, clicks build a path, `Load scene` runs the replay.

## 7. Testing and verification model

Dev markers in `Snapshots/`, one-shot, all paired with `dev-autoload.txt`
except the table/book drivers:

| marker | drives |
|---|---|
| `dev-autoload.txt` | vanilla load of the newest autosave |
| `dev-load.txt` | load one `Runs/` save through the table path |
| `dev-plan.txt` + `dev-replay.txt` | log a plan and its pre-checks; replay it |
| `dev-capture.txt`, `dev-stat.txt`, `dev-keyparam.txt` | capture after changing a stat / key parameter, so a plan starts from it |
| `dev-uitest.txt`, `dev-table.txt` | table open/confirm/delete/load; filter cycle, row counts, type pop-up |
| `dev-book.txt` | tabs, table, tree: wheel, pop-ups, conflicting pick, plan, undo |
| `dev-loadpath.txt` | Load scene end to end (`back`, `chapter`, `start`, `ahead`, `death`, `offpath`, `first`, `new`, `file`) |
| `dev-autoplay.txt` | debug runs that make test saves at named chapters |
| `dev-history.txt`, `dev-scene-tree.txt`, `dev-bolt.txt`, `dev-ironman.txt`, `dev-lang.txt` | history dump, UI dump, Bolt dump, Iron Man load, language switch |

Documented as verified in game: bootstrap and probe; capture and dedup; a mod
load reopening the scene byte-exactly; startup repair, folder rename,
loose-root merge, baseline once with unchanged hashes; the autosave mirror; a
checkpoint before a mod load and a cross-run checkpoint with blob-first
resolution; the table with all four categories and a row of each kind loading;
the same-scene load leaving one scene copy; the table type filter cycle
(All 18 -> Game autosave 7 -> Before load 8 -> Manual 2 -> Original 1 -> All)
and a type pop-up opening; the per-branch plan
`02.07.02 > 02.08.01 > 02.09.01`; the `Cunning >= 4` Poets replay
`02.07.02 > 02.N.03 > 02.08.01 > 02.09.01`; chapter 1's tree and conditions
(chapters 4-5 offline only); a two-answer replay; language switch.

Pending in game (not to be claimed): the "Show more" paging and loading the
Original row, the complete `dev-loadpath` matrix after the unload change, a
game-menu load and a death -> restart checkpoint, a replay adding no per-scene
mirror entries, achievements after a load, a full playthrough pass, and the
`.baseline` marker path across a restart.

Regression checks after a run: the folder set under `Saves/` unchanged except
the run's own files and no files at the `Saves/` root; `Backups/<id>/` hashes
matching `MANIFEST.txt`; one loaded copy of the story scene and one answer per
`ButtonIndex` (`Recorder.UniqueButtons`, `DedupeSidecars`); table rows equal to
distinct state signatures per kind with no choice points; every save the game
reads carrying a non-empty id; no `GAME Exception` in the log from a mod step.

## 8. Failure modes and invariants

| must never happen | why | prevention |
|---|---|---|
| mint a game save folder | splits one timeline, ids collide, undeletable saves | no minting code; ids only recovered from folders or the live slot |
| write at the `Saves/` root | the game's own list shows them; nothing deletes them | `EnsureSlotId` (mod timeline only); `FixSaveFolder` redirects the bare root and creates the target |
| duplicate the live scene | double answers, `TextController` out of range, two sidecars per answer | unload every copy of the previous story scene before the transition; `UniqueButtons`; sidecar dedupe at startup |
| overwrite the baseline | destroys the only pre-mod state | one-time `.baseline`, `.complete` guard, staged copy then move, never re-copied |
| lose the state a load replaces | a rewind could not be undone | checkpoint before mod loads, game-menu loads and chapter restarts; filed with both runs on a switch |
| empty slot id on a live save | chapter restart falls back to `ResetToDefault` | id written into stored saves; `RecoverId` on read; `RepairIds` at startup |
| stale adopted run | saves land in another run's folder | run set only at the four save-changing hooks; a real blob id beats the folder; an empty id is valid |
| `_no_save` reaching the game | autosave throw, blank book | `RealOrEmpty` maps it to `""`; it is a folder name only |
| mod saves in the game's load window | the game could delete them | different folder, different code path, nothing registered with `LoadSaves` |
| a failed write during autosave | scene setup stops, blank page | repaired folder created before the game writes; game errors copied into the mod log |

## 9. Open questions / future work

- Objectives are simulated from the start save / hero (`SceneFlow.After` swaps
  their `Unlocked` flags), but objectives set by a scene event after that state
  are not: extract the Bolt `SetMember`/objective units per scene for
  `SceneFlow.After` and `SceneParams.Simulate`.
- Exact pre-answer branch prediction: labels and conditions are evaluated on the
  state before the answer, so a switch set by the scene's events can still
  change the branch; the replay stops at the first divergence meanwhile.
- Performance: the tree scans folders and builds the playthrough plus ahead
  columns per open; caching across opens is open.
- Same-scene edge cases: a load while a pop-up or consequence page is open, and
  a load during a replay, have no test.
- Achievements: a replay can re-grant a held achievement (Steam ones are
  idempotent) — needs an in-game check. Steam Cloud is out of scope: Auto-Cloud
  is disabled for the app and the save path is not recursive.
- Verification debt: section 7 and phases 8-13 in [docs/TODO.md](TODO.md).
