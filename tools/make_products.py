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


def shake():
    """
    A milkshake: tall tapered cup, domed top, straw out at an angle.

    THE STRAW IS THE WHOLE IDENTITY. Without it this is the coffee cup with the lid off,
    and a shop that sells both would be showing the same picture twice.
    """
    img, d = canvas()

    d.polygon([(s(84), s(96)), (s(172), s(96)), (s(158), s(238)), (s(98), s(238))], fill=WHITE)
    el(d, (80, 66, 176, 122))                                    # domed top
    rr(d, (74, 92, 182, 112), 8)                                 # rim
    d.polygon([(s(150), s(30)), (s(168), s(36)), (s(140), s(104)), (s(126), s(100))], fill=WHITE)

    return img


def cone():
    """An ice cream cone: two scoops and a waffle cone, with the lattice cut back out."""
    img, d = canvas()

    el(d, (74, 62, 142, 128))                                    # scoop, left
    el(d, (116, 54, 186, 124))                                   # scoop, right
    el(d, (92, 96, 168, 158))                                    # the pair sitting on the cone

    d.polygon([(s(84), s(132)), (s(176), s(132)), (s(130), s(240))], fill=WHITE)

    # The waffle, cut out rather than drawn on, so it survives the tint.
    for i in range(-3, 5):
        d.line([(s(70 + i * 22), s(126)), (s(126 + i * 22), s(246))], fill=CLEAR, width=s(5))
        d.line([(s(190 - i * 22), s(126)), (s(134 - i * 22), s(246))], fill=CLEAR, width=s(5))

    return img


def donut():
    """
    A ring with icing drooling down one side and sprinkles punched through it.

    Deliberately not the bagel. Same ring underneath, but the icing has a wavy lower edge
    and the holes are little bars rather than round seeds, which is enough at row size.
    """
    img, d = canvas()

    el(d, (34, 58, 222, 222))
    el(d, (100, 118, 156, 166), CLEAR)

    for i, (cx, cy, a) in enumerate(((78, 108, 40), (170, 104, -35), (128, 82, 5),
                                     (94, 178, -25), (166, 176, 30), (128, 200, 0),
                                     (62, 142, 80), (196, 146, -80))):
        r = math.radians(a)
        dx, dy = math.cos(r) * 11, math.sin(r) * 11
        d.line([(s(cx - dx), s(cy - dy)), (s(cx + dx), s(cy + dy))], fill=CLEAR, width=s(7))

    return img


def slice():
    """A wedge of pizza: crust along the top, point at the bottom, holes for the pepperoni."""
    img, d = canvas()

    d.polygon([(s(48), s(74)), (s(208), s(74)), (s(128), s(240))], fill=WHITE)
    rr(d, (42, 52, 214, 92), 18)                                 # crust

    for cx, cy, r in ((96, 122, 15), (158, 118, 14), (128, 168, 13), (110, 204, 10)):
        el(d, (cx - r, cy - r, cx + r, cy + r), CLEAR)

    return img


def noodles():
    """
    A takeaway box with two chopsticks and a tangle over the rim.

    The China buffet needed something that was not the chilli bowl, and the wire-handled
    box is the one silhouette everybody reads as Chinese food without a word of text.
    """
    img, d = canvas()

    # Chopsticks first, so the box front covers where they enter it.
    d.polygon([(s(146), s(26)), (s(160), s(30)), (s(140), s(150)), (s(128), s(148))], fill=WHITE)
    d.polygon([(s(176), s(34)), (s(190), s(40)), (s(152), s(152)), (s(140), s(148))], fill=WHITE)

    # A tangle of noodles over the rim, drawn as arcs so it is not a solid lump.
    for i in range(3):
        d.arc([s(64 + i * 8), s(78 + i * 10), s(192 - i * 8), s(140 + i * 6)],
              200, 340, fill=WHITE, width=s(9))

    d.polygon([(s(62), s(110)), (s(194), s(110)), (s(170), s(240)), (s(86), s(240))], fill=WHITE)
    rr(d, (56, 100, 200, 126), 8)                                # rim
    rr(d, (104, 152, 152, 214), 8, CLEAR)                        # label panel

    return img


def loaf():
    """A tin loaf: domed top, straight sides, three slashes across the crust."""
    img, d = canvas()

    rr(d, (40, 108, 216, 226), 20)
    el(d, (40, 62, 216, 158))

    for i, x in enumerate((84, 128, 172)):
        d.line([(s(x - 20), s(112)), (s(x + 8), s(76))], fill=CLEAR, width=s(9))

    return img


def carton():
    """
    A milk carton: gabled top, straight body, a band across the front.

    The gable is the whole silhouette. Square it off and this is the takeaway box,
    which is already in the set and already means something else.
    """
    img, d = canvas()

    rr(d, (74, 94, 182, 238), 8)
    d.polygon([(s(74), s(102)), (s(128), s(34)), (s(182), s(102))], fill=WHITE)

    # The fold line down the gable, and the label band, cut out so the tint reads.
    d.line([(s(128), s(38)), (s(128), s(98))], fill=CLEAR, width=s(6))
    rr(d, (92, 140, 164, 196), 6, CLEAR)

    return img


def croissant():
    """
    A croissant: fat middle, horns tapering down and in, nicked between the segments.

    Built along an arc rather than as one crescent, because a plain crescent at row size
    is a moon. The taper is what does the work -- equal-sized lumps read as a cloud, and
    the first attempt at this did exactly that.
    """
    img, d = canvas()

    def at(t):
        a = math.radians(202 + t * 136)
        return 128 + math.cos(a) * 76, 182 + math.sin(a) * 76

    for t, r in ((0.00, 13), (0.12, 24), (0.28, 36), (0.50, 43),
                 (0.72, 36), (0.88, 24), (1.00, 13)):
        cx, cy = at(t)
        el(d, (cx - r, cy - r * 0.95, cx + r, cy + r * 0.95))

    # Nicks between the segments, cut deep enough to survive the downsample.
    for t in (0.30, 0.70):
        cx, cy = at(t)
        d.line([(s(cx), s(cy - 40)), (s(cx), s(cy + 40))], fill=CLEAR, width=s(8))

    return img


def muffin():
    """A muffin: fluted case, domed top spilling over it."""
    img, d = canvas()

    el(d, (52, 58, 204, 168))                                    # the top, wider than the case
    d.polygon([(s(74), s(130)), (s(182), s(130)), (s(158), s(238)), (s(98), s(238))], fill=WHITE)

    # Flutes cut out of the case.
    for x in (98, 128, 158):
        d.line([(s(x), s(140)), (s(x), s(232))], fill=CLEAR, width=s(6))

    return img


def cigar():
    """
    A cigar: fat, tapered at the lit end, with a band near the foot.

    Deliberately not the cigarette pack, which is already in the set and means the
    cheap thing. This is the one you get handed at a tobacconist.
    """
    img, d = canvas()

    d.polygon([(s(28), s(150)), (s(48), s(120)), (s(228), s(102)), (s(228), s(150))], fill=WHITE)
    el(d, (206, 100, 246, 152))                                  # rounded head
    rr(d, (150, 106, 186, 150), 4, CLEAR)                        # the band

    # A wisp off the lit end, so it reads as lit rather than as a peg.
    for i, (x, y) in enumerate(((44, 92), (58, 66), (44, 42))):
        el(d, (x - 9, y - 9, x + 9, y + 9))

    return img


def sweetjar():
    """A confectioner's jar: wide belly, screw lid, sweets showing through."""
    img, d = canvas()

    rr(d, (56, 84, 200, 240), 26)
    rr(d, (72, 46, 184, 88), 12)                                 # lid
    rr(d, (86, 34, 170, 54), 8)                                  # knob

    # The sweets, cut out so they read at any tint.
    for cx, cy in ((92, 140), (128, 128), (164, 142), (106, 180), (150, 182), (128, 214)):
        el(d, (cx - 15, cy - 15, cx + 15, cy + 15), CLEAR)

    return img


def wrap():
    """
    A wrap, cut on the diagonal and standing up: cylinder, angled face, filling showing.

    Not the taco shape, which is already spoken for by three taco places and a burrito.
    A wrap stands on its cut end and a taco lies on its side, and at row size that
    difference is the entire way you tell the two shops apart.
    """
    img, d = canvas()

    # The barrel, leaning very slightly, with the diagonal cut across the top.
    d.polygon([(s(84), s(96)), (s(176), s(56)), (s(190), s(230)), (s(96), s(238))], fill=WHITE)
    el(d, (82, 62, 180, 118))                                    # the cut face

    # Filling: a couple of bites cut out of the face so it is not a blank disc.
    el(d, (104, 78, 132, 98), CLEAR)
    el(d, (136, 70, 160, 88), CLEAR)

    # The seam down the side, which is what makes it rolled rather than turned.
    d.line([(s(120), s(122)), (s(132), s(228))], fill=CLEAR, width=s(6))

    return img


def cookie():
    """A cookie: filled disc with a bitten edge and chips punched through it."""
    img, d = canvas()

    el(d, (36, 36, 220, 220))
    el(d, (188, 44, 258, 114), CLEAR)                            # the bite

    for cx, cy, r in ((88, 92, 15), (140, 80, 13), (76, 150, 14),
                      (132, 146, 16), (170, 168, 12), (108, 194, 13)):
        el(d, (cx - r, cy - r, cx + r, cy + r), CLEAR)

    return img


def fruit():
    """An apple. The HUD already draws one; this is the whole, unbitten shape."""
    return base.apple(4)


# ===========================================================================

# ===========================================================================
# Shapes added when the catalogue outgrew the first set
#
# The rule at the top of this file still holds -- one shape serves many items, and a can is
# a can whatever is in it. These are not exceptions to that; they are the cases where the
# shared shape was simply WRONG rather than merely general. A whole pizza drawn as a
# takeaway box, a roast chicken drawn as a bucket and a slice of apple pie drawn as a wedge
# of pepperoni pizza are not items sharing a silhouette, they are items wearing somebody
# else's.
# ===========================================================================


def pizza():
    """A whole pizza seen from above: rim, cut lines, pepperoni. Not the single wedge."""
    img, d = canvas()

    el(d, (24, 24, 232, 232))
    el(d, (44, 44, 212, 212), CLEAR)          # rim
    el(d, (52, 52, 204, 204))                 # the pizza itself

    # Cut into six, which reads as a whole pizza where four reads as a target.
    for i in range(6):
        a = math.radians(i * 60.0)
        d.line([s(128), s(128),
                s(128 + math.cos(a) * 76), s(128 + math.sin(a) * 76)],
               fill=CLEAR, width=s(6))

    for cx, cy in ((96, 96), (160, 104), (112, 160), (168, 158)):
        el(d, (cx - 13, cy - 13, cx + 13, cy + 13), CLEAR)

    return img


def pie():
    """A round pie: crimped rim, domed lid, two steam vents cut into the top."""
    img, d = canvas()

    # The dish, then the lid sitting proud of it.
    rr(d, (34, 150, 222, 206), 16)
    el(d, (40, 78, 216, 186))

    # Crimping, as notches taken out of the rim rather than added to it -- added scallops
    # close up into a blob at icon size, cut ones stay legible.
    for i in range(9):
        x = 46 + i * 21
        el(d, (x - 9, 138, x + 9, 162), CLEAR)

    for x in (108, 148):
        rr(d, (x - 6, 100, x + 6, 126), 6, CLEAR)

    return img


def chicken():
    """
    THE HUD'S OWN DRUMSTICK, not a second drawing of one.

    Three attempts at drawing a piece of chicken here failed in three different ways -- two
    legs on a body became a face with ears, a bird on a spit became a bird, and a hand-rolled
    drumstick came out as a dumbbell because a fat blob with a knuckle at each end IS a bone
    unless the meat tapers into it properly.

    make_icons already solved this. Its drumstick has a meat circle tapering through a neck
    into the shaft, the taper drawn as a quad between two circles so there is no join to
    hide, and the whole thing rotated so the meat sits upper-right. It took its own two goes
    to get there and the comments in it record why the obvious collar cut had to come out.

    Calling it is not laziness, it is the correct answer twice: one drawing to maintain, and
    the meter and the meat on the shelf agree about what chicken looks like.
    """
    return base.drumstick()


def bag():
    """
    A paper grocery bag, handles CUT OUT of the fold rather than arcing over it.

    Drawn arcs above the bag came out as a ribbon and the whole thing read as a wrapped
    present. A die-cut handle is a hole, and a hole cannot be mistaken for a bow.
    """
    img, d = canvas()

    d.polygon([(s(46), s(62)), (s(210), s(62)), (s(198), s(236)), (s(58), s(236))], fill=WHITE)

    rr(d, (40, 54, 216, 96), 8)               # the folded top

    # The two handle holes, in the fold.
    for x in (98, 158):
        rr(d, (x - 22, 64, x + 22, 86), 10, CLEAR)

    # A crease down the face, so it reads as paper and not as a block.
    d.line([s(128), s(104), s(128), s(228)], fill=CLEAR, width=s(5))

    return img


def jerky():
    """Three strips of dried meat, overlapping, with torn ends."""
    img, d = canvas()

    for i, (x, y, lean) in enumerate(((58, 60, 8), (96, 108, -6), (74, 156, 10))):
        d.polygon([
            (s(x), s(y)), (s(x + 118), s(y + lean)),
            (s(x + 118), s(y + lean + 40)), (s(x), s(y + 40)),
        ], fill=WHITE)

        # A couple of bites out of each edge. Dried meat is never a clean rectangle.
        el(d, (x + 26 + i * 8, y - 7, x + 50 + i * 8, y + 9), CLEAR)
        el(d, (x + 62 - i * 6, y + lean + 32, x + 88 - i * 6, y + lean + 48), CLEAR)

    return img


def sundae():
    """A footed glass with two scoops and a cherry. The GLASS is what separates it from a cone."""
    img, d = canvas()

    el(d, (66, 40, 138, 108))                 # scoops
    el(d, (120, 34, 194, 104))

    el(d, (150, 12, 184, 46))                 # cherry
    d.line([s(167), s(20), s(186), s(2)], fill=WHITE, width=s(6))

    d.polygon([(s(62), s(96)), (s(194), s(96)), (s(150), s(190)), (s(106), s(190))], fill=WHITE)
    rr(d, (118, 186, 138, 220), 4)            # stem
    rr(d, (78, 216, 178, 238), 10)            # foot

    return img


def cake():
    """
    A slice of cake from the SIDE: flat on the plate, tall at the back, tapering forward.

    Pointing the wedge downward made it a cone -- a triangle narrowing to the bottom of the
    frame is an ice cream every time, and there is already a cone in this set for that. Laid
    on its base with the layers running across it, it can only be cake.
    """
    img, d = canvas()

    d.polygon([(s(44), s(76)), (s(196), s(76)), (s(212), s(212)), (s(44), s(212))], fill=WHITE)

    rr(d, (38, 64, 202, 92), 10)              # icing along the top

    # Two layer lines, cut out so they hold at any tint.
    for y in (128, 168):
        d.polygon([(s(52), s(y)), (s(206), s(y + 2)), (s(206), s(y + 15)), (s(52), s(y + 13))],
                  fill=CLEAR)

    el(d, (58, 34, 94, 70))                   # cherry
    d.line([s(76), s(42), s(96), s(20)], fill=WHITE, width=s(6))

    return img


def dumpling():
    """Three pleated dumplings in a huddle. The pleats are the whole read."""
    img, d = canvas()

    for cx, cy, r in ((80, 150, 52), (176, 150, 52), (128, 92, 54)):
        el(d, (cx - r, cy - r * 0.86, cx + r, cy + r))

        # Pleats along the crown, cut out. Three each is enough at icon size.
        for k in (-1, 0, 1):
            px = cx + k * (r * 0.44)
            rr(d, (px - 5, cy - r * 0.86, px + 5, cy - r * 0.30), 5, CLEAR)

    # A steamer line under them so they are sitting in something.
    rr(d, (36, 206, 220, 226), 9)

    return img


def shot():
    """A shot glass: thick base, straight sides, filled near the top."""
    img, d = canvas()

    d.polygon([(s(80), s(72)), (s(176), s(72)), (s(166), s(214)), (s(90), s(214))], fill=WHITE)
    rr(d, (74, 62, 182, 84), 8)               # rim
    rr(d, (72, 208, 184, 232), 8)             # base

    # The level, cut out, which is what makes it a shot rather than an empty glass.
    d.polygon([(s(92), s(96)), (s(164), s(96)), (s(160), s(126)), (s(96), s(126))], fill=CLEAR)

    return img


def egg():
    """A fried egg: white spread wide, yolk cut out of it."""
    img, d = canvas()

    # Two overlapping ellipses and a lobe, so the white is not a circle.
    el(d, (30, 66, 190, 196))
    el(d, (96, 44, 226, 168))
    el(d, (76, 130, 200, 216))

    el(d, (104, 96, 168, 160), CLEAR)         # yolk
    el(d, (112, 104, 160, 152))               # and the yolk itself, standing proud

    return img


# ===========================================================================
# The second pass: splitting the shapes that were carrying too much
#
# Twenty-seven drinks were sharing four pictures, and a pot of tea, a can of energy drink and
# a bubble tea are not the same object however generous you are being.
#
# The rule at the top of this file has NOT changed. eCola and Sprunk are still one can,
# because a can is a can and the colour and the name carry the rest. What changed is that
# "a cup" had been asked to mean espresso, teapot, slush and orange juice all at once, and
# that is not generality, it is a shrug.
# ===========================================================================


def teapot():
    """A pot: round body, spout, handle, lid with a knob."""
    img, d = canvas()
    el(d, (60, 96, 196, 214))
    d.polygon([(s(56), s(122)), (s(16), s(166)), (s(30), s(188)), (s(64), s(152))], fill=WHITE)
    d.arc([s(174), s(112), s(242), s(186)], 300, 120, fill=WHITE, width=s(15))
    rr(d, (86, 76, 170, 102), 10)
    rr(d, (116, 54, 140, 82), 8)
    return img


def mug():
    """A handled mug with steam. Sits down, unlike the takeaway cup."""
    img, d = canvas()
    rr(d, (54, 92, 180, 224), 18)
    d.arc([s(158), s(112), s(230), s(192)], 300, 120, fill=WHITE, width=s(16))
    for x in (84, 122, 160):
        d.arc([s(x - 16), s(26), s(x + 16), s(80)], 200, 20, fill=WHITE, width=s(7))
    return img


def espresso():
    """A small cup on a saucer. The SAUCER is the read -- nothing else in the set has one."""
    img, d = canvas()
    rr(d, (78, 92, 172, 174), 12)
    d.arc([s(152), s(104), s(208), s(164)], 300, 120, fill=WHITE, width=s(12))
    el(d, (30, 178, 226, 224))
    return img


def juice():
    """A tall glass, a wedge of fruit on the rim, and a straw."""
    img, d = canvas()
    d.polygon([(s(76), s(76)), (s(178), s(76)), (s(166), s(230)), (s(88), s(230))], fill=WHITE)
    d.line([s(150), s(70), s(198), s(16)], fill=WHITE, width=s(11))
    d.pieslice([s(20), s(36), s(106), s(122)], 20, 200, fill=WHITE)
    d.pieslice([s(36), s(52), s(90), s(106)], 20, 200, fill=CLEAR)
    d.polygon([(s(88), s(104)), (s(167), s(104)), (s(163), s(128)), (s(92), s(128))], fill=CLEAR)
    return img


def slush():
    """A domed lid and a fat straw: a frozen drink, not a coffee."""
    img, d = canvas()
    d.polygon([(s(78), s(104)), (s(178), s(104)), (s(164), s(234)), (s(92), s(234))], fill=WHITE)
    d.pieslice([s(62), s(52), s(194), s(150)], 180, 360, fill=WHITE)
    rr(d, (58, 94, 198, 118), 8)
    d.polygon([(s(140), s(58)), (s(166), s(58)), (s(192), s(8)), (s(166), s(2))], fill=WHITE)
    return img


def bubbletea():
    """A sealed cup, a fat straw, and pearls in the bottom."""
    img, d = canvas()
    d.polygon([(s(74), s(74)), (s(182), s(74)), (s(168), s(234)), (s(88), s(234))], fill=WHITE)
    rr(d, (64, 60, 192, 84), 6)
    rr(d, (124, 4, 158, 72), 8)
    for cx, cy in ((104, 194), (134, 204), (160, 192), (118, 218), (148, 220)):
        el(d, (cx - 13, cy - 13, cx + 13, cy + 13), CLEAR)
    return img


def smoothie():
    """A short tumbler, a fruit half on the rim, a straw."""
    img, d = canvas()
    rr(d, (70, 96, 186, 232), 14)
    d.line([s(156), s(92), s(198), s(28)], fill=WHITE, width=s(11))
    el(d, (26, 40, 120, 128))
    el(d, (50, 64, 96, 104), CLEAR)
    d.polygon([(s(78), s(132)), (s(180), s(132)), (s(176), s(154)), (s(82), s(154))], fill=CLEAR)
    return img


def beer():
    """A stubby with a label band. Shorter neck than the wine bottle, on purpose."""
    img, d = canvas()
    rr(d, (110, 18, 146, 78), 8)
    d.polygon([(s(110), s(70)), (s(146), s(70)), (s(180), s(118)), (s(76), s(118))], fill=WHITE)
    rr(d, (76, 108, 180, 238), 14)
    rr(d, (70, 142, 186, 196), 6, CLEAR)
    return img


def wine():
    """A long neck, a sloped shoulder, and a capsule at the top."""
    img, d = canvas()
    rr(d, (112, 6, 144, 42), 6)
    rr(d, (116, 34, 140, 98), 4)
    d.polygon([(s(116), s(90)), (s(140), s(90)), (s(180), s(152)), (s(76), s(152))], fill=WHITE)
    rr(d, (76, 144, 180, 242), 12)
    rr(d, (70, 172, 186, 216), 6, CLEAR)
    return img


def water():
    """A ribbed bottle with a screw cap. The RIBS say water rather than beer."""
    img, d = canvas()
    rr(d, (104, 8, 152, 46), 8)
    rr(d, (112, 42, 144, 80), 4)
    d.polygon([(s(112), s(74)), (s(144), s(74)), (s(176), s(114)), (s(80), s(114))], fill=WHITE)
    rr(d, (80, 106, 176, 242), 14)
    for y in (142, 170, 198):
        rr(d, (74, y, 182, y + 11), 5, CLEAR)
    return img


def energy():
    """A tall slim can with a bolt on it. Narrower than the soda can, which is the difference."""
    img, d = canvas()
    rr(d, (92, 20, 164, 242), 16)
    rr(d, (86, 12, 170, 40), 8)
    d.polygon([(s(114), s(80)), (s(150), s(122)), (s(124), s(122)), (s(146), s(180)),
               (s(106), s(138)), (s(134), s(138))], fill=CLEAR)
    return img


def sub():
    """A long roll split down its length, filling showing. Not the round sandwich."""
    img, d = canvas()
    rr(d, (14, 94, 242, 178), 42)
    d.polygon([(s(30), s(130)), (s(226), s(130)), (s(214), s(152)), (s(42), s(152))], fill=CLEAR)
    for x in (74, 128, 182):
        el(d, (x - 17, 102, x + 17, 126), CLEAR)
    return img


def toastie():
    """Two triangles leaning together, with grill bars across them."""
    img, d = canvas()
    d.polygon([(s(18), s(198)), (s(126), s(198)), (s(72), s(70))], fill=WHITE)
    d.polygon([(s(124), s(214)), (s(240), s(214)), (s(182), s(82))], fill=WHITE)
    for i in range(3):
        d.line([s(38 + i * 13), s(192 - i * 6), s(94 + i * 13), s(120 - i * 8)],
               fill=CLEAR, width=s(7))
    return img


def calzone():
    """A folded half-moon, crimped along the straight edge, two vents. Also does the patty."""
    img, d = canvas()
    d.pieslice([s(22), s(58), s(234), s(254)], 180, 360, fill=WHITE)
    rr(d, (22, 184, 234, 218), 14)
    for i in range(8):
        x = 40 + i * 25
        el(d, (x - 11, 188, x + 11, 216), CLEAR)
    for x, y in ((102, 122), (152, 136)):
        rr(d, (x - 6, y, x + 6, y + 32), 6, CLEAR)
    return img


def platter():
    """
    A loaded plate: food ON the plate rather than merging into it.

    The first version drew three ellipses over a solid plate and they welded into one lump --
    at this size two white shapes touching are one white shape. Each piece now gets a slightly
    larger CLEAR shape punched behind it first, which leaves a hairline of plate showing round
    every one. That gap is the only reason the pile reads as separate things.
    """
    img, d = canvas()

    el(d, (10, 128, 246, 240))                       # the plate
    el(d, (30, 142, 226, 226), CLEAR)                # rim line
    el(d, (34, 146, 222, 222))

    for cx, cy, rx, ry in ((74, 150, 42, 40), (150, 136, 48, 44), (128, 190, 52, 30)):
        el(d, (cx - rx - 7, cy - ry - 7, cx + rx + 7, cy + ry + 7), CLEAR)
        el(d, (cx - rx, cy - ry, cx + rx, cy + ry))

    return img


def ramen():
    """A bowl with chopsticks standing in it and a tangle above the rim."""
    img, d = canvas()
    d.polygon([(s(142), s(16)), (s(160), s(20)), (s(132), s(128)), (s(116), s(124))], fill=WHITE)
    d.polygon([(s(176), s(24)), (s(194), s(30)), (s(148), s(132)), (s(132), s(126))], fill=WHITE)
    for i in range(2):
        d.arc([s(64 + i * 12), s(92 + i * 8), s(192 - i * 12), s(150 + i * 4)],
              200, 340, fill=WHITE, width=s(9))
    d.pieslice([s(26), s(100), s(230), s(248)], 0, 180, fill=WHITE)
    rr(d, (20, 106, 236, 134), 10)
    return img


def cupnoodle():
    """A pot noodle: straight tub, foil lid peeled back, fork standing in it."""
    img, d = canvas()
    d.polygon([(s(72), s(88)), (s(184), s(88)), (s(170), s(240)), (s(86), s(240))], fill=WHITE)
    rr(d, (62, 74, 194, 100), 8)
    d.polygon([(s(182), s(84)), (s(244), s(38)), (s(228), s(16)), (s(166), s(68))], fill=WHITE)
    rr(d, (112, 16, 132, 86), 6)
    return img


def readymeal():
    """A tray with compartments and a film lid pulled part way back."""
    img, d = canvas()
    rr(d, (22, 86, 234, 222), 16)
    rr(d, (44, 108, 122, 200), 8, CLEAR)
    rr(d, (136, 108, 212, 148), 8, CLEAR)
    rr(d, (136, 160, 212, 200), 8, CLEAR)
    d.polygon([(s(22), s(86)), (s(234), s(86)), (s(212), s(48)), (s(44), s(48))], fill=WHITE)
    return img


def corndog():
    """A battered dog on a STICK. The stick is all that separates it from a hot dog."""
    img, d = canvas()
    rr(d, (94, 18, 164, 178), 34)
    rr(d, (118, 162, 140, 248), 9)
    for y in (62, 102, 142):
        d.line([s(90), s(y), s(168), s(y - 18)], fill=CLEAR, width=s(6))
    return img


def donutbox():
    """A flat box with a window, and rings showing through it."""
    img, d = canvas()
    rr(d, (16, 94, 240, 218), 12)
    rr(d, (38, 112, 218, 180), 8, CLEAR)
    for cx in (78, 128, 178):
        el(d, (cx - 27, 118, cx + 27, 172))
        el(d, (cx - 11, 136, cx + 11, 156), CLEAR)
    d.polygon([(s(16), s(94)), (s(240), s(94)), (s(216), s(56)), (s(40), s(56))], fill=WHITE)
    return img


def baguette():
    """A long stick loaf, slashed on the diagonal."""
    img, d = canvas()
    d.polygon([(s(26), s(202)), (s(76), s(242)), (s(234), s(68)), (s(188), s(28))], fill=WHITE)
    el(d, (14, 188, 86, 250))
    el(d, (178, 18, 248, 84))
    for i in range(4):
        d.line([s(78 + i * 36), s(194 - i * 36), s(116 + i * 36), s(164 - i * 36)],
               fill=CLEAR, width=s(8))
    return img


def pipe():
    """A briar pipe: a bowl, and a stem curving away from it. Not the cigar."""
    img, d = canvas()
    d.polygon([(s(38), s(94)), (s(122), s(94)), (s(110), s(200)), (s(54), s(200))], fill=WHITE)
    el(d, (32, 78, 128, 114))
    el(d, (52, 90, 108, 106), CLEAR)
    d.arc([s(96), s(146), s(246), s(222)], 180, 330, fill=WHITE, width=s(18))
    rr(d, (214, 146, 250, 174), 8)
    return img


def cigbox():
    """A flip-top box, lid open, foil folded back. The soft pack keeps p_pack."""
    img, d = canvas()
    rr(d, (72, 84, 188, 242), 10)
    d.polygon([(s(72), s(94)), (s(188), s(94)), (s(204), s(44)), (s(88), s(44))], fill=WHITE)
    d.polygon([(s(98), s(88)), (s(178), s(88)), (s(188), s(58)), (s(108), s(58))], fill=CLEAR)
    rr(d, (66, 142, 194, 172), 6, CLEAR)
    return img


def noodleplate():
    """
    A plate of noodles with a fork standing in them.

    Same trap as the platter and the same fix: solid arcs laid straight onto a solid plate
    came out as one paddle-shaped blob. The noodles and the fork are each punched clear of
    their surroundings first, so every stroke has an edge.
    """
    img, d = canvas()

    el(d, (10, 130, 246, 240))                       # plate
    el(d, (30, 144, 226, 226), CLEAR)
    el(d, (34, 148, 222, 222))

    # A nest of noodles, cut out of the plate then drawn back in, so it sits ON it.
    el(d, (44, 108, 212, 208), CLEAR)
    for i in range(3):
        d.arc([s(52 + i * 16), s(116 + i * 12), s(204 - i * 16), s(196 - i * 8)],
              195, 345, fill=WHITE, width=s(11))

    # The fork, with its own clearance so it is not swallowed by the tangle.
    d.polygon([(s(168), s(24)), (s(206), s(30)), (s(174), s(166)), (s(146), s(160))], fill=CLEAR)
    d.polygon([(s(174), s(34)), (s(198), s(38)), (s(170), s(156)), (s(152), s(152))], fill=WHITE)
    for i in range(3):
        rr(d, (168 + i * 13, 14, 178 + i * 13, 48), 4)

    return img


def burrito():
    """A foil-wrapped cylinder with the paper peeled down one end."""
    img, d = canvas()
    d.polygon([(s(50), s(212)), (s(98), s(246)), (s(232), s(64)), (s(184), s(30))], fill=WHITE)
    el(d, (36, 190, 112, 252))
    d.polygon([(s(148), s(116)), (s(198), s(152)), (s(118), s(252)), (s(62), s(224))], fill=WHITE)
    for i in range(3):
        d.line([s(92 + i * 27), s(234 - i * 5), s(138 + i * 27), s(168 - i * 5)],
               fill=CLEAR, width=s(6))
    return img


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
    "shake": shake,
    "cone": cone,
    "donut": donut,
    "slice": slice,
    "noodles": noodles,
    "loaf": loaf,
    "carton": carton,
    "croissant": croissant,
    "muffin": muffin,
    "cigar": cigar,
    "sweetjar": sweetjar,
    "wrap": wrap,
    "cookie": cookie,

    "pizza": pizza,
    "pie": pie,
    "chicken": chicken,
    "bag": bag,
    "jerky": jerky,
    "sundae": sundae,
    "cake": cake,
    "dumpling": dumpling,
    "shot": shot,
    "egg": egg,

    "teapot": teapot,
    "mug": mug,
    "espresso": espresso,
    "juice": juice,
    "slush": slush,
    "bubbletea": bubbletea,
    "smoothie": smoothie,
    "beer": beer,
    "wine": wine,
    "water": water,
    "energy": energy,
    "sub": sub,
    "toastie": toastie,
    "calzone": calzone,
    "platter": platter,
    "ramen": ramen,
    "cupnoodle": cupnoodle,
    "readymeal": readymeal,
    "corndog": corndog,
    "donutbox": donutbox,
    "baguette": baguette,
    "pipe": pipe,
    "cigbox": cigbox,
    "noodleplate": noodleplate,
    "burrito": burrito,
}


def main():
    print("Writing product icons to " + OUT)

    for name, make in sorted(SHAPES.items()):
        base.save(make(), "p_" + name + ".png")

    print("Done, %d shapes. Deploy with:  .\\build.ps1 -Deploy -FreshData" % len(SHAPES))


if __name__ == "__main__":
    main()
