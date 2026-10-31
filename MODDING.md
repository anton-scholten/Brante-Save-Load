# Making a mod for The Life and Suffering of Sir Brante

Short, practical notes for an AI agent. Deep detail lives in `docs/FINDINGS.md`
(game internals), `docs/DESIGN.md` (this mod's design), `docs/BRANCHES.md`
(choice data) and `AGENTS.md` (repo rules). This file is the map.

## The stack

- Unity **2018.3.0f2**, Mono / .NET 4.x, C# 7.3-era runtime. The Windows build
  usually runs under Proton; all paths inside the game are Windows paths.
- No mod loader, no BepInEx. Injection is **Unity Doorstop 4.6.0**:
  `winhttp.dll` + `doorstop_config.ini` in the game folder make Mono call
  `Doorstop.Entrypoint.Start()` before the engine initialises.
- Patching is **Lib.Harmony 2.3.3** (`vendor/0Harmony.dll`, net472).
- `tools/build.sh` compiles every `Brante-Save-Load/Scripts/*.cs` with `csc`
  from any dotnet SDK (>= 6) **against the installed game's own**
  `_Data/Managed` assemblies (mscorlib, UnityEngine modules, UI, TMP,
  `Assembly-CSharp.dll`). No csproj, no NuGet.
- `tools/install.sh` writes `winhttp.dll` + `doorstop_config.ini` into the game
  folder and symlinks/copies `Brante-Save-Load/` there. `--copy`, `--uninstall`,
  `--purge` exist. Never build while the game is running (the DLL is mapped).

```sh
./tools/build.sh
./tools/install.sh
```

Set `BRANTE_DIR` if the game is not auto-found, `DOTNET_DIR` for a non-default
SDK.

## Entry point: the one non-obvious rule

`Entrypoint.Start()` runs **before Unity exists**. Do not touch the Unity API
there. A failed `new GameObject()` at that point poisons Mono's icall wrapper:
the same call keeps throwing even after boot. The safe pattern used here:

1. `Start()` registers `AppDomain.AssemblyLoad` (pure managed).
2. When `Assembly-CSharp` loads, apply Harmony patches (pure managed, safe).
3. The first patched `Awake` postfix creates the host `GameObject`:
   `GameManager.Awake`, `SaveLoadManager.Awake`, `MainMenuController.Awake`.
4. `ModHost` is the persistent MonoBehaviour for `Update`/coroutines. Use
   `ModHost.Current.StartCoroutine(...)`.

Log with `ModLog` to `Brante-Save-Load/Brante-Save-Load.log`. That log is the
mod's only diagnostic channel; game exceptions land in the game's
`output_log.txt`.

A patch looks like this (string targets in `Entrypoint.cs` do the same):

```csharp
[HarmonyPatch(typeof(_Scripts.Managers.GameManager), "Awake")]
static class BootPatch {
    static void Postfix() { ModHost.Ensure(); }
}
```

Useful game types (all in `Assembly-CSharp.dll`, namespace `_Scripts.*`):

- `_Scripts.Managers.GameManager` — boot, turn/season, `ShowedScenesList`,
  `OpenedSceneName`, `LoadScene`.
- `_Scripts.Managers.SaveLoadManager` — saves, `LoadGame(...)` overloads,
  `SaveGame`, `SaveChapterState`, `GetSaveFolderPath`, `LoadSaves`.
- `_Scripts.AMVCC.Controllers.SceneController` — every choice scene; `Awake`
  is the "scene is open" hook.
- `_Scripts.AMVCC.Views.ParameterButtonChanger` — one per answer button;
  `Button_Click` is the answer hook.
- `_Scripts.AMVCC.Models.Static.KeyChapterParametersController` — story
  switches (`Post`, `TriggerBlock`, `PeaceTree`, `GoToFinals`, ...) and
  `AllGameObjectives`.
- `_Scripts.AMVCC.Views.Windows.Destiny.Objective` / `ObjectiveEnum` — story
  objectives.
- `_Scripts.AMVCC.Views.Windows.Death.*` — the death page.

Decompile with `ilspycmd` (`~/.dotnet/tools/ilspycmd`), e.g.:

```sh
DOTNET_ROOT=$HOME/.dotnet ~/.dotnet/tools/ilspycmd \
  -t _Scripts.Managers.SaveLoadManager \
  "/path/to/..._Data/Managed/Assembly-CSharp.dll"
```

The story itself is **not C#**: chapter/answer flow lives in Bolt visual
scripting machines. Hook at runtime; do not try to statically model it.
Choice scenes are additive scenes containing `ParameterButtonChanger` buttons.
The game's `log.txt` answer log is dead code (never written).

## Saves (the part that can destroy player state)

- The game serialises one `[Serializable]` class, `SaveLoadHelper`, with
  `BinaryFormatter`. It holds the entire state. One playthrough has one live
  `Saves/<id>/Autosave.dat` (rolling) plus `Chapter_N.dat` at each chapter
  start. The folder name **is** `SlotInfo.Id` and contains slashes.
- `SaveLoadManager.LoadGame(SaveLoadHelper)` is **public** and is the safe mod
  load path: it restores all singletons and resumes via `LastOpenScene`.
- Never mint a save folder. Write only inside the mod's own folder, plus the
  one-time id repair on existing saves. A new game's folder must come from the
  game's own `CreateSaveLoadSlot`.
- To load through the game's UI flow: open `GameLoadingScreen` additively,
  broadcast `StartSceneTransitionEvent`, let the dissolve call
  `AutomaticSceneLoad`. Loading a save of the **same scene on screen** leaves
  two copies unless you unload the old copy after `LoadGame` (this mod's
  `LoadFlow` does exactly that).
- `SaveLoadManager.Instance` can be a **destroyed object** the game still
  saves with. Unity's `== null` lies; use `Recorder.SaveManagers()` /
  `GameSaveManager()` rather than `== null` + `FindObjectOfType`.
- `_no_save` is a folder name only (a game with no main-save id). It must
  never reach the game as a save id.
- Keep one immutable baseline of the player's pre-mod save, and a checkpoint
  before every rewind. This repo's model: `Backups/<id>/` (one-time, never
  pruned) + `Runs/<id>/{Choice,Checkpoints,Autosaves,Manual}`.

## Story data this repo extracts

Shipped TSVs, regenerated from the installed game (never hand-edit):

| file | meaning |
|---|---|
| `scene-texts.tsv` | localised scene titles and answer texts |
| `scene-flow.tsv` | one row per flow occurrence: `<chapter> <order> <scene> <turn> <season> <labels> <condition>` |
| `scene-effects.tsv` | each answer's stat/relation/status changes and its requirements |
| `scene-params.tsv` | key chapter parameter changes per scene/answer (button 0 = scene) |
| `scene-objectives.tsv` | objectives a scene/answer unlocks (`KeyChapterParametersController.EarnObjective`) |

Regenerate after a game update:

```sh
python3 tools/bolt_story.py        # scene-flow.tsv (UnityPy)
python3 tools/localization.py      # scene-texts.tsv
python3 tools/scene_effects.py     # scene-effects.tsv (UnityPy)
python3 tools/scene_params.py      # scene-params.tsv (UnityPy)
python3 tools/scene_objectives.py  # scene-objectives.tsv (UnityPy)
```

Branching facts:

- The chapter flow **restarts at the top after every scene**, so a triggered
  scene whose flow position is earlier is still a candidate. `SceneFlow.After`
  returns every unshown scene passing the live labels/conditions, in flow order.
- Conditions use stats, relations, character statuses, objectives and key
  chapter parameters. Evaluate them with a **simulated hero** (swap
  `ParametersManager.Parameters`, `CharacterParametersSerializeHelper.Characters`
  and `AllGameObjectives[*].Unlocked`, then restore).
- Objectives are earned **after the answer** (when the consequence fade ends),
  not at scene open. `scene-objectives.tsv` + `Hero.ApplyObjectives` models this.
- Key parameters drive whole tracks (`Post=Judge/Inquisitor/Rebel/...`, `PeaceTree`,
  `TriggerBlock`, `GoToFinals`); scene/open changes are button `0`, answer
  changes are button N.
- The answer button number must come from the `ButtonDescriptionN` **object
  name**: nine buttons in six scenes serialise `ButtonIndex == 0`.
- A plan is only a guess: replay it through `AutoPlay`, which clicks the picked
  answer, pages consequences/popups and checks every scene against
  `plan.Order`; a skipped/off-path scene stops and opens the tree with the
  scene named. Never claim a path without an ordered replay check.

## Dev harness

Marker files in `<persistentDataPath>/Brante-Save-Load/Snapshots/` are one-shot
and read at runtime. Pair most with `dev-autoload.txt`. Useful ones:

- `dev-autoload` — load the newest game autosave through the vanilla flow.
- `dev-load` — then load one mod save by file name under `Runs/` (table path:
  checkpoint, baseline, adopt).
- `dev-plan` / `dev-replay` — build a path to a scene, optionally replay it.
- `dev-book` — drive the tree/save table UI; with `dev-loadpath` it runs the
  replay matrix (`back N`, `chapter`, `start`, `ahead`, `death`, `offpath`,
  `first`, `new`, `file <name>`).
- `dev-table` — open the table and cycle its type filter.
- `dev-autoplay` — `chapters 2 3` starts a fresh debug run from the newest
  `Chapter_1.dat`, plays first-open answers and makes saves at chapter starts.
- `dev-stat`, `dev-keyparam`, `dev-capture`, `dev-history`, `dev-uitest`.

Kill the game's own `…Sir Brante.exe` processes (not only the Proton wrapper),
then `wineserver -k`. Read the mod log after every run.

## Pitfalls that cost time

- Never call Unity before the engine is up (see entry point).
- Unity 2018 Mono: stick to .NET 4.x-era APIs; no `Span`, no modern reflection.
- Do not duplicate the live game save; never write to `Saves/` root.
- Additive scene loads duplicate the scene if the old copy is not unloaded.
- Fast replay at `Time.timeScale = 10` needs `Time.fixedDeltaTime` scaled too,
  or the game sinks to 1 fps in a fixed-step spiral.
- Achievement/pointer `NullReferenceException`s after an AutoPlay click are
  game-side noise; the replay can still continue.
- A run folder accumulates snapshots from several attempts; `ShowedScenesList`
  in a snapshot can carry scenes from a later attempt. `OnThisTimeline` /
  `ResolveTimeline` keep the tree from using another timeline's save; the
  ordered replay is still the final authority.
- Objectives earned by scenes **still ahead** are not simulated (the tree only
  knows the current answer's objectives); such a plan is speculative and may
  stop at the real track. Same for a key parameter set only by a pop-up
  (`052.210.050_Final5`'s true-death `GoToFinals`).

## Where to read next

- `docs/FINDINGS.md` — every game fact, with the experiment that proved it.
- `docs/DESIGN.md` — save ownership, load flow, run identity, path model.
- `docs/BRANCHES.md` — scene flow, answer effects, objectives extraction.
- `docs/TODO.md` — what remains.
- `AGENTS.md` — repo layout, build/install rules, dev markers, invariants.
