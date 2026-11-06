# Brante - Save / Load

<img src="Thumbnail.png" alt="thumbnail" width="200" align="right">

A mod for [The Life and Suffering of Sir Brante](https://store.steampowered.com/app/1272160/): save and load anywhere, go back to any choice you have made, and see your whole playthrough as a tree.

<br clear="right">

![The game's main UI (a book) with the mod's two tabs on top of its left page, right of the year and chapter tabs: a medallion with a download and upload arrows (illustrating saving and loading) and the other with linked boxes (illustrating the tree view).](Screenshot_1_annotated.jpg)

## Install

```sh
./tools/install.sh              # finds the game in your Steam libraries
```

Set `BRANTE_DIR` if the game isn't found. No .NET SDK needed: the built DLL is in the repo.
On Windows without bash, copy `Brante-Save-Load/`, `doorstop/doorstop_config.ini` and `vendor/doorstop/winhttp.dll` next to the game's `.exe`.
If the mod doesn't start under Proton, set the Steam launch option to `WINEDLLOVERRIDES=winhttp=n,b %command%`.

## Use

Two tabs appear on top of the book: **save / load** (the arrows) and **tree view** (the linked boxes).

### Save / Load

<p>
<img src="Screenshot_2a.jpg" width="49%" alt="The save / load screen: a table of saves (game autosaves and snapshots taken before each load), with the save type, date saved, chapter, age, year, season, turn, scene and choice of each, and a button to delete the save.">
<img src="Screenshot_2b.jpg" width="49%" alt="The same screen showing the confirmation dialogue after clicking a save to load it. The clicked row stays bright with a red dashed outline while the rest is dimmed.">
</p>

- Click a save to load it; the bin deletes it. **Save now** adds a save of your own.
- **Save type** (first header) filters: game autosaves, saves made before each load, your own saves, and the backup of your original save. Other headers sort.
- Every load first saves where you were, so any load can be undone.

### Tree view

![The tree view: scenes are shown as columns of available choices, the player's path is shown in red and yellow highlighted choices, a picked choice is shown by a red dashed outline and can be loaded using the "load scene" button at the top, and "Pick an answer here to go on" marks the next scene to pick in. Hovering a choice shows what it changes and requires; the stats it changes are marked in the stat rows at the bottom, green for a rise and red for a fall. Stats not yet unlocked remain greyed out.](Screenshot_3.jpg)

- Columns are scenes, boxes are choices; your path is yellow and red. Scenes after the one you're on are predicted.
- **Hover** a choice to see what it changes and requires; the stats it changes light up below (green up, red down).
- **Click** choices to build a new path. Click again to undo; the arrow by the × resets all. Greyed choices can't be taken; red ones conflict with an earlier pick.
- **Load scene** plays your path by itself (at 40× speed) up to the dashed choice. Every scene before it needs a choice; "Pick an answer here to go on" shows which one is missing.

## Good to know

- Your original save is backed up once to `Brante-Save-Load/Backups/`; the game's own saves keep working as normal.
- Achievements stay on. Works in Iron Man. In all eleven of the game's languages.
- Fixes a game bug: the CONTINUE button on the true-death pages no longer gets stuck before the ending.
- The predicted scenes are close but not exact (a few stat changes aren't in the game's tables). If Load scene meets a scene it didn't expect, it stops there so you can pick and load again.
- Load scene takes about 2-3 s per scene.

## Uninstall

```sh
./tools/install.sh --uninstall                    # keeps all saves
./tools/install.sh --uninstall --restore-backup   # also puts your original save back
./tools/install.sh --uninstall --purge            # also deletes the mod's saves
```

## More

[docs/FINDINGS.md](docs/FINDINGS.md) explains how the game and the mod work, [MODDING.md](MODDING.md) is a compact how-to, and [AGENTS.md](AGENTS.md) covers working on the mod (build with `./tools/build.sh`).

## Licence

GPL-3.0 (see LICENSE). Bundled: Unity Doorstop 4.6.0 (LGPL-2.1, vendor/doorstop/LICENSE), Lib.Harmony 2.3.3 (MIT, vendor/LICENSE-harmony.txt).
The game is Sever/101XP's; none of its files are in this repo. The thumbnail and screenshots show its art to illustrate the mod.
