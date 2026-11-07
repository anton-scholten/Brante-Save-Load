#!/usr/bin/env bash
#
# Installs the mod into the game folder.
#
#   ./tools/install.sh              build, then symlink (best for development:
#                                   a rebuild is picked up by the next game
#                                   start, no reinstall). Without a .NET SDK
#                                   the prebuilt DLL from the repo is used.
#   ./tools/install.sh --copy       copy instead (for handing someone a folder)
#   ./tools/install.sh --uninstall  remove everything again
#   ./tools/install.sh --no-build   skip the build step
#
# What gets installed:
#   game folder:
#     Brante-Save-Load     -> symlink/copy of the repo's Brante-Save-Load/
#     doorstop_config.ini  -> from repo doorstop/ (edited once, then yours)
#     winhttp.dll          -> Doorstop's proxy DLL (from vendor/doorstop/)
#   Proton prefix (if the game has one):
#     user.reg             -> per-app Wine DllOverrides entry so the Doorstop
#                             proxy loads without any launch option
# None of the game's own files are modified; --uninstall removes only these.
#
# The game is found through Steam's library list (distro, Flatpak or Snap
# Steam; see tools/steam.sh). Set BRANTE_DIR if it is not auto-detected.

set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MOD_NAME="Brante-Save-Load"
SRC="$REPO_DIR/$MOD_NAME"

MODE="link"
BUILD=1
RESTORE=0
PURGE=0
for arg in "$@"; do
    case "$arg" in
        --uninstall)      MODE="uninstall"; BUILD=0 ;;
        --copy)           MODE="copy" ;;
        --no-build)       BUILD=0 ;;
        --restore-backup) RESTORE=1 ;;
        --purge)          PURGE=1 ;;
        *) echo "Unknown option: $arg" >&2; exit 1 ;;
    esac
done
if [[ $RESTORE -eq 1 || $PURGE -eq 1 ]] && [[ "$MODE" != "uninstall" ]]; then
    echo "--restore-backup and --purge only apply to --uninstall." >&2
    exit 1
fi

source "$REPO_DIR/tools/steam.sh"

if ! GAME="$(find_game)"; then
    echo "Could not find the game. Set BRANTE_DIR to its install directory." >&2
    exit 1
fi

# Steam libraries, for finding the game's Proton prefix (compat data may live
# in any library, not necessarily the one holding the game files).
mapfile -t LIBS < <(steam_libraries)

# --- Proton/Wine: load Doorstop's proxy without a launch option --------------
# Wine prefers its builtin winhttp.dll over the native proxy in the game dir,
# which would silently skip Doorstop. A per-app DllOverrides entry in the
# game's Proton prefix does exactly what the launch option
# WINEDLLOVERRIDES=winhttp=n,b would, but persistently and per game only.
# user.reg is plain text; Wine re-reads it when the prefix starts, so editing
# it is only meaningful while nothing is running (guaranteed further below).
WINE_SECTION='[Software\\Wine\\AppDefaults\\The Life and Suffering of Sir Brante.exe\\DllOverrides]'
WINE_VALUE='"winhttp"="native,builtin"'

wine_prefixes() {
    local lib p
    for lib in "$(cd "$(dirname "$GAME")/.." && pwd)" "${LIBS[@]}"; do
        p="$lib/compatdata/1272160/pfx"
        [[ -f "$p/user.reg" ]] && echo "$p"
    done | awk '!seen[$0]++'
}

wine_override_add() {
    local p
    while read -r p; do
        if grep -qF "$WINE_SECTION" "$p/user.reg"; then
            echo "Wine override already present ($p)"
            return 0
        fi
        printf '\n%s\n%s\n' "$WINE_SECTION" "$WINE_VALUE" >> "$p/user.reg"
        echo "Added Wine override to $p (winhttp=native,builtin):"
        echo "the game now starts modded without any launch option."
        if pgrep -x wineserver >/dev/null 2>&1; then
            echo "Note: a wineserver is still running. If the override does not"
            echo "stick (check Brante-Save-Load/Brante-Save-Load.log after one run),"
            echo "close everything Proton and re-run ./tools/install.sh --no-build."
        fi
        return 0
    done < <(wine_prefixes)
    echo "No Proton prefix with user.reg found (game not run under Steam Play yet?)."
    echo "Fallback: set the launch option  WINEDLLOVERRIDES=winhttp=n,b %command%"
    return 0
}

# Is the game actually running? Matches real game processes (Wine's exe,
# Steam's helper, Proton's python) but skips anything in our own parent
# chain: a caller - test harness, wrapper script - may quote the exe name
# in its own command line, which must not count as the game running.
game_running() {
    local pid ppid chain=" $$ "
    ppid="$PPID"
    while [[ -n "$ppid" && "$ppid" -gt 1 ]]; do
        chain="$chain$ppid "
        ppid="$(awk '/^PPid:/{print $2}' "/proc/$ppid/status" 2>/dev/null)"
    done
    for pid in $(pgrep -f "Sir Brante\.exe" 2>/dev/null); do
        case "$chain" in *" $pid "*) continue;; esac
        return 0
    done
    return 1
}

wine_override_del() {
    local p
    while read -r p; do
        grep -qF "$WINE_SECTION" "$p/user.reg" || continue
        # Prefix match on the header: once wineserver has rewritten user.reg,
        # the section header carries a key-count suffix ("...] 12345"), so an
        # exact line match would silently skip the removal. The header is
        # passed via ENVIRON because awk -v would interpret the backslashes.
        HDR="$WINE_SECTION" awk '
            index($0, ENVIRON["HDR"]) == 1 { skip = 1; next }
            skip && /^\[/ { skip = 0 }
            skip { next }
            { print }
        ' "$p/user.reg" > "$p/user.reg.tmp" && mv "$p/user.reg.tmp" "$p/user.reg"
        if grep -qF "$WINE_SECTION" "$p/user.reg"; then
            echo "FAILED to remove the Wine override from $p" >&2
            return 1
        fi
        echo "Removed Wine override from $p"
        return 0
    done < <(wine_prefixes)
}

# The mod's data folder (saves + backups) lives in the game's per-user data
# directory, inside the Proton prefix on Linux. One per prefix that has run the
# game. The old name is still checked so pre-rename data is found.
data_dirs() {
    local p d
    while read -r p; do
        for d in "$p"/drive_c/users/*/AppData/LocalLow/"SEVER/The Life and Suffering of Sir Brante"/Brante-Save-Load \
                 "$p"/drive_c/users/*/AppData/LocalLow/"SEVER/The Life and Suffering of Sir Brante"/BranteLoadAny; do
            [[ -d "$d" ]] && echo "$d"
        done
    done < <(wine_prefixes) | awk '!seen[$0]++'
}

# Puts the copies taken before the first rewind back into the game's Saves/.
# The current (rewound) save is moved to Brante-Save-Load/PreRestore/ first, so
# nothing is lost either way.
restore_backups() {
    local data pd saves backups b id rel r f target stamp moved
    while read -r data; do
        pd="$(dirname "$data")"
        saves="$pd/Saves"
        backups="$data/Backups"
        [[ -d "$backups" && -d "$saves" ]] || continue
        for b in "$backups"/*/; do
            [[ -d "$b" ]] || continue
            id="$(basename "$b")"
            rel=""
            while read -r f; do
                r="${f#"$saves"/}"
                r="${r%/Autosave.dat}"
                if [[ "${r//\//_}" == "$id" ]]; then rel="$r"; break; fi
            done < <(find "$saves" -name Autosave.dat 2>/dev/null)
            [[ -n "$rel" ]] || rel="$id"   # the save folder is gone; recreate it
            target="$saves/$rel"
            stamp="$(date +%Y%m%d_%H%M%S)"
            if [[ -d "$target" ]]; then
                moved="$data/PreRestore/$(echo "$rel" | tr '/' '_')-$stamp"
                mkdir -p "$data/PreRestore"
                mv "$target" "$moved"
                echo "Moved the current save out of the way: $moved"
            fi
            mkdir -p "$target"
            find "$b" -maxdepth 1 -type f ! -name 'README.txt' -exec cp -f {} "$target/" \;
            echo "Restored the pre-mod save into $target"
        done
    done < <(data_dirs)
}

purge_data() {
    local data
    while read -r data; do
        rm -rf "$data"
        echo "Deleted the mod's data (saves and backups): $data"
    done < <(data_dirs)
}

DEST="$GAME/$MOD_NAME"
LEGACY="$GAME/BranteLoadAny"   # the mod's old name, cleaned up if left behind
DOORSTOP_INI="$GAME/doorstop_config.ini"
WINHTTP="$GAME/winhttp.dll"

if [[ "$MODE" == "uninstall" ]]; then
    if [[ $RESTORE -eq 1 ]] && game_running; then
        echo "The game appears to be running; close it before restoring a backup." >&2
        exit 1
    fi
    if [[ $RESTORE -eq 1 ]]; then
        restore_backups
    fi
    for path in "$DEST" "$LEGACY" "$DOORSTOP_INI" "$WINHTTP"; do
        if [[ -L "$path" ]]; then
            rm "$path"; echo "Removed symlink $path"
        elif [[ -f "$path" ]]; then
            rm -f "$path"; echo "Removed $path"
        elif [[ -d "$path" ]]; then
            rm -rf "$path"; echo "Removed $path"
        else
            echo "Nothing at $path"
        fi
    done
    if game_running; then
        echo "WARNING: the game is running. The Wine override edit in user.reg"
        echo "may be overwritten when Wine exits - re-run --uninstall after"
        echo "closing the game if the override reappears."
    fi
    wine_override_del
    if [[ $PURGE -eq 1 ]]; then
        purge_data
    fi
    echo "Uninstalled. The game's own files were never touched."
    if [[ $PURGE -eq 1 ]]; then
        echo "The mod's saves and backups were deleted (--purge)."
    else
        while read -r data; do
            echo "The mod's own data is still here: $data"
            echo "  (choice points and manual saves in Runs/, pre-mod copies in Backups/)"
        done < <(data_dirs)
        echo "Restore the pre-mod save and remove the mod:  ./tools/install.sh --uninstall --restore-backup --purge"
        echo "Just delete the mod's own saves:              ./tools/install.sh --uninstall --purge"
    fi
    exit 0
fi

# The repo ships a built DLL, so players need no SDK: without one, install
# that. (Same lookup as build.sh's find_dotnet.)
have_dotnet() {
    [[ -n "${DOTNET_DIR:-}" && -x "$DOTNET_DIR/dotnet" ]] && return 0
    [[ -x "$HOME/.dotnet/dotnet" ]] && return 0
    command -v dotnet >/dev/null 2>&1
}
if [[ $BUILD -eq 1 ]] && ! have_dotnet && [[ -f "$SRC/$MOD_NAME.dll" ]]; then
    echo "No .NET SDK found: installing the prebuilt $MOD_NAME.dll shipped with the repo."
    echo "(Only needed to rebuild after changing the source; see AGENTS.md.)"
    echo
    BUILD=0
fi

if [[ $BUILD -eq 1 ]]; then
    BRANTE_DIR="$GAME" "$REPO_DIR/tools/build.sh"
    echo
fi

if [[ ! -f "$SRC/$MOD_NAME.dll" ]]; then
    echo "$MOD_NAME/$MOD_NAME.dll is missing; run ./tools/build.sh first." >&2
    exit 1
fi

if game_running; then
    echo "The game appears to be running; close it before installing." >&2
    exit 1
fi

# Install the Doorstop bootstrap. Steam verifies game files, so these three
# entries are exactly what a 'verify integrity' pass would not expect - the
# uninstaller (or Steam's verify) removes them cleanly.
# A link from the mod's old name would keep the game loading the old DLL.
[[ -L "$LEGACY" ]] && rm "$LEGACY" && echo "Removed the old $LEGACY link"

if [[ "$MODE" == "copy" ]]; then
    rm -rf "$DEST"
    mkdir -p "$DEST"
    # the DLLs and the game data the mod reads next to its DLL (scene
    # texts, flow, effects, params, objectives); not the source or the log
    cp -f "$SRC/$MOD_NAME.dll" "$SRC/0Harmony.dll" "$SRC"/*.tsv "$DEST/"
    echo "Copied mod to $DEST ($(ls "$DEST" | wc -l) files)"
else
    [[ -L "$DEST" ]] && rm "$DEST"
    [[ -d "$DEST" ]] && rm -rf "$DEST"
    ln -s "$SRC" "$DEST"
    echo "Linked $DEST -> $SRC"
fi

cp -f "$REPO_DIR/doorstop/doorstop_config.ini" "$DOORSTOP_INI"
cp -f "$REPO_DIR/vendor/doorstop/winhttp.dll" "$WINHTTP"
echo "Installed doorstop_config.ini and winhttp.dll"

wine_override_add

cat <<'EOF2'

Done. Next:
  1. Start the game. Under Proton the installer has already registered the
     Wine override, so no launch option is needed (if that step reported no
     prefix, use:  WINEDLLOVERRIDES=winhttp=n,b %command%).
  2. Read the probe log:
       cat Brante-Save-Load/Brante-Save-Load.log
     (in the game folder; with a symlink it is in the repo)

Note: Doorstop only injects C# code. It does not modify game logic by itself.
EOF2
