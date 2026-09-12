# -*- coding: utf-8 -*-
"""
The icon set, smaller -- and back again.

    python tools/icon-size.py small      shrink to the sizes below
    python tools/icon-size.py full       put the archived originals back
    python tools/icon-size.py report     say what is there now and what it costs

WHY IT IS WORTH DOING. Every icon is a texture the game holds for the whole session: they
load the first time each is drawn and nothing releases them. At 256 square the set is
eighty-three megabytes of video memory if you browse enough shops to touch all of it, and it
only ever goes up. On a machine already short of VRAM that is the one thing this mod does
that gets worse the longer you play.

AND SMALLER IS ALSO SHARPER, which is the part that makes this free. The product pictures
are 48-pixel sprites blown up by a whole number to fill the canvas -- there is no detail past
48 -- and they are drawn about seventy-five pixels tall. A 256 texture is therefore being
scaled DOWN by more than three, which softens exactly the hard pixel edges the art is made
of. At 128 the blow-up is x2 and the draw is close to one-to-one.

REVERTING IS A COPY. The full-size set is archived to tools/icons-full the first time this
runs and never written again, so "full" is always the originals and never a shrink of a
shrink. Running "small" twice is the same as running it once, for the same reason: the
glyphs are resampled FROM THE ARCHIVE, not from whatever is in data/icons at the time.

THE PIXEL ART IS RE-RENDERED, NOT RESIZED. A 48-pixel sprite at x5 is 240 wide; scaling that
to 128 is a factor of 2.5, which lands half the source pixels on two screen pixels and half
on three -- the exact mess the whole-number blow-up exists to avoid. So the bind record is
re-run at the smaller canvas instead and every picture is rebuilt from its own source at x2.
Nothing is re-chosen: tools/pixelart-bind.py is the same list of decisions either way.
"""
import io
import os
import shutil
import struct
import sys

from PIL import Image

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ICONS = os.path.join(HERE, "data", "icons")
ARCHIVE = os.path.join(HERE, "tools", "icons-full")

sys.path.insert(0, os.path.join(HERE, "tools"))

# What each family shrinks to. The pixel art is not here because it is re-rendered rather
# than resampled -- see the module note.
SMALL_GLYPH = 128          # everything square at 256: the product glyphs, the marks, the row icons
SMALL_SEAL = 512           # the three 1024 seals, which are drawn big on the splash
SMALL_WIDE = 0.5           # the two wordmarks, which are not square


def size_of(path):
    with open(path, "rb") as f:
        head = f.read(24)
    return struct.unpack(">II", head[16:24])


def scan(folder):
    out = []
    for f in sorted(os.listdir(folder)):
        if f.lower().endswith(".png"):
            p = os.path.join(folder, f)
            w, h = size_of(p)
            out.append((f, w, h, os.path.getsize(p)))
    return out


def report(folder=ICONS, label="data/icons"):
    rows = scan(folder)
    vram = sum(w * h * 4 for _, w, h, _ in rows)
    disk = sum(b for _, _, _, b in rows)

    print("  %-16s %3d files, %.1f MB on disk, %.1f MB as textures" %
          (label, len(rows), disk / 1048576.0, vram / 1048576.0))

    by = {}
    for _, w, h, _ in rows:
        by[(w, h)] = by.get((w, h), 0) + 1
    for (w, h), n in sorted(by.items(), key=lambda kv: -kv[1] * kv[0][0] * kv[0][1]):
        print("      %4dx%-5d %3d  %5.1f MB" % (w, h, n, (w * h * 4 * n) / 1048576.0))

    return vram


def archive():
    """The originals, once. Never written again -- see the module note."""
    if os.path.isdir(ARCHIVE) and os.listdir(ARCHIVE):
        return False

    os.makedirs(ARCHIVE, exist_ok=True)
    for f, _, _, _ in scan(ICONS):
        shutil.copyfile(os.path.join(ICONS, f), os.path.join(ARCHIVE, f))

    print("  archived %d file(s) to %s" % (len(os.listdir(ARCHIVE)),
                                           os.path.relpath(ARCHIVE, HERE)))
    return True


def small():
    if not os.path.isdir(ICONS):
        raise SystemExit("no data/icons")

    print("BEFORE")
    was = report()
    print()

    archive()

    # ---- the pixel art, rebuilt from its own sources at the smaller canvas ----
    import pixelart
    import importlib
    bind = importlib.import_module("pixelart-bind")

    pixelart.CANVAS = SMALL_GLYPH

    print("\n  re-rendering the pixel art at %d, from tools/pixelart-bind.py" % SMALL_GLYPH)

    quiet = io.StringIO()
    out, sys.stdout = sys.stdout, quiet
    try:
        bind.main()
    finally:
        sys.stdout = out

    done = len([l for l in quiet.getvalue().splitlines() if " <- " in l])
    print("  %d picture(s) rebuilt" % done)

    # ---- everything else, resampled FROM THE ARCHIVE ----
    rebuilt = {f for f, _, _, _ in scan(ICONS)
               if os.path.exists(os.path.join(ARCHIVE, f)) and
               size_of(os.path.join(ICONS, f)) != size_of(os.path.join(ARCHIVE, f))}

    touched = 0

    for f, w, h, _ in scan(ARCHIVE):
        if f in rebuilt:
            continue

        if w == h and w > SMALL_SEAL:
            tw = th = SMALL_SEAL
        elif w == h and w > SMALL_GLYPH:
            tw = th = SMALL_GLYPH
        elif w != h and w > SMALL_GLYPH:
            tw, th = int(w * SMALL_WIDE), int(h * SMALL_WIDE)
        else:
            continue

        # LANCZOS, because these are antialiased vector art rather than pixel art -- the
        # opposite of what the pictures above want, and the same filter their own generator
        # used to come down from its supersample.
        im = Image.open(os.path.join(ARCHIVE, f)).convert("RGBA")
        im.resize((tw, th), Image.LANCZOS).save(os.path.join(ICONS, f), optimize=True)
        touched += 1

    print("  %d glyph(s) resampled from the archive" % touched)

    print("\nAFTER")
    now = report()

    print("\n  %.1f MB -> %.1f MB of texture memory, %.0f%% less" %
          (was / 1048576.0, now / 1048576.0, 100.0 * (was - now) / max(1, was)))
    print("  revert with: python tools/icon-size.py full")


def full():
    if not os.path.isdir(ARCHIVE) or not os.listdir(ARCHIVE):
        raise SystemExit("nothing archived -- tools/icons-full is empty, so there is nothing to go back to")

    n = 0
    for f, _, _, _ in scan(ARCHIVE):
        shutil.copyfile(os.path.join(ARCHIVE, f), os.path.join(ICONS, f))
        n += 1

    print("  restored %d file(s) from %s" % (n, os.path.relpath(ARCHIVE, HERE)))
    print()
    report()
    print("\n  deploy with: pwsh -NoProfile -File build.ps1 -Deploy -FreshData")


def main(argv):
    what = argv[0] if argv else "report"

    if what == "small":
        small()
    elif what == "full":
        full()
    elif what == "report":
        report()
        if os.path.isdir(ARCHIVE) and os.listdir(ARCHIVE):
            print()
            report(ARCHIVE, "tools/icons-full")
    else:
        print(__doc__)
        sys.exit(2)


if __name__ == "__main__":
    main(sys.argv[1:])
