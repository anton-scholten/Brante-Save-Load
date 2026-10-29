"""Where the game and its Proton prefix are, for the tools in this folder.

Same lookup as tools/steam.sh (used by build.sh and install.sh): BRANTE_DIR,
else every Steam library listed by the distro, Flatpak or Snap Steam.
"""

import os
import pathlib
import re

GAME_SUBDIR = "The Life and Suffering of Sir Brante"
APP_ID = "1272160"


def steam_roots():
    home = pathlib.Path.home()
    for root in (home / ".steam/steam",
                 home / ".local/share/Steam",
                 home / ".var/app/com.valvesoftware.Steam/.local/share/Steam",
                 home / ".var/app/com.valvesoftware.Steam/data/Steam",
                 home / "snap/steam/common/.local/share/Steam",
                 home / "snap/steam/common/.steam/steam"):
        if (root / "steamapps").is_dir():
            yield root


def steam_libraries():
    """Every Steam library folder, each once; a root counts as one."""
    seen = []
    for root in steam_roots():
        libs = [root]
        vdf = root / "steamapps/libraryfolders.vdf"
        if vdf.is_file():
            text = vdf.read_text(encoding="utf-8", errors="replace")
            libs += [pathlib.Path(p) for p in re.findall(r'"path"\s+"([^"]+)"', text)]
        for lib in libs:
            if lib not in seen:
                seen.append(lib)
    return seen


def find_game(explicit=None):
    """The game's install folder: `explicit`, BRANTE_DIR, or a Steam library."""
    if explicit:
        return pathlib.Path(explicit)
    if os.environ.get("BRANTE_DIR"):
        return pathlib.Path(os.environ["BRANTE_DIR"])
    for lib in steam_libraries():
        cand = lib / "steamapps/common" / GAME_SUBDIR
        if (cand / (GAME_SUBDIR + "_Data") / "resources.assets").is_file():
            return cand
    return None


def find_runs_dir():
    """The mod's Runs folder inside the game's Proton prefix, or None."""
    for lib in steam_libraries():
        pfx = lib / "steamapps/compatdata" / APP_ID / "pfx"
        for cand in pfx.glob("drive_c/users/*/AppData/LocalLow/SEVER/"
                             + GAME_SUBDIR + "/Brante-Save-Load/Runs"):
            return cand
    return None
