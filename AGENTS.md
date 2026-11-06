# Working on this repository

Notes for anyone, human or AI agent, changing this mod. The [README](README.md) is
for people who just want to use it.

## What it is

A mod for [The Life and Suffering of Sir Brante](https://store.steampowered.com/app/1272160/)
(Unity 2018.3, Windows build under Proton): load the game at **any choice you have
already reached**, plus a save/load system of its own whose saves live outside the
game's own. Two tabs the mod adds to the top of the book open its window: the
tree of past choice points, and the table of saves it can return to. Clicking an entry returns the
game to that exact state. [docs/FINDINGS.md](docs/FINDINGS.md) explains how the
game works and where every fact used by the mod came from.

## Layout

```
Brante-Save-Load/            the folder that gets installed into the game dir
  Brante-Save-Load.dll       built by tools/build.sh; the Doorstop entry assembly
  0Harmony.dll               vendored Harmony (copied by the build from vendor/)
  scene-texts.tsv            every scene's title and answers (from the game; see data/)
  scene-flow.tsv             every chapter's scene gates: flow row(s) per scene + conditions (from the game)
  scene-effects.tsv          every answer's stat/relation/status changes (from the game)
  scene-params.tsv           key chapter parameter changes by scenes/answers (from the game)
  scene-objectives.tsv       the story objectives scenes/answers unlock (from the game)
  Scripts/*.cs               the source
    Entrypoint.cs            Doorstop entry: Harmony patches, host creation
    ModLog.cs                file logger (usable before Unity is up)
    ModHost.cs               host component; probes, dev markers, startup repairs
    Recorder.cs              choice snapshots, checkpoints, autosave mirror, baseline,
                             run tracking, id repair, traces
    ChoiceWindow.cs          book tabs, save table (type filter, pages), tree view,
                             path picks + Load scene
    AutoPlay.cs              plays scenes by itself (Load scene replay, debug runs)
    History.cs               the save's scene path + offered answers
    SceneTexts.cs            loader for scene-texts.tsv (titles + answers)
    SceneFlow.cs             scene-flow.tsv: upcoming scenes, condition evaluator
    SceneEffects.cs          scene-effects.tsv: what each answer changes (tree pop-up)
    SceneParams.cs           scene-params.tsv: key chapter parameter simulation
    SceneObjectives.cs       scene-objectives.tsv: objectives scenes/answers unlock
    StatPanel.cs             tree's stat columns: clones of the game's stat row
    Loc.cs                   the UI's own translations (11 languages)
    Palette.cs               every colour the UI uses (fades, not near-copies)
    BoltDump.cs              runtime dump of the choice machine (dev only)
doorstop/doorstop_config.ini the Doorstop config installed into the game folder
vendor/                      vendored third-party binaries + their licenses
  doorstop/winhttp.dll       Unity Doorstop 4.6.0 (x64), LGPL-2.1
  0Harmony.dll               Lib.Harmony 2.3.3 (net472), MIT
tools/build.sh               compile against the game's own Managed/ DLLs (UI, TMP, Audio modules)
tools/install.sh             build, then symlink/copy into the game folder
tools/steam.sh               Steam library lookup (distro/Flatpak/Snap), sourced by both
tools/gamedir.py             the same lookup for the Python tools (+ the prefix's Runs/)
tools/nrbf.py                read a .dat save (BinaryFormatter) outside the game
tools/localization.py        extract titles/answers -> data/, shipped TSV
tools/bolt_extract.py        dump every Bolt graph as JSON (no UnityPy needed)
tools/bolt_graph.py          Walk Bolt graphs (macro resolution, LoadScene)
tools/bolt_story.py          every chapter's scene flow + conditions -> shipped TSV
tools/scene_effects.py       answer effects from the scene files -> shipped TSV
tools/scene_params.py        key chapter parameter changes from scenes/answers -> shipped TSV
tools/scene_objectives.py    the objectives scenes/answers unlock -> shipped TSV
tools/build_graph.py         merge traces + snapshots + event sheet
data/                        extracted game data (see docs/BRANCHES.md)
docs/                        FINDINGS (game internals), BRANCHES (choice data), TODO
```

## Build, install

```sh
./tools/build.sh             # after editing any .cs  (dotnet SDK >= 6 required)
./tools/install.sh           # build + install: symlink Brante-Save-Load/, write
                             # doorstop_config.ini + winhttp.dll into the game dir
./tools/install.sh --copy    # copy instead of symlink
./tools/install.sh --uninstall                    # keep the player's data
./tools/install.sh --uninstall --restore-backup   # put the pre-mod save back
./tools/install.sh --uninstall --purge            # delete the mod's data
```

The build needs the game installed (it compiles against the game's own
`_Data/Managed` assemblies — no .NET Framework reference packs needed).
Set `BRANTE_DIR` if the game is not found, `DOTNET_DIR` for a non-default SDK.

## How the mod gets into the game

[Unity Doorstop](https://github.com/NeighTools/UnityDoorstop) (the same mechanism
BepInEx uses, minus BepInEx): `winhttp.dll` + `doorstop_config.ini` in the game
folder cause the game's Mono runtime to run `Doorstop.Entrypoint.Start()` before
anything else. There is no mod loader, no plugin framework — the repo is
self-contained. Doorstop files are the only things placed beside the game files,
and `--uninstall` removes exactly what was installed.

## Game data files (regenerate, don't hand-edit)

`Brante-Save-Load/scene-texts.tsv`, `scene-flow.tsv`, `scene-effects.tsv`,
`scene-params.tsv` and `scene-objectives.tsv` are extracted from the
installed game and shipped with the mod. Regenerate them after a game update with

```sh
python3 tools/bolt_story.py       # scene-flow.tsv (needs UnityPy); run first
python3 tools/localization.py     # scene-texts.tsv (+ data/localization.csv)
python3 tools/scene_effects.py    # scene-effects.tsv (needs UnityPy)
python3 tools/scene_params.py     # scene-params.tsv (needs UnityPy)
python3 tools/scene_objectives.py # scene-objectives.tsv (needs UnityPy)
```

`tools/bolt_*.py`, `tools/scene_effects.py`, `tools/scene_params.py`,
`tools/scene_objectives.py` and `tools/localization.py` need the
game installed; all but `localization.py` and `bolt_extract.py` need UnityPy (`pip install unitypy`, or PYTHONPATH pointing at it).
`tools/build_graph.py` is optional tooling that merges the mod's traces with the
community event sheet (`data/events.json`).

## Rules that are not obvious

- **The game's save system is BinaryFormatter** over a `[Serializable]` class
  (`SaveLoadHelper`) that holds the entire game state. The mod never creates or
  renames the folder of a save that has an id: a new game's folder comes from the
  game's own `CreateSaveLoadSlot`. The one exception is a save with **no**
  main-save id loaded through the mod (an old game, or one that lost its id):
  `Recorder.AdoptAsCurrentSave` gives it one with `NewSlotId`, built exactly as
  `CreateSaveLoadSlot` builds it (hero name + `_` + `DateTime.Now` without spaces,
  colons and dots), so the game's autosave and chapter files get a folder of their
  own and its menu lists the game. Otherwise the mod writes only inside its own
  folder (`Brante-Save-Load/`) plus the one-time id repair below.
- **One live game save per playthrough.** The game has exactly one rolling
  `Autosave.dat` in one `Saves/<id>/` folder, plus a `Chapter_N.dat` at each
  chapter start. Everything else is a copy the mod makes; the game's own save is
  never duplicated, so its Continue, autosave and chapter restart keep following
  the live timeline.
- **A save's folder is its id.** `SlotInfo.Id` contains slashes
  (`Biggie_10/23/2026103816AM`), so the folder is a nested path. Saves written by
  older builds can have an empty `SlotInfo.Id`, even sitting in the right folder;
  the id is filled from that folder wherever the game reads saves — Harmony
  postfixes on `SaveLoadManager.LoadSaves()` (matched back by hero + save date,
  only when unambiguous) and `SaveLoadManager.LoadData(string)` — and
  `Recorder.RepairIds` writes the recovered id back into `Saves/**/Autosave.dat`
  once at startup.
- **A loose `Saves/Autosave.dat` is merged into its run.** A game that lost its
  id saves in `Saves/` itself. `Recorder.RepairLooseRoot` merges it only into
  a run it continues: same hero, the run's whole scene list is the start of the
  loose one (>= 5 scenes - every run shares the opening), the loose file is
  newer than the run's `Autosave.dat`, and exactly one run shares the longest
  start. Then it moves it plus `Chapter_N.dat` and `testSave.txt` in,
  quarantining the files it replaces to
  `Brante-Save-Load/Backups/quarantine/<id>/`. Anything else stays in place
  (logged); an older loose state never replaces a newer run save.
- **The current run is tracked, never guessed.** It is set only where the game
  changes saves: postfix on `LoadGame(int)` (the game's own Load/Continue),
  `CreateSaveLoadSlot` (new game), `LoadGame(string,int)` (death -> restart
  chapter), and `Recorder.BeginModLoad` from the mod's own load path.
  `CurrentRunId()` prefers the live slot; an empty id is a valid value and is
  never replaced by a previous run's.
- **Slot repair only runs on a mod timeline.** `Recorder.EnsureSlotId` (prefix on
  `SaveGame`/`SaveChapterState`) fills a missing id only while the game plays a
  save the *mod* loaded, and never changes a non-empty slot.
  `Recorder.FixSaveFolder` (postfix on `GetSaveFolderPath`) only redirects the
  bare `Saves/` root. A load from the game's own menu is left alone and saves
  back into its own folder.
- **One baseline, taken once.** At the first start of this version (marker
  `Brante-Save-Load/.baseline` missing) every existing game save folder is copied
  to `Brante-Save-Load/Backups/<id>/` (loose root files to `Backups/_no_save/`):
  staged in `*.tmp-<pid>`, with `MANIFEST.txt`, `README.txt` and `.complete`,
  then moved into place. It is never re-copied or pruned; a backup from an older
  version (a folder without `.complete`) is kept untouched and only marked.
  `Recorder.EnsureBaseline(id)` is the fallback before a load.
- **Checkpoint before every rewind.** `Runs/<run>/Checkpoints/` gets the live
  state before every mod load (a single save or a Load scene replay), before a
  load from the game's own menu (a copy of the run's `Autosave.dat`, since the
  main menu has no live state), and before a death -> Restart chapter. Sidecar
  has `kind=checkpoint` and `reason=`. A load that switches runs also files the
  checkpoint with the target run, so that run's table shows it; the save's own
  `SlotInfo.Id` (the run it belongs to) wins over the folder it is filed in when
  it is loaded.
- **The four stores under `Runs/<run-id>/`:**
  - `Choice/` — choice-point snapshots, one per decision, content-hashed, never
    pruned. Only the tree view reads them; they are **not** in the save table.
  - `Checkpoints/` — pre-rewind checkpoints, newest 100 per run.
  - `Autosaves/` — a mirror of the game's own `Autosave.dat`, copied after
    `SaveGame`/`SaveChapterState` (hash-deduped, newest 100, skipped while
    `AutoPlay.Running` so a replay gets one checkpoint rather than one per
    scene). These are the game's resume points (`SceneState_GAME_SETUP`), not
    choice points.
  - `Manual/` — "Save now", never pruned.
  `Auto` was renamed to `Choice` by `Recorder.MigrateFolderNames`; readers fall
  back to `Auto` if the rename was skipped. `trace.log` stays at
  `Runs/<run>/trace.log`.
- **Save table categories.** `Game autosave` (mirror), `Before load`
  (checkpoints), `Manual`, and `Original save` (the baseline
  `Backups/<id>/Autosave.dat`, read-only, no delete). The live
  `Saves/<id>/Autosave.dat` is not listed, and identical states are not merged
  across categories (they are different artifacts). The first header, "Save
  type", is a filter button: it cycles `All -> Game autosave -> Before load ->
  Manual -> Original -> All`, resets to All when the window opens, and does not
  sort. Rows are built in pages of 120 with a "Show more" row.
- **Table rows are de-duplicated by state.** `Recorder.StateSignature` hashes
  the save with `SlotInfo.Id`/`Date` cleared, so two writes of the same state
  (the same scene saved twice, "Save now" pressed twice, a checkpoint filed in
  two runs) are one row; the newest is shown, older files are its copies, and
  deleting the row deletes every copy. Automatic stores are also pruned of
  duplicates at startup (`Recorder.DedupeStores`); manual files are never
  deleted automatically.
- **Load scene validates the picked path.** `PlanLoad` builds the choice-scene
  order from the start to the target, requires every answer to be one of the
  scene's choices and open, and checks that the start save is this path's
  (`StartLooksOnPath`: same `LastOpenScene` and a scene-list prefix). The replay
  walks that order: a scene not on it, or a scene that skips an earlier one,
  stops the run and opens the tree with the scene named. A path through scenes
  still ahead is logged as speculative. `dev-plan.txt` logs a plan without
  replaying it. A stop or a failed pre-check is shown in the tree's hint line in
  `Palette.Red` (the delete-mark red).
- **Same-scene loads unload the old copy first.** The game adds a scene and
  unloads the previous active scene only after the new copy's `Start`; loading
  a save of the scene on screen therefore left two copies (`14 buttons` for a
  7-answer scene, `TextController.Start` out of range). `LoadFlow` unloads every
  loaded copy of the scene on screen after `LoadGame`, makes `GameLoadingScreen`
  active, then broadcasts the transition. Answer sidecars are also de-duplicated
  by `ButtonIndex` (and repaired at startup by `Recorder.DedupeSidecars`), so a
  duplicate copy can never write each answer twice.
- **The chapter flow restarts at the top after every scene.** `scene-flow.tsv`
  has one row per flow occurrence (a scene can sit at different positions under
  different switch labels, e.g. `TriggerBlock`), and `SceneFlow.After` returns
  every unshown scene of the chapter in flow order that passes the live labels
  and conditions - not only scenes after the current one. A triggered scene
  whose flow position is earlier than the scene on screen (e.g. `02.N.03` after
  `02.07.02`) is therefore offered as the next scene, as the game does; the
  per-branch order keeps the true/false branches apart. The TSV is regenerated
  by `tools/bolt_story.py`.
- **Time gates can be ranges.** The flow TSV's turn column is `N` (the gate's
  `Turn == N`) or an inclusive range `N-M` / `N-` / `-M` for the trigger gates
  that compare with `<`, `<=`, `>=`, `>` (`054.100.090_Milanid` is
  `Turn >= 4 && Turn < 8`, i.e. `4-7`, Season any). Recording only the first
  constant (as `8`) put such scenes turns too late. `SceneFlow.InSlot` is the
  test; a row whose last turn has passed is dropped. Immediate scenes come first
  in flow order, then later slots by (turn, season) - the game moves to the
  earliest slot, not the next in flow order.
- **The scenes ahead are a forward simulation.** `AddUpcoming` steps a `Sim`
  (hero stats, characters, objectives, key chapter parameters, scenes shown,
  chapter, turn, season) from the scene on screen - or from the first played
  scene whose answer was changed, starting from that scene's snapshot - to the
  end of the story: `SceneFlow.Step` returns the first scene the flow would
  open (the simulated state is swapped into the game's managers only for the
  call, synchronously, and always put back - the game's per-frame achievement
  poll can never see it), its opening parameters apply, the picked (or played,
  or first open) answer applies its effects, objectives and parameters, then the
  scene's own objectives. Columns are added for scenes that offer answers. A
  pick anywhere re-simulates what follows it. Played entries after the scene on
  screen (an abandoned attempt in the save's list) get no columns.
  `SceneFlow.FirstCore` sorts candidates by the final order before evaluating
  any condition and stops at the first that passes: the whole story simulates
  in ~60 ms of flow time, and the tree builds in about 0.5 s.
- **A pick or Reset path is a re-prediction, not a rebuild (~60 ms).** The
  window keeps the played columns of the last full build (`_playedCols`) and
  `ResimulateAhead` predicts from them again, for a played or an upcoming pick
  alike; the history and snapshots are read only when the tree opens.
  `SceneCol` caches each scene's title, snapshot and answers per build
  (`History.SnapshotFor` touches the disk: ~2 ms a scene under Wine). The
  drawing is per column (`DrawColumn`, one container each): a column whose
  `ColDrawKey` (scene, flags, visible answers, chapter rule, "Upcoming" foot)
  is unchanged is kept - slid to its new index when a scene before it came or
  went - and its `NodeRef`s are re-pointed at the new column and choice
  objects before `StyleNode`. Restyling boxes that still pointed at the old
  prediction was why a pick in a predicted scene showed nothing.
- **Picks ahead are told apart from assumptions.** A predicted scene that the
  old path played keeps that answer by default (`PlayedChoice`); it is drawn
  pale yellow, a real pick full yellow. The "Pick an answer here to go on"
  note (`Loc.K.PickNext`, drawn in `DrawPath`) sits under the first scene
  before the dashed answer that has no answer - the one Load scene's
  `MissingPick` names, the scene on screen included - or else under the
  scene after the dashed answer. A dashed answer in the scene on screen has
  nothing to load (the game is there): the button's hint says so
  (`Loc.K.OnThisScene`) instead of "nothing to load".
- **Path lines and stat marks.** A picked answer lower than the next
  scene's last answer gets that scene's grey spine extended down to it
  (`DrawPath`, the spine's colour and width), so the red path never ends in
  the air. Hovering an answer marks the stat rows it changes
  (`StatPanel.HighlightChanges`: the hero at that scene before and after the
  answer's effects; `Palette.Good`/`Bad` at 28%, the game's consequence-row
  green and red); leaving the answer clears them.
- **Load scene shows its progress and blocks the player.** `ReplayTo` puts
  up a box on its own canvas (sorting order 9000, `DontDestroyOnLoad`) over a
  dim that catches every click; `AutoPlay` clicks through `ExecuteEvents`, not
  the pointer, so it is not blocked. "Scene N of M" counts the plan's choice
  scenes; the bar creeps within a scene and the time left is the pace so far
  (2.5 s a scene before the first is reached) times the scenes to go, on the
  real clock. It is removed in a `finally`, before the tree opens on a stop.
- **The load/delete dialog.** The question centred on top, the save's time
  and choice in a `Palette.Card` box, a note (delete, save now) under it. The
  row it is about keeps its colours and gets the red dashed outline: the dim
  is four shade rectangles around it (`ShadeAround`), the panel's own image
  stays the clear click target. A sorting canvas on the row did not lift it
  out of the table's masked scroll view.
- **Changers off the answer buttons apply with any answer.** In
  `scene-params.tsv` button `-1` is a `KeyChapterParametersChanger` on a
  button every answer passes - the text pages' `NextButton/Image`
  (`02.10.01` sets `TriggerBlock=true`, `02.10.02_Funeral_Ded` sets it back)
  or every answer's continue button (`Post` in the chapter-3 finals). It fires
  on a pointer press. `SceneParams.Apply` applies them with any answer;
  ignoring them let the simulation open the trigger scenes (`02.N.0x`, only
  under `TriggerBlock=false`) before Last Respects, and Load scene stopped
  there. `AutoPlay.Click`/`TurnPages` also fire changers on a pressed
  button's children, as a mouse press bubbling up from the child would.
- **Objectives unlocked by an answer or a scene are simulated too.**
  `scene-objectives.tsv` (from `tools/scene_objectives.py`) lists every
  `KeyChapterParametersController.EarnObjective` target by scene and button
  (0 = the scene itself, N = `ButtonDescriptionN`; 201 rows, 94 scenes).
  `Hero.ApplyObjectives` adds them after the answer's `SceneEffects`, so a
  changed pick keeps the branches its objectives open (e.g. the chapter-3
  `Bourgeois`/`Noble`/`Cleric` tracks after `030.020.010_Exam`). A scene-level
  row is applied when the scene ends, never at its open, so a scene cannot
  unblock its own answers.
- **Key chapter parameters are extracted per scene/answer.** `scene-params.tsv`
  (from `tools/scene_params.py`) records the `KeyChapterParametersOnEnableChanger`
  (button 0) and `KeyChapterParametersChanger` (answer button index) values.
  `SceneParams` loads them. `PlanLoad` runs `SimulatePlan`: the same `Sim` from
  the start save through the plan's answers, checking that each choice scene the
  game would open next is the plan's next one, and logs the first difference
  (`plan.Speculative`); the label-only `SceneParams.Simulate` is the fallback
  when the start save cannot be simulated. Stats the game changes outside answer
  effects are not simulated, so this is a prediction: the ordered replay is
  still the final check.
- **A double click on a menu save loads once.** The game's save slot
  (`SaveSlotBehaviour.OnPointerClick`) calls `LoadGame(int)` on every click
  with no guard; two clicks 0.14 s apart loaded the save twice, opened the
  scene four times and left the game on the timeline screen. `GameLoadPrefix`
  drops a second `LoadGame(int)` within 3 s of the first (logged "a double
  click").
- **The true-death pages are fixed (a game bug).** `FourthDeath_3`..`_6`
  wire CONTINUE to `FourthDeathContinueButtonController.Button_Click`, whose
  `DeathObjective` is empty in the shipped scenes: `SetAchievementInfo` throws,
  the button stays disabled, the story stops. A prefix (`Entrypoint.TrueDeathPrefix`)
  calls `SceneButton_Click` instead when the objective is missing, as
  `FourthDeath_1`/`_2`/`_7`/`_8` do. Filling the objective in is not enough:
  the pop-up's close reruns `GameFinals`, which reopens the page it never
  marked shown, forever. Verified: `FourthDeath_1`..`_8`, `LiveTimeline`,
  `Credits_GameOver`, main menu.
- **Achievements stay on.** The game unlocks one only while
  `GameManager.BlockAchievements` is false; `AchievementsBlocker` objects set it
  on `Start` (the main menu's to true, the loading screen's to false). The
  mod's load passes the loading screen too, and `ChoiceWindow.AllowAchievements`
  clears the flag after every mod load unless a scene that blocks on purpose is
  loaded. `ModHost.Update` logs the flag, the loaded blockers and Steam's stats
  whenever it changes ("Achievements: ...").
- **Fast replay.** `AutoPlay` runs the game's clock at 40x (every animation
  and timed wait is on the scaled clock; `Time.maximumDeltaTime` is raised to
  0.5 s, as a 0.1 s cap held a 60 fps frame to 6x whatever the scale), its own
  waits shrink with it (`Guard`), a scene's text pages are turned in one step
  through `TextController.NextPage` (`TurnPages`; it stops after a page that
  triggers a death or opens a stat/character pop-up), and a button counts only
  when a click could reach it on a settled screen (`Reachable`: no parent
  `CanvasGroup` blocks raycasts, is invisible, or has an alpha that is still
  moving). After an answer screen's continue the next press waits 0.25 s of
  real time: closer, it and an event pop-up's started the next scene's load
  twice and crashed the game in native code. A 28-answer chapter-4 replay:
  389 s -> 78 s; the whole story (debug run, chapters 1-5) ~4.5 min. What is
  left is mostly scene loading (~0.8 s a scene).
- **Pop-ups are closed before a mod load.** The game's pop-ups (interlude,
  event, trigger, objective) live on `UIManager.DialogCanvas`, which survives
  scene changes, and an interlude applies its stat changes when it is closed.
  `ChoiceWindow.ClosePopups` destroys them, empties the pop-up queue and
  clears `IsPopupOpened` before `LoadGame`, or a pop-up of the scene left
  behind lands its changes on the loaded state. Interludes applied are logged
  ("Interlude X applied in S: ...").
- **No quick load.** An estimated save built from recorded playthroughs
  (Quick load, 5 Nov 2026) matched the exact replay but was only about twice
  as fast on long paths with many recordings, and barely faster otherwise;
  it was removed. FINDINGS ("Quick load everywhere") has what was measured.
- **Chapter flows' `Once` units are reset before a mod load.** Every chapter's
  flow macro (`ChildhoodChapter` ... `Insurrection_Default`, on the persistent
  `TalismanFlowMachine`) has a Bolt `Once`: the first chapter end loads the
  finish screen, any later one fires `ClickTriggerEvent`, which reruns the
  flow - endless recursion, a native crash. Its flag lives in the machine's
  graph data, not in the save, so playing a chapter end a second time in one
  session after a mod load crashed (any replay across it).
  `BoltDump.ResetOnceUnits` clears every `Once` flag in `LoadFlow`, as in a
  fresh session.
- **Answer names are the button objects' names.** A few localisation keys are
  `ButtonDescription_1`; the scene's button is always `ButtonDescription1`, and
  `tools/localization.py` writes that name, or a predicted answer can never be
  clicked.
- **Save type help.** Each table type cell has an invisible hover target; its
  `TreeHover` shows the same paper/outline pop-up as the tree, with the short
  explanation from `Loc.K.HelpAutosave/HelpCheckpoint/HelpManual/HelpOriginal`.
  The year and turn columns are wide enough for their headers and values.
- **`SaveLoadManager.Instance` can be a destroyed object the game still saves
  with.** Never test it with Unity's `== null` and fall back to
  `FindObjectOfType`: use `Recorder.SaveManagers()` / `GameSaveManager()`.
- **`_no_save` is a folder name only** (`Recorder.NoRun`: the mod's saves and
  baseline copy for a game that had no main-save id). It must never reach the
  game as a save slot id: the game's autosave would throw and the scene would
  stay blank.
- **`SaveLoadManager.LoadGame(SaveLoadHelper)` is public** — that is the mod's
  load path. It restores all singletons and resumes via `LastOpenScene`.
- **Choice scenes are additive scenes containing `ParameterButtonChanger`
  buttons**; the story graph lives in Bolt visual-scripting machines, not in C#.
  Do not try to statically model the story; hook at runtime.
- **The game's `log.txt` answer log is dead code** (never written) — see
  FINDINGS.md. Anything the mod needs, it must record itself.
- **Unity 2018.3 / Mono / C# 7.3**: the compiler is modern csc with
  `-langversion:latest` targeting the game's mscorlib, but the *runtime* is old.
  Stick to constructs safe on .NET 4.x-era Mono. No `Span`, no nullable reference
  types at runtime boundaries, etc.
- **`0Harmony.dll` is vendored on purpose** (Lib.Harmony 2.3.3, net472): the
  game is Unity 2018-era; net35/net472 Harmony builds are the battle-tested
  combination for such games. Bump it deliberately, not casually.
- **The game runs under Proton** — paths are Windows-style inside the prefix;
  `Application.persistentDataPath` lands in
  `.../compatdata/1272160/pfx/drive_c/users/steamuser/AppData/LocalLow/SEVER/...`.
  All file access goes through Unity's own APIs, so nothing prefix-specific is
  needed in code.
- **Do not run the game while building** — Proton maps the DLL and the overwrite
  will fail (or the game will run the old one).

## Workflow the owner expects

Finish each change with `./tools/build.sh` succeeding, then commit (including
the rebuilt `Brante-Save-Load/Brante-Save-Load.dll` — the repo's mod folder is meant
to be runnable without a toolchain, same as Besiege-Sand ships its DLL), then
run `./tools/install.sh`. Read `Brante-Save-Load/Brante-Save-Load.log` after starting
the game — that log is the mod's only diagnostic channel.

To test the mod in-game without playing to a choice point, drop marker files
into the mod's folder inside the Proton prefix
(`<persistentDataPath>/Brante-Save-Load/Snapshots/`). All are one-shot (deleted
on use) and must be paired with `dev-autoload.txt`:

| marker | what it does |
|---|---|
| `dev-autoload.txt` | replay the vanilla load sequence onto the newest autosave |
| `dev-load.txt` | with `dev-autoload.txt`: after the autoloaded scene is open, load one mod save through the table path (checkpoint, baseline, adopt). Holds a file name under `Runs/`. The other dev markers still run afterwards, so `dev-load` + `dev-book`/`dev-loadpath` works on the loaded scene |
| `dev-plan.txt` | with `dev-autoload.txt`: build a path from the scene on screen to the named scene (first open answer everywhere), log the plan and its pre-checks; with `dev-load.txt` too, plan from the loaded scene. Does not replay |
| `dev-keyparam.txt` | with `dev-load.txt`: `Name=Value` set on `KeyChapterParametersController` after the load, before `dev-plan`. Used to test a branch such as `TriggerBlock=false` |
| `dev-stat.txt` | with `dev-load.txt`: `Name=Value` set on `ParametersManager.Parameters` (e.g. `Cunning=4`) after the load, before `dev-plan` |
| `dev-capture.txt` | with `dev-load.txt`: capture the current scene's choice point after `dev-stat`, so `dev-plan` starts from the modified state |
| `dev-replay.txt` | with `dev-plan.txt`: replay the planned path (fast-forward) instead of only logging it; `N`: load the `dev-load` save again and replay, N times in one session (logs Bolt variables that changed) |
| `dev-slow.txt` | kept while present: replays run at 1x instead of 40x |
| `dev-uitest.txt` | drive the save table: open, confirm, delete, load |
| `dev-table.txt` | open the save table, cycle the type filter through every category, log row counts, show/hide a type pop-up, page with "Show more" (a 10-row first page when the run has fewer rows) and check the Original row has no delete cell; with `dev-table-load.txt` also click the Original row and confirm its load; with `dev-table-open.txt` leave the table open afterwards (screenshots) |
| `dev-restart.txt` | with `dev-autoload.txt` (and `dev-load.txt`): make the game's chapter restart call (`LoadChapterState`, as its Restart button does) and log the checkpoint and run state |
| `dev-pick.txt` | with `dev-autoload.txt`: open the tree, apply `scene=ButtonDescriptionN` per line (`reset` = Reset path), log every column after each step (drawn colours, Load scene target, where the pick note is) and leave it open; `walk N`: pick the first open answer in each of the next N scenes, logging after each whether Load scene runs and why not; `hover scene=answer`: scroll to that answer and hold its hover (pop-up and stat marks); `load`: press Load scene and confirm (replays the path); `confirm`: then open the save table and click its first row, leaving the load dialog open |
| `dev-book.txt` | the book tabs (tree view, then save table), the save table, then the tree view: wheel, pop-ups, stat pop-up, a conflicting pick, chapter rule, a pick + its load plan, undo |
| `dev-loadpath.txt` | with `dev-book.txt`: Load scene end to end; `back N` replays from another answer N scenes back, `chapter` from the chapter's start save, `start` from chapter 1 with an early change, `ahead` into the next chapter, `death` through a death, `offpath` a stop off the path, `first` the story's first scenes from a stand-in start (another run's chapter 1 save), `new` the same from a start built from the game's defaults, `file <name>` load that mod save as the table does (`file <name> norun`: with no run active) |
| `dev-history.txt` | log the save's scene path (`ShowedScenesList`) |
| `dev-autoplay.txt` | empty: log the buttons the game puts up after an answer; `chapters 2 3`: start a debug run (own main save `Debug_<date>`, from the newest `Chapter_1.dat`), play answers that keep the hero alive (no Death, most Will, build the stat the track's final gate needs - `AutoPlay.SafePick`), on through the hero's last death, and make a mod save when each listed chapter starts; `on`: play on from the scene on screen (after `dev-autoload`/`dev-load`) with the same picker, through the hero's last death, to the credits |
| `dev-stattrail.txt` | `<run folder> <stat> ...`: log those stats at every choice point of a run, oldest first (no `dev-autoload.txt` needed) |
| `dev-scene-tree.txt` | dump the loaded scene's UI components |
| `dev-bolt.txt` | dump the live Bolt graph of the choice machine |
| `dev-ironman.txt` | load the save as an Iron Man game |
| `dev-lang.txt` | switch the game language, to exercise the window rebuild |

Kill the game's own `…Sir Brante.exe` processes (not just the Proton wrapper)
plus `wineserver -k` after each run.

To screenshot the running game (Wayland/KDE session):
`DISPLAY=:1 xwininfo -root -tree | grep -i brante` finds the window's display,
then `spectacle -b -n -o shot.png` captures the full screen (it includes
XWayland windows). ImageMagick `import`, `xwd`, and `ffmpeg x11grab` do **not**
work here (black/`BadMatch` on the X1 root).

## Open work

- [docs/TODO.md](docs/TODO.md): what remains of the plan.
- Phases 0–13 are implemented and verified in-game: the tree over the whole
  playthrough with a forward simulation of the scenes ahead, path picks with
  Load scene (replayed by `AutoPlay`), the save/load rework (save ownership and
  repair, the one-time baseline, pre-rewind checkpoints including the chapter
  restart, the game-autosave mirror, the unified table with paging and the
  Original row) and id minting for id-less loads.
- Remaining: the full playthrough pass on a clean run, the README screenshot
  refresh, and the stat changes outside answer effects that the simulation
  cannot see.
