# What is inside the game (decompiled 23 Oct 2026)

Everything here was read from `Assembly-CSharp.dll` of Unity **2018.3.0f2** (Windows
build, Steam app 1272160) with ILSpy, and cross-checked against the running game where
noted. Line numbers refer to the decompiled output, not to any source release.

Game dir: `/media/game_drive/SteamLibrary/steamapps/common/The Life and Suffering of Sir Brante`
Managed dir: `The Life and Suffering of Sir Brante_Data/Managed/`

## The one-paragraph summary

The whole game state lives in plain C# objects reachable from four singletons
(`GameManager.Instance`, `ParametersManager.Instance`, `KeyChapterParametersController.Initiate`,
`CharacterParametersSerializeHelper.Initiate`). The game serialises all of it into one
`SaveLoadHelper` object with `BinaryFormatter`, and — the key for this mod —
`SaveLoadManager.LoadGame(SaveLoadHelper)` is **public** and restores any such object
in place. A "choice point" is simply an additive scene that contains
`ParameterButtonChanger` buttons. Therefore: snapshot `SaveLoadHelper` whenever a choice
scene appears → later feed the snapshot back to `LoadGame` → the game returns to that
exact choice screen. No replay, no state reconstruction of our own.

## Save system (`_Scripts.Managers.SaveLoadManager`)

- Folder: `Application.persistentDataPath/Saves/<SlotInfo.Id>/`
  - Windows/Proton: `.../compatdata/1272160/pfx/drive_c/users/steamuser/AppData/LocalLow/SEVER/The Life and Suffering of Sir Brante/Saves/`
  - `<SlotInfo.Id>` = `HeroName_datetime` (created in `CreateSaveLoadSlot`)
- Files per slot:
  - `Autosave.dat` — BinaryFormatter blob of `SaveLoadHelper`. This is *the* save;
    it is rewritten on every `SaveGame` call (one rolling save, not a timeline).
  - `Chapter_1.dat` … `Chapter_5.dat` — chapter-start states, written by
    `SaveChapterState()`, used by the loading screen's chapter selector.
  - `testSave.txt` — a `JsonUtility` mirror of every `.dat`, written for debugging.
    Useful: our tooling can read state as JSON without BinaryFormatter.
- `log.txt` in persistentDataPath — the answer log. **Dead code**: `ChoiceLoggingSystem.AddAnswerInLog`
  is never called from C# (`WriteAnswerInLog` is broadcast by `SceneController`, but no
  C# listener exists), so `log.txt` is always `{"SelectedAnswers":{}}`. The game does
  not persist choice history. **Confirmed at runtime** (Phase 2 probe): the file exists
  and is empty (0 bytes) after a real session.
- `SaveGame(turn, chapter, lastOpenScene, state)` → `SaveData()` copies ~25 fields out
  of the singletons into a fresh `SaveLoadHelper` and serialises it. The field list is
  the de facto schema of the save (see `SaveLoadHelper` below).

### SaveLoadHelper — the complete state schema

`[Serializable]` class, plain properties, all listed in the decompiled
`_Scripts.Helpers/SaveLoadHelper.cs`:

| Field | Meaning |
| --- | --- |
| `ModelParameters` | all character stats (Willpower, Determination, …) |
| `ChapterParameters` | key chapter parameters (name→value list) |
| `CharacterModel` | relations/statuses of every character |
| `AllGameObjectives`, `AllGameEvents` | unlock state of objectives/events |
| `HeroName`, `Year`, `PreviousYear`, `HeroAge`, `Turn`, `Season` | time/progress |
| `Chapter` (0–5), `ChapterName` | which chapter |
| `LastOpenScene` | **name of the scene to resume into** |
| `SceneInSequence` | position within the scene sequence |
| `ShowedScenesList` | every scene ever opened + `IsShowed` flag |
| `SceneState` | state-machine state (`SceneStates` enum) |
| `CurrentConsequence`, `InsurrectionDayStage`, `InsurrectionPreviousDayStage` | war-day bookkeeping |
| `StoryMode`, `IronManMode`, `GodMode` | modes |
| `SlotInfo` | `{Id, Date}` of the originating slot |

## Loading (`SaveLoadManager.LoadGame`)

Four overloads; the important one is public and takes our object directly:

```
LoadGame(SaveLoadHelper save)
  → InitializeData(save)                 // restores every singleton from `save`
  → Messenger.Broadcast("LoadGameComplete")
      → GameLoadingScreenBehaviour: CustomEvent.Trigger(TalismanFlowMachine, "Click")
      → on "SceneTransitionCompleteEvent": AutomaticSceneLoad():
            if LastOpenScene was already IsShowed → continue via flow machine
            else → GameManager.LoadScene(LastOpenScene)   // re-open the exact scene
```

So restoring a snapshot is literally `SaveLoadManager.Instance.LoadGame(snapshot)`,
possibly preceded by opening the `GameLoadingScreen` scene the way `LoadWindow` does.
`InitializeData` is idempotent w.r.t. live singletons; it also fills in defaults for
objectives/parameters missing from older saves.

`LoadGame(int slotId)` (used by the game's own Load window) indexes a private
`_savedGames` list — not usable for us, and we do not need it.

## Choice points

- A story scene is an additive scene containing `SceneController`
  (`_Scripts.AMVCC.Controllers`) plus N × `ParameterButtonChanger` — one per answer.
  `ParameterButtonChanger` carries: `ButtonTextKey` (I2-loc key), `ButtonText` (TMP),
  `ButtonIndex`, the stat/relation/status changes it applies, and all the unlock
  conditions (stats, relations, statuses, objectives, `DifficultConditions`).
- Player clicks an answer → `SceneController.NextButton_Click()`:
  1. applies Parameters/Relations/Statuses from the chosen button,
  2. triggers the Bolt flow machine (`CustomEvent.Trigger(TalismanFlowMachine, "Click")`),
     which advances the story graph and loads the next scene,
  3. broadcasts `WriteAnswerInLog(sceneName, buttonIndex)` — unobserved (see above).
- The story graph itself lives in **Bolt (Ludiq) visual-scripting flow machines** inside
  the scene assets, not in C#. `Bolt.Core.Runtime.dll` etc. are in Managed/. This means
  the *sequence* of scenes is data, not code — but we do not need it: we snapshot at
  runtime instead of statically modelling the graph.
- `GameManager.LoadScene(name)`: additive `LoadSceneAsync`, sets `OpenedSceneName`,
  registers the scene in `ShowedScenesList` (as `IsShowed=false`), unloads the previous
  scene. `SceneShowChecker.OnEnable` flips `IsShowed=true` when the scene is on screen.
- Scene names encode the story path (e.g. `01.01.01`, `052.210.060_Final5`), and
  `ShowedScenesList` is an ordered list of first-opens — from it the taken path can be
  inferred for display, but it cannot be *rewound* without replay.

## Determinism / why snapshots are exact

The game does use randomness (the community complains about it reducing Willpower), but
the roll has already resolved by the time the choice scene is on screen, and its effects
are already inside the stats captured in `SaveLoadHelper`. A snapshot taken while the
choice is displayed is therefore an exact restore regardless of RNG. We never need to
replay or re-roll.

## UI integration points

- **`WindowPause`** (`_Scripts.AMVCC.Views.Windows`) — the in-game pause window; it
  owns `_saveWindow` / `_loadWindow` references. This is "the settings" the user sees
  in game. Our button goes here (spawned programmatically; we do not edit prefabs).
- `SettingsWindow` — audio/video only. Not our target.
- `LoadWindow`/`SaveSlot` — the game's own load UI; it builds slots from
  `GetAllSaves()` (only `Autosave.dat` files). Our files live elsewhere and are never
  seen by it. Independence is structural: different folder, different code path.
- Localisation is I2 (`LocalizationManager.GetTranslation(key)`); scene titles use
  key `<SceneName>_Title`. Fallbacks: raw key or our own English strings.
- Fonts: `GameManager.Instance.DefaultMaterial` (TMP) — reuse for our UI text.

## Achievements

`SteamStatsAndAchievements` fires from scene-show events keyed on `ShowedScenesList`
contents and `BlockAchievements` (`AchievementsBlocker` scenes). Steam achievements are
one-way and idempotent; replaying a scene after loading a snapshot can at worst re-grant
an already-held achievement. No harm; noted for completeness.

## Mod loader facts

- No BepInEx/UMM installed or required. Nexus has exactly one mod (Arabic translation,
  BepInEx-based) — nothing to be compatible with.
- The game is a Windows Mono Unity 2018.3 build run under Proton. Doorstop (native
  `winhttp.dll` proxy + `doorstop_config.ini` next to the exe) is the proven bootstrap
  under Proton (it is what BepInEx uses). We vendor Doorstop and a ~200-line bootstrap
  instead of taking a BepInEx dependency.
- **Proton needs `WINEDLLOVERRIDES=winhttp=n,b`** or Wine loads its builtin winhttp and
  Doorstop never runs. Verified with `WINEDEBUG=+loaddll`: only with the override does
  the game-dir `WINHTTP.dll` load as native. For Steam players this means a launch
  option: `WINEDLLOVERRIDES=winhttp=n,b %command%`.
- Managed/.NET level: .NET 4.x equivalent (Mono 5.x). Compile plugins as **net472**.
  Unity assemblies are modular (`UnityEngine.CoreModule.dll` …).
- The mod loader blacklists nothing — this is not Besiege; `System.IO`,
  `System.Reflection` etc. are all usable. (One fewer constraint.)

## The icall-poisoning hazard (verified in-game, 23 Oct 2026)

Doorstop runs our entrypoint **before the Unity engine initialises**, so the engine's
icalls (native bindings) are not registered yet. The obvious `new GameObject()` there
throws `MissingMethodException` on `GameObject.Internal_CreateGameObject` — expected.
The **dangerous** part: Mono generates and caches the icall wrapper for a method slot on
first use, including the *failure*. Once our assembly has called `new GameObject()`
too early, **every later call from our assembly keeps throwing, forever** — verified:
still failing inside a `GameManager.Awake` postfix (engine fully up, the game itself
instantiating prefabs at that very moment). The game never notices because it only ever
creates objects via `Object.Instantiate`, a different method slot.

Rule for every future phase of this mod: **no `UnityEngine` API call may happen before
the engine is up** — not even "just to see if it works yet". All bootstrap work before
that point must be pure managed code (`System.*`, reflection, Harmony IL patching).

## Working bootstrap (verified in-game, 23 Oct 2026)

1. Doorstop entrypoint: pure-managed only. Attach an `AppDomain.AssemblyLoad` hook.
2. `Assembly-CSharp` loads as assembly #68, ~1s after the entrypoint (all engine module
   assemblies load first). In that callback, apply Harmony postfixes to the `Awake()` of
   `_Scripts.Managers.GameManager`, `_Scripts.Managers.SaveLoadManager` and
   `_Scripts.AMVCC.Controllers.MainMenuController` (Harmony is pure IL work, safe here).
3. First of those `Awake()`s runs ~12s later on the main thread, engine fully up. Its
   postfix creates the host `GameObject` (`new GameObject("Brante-Save-Load")` +
   `DontDestroyOnLoad` + `AddComponent<ModHost>`) — first-ever Unity API call, works.
4. Probe results from that host:
   - `Unity 2018.3.0f2`, platform `WindowsPlayer`.
   - `Application.persistentDataPath` =
     `C:/users/steamuser/AppData/LocalLow/SEVER/The Life and Suffering of Sir Brante`
     (Windows-style path inside the Proton prefix).
   - `Saves/` holds the player's slot; `log.txt` empty (dead code, see above).
   - `GameManager` and `SaveLoadManager` instances already exist at the main menu.
   - **`SaveLoadManager.LoadGame(SaveLoadHelper)` present at runtime** — the load path
     is exactly as decompiled.
   - Types `SceneController`, `WindowPause` and `ParameterButtonChanger` (real name:
     `_Scripts.AMVCC.Views.ParameterButtonChanger`, *not* under `Assets.`) all present.
- The game's exe is `The Life and Suffering of Sir Brante.exe` (not `Brante.exe`).
- Test-run hygiene: `timeout`-killing the Proton wrapper orphans the Wine game process
  (black screen). Kill the `…Sir Brante.exe` processes themselves, then `wineserver -k`.

## Phase 3 capture facts (verified in-game, 23 Oct 2026)

- **Choice buttons are serialized INACTIVE** in the choice scene file. The show
  animation switches them on at the end (`EndAnimationHelper.EndAnimation()`
  activates the panel; the game then collects them with `FindObjectsOfType`).
  Consequence for the recorder: `FindObjectsOfType<ParameterButtonChanger>()`
  finds *nothing* at `SceneController.Awake` (and for a while after), but
  `Resources.FindObjectsOfTypeAll<…>()` filtered by `gameObject.scene.IsValid()`
  returns them immediately — their texts (`ButtonTextKey`) are plain serialized
  fields and readable while inactive. This makes sidecar choice texts available
  at capture time, no polling needed (the polling coroutine remains as fallback).
- **Never call `SaveData`**: both overloads (`SaveData(int,int,SceneStates)` and
  `SaveData(SaveLoadHelper)`) write `Autosave.dat` into the game's `Saves/` as a
  side effect. The recorder mirrors the ~25 field copies instead — same values,
  zero game save code invoked.
- **`SaveLoadManager.Instance` can be dead mid-game.** `SaveLoadManager` is not
  `DontDestroyOnLoad` (its own private `Start()` hides `SaveLoadBase.Start()`,
  which is what would have done it), so it dies when the main-menu scene unloads;
  `Singleton<T>.OnDestroy`-style cleanup leaves `Instance` Unity-fake-null.
  Resolve with a `FindObjectOfType` fallback (its only needed member,
  the `ChapterName` lookup table, is a constant — a verbatim copy ships as
  fallback). `GameManager`/`ParametersManager`/`KeyChapterParametersController`/
  `CharacterParametersSerializeHelper` were alive at capture; same fallback
  applied to all.
- **`LoadGame` overloads differ in slot bookkeeping**: `LoadGame(int slotId)`
  also sets `_currentSaveFile` and `_currentSaveSlotInfo` (which decides WHERE
  later autosaves are written); `LoadGame(SaveLoadHelper)` does not. Loading a
  save with the bare overload made a subsequent game autosave drop
  `Autosave.dat`/`testSave.txt` at the `Saves/` root (observed; removed again).
  Phase 4 must set slot info (or block autosave) when loading snapshots.
- **Vanilla load sequence** (fully C#-drivable, no Bolt needed for the plumbing):
  1. `SaveSlot.OnPointerClick` → `LoadGame(slotId)` → `InitializeData` +
     broadcast `LoadGameComplete` → `LoadWindow` handler loads the
     `GameLoadingScreen` scene additively (and unloads itself);
  2. player clicks Continue → broadcast `StartSceneTransitionEvent` →
     `SceneTransitionDissolve` (Animator trigger) → its animation event calls
     `LoadScene()` → broadcast `SceneTransitionCompleteEvent`;
  3. `GameLoadingScreenBehaviour.AutomaticSceneLoad`:
     - `LastOpenScene == "MainMenu"` → flow-machine `Click`;
     - scene in `ShowedScenesList` **with `IsShowed=true`** → flow-machine
       `Click` (continue *past* the scene);
     - **`IsShowed=false` → `GameManager.LoadScene(LastOpenScene)`** (re-open
       the scene — exactly what a snapshot taken at a choice scene needs, since
       the capture happens before `SceneShowChecker.OnEnable` flips the flag);
  4. loading screen unloads itself either way.
  The dev harness replays steps 1–3 minus the UI clicks; it doubles as the dry
  run for Phase 4's loader.
- `GameManager.OnSceneAnimationStart` is invoked by `UIManager` on page-turn
  finish (`OnFinishPageTurnEvent`) / popup close; `SceneController`'s handler
  only plays the scene SFX — it does **not** create the buttons (they were
  already in the scene, inactive, per above).
- **Wine/Doorstop without a launch option (verified)**: the exe statically
  imports only `UnityPlayer.dll` + `KERNEL32.dll`; `UnityPlayer.dll` imports
  `WINHTTP.dll` (Doorstop's hook point; also `VERSION.dll`, `WINMM.dll`).
  Wine's builtin-first policy loads its own winhttp unless told otherwise; a
  per-app override in the Proton prefix's `user.reg`
  (`[Software\\Wine\\AppDefaults\\The Life and Suffering of Sir Brante.exe\\DllOverrides]`
  → `"winhttp"="native,builtin"`) makes the proxy load — verified by starting
  the game with no `WINEDLLOVERRIDES` and seeing the mod log. `install.sh`
  writes/removes this entry (only while the game is not running).

## Phase 4 UI + load facts (verified in-game, 23 Oct 2026)

- `UIManager`: `public static UIManager Initiate { get; private set; }` (set in
  its own `Awake`). `Update` polls Escape (unless `IsEscButtonBlocked`) →
  `ShowPauseMenu()` → `PauseWindow.SetActive(!activeSelf)`. There are **no
  `timeScale` writes anywhere in game code** — "pause" is visual only, so the
  mod's coroutines keep running while the menu is open.
- `PauseWindow` is a **public GameObject field** registered in `UIManager.Awake`.
  The live object is `Window_Pause`, which lives in the **DontDestroyOnLoad
  scene from boot** — one window present in every scene, not per-scene. It has
  the `WindowPause` component; its canvas is **WorldSpace, sortingOrder 12**;
  children: `Bg` (stretched background) + `Buttons` (831.3×881.9 rect centred
  on the window). Contents (screenshot-verified): a SETTINGS sheet with
  Music/Sound sliders, "Hidden Consequences" and "Animated Illustrations"
  toggles, QUIT TO MAIN MENU and RESUME — no Save/Load buttons in the sheet.
- Canvas inventory at the main menu: `Canvas` order 2, `EarnCanvas` 2,
  `EventTooltip` 7, `InsurrectionSideTooltip` 8, `ConditionTooltip` 9 (all
  ScreenSpaceOverlay) plus the pause window's WorldSpace 12 — so a
  ScreenSpaceOverlay canvas at **sortingOrder 8000 is always on top**.
- Our injected button is a child of `Window_Pause`: it renders and raycasts on
  the game's own pause canvas, so it is clickable exactly like the game's own
  buttons. Placement: bottom centre, 340×46, 14 units above the window's lower
  edge — below the parchment sheet, clear of RESUME.
- **In-game load flow (from pause, through the mod's UI)**: `SaveLoadManager.Instance`
  was **alive** in every test (the feared dead-singleton case did not occur;
  a `FindObjectOfType` fallback is kept anyway) → `LoadGame(helper)` → 0.5 s →
  additive `GameLoadingScreen` → wait done → `Messenger.Broadcast("StartSceneTransitionEvent")`
  → dissolve → `AutomaticSceneLoad` → `LoadScene(LastOpenScene)`. The scene
  reopens and the recorder's dedup skip on re-capture proves the state
  round-trips byte-exactly. During the flow the game calls its save entry
  points (`SaveChapterState`/`SaveGame` Harmony prefixes fired) — that is why,
  until 24 Oct 2026, a mod snapshot load switched the session-wide save gate on.
  The gate has since been replaced by a one-time backup of the game's save
  folder plus normal saving (see "Letting the game save again after a rewind").
- Dev UI test: marker `Snapshots/dev-uitest.txt` (next to `dev-autoload.txt`)
  makes `ModHost` drive pause → list → load-confirm → delete-confirm → load
  through the real `onClick` handlers, holding each state 3 s for screenshots.
- **Runtime-built TMP/UI lesson**: children created under a freshly built
  layout (VerticalLayoutGroup etc.) start with a zero-width rect, and the item
  text then painted **2+ seconds late** in practice; `Canvas.ForceUpdateCanvases()`
  right after building/refreshing the list (and after swapping confirm-dialog
  text) makes the first paint correct.

## Save-table fields (Phase 5 table, checked against the decompile)

- `Chapter` (0–5) is the life stage. `ChapterName` gives its key:
  `Chapter0/1.Childhood`, `Chapter2.Teen`, `Chapter3.Youth`, `Chapter4.Peace`,
  `Chapter5.War` (the revolt).
- `Season` is NOT a life stage. It is a counter 1–5 inside a turn
  (`IncreaceTurn.cs`: `Season++` up to 5, then back to 1 with `Turn++`).
- `Turn` counts those 5-step cycles; `Year` is the in-game year, separate.
- `LastOpenScene` looks like `01.03.01`: the first pair matches the chapter
  (`01` = chapter 1), the middle number advances with the story. It is not
  computed from turn or season.
- The settings menu is a `VerticalLayoutGroup` of rows (`Buttons > Object6/7`),
  each a horizontal wrapper around the black frame holding one button. The mod's
  button is a copy of the lowest row; the `Buttons` sheet is lifted and grown by
  one row so it fits.
- Steam Cloud does NOT sync this game (checked 23 Oct 2026). The path config is
  right: Steam's `appcache/appinfo.vdf` holds, for app 1272160, root
  `WinAppDataLocalLow`, path `SEVER/The Life and Suffering of Sir Brante/Saves`,
  pattern `*.dat` — and the client writes its `steam_autocloud.vdf` marker into
  that folder on every launch (inside the Proton prefix too). But Steam never
  uploads anything: `logs/cloud_log.txt` says, for every launch and exit,
  `AutoCloud is disabled. Run "testappcloudpaths 1272160" in the console to
  enable it` and `Sync Disabled`, the cloud change number stays 0, and no
  `userdata/<id>/1272160/` folder is ever created. Cloud enablement is a
  Steamworks setting of the app (developer side); a mod cannot turn it on.
  Second, smaller problem: the game puts each playthrough in a subfolder
  (`Saves/<Hero>_<date>/…`, the ID contains "/" from a date string), so the
  Auto-Cloud path must be marked recursive or `*.dat` only matches the marker
  file at the top of `Saves/`.

## Main-save identity (checked against the decompile)

- A new game creates the folder `Saves/<Hero>_<timestamp>` and keeps its
  `SaveSlotInfo.Id` in `SaveLoadManager._currentSaveSlotInfo`. A loaded save
  restores that object (`_currentSaveFile`). One playthrough = one ID.
- Inside one playthrough there is only one rolling `Autosave.dat` plus the
  per-chapter files, so the same hero name can exist several times (one per
  playthrough) but a playthrough never has several saves of its own.
- The mod therefore keys its saves by that ID: `Brante-Save-Load/Runs/<ID>/Auto`
  (choice points) and `.../Manual` ("Save now"). The list shows only the
  current ID. Older snapshots recorded before this change have no ID and are
  not listed.

## Run-folder migration (implemented, verified in-game 23 Oct 2026)

- Saves recorded before run folders existed had no main-save ID. `Recorder.MigrateLegacy()`
  (started from `ModHost.Start`) moves `Snapshots/*.dat` (choice points) and the old
  `Slots/*.dat` (manual saves) into `Runs/<ID>/{Auto,Manual}/`, plus whatever sat in
  `Runs/_no_save/`.
- Matching: each legacy file is read, its `HeroName` looked up in the index built from the
  game's own `Saves/**/Autosave.dat` files. Only a hero name with exactly ONE main save on
  disk is migrated; anything ambiguous stays put and is logged. On the test machine all 21
  files matched `Biggie_10/23/2026103816AM` (0 kept).
- Files are MOVED untouched: the file name carries the content hash the recorder dedups by,
  and rewriting the blob would change that hash and duplicate every choice point.
- `SlotInfo.Id` inside the blob stays empty (stable dedup); the run comes from the folder.
- Loading a save pins the run for the session (`Recorder.PinRunId`): a snapshot restores the
  game state, but the game's own `SaveLoadManager` bookkeeping can be empty afterwards, and a
  load through `LoadGame(SaveLoadHelper)` does not update `_currentSaveSlotInfo`. The run ID
  for a UI load is taken from the file's folder (`RunIdFromPath`), for the dev autoload from
  the loaded `SlotInfo.Id`.
- The table's "Saved" time comes from the sidecar's `recorded=` line, not the file timestamp
  (the first, rewriting migration reset those).

## Playthrough record: `ShowedScenesList` (Phase 6, checked 24 Oct 2026)

- `[Serializable] ShowedScenes { string SceneName; bool IsShowed; }`; the save
  and `GameManager` both hold `List<ShowedScenes>`.
- `GameManager` appends a scene when it is opened (skipping names already in the
  list) and the per-scene behaviour flips `IsShowed` when the scene is actually
  shown (`SceneShowChecker`, `ChapterCutscene`, …). The list is therefore the
  ordered path of the playthrough.
- The last entry is the scene on screen and is still `pending` at capture time —
  the same not-yet-shown flag the loader relies on to re-open a choice scene.
- Naming: story scenes are `<chapter>.<scene>.<n>` (e.g. `01.05.01`); the
  machinery uses plain names (`Disclaimer`, `Cutscene_1`, `Intro`,
  `LoadingScreen_Child`, `ChildhoodPicture`, `ChildhoodChapterStart`). Titles are
  `<scene>_Title` localisation keys; machinery scenes mostly have none, so
  "has a title" is a good filter for story scenes.
- A real save (childhood, turn 5) had 12 entries: Disclaimer, Cutscene_1, Intro,
  LoadingScreen_Child, ChildhoodPicture, ChildhoodChapterStart, 01.01.01 Birth,
  01.02.01_2 Hide and Seek, 01.02.01 The Great Descent, 01.03.01 The Lot of
  Suffering, 01.04.01 Gloria's Rhymes, 01.05.01 The Newborn's Cry (pending).

## The main-save ID inside a save is load-bearing (found 24 Oct 2026)

- `SaveLoadManager.GetSaveFolderPath(helper)` = `Saves/<helper.SlotInfo.Id>/`.
  "Restart chapter" after a death (`GoToNextDeathScene` -> `DeathWindowBehaviour`
  -> `SaveLoadManager.LoadChapterState`) reads `Chapter_N.dat` through it, for
  **`_currentSaveFile`** — the save that was loaded, not a global path.
- A save with an empty `SlotInfo.Id` therefore makes the game look in `Saves/`
  itself; the file is missing and `LoadGame(path, chapter)` falls back to
  `ResetToDefault` — the whole run is reset.
- The mod empties the ID when hashing a snapshot (so identical states hash the
  same), but now writes the real ID into the stored file and sets it on the
  loaded helper, so the game can find its own chapter files.
- While the save gate existed (before 24 Oct 2026), chapter files stayed the
  original timeline's, so a chapter restart went back to that timeline's
  chapter start. Saving is now normal, so this no longer applies.

## Letting the game save again after a rewind (24 Oct 2026)

- Loading a save through `LoadGame(SaveLoadHelper)` does NOT update
  `_currentSaveSlotInfo` (only `LoadGame(int slotId)` does). `SaveData` builds its
  path from that field, so a save right after a mod load lands in `Saves/` itself
  (`Autosave.dat` next to the save folders).
- Also, `SaveGame` is called by the game shortly after a load (scene transition),
  so this happens within seconds, not only on a manual save.
- Fix: a prefix on `SaveGame`/`SaveChapterState` forces a slot id when the mod
  knows the real one, and a postfix on `GetSaveFolderPath` redirects a bare
  `Saves/` (or a mangled `Biggie_10_23_…` folder) to the real
  `Saves/<id>/`. Verified: nothing is written at `Saves/` root any more.
- The real id contains slashes (`Biggie_10/23/2026103816AM`) and can be derived
  from a save path (`Recorder.IdFromSavePath`) even when the blob's `SlotInfo.Id`
  is empty; that is how the dev harness adopts it.
- Before the first rewind the mod copies `Saves/<id>/` (minus Steam's
  `steam_autocloud.vdf`) to `Brante-Save-Load/Backups/<id>/` and writes a
  `README.txt` with restore steps.

## No full choice tree in the game data (checked 27 Oct 2026)

- Choices are per-scene `ParameterButtonChanger` buttons; which choice leads to
  which scene lives in **Bolt visual-scripting flow machines**, stored as
  assets, not in C#. Nothing at runtime exposes the graph, so "every possible
  choice and how it branches" cannot be read from a save (or from C#).
  Enumerating it would mean parsing the Bolt assets offline - a separate
  reverse-engineering project - and it would spoil the story.
- What IS available: each captured choice point stores the exact choice texts
  offered at that scene (`choices:` in the `.meta.txt` sidecar), and the
  playthrough path (`ShowedScenesList`). That gives a partial, player-specific
  tree: what was offered where, along the paths actually taken.

## Determinism, and what a "replay" would need (27 Oct 2026)

- The game's code uses **no `Random`** at all (no `UnityEngine.Random`, no
  `System.Random` in Assembly-CSharp): outcomes are decided by parameters,
  relations, objectives and scene conditions. A replay would therefore not
  diverge by itself.
- It would still need: (a) the answer chosen at every past decision - the save
  stores only the LAST click (`CurrentConsequence`), not a history; (b) driving
  the game through scenes, popups, cutscenes and Bolt events without a player
  (`ParameterButtonChanger.Button_Click` expects the live UI; calling it on an
  inactive button throws); (c) a valid starting state, which exists only at
  chapter starts. That is a project of its own, not a data lookup.
- What IS recorded now: each click writes `chosen=<button GameObject name>` into
  the scene's sidecar (Harmony postfix on `Button_Click`), and the NEXT
  snapshot's `CurrentConsequence` names the answer picked in the previous scene.
  Sidecars list the offered choices with their button names, so the tree can mark
  the branch taken from the moment the mod is installed.
- A full graph of every possible choice is NOT available at runtime: branching
  lives in Bolt machines; only choices of scenes actually reached and captured
  are known.

## Driving the game by itself (AutoPlay, 28 Oct 2026)

`AutoPlay.cs` plays scenes the way a player clicks, which is what Load scene
(replay a picked path) and the debug runs (`dev-autoplay.txt`) need. What it
takes, all verified over chapters 1-2 and into 3:

- **Answer**: when `ChoiseDescription` exists and a `ParameterButtonChanger` is
  visible, call `Button_Click()` on the wanted one (`GetButtonState()` says
  whether the hero may take it). The game sets `CurrentConsequene` to the
  button's GameObject name (`ButtonDescriptionN`).
- **Everything else is "press the button a player would"**, sent as a real
  pointer click (`ExecuteEvents` enter/down/up/click) so animations and
  handlers run as with the mouse. Seen in two chapters: `PageControl/Next`
  (scene text and consequence pages), `AnswerSelectionControlPanel/Button`
  (the consequence's continue), `InterludePopup` `RightArrow` / `ClosePopoup`,
  `InformationPopup` and `EventPopup` `Button`, `Window_ChapterStart` /
  `Window_ChapterFinal` `RightArrow` and `NextButtonPanel` ("Begin chapter",
  `ChapterStartWindowController.NextSceneButton_Click`), the loading screen's
  `TopPanel/Button`, and the chapter title picture (`StartPictureHelper
  .OnPointerClick`). The driver picks by name (`Next`, `RightArrow`) or by the
  click handler's method/class name (Next, Continue, Close, Skip, ...), never
  the HUD, the pause menu, or Prev/Back/Settings/Exit/Load/Save handlers - and
  the never-list only looks at names and methods, since handler *classes* such
  as `ChapterStartWindowController` contain "Window".
- **Inside the mod, `Button` is ambiguous**: one of the game's namespaces
  defines its own `Button`, so `UnityEngine.UI.Button` must be spelled out or
  `FindObjectsOfTypeAll<Button>()` silently finds nothing.
- Cutscenes and loading screens need nothing; a scene costs about 20 s at
  normal speed. With `Time.timeScale = 10` (animations, page turns and timed
  waits follow it) and the sound off, about 5 s - loading is disk-bound. At
  that speed the answer panel appears before the scene is fully set up, and
  answering at once makes the game's own `SceneController.GoToConsequence`
  throw a `NullReferenceException`: wait ~0.25 s after the panel appears.
- **Answer effects can be simulated with the game's own code**:
  `ParametersManager.SetParameterValue(Parameter, int)` (clamping per stat,
  some stats added or overwritten), `SetCharacterRelation(Character, int,
  list)` (clamped to -5..5) and `SetParameterValue(Character, CharacterStatus,
  list)` are pure - no events - and work on `ParametersManager.Parameters`
  (a public `ModelParameters` field, the same object the saves store) or on the
  list passed in. Swapping a deep copy into `Parameters` for the call, then
  back, runs a "what if" without touching the game. Requirement checks
  (`CompareParametersValue`, `CompareCharacterRelation`,
  `CompareCharacterStatus`) take the same inputs.
- **The game's `Chapter_N.dat` is a clean chapter start**: loading a copy under
  a new `SlotInfo.Id` (written as both `Autosave.dat` and `Chapter_1.dat` in
  `Saves/<id>/`, so `Recorder.RealRunId` finds it) gives a separate main save
  that never touches the player's run.
- **Bug found on the way**: `Recorder.RecordChoice` paired `X.meta.txt` with
  `Path.GetFileNameWithoutExtension(..) + ".dat"` = `X.meta.dat`, which never
  exists - so no click had ever been recorded and the tree relied entirely on
  the next snapshot's `CurrentConsequence`. Fixed.

## The game's stat rows (tree view stats, 28 Oct 2026)

The character/family/province tabs draw each stat with `ParameterComponent`
(`_Scripts.AMVCC.Views.Windows`): `Icon`, `Name`, `TextValue`, `Descr` (the
segment word, key `<ParametersList>.Segment<n>` for the scale step the value
falls in), and a `SimpleSlider` or - when `MinValue == -MaxValue` - a
`TwoSideSlider`, whose `ParameterSliderComponent.ScaleContainer` takes an
instance of the stat's own `Parameter.ScalePrefab` (the scale marks). The
row prefab `Parameter_Horizontal` (375 x 103) is loaded as an asset
(`FindObjectsOfTypeAll`, scene not valid). Cloned inside an inactive parent,
none of its scripts wake up; removing every non-UnityEngine/TMPro
MonoBehaviour and disabling its Animator leaves a static row the mod fills
itself. Tab titles: `HUD.Character` (Personality), `HUD.Family`, `HUD.Home`,
`HUD.Empire` (The Province), `Work` (Occupation), `HUD.Insurrection`.
`Animator` sits in UnityEngine.AnimationModule, which the build does not
reference - disable it through its `Behaviour` base.

The stat pop-up is the game's own `ParameterValueTooltipComponent`
(`_Scripts.AMVCC.Views.Windows.Components`, registered as
`RegisterGO.Find("ParameterValueTooltip")`): `ClearTooltipWindow()`, then
`SetRow(range, segmentWord, highlighted)` per scale step, and `Title`,
`Icon`, `Description` (key `<ParametersList>.Description`). Its root carries
its own `Canvas` with `overrideSorting` at order 9, so a clone parented into
a window drawn at a higher order is invisible until its canvas order is
raised too. It is about 740 units tall - taller than the room above a row
near the bottom of the screen.

The row prefab `Parameter_Horizontal` carries a `HorizontalLayoutGroup` +
`ContentSizeFitter`: any child added to it (a hover hit area, say) is laid
out as one more cell and squeezed to 0 x 0, so it never receives the
pointer. Give such a child a `LayoutElement` with `ignoreLayout`. Its row
content is laid out at fixed widths: shrinking the row's width overlaps the
icon, text and slider instead of narrowing them.

## TextMesh Pro in this game (29 Oct 2026)

The game's TMP build returns nonsense from `GetPreferredValues` /
`preferredWidth` for labels made at runtime (10.8 units for "Load scene" at
size 22, and the longest word's width when word wrap is on). To size
something to its text, turn wrap off and overflow to `Overflow`, call
`ForceMeshUpdate()` and read `textBounds` - which needs the label active.

## The book's HUD (29 Oct 2026)

Each story scene brings its own HUD (`Buttons/HUD.*`), so anything added to
it has to be added again after every scene load. `HUD.Settings` is one
Image (`icon_menu_off`, the gear medallion) with Button, Notification,
Animator, `HudButtonBehavior` and `PointerSfxPlayer`, anchored top right at
(-544.6, -86.3). `HUD.Chapter` (`icon_chapter_on`, a plain medallion) and
`HUD.Main` (the year) sit top left at x 542.4 and 375.9. The wooden strips
behind the chapter and settings medallions are not part of the buttons:
they are painted into the book's background images (`HUD/BG` sprite
`background`, `Back/Panel` sprite `book_new`). A copy of a tab elsewhere
needs the patch of those sprites under the original cut out
(`Sprite.Create` on the same texture, no read access needed) and laid
behind it. A copied Button keeps its persistent `onClick` - replace the
event, or the copy also opens the settings.

## Chapter 5's texts are not in the shipped CSV (29 Oct 2026)

The localisation CSV inside `resources.assets` is an older export: the
`060.*` keys (chapter 5) have Russian only, so a table built from it has no
English titles or answers for that chapter. The I2 `LanguageSource` loaded
at runtime has every language, so the mod reads titles and answers live
(`Loc.Game`) and falls back to the shipped table only where the game has no
text. Button keys must be sorted by their number (`ButtonDescription10`
comes after `2`).

## Stat tabs and when they open (29 Oct 2026)

Each book tab is a `HUD.*` button with a `HudButtonBehavior` whose
`ChapterToUnlock` decides when it opens (`HUDController.CheckHudButtons`:
unlocked when `<= GameManager.Instance.Chapter`; until then the tooltip is
`HUD.WillOpen<N>`). The windows list fixed stats in their prefabs
(`ParameterGetSet.Parameter`), not by category:

| tab (opens in) | window | stats |
|---|---|---|
| `HUD.Character` (1) | Window_Character | Will, Curiosity, Activity, Discipline, Cunning, Spirituality, Diplomacy, Valor, Manipulation, Sabotage, Theology, Preaching, Death |
| `HUD.Home` (1) | Window_Home | Unity, Money, Reputation (+ Heir) |
| `HUD.Destiny` (1) | Window_Soul | Volition, Love, Rule |
| `HUD.Empire` (2) | Window_Empire | State, Church, Prosperity, Order |
| `HUD.Inquisition` (4) | Window_Work | Authority, Carier, Protest, Justice, Repression, Toleration |
| `HUD.Insurrection` (5) | Window_Insurrection | Insurrection, Army, NoblesMans, PriestsMans, CommonerMans |

Stats no window shows: Tolerance and GWrath (EmpireParameter), Control,
Cruelty, Weight and Сontention (WarParameter; the first letter of
"Сontention" is Cyrillic). The asset "Emperor" is a stray copy of Will with
no icon. The values of a locked tab's stats exist in the save all along.

`book_new` (the open book, `Back/Panel`) is drawn with `preserveAspect`: its
sprite is 1.61:1, so on a 16:9 screen it is narrower than its rect. Mapping
screen points onto it must use the fitted rect. The settings tab button
swaps in its gear sprite on hover (Button sprite swap / `HudButtonBehavior.
_highlightedButton`); a copy needs its transition changed.

Scene conditions in `scene-flow.tsv` look at story objectives
(`GetObjectiveStatus`, 413 uses), stats (`GetParameterValue`, 252),
characters' fates (`GetCharacterStatus`, 199), scenes already shown
(`GetSceneShowStatus`, 29), other scenes' conditions (5) and relationships
(`GetCharacterRelation`, 4).

