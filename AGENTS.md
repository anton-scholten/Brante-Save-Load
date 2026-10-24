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
BranteLoadAny/               the folder that gets installed into the game dir
  BranteLoadAny.dll          built by tools/build.sh; the Doorstop entry assembly
  0Harmony.dll               vendored Harmony (copied by the build from vendor/)
  Scripts/*.cs               the source
    Entrypoint.cs            Doorstop entry: creates the host GameObject
    ModLog.cs                file logger (usable before Unity is up)
    ModHost.cs               the host component; probe first, features after
    Recorder.cs              choice-point snapshot capture + dedup
    ChoiceWindow.cs          "Load any choice" pause button + list + load flow
doorstop/doorstop_config.ini the Doorstop config installed into the game folder
vendor/                      vendored third-party binaries + their licenses
  doorstop/winhttp.dll       Unity Doorstop 4.6.0 (x64), LGPL-2.1
  0Harmony.dll               Lib.Harmony 2.3.3 (net472), MIT
tools/build.sh               compile against the game's own Managed/ DLLs
tools/install.sh             build, then symlink/copy into the game folder
docs/                        FINDINGS (what the game's code does), DESIGN, TODO
```

## Build, install

```sh
./tools/build.sh             # after editing any .cs  (dotnet SDK >= 6 required)
./tools/install.sh           # build + install: symlink BranteLoadAny/, write
                             # doorstop_config.ini + winhttp.dll into the game dir
./tools/install.sh --copy    # copy instead of symlink
./tools/install.sh --uninstall
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

## Rules that are not obvious

- **The game's save system is BinaryFormatter** over a `[Serializable]` class
  (`SaveLoadHelper`) that holds the entire game state. Never write to the game's
  `Saves/` folder — the mod keeps its snapshots in its own folder (see DESIGN.md
  once it exists) precisely so the two systems cannot interfere.
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
the rebuilt `BranteLoadAny/BranteLoadAny.dll` — the repo's mod folder is meant
to be runnable without a toolchain, same as Besiege-Sand ships its DLL), then
run `./tools/install.sh`. Read `BranteLoadAny/BranteLoadAny.log` after starting
the game — that log is the mod's only diagnostic channel.

To test the mod in-game without playing to a choice point, drop marker files
into the mod's folder inside the Proton prefix
(`<persistentDataPath>/BranteLoadAny/Snapshots/`): `dev-autoload.txt` replays
the vanilla load sequence onto the newest autosave, and `dev-uitest.txt`
(alongside it) then drives the choice-list UI through every state. Kill the
game's own `…Sir Brante.exe` processes (not just the Proton wrapper) plus
`wineserver -k` after each run.

To screenshot the running game (Wayland/KDE session):
`DISPLAY=:1 xwininfo -root -tree | grep -i brante` finds the window's display,
then `spectacle -b -n -o shot.png` captures the full screen (it includes
XWayland windows). ImageMagick `import`, `xwd`, and `ffmpeg x11grab` do **not**
work here (black/`BadMatch` on the X1 root).

## Open work

- [docs/TODO.md](docs/TODO.md): what remains of the plan.
- Phases 0–5 are done and verified in-game (the save list is a sortable table;
  saves live per main save in `BranteLoadAny/Runs/<main save ID>/`, and older
  ones are migrated at startup). Phase 6 (reconstructing pre-mod choice history)
  and Phase 7 (polish) follow.
