# Making a mod for The Life and Suffering of Sir Brante

Short map for an AI agent. Deep detail lives in `docs/FINDINGS.md` (game
internals and the experiments that proved them), `docs/DESIGN.md` (this mod's
design), `docs/BRANCHES.md` (scene-flow/choice data), `docs/TODO.md` (status)
and `AGENTS.md` (repo rules). This file is the orientation.

## Stack

- Unity **2018.3.0f2**, Mono / .NET 4.x, C# 7.3-era runtime. Windows build
  usually under Proton; paths inside the game are Windows paths.
- No mod loader. **Unity Doorstop 4.6.0** (`winhttp.dll` +
  `doorstop_config.ini`) makes Mono call `Doorstop.Entrypoint.Start()` before
  the engine initialises.
- **Lib.Harmony 2.3.3** (`vendor/0Harmony.dll`, net472) for patches.
- `tools/build.sh` compiles `Brante-Save-Load/Scripts/*.cs` with `csc` from a
  dotnet SDK (>= 6) against the installed game's own `_Data/Managed` DLLs
  (mscorlib, UnityEngine modules, UI, TMP, Audio, IMGUI,
  `Assembly-CSharp.dll`). No csproj/NuGet.
- `tools/install.sh` writes `winhttp.dll` + `doorstop_config.ini` into the game
  folder and symlinks/copies `Brante-Save-Load/` there. `--copy`,
  `--uninstall`, `--purge` exist. Never build while the game is running (the
  DLL is mapped).

```sh
./tools/build.sh
./tools/install.sh
```

Set `BRANTE_DIR` if the game is not found, `DOTNET_DIR` for a non-default SDK.

## Entry point: the one hard rule

`Entrypoint.Start()` runs **before Unity exists**. Do not touch the Unity API
there. A failed `new GameObject()` at that point poisons Mono's icall wrapper:
the call keeps throwing even after boot. The working pattern:

1. `Start()` registers `AppDomain.AssemblyLoad` (pure managed).
2. When `Assembly-CSharp` loads, apply Harmony patches (pure managed).
3. First patched `Awake` postfix creates the host `GameObject`:
   `GameManager.Awake`, `SaveLoadManager.Awake`, `MainMenuController.Awake`.
4. `ModHost` is the persistent MonoBehaviour for `Update`/coroutines. Use
   `ModHost.Current.StartCoroutine(...)`.

Log with `ModLog` to `Brante-Save-Load/Brante-Save-Load.log`; that is the mod's
only diagnostic channel. Game exceptions land in the game's `output_log.txt`.

Patches are applied by hand, from a table of type/method/patch names in
`Entrypoint` (`"_Scripts.Managers.GameManager, Assembly-CSharp"`, `"Awake"`,
`"BootHookPostfix"`), resolved with `Type.GetType` in the `AssemblyLoad` handler
for `Assembly-CSharp` - the entry point runs before that assembly is loaded, so
nothing there may name a game type directly.

Useful game types (`Assembly-CSharp.dll`, namespace `_Scripts.*`):

- `_Scripts.Managers.GameManager` — boot, turn/season, `ShowedScenesList`,
  `OpenedSceneName`, `LoadScene`.
- `_Scripts.Managers.SaveLoadManager` — saves, `LoadGame(...)` overloads,
  `SaveGame`, `SaveChapterState`, `GetSaveFolderPath`, `LoadSaves`.
- `_Scripts.AMVCC.Controllers.SceneController` — every choice scene; `Awake`
  is the "scene open" hook.
- `_Scripts.AMVCC.Views.ParameterButtonChanger` — one per answer button;
  `Button_Click` is the answer hook.
- `_Scripts.AMVCC.Models.Static.KeyChapterParametersController` — switches
  (`Post`, `TriggerBlock`, `PeaceTree`, `GoToFinals`, ...) and
  `AllGameObjectives`.
- `_Scripts.AMVCC.Views.Windows.Destiny.Objective` /
  `_Scripts.AMVCC.Views.Windows.ObjectiveEnum` — story objectives (destinies).
- `_Scripts.AMVCC.Models.Static.Character` / `CharacterList` /
  `CharacterModel` — people: the asset (portraits, estate, family), the enum
  (Cyrillic names for the later cast: `Айлис` is Aylis), the saved relation
  and status.
- `_Scripts.AMVCC.Views.Windows.Death.*` — death page.

Decompile with:

```sh
DOTNET_ROOT=$HOME/.dotnet ~/.dotnet/tools/ilspycmd \
  -t _Scripts.Managers.SaveLoadManager \
  "/path/to/..._Data/Managed/Assembly-CSharp.dll"
```

The story is **not C#**: chapter/answer flow lives in Bolt visual-scripting
machines. Hook at runtime; do not try to statically model the whole story.
Choice scenes are additive scenes containing `ParameterButtonChanger` buttons.
The game's `log.txt` answer log is dead code (never written).

## Save system

- One `[Serializable]` `SaveLoadHelper` holds the entire game state, written
  with `BinaryFormatter`. One playthrough has one live `Saves/<id>/Autosave.dat`
  (rolling) plus `Chapter_N.dat` at chapter starts. The folder name **is**
  `SlotInfo.Id` and contains slashes.
- `SaveLoadManager.LoadGame(SaveLoadHelper)` is **public** and is the safe mod
  load path: restores all singletons and resumes via `LastOpenScene`.
- Normally never mint/rename a save folder: a new game's folder comes from the
  game's own `CreateSaveLoadSlot`. **Exception**: a save with no main-save id
  loaded through the mod gets one via `Recorder.NewSlotId` (same shape as
  `CreateSaveLoadSlot`: hero + `_` + timestamp), so the game's autosave and
  chapter files have a folder and its menu lists the game.
- Never duplicate the live save. One immutable baseline of the pre-mod save
  (`Backups/<id>/`) plus a checkpoint before every rewind; this repo's stores
  are `Runs/<id>/{Choice,Checkpoints,Autosaves,Manual}`.
- A loose `Saves/Autosave.dat` is merged only into a run it continues (same
  hero, the run's whole scene list is the start of the loose one, loose file
  newer, unique best match); everything else stays put. An older loose state
  never replaces a newer run save.
- `SaveLoadManager.Instance` can be a **destroyed object** the game still saves
  with. Unity's `== null` lies; use `Recorder.SaveManagers()` /
  `GameSaveManager()` rather than `== null` + `FindObjectOfType`.
- `_no_save` is a folder name only; never pass it to the game as a save id.
- Loading through the game's UI flow: open `GameLoadingScreen` additively,
  broadcast `StartSceneTransitionEvent`, let the dissolve call
  `AutomaticSceneLoad`. Loading a save of the **same scene on screen** leaves
  two copies unless the old copy is unloaded after `LoadGame` (see
  `ChoiceWindow.LoadFlow`).
- Pop-ups (`Interlude`, `Event`, `Trigger`, `Objective`) live on
  `UIManager.DialogCanvas` and survive scene changes; close them before a mod
  load or an interlude from the left-behind scene applies its stats to the
  loaded state.
- `tools/nrbf.py` reads a `.dat` save outside the game (BinaryFormatter/MS-NRBF).

## Story data this repo extracts

Shipped TSVs, regenerated from the installed game; never hand-edit:

| file | meaning |
|---|---|
| `scene-texts.tsv` | localised scene titles and answer texts/names |
| `scene-flow.tsv` | one row per flow occurrence: `<chapter> <order> <scene> <turn> <season> <labels> <condition>` |
| `scene-effects.tsv` | answer effects/requirements, scene-opening (button 0) changes, character unlocks, title/name keys |
| `scene-params.tsv` | key chapter parameter changes per scene/answer |
| `scene-objectives.tsv` | objectives a scene/answer unlocks (`EarnObjective`) |

```sh
python3 tools/bolt_story.py        # scene-flow.tsv (UnityPy)
python3 tools/localization.py      # scene-texts.tsv (live I2 terms, not the old CSV)
python3 tools/scene_effects.py     # scene-effects.tsv (UnityPy)
python3 tools/scene_params.py      # scene-params.tsv (UnityPy)
python3 tools/scene_objectives.py  # scene-objectives.tsv (UnityPy)
```

## Branching and the forward simulation

- Bolt chapter macros are gates. The chapter flow **restarts at the top after
  every scene**, so a triggered scene earlier in flow order is still a
  candidate. `scene-flow.tsv` is in topological order; siblings keep port order.
- Turn gates can be a single `N` or a range (`N-M`, `N-`, `-M`) for `<`, `<=`,
  `>`, `>=` comparisons. `SceneFlow.InSlot` tests them; a row whose last turn
  passed is dropped. Immediate scenes come first, then later slots by
  (turn, season).
- A switch's default branch is a label too: `Name=!A,B` means "none of these".
  Unset save values take the default (`None`/`No` are never listed).
- Conditions test stats, relations, character statuses, objectives and key
  parameters. The tree is a **forward simulation**: `Sim` starts from the scene
  on screen (or the first changed played scene's snapshot), steps
  `SceneFlow.Step` to the end of the story, applies a scene's opening changes,
  the chosen/first-open answer's effects/objectives/parameters, then the
  scene's own objectives. The live managers are swapped in only for the
  synchronous condition call and always restored.
- Scene opening changes are button `0` (`SceneEffects.ForScene`): answer-less
  `ConsequenceSceneController` scenes (incidents, triggers, finals) and PREGAME
  interludes. `KeyChapterParametersChanger` off the answer buttons is button
  `-1` (text-page Next / every answer's continue) and applies with any answer.
- Objectives are earned **after** the answer, not at scene open
  (`scene-objectives.tsv`). `KeyChapterParametersController.EarnObjective`
  tests nothing: an objective's `Condition` list only greys its entry in the
  Destiny window (`ObjectiveInitializer.GetObjectivePossibility`), and it can be
  wrong (both faith shields list Will >= 0, which only one answer needs). The
  earning answer's requirements are the real gate. A character is met when the scene opens
  (`UnlockCharacter`, button-0 `U` rows), not by the answer.
- The answer button number comes from the `ButtonDescriptionN` **object name**:
  nine buttons in six scenes serialise `ButtonIndex == 0`.
- The fourth death (`Death = 4`) and any `GoToFinals=Yes` end the story, not
  the chapter flow. The game then runs the `GameFinals` macro: the true-death
  pages first, then - unless `FuckOffGods=Yes` (The Judgment of the Twins:
  straight to the credits) or `DeathBeforeInsurrection=Yes` - "The Outcome of
  the Revolt" pages (`070.090.0x0_TheOutcomeOfTheUprisingN`, then
  `Final_Provinces`), the family's fates, the credits. The prediction ends in
  those boxes and does not plan on.
- A character's starting relation is not always 0 (Felipe starts at -1), so
  "relation != 0" does not mean a scene changed it. Use the `R`/`S` rows.
- A plan is still a prediction. `AutoPlay` clicks the picked answer, pages
  consequences/pop-ups, and checks every scene against `plan.Order`;
  skipped/off-path stops and opens the tree with the scene named. Never claim a
  path without an ordered replay check.

## Mod architecture (this repo)

- `Entrypoint` patches, `ModHost` runs, `Recorder` owns snapshots/saves/run
  identity/repair, `ChoiceWindow` the book tabs and window, `AutoPlay` the
  replay.
- `History` reads the game's scene path; `SceneTexts`, `SceneEffects`,
  `SceneFlow`, `SceneParams`, `SceneObjectives` load the shipped tables;
  `StatPanel` draws the stat/people/Destiny panels; `Palette`, `Loc` own look
  and text (13 language tables; the game ships 14 I2 languages, both Spanish
  share `_es`).
- `PathId` turns a live state into a short text ID (`BSL1:...`) for copy/paste.
  It never feeds BinaryFormatter: it encodes the game's fixed lists, scenes,
  keys, varints and dates, then raw DEFLATE; decoding validates shape, length,
  CRC, counts, enums, strings and scene index before changing anything, then
  saves a `Manual/pathid_*.dat` and loads it through the normal table path.
- `AutoPlay` runs at 40x, scales `Time.fixedDeltaTime` and raises
  `Time.maximumDeltaTime`, turns text pages in one step and waits for buttons to
  be reachable. After an answer-screen continue it waits 0.25 s real time before
  the next press (closer caused a native crash).
- Achievements: the game unlocks only while `GameManager.BlockAchievements` is
  false; the loading screen clears it, the main menu sets it.
  `ChoiceWindow.AllowAchievements` clears it after every mod load unless a
  scene blocks on purpose.
- Chapter flow macros contain Bolt `Once` flags that are not in the save; a
  second chapter end in one session used to crash. `BoltDump.ResetOnceUnits`
  clears them in `LoadFlow`.
- `FourthDeath_3.._6` wire CONTINUE to a button with an empty `DeathObjective`;
  the game throws and stops. `Entrypoint.TrueDeathPrefix` calls
  `SceneButton_Click` instead (game bug workaround).

## UI (Unity UI + TextMesh Pro, 2018.3)

- Measure TMP text with its object **active** and a real height limit:
  `GetPreferredValues(text, width, 10000)`. Inactive, or with a height of 0,
  it measured a third to a tenth of the true size.
- TMP leaves trailing spaces out of `GetPreferredValues`; add a space's width
  yourself when placing words one by one.
- A `LayoutElement` outranks the layout group's own sizing and keeps its
  values between uses: reset `minWidth`/`preferredWidth` to -1 before
  measuring a reused box. Wrapped rows need two
  `LayoutRebuilder.ForceRebuildLayoutImmediate` passes to settle.
- Struck text: put the colour before `<s>` (`<color=..><s>..</s></color>`);
  TMP draws the line in the colour current at the tag.
- A line that must look the same everywhere is a whole number of screen
  pixels: width in units = pixels x (Screen.height / 1080) / lossyScale.
- A sorting `Canvas` on one row does not lift it out of a masked scroll view;
  shade around it instead.
- The game's tooltip (`TooltipComponent`) is sized for a stat's scale; long
  text goes in a pop-up of your own (clone its parchment sprite and font).

## Dev harness

Markers in `<persistentDataPath>/Brante-Save-Load/Snapshots/`, one-shot, mostly
paired with `dev-autoload.txt`. Read `AGENTS.md` for the full table; the ones
worth knowing first:

- `dev-autoload` — load the newest game autosave through the vanilla flow.
- `dev-load` — then load one mod save by name under `Runs/` (table path:
  checkpoint, baseline, adopt). Other dev markers still run afterwards.
- `dev-plan` / `dev-replay` — build a path to a scene, optionally replay it
  (`dev-replay` with `N`: reload and replay N times; `dev-slow` keeps 1x).
- `dev-pick` — drive the tree: `scene=ButtonDescriptionN`, `walk N`, `hover`,
  `load`, `stopafter`, `destinyall`, `pathid`, `lang`.
- `dev-book` / `dev-loadpath` — replay matrix (`back N`, `chapter`, `start`,
  `ahead`, `death`, `offpath`, `first`, `new`, `file <name>`).
- `dev-table` — table filter/paging/Original row; `dev-table-load` clicks the
  Original row, `dev-table-open` leaves it open.
- `dev-autoplay` — `chapters N...` starts a fresh debug run from the newest
  `Chapter_1.dat`, plays alive answers (no Death, most Will) and saves at
  chapter starts; `on` continues from the current scene to the credits.
- `dev-stat`, `dev-keyparam`, `dev-capture`, `dev-history`, `dev-restart`,
  `dev-stattrail`, `dev-uitest`.

Kill the game's own `…Sir Brante.exe` processes (not only the Proton wrapper),
then `wineserver -k`. Read the mod log after every run.

## Pitfalls that cost time

- Never call Unity before the engine is up (see entry point).
- Unity 2018 Mono: stick to .NET 4.x-era APIs; no `Span`, no modern reflection.
- Do not duplicate the live game save; never write to `Saves/` root.
- Additive scene loads duplicate a scene if the old copy is not unloaded.
- Fast replay needs `Time.fixedDeltaTime` and `Time.maximumDeltaTime` handled;
  scale alone causes a fixed-step spiral. Pressing the next button too soon
  after a continue crashed the game in native code.
- A run folder accumulates snapshots from several attempts. `OnThisTimeline` /
  `ResolveTimeline` reject another timeline's save, and the forward simulation
  ignores scene-list entries after the scene on screen; the ordered replay is
  still final.
- A snapshot taken at `SceneController.Awake` is before PREGAME interludes; the
  live answer screen is after them. `Recorder.LiveSceneState` distinguishes
  `SHOW_ANIMATION` from `GAME_SETUP` so a load does not run interludes twice.
- `ShowedScenesList` order is not reliable enough to treat as play order when
  attempts mix; trust the current scene plus the flow simulation.
- The game's own `log.txt` is dead code; record answers yourself.
- Achievement/pointer `NullReferenceException`s after an AutoPlay click are
  game-side noise; the replay can still continue.

## Where to read next

- `docs/FINDINGS.md` — every game fact, with the experiment that proved it.
- `docs/DESIGN.md` — save ownership, load flow, run identity, path model.
- `docs/BRANCHES.md` — flow, answer effects, objectives extraction, localization.
- `docs/TODO.md` — what remains.
- `AGENTS.md` — repo layout, build/install, dev markers, invariants.
