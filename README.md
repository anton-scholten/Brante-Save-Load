# Brante — Save / Load

A mod for [The Life and Suffering of Sir Brante](https://store.steampowered.com/app/1272160/)
that lets you **return to any choice you have already reached** — no more losing an
ending to a dice roll chapters ago — and adds a **save/load system of its own** that
never touches the game's saves.

## What it does

- **Load any past choice point.** Every time a choice appears on screen, the mod
  records the exact game state. Press Esc, click **"Save / Load"** at the
  bottom of the settings menu, pick any entry from the list, confirm — and the
  game returns to that exact choice screen. Entries can also be removed from the
  list (that only deletes the mod's own save file).
- **Its own snapshots, and a backup of the game's save.** Choice points are
  recorded into the mod's own folder as you reach them. Before the mod loads one
  of its saves for the first time it copies the game's own save folder to
  `Brante-Save-Load/Backups/<save>/` (with a `README.txt` saying how to put it
  back). After that the game saves normally again, exactly as it would after
  loading an older save from its own menu — so autosaves keep working, and a
  chapter restart after a death continues *your* timeline rather than an
  abandoned one.
- **Your own saves.** The same window lists the saves of the **current main save
  only** in one table: automatic choice points and manual saves (**"Save now"**,
  shown in a yellow row colour). Click a column header to sort by it, click again
  to reverse; the table scrolls and the × on the right removes a row. A choice
  name too long for its column rests with an ellipsis and then scrolls (marquee)
  until its end is shown. Saves are kept
  per main save in `Brante-Save-Load/Runs/<main save ID>/`, separate from the game's
  saves: using them never changes your game saves, and the game's saves never
  change them. Saves from older versions of the mod are moved into the new
  per-main-save folders automatically on the next start.
- **In your language.** The mod's own window follows the game language and is
  translated into all eleven languages the game ships (English, Russian, German,
  French, Spanish, Italian, Polish, Turkish, Japanese, Korean, Simplified
  Chinese): switch the language in the settings and the window rebuilds.
- **Iron Man included.** The game disables its own save/load menu in Iron Man;
  the mod's window keeps working there, so a permadeath run can still be rewound.
- **Playthrough history, as a tree.** The game records every scene the current
  save has passed through. **History** draws that path left to right: one column
  per scene, the answers offered there stacked underneath, and the answer you
  picked joined by a darker line. The answers come from the game's own
  translation table, so this works for scenes played before the mod too;
  hovering shows the full text, and columns the mod has a save for can be
  clicked to load them. **Explore** turns the tree into a path builder: click any
  answer to move to the scene it leads to (the game's own scene chain is shipped
  with the mod), keep choosing to walk forward, and "Load this scene" jumps to
  the place you reached once it is one the mod has a save for. Decisions made before the mod was
  installed can be shown but not reopened — the game keeps no earlier states, and
  only records the most recent answer. A dataset of the game's events — all five
  chapters, 175 events with every option, requirement and outcome — lives in
  `data/events.json` (raw sheet in `data/events.csv`, see `docs/BRANCHES.md`);
  it is the base for showing the full tree later; `tools/build_graph.py` merges
  it with the mod's traces to map scenes to events and to record which choice
  leads where (`data/graph.json`).

### Platforms

The game itself ships a Windows build, so on Linux it runs through Proton and
the mod works inside the same Windows environment (paths are resolved by Unity
and by `Path.Combine`, never hard-coded, and file names are sanitised with the
union of Windows and Unix rules so a save maps to the same folder everywhere).
The bundled Doorstop is the Windows build (`winhttp.dll`); native Linux or macOS
builds of the game would need that platform's Doorstop binary, which is not
included — under Steam Play nothing else is required.

To install on Windows without the bash installer, copy three things into the
game folder (next to `The Life and Suffering of Sir Brante.exe`):

1. the `Brante-Save-Load/` folder from this repo,
2. `doorstop/doorstop_config.ini`,
3. `vendor/doorstop/winhttp.dll`.

Then start the game and check `Brante-Save-Load/Brante-Save-Load.log`.

### What uninstall does

`./tools/install.sh --uninstall` removes only what the mod put into the game
folder (the folder/link, `doorstop_config.ini`, `winhttp.dll`) and the Proton
Wine override. It does **not** touch your saves: the game's own saves stay in
`Saves/`, and the mod's saves plus the pre-mod backup copies stay in
`Brante-Save-Load/` (the command prints the exact path).

Two optional flags change that, and both are explicit on purpose:

- `--restore-backup` copies each `Backups/<save>/` back into the game's `Saves/`
  folder, moving the current (rewound) save to `Brante-Save-Load/PreRestore/`
  first, so neither version is lost.
- `--purge` deletes the mod's whole data folder (choice points, manual saves,
  backups). Use it after `--restore-backup` to get a clean uninstall.

### Steam Cloud

The game's saves do **not** currently reach Steam Cloud. Steam's own cloud log
reports `AutoCloud is disabled` for the game, it allocates no cloud storage for it
(no `userdata/<id>/1272160` folder; cloud change number stays 0), so nothing is
uploaded or downloaded.

The save location itself is correct: the game's Auto-Cloud path in Steam's app
cache is `AppData\LocalLow\SEVER\The Life and Suffering of Sir Brante\Saves`
with pattern `*.dat`, which is exactly where the game writes. Turning the sync on
is a Steamworks setting of the game, which only the developer can change. If they
do, the path must also be marked *recursive*: the game keeps each playthrough in a
subfolder (`Saves/<Hero>_<date>/`), which a non-recursive `*.dat` pattern would
miss.

Until Steam syncs them, copy the `Saves` folder between computers by hand. The
mod's own saves (in `Brante-Save-Load/`) are not meant to be synced.

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
to `Brante-Save-Load/`, `doorstop_config.ini` and `winhttp.dll` (Doorstop, the same
bootstrap BepInEx uses, without BepInEx) — plus, under Proton, a per-app Wine
`DllOverrides` entry in the game's compat-data prefix so the proxy DLL is picked
up automatically. `./tools/install.sh --uninstall` removes exactly those,
including the Wine override in the prefix. The game also survives the mod
being half-removed: with `doorstop_config.ini`/`winhttp.dll` left behind but
`Brante-Save-Load/` gone, Doorstop fails silently and the game runs unmodded
(verified).

### Proton (Steam Play)

Nothing to do: the installer registers the Wine override in the game's Proton
prefix, so the mod starts with the game. If that step reported *no prefix found*
(or the override did not stick), fall back to setting the game's launch option
in Steam:

```
WINEDLLOVERRIDES=winhttp=n,b %command%
```

You can check it worked by looking at `Brante-Save-Load/Brante-Save-Load.log` in the game
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
