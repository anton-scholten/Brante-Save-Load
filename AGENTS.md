# Working on this repository

Notes for anyone, human or AI agent, changing this mod. The [README](README.md) is
for people who just want to use it.

## What it is

A mod for [The Life and Suffering of Sir Brante](https://store.steampowered.com/app/1272160/)
(Unity 2018.3, Windows build under Proton): load the game at **any choice you have
already reached**, plus a save/load system of its own that does not touch the game's
saves. A settings button opens the list of past choice points; clicking one returns
the game to that exact choice screen. [docs/FINDINGS.md](docs/FINDINGS.md) explains
how the game works and where every fact used by the mod came from.

## Layout

```
Brante-Save-Load/            the folder that gets installed into the game dir
  Brante-Save-Load.dll       built by tools/build.sh; the Doorstop entry assembly
  0Harmony.dll               vendored Harmony (copied by the build from vendor/)
  scene-texts.tsv            every scene's title and answers (from the game; see data/)
  scene-edges.tsv            the story's scene chains (from the game; see data/)
  Scripts/*.cs               the source
    Entrypoint.cs            Doorstop entry: Harmony patches, host creation
    ModLog.cs                file logger (usable before Unity is up)
    ModHost.cs               host component; probes, dev markers, save-slot adopt
    Recorder.cs              snapshots, dedup, backup, traces, save repair
    ChoiceWindow.cs          save table, history tree, explore path builder
    History.cs               the save's scene path + offered answers
    SceneTexts.cs            loader for scene-texts.tsv (titles + answers)
    SceneEdges.cs            loader for scene-edges.tsv (choice -> next scene)
    Loc.cs                   the UI's own translations (11 languages)
    BoltDump.cs              runtime dump of the choice machine (dev only)
doorstop/doorstop_config.ini the Doorstop config installed into the game folder
vendor/                      vendored third-party binaries + their licenses
  doorstop/winhttp.dll       Unity Doorstop 4.6.0 (x64), LGPL-2.1
  0Harmony.dll               Lib.Harmony 2.3.3 (net472), MIT
tools/build.sh               compile against the game's own Managed/ DLLs
tools/install.sh             build, then symlink/copy into the game folder
tools/steam.sh               Steam library lookup (distro/Flatpak/Snap), sourced by both
tools/localization.py        extract titles/answers -> data/, shipped TSV
tools/bolt_extract.py        dump every Bolt graph as JSON (no UnityPy needed)
tools/bolt_graph.py          Walk Bolt graphs (macro resolution, LoadScene)
tools/bolt_scenes.py         scene catalogue per chapter
tools/bolt_edges.py          scene chains (the transition table) -> shipped TSV
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

`Brante-Save-Load/scene-texts.tsv` and `scene-edges.tsv` are extracted from the
installed game and shipped with the mod. Regenerate them after a game update with

```sh
python3 tools/localization.py     # scene-texts.tsv (+ data/localization.csv)
python3 tools/bolt_edges.py       # scene-edges.tsv (needs UnityPy)
```

`tools/bolt_*.py` / `tools/localization.py` need the game installed; the Bolt
readers need UnityPy (`pip install unitypy`, or PYTHONPATH pointing at it).
`tools/build_graph.py` is optional tooling that merges the mod's traces with the
community event sheet (`data/events.json`).

## Rules that are not obvious

- **The game's save system is BinaryFormatter** over a `[Serializable]` class
  (`SaveLoadHelper`) that holds the entire game state. The mod never writes into
  the game's `Saves/` folder itself, but it does let the *game* keep saving
  there after a rewind (as it would after loading from its own menu) — after
  copying that save folder to `Backups/<save>/` once. Never delete or rewrite
  that backup; it is the only copy of the pre-mod timeline.
- **A save's folder comes from `SlotInfo.Id`**, which contains slashes
  (`Biggie_10/23/2026103816AM`), so it is a nested path. After a mod load the
  game's slot bookkeeping can be empty, which would drop autosaves into `Saves/`
  itself; `Recorder.EnsureSlotId` (prefix on the save calls) and
  `Recorder.FixSaveFolder` (postfix on `GetSaveFolderPath`) repair that.
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
| `dev-uitest.txt` | drive the save table: open, confirm, delete, load |
| `dev-book.txt` | open the settings menu, dump its layout, then the history tree and explore two steps |
| `dev-history.txt` | log the save's scene path (`ShowedScenesList`) |
| `dev-advance.txt` | page through the scene text (experimental) |
| `dev-click.txt` | click the first visible answer (experimental) |
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
- All phases are implemented and verified in-game: the save table, the history
  tree over the whole playthrough (answers for every scene come from the game's
  localisation), and the explore mode that walks the story's scene chains and
  loads the reached scene. Remaining: the achievements check, a full playthrough
  pass (death -> restart chapter), and a design document.
