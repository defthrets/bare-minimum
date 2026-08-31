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


def main():
    print("Writing icons to " + OUT)

    for stage in range(5):
        save(apple(stage), "apple%d.png" % stage)

    # FLAT COPIES, NO RIM, for the marks that stand in the foot of a bar.
    #
    # The bar draws its mark the way the fuel gauge in Fumes draws its pump: as a flat
    # silhouette, near-black over the fill and near-white over the empty channel. That
    # only works on art with no outline of its own. CustomSprite MULTIPLIES its colour
    # with the texture, so a black rim stays black whatever ink it is given -- ask for a
    # black silhouette and you get a black shape inside a black halo, which at fifteen
    # pixels wide is a smudge; ask for a white one and the rim cuts it up.
    #
    # So the HUD icons keep their rim, because they sit on the world and need it, and the
    # bar marks get a copy without one. Same drawing, one flag apart.
    for stage in range(5):
        save(apple(stage), "apple%d_flat.png" % stage, outline=False)

    for stage in range(5):
        save(eye(stage), "eye%d_flat.png" % stage, outline=False)

    # Three frames per stage. Frame 0 keeps the plain name because it is the resting
    # state and everything else in the mod -- the menu title marks, the settings panel --
    # asks for eyeN.png and wants the eye at rest.
    for stage in range(5):
        for phase in range(3):
            name = "eye%d.png" % stage if phase == 0 else "eye%d_%d.png" % (stage, phase)
            save(eye(stage, phase), name)

    print("Done. Deploy with:  .\\build.ps1 -Deploy -FreshData")


if __name__ == "__main__":
    main()
