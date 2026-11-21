#!/usr/bin/env python3
"""One image of the mod in every language the game has: the same screen,
cut into slightly tilted vertical bands, one language each (README).

The screenshots come from the game itself: tools/language_shots.sh (or
dev-pick `shots`) writes Brante-Save-Load/Shots/NN_<Language>.png, one per
language, in the order ChoiceWindow.ShotLanguages lists them. This tool only
puts them together, with ImageMagick (`magick`); no Python packages needed.

Usage:  python3 tools/language_bands.py SHOTS_DIR [OUT] [--width 1920]
                [--tilt 12] [--no-labels]
"""

import argparse
import math
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent.parent

# Each band's label: the language's own name, by the file name's language.
NAMES = {
    "English": "English",
    "Russian": "Русский",
    "German": "Deutsch",
    "FrenchFrance": "Français",
    "SpanishSpain": "Español",
    "SpanishLatinAmericas": "Español (LatAm)",
    "Italian": "Italiano",
    "PortugueseBrazil": "Português (BR)",
    "Polish": "Polski",
    "Turkish": "Türkçe",
    "Japanese": "日本語",
    "Korean": "한국어",
    "ChineseSimplified": "简体中文",
    "ChineseTraditional": "繁體中文",
}


def run(args):
    subprocess.run(args, check=True)


def size_of(path):
    out = subprocess.run(["magick", "identify", "-format", "%w %h", str(path)],
                         check=True, capture_output=True, text=True).stdout
    w, h = out.split()
    return int(w), int(h)


def label_font():
    """A font with Latin, Cyrillic and CJK letters, or None."""
    if not shutil.which("fc-match"):
        return None
    for family in ("Noto Sans CJK SC", "Noto Sans CJK JP", "Source Han Sans"):
        out = subprocess.run(["fc-match", "-f", "%{file}", family + ":style=Medium"],
                             capture_output=True, text=True).stdout.strip()
        if out and ("CJK" in out or "Han" in out):
            return out
    return None


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("shots", help="folder with NN_<Language>.png")
    ap.add_argument("out", nargs="?", default=str(ROOT / "Languages.jpg"))
    ap.add_argument("--width", type=int, default=1920, help="output width (px)")
    ap.add_argument("--tilt", type=float, default=12.0, help="band tilt from vertical (degrees)")
    ap.add_argument("--no-labels", action="store_true")
    args = ap.parse_args()

    if not shutil.which("magick"):
        print("ImageMagick 7 (`magick`) is needed.", file=sys.stderr)
        return 1
    shots = sorted(p for p in pathlib.Path(args.shots).glob("*.png") if re.match(r"\d\d_", p.name))
    if len(shots) < 2:
        print("No NN_<Language>.png screenshots in " + args.shots, file=sys.stderr)
        return 1

    w, h = size_of(shots[0])
    scale = args.width / w
    W, H = args.width, round(h * scale)
    n = len(shots)
    # boundary k runs from (top[k], 0) down to (top[k] - lean, H), leaning
    # right; the boundaries cross the labels' line at equal steps, so every
    # band is as wide there and its label sits in its middle
    lean = H * math.tan(math.radians(args.tilt))
    size = max(11, round(W / n / 10))                   # the labels' size
    label_y = H - size * 2.4
    at_label = lean * (label_y + size / 2) / H          # how far a boundary leans by then
    top = [k * W / n + at_label for k in range(n + 1)]
    bot = [x - lean for x in top]
    top[0], bot[0] = -lean * 2, -lean * 2          # the outer bands reach the edges
    top[n], bot[n] = W + lean * 2, W + lean * 2

    font = None if args.no_labels else label_font()
    with tempfile.TemporaryDirectory() as tmp:
        tmp = pathlib.Path(tmp)
        cmd = ["magick", "-size", "%dx%d" % (W, H), "xc:black"]
        for k, shot in enumerate(shots):
            poly = "polygon %.1f,0 %.1f,0 %.1f,%d %.1f,%d" % (
                top[k], top[k + 1], bot[k + 1], H, bot[k], H)
            mask = tmp / ("mask%02d.png" % k)
            run(["magick", "-size", "%dx%d" % (W, H), "xc:black", "-fill", "white",
                 "-draw", poly, str(mask)])
            cmd += ["(", str(shot), "-resize", "%dx%d!" % (W, H), str(mask),
                    "-alpha", "off", "-compose", "CopyOpacity", "-composite", ")",
                    "-compose", "over", "-composite"]
        # a thin light seam along each boundary
        for k in range(1, n):
            cmd += ["-stroke", "rgba(255,250,235,0.85)", "-strokewidth", "2",
                    "-draw", "line %.1f,0 %.1f,%d" % (top[k], bot[k], H)]
        # each band's language near the bottom, on a dark tag
        if font:
            y = label_y
            for k, shot in enumerate(shots):
                key = re.sub(r"^\d\d_", "", shot.stem)
                lbl = tmp / ("label%02d.png" % k)
                run(["magick", "-background", "none", "-font", font, "-pointsize", str(size),
                     "-fill", "rgb(255,250,235)", "label:" + NAMES.get(key, key), str(lbl)])
                lw, lh = size_of(lbl)
                # the band's middle at the label's height (the outer bands:
                # between their inner edge and the picture's edge)
                at = lambda xs: xs[0] + (xs[1] - xs[0]) * ((y + lh / 2) / H)
                left = at((top[k], bot[k])) if k > 0 else 0.0
                right = at((top[k + 1], bot[k + 1])) if k < n - 1 else float(W)
                cx = (left + right) / 2
                pad = size * 0.45
                cmd += ["-fill", "rgba(40,30,25,0.8)", "-stroke", "none", "-draw",
                        "roundrectangle %.1f,%.1f %.1f,%.1f 5,5" % (
                            cx - lw / 2 - pad, y - pad * 0.5, cx + lw / 2 + pad, y + lh + pad * 0.5),
                        str(lbl), "-geometry", "+%d+%d" % (round(cx - lw / 2), round(y)),
                        "-compose", "over", "-composite"]
        cmd += ["-quality", "86", args.out]
        run(cmd)
    print("%d languages -> %s (%dx%d)" % (n, args.out, W, H))
    return 0


if __name__ == "__main__":
    sys.exit(main())
