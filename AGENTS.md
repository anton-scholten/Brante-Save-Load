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
  (`SaveLoadHelper`) that holds the entire game state. The mod never creates,
  renames or mints game save folders (`Recorder.MintRunId` is gone): a new game's
  folder comes from the game's own `CreateSaveLoadSlot`. The mod writes only
  inside its own folder (`Brante-Save-Load/`) plus the one-time id repair below.
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
- **Time gates put immediate scenes first.** `SceneFlow.Ahead.Immediate` marks
  a candidate whose `Turn`/`Season` gate matches the live game (or a trigger
  scene with Turn 0); `After` sorts immediate scenes (flow order) before later
  time slots and later chapters. The turn/season progression itself is still
  driven by the game; the tree only orders what can come next.
- **Conditions are evaluated after the current answer.** `WorkOutOpenAnswers`
  stores each column's hero after its answer (`AfterHero`); `AddUpcoming` asks
  `SceneFlow.After(..., hero)`, which briefly swaps that hero's stats,
  characters and unlocked objectives into the live managers while evaluating
  the scene conditions. A current/played-scene pick rebuilds the columns
  (`RefreshKeepingScroll(true)`), so an answer that turns a trigger's condition
  true makes it appear ahead; an upcoming-scene pick only restyles.
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
  `SceneParams` loads them; `PlanLoad` simulates the start save's parameters
  through the picked answers and scenes and logs when an answer moves a later
  scene out of its branch (`plan.Speculative`). The ordered replay is still the
  final check.
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
| `dev-replay.txt` | with `dev-plan.txt`: replay the planned path (fast-forward) instead of only logging it |
| `dev-uitest.txt` | drive the save table: open, confirm, delete, load |
| `dev-table.txt` | open the save table, cycle the type filter through every category, log row counts, and show/hide a type pop-up |
| `dev-book.txt` | the book tabs (tree view, then save table), the save table, then the tree view: wheel, pop-ups, stat pop-up, a conflicting pick, chapter rule, a pick + its load plan, undo |
| `dev-loadpath.txt` | with `dev-book.txt`: Load scene end to end; `back N` replays from another answer N scenes back, `chapter` from the chapter's start save, `start` from chapter 1 with an early change, `ahead` into the next chapter, `death` through a death, `offpath` a stop off the path, `first` the story's first scenes from a stand-in start (another run's chapter 1 save), `new` the same from a start built from the game's defaults, `file <name>` load that mod save as the table does (`file <name> norun`: with no run active) |
| `dev-history.txt` | log the save's scene path (`ShowedScenesList`) |
| `dev-autoplay.txt` | empty: log the buttons the game puts up after an answer; `chapters 2 3`: start a debug run (own main save `Debug_<date>`, from the newest `Chapter_1.dat`), play the first open answer everywhere and make a mod save when each listed chapter starts |
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
- Phases 0–7 are implemented and verified in-game: the tree over the whole
  playthrough (answers for every scene come from the game's localisation), path
  picks with Load scene (replayed by `AutoPlay`), and the debug runs that make
  test saves.
- The save/load rework (TODO phases 8–13: save ownership and repair, the
  one-time baseline, pre-rewind checkpoints, the game-autosave mirror, the
  unified table) is implemented and `./tools/build.sh` passes; **the in-game
  pass is still pending**. Remaining after that: the achievements check, a full
  playthrough pass (death -> restart chapter), and a design document.
