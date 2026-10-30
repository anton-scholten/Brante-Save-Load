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
- **A tree view of the whole story.** See below.

## Tree view

The **Tree view** button at the top of the Save / Load window - or the
**save / load / mod** tab on top of the book's left page, next to the year and
chapter tabs - opens the whole story as a tree, left to right, filling the whole screen like the game's own pages (the mouse
wheel scrolls it sideways). The scrollbar doubles as a progress bar: its track
is filled in red from the start of the story up to the scene you are on. The
× in the top right corner closes it.

- **Columns are scenes, boxes are answers.** The scene on screen says
  "Current" at its foot, the next one "Upcoming". Every scene this save has been
  through, in order, then everything still ahead: the rest of this chapter and
  every later one, each chapter starting at a vertical rule with its name on
  top. Answer texts come from the game's own translation table, so scenes
  played before the mod was installed show theirs too.
- **What you played is highlighted.** Played scenes sit on a tinted band under
  a dark timeline bar, your answer joined to the next scene by a dark line.
  Answers given before the mod was installed were never recorded by anyone;
  those scenes say so at the bottom of their band.
- **Hover an answer** for its full text, what it changes (stats with the game's
  own icons, relations and fates with the character's portrait) and what it
  requires: all of the listed requirements, or one of a "one of" group, green
  when the hero meets it at that point, red when not.
- **Click answers to build a path.** Each click picks that answer; the path
  follows it and the last pick carries a red dashed outline. Answers your hero
  could not take at that point are greyed out and cannot be picked. The
  **reset path** button (top right, next to the ×) puts every pick back to
  what you played. If a pick changes the hero so much that an answer you
  picked further on is no longer open to him there, that answer turns red
  and Load scene is greyed out until you pick another one.
- **Your hero's stats** fill the space under the tree's scrollbar (the tree
  takes only the height its answers need): every stat in the game, one
  column per tab of the game's book (Personality, House of Brante, Destiny,
  The Province, Occupation, The Revolt), each stat a row in the game's own style - icon, name, value, the game's
  word for the level, and its slider with the stat's scale. They show the
  hero after your last pick along the path, values above today's in green
  and below in red, and go back to today's with reset path. Hover a stat for
  the game's own pop-up: what it means and every step of its scale, the
  hero's step highlighted. A tab the game has not opened yet at that
  chapter is greyed out, with the game's own "will unlock in chapter ..."
  under its title.
- **Load scene** (top middle) takes you to the scene of your last pick along
  your path. It is greyed out while that would load exactly the scene and
  answers you have now, or while a pick conflicts; hover it to see why. The mod loads its newest save from before the first answer you
  changed and plays your picked answers itself - at ten times speed, sound off,
  a few seconds a scene - until the selected scene is on screen, where it
  stops and leaves the choice to you.

To grey answers out, the tree works out the hero at every scene of the path:
exactly as the mod saved him there where it has a save, and from your first
changed answer on by applying each picked answer's effects with the game's own
rules.

## Notes

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

### What the mod ships besides the DLL

Three tables extracted from your installed copy of the game feed the tree view:
`scene-flow.tsv` (the order in which each chapter reaches its scenes, with the
conditions on the way), `scene-texts.tsv` (every scene's title and answers) and
`scene-effects.tsv` (what each answer changes and requires). After a game
update, regenerate them with `tools/bolt_story.py`, `tools/localization.py` and
`tools/scene_effects.py`, in that order (see `docs/BRANCHES.md`).

The community event sheet in `data/events.json` (all five chapters, 175 events
with every option, requirement and outcome; raw sheet in `data/events.csv`) is
kept for reference; `tools/build_graph.py` maps it onto the game's scenes.

### Known limitations

- Load scene needs a mod save at or before the first changed answer, so it
  cannot reach back past the moment the mod was installed: the game keeps no
  earlier states.
- Upcoming scenes are the rest of your chapter in the order the game reaches
  them, on your run's track (your Lot in chapter 3, your post in chapter 4),
  then every later chapter. In a later chapter a track that is decided by
  then (the Lot, the post) is not known yet, so only what all tracks share is
  listed, and every scene with a condition says at its foot what it is
  "possible depending on": stats, relationships, characters' fates or story
  events.
  In your current chapter, conditions (objectives, stats, characters' fates)
  are checked against your save as it is now: a scene whose condition fails
  today is left out even if a later answer could still unlock it. The
  epilogue's fate pages have no answers, so the tree ends with the last choice
  of chapter 5.
- Load scene replays the path in the game itself, about 5 to 10 seconds a scene.
- A scene ahead without a pick counts as its first open answer - the one
  Load scene gives there - so every scene after it is still checked.
- Greying past a changed answer is an estimate: stat changes that happen
  between scenes are not simulated, and story objectives are taken from the
  mod's saves and carried forward unchanged past a changed answer. If the
  replay meets an answer the hero cannot take after all, it stops at that
  scene.
- Some stats (Love, Rule, Volition, Tolerance, Wrath, Control, Cruelty, Weight,
  Contention) have no description or level names in the game itself; their
  pop-up says so.
- Steam Cloud is not enabled for the game itself, so the game's saves do not sync
  between machines (see Steam Cloud above).

## Why it works this way

Sir Brante (Unity 2018.3) keeps the entire game state in one serialisable object
and exposes a public method to restore it. A "choice point" is just the scene
currently on screen. So the mod snapshots that object while a choice is shown and
feeds it back later — the restore is exact, randomness included, because the dice
have already rolled by the time a choice is visible. Details in
[docs/FINDINGS.md](docs/FINDINGS.md).

## Install

```sh
./tools/install.sh          # install (symlink; best for development)
./tools/install.sh --copy   # copy instead
```

Requires: the game, installed through Steam, and bash. The installer finds the
game in any of your Steam libraries, whether Steam is the distro package, the
Flatpak or the Snap; for any other location (or a non-Steam copy) run it as
`BRANTE_DIR=/path/to/"The Life and Suffering of Sir Brante" ./tools/install.sh`.
The mod's DLL ships built in the repo, so no .NET SDK is needed to play: when
one is installed (≥ 6) the installer rebuilds from source first, otherwise it
uses the prebuilt DLL. Runs under Proton or native Windows (for Windows without
bash, see the manual steps under Platforms). The installer places three things in the game folder — a symlink
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
