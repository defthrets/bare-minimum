"""
Generates the PNGs that ship in data/icons/.

Run from anywhere:  python tools/make_icons.py

WHITE WITH ALPHA, always. Every icon is drawn as a white silhouette and tinted at runtime by
CustomSprite.Color, so the green of a full stomach, the amber of a hungry one and the red of
a starving one all come from one file. Baking the colour in would mean five files per stage
and a set that drifts apart the first time the palette moves.

Supersampled 4x and resized down with LANCZOS, because PIL has no antialiased drawing.

TWO ICONS, FIVE STAGES EACH. The reading is carried by the SILHOUETTE and not by a fill:
an icon that only gets emptier is a bar wearing a costume, and bars were the one thing ruled
out at the start. An apple is eaten down to a core; an eye closes.
"""

import math
import os

from PIL import Image, ImageChops, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "data", "icons")

SIZE = 256
SS = 4                  # supersample factor
W = SIZE * SS

WHITE = (255, 255, 255, 255)
CLEAR = (255, 255, 255, 0)


def canvas():
    img = Image.new("RGBA", (W, W), CLEAR)
    return img, ImageDraw.Draw(img)


# How thick the outline is, in design units. About 1.3 px once the icon is drawn at its
# HUD size of ~46, which is a hairline that reads as a drawn edge rather than as a border.
OUTLINE = 7


def outlined(img, radius=OUTLINE):
    """
    Wraps the shape in a black rim, baked into the PNG.

    THE OUTLINE SURVIVES THE RUNTIME TINT, and that is the whole reason this can be baked in
    at all. CustomSprite.Color MULTIPLIES with the texture rather than replacing it, so a
    white pixel takes the tint (white x amber = amber) and a BLACK pixel stays black
    whatever the tint is (black x anything = black). One file therefore carries both the
    tintable body and a fixed black edge, with no second sprite and no second draw call.

    The rim is built by stamping the shape's own alpha around a circle rather than with a
    box filter: PIL's MaxFilter dilates with a SQUARE kernel, and on an apple that comes out
    visibly boxy at the shoulders. Twenty-four stamps at the full radius plus a ring at half
    it fills the corners smoothly.

    THE INTERNAL HOLES GET RIMMED TOO, and that is a happy consequence rather than extra
    work: stamping the alpha outward in every direction also closes in on a hole from every
    direction, so the apple's bites, the eye's iris and the core's pips each end up with the
    same black edge as the silhouette, shrunk by the radius. It is what stops the bites
    reading as gaps in a flat colour and makes the whole thing read as drawn.

    The radius therefore cannot exceed half the smallest hole, or that hole fills in
    completely. The tightest are the core's pips at 14 units across, which is why 7 is the
    practical ceiling here.
    """
    alpha = img.getchannel("A")
    grown = alpha.copy()

    for scale in (1.0, 0.5):
        r = radius * SS * scale
        if r < 1:
            continue

        for i in range(24):
            a = 2.0 * math.pi * i / 24.0
            dx = int(round(math.cos(a) * r))
            dy = int(round(math.sin(a) * r))

            # offset() wraps around the edges, which is harmless here only because every
            # icon is drawn with a margin far wider than the radius, so what wraps in is
            # transparent. Anything drawn to the canvas edge would smear across.
            grown = ImageChops.lighter(grown, ImageChops.offset(alpha, dx, dy))

    rim = Image.new("RGBA", img.size, (0, 0, 0, 0))
    rim.putalpha(grown)

    # The body goes ON TOP of the rim, so its antialiased edge blends into black and the
    # transition reads as one drawn line rather than two stacked shapes.
    rim.alpha_composite(img)
    return rim


def save(img, name, outline=True):
    if not os.path.isdir(OUT):
        os.makedirs(OUT)

    if outline:
        img = outlined(img)

    img = img.resize((SIZE, SIZE), Image.LANCZOS)
    path = os.path.join(OUT, name)
    img.save(path, "PNG")
    print("  %-14s %d x %d" % (name, SIZE, SIZE))


def s(v):
    """Design space (0..256) to supersampled pixels."""
    return int(round(v * SS))


def face(px):
    for name in ("ariblk.ttf", "arialbd.ttf", "arial.ttf", "segoeuib.ttf"):
        try:
            return ImageFont.truetype(name, px)
        except OSError:
            continue
    return ImageFont.load_default()


# ===========================================================================
# HUNGER -- an apple, bitten down to a core
# ===========================================================================
#
# THE APPLE IS A PROFILE, NOT AN ELLIPSE. It started as an ellipse 184 wide by 136 tall,
# which is a tomato: half again wider than it is tall, widest across the middle, no taper.
# An apple is about as tall as it is wide, widest across the SHOULDERS in the upper third,
# and narrows toward the base. None of that is available from an ellipse, so the body is a
# half-width profile sampled down the fruit and mirrored.
#
# The core is worth the trouble on its own. A burger eaten to nothing leaves an empty plate,
# which is a different object sitting where the food was; an apple leaves a core, which is
# unmistakably the same apple and unmistakably finished.

APPLE_TOP = 70.0
APPLE_BOTTOM = 236.0
APPLE_DIP = 24.0                # how far the crown sinks between the shoulders

APPLE_PROFILE = [
    (0.00, 46.0),
    (0.10, 68.0),
    (0.24, 80.0),
    (0.38, 82.0),               # widest -- the shoulder, in the upper third
    (0.52, 78.0),
    (0.66, 70.0),
    (0.80, 57.0),
    (0.91, 41.0),
    (1.00, 20.0),               # the base: narrow, but not a point
]

# Bites, per stage: (centre x, centre y, radius).
#
# The first three eat the right-hand side from the top DOWN, so each stage takes a visibly
# different piece. When two bites both landed on the upper right, 75% and 50% came out as
# nearly the same silhouette.
#
# THE LAST STAGE ALSO TAKES ONE FROM THE LEFT, at waist height. That starts the hourglass, so
# 25% reads as "almost a core" rather than as a slightly smaller apple, and the step to the
# core at 0% is not a jump to a shape nobody saw coming.
APPLE_BITES = {
    4: (),
    3: ((214, 108, 48),),
    2: ((204, 96, 52), (210, 180, 48)),
    1: ((182, 92, 56), (188, 188, 54), (196, 138, 52), (44, 150, 42)),
}


def _profile(u):
    """
    Half-width at a height, by cubic Hermite through the profile points.

    Interpolated rather than joined with straight lines: nine points across a 166-unit fruit
    is a facet every eighteen units, and at 4x the flats are plainly visible on the shoulder
    -- the one part of the outline the eye actually reads as "apple".
    """
    pts = APPLE_PROFILE

    if u <= pts[0][0]:
        return pts[0][1]
    if u >= pts[-1][0]:
        return pts[-1][1]

    i = 0
    while i < len(pts) - 2 and u > pts[i + 1][0]:
        i += 1

    x0, y0 = pts[i]
    x1, y1 = pts[i + 1]
    dx = x1 - x0
    if dx <= 0.0:
        return y0

    # One-sided differences at the ends, central differences everywhere else.
    prev = pts[i - 1] if i > 0 else pts[i]
    nxt = pts[i + 2] if i + 2 < len(pts) else pts[i + 1]

    m0 = (y1 - prev[1]) / (x1 - prev[0]) if x1 != prev[0] else 0.0
    m1 = (nxt[1] - y0) / (nxt[0] - x0) if nxt[0] != x0 else 0.0

    t = (u - x0) / dx
    t2 = t * t
    t3 = t2 * t

    return ((2 * t3 - 3 * t2 + 1) * y0 + (t3 - 2 * t2 + t) * dx * m0 +
            (-2 * t3 + 3 * t2) * y1 + (t3 - t2) * dx * m1)


def _apple_body(target, steps=140):
    """The outline as ONE filled polygon: right side down, base, left side up, then the dip."""
    d = ImageDraw.Draw(target)
    h = APPLE_BOTTOM - APPLE_TOP
    pts = []

    for i in range(steps + 1):
        u = i / float(steps)
        pts.append((s(128 + _profile(u)), s(APPLE_TOP + u * h)))

    # The calyx dimple: the base lifts slightly in the middle rather than running flat.
    pts.append((s(136), s(APPLE_BOTTOM - 7)))
    pts.append((s(120), s(APPLE_BOTTOM - 7)))

    for i in range(steps, -1, -1):
        u = i / float(steps)
        pts.append((s(128 - _profile(u)), s(APPLE_TOP + u * h)))

    # The dip between the shoulders, closing the outline across the top.
    for i in range(1, 20):
        t = i / 20.0
        pts.append((s(128 - 46 + 92 * t),
                    s(APPLE_TOP + APPLE_DIP * math.sin(math.pi * t))))

    d.polygon(pts, fill=WHITE)


def _stem_and_leaf(d, img):
    """The stalk out of the dip, and a leaf off the side of it."""
    # Three short segments, so it bends rather than standing up like an aerial.
    d.line([s(128), s(94), s(134), s(70), s(146), s(46)], fill=WHITE, width=s(11),
           joint="curve")

    # THE LEAF IS DRAWN ON ITS OWN LAYER AND ROTATED. PIL cannot rotate a primitive, and an
    # unrotated ellipse beside a stalk reads as a bubble rather than as a leaf. Lifted clear
    # of the shoulder so the two shapes stay separate at small sizes.
    leaf = Image.new("RGBA", img.size, CLEAR)
    ImageDraw.Draw(leaf).ellipse([s(150), s(26), s(208), s(58)], fill=WHITE)
    img.alpha_composite(leaf.rotate(-24, resample=Image.BICUBIC, center=(s(150), s(42))))


def apple(stage):
    """stage 4 = whole, stage 0 = the core."""
    img, d = canvas()

    body = Image.new("RGBA", img.size, CLEAR)
    _apple_body(body)
    bd = ImageDraw.Draw(body)

    if stage == 0:
        # THE CORE, made by taking one big bite out of each side of a whole apple. The
        # flared top, narrow waist and flared bottom are a consequence of eating it, not a
        # separate drawing of a core -- which is exactly why it reads as the same fruit.
        for cx in (26, 230):
            bd.ellipse([s(cx - 70), s(152 - 70), s(cx + 70), s(152 + 70)], fill=CLEAR)

        # Two pips in the waist, punched out so they read against the flat tint.
        for px, py in ((120, 138), (136, 166)):
            bd.ellipse([s(px - 7), s(py - 10), s(px + 7), s(py + 10)], fill=CLEAR)
    else:
        for bx, by, br in APPLE_BITES.get(stage, ()):
            bd.ellipse([s(bx - br), s(by - br), s(bx + br), s(by + br)], fill=CLEAR)
            # A second disc offset off each bite, so the edge is a ragged mouthful rather
            # than a clean machined crescent.
            bd.ellipse([s(bx - br * 0.70), s(by - br * 1.16),
                        s(bx + br * 0.40), s(by - br * 0.10)], fill=CLEAR)

    img.alpha_composite(body)
    _stem_and_leaf(d, img)
    return img


# ===========================================================================
# SLEEP -- an eye closing
# ===========================================================================
#
# Per stage: how tall the opening is, and how much of it the upper lid covers.
#
# THE LID FRACTION IS PER STAGE AND NOT A FORMULA. It used to be derived from the opening
# (lid = cy - opening + (62 - opening) * 0.55), which is fine while the eye is wide and
# inverts as it closes: at stage 1 that put the lid BELOW the middle of a lens only 30 units
# tall and left a four-unit sliver. The icon rendered as a downturned mouth with a Z over it.
LIDS = {
    4: (62, 0.14),
    3: (52, 0.30),
    2: (38, 0.46),
    1: (30, 0.56),
    0: (0, 0.0),
}

# The iris shrinks with the opening too. A 32-unit hole inside a 30-unit slit swallows the
# whole gap, so the eye loses its white and reads as an outline.
IRIS = {4: 32, 3: 30, 2: 26, 1: 20, 0: 0}


def eye(stage):
    """stage 4 = wide awake, stage 0 = shut, with a Z."""
    img, d = canvas()
    cx, cy, half = 128, 138, 86

    opening, lid_frac = LIDS[stage]

    if opening <= 0:
        d.arc([s(cx - half), s(cy - 36), s(cx + half), s(cy + 36)], 15, 165,
              fill=WHITE, width=s(12))
        for a in (35, 90, 145):
            r = math.radians(a)
            x0 = cx + math.cos(r) * half * 0.84
            y0 = cy + math.sin(r) * 32
            d.line([s(x0), s(y0), s(x0 + math.cos(r) * 24), s(y0 + math.sin(r) * 24)],
                   fill=WHITE, width=s(9))
    else:
        # A narrower eye is a SHORTER one. A lens held at full width while its height
        # collapses is a letterbox; the corners have to come in as the lids meet.
        w = half * (0.72 + 0.28 * (opening / 62.0))

        lens = Image.new("RGBA", img.size, CLEAR)
        ld = ImageDraw.Draw(lens)
        ld.ellipse([s(cx - w), s(cy - opening), s(cx + w), s(cy + opening)], fill=WHITE)

        # The iris is a HOLE and the catchlight is a small disc OFF CENTRE inside it.
        # Concentric, the pair is a donut, and at 100% the icon read as a target.
        pr = IRIS[stage]
        ld.ellipse([s(cx - pr), s(cy - pr), s(cx + pr), s(cy + pr)], fill=CLEAR)

        gr = pr * 0.30
        gx, gy = cx - pr * 0.34, cy - pr * 0.34
        ld.ellipse([s(gx - gr), s(gy - gr), s(gx + gr), s(gy + gr)], fill=WHITE)

        # The lid comes down over the TOP of the lens by its own stated fraction, so the
        # visible slit is always a real proportion of the opening. Masked rather than drawn
        # over, so the iris is CUT by the eyelid instead of sitting under it -- that is the
        # difference between looking sleepy and looking like a squint.
        lid_y = cy - opening + 2.0 * opening * lid_frac

        keep = Image.new("L", img.size, 255)
        ImageDraw.Draw(keep).rectangle([0, 0, img.size[0], s(lid_y)], fill=0)
        lens.putalpha(Image.composite(lens.getchannel("A"),
                                      Image.new("L", img.size, 0), keep))
        img.alpha_composite(lens)

        # The lid's own edge, drawn wider than the lens so it overhangs the corners -- that
        # is what makes it read as a lid lying over the eye rather than as its outline.
        #
        # ITS HEIGHT SCALES WITH THE OPENING. Fixed at 46 the dome towered over a nearly-shut
        # eye, so 25% came out as CLOSED with a line under it.
        arch = 28 + opening * 0.30
        d.arc([s(cx - w - 6), s(lid_y - arch), s(cx + w + 6), s(lid_y + arch)],
              182, 358, fill=WHITE, width=s(10))

        # NO LOWER LASHES ON AN OPEN EYE. Two strokes under the outer corners were meant to
        # say "drooping"; across a slit twenty units tall they read as bars over the eye and
        # stage 1 came out looking like a cage. Lashes belong on the closed eye only.

    if stage <= 1:
        d.text((s(186), s(20)), "Z", font=face(s(66) if stage == 0 else s(52)), fill=WHITE)

    return img


def main():
    print("Writing icons to " + OUT)

    for stage in range(5):
        save(apple(stage), "apple%d.png" % stage)

    for stage in range(5):
        save(eye(stage), "eye%d.png" % stage)

    print("Done. Deploy with:  .\\build.ps1 -Deploy -FreshData")


if __name__ == "__main__":
    main()
