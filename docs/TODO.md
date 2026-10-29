# TODO

The plan, as approved, with status. Phases 0–4 are done; 5–7 are ahead.

## Phase 0 — repo scaffold (done)

- [x] Layout mirroring Besiege-Sand: installable folder, tools/, docs/, AGENTS.md
- [x] Vendor Doorstop 4.6.0 (win x64) + Lib.Harmony 2.3.3 with licenses
- [x] tools/build.sh: csc from dotnet SDK against the game's own Managed/ DLLs
- [x] tools/install.sh: symlink/copy/uninstall into the game folder
- [x] Doorstop bootstrap: Entrypoint → host GameObject → ModHost

## Phase 1 — reverse engineering (done)

- [x] Decompile Assembly-CSharp.dll (ILSpy), map save/choice/UI systems
- [x] docs/FINDINGS.md written
- Confirmed by decompile:
  - saves = BinaryFormatter `SaveLoadHelper`, one rolling `Autosave.dat` per slot
  - `LoadGame(SaveLoadHelper)` public — the mod's load path
  - choice scenes = additive scenes with `ParameterButtonChanger` buttons
  - the game's own answer log (`log.txt`) is dead code — mod must record choices itself
  - `ShowedScenesList` + `LastOpenScene` determine resume behaviour

## Phase 2 — runtime probe (done, 23 Oct 2026)

- [x] Bootstrap made to work in-game (the hard part, see FINDINGS.md for the details):
  - Doorstop entrypoint must stay pure-managed; `new GameObject()` there is fatal in a
    sneaky way — Mono caches the failed icall wrapper, so the call keeps throwing even
    after the engine is up. Never touch the Unity API before engine init.
  - Working path: `AssemblyLoad` hook → Harmony postfix on
    `GameManager`/`SaveLoadManager`/`MainMenuController.Awake` → host GameObject created
    in the first of those, engine fully up. ~20s from game start to probe output.
- [x] Probe results (all confirmed in `Brante-Save-Load/Brante-Save-Load.log`):
  - `log.txt` empty (0 bytes) in a real session — dead code, mod records choices itself
  - `persistentDataPath` = `C:/users/steamuser/AppData/LocalLow/SEVER/…` (Proton prefix)
  - `SaveLoadManager.LoadGame(SaveLoadHelper)` present at runtime, instances of
    `GameManager`/`SaveLoadManager` alive at the main menu
  - types `SceneController`, `WindowPause`, `ParameterButtonChanger` all present
    (real name: `_Scripts.AMVCC.Views.ParameterButtonChanger`)
- [x] WindowPause presence *per in-game scene* — resolved in Phase 4: `Window_Pause`
  is ONE `DontDestroyOnLoad` object registered in `UIManager.PauseWindow`, present
  in every scene from boot (not per-scene); `WindowPause` component confirmed
- [ ] timing of autosave calls — dropped: informational only, our hook point is
  scene-load based and does not depend on it
- [x] Proton: installer now writes a per-app Wine `DllOverrides` entry into the
  game's Proton prefix (`user.reg`), so no launch option is needed; the launch
  option `WINEDLLOVERRIDES=winhttp=n,b` remains as documented fallback (README)

## Phase 3 — choice-point capture (done, 23 Oct 2026)

- [x] Hook choice-scene appearance: Harmony postfix on `SceneController.Awake`
  (fires for every choice scene, main thread, state final — the chosen answer's
  effects only apply on click, and `ShowedScenesList` still has the scene as
  *not* `IsShowed`, which is exactly what makes the loader re-open it)
- [x] Snapshot the full `SaveLoadHelper` (field-by-field mirror of the game's
  `SaveData`, but **no** game save code called and no file I/O outside
  `<persistentDataPath>/Brante-Save-Load/Snapshots/`) — 17 KB BinaryFormatter blob,
  byte-identical format to the game's own saves
- [x] Sidecar `.meta.txt`: timestamp, hero/age/year/turn/season, chapter,
  localized scene title, and every offered choice text
- [x] Dedup: SHA1 over the blob in the filename (`<scene>_t<turn>_<hash10>.dat`);
  identical state → "unchanged, skipped" (verified)
- [x] `Saves/` untouched, structurally (own code path) and enforced at runtime
  for the dev harness by a save-gate Harmony prefix (verified: root stays clean)
- Verified in-game via the dev autoload harness (marker file → loads newest
  autosave through the game's *own* loading-screen/dissolve/`AutomaticSceneLoad`
  sequence — which also rehearses Phase 4's load path)

## Phase 4 — settings button + choice list UI (done, 23 Oct 2026)

- [x] Button in the pause/settings window: `ChoiceWindow.cs` injects it into
  `UIManager.PauseWindow` (bottom-centre of `Window_Pause`) at host start and
  again from a Harmony postfix on `UIManager.ShowPauseMenu` (re-ensure + retry
  if UIManager wasn't up at start). No prefab edits; rides the pause window's
  own canvas/raycaster, so it is clickable like the game's own buttons.
- [x] Scrollable list: own ScreenSpaceOverlay canvas (sortingOrder 8000; the
  game's canvases measured ≤20 at boot and the pause window is WorldSpace
  order 12, so ours is always on top), ScrollRect + RectMask2D +
  VerticalLayoutGroup; newest first by file time; each row shows the localised
  choice title + `chapter · hero · age/year/turn/season · recorded` parsed
  from the `.meta.txt` sidecar.
- [x] Delete + refresh: per-row `[x]` (child of the row, so it raycasts above
  the row's own load handler) → same confirm dialog in delete mode → deletes
  the snapshot `.dat` + sidecar, refreshes the list. Only ever touches the
  mod's own folder.
- [x] Click → confirm → `LoadGame(snapshot)` through the game's own loading
  screen. Verified in-game: the scene reopens and the recorder's dedup skip
  proves the state round-tripped exactly. See FINDINGS.md ("Phase 4 facts")
  for the measured pause-window layout and load-flow results.
- [x] Dev UI test: marker `Snapshots/dev-uitest.txt` (together with
  `dev-autoload.txt`) walks pause → list → load-confirm → delete-confirm →
  load through the real `onClick` handlers, holding each state 3s for
  screenshots. Delete is exercised end-to-end only when ≥2 snapshots exist
  (a seeded duplicate is deleted; the original must survive).
- Learned (deviations from the plan):
  - The plan's "Phase 4 must set slot info too" was **replaced** (23 Oct) by
    session-wide save blocking (`Recorder.BlockGameSaves` + a save-gate prefix).
    That in turn was **replaced on 24 Oct** by a one-time backup of the game's
    save folder plus normal saving, because the block left chapter starts stale
    and broke chapter restarts after a death (see Phase 7).
  - `SaveLoadManager.Instance` was ALIVE in-game in every test (the dead-
    singleton risk from Phase 3 didn't materialise); the `FindObjectOfType`
    fallback is kept anyway.
  - TMP text and runtime-built layout must be flushed with
    `Canvas.ForceUpdateCanvases()` right after building/refreshing: items are
    created at zero width (the layout group sizes them on the first canvas
    rebuild), and without the flush the item text painted 2+ seconds late
    (observed in screenshots).

## Phase 5 — separate manual save/load (done, 23 Oct 2026)

- [x] "Save now" / load / remove for the mod's own saves (same snapshot
  format), stored in `<persistentDataPath>/Brante-Save-Load/Slots/` as
  `save_<timestamp>.dat` with `.meta.txt` sidecars (`Recorder.SaveManual`)
- [x] Lives in the same window as a second tab ("Saved games"), next to
  "Choice points". Every save/load/remove goes through the confirm dialog
- [x] Nothing is written to the game's `Saves/` folder
- [x] Documented as independent of the game's saves in README
- [x] Saves keyed by main-save ID (`Brante-Save-Load/Runs/<ID>/`), list shows the current run only
- [x] Existing saves migrated into the run folders at startup (`Recorder.MigrateLegacy`):
  matched by hero name when exactly one main save exists; verified in-game (21 moved, 0 kept)
- [x] Run pinned on load, so saves stay in the right folder after loading a snapshot
- [x] In-game pass (23 Oct 2026): save, load, overwrite, remove all ran
  without errors (log checked)
- [x] Save screen rebuilt as sortable tables (both tabs): one row per save,
  column headers sort (click again to reverse), × far right deletes, rows
  scroll with a scrollbar. Table values are read from the save file itself
- [x] List polish (verified in-game 23 Oct 2026): "Save / Load" naming,
  centred "Save now", long choice names scroll (marquee) after an ellipsis
- [x] Steam Cloud investigated (23 Oct 2026): the app's Auto-Cloud path is
  correct, but Steam reports AutoCloud disabled for app 1272160 and syncs
  nothing. Enabling it is a Steamworks (developer) setting; the path would
  also need "recursive" because saves live in per-playthrough subfolders
- [x] Table columns: chapter shown by name (sorted by number), hero column
  dropped (one hero per list)

Note: the fixed five slots of the first version were replaced by unlimited
saves (one row each); the two test files `slot1.dat`/`slot2.dat` still list.

## Phase 6 — playthrough history (done, 24 Oct 2026)

- [x] Parsed the current save: `ShowedScenesList` is an ordered list of
  `{SceneName, IsShowed}` that the game appends to when a scene opens
  (`History.cs`). It is the path the playthrough has taken, including the part
  played before the mod was installed. `IsShowed` flips when the scene is
  actually shown; the newest entry is still `pending` (that is also what makes
  the game's loader re-open it).
- [x] Shown as a read-only **History** panel in the Save / Load window
  ("Playthrough"): numbered list, localised title (`<scene>_Title`), scene id,
  `[save available]` when the mod has a snapshot for that scene, `[current]`
  for the scene on screen. Game machinery scenes (no title key) are skipped.
- [x] Panel loading of pre-mod choice points: **decided against**. The save
  records that a scene was shown, not which answer was clicked, and the state
  needed to restore an earlier choice no longer exists in the save (the
  game keeps one rolling autosave, and chapter-start files only). Replaying
  through Bolt to rebuild it would depend on dice and event triggers, so the
  list ships as history and loading applies from the moment the mod is
  installed.

## Phase 7 — polish

- [x] Death / "Restart chapter" (real bug, fixed 24 Oct 2026): the game reads
  `Saves/<main save ID>/Chapter_N.dat`, and the ID comes from the loaded save.
  Mod snapshots stored an empty ID (for byte-stable dedup), so after a rewind
  the game would look for `Saves/Chapter_N.dat` — missing — and fall back to a
  default save, resetting the run. New snapshots store the real ID, and the load
  flow fills it in for old ones (verified in-game: `main save=Biggie_10/23/…`).
- [x] Language switch and translations (24 Oct 2026): the window rebuilds when
  the game language changes, and every string the mod shows is translated into
  the game's eleven languages (`Loc.cs`); the game's own keys are used for
  chapter names and scene titles, so those follow the language too. Verified
  in-game in Russian.
- [x] Chapter restarts after a rewind (24 Oct 2026): the save gate was removed.
  The mod now copies the game's own save folder to
  `Brante-Save-Load/Backups/<save>/` once (with a `README.txt` telling the
  player how to restore it), then lets the game save normally again, so
  autosaves, chapter starts and (if enabled) cloud follow the rewound timeline.
  Slot-id repair hooks keep those files inside the save's own folder instead of
  `Saves/` itself. Verified in-game: no stray files in `Saves/`, autosave
  updated inside the save folder.
- [x] Iron Man mode (24 Oct 2026): the game disables its own chapter-load menu
  in Iron Man; the mod's window ignores that and keeps working (verified by
  loading a save with `IronManMode` forced on: scene reopens, no errors).
  Autosaves continue normally after a rewind, as in a normal run.
- [ ] Load a save after switching to a different main save (per-run folders make
  this testable now).
- [x] Cross-platform paths: sanitiser uses the union of Windows/Unix rules, all
  paths go through `Path.Combine`/`Path.DirectorySeparatorChar`, backup README
  lists the Windows, Proton and macOS locations. Uninstall gained
  `--restore-backup` and `--purge` (tested against a throwaway prefix).
- [x] Settings button visible on the FIRST open (27 Oct 2026): it is built at
  startup, not on the first pause-menu toggle, and the menu is located by its
  structure (the smallest layout whose children are labelled button rows) so it
  works before the window has ever been laid out.
- [x] Choice column no longer blinks while sorting (27 Oct 2026): the scrolling
  cell measured in `Update`, one frame after the row was built; the table now
  measures the marquees right after the canvas flush.
- [x] History steps are clickable where the mod has a save for that scene
  (loads it through the confirm dialog); older scenes have no state to restore.
- [x] History as a tree (27 Oct 2026): columns per captured decision, choices
  stacked, taken branch on a darker path, hover shows the full text, clicking a
  node loads that decision. The clicked answer is recorded (sidecar `chosen=`,
  and derived from the next snapshot's `CurrentConsequence`).
- [x] Branch data gathered (`data/branches.json`, `data/events.csv/json`,
  `docs/BRANCHES.md`): the community event sheet covers all five chapters - 175
  events, every option, requirement and outcome (credit: Brian Sirith). Only
  the option -> next event edges are still missing; `trace.log` supplies them.
- [x] Trace logging (`Runs/<save>/trace.log`): every scene load and click, so
  the missing graph edges (choice -> next scene) can be merged from play.
- [x] Merge tool (`tools/build_graph.py`): joins traces, snapshots and the
  community sheet into `data/graph.json` — scene ids ↔ event titles by title
  key, and choice → next scene edges from `trace.log`. Verified with a
  synthetic trace; the real save has 3 events mapped and 0 edges yet.
- [x] Bolt graph extraction (`tools/bolt_extract.py`): the game stores each
  machine's graph as JSON in `resources.assets` - 481 graphs extracted in under
  a second, 445 with `LoadScene` literals, 9 with the `Click` event.
- [x] Resolve `SuperUnit.nest.macro`: the machine object ends with a PPtr array,
  one entry per SuperUnit (`tools/bolt_graph.py`). Control flow now walks from
  the `Click` event through switches and macros to `LoadScene`.
- [x] Chapter macros catalogue the scenes (one macro per scene id), but a scene
  macro only loads itself and no switch uses scene ids - scene files do not hold
  a successor table either (1 of 25 does). The per-choice transition is decided
  by the scene's Bolt **state machine** / the `%true`/`%false` switch - still to
  be decoded.
- [x] Ruled out Bolt state graphs (SceneStateMachine is C#) and the chapter
  switch (it only routes chapter/finals). Exported the game's own scene
  catalogue per chapter (`tools/bolt_scenes.py` -> `data/scene-catalog.json`,
  75 scenes).
- [x] Extract the localisation table (`tools/localization.py`): scene titles and
  every answer's text for 61 scenes, offline - no need to play a scene to know
  what it offers.
- [x] Full-path tree (28 Oct 2026): the mod ships `scene-texts.tsv` (from
  `tools/localization.py`) and `SceneTexts.cs` reads it, so the history shows
  one column per played scene - including scenes from before the mod, which
  have no snapshot - with every answer and the taken one highlighted. Only
  captured decisions are clickable.

- [x] **Path builder (28 Oct 2026).** Explore toggle in the history panel:
  clicking any answer moves the preview to the next scene (from
  `scene-edges.tsv`), columns continue as far as the chain goes, and "Load this
  scene" appears when the reached scene has a snapshot. Verified in-game:
  `01.05.01 → 01.06.01 → 01.07.01` walked from the panel.
- Design notes behind it (kept for context): In the tree, let the player click
  any answer at any step; the view then shows the scene that answer leads to, and
  they can keep picking until they load the resulting scene. This is the
  "load any scene by quickly making all the selections" feature.
  - It needs the missing piece: choice -> next scene edges (see the Bolt notes in
    BRANCHES.md). Without them the view cannot know where an unplayed answer
    goes; with them it is only rendering.
  - Two possible designs:
    1. **Offline graph**: `data/scene-edges.json` + `scene-edges.tsv` (shipped
       with the mod) hold the chapter chains, extracted from the chapter macros'
       wiring (`tools/bolt_edges.py`) - 58 edges, matching a real playthrough.
       The path builder can now walk forward without playing.
    2. **Live simulation**: load the nearest snapshot, then invoke the game's
       own `Button_Click` for each chosen answer and let the game advance. Works
       for captured scenes, but each step plays the consequence animation
       (seconds per click) and needs the page-advance driver, still unresolved.
  - UI: a toggle in the History panel ("explore"), which unlocks clicking
    read-only answers; a "load this scene" button appears once the path reaches a
    scene the mod has a snapshot for, otherwise the path is only a preview.
- [x] Transition order: solved from the chapter macros' wiring
  (`tools/bolt_edges.py`), no need for the scene-file `UnityEvent` parsing.
- [x] Full path view: the history tree shows the whole playthrough with every
  answer, and explore mode walks the chains forward.
- [ ] Optional cross-check: traces record the edges actually played; a page
  advancing driver would let a harness click through loaded scenes.
- [x] Polish pass (28 Oct 2026): answer texts are filled from the localisation
  table when a sidecar holds a key or an empty entry, blank rows are never drawn,
  the explore view says "pick an answer to continue" and scrolls to the newest
  column; documentation (README, AGENTS, TODO, BRANCHES) brought up to date.
- [x] Tree fixes (28 Oct 2026): vertical lines hung below their end point (top
  pivot placed at the lower end), so spines missed the answers; the taken path
  now leaves from the node's edge instead of striking through its text; blank
  answers close up instead of leaving gaps; only loadable nodes react to hover;
  the current scene is marked; explore picks on an earlier played scene cut the
  path there; horizontal scrollbar, and the wheel scrolls sideways while the
  tree fits vertically. Verified in-game with `dev-book.txt`.
- [x] Tree view (28 Oct 2026): the History button is now "Tree view"; the
  chosen answer comes from the next choice scene's snapshot (`CurrentConsequence`)
  when no click was recorded - it used to read the live game's value for every
  scene, so nothing was highlighted; the rest of the chapter is shown as muted
  "upcoming" columns from `scene-edges.tsv`; the wheel scrolls the tree from
  anywhere in it (the empty viewport was not a raycast target); the table's
  Chapter column drops the "Chapter I." prefix. Verified in-game.
- [x] Complete flow for all chapters (28 Oct 2026): `tools/bolt_story.py`
  flattens every chapter macro (including `PeaceChapter`'s four tracks,
  `Insurrection` and `GameFinals`) into the scene order with time slots, switch
  labels and branch conditions; `scene-flow.tsv` replaces `scene-edges.tsv`,
  `SceneFlow.cs` evaluates the conditions against the live game for the
  upcoming columns and Explore, and `scene-texts.tsv` now covers all 263 story
  scenes. Chapter 1 verified in-game (upcoming and explore unchanged, every
  condition parses and evaluates); chapters 4-5 checked offline only, since no
  save that far exists here.
- [x] Tree view, round three (28 Oct 2026): every played scene on a band
  under a timeline bar (answers before the mod are not recorded anywhere, so
  those columns show no highlight); every later chapter after the current one,
  each behind a rule with the chapter's name; hover pop-up with the full text
  and the answer's effects (`tools/scene_effects.py`, `SceneEffects.cs`) using
  the game's stat icons and portraits; the tree fills the screen; Explore no
  longer scrolls; loading from the tree confirms over it and Cancel returns to
  it. Verified in-game with `dev-book.txt` (pop-up, load + Cancel, chapter rule).
- [x] Answer requirements in the pop-up (28 Oct 2026): all-of and one-of
  groups from `ParameterButtonChanger`, checked live (green/red) for the
  current and upcoming scenes. Verified in-game.
- [x] Tree view, round four (28 Oct 2026): clicks select answers (red dashed
  mark on the last), Undo resets them, Load scene replays the picked path with
  `AutoPlay` from the newest mod save before the first change; Explore removed;
  "not recorded" note on the band; the path no longer runs down the next
  scene's spine before an answer is picked; Tree view button with its own icon
  at the top of the save window. Debug runs (`dev-autoplay.txt`) make test
  saves at chapters 2 and 3. Fixed the click recorder (it never wrote
  `chosen=`). Verified in-game, including a two-answer replay.
- [x] Blocked answers greyed out along the picked path (hero simulated with
  the game's own effect rules from the nearest exact save), Load scene refuses
  a path through a greyed pick; replay at 10x speed, muted (~5 s a scene).
  Verified in-game on the debug run.
- [x] Tree view, round five (28 Oct 2026): opening ~470 -> ~310 ms and a pick
  ~850 -> ~30 ms (history and scenes ahead built once per open, a pick only
  restyles the drawn answers and redraws the path layer; one folder scan for
  snapshots, cached sidecars, field copies instead of serialised clones,
  cached reflection in SceneFlow); close as an x top right; stats row along
  the path; two-line "not recorded" note. Fixed: a scene's snapshot lookup
  also matched scenes whose id starts with it (01.02.01 / 01.02.01_2).
- [ ] Achievement side-effects on load (verify).
- [ ] Full in-game pass on a played run: chapter transition, death -> restart
  chapter, explore + load from the panel.
- [ ] Design document (`docs/DESIGN.md`).
