"""
Asserts that each icon stage really is emptier than the one before it.

Run:  python tools/check_stages.py

THIS EXISTS BECAUSE EYEBALLING IT FAILED. The apple at 25% used to take a fourth bite out of
its left-hand side, and between that and the three on its right it ended up with LESS fruit
on screen than the finished core at 0% -- so the sequence ran backwards at the very end. Every
one of the five shapes looked plausible on its own; only the set was wrong, and that is
precisely the kind of mistake a person does not catch by looking at five pictures in a row.

Opaque pixel count is a crude proxy for "how much is left", and crude is the point: it needs
no judgement, it runs in a second, and it fails loudly.
"""

import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.join(os.path.dirname(HERE), "data", "icons")

# Anything above this counts as ink. Well clear of the antialiased fringe, which would
# otherwise make the count depend on how much outline perimeter a shape happens to have.
SOLID = 128


def ink(path, box=None):
    """
    Opaque pixels, optionally only inside a region.

    THE REGION MATTERS FOR THE EYE. A "Z" appears at stage 1 and grows at stage 0, so counting
    the whole canvas measures the eye AND the Z together -- and the Z adds enough ink to make
    a closing eye look like it is opening again. The first run of this script duly reported
    the eye failing when the eye was fine and the measurement was wrong.

    Cropping to the eye itself measures the thing the sequence is actually about.
    """
    with Image.open(path) as im:
        alpha = im.convert("RGBA").getchannel("A")
        if box is not None:
            alpha = alpha.crop(box)
        return sum(1 for v in alpha.tobytes() if v > SOLID)


def check(prefix, label, box=None):
    print("%s  (4 = full, 0 = empty)" % label)

    counts = []
    for stage in range(4, -1, -1):
        path = os.path.join(ICONS, "%s%d.png" % (prefix, stage))

        if not os.path.isfile(path):
            print("  MISSING %s" % path)
            return False

        counts.append((stage, ink(path, box)))

    ok = True
    previous = None

    for stage, n in counts:
        note = ""

        if previous is not None:
            if n >= previous:
                note = "  <-- FAILS: not emptier than stage %d" % (stage + 1)
                ok = False
            else:
                note = "  (-%d)" % (previous - n)

        print("  stage %d  %7d px%s" % (stage, n, note))
        previous = n

    return ok


def main():
    good = True

    good &= check("apple", "HUNGER, the apple")
    print("")

    # NO CHECK FOR THE MOON, and that is on purpose rather than an oversight.
    #
    # It has been three things: an eye that closed, a moon that waned, and a sun that set.
    # All three were checked here for the same reason the apple is -- five shapes that each
    # look plausible on their own can still be out of order as a set, and only counting
    # pixels catches it.
    #
    # It is a plain crescent at every stage now, asked for twice, and carries its state in
    # COLOUR alone. There is no ordering left in the art to verify, and asserting it got
    # emptier would fail five files that are identical on purpose.
    #
    # If a sequence ever comes back, so does this line:
    #     good &= check("moon", "SLEEP")

    print("")

    if good:
        print("OK - both sets get emptier at every step.")
        return 0

    print("FAILED - a stage is not emptier than the one before it. See above.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
