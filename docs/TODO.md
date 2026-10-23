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

## Phase 2 — runtime probe (next)

- [ ] Run the game with the probe installed; confirm FINDINGS.md against reality:
  - [ ] `log.txt` empty in a real playthrough
  - [ ] where `persistentDataPath` lands under Proton
  - [ ] `WindowPause` exists in all in-game scenes
  - [ ] `SaveLoadManager.LoadGame(SaveLoadHelper)` present at runtime (probe already checks)
  - [ ] timing/sequence of autosave calls (informational only)
- [ ] docs/FINDINGS.md updated with anything that differs

## Phase 3 — choice-point capture

- [ ] Hook choice-scene appearance (Harmony patch on `SceneController` or sceneLoaded)
- [ ] Snapshot the full `SaveLoadHelper` state into the mod's own folder
- [ ] Sidecar metadata: timestamp, chapter, year/age, scene name, localized scene
      title, the texts of the offered choices
- [ ] Dedup: skip if an identical state (same scene + same stats) already recorded
- [ ] Never write to the game's `Saves/` folder (structural: separate code path)

## Phase 4 — settings button + choice list UI

- [ ] Button in the pause/settings window (spawned programmatically, no prefab edits)
- [ ] Scrollable list: chapter, choice text(s), date; newest first; delete + refresh
- [ ] Click → confirm → `LoadGame(snapshot)` through the game's own loading screen

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
