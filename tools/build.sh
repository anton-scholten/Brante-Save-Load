#!/usr/bin/env bash
#
# Compiles Brante-Save-Load.dll for The Life and Suffering of Sir Brante.
#
#   ./tools/build.sh            build the mod assembly
#   ./tools/build.sh --check    compile to a temp file only, write nothing
#
# The game is Unity 2018.3 (Mono, .NET 4.x). We compile against the game's
# OWN assemblies in <game>/..._Data/Managed (mscorlib, UnityEngine modules,
# Assembly-CSharp), so the mod always matches the installed game exactly.
# No .NET SDK project file is used; csc from any dotnet SDK (>= 6) does the
# work, so `dotnet` is the only requirement. Set DOTNET_DIR if the SDK is
# not in the default place (~/.dotnet).
#
# The game must NOT be running while you build (Proton maps the DLL).

set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="$REPO_DIR/Brante-Save-Load/Scripts"
OUT="$REPO_DIR/Brante-Save-Load/Brante-Save-Load.dll"
BUILD_DIR="${TMPDIR:-/tmp}/brante-save-load-build"

CHECK_ONLY=0
if [[ "${1:-}" == "--check" ]]; then
    CHECK_ONLY=1
fi

find_dotnet() {
    if [[ -n "${DOTNET_DIR:-}" && -x "$DOTNET_DIR/dotnet" ]]; then echo "$DOTNET_DIR/dotnet"; return; fi
    if [[ -x "$HOME/.dotnet/dotnet" ]]; then echo "$HOME/.dotnet/dotnet"; return; fi
    if command -v dotnet >/dev/null 2>&1; then command -v dotnet; return; fi
    return 1
}

if ! DOTNET="$(find_dotnet)"; then
    echo "dotnet SDK not found. Install it (https://dot.net) or set DOTNET_DIR." >&2
    exit 1
fi

# The csc compiler ships inside the SDK; no NuGet restore, no csproj.
CSC="$("$DOTNET" --list-sdks | tail -1 | sed -E 's/^([0-9.]+) \[(.*)\]$/\2\/\1\/Roslyn\/bincore\/csc.dll/')"
if [[ ! -f "$CSC" ]]; then
    echo "Could not locate csc.dll inside the dotnet SDK ($("$DOTNET" --list-sdks))." >&2
    exit 1
fi

source "$REPO_DIR/tools/steam.sh"

if ! GAME="$(find_game)"; then
    echo "Could not find the game. Set BRANTE_DIR to its install directory." >&2
    exit 1
fi

MANAGED="$GAME/The Life and Suffering of Sir Brante_Data/Managed"
HARMONY="$REPO_DIR/vendor/0Harmony.dll"

echo "Game:    $GAME"
echo "Managed: $MANAGED"

mkdir -p "$BUILD_DIR"
TMP_OUT="$BUILD_DIR/Brante-Save-Load.$$.dll"
trap 'rm -f "$TMP_OUT"' EXIT

# -nostdlib: the game's mscorlib is the framework. Referencing the game's own
# facades + module DLLs is how Unity 2018 resolves UnityEngine types.
set +e
"$DOTNET" "$CSC" -nologo -noconfig -nostdlib+ -langversion:latest \
    -target:library -out:"$TMP_OUT" \
    -r:"$MANAGED/mscorlib.dll" \
    -r:"$MANAGED/System.dll" \
    -r:"$MANAGED/System.Core.dll" \
    -r:"$MANAGED/UnityEngine.dll" \
    -r:"$MANAGED/UnityEngine.CoreModule.dll" \
    -r:"$MANAGED/UnityEngine.TextRenderingModule.dll" \
    -r:"$MANAGED/UnityEngine.UIModule.dll" \
    -r:"$MANAGED/UnityEngine.AudioModule.dll" \
    -r:"$MANAGED/UnityEngine.UI.dll" \
    -r:"$MANAGED/Unity.TextMeshPro.dll" \
    -r:"$MANAGED/Assembly-CSharp.dll" \
    -r:"$HARMONY" \
    "$SRC_DIR"/*.cs
rc=$?
set -e

if [[ $rc -ne 0 ]]; then
    echo
    echo "Build FAILED. Existing assembly (if any) left untouched." >&2
    exit $rc
fi

if [[ $CHECK_ONLY -eq 1 ]]; then
    echo "Build OK (check only; $(stat -c%s "$TMP_OUT") bytes, not installed)"
else
    mv -f "$TMP_OUT" "$OUT"
    cp -f "$HARMONY" "$REPO_DIR/Brante-Save-Load/0Harmony.dll"
    echo "Build OK -> $OUT (+ 0Harmony.dll copied alongside)"
fi
