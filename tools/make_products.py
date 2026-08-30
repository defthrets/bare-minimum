"""
Generates the product icons for the shop UI, into data/icons/p_*.png.

Run:  python tools/make_products.py

WHY NOT THE GAME'S OWN ART. There is no texture dictionary in GTA V holding food
product images -- the shop textures that exist are signage and menu boards, not per-item
icons, and the actual burgers and cans are 3D props whose textures live inside the RPF
archives. Getting at those would mean OpenIV, a .ytd and an asset mod per game edition,
which is exactly what this mod exists without. So the products are drawn here, in the same
white-on-alpha pipeline as the HUD icons, and TINTED PER ITEM at draw time.

ONE SHAPE SERVES MANY ITEMS. There are thirty-odd things on sale and eighteen shapes: a can
is a can whether it holds eCola or Sprunk, and the colour plus the name does the rest.
Drawing thirty near-identical cans would be worse art and a worse file to maintain.

Same supersample-and-downsample and the same black rim as the HUD set, so the two look like
they came from the same hand.
"""

import math
import os

from PIL import Image, ImageDraw

import make_icons as base

OUT = base.OUT
SS = base.SS
W = base.W
WHITE = base.WHITE
CLEAR = base.CLEAR

s = base.s


def canvas():
    img = Image.new("RGBA", (W, W), CLEAR)
    return img, ImageDraw.Draw(img)


def rr(d, box, radius, fill=WHITE):
    d.rounded_rectangle([s(box[0]), s(box[1]), s(box[2]), s(box[3])], radius=s(radius), fill=fill)


def el(d, box, fill=WHITE):
    d.ellipse([s(box[0]), s(box[1]), s(box[2]), s(box[3])], fill=fill)


# ===========================================================================
# Drinks
# ===========================================================================

def can():
    """A drinks can: straight body, tapered top and bottom, tab on the lid."""
    img, d = canvas()

    rr(d, (84, 52, 172, 210), 16)
    rr(d, (78, 74, 178, 186), 10)          # the belly, slightly wider

    # Lid and base rims, punched so the can reads as pressed metal.
    rr(d, (88, 62, 168, 70), 4, CLEAR)
    rr(d, (88, 192, 168, 200), 4, CLEAR)

    # Pull tab.
    el(d, (116, 40, 140, 58))
    el(d, (122, 46, 134, 54), CLEAR)

    return img


def bottle():
    """A long-necked bottle: beer, wine, anything with a shoulder."""
    img, d = canvas()

    rr(d, (112, 24, 144, 84), 8)                    # neck
    d.polygon([(s(112), s(84)), (s(144), s(84)),
               (s(168), s(120)), (s(88), s(120))], fill=WHITE)   # shoulder
    rr(d, (88, 116, 168, 224), 14)                  # body

    rr(d, (108, 20, 148, 34), 5, CLEAR)             # cap line
    rr(d, (98, 140, 158, 186), 6, CLEAR)            # label

    return img


def flask():
    """A squat spirits bottle: square shoulders, short neck. Whiskey."""
    img, d = canvas()

    rr(d, (114, 30, 142, 72), 6)
    rr(d, (80, 68, 176, 226), 12)
    rr(d, (108, 26, 148, 38), 4, CLEAR)
    rr(d, (92, 110, 164, 170), 6, CLEAR)

    return img


def cup():
    """A takeaway cup: tapered body, lid, and a wisp of steam."""
    img, d = canvas()

    d.polygon([(s(84), s(84)), (s(172), s(84)),
               (s(156), s(224)), (s(100), s(224))], fill=WHITE)
    rr(d, (76, 66, 180, 90), 8)                     # lid
    rr(d, (94, 130, 162, 168), 6, CLEAR)            # sleeve

    for x in (110, 128, 146):
        d.line([s(x), s(52), s(x + 8), s(30), s(x), s(12)], fill=WHITE, width=s(6), joint="curve")

    return img


# ===========================================================================
# Hot food
# ===========================================================================

def burger():
    img, d = canvas()

    d.pieslice([s(46), s(66), s(210), s(150)], 180, 360, fill=WHITE)   # crown
    d.rectangle([s(46), s(104), s(210), s(118)], fill=WHITE)

    for i in range(7):
        cx = 52 + i * 26
        el(d, (cx - 20, 122, cx + 20, 150))                            # lettuce

    rr(d, (38, 152, 218, 184), 14)                                     # patty
    d.pieslice([s(46), s(160), s(210), s(232)], 0, 180, fill=WHITE)    # base
    d.rectangle([s(46), s(186), s(210), s(196)], fill=WHITE)

    for sx, sy in ((92, 88), (128, 80), (164, 88)):
        el(d, (sx - 7, sy - 5, sx + 7, sy + 5), CLEAR)

    return img


def sandwich():
    """
    A round of sandwiches cut in half, the two halves leaning together.

    THE FILLING HAS TO SHOW ON THE CUT FACE. The first attempt was two bare triangles with a
    line across each, and bare triangles point-up read as fangs, or as mountains -- nothing
    about the silhouette said bread. Filling stripes across the cut, and a crust along the
    top edge of each, are what make it a sandwich rather than a wedge.
    """
    img, d = canvas()

    for ox, oy in ((0, 14), (96, 0)):
        # The cut face: a triangle standing on its long edge.
        d.polygon([(s(28 + ox), s(206 + oy)), (s(130 + ox), s(206 + oy)),
                   (s(79 + ox), s(96 + oy))], fill=WHITE)

        # Two filling stripes across it, punched out so they read against a flat tint.
        d.polygon([(s(48 + ox), s(164 + oy)), (s(110 + ox), s(164 + oy)),
                   (s(101 + ox), s(146 + oy)), (s(57 + ox), s(146 + oy))], fill=CLEAR)
        d.polygon([(s(40 + ox), s(186 + oy)), (s(118 + ox), s(186 + oy)),
                   (s(110 + ox), s(172 + oy)), (s(48 + ox), s(172 + oy))], fill=CLEAR)

    return img


def taco():
    """
    A folded shell, opening UPWARD, with filling standing proud of it.

    The first version used a pieslice from 180 to 360, which is the TOP half of an ellipse --
    a dome, or a hill. A taco is the bottom half: a U you could put something in. That one
    number is the whole difference between a taco and a small mountain.
    """
    img, d = canvas()

    # The filling first, so the shell overlaps it and it sits inside rather than on top.
    for i in range(7):
        cx = 52 + i * 26
        el(d, (cx - 22, 74, cx + 22, 132))

    # The shell: lower half of an ellipse, with a rim across the opening.
    d.pieslice([s(34), s(24), s(222), s(224)], 0, 180, fill=WHITE)
    rr(d, (34, 108, 222, 132), 11)

    return img


def hotdog():
    """A bun with a sausage and a stripe of sauce."""
    img, d = canvas()

    rr(d, (26, 118, 230, 176), 28)                  # bun
    rr(d, (40, 96, 216, 138), 21)                   # sausage
    d.line([s(60), s(112), s(96), s(126), s(132), s(112), s(168), s(126), s(198), s(112)],
           fill=CLEAR, width=s(9), joint="curve")   # sauce

    return img


def bowl():
    """A bowl with steam. Chilli, noodles, a diner plate of something."""
    img, d = canvas()

    d.pieslice([s(36), s(110), s(220), s(238)], 0, 180, fill=WHITE)
    rr(d, (28, 108, 228, 128), 9)                   # rim
    rr(d, (86, 224, 170, 238), 6)                   # foot

    for x in (100, 128, 156):
        d.line([s(x), s(88), s(x + 10), s(64), s(x), s(40)], fill=WHITE, width=s(6), joint="curve")

    return img


def bucket():
    """A tapered bucket with a lid. Fried chicken."""
    img, d = canvas()

    d.polygon([(s(62), s(92)), (s(194), s(92)),
               (s(170), s(232)), (s(86), s(232))], fill=WHITE)
    rr(d, (52, 72, 204, 98), 10)                    # lid
    rr(d, (78, 140, 178, 180), 6, CLEAR)            # band

    return img


def plate():
    """
    A plate with a knife and fork either side.

    The cutlery is doing the work. A bare ellipse with a smaller ellipse inside it is a
    fried egg, an eye, or a target -- it is only a place setting once there is something to
    eat it with, and that reads at any size because it changes the silhouette.
    """
    img, d = canvas()

    el(d, (58, 88, 198, 216))
    el(d, (78, 106, 178, 198), CLEAR)
    el(d, (94, 120, 162, 184))

    # Fork, left: three tines, a neck and a handle.
    for x in (20, 34, 48):
        rr(d, (x - 5, 74, x + 5, 122), 4)
    rr(d, (13, 116, 55, 136), 7)
    rr(d, (26, 132, 42, 222), 7)

    # Knife, right.
    d.polygon([(s(210), s(74)), (s(232), s(88)), (s(232), s(150)), (s(210), s(150))], fill=WHITE)
    rr(d, (213, 148, 229, 222), 7)

    return img


def box():
    """A takeaway box, lid ajar."""
    img, d = canvas()

    rr(d, (48, 116, 208, 226), 10)
    d.polygon([(s(40), s(116)), (s(216), s(116)),
               (s(196), s(78)), (s(60), s(78))], fill=WHITE)
    d.line([s(48), s(116), s(208), s(116)], fill=CLEAR, width=s(7))

    return img


# ===========================================================================
# Snacks and packets
# ===========================================================================

def chips():
    """
    A crisp packet: fat in the middle, pinched flat at the seals.

    THE SEALS ARE ZIGZAGS, not tick marks. The first version drew a row of short vertical
    lines across each end, and a rectangle with regular legs down two sides is a microchip.
    A sawtooth edge is what a crimped foil packet actually looks like and reads as nothing else.
    """
    img, d = canvas()

    rr(d, (46, 66, 210, 192), 30)               # the bulging body
    d.rectangle([s(62), s(50), s(194), s(210)], fill=WHITE)

    # Sawtooth seals, top and bottom, cut into the packet.
    for y, up in ((44, True), (216, False)):
        pts = []
        for i in range(11):
            x = 58 + i * 14
            dy = -9 if (i % 2 == 0) == up else 9
            pts.append((s(x), s(y + dy)))
        pts.append((s(206), s(y - 30 if up else y + 30)))
        pts.append((s(58), s(y - 30 if up else y + 30)))
        d.polygon(pts, fill=CLEAR)

    rr(d, (82, 104, 174, 154), 8, CLEAR)        # label window

    return img


def bar():
    """
    A chocolate bar stood on end, half out of its wrapper, scored into squares.

    Upright rather than lying down: at icon size a horizontal bar is a rectangle, and a
    rectangle is whatever you already expected. The wrapper line across it and the scoring
    above it are what say "snap a piece off".
    """
    img, d = canvas()

    rr(d, (78, 26, 178, 232), 8)

    # Scored squares on the exposed half.
    for row in range(3):
        for col in range(2):
            rr(d, (88 + col * 46, 36 + row * 40, 126 + col * 46, 68 + row * 40), 5, CLEAR)

    # The wrapper: a band across the lower half with a torn top edge.
    d.rectangle([s(72), s(158), s(184), s(238)], fill=WHITE)
    pts = []
    for i in range(9):
        x = 72 + i * 14
        pts.append((s(x), s(158 + (0 if i % 2 else 10))))
    pts.append((s(184), s(140)))
    pts.append((s(72), s(140)))
    d.polygon(pts, fill=CLEAR)

    return img


def tin():
    """A sardine tin with a roll-back lid."""
    img, d = canvas()

    rr(d, (34, 96, 222, 194), 14)
    rr(d, (48, 108, 208, 182), 8, CLEAR)
    rr(d, (56, 116, 200, 174), 6)

    # The rolled lid at the top corner.
    el(d, (186, 66, 236, 116))
    el(d, (198, 78, 224, 104), CLEAR)

    return img


def bagel():
    """A ring with a seeded top."""
    img, d = canvas()

    el(d, (38, 62, 218, 218))
    el(d, (104, 122, 152, 162), CLEAR)

    for a in range(0, 360, 45):
        r = math.radians(a)
        cx = 128 + math.cos(r) * 62
        cy = 140 + math.sin(r) * 54
        el(d, (cx - 7, cy - 5, cx + 7, cy + 5), CLEAR)

    return img


def pack():
    """
    A cigarette packet, lid open, three standing proud of it.

    The cigarettes have to CLEAR the pack by a good margin and be banded at the filter, or
    the whole thing reads as a box with an aerial. Three at staggered heights, because two
    at the same height is a plug socket.
    """
    img, d = canvas()

    # The cigarettes, drawn first so the pack front overlaps their bases.
    for x, top in ((84, 34), (116, 22), (148, 40)):
        rr(d, (x, top, x + 24, 150), 10)
        rr(d, (x + 2, top + 42, x + 22, top + 62), 6, CLEAR)     # filter band

    rr(d, (70, 96, 188, 240), 10)                                # pack front
    rr(d, (64, 88, 194, 116), 8)                                 # open lid, tilted forward
    rr(d, (86, 152, 172, 208), 7, CLEAR)                         # label

    return img


def fruit():
    """An apple. The HUD already draws one; this is the whole, unbitten shape."""
    return base.apple(4)


# ===========================================================================

SHAPES = {
    "can": can,
    "bottle": bottle,
    "flask": flask,
    "cup": cup,
    "burger": burger,
    "sandwich": sandwich,
    "taco": taco,
    "hotdog": hotdog,
    "bowl": bowl,
    "bucket": bucket,
    "plate": plate,
    "box": box,
    "chips": chips,
    "bar": bar,
    "tin": tin,
    "bagel": bagel,
    "pack": pack,
    "fruit": fruit,
}


def main():
    print("Writing product icons to " + OUT)

    for name, make in sorted(SHAPES.items()):
        base.save(make(), "p_" + name + ".png")

    print("Done, %d shapes. Deploy with:  .\\build.ps1 -Deploy -FreshData" % len(SHAPES))


if __name__ == "__main__":
    main()
