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
    # right. Every band is as wide (band): the first starts left of the
    # picture at its foot, the last ends right of it at its head, so the
    # outer two show a little less of themselves, none more
    lean = H * math.tan(math.radians(args.tilt))
    band = (W + lean) / n
    top = [k * band for k in range(n + 1)]
    bot = [x - lean for x in top]
    ink = "rgb(24,22,20)"                               # seams and names
    seam = max(2, round(W / 640))

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
        # a dark seam along each boundary
        for k in range(1, n):
            cmd += ["-stroke", ink, "-strokewidth", str(seam),
                    "-draw", "line %.1f,0 %.1f,%d" % (top[k], bot[k], H)]
        # each band's language written along it, leaning as it does: its
        # letters about a quarter of the band's width
        if font:
            size = round(band * 0.3)
            for k, shot in enumerate(shots):
                key = re.sub(r"^\d\d_", "", shot.stem)
                lbl = tmp / ("label%02d.png" % k)
                run(["magick", "-background", "none", "-font", font, "-pointsize", str(size),
                     "-fill", ink, "label:" + NAMES.get(key, key),
                     "-rotate", "%.2f" % -(90 - args.tilt), "+repage", str(lbl)])
                lw, lh = size_of(lbl)
                # low in the band, where the stat panel is quiet; raised or
                # lowered where the band's middle there is off the picture
                mid = (top[k] + top[k + 1]) / 2             # the band's middle at the head
                cy = H * 0.72
                lo, hi = lh / 2 + seam, H - lh / 2 - seam
                margin = lw / 2 + band * 0.1
                cx = mid - lean * cy / H
                if cx < margin:
                    cy = (mid - margin) * H / lean
                elif cx > W - margin:
                    cy = (mid - (W - margin)) * H / lean
                cy = min(max(cy, lo), hi)
                cx = mid - lean * cy / H
                cmd += [str(lbl), "-geometry", "+%d+%d" % (round(cx - lw / 2), round(cy - lh / 2)),
                        "-compose", "over", "-composite"]
        cmd += ["-quality", "86", args.out]
        run(cmd)
    print("%d languages -> %s (%dx%d)" % (n, args.out, W, H))
    return 0


if __name__ == "__main__":
    sys.exit(main())
