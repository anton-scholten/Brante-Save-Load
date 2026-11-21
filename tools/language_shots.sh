#!/usr/bin/env bash
# The README's languages image: one screenshot of the mod in each of the
# game's 14 languages, taken by the game itself in one session, then cut
# into tilted bands by tools/language_bands.py.
#
#   tools/language_shots.sh [SAVE] [OUT]
#
# SAVE: a mod save under the prefix's Brante-Save-Load/Runs/ (default: the
# newest Manual/*.dat). The game is started through Steam, loads the newest
# autosave, then SAVE (the table's load path), and dev-pick `shots` runs the
# lines below once per language: the window rebuilt, the picks applied, the
# answer hovered, the screen saved to Brante-Save-Load/Shots/NN_<Language>.png.
# Close the game first; it is closed again at the end.
#
# The default lines make the README picture: chapter II, "Call the guards"
# picked at The Nobleman's Lot and "Support Father" at Trial of the
# Assailants, the Destiny tab, and the pop-up of "Bleed" in Last Respects.
# Put other dev-pick lines in LANG_SHOT_LINES (one per line) for another
# screen.
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=steam.sh
source "$REPO/tools/steam.sh"
GAME="$(find_game)" || { echo "Game not found; set BRANTE_DIR." >&2; exit 1; }
STEAMAPPS="$(cd "$GAME/../.." && pwd)"
PFX="$STEAMAPPS/compatdata/1272160/pfx"
MOD="$PFX/drive_c/users/steamuser/AppData/LocalLow/SEVER/The Life and Suffering of Sir Brante/Brante-Save-Load"
LOG="$GAME/Brante-Save-Load/Brante-Save-Load.log"
[[ -d "$MOD/Runs" ]] || { echo "No mod data at $MOD" >&2; exit 1; }

SAVE="${1:-}"
if [[ -z "$SAVE" ]]; then
    newest="$(ls -t "$MOD"/Runs/*/Manual/*.dat 2>/dev/null | head -1)"
    [[ -n "$newest" ]] || { echo "No manual save under $MOD/Runs" >&2; exit 1; }
    SAVE="${newest#"$MOD/Runs/"}"
fi
OUT="${2:-$REPO/Languages.jpg}"
LINES="${LANG_SHOT_LINES:-02.08.01=ButtonDescription3
02.09.01=ButtonDescription1
mode destiny
scrollto 02.09.01
hold 02.10.02_Funeral_Ded=ButtonDescription2}"

stop_game() {
    pkill -f '[S]ir Brante.exe' 2>/dev/null || true
    sleep 3
    WINEPREFIX="$PFX" wineserver -k 2>/dev/null || true
}

stop_game
mkdir -p "$MOD/Snapshots"
rm -rf "$MOD/Shots"
: > "$MOD/Snapshots/dev-autoload.txt"
printf '%s\n' "$SAVE" > "$MOD/Snapshots/dev-load.txt"
printf 'shots\n%s\n' "$LINES" > "$MOD/Snapshots/dev-pick.txt"
start=$(( $(wc -l < "$LOG" 2>/dev/null || echo 0) + 1 ))
echo "Save: $SAVE"
echo "Starting the game..."
(setsid steam steam://rungameid/1272160 >/dev/null 2>&1 &)

# about a minute to the loaded save, then ~4 s a language
for _ in $(seq 1 120); do
    if tail -n +"$start" "$LOG" 2>/dev/null | grep -q "DEV SHOTS: done"; then break; fi
    sleep 5
done
tail -n +"$start" "$LOG" | grep "DEV SHOT" || true
stop_game
n=$(ls "$MOD/Shots"/*.png 2>/dev/null | wc -l)
[[ $n -gt 1 ]] || { echo "No screenshots were written (see $LOG)." >&2; exit 1; }
python3 "$REPO/tools/language_bands.py" "$MOD/Shots" "$OUT"
