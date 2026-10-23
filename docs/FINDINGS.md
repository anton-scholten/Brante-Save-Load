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
  not persist choice history. (Verify at runtime with the probe, but the decompile is
  unambiguous: no other caller.)
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
- Managed/.NET level: .NET 4.x equivalent (Mono 5.x). Compile plugins as **net472**.
  Unity assemblies are modular (`UnityEngine.CoreModule.dll` …).
- The mod loader blacklists nothing — this is not Besiege; `System.IO`,
  `System.Reflection` etc. are all usable. (One fewer constraint.)

## Open questions for the runtime probe

1. Confirm `log.txt` is empty in a real playthrough (expected: yes, dead code).
2. Exact timing of `SaveGame` calls from the Bolt flow (which state transitions
   autosave on) — matters only for deciding our own snapshot hook point; our plan is
   to hook scene-load instead, which does not depend on this.
3. Whether `WindowPause` exists in all in-game scenes (it should; verify).
4. Where `persistentDataPath` actually lands under this Proton prefix.
