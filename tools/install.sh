#!/usr/bin/env bash
#
# Installs the mod into the game folder.
#
#   ./tools/install.sh              build, then symlink (best for development:
#                                   a rebuild is picked up by the next game
#                                   start, no reinstall)
#   ./tools/install.sh --copy       copy instead (for handing someone a folder)
#   ./tools/install.sh --uninstall  remove everything again
#   ./tools/install.sh --no-build   skip the build step
#
# What gets installed into the game folder:
#   BranteLoadAny        -> symlink/copy of the repo's BranteLoadAny/
#   doorstop_config.ini  -> from repo doorstop/ (edited once, then yours)
#   winhttp.dll          -> Doorstop's proxy DLL (from vendor/doorstop/)
# None of the game's own files are modified; --uninstall removes only these.
#
# Set BRANTE_DIR if the game is not auto-detected.

set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MOD_NAME="BranteLoadAny"
SRC="$REPO_DIR/$MOD_NAME"

MODE="link"
BUILD=1
for arg in "$@"; do
    case "$arg" in
        --uninstall) MODE="uninstall"; BUILD=0 ;;
        --copy)      MODE="copy" ;;
        --no-build)  BUILD=0 ;;
        *) echo "Unknown option: $arg" >&2; exit 1 ;;
    esac
done

find_game() {
    if [[ -n "${BRANTE_DIR:-}" ]]; then echo "$BRANTE_DIR"; return; fi
    local candidates=()
    local vdf lib
    for vdf in "$HOME/.steam/steam/steamapps/libraryfolders.vdf" \
               "$HOME/.local/share/Steam/steamapps/libraryfolders.vdf"; do
        [[ -f "$vdf" ]] || continue
        while read -r lib; do candidates+=("$lib/steamapps/common"); done \
            < <(grep -oE '"path"[[:space:]]+"[^"]+"' "$vdf" | sed -E 's/.*"([^"]+)"$/\1/')
    done
    candidates+=("$HOME/.steam/steam/steamapps/common" "$HOME/.local/share/Steam/steamapps/common")
    local dir
    for dir in "${candidates[@]}"; do
        [[ -f "$dir/The Life and Suffering of Sir Brante/The Life and Suffering of Sir Brante_Data/globalgamemanagers" ]] \
            && { echo "$dir/The Life and Suffering of Sir Brante"; return; }
    done
    return 1
}

if ! GAME="$(find_game)"; then
    echo "Could not find the game. Set BRANTE_DIR to its install directory." >&2
    exit 1
fi

DEST="$GAME/$MOD_NAME"
DOORSTOP_INI="$GAME/doorstop_config.ini"
WINHTTP="$GAME/winhttp.dll"

if [[ "$MODE" == "uninstall" ]]; then
    for path in "$DEST" "$DOORSTOP_INI" "$WINHTTP"; do
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
    echo "Uninstalled. The game's own files were never touched."
    exit 0
fi

if [[ $BUILD -eq 1 ]]; then
    BRANTE_DIR="$GAME" "$REPO_DIR/tools/build.sh"
    echo
fi

if [[ ! -f "$SRC/$MOD_NAME.dll" ]]; then
    echo "$MOD_NAME/$MOD_NAME.dll is missing; run ./tools/build.sh first." >&2
    exit 1
fi

if pgrep -f "The Life and Suffering of Sir Brante.exe" >/dev/null 2>&1; then
    echo "The game appears to be running; close it before installing." >&2
    exit 1
fi

# Install the Doorstop bootstrap. Steam verifies game files, so these three
# entries are exactly what a 'verify integrity' pass would not expect - the
# uninstaller (or Steam's verify) removes them cleanly.
if [[ "$MODE" == "copy" ]]; then
    rm -rf "$DEST"
    mkdir -p "$DEST"
    cp -f "$SRC/$MOD_NAME.dll" "$SRC/0Harmony.dll" "$DEST/"
    [[ -f "$SRC/BranteLoadAny.log" ]] && rm -f "$DEST/BranteLoadAny.log"
    echo "Copied mod to $DEST"
else
    [[ -L "$DEST" ]] && rm "$DEST"
    [[ -d "$DEST" ]] && rm -rf "$DEST"
    ln -s "$SRC" "$DEST"
    echo "Linked $DEST -> $SRC"
fi

cp -f "$REPO_DIR/doorstop/doorstop_config.ini" "$DOORSTOP_INI"
cp -f "$REPO_DIR/vendor/doorstop/winhttp.dll" "$WINHTTP"
echo "Installed doorstop_config.ini and winhttp.dll"

cat <<'EOF2'

Done. Next:
  1. Proton/Steam Play only: set the game's launch option so Wine loads
     the bundled Doorstop proxy instead of its builtin winhttp:
       WINEDLLOVERRIDES=winhttp=n,b %command%
     (Without it the game runs unmodded - no error, just no mod.)
  2. Start the game.
  3. Read the probe log:
       cat BranteLoadAny/BranteLoadAny.log
     (in the game folder; with a symlink it is in the repo)

Note: Doorstop only injects C# code. It does not modify game logic by itself.
EOF2
