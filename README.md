# Brante — Load Any Choice

A mod for [The Life and Suffering of Sir Brante](https://store.steampowered.com/app/1272160/)
that lets you **return to any choice you have already reached** — no more losing an
ending to a dice roll chapters ago — and adds a **save/load system of its own** that
never touches the game's saves.

## What it does

- **Load any past choice point.** Every time a choice appears on screen, the mod
  records the exact game state. Open the mod's list from the settings/pause window,
  click any entry, and the game returns to that exact choice screen.
- **Its own save/load.** The mod's snapshots live in their own folder, written and
  read by the mod's own code. The game's saves and its Load window keep working,
  unchanged; the two systems cannot interfere with each other.
- **Existing saves.** (Planned, see docs/TODO.md) reconstruct the list of choices
  you took in a playthrough started before the mod was installed.

## Why it works this way

Sir Brante (Unity 2018.3) keeps the entire game state in one serialisable object
and exposes a public method to restore it. A "choice point" is just the scene
currently on screen. So the mod snapshots that object while a choice is shown and
feeds it back later — the restore is exact, randomness included, because the dice
have already rolled by the time a choice is visible. Details in
[docs/FINDINGS.md](docs/FINDINGS.md).

## Install

```sh
./tools/install.sh          # build + install (symlink; best for development)
./tools/install.sh --copy   # copy instead
```

Requires: the game (Steam), a .NET SDK (≥ 6) for building. Runs under Proton or
native Windows. The installer places three things in the game folder — a symlink
to `BranteLoadAny/`, `doorstop_config.ini` and `winhttp.dll` (Doorstop, the same
bootstrap BepInEx uses, without BepInEx) — and
`./tools/install.sh --uninstall` removes exactly those.

### Proton (Steam Play) — one extra step

Under Proton, Wine must be told to use the bundled `winhttp.dll` instead of its own,
or Doorstop never starts (the game then simply runs unmodded). Set this as the
game's launch option in Steam:

```
WINEDLLOVERRIDES=winhttp=n,b %command%
```

You can check it worked by looking at `BranteLoadAny/BranteLoadAny.log` in the game
folder after starting the game — it should show the mod's first lines within
seconds.

## Building

```sh
./tools/build.sh        # compiles against the game's own Managed/ assemblies
```

No .NET project files, no NuGet: the build calls the compiler inside your dotnet
SDK directly and references the game's own DLLs, so the mod always matches the
installed game.

## Licence

GPL-3.0 (see LICENSE). Bundled: Unity Doorstop 4.6.0 (LGPL-2.1,
vendor/doorstop/LICENSE), Lib.Harmony 2.3.3 (MIT, vendor/LICENSE-harmony.txt).
The game is Sever/101XP's; nothing of theirs is redistributed here.
