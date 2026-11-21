#!/usr/bin/env python3
"""One image of the mod in every language the game has: the same screen,
cut into slightly tilted vertical bands, one language each (README).

The screenshots come from the game itself: tools/language_shots.sh (or
dev-pick `shots`) writes Brante-Save-Load/Shots/NN_<Language>.png, one per
language, in the order ChoiceWindow.ShotLanguages lists them. This tool only
puts them together, with ImageMagick (`magick`); no Python packages needed.

Usage:  python3 tools/language_bands.py SHOTS_DIR [OUT] [--width 1920]
                [--tilt 12] [--no-flags]
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

# Each band's flag (an emoji flag, drawn from Noto Color Emoji through
# ImageMagick's Pango), by the file name's language. Latin American
# Spanish shows Mexico's, Traditional Chinese Taiwan's.
FLAGS = {
    "English": "\U0001F1EC\U0001F1E7",
    "Russian": "\U0001F1F7\U0001F1FA",
    "German": "\U0001F1E9\U0001F1EA",
    "FrenchFrance": "\U0001F1EB\U0001F1F7",
    "SpanishSpain": "\U0001F1EA\U0001F1F8",
    "SpanishLatinAmericas": "\U0001F1F2\U0001F1FD",
    "Italian": "\U0001F1EE\U0001F1F9",
    "PortugueseBrazil": "\U0001F1E7\U0001F1F7",
    "Polish": "\U0001F1F5\U0001F1F1",
    "Turkish": "\U0001F1F9\U0001F1F7",
    "Japanese": "\U0001F1EF\U0001F1F5",
    "Korean": "\U0001F1F0\U0001F1F7",
    "ChineseSimplified": "\U0001F1E8\U0001F1F3",
    "ChineseTraditional": "\U0001F1F9\U0001F1FC",
}


# The name under each flag: the language's own name.
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


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("shots", help="folder with NN_<Language>.png")
    ap.add_argument("out", nargs="?", default=str(ROOT / "Languages.jpg"))
    ap.add_argument("--width", type=int, default=1920, help="output width (px)")
    ap.add_argument("--tilt", type=float, default=12.0, help="band tilt from vertical (degrees)")
    ap.add_argument("--no-flags", action="store_true")
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
    ink = "rgba(24,22,20,0.2)"                          # the seams, faint
    seam = max(2, round(W / 640))
    inset = 50 * W / 1920                               # the seams stop this far from the edges

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
        # a faint seam along each boundary, inside a margin of the picture
        for k in range(1, n):
            x = lambda y: top[k] - lean * y / H
            y0, y1 = inset, H - inset
            # where the line leaves the margin at the sides: x(y) = W - inset, inset
            y0 = max(y0, (top[k] - (W - inset)) * H / lean)
            y1 = min(y1, (top[k] - inset) * H / lean)
            if y1 - y0 > seam:
                cmd += ["-stroke", ink, "-strokewidth", str(seam),
                        "-draw", "line %.1f,%.1f %.1f,%.1f" % (x(y0), y0, x(y1), y1)]
        # each band's flag where its middle line meets the diagonal from
        # the top left to the bottom right: x = W*y/H and x = mid - lean*y/H
        if not args.no_flags:
            fw = round(band * 0.55)
            for k, shot in enumerate(shots):
                flag = FLAGS.get(re.sub(r"^\d\d_", "", shot.stem))
                if not flag:
                    continue
                img = tmp / ("flag%02d.png" % k)
                run(["magick", "-background", "none",
                     "pango:<span font=\"Noto Color Emoji\" size=\"96000\">" + flag + "</span>",
                     "-trim", "+repage", "-resize", "%dx" % fw, str(img)])
                iw, ih = size_of(img)
                # its name under it, upright, on a soft halo of the paper's
                # colour so it reads over the game's own text
                key = re.sub(r"^\d\d_", "", shot.stem)
                lbl = tmp / ("name%02d.png" % k)
                run(["magick", "-background", "none",
                     "pango:<span font=\"Noto Sans Medium\" size=\"%d\" foreground=\"#2a221c\">%s</span>"
                     % (round(band * 0.1 * 1024), NAMES.get(key, key)),
                     "-trim", "+repage",
                     "(", "+clone", "-background", "rgb(239,228,205)", "-shadow", "100x3+0+0", ")",
                     "+swap", "-background", "none", "-layers", "merge", "+repage", str(lbl)])
                lw, lh = size_of(lbl)
                gap = band * 0.04
                gh = ih + gap + lh                          # flag and name together
                mid = (top[k] + top[k + 1]) / 2
                cy = mid * H / (W + lean)
                cx = W * cy / H
                # the corner ones kept whole, a little in from the edges
                edge = band * 0.08
                half = max(iw, lw) / 2
                cx = min(max(cx, half + edge), W - half - edge)
                cy = min(max(cy, ih / 2 + edge), H - (gh - ih / 2) - edge)
                cmd += [str(img), "-geometry", "+%d+%d" % (round(cx - iw / 2), round(cy - ih / 2)),
                        "-compose", "over", "-composite",
                        str(lbl), "-geometry", "+%d+%d" % (round(cx - lw / 2), round(cy + ih / 2 + gap)),
                        "-compose", "over", "-composite"]
        cmd += ["-quality", "86", args.out]
        run(cmd)
    print("%d languages -> %s (%dx%d)" % (n, args.out, W, H))
    return 0


if __name__ == "__main__":
    sys.exit(main())
