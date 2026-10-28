# Steam library discovery, sourced by build.sh and install.sh.
#
# Steam keeps its list of library folders in steamapps/libraryfolders.vdf
# under its own root, and that root depends on how Steam was installed:
# the distro package (~/.steam/steam, ~/.local/share/Steam), Flatpak
# (~/.var/app/com.valvesoftware.Steam/...) or Snap (~/snap/steam/common/...).

GAME_SUBDIR="The Life and Suffering of Sir Brante"

steam_roots() {
    local r
    for r in "$HOME/.steam/steam" \
             "$HOME/.local/share/Steam" \
             "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam" \
             "$HOME/.var/app/com.valvesoftware.Steam/data/Steam" \
             "$HOME/snap/steam/common/.local/share/Steam" \
             "$HOME/snap/steam/common/.steam/steam"; do
        [[ -d "$r/steamapps" ]] && echo "$r"
    done
}

# Every Steam library folder (one per line, duplicates removed). A Steam root
# is a library itself even when its libraryfolders.vdf is missing.
steam_libraries() {
    local root vdf
    while read -r root; do
        echo "$root"
        vdf="$root/steamapps/libraryfolders.vdf"
        [[ -f "$vdf" ]] || continue
        grep -oE '"path"[[:space:]]+"[^"]+"' "$vdf" | sed -E 's/.*"([^"]+)"$/\1/'
    done < <(steam_roots) | awk '!seen[$0]++'
}

# The game's install directory: BRANTE_DIR, else the first Steam library
# holding the game. Fails when neither applies.
find_game() {
    if [[ -n "${BRANTE_DIR:-}" ]]; then echo "$BRANTE_DIR"; return; fi
    local lib dir
    while read -r lib; do
        dir="$lib/steamapps/common/$GAME_SUBDIR"
        [[ -f "$dir/${GAME_SUBDIR}_Data/Managed/Assembly-CSharp.dll" ]] \
            && { echo "$dir"; return; }
    done < <(steam_libraries)
    return 1
}
