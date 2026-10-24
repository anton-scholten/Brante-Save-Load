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
- **Playthrough history.** The game records every scene the current save has
  passed through. **History** in the same window lists that path in order,
  including the part played before the mod was installed, and marks the scenes
  the mod already has a save for. It is read-only: the game's save records which
  scenes were shown, not which answer was clicked, so earlier choice points
  cannot be restored from it.

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
