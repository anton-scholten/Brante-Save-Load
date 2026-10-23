# TODO

The plan, as approved, with status. Phases 0–1 are done; the rest is ahead.

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
- [ ] WindowPause presence *per in-game scene* — deferred to Phase 4 (only the main
  menu was observed; the pause window is only relevant in-game anyway)
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

## Phase 4 — settings button + choice list UI

- [ ] Button in the pause/settings window (spawned programmatically, no prefab edits)
- [ ] Scrollable list: chapter, choice text(s), date; newest first; delete + refresh
- [ ] Click → confirm → `LoadGame(snapshot)` through the game's own loading screen
- Learned from Phase 3's harness (apply here):
  - vanilla load sequence = `LoadGame` → `GameLoadingScreen` scene → broadcast
    `StartSceneTransitionEvent` → dissolve animation event →
    `SceneTransitionCompleteEvent` → `AutomaticSceneLoad` (IsShowed=false →
    `LoadScene(LastOpenScene)`, true → flow-machine `Click`)
  - `LoadGame(int slotId)` is the overload that also sets `_currentSaveSlotInfo`
    (where autosaves are written); `LoadGame(SaveLoadHelper)` does NOT — a later
    autosave would drop files in odd places. Phase 4 must set slot info too.
  - `SaveLoadManager.Instance` can be dead mid-game (it dies with the main-menu
    scene in some paths) — resolve with a `FindObjectOfType` fallback

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
