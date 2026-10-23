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
- [x] Probe results (all confirmed in `BranteLoadAny/BranteLoadAny.log`):
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
  `<persistentDataPath>/BranteLoadAny/Snapshots/`) — 17 KB BinaryFormatter blob,
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
  - The plan's "Phase 4 must set slot info too" is **replaced** by session-wide
    save blocking (`Recorder.BlockGameSaves`, set by the load flow and enforced
    by the save-gate Harmony prefix): pointing the slot bookkeeping at the
    original slot would let a later autosave overwrite the pre-rewind timeline;
    pointing it at a new slot would write into `Saves/` (forbidden) and clutter
    the game's Load window. Blocked autosaves + new choice snapshots cover
    progress after a rewind instead.
  - `SaveLoadManager.Instance` was ALIVE in-game in every test (the dead-
    singleton risk from Phase 3 didn't materialise); the `FindObjectOfType`
    fallback is kept anyway.
  - TMP text and runtime-built layout must be flushed with
    `Canvas.ForceUpdateCanvases()` right after building/refreshing: items are
    created at zero width (the layout group sizes them on the first canvas
    rebuild), and without the flush the item text painted 2+ seconds late
    (observed in screenshots).

## Phase 5 — separate manual save/load

- [ ] "Save now" / "Load" for the mod's own named slots (same snapshot format)
- [ ] Documented as independent of the game's slots in README

## Phase 6 — existing saves (choice history reconstruction)

- [ ] Parse the user's current save: `ShowedScenesList` records the scene path taken
- [ ] Present it as a list (what was chosen where, as far as scenes encode it)
- [ ] Decide (after Phase 2 data): replay-based loading of pre-mod choice points —
      only feasible if chapters replay deterministically; otherwise ship the list
      as history and loadable-from-now-on

## Phase 7 — polish

- [ ] Edge cases: chapter transitions, death scenes, Iron Man mode, language switch
- [ ] Achievement side-effects on load (expected: none harmful; verify)
- [ ] In-game test pass, README final, design doc
