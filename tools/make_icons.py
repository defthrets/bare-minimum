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


# How thick the outline is, in design units. About 1.8 px once the icon is drawn at its HUD
# size of ~46 -- heavy enough to read as a deliberate edge at a glance.
#
# THIS NUMBER IS CAPPED BY THE SMALLEST HOLE IN THE ART, because the rim closes in on holes
# from every side as well as growing outward. The core's pips are the tightest thing here, and
# they had to be ENLARGED to go with this: at 14 units across they were exactly 2x the old
# radius of 7, so any increase at all would have sealed them shut and left the core blank.
OUTLINE = 13

# How solid the rim is, 0 to 1.
#
# Not pure black. At full strength a 10-unit rim on a 46 px icon is a hard band that competes
# with the shape it is meant to define -- it stops being an outline and becomes half the
# drawing. At 65% it still reads as a definite edge while letting the game show through, which
# is what keeps the icon sitting ON the scene rather than punched out of it.
OUTLINE_ALPHA = 0.65


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
    completely -- which is why the core's pips are sized off OUTLINE rather than fixed.
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

    # Black, at OUTLINE_ALPHA. The body is composited over it at full strength afterwards,
    # so only the part of the rim that sticks out past the shape is ever seen at this alpha.
    faded = grown.point(lambda v: int(v * OUTLINE_ALPHA))

    rim = Image.new("RGBA", img.size, (0, 0, 0, 0))
    rim.putalpha(faded)

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
# HUNGER -- a drumstick
# ===========================================================================
#
# THE SAME DRAWING AT EVERY STAGE, like the moon. It was an apple eaten down to a core
# across five states, which is the argument this whole icon set was built on -- and it is
# gone by the same decision that took the moon's phases: hunger now says how it is doing
# in COLOUR ALONE. See check_stages, which as a result has nothing left to check.
#
# Drawn to a reference silhouette: fat rounded meat up and to the right, a narrow neck,
# and a two-knuckled bone end down to the left.
#
# BUILT ALONG A HORIZONTAL AXIS AND ROTATED, rather than with every point worked out on
# the diagonal. PIL cannot rotate a primitive, but it can rotate a LAYER, and one rotate
# at the end is far less arithmetic to get wrong than thirty rotated coordinates -- the
# same trick the apple's leaf used.
DRUM_ANGLE = 38.0               # degrees anticlockwise; meat ends up upper-right

# THE TAPER NEEDS LENGTH OR IT IS A BALL ON A STUB. First attempt put a 68-radius meat
# eighty units from a 21-radius neck; over that little run the sides are nearly vertical
# and the whole thing read as a balloon with a knot. Ninety-odd units between a smaller
# fat end and a narrower neck is what makes it a leg.
DRUM_MEAT = (184.0, 128.0, 61.0)        # centre x, y, radius of the fat end
DRUM_NECK = (90.0, 128.0, 16.0)         # where the meat narrows to
DRUM_SHAFT = (54.0, 94.0, 9.0)          # bone: from x, to x, half-height
DRUM_KNOB = (50.0, 17.0, 15.0)          # knuckles: centre x, y offset, radius

# Two nicks in the meat. In the reference they are highlights on a black shape, which on
# a WHITE-on-alpha source means punching holes -- the tint multiplies, so a hole is the
# only way to get a mark that survives being coloured.
DRUM_MARKS = ((203.0, 104.0, 13.0, 9.0), (176.0, 106.0, 6.0, 6.0))


def drumstick(stage=0):
    """A chicken drumstick. The same one at every stage -- see the note above."""
    img, d = canvas()

    layer = Image.new("RGBA", img.size, CLEAR)
    ld = ImageDraw.Draw(layer)

    mx, my, mr = DRUM_MEAT
    nx, ny, nr = DRUM_NECK

    # The meat, and the neck it tapers into: a circle at each end and the quad between
    # them, which gives one continuous outline with no join to hide.
    # Very slightly longer than it is round, which is the difference between a leg and a
    # balloon at a glance. Only a few units -- more and it is an aubergine.
    ld.ellipse([s(mx - mr * 1.06), s(my - mr), s(mx + mr * 1.06), s(my + mr)], fill=WHITE)
    ld.ellipse([s(nx - nr), s(ny - nr), s(nx + nr), s(ny + nr)], fill=WHITE)
    ld.polygon([(s(nx), s(ny - nr)), (s(mx), s(my - mr)),
                (s(mx), s(my + mr)), (s(nx), s(ny + nr))], fill=WHITE)

    # The bone.
    x0, x1, half = DRUM_SHAFT
    ld.polygon([(s(x0), s(my - half)), (s(x1), s(my - half * 1.35)),
                (s(x1), s(my + half * 1.35)), (s(x0), s(my + half))], fill=WHITE)

    kx, ko, kr = DRUM_KNOB
    for side in (-1, 1):
        ky = my + ko * side
        ld.ellipse([s(kx - kr), s(ky - kr), s(kx + kr), s(ky + kr)], fill=WHITE)

    # NO COLLAR CUT. Two goes were spent notching the join between meat and bone to say
    # they are different materials, and both left a ragged edge -- the cut runs almost
    # parallel to the taper there, so it takes slivers rather than a clean bite, and the
    # rim routine then traces every one of them.
    #
    # It does not need one. The two knuckles at the end are what say "bone", and the
    # silhouette reads as a leg without a step in it.

    for hx, hy, hw, hh in DRUM_MARKS:
        ld.ellipse([s(hx - hw), s(hy - hh), s(hx + hw), s(hy + hh)], fill=CLEAR)

    img.alpha_composite(layer.rotate(DRUM_ANGLE, resample=Image.BICUBIC, center=(s(128), s(128))))
    return img


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

# A final squash, applied to the finished fruit before the rim goes on.
#
# WHY A TRANSFORM AND NOT NEW NUMBERS. The profile, the four bite positions, the two core
# ellipses and the pips are all absolute coordinates tuned against each other, and the
# order of the five stages is asserted by pixel count in tools/check_stages.py. Editing the
# geometry to change the proportions means re-tuning all of it and re-proving the order;
# scaling the finished shape keeps every one of those relationships exactly as it was.
#
# BEFORE THE OUTLINE, which is the other half of why it goes here rather than in save():
# squashing an image that already has a 13-unit rim gives it a 13-by-11 one, and an
# outline that is thinner on the top and bottom than the sides is the first thing that
# looks wrong about an icon.
APPLE_SQUASH_Y = 0.88           # shorter
APPLE_SQUASH_X = 1.05           # and a little wider with it
APPLE_PIVOT = 153.0             # the fruit's own middle, so it squashes in place

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
# The bites eat the right-hand side from the top DOWN, so each stage takes a visibly
# different piece. When two bites both landed on the upper right, 75% and 50% came out as
# nearly the same silhouette.
#
# STAGE 1 MUST STILL HAVE MORE APPLE LEFT THAN THE CORE DOES, and that is not obvious by
# eye. It used to take a fourth bite from the LEFT at waist height, on the theory that
# starting the hourglass would make 25% read as "almost a core" -- but between that and the
# three on the right it ate down to 16,600 opaque pixels against the core's 20,200. The 25%
# apple was visibly MORE eaten than the finished core, so the sequence ran backwards at the
# very end.
#
# The left bite is gone and the right ones are pulled back. The whole left profile now
# survives, full height, which is plainly more fruit than a narrow-waisted core.
#
# Checked by COUNTING PIXELS, not by looking: tools/check_stages.py asserts the opaque area
# falls at every step. Five shapes that each look plausible alone can still be out of order
# as a set, and that is exactly the mistake this comment exists to stop being repeated.
APPLE_BITES = {
    4: (),
    3: ((214, 108, 48),),
    2: ((204, 96, 52), (210, 180, 48)),
    1: ((193, 94, 50), (199, 186, 48), (205, 140, 46)),
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
        for cx in (24, 232):
            bd.ellipse([s(cx - 73), s(152 - 73), s(cx + 73), s(152 + 73)], fill=CLEAR)

        # Two pips in the waist, punched out so they read against the flat tint.
        #
        # SIZED AGAINST THE OUTLINE, not chosen freely. The rim eats OUTLINE units into a hole
        # from each side, so a pip narrower than twice that is not a small pip -- it is no pip
        # at all, filled in solid with nothing to show it was ever there.
        pw = OUTLINE + 4
        ph = OUTLINE + 8

        for px, py in ((119, 136), (137, 168)):
            bd.ellipse([s(px - pw), s(py - ph), s(px + pw), s(py + ph)], fill=CLEAR)
    else:
        for bx, by, br in APPLE_BITES.get(stage, ()):
            bd.ellipse([s(bx - br), s(by - br), s(bx + br), s(by + br)], fill=CLEAR)
            # A second disc offset off each bite, so the edge is a ragged mouthful rather
            # than a clean machined crescent.
            bd.ellipse([s(bx - br * 0.70), s(by - br * 1.16),
                        s(bx + br * 0.40), s(by - br * 0.10)], fill=CLEAR)

    img.alpha_composite(body)
    _stem_and_leaf(d, img)

    return _squash(img, APPLE_SQUASH_X, APPLE_SQUASH_Y, 128.0, APPLE_PIVOT)


def _squash(img, sx, sy, px, py):
    """Scales an image about a point, on the same canvas."""
    if sx == 1.0 and sy == 1.0:
        return img

    w, h = img.size
    scaled = img.resize((max(1, int(round(w * sx))), max(1, int(round(h * sy)))),
                        Image.LANCZOS)

    out = Image.new("RGBA", img.size, CLEAR)

    # The pivot has to land back on itself, or the fruit walks up the canvas as the
    # squash gets stronger and the stem ends up off the top.
    out.alpha_composite(scaled, (int(round(s(px) - s(px) * sx)),
                                 int(round(s(py) - s(py) * sy))))
    return out


# ===========================================================================
# SLEEP -- a moon, waning
# ===========================================================================
#
# It was an eye that closed. A moon says the same thing with none of the trouble: an
# eye has to stay an eye at every width, which is why it needed a lid modelled as two
# half-ellipses and an iris that shrinks to match, and it still only ever meant sleep
# by convention. A moon means night to everybody, and it comes with its own way of
# running down.
#
# THE PHASE IS THE STATE, exactly as the bites are for the apple. Full and round when
# you are rested, down to a thin crescent when you are not -- so the silhouette still
# carries the reading and the colour is still only confirming it.
#
# THE CRESCENT, AND ONLY THE CRESCENT, at every stage. It waned through phases once and
# ran sun-through-to-moon once, and both are gone: this is a plain crescent moon with two
# stars, the same drawing five times over.
#
# WHAT THAT COSTS, so nobody has to rediscover it and put the phases back by accident:
# in Bars mode, nothing -- the bar is the reading and the mark is only identity, exactly
# as the pump is in Fumes. In Icons mode the sleep icon now says how tired you are in
# COLOUR ALONE, blue through to deep purple, where the apple still says it in shape as
# well. That is a deliberate trade, asked for twice.
#
# Per stage: ray length (none), shadow offset, and how many stars are out. Kept as a
# table rather than collapsed to a constant, because it is the one place the phases could
# come back from and a table of five identical rows says that more clearly than a
# stage-less function would.
MOON_STAGES = {
    4: (0.0, 36.0, 2),
    3: (0.0, 36.0, 2),
    2: (0.0, 36.0, 2),
    1: (0.0, 36.0, 2),
    0: (0.0, 36.0, 2),
}

MOON_R = 88.0                   # the disc's radius
MOON_CUT_R = 80.0               # the cutter
MOON_CUT_ANGLE = 0.0            # straight right: the horns point up and down

# THICKNESS IS R - CUT_R + OFFSET. At offset 36 that is 46 of a possible 88 -- a bit over
# half the radius, which is what it took to still read at the fifteen pixels a bar is
# drawn at. A finer crescent looked right at 256 and closed into a hairline in the game.
#
# It also means the cutter is SMALLER than the disc, which costs the very sharp horn
# tips: only a cutter bigger than the disc closes in faster than the outer edge and pulls
# the ends out to points. Blunter horns are the price of a crescent you can see.

# Eight rays, as a fraction of the disc's radius for their inner end.
MOON_RAY_COUNT = 8
MOON_RAY_INNER = 1.16
MOON_RAY_WIDTH = 0.30

# The stars, as (x, y, radius). The second one only comes out at the last stage.
#
# They sit INSIDE the disc's outer circle but well inside the CUTTER's, which is the void
# the crescent opens onto -- so they are nowhere near the lit edge and the rim routine
# never has to reconcile a star and the moon's rim in the same few pixels. That is the
# constraint that matters: check against MOON_CUT_R if these ever move again.
# FIVE-POINTED, and outside the crescent's own circle for the big one.
#
# The small star sits inside the moon's disc but well inside the CUTTER's, which is the
# void the crescent opens onto -- so it is nowhere near the lit edge. The big one clears
# the disc entirely. That is the constraint that matters, not the canvas: check against
# MOON_CUT_R and MOON_R if these ever move.
MOON_STARS = ((198.0, 92.0, 25.0), (188.0, 158.0, 17.0))


def star(d, cx, cy, r):
    """
    A five-pointed star, point up.

    NOT the four-pointed sparkle it was. A sparkle is a glint -- it says "shiny", which is
    what it was doing next to a moon that did not need it. Five points is what everybody
    draws when they mean a star, and the reference has them.

    Inner radius is 0.40 of the outer. The mathematically pure figure is 0.382; a little
    fatter survives being shrunk to fifteen pixels, where a thin-armed star fills in at the
    waist and comes out a blob.
    """
    inner = r * 0.40

    pts = []

    for i in range(10):
        rad = r if i % 2 == 0 else inner
        a = math.radians(-90.0 + i * 36.0)

        pts.append((s(cx + math.cos(a) * rad), s(cy + math.sin(a) * rad)))

    d.polygon(pts, fill=WHITE)


def moon(stage=0):
    """Stage 4 is a sun, stage 0 is the crescent. See MOON_STAGES."""
    img, d = canvas()

    cx, cy = 128, 132

    rays, shadow, stars = MOON_STAGES[stage]

    # RAYS FIRST, so the disc drawn over them buries their inner ends. Drawn as tapered
    # triangles rather than lines: a line of even width reads as a spoke, and a sun's rays
    # come to a point.
    if rays > 0.0:
        inner = MOON_R * MOON_RAY_INNER
        outer = inner + rays

        for i in range(MOON_RAY_COUNT):
            a = math.radians(i * 360.0 / MOON_RAY_COUNT + 22.5)
            across = a + math.pi / 2.0

            half = MOON_R * MOON_RAY_WIDTH / 2.0

            tipx, tipy = cx + math.cos(a) * outer, cy + math.sin(a) * outer
            bx, by = cx + math.cos(a) * inner, cy + math.sin(a) * inner

            d.polygon([
                (s(tipx), s(tipy)),
                (s(bx + math.cos(across) * half), s(by + math.sin(across) * half)),
                (s(bx - math.cos(across) * half), s(by - math.sin(across) * half)),
            ], fill=WHITE)

    d.ellipse([s(cx - MOON_R), s(cy - MOON_R), s(cx + MOON_R), s(cy + MOON_R)], fill=WHITE)

    if shadow > 0.0:
        a = math.radians(MOON_CUT_ANGLE)
        ox = cx + math.cos(a) * shadow
        oy = cy + math.sin(a) * shadow

        d.ellipse([s(ox - MOON_CUT_R), s(oy - MOON_CUT_R),
                   s(ox + MOON_CUT_R), s(oy + MOON_CUT_R)], fill=CLEAR)

    for i in range(stars):
        sx, sy, sr = MOON_STARS[i]
        star(d, sx, sy, sr)

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
# WIDENED TO SURVIVE THE OUTLINE. The rim eats OUTLINE units into the open slit from the
# top AND the bottom, so a 13-unit rim costs 26 units of daylight -- and the old stage 1 slit
# was only 26 units tall to begin with. It would have sealed shut and left the "nearly
# asleep" eye identical to the closed one, which is the pair that has to be distinguishable.
#
# Slit height is 2 * opening * (1 - lid), so these give 107, 78, 52 and 32 before the rim
# takes its 26. Stage 1 keeps a sliver, which is exactly what it should be.
LIDS = {
    4: (62, 0.14),
    3: (54, 0.28),
    2: (46, 0.44),
    1: (38, 0.58),
    0: (0, 0.0),
}

# The iris shrinks with the opening too -- a hole as tall as the slit swallows the white
# entirely and the eye reads as an outline. It also has to clear the rim from both sides,
# which is why the smallest is kept comfortably above OUTLINE.
IRIS = {4: 32, 3: 30, 2: 27, 1: 22, 0: 0}


# How far through its own movement each animation frame is. Frame 0 is the resting
# state and is the file the HUD draws for all but a fraction of a second.
#
# THE MOVEMENT REVERSES AT STAGE 0. Every open eye animates by CLOSING -- that is a
# blink. A shut eye cannot blink, so at stage 0 the same three frames run the other
# way and it cracks open instead: somebody fighting to stay awake rather than a
# corpse. Same machinery, same file names, opposite direction.
PHASES = (0.0, 0.55, 1.0)


def eye(stage, phase=0):
    """stage 4 = wide awake, stage 0 = shut with a Z. phase 0-2 animates the lid."""
    img, d = canvas()
    cx, cy, half = 128, 138, 86

    opening, lid_frac = LIDS[stage]

    t = PHASES[phase]

    if stage == 0:
        # Shut, opening to a crack. Deliberately small: a stage-0 eye that opened as
        # wide as stage 1 would read as the meter having refilled itself.
        opening = 17.0 * t
        lid_frac = 0.62
    else:
        # Open, closing. The opening collapses and the lid comes down to meet it, which
        # is the same pair of numbers the stages already move between -- so a blink
        # looks like the icon travelling through its own states rather than a new
        # drawing spliced in.
        opening = opening * (1.0 - t)
        lid_frac = lid_frac + (1.0 - lid_frac) * t

    if opening < 6.0:
        opening = 0

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

        # THE LID IS THE SHAPE OF THE EYE, not something drawn on top of it.
        #
        # There used to be an arch stroked over the lens to say "lid". It read as an
        # eyebrow, and an eyebrow is a face rather than an eye. Cutting the lens flat
        # instead left a D. Cutting it with a second ellipse left a visible facet where
        # the two curves crossed at an angle.
        #
        # So the lens is built as two half-ellipses sharing one width: the lower half
        # keeps the full opening, the upper half is shortened by the lid fraction. They
        # meet at the widest point with the same vertical tangent, so the join is
        # invisible, and a tired eye is simply an eye whose top arc has come down. One
        # shape, no second feature, and the droop is in the silhouette where the rest of
        # this icon set keeps its meaning.
        top = opening * (1.0 - lid_frac)

        lens = Image.new("RGBA", img.size, CLEAR)
        ld = ImageDraw.Draw(lens)

        ld.ellipse([s(cx - w), s(cy - opening), s(cx + w), s(cy + opening)], fill=WHITE)
        ld.rectangle([0, 0, img.size[0], s(cy)], fill=CLEAR)
        ld.ellipse([s(cx - w), s(cy - top), s(cx + w), s(cy + top)], fill=WHITE)

        # The iris is a HOLE and the catchlight is a small disc OFF CENTRE inside it.
        # Concentric, the pair is a donut, and at 100% the icon read as a target.
        #
        # Scaled to the ANIMATED opening, and then held under the top arc: a hole as tall
        # as the lid is low swallows what little white is left and the eye reads as an
        # outline. Anything punched outside the lens does nothing anyway, which is what
        # clips the iris on a droopy eye for free.
        rest = LIDS[stage][0]
        pr = IRIS[stage] if rest <= 0 else IRIS[stage] * (opening / float(rest))
        if pr > top * 0.86:
            pr = top * 0.86
        if pr < 6:
            pr = 0

        if pr > 0:
            ld.ellipse([s(cx - pr), s(cy - pr), s(cx + pr), s(cy + pr)], fill=CLEAR)

            gr = pr * 0.30
            gx, gy = cx - pr * 0.34, cy - pr * 0.34
            ld.ellipse([s(gx - gr), s(gy - gr), s(gx + gr), s(gy + gr)], fill=WHITE)

        img.alpha_composite(lens)

        # NO LOWER LASHES ON AN OPEN EYE. Two strokes under the outer corners were meant to
        # say "drooping"; across a slit twenty units tall they read as bars over the eye and
        # stage 1 came out looking like a cage. Lashes belong on the closed eye only.

    if stage <= 1:
        d.text((s(186), s(20)), "Z", font=face(s(66) if stage == 0 else s(52)), fill=WHITE)

    return img


# ===========================================================================
# The vitals' marks: a heart, a shield, a bolt, for the plates under health, armour and
# energy. Rim-free like the other plate marks, and for the same reason. Drawn in the
# same 256 design space as everything above; these came across from Vitals with that mod.
# ===========================================================================

def heart():
    """
    Two lobes and a point.

    The lobes are circles and the point is a triangle whose top corners sit on the lobes'
    widest points, so the outline runs from lobe to tip in one straight line with no step
    where the shapes meet. The tip is a little below where a circle would put it, which is
    what makes it a heart rather than a rounded triangle.
    """
    img, d = canvas()

    r = 46
    cy = 104
    left, right = 92, 164

    for cx in (left, right):
        d.ellipse([s(cx - r), s(cy - r), s(cx + r), s(cy + r)], fill=WHITE)

    d.polygon([(s(left - r), s(cy)), (s(right + r), s(cy)), (s(128), s(214))], fill=WHITE)

    return img


def shield():
    """
    A heater shield: a flat top, straight shoulders, and two arcs down to a point.

    The arcs are quarter-ellipses rather than circles, so the point is lower than it is wide
    and the thing reads as a shield rather than as a badge. Thirty points an arc is more than
    the 4x supersample can show, which is the idea -- the facets are gone before the resize.
    """
    img, d = canvas()

    top, left, right = 50, 52, 204
    shoulder, tip = 118, 216
    rx, ry = 76, tip - shoulder
    steps = 30

    pts = [(left, top), (right, top), (right, shoulder)]

    for i in range(1, steps):
        a = (math.pi / 2) * i / steps
        pts.append((128 + rx * math.cos(a), shoulder + ry * math.sin(a)))

    pts.append((128, tip))

    for i in range(steps - 1, 0, -1):
        a = (math.pi / 2) * i / steps
        pts.append((128 - rx * math.cos(a), shoulder + ry * math.sin(a)))

    pts.append((left, shoulder))

    d.polygon([(s(x), s(y)) for x, y in pts], fill=WHITE)

    return img


def bolt():
    """
    A lightning bolt, the seven-cornered one.

    Leaning right, which is the way every other bolt on a HUD leans, and heavier in the
    middle than at either end so it still has a body at nine pixels tall.
    """
    img, d = canvas()

    pts = [(154, 34), (70, 144), (122, 144), (104, 222), (188, 108), (136, 108)]

    d.polygon([(s(x), s(y)) for x, y in pts], fill=WHITE)

    return img



# ===========================================================================
# The dashboard lights, for the corner of the minimap's frame: an engine, a headlamp, an oil
# can and a handbrake, the way a dash draws them. White silhouettes, rim-free like the other
# plate marks; the colour is the state, put on at draw time.
# ===========================================================================

def _ring(d, cx, cy, r, thick, start=0, end=360):
    """A thick arc: the slice of the outer disc less the slice of the inner one."""
    d.pieslice([s(cx - r), s(cy - r), s(cx + r), s(cy + r)], start, end, fill=WHITE)
    d.pieslice([s(cx - r + thick), s(cy - r + thick), s(cx + r - thick), s(cy + r - thick)], start - 1, end + 1, fill=CLEAR)


def dash_lamp():
    """A headlamp: the D of the lens, and three beams going out from it, leaning down a little."""
    img, d = canvas()

    d.pieslice([s(26), s(70), s(130), s(186)], 90, 270, fill=WHITE)                    # the lens
    d.rectangle([s(76), s(70), s(94), s(186)], fill=WHITE)                             # its flat back

    for i in range(3):
        y = 92 + i * 36
        d.polygon([(s(110), s(y)), (s(226), s(y - 8)), (s(226), s(y + 6)), (s(110), s(y + 14))], fill=WHITE)

    return img


def dash_oil():
    """An oil can: the body, a spout up to the right, a loop of a handle on top, a drop falling from the spout."""
    img, d = canvas()

    d.rounded_rectangle([s(60), s(122), s(170), s(184)], radius=s(14), fill=WHITE)     # the body
    d.polygon([(s(160), s(134)), (s(222), s(92)), (s(236), s(108)), (s(176), s(160))], fill=WHITE)  # the spout
    d.polygon([(s(26), s(146)), (s(62), s(132)), (s(62), s(166))], fill=WHITE)         # the lip at the back

    _ring(d, 112, 116, 30, 14, 180, 360)                                                 # the handle
    d.rectangle([s(82), s(114), s(96), s(126)], fill=WHITE)
    d.rectangle([s(128), s(114), s(142), s(126)], fill=WHITE)

    d.ellipse([s(220), s(146), s(240), s(170)], fill=WHITE)                             # the drop
    d.polygon([(s(230), s(126)), (s(221), s(154)), (s(239), s(154))], fill=WHITE)

    return img


def dash_brake():
    """The handbrake light: a ring with a mark in it, between two brackets."""
    img, d = canvas()

    _ring(d, 128, 128, 58, 16)                                                          # the ring
    d.rounded_rectangle([s(120), s(92), s(136), s(140)], radius=s(6), fill=WHITE)      # the mark
    d.ellipse([s(118), s(150), s(138), s(170)], fill=WHITE)

    _ring(d, 128, 128, 92, 16, 130, 230)                                                # the left bracket
    _ring(d, 128, 128, 92, 16, 310, 410)                                                # the right bracket

    return img


# ===========================================================================
# THIRST -- a drop of water
# ===========================================================================
#
# THE SAME DRAWING AT EVERY STAGE, like the drumstick and for the same reason: the reading
# is the bar's, and an icon that only gets emptier is a second bar wearing a costume. What
# the mark is for is saying WHICH meter, at a glance, from the plate under the column --
# and a drop says water to everybody in every language there is.
#
# A TEARDROP, NOT A CIRCLE WITH A HAT ON. The shape is a circle at the bottom and the two
# TANGENT lines from the apex down to it, which is what makes the sides meet the bulge
# without a corner -- the join is where the tangent touches, so the curvature is continuous
# and there is nothing for the rim routine to catch on. Drawing it as a triangle stacked on
# a circle leaves two nicks at the shoulders that are perfectly visible at 46 pixels.

DROP_APEX = 42.0                        # the point, in design units from the top
DROP_BULGE = (128.0, 158.0, 58.0)       # centre x, y and radius of the round end


def droplet(stage=0):
    """A falling drop. See the note above for why the stage is ignored."""
    img, d = canvas()

    layer = Image.new("RGBA", img.size, CLEAR)
    ld = ImageDraw.Draw(layer)

    cx, cy, r = DROP_BULGE
    ax, ay = cx, DROP_APEX

    span = cy - ay

    # Where the sides touch the bulge. cos(phi) = r / span is the angle between the line to
    # the apex and the radius that meets the tangent -- the one bit of trigonometry in the
    # shape, and the reason it has no shoulders.
    phi = math.acos(max(-1.0, min(1.0, r / span)))

    right = -math.pi / 2.0 + phi
    left = -math.pi / 2.0 - phi

    points = [(s(ax), s(ay))]

    # Round the bulge the long way -- from the right-hand tangent point, down under the
    # bottom, and back up to the left one.
    steps = 96
    for i in range(steps + 1):
        a = right + (left + 2.0 * math.pi - right) * i / steps
        points.append((s(cx + math.cos(a) * r), s(cy + math.sin(a) * r)))

    ld.polygon(points, fill=WHITE)

    # NO HIGHLIGHT. One was drawn and taken out again: CustomSprite MULTIPLIES the tint
    # through the sprite, so a lighter fill is impossible and the only way to mark the inside
    # of a shape is to punch a hole in it -- which on a drop is a pupil, and the mark under
    # the bar came out looking like an eye. The drumstick can afford its two nicks because
    # they sit off-centre in a much wider shape. This cannot, and does not need to: a
    # teardrop is already unmistakable in silhouette alone at forty-six pixels.

    img.alpha_composite(layer)
    return img


def main():
    print("Writing icons to " + OUT)

    # THE DRUMSTICK REPLACED THE APPLE, and the apple's five states with it. apple() and
    # its whole apparatus -- the profile spline, the bite table, the core -- are still here
    # and still work; nothing asks for the files any more, so nothing writes them. Same as
    # eye(), which the moon replaced the same way.
    for stage in range(5):
        save(drumstick(stage), "food%d.png" % stage)

    # FLAT COPIES, NO RIM, for the marks under the bars.
    #
    # THE OUTLINED SET IS STILL THE ICON HUD'S. That one sits on the world at whatever size
    # HudSize says and needs its rim to survive a bright sky. The bar marks are a logo
    # under an instrument, asked for as a plain black silhouette, and a black rim on black
    # art is invisible at best -- CustomSprite MULTIPLIES, so the rim and the fill come out
    # the same colour and all it does is fatten the shape by thirteen units.
    #
    # Two sets, one drawing, one flag apart.
    for stage in range(5):
        save(drumstick(stage), "food%d_flat.png" % stage, outline=False)

    # THE MOON REPLACED THE EYE. eye() is still here and still works -- it is a decent
    # piece of drawing and the argument for the moon was about meaning, not quality --
    # but nothing asks for its files any more, so nothing writes them.
    for stage in range(5):
        save(moon(stage), "moon%d.png" % stage)

    for stage in range(5):
        save(moon(stage), "moon%d_flat.png" % stage, outline=False)

    # THIRST. Five copies of one drawing, as the drumstick is, so Gauge can index the set by
    # stage without knowing or caring that the stages look alike.
    for stage in range(5):
        save(droplet(stage), "drop%d.png" % stage)

    for stage in range(5):
        save(droplet(stage), "drop%d_flat.png" % stage, outline=False)

    # THE VITALS' MARKS, rim-free like the other plate marks.
    save(heart(), "heart.png", outline=False)
    save(shield(), "shield.png", outline=False)
    save(bolt(), "bolt.png", outline=False)

    # THE DASHBOARD LIGHTS, for the corner of the minimap's frame. Rim-free as well.
    save(dash_lamp(), "dash_lamp.png", outline=False)
    save(dash_oil(), "dash_oil.png", outline=False)
    save(dash_brake(), "dash_brake.png", outline=False)

    print("Done. Deploy with:  .\\build.ps1 -Deploy -FreshData")


if __name__ == "__main__":
    main()
