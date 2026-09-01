"""
Generates the small row glyphs for the settings menu: data/icons/s_*.png

Run from anywhere:  python tools/make_settings_icons.py

SEPARATE FROM make_icons.py ON PURPOSE. That script owns the two HUD meters, which are five
staged drawings apiece with a rim routine and a stage table that has been argued over at
length. These are flat pictograms with none of that, and putting them in the same file would
mean every future change to a drumstick reading past twelve unrelated glyphs to get there.

WHITE WITH ALPHA, like everything else in this folder, because CustomSprite.Color MULTIPLIES:
one white shape becomes light ink on a dark row and black ink on the amber highlight without
a second file. Bake a colour in and the row highlight turns it to mud.

NO RIM. The HUD icons carry a black outline because they sit on the world and the world is
whatever colour it likes. These sit on a menu row, which is one of two known colours, so an
outline would only fatten the shape and cost it detail at the size a row gives it -- about
thirty pixels.

DRAWN HEAVY. A glyph is about 30px on screen in a menu row. Hairlines vanish there, so every
stroke here is deliberately thicker than it looks right at 256.
"""

import math
import os

from PIL import Image, ImageChops, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "data", "icons")

SIZE = 256
SS = 4
W = SIZE * SS

WHITE = (255, 255, 255, 255)
CLEAR = (255, 255, 255, 0)

# The box every glyph is drawn inside, in 256-space. Kept well in from the edges so nothing
# touches the row's icon slot and every glyph reads as the same visual weight.
PAD = 34.0
BOX = (PAD, PAD, SIZE - PAD, SIZE - PAD)


def canvas():
    img = Image.new("RGBA", (W, W), CLEAR)
    return img, ImageDraw.Draw(img)


def s(v):
    return v * SS


def box(d, x0, y0, x1, y1, r=0.0):
    """A rectangle, optionally round-cornered."""
    if r > 0.0:
        d.rounded_rectangle([s(x0), s(y0), s(x1), s(y1)], radius=s(r), fill=WHITE)
    else:
        d.rectangle([s(x0), s(y0), s(x1), s(y1)], fill=WHITE)


def disc(d, cx, cy, r):
    d.ellipse([s(cx - r), s(cy - r), s(cx + r), s(cy + r)], fill=WHITE)


def hole(d, cx, cy, r):
    d.ellipse([s(cx - r), s(cy - r), s(cx + r), s(cy + r)], fill=CLEAR)


def ring(d, cx, cy, r, thick):
    disc(d, cx, cy, r)
    hole(d, cx, cy, r - thick)


def line(d, x0, y0, x1, y1, thick):
    d.line([s(x0), s(y0), s(x1), s(y1)], fill=WHITE, width=int(s(thick)))


def poly(d, pts):
    d.polygon([(s(x), s(y)) for x, y in pts], fill=WHITE)


# =========================================================================
# The glyphs
# =========================================================================


def power():
    """On/off: a broken ring with a bar through the gap."""
    img, d = canvas()
    cx, cy = 128, 134

    ring(d, cx, cy, 74, 20)

    # The gap at the top, cut wide enough that the bar sits in clear air.
    d.pieslice([s(cx - 80), s(cy - 80), s(cx + 80), s(cy + 80)],
               start=249, end=291, fill=CLEAR)

    box(d, cx - 10, cy - 96, cx + 10, cy - 18, r=10)
    return img


def clock():
    img, d = canvas()
    cx, cy = 128, 128

    ring(d, cx, cy, 84, 20)

    # Hands. Deliberately not at 12:00 -- a clock reading noon looks like a stopped one.
    box(d, cx - 9, cy - 54, cx + 9, cy + 8, r=9)
    box(d, cx - 6, cy - 8, cx + 52, cy + 10, r=8)
    return img


def run():
    """Speed: three chevrons leaning forward."""
    img, d = canvas()

    for i in range(3):
        x = 62 + i * 46
        poly(d, [(x, 48), (x + 34, 128), (x, 208), (x - 20, 208), (x + 14, 128),
                 (x - 20, 48)])
    return img


def heart():
    img, d = canvas()
    cx = 128

    disc(d, cx - 38, 104, 46)
    disc(d, cx + 38, 104, 46)
    poly(d, [(cx - 82, 118), (cx + 82, 118), (cx, 216)])
    return img


def bed():
    img, d = canvas()

    # Headboard, mattress, one leg each end.
    box(d, 32, 88, 50, 196, r=6)
    box(d, 32, 138, 224, 176, r=10)
    box(d, 206, 176, 224, 196, r=4)
    box(d, 32, 176, 50, 196, r=4)

    # Pillow and the hump of somebody in it.
    box(d, 62, 108, 112, 138, r=12)
    poly(d, [(116, 138), (168, 108), (214, 138)])
    return img


def car():
    img, d = canvas()

    # Cabin over body, two wheels.
    poly(d, [(74, 122), (98, 78), (158, 78), (188, 122)])
    box(d, 34, 118, 222, 162, r=16)
    disc(d, 78, 168, 26)
    disc(d, 178, 168, 26)
    hole(d, 78, 168, 11)
    hole(d, 178, 168, 11)
    return img


def cart():
    """Shops and prices: a basket on two wheels."""
    img, d = canvas()

    poly(d, [(56, 84), (218, 84), (192, 158), (82, 158)])
    box(d, 24, 62, 64, 82, r=8)
    disc(d, 96, 190, 20)
    disc(d, 178, 190, 20)
    return img


def pin():
    """A map marker."""
    img, d = canvas()
    cx = 128

    disc(d, cx, 108, 66)
    poly(d, [(cx - 52, 142), (cx + 52, 142), (cx, 222)])
    hole(d, cx, 104, 26)
    return img


def eye():
    """Visibility.

    THE LENS IS AN INTERSECTION OF TWO CIRCLES, built with masks and multiplied.

    Cutting a filled ellipse with two more ellipses was the obvious way and it does not
    work: each cut spans the full width, so it takes the whole top and bottom and leaves a
    band pinched in the middle -- a bowtie, not an eye. An intersection has the pointed
    corners and the fat middle the shape actually needs.
    """
    img, d = canvas()
    cx, cy = 128, 128

    # Circles this big, offset this far, leave a lens about 210 x 120.
    r, off = 150.0, 108.0

    # Two circles of radius r whose centres sit off apart, vertically.
    top = Image.new("L", (W, W), 0)
    ImageDraw.Draw(top).ellipse([s(cx - r), s(cy + off / 2.0 - r),
                                 s(cx + r), s(cy + off / 2.0 + r)], fill=255)

    bot = Image.new("L", (W, W), 0)
    ImageDraw.Draw(bot).ellipse([s(cx - r), s(cy - off / 2.0 - r),
                                 s(cx + r), s(cy - off / 2.0 + r)], fill=255)

    lens = ImageChops.multiply(top, bot)

    white = Image.new("RGBA", (W, W), WHITE)
    img.paste(white, (0, 0), lens)

    # THE PUPIL IS A HOLE, not a disc with a hole in it. The lens is already solid white,
    # so a white disc drawn on it is invisible and only the cut-out ever showed -- which is
    # why the first version had a pinhole where a pupil belonged.
    d = ImageDraw.Draw(img)
    hole(d, cx, cy, 32)
    return img


def layout():
    """Placement: a frame with a block parked in one corner."""
    img, d = canvas()

    box(d, 30, 46, 226, 210, r=14)
    d.rounded_rectangle([s(48), s(64), s(208), s(192)], radius=s(8), fill=CLEAR)
    box(d, 148, 122, 200, 184, r=6)
    return img


def bars():
    """The bar HUD: three columns at different levels."""
    img, d = canvas()

    for i, top in enumerate((64.0, 34.0, 96.0)):
        x = 52 + i * 56
        box(d, x, top, x + 34, 214, r=8)
    return img


def wave():
    """Movement and animation: a sine, drawn thick."""
    img, d = canvas()

    pts = []
    for i in range(97):
        u = i / 96.0
        x = 26 + u * 204
        y = 128 - math.sin(u * math.pi * 2.0) * 58
        pts.append((x, y))

    for i in range(len(pts) - 1):
        line(d, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1], 22)

    # The joins leave notches on the outside of each bend without these.
    for x, y in pts[::8]:
        disc(d, x, y, 11)
    return img


GLYPHS = {
    "s_power": power,
    "s_clock": clock,
    "s_run": run,
    "s_heart": heart,
    "s_bed": bed,
    "s_car": car,
    "s_cart": cart,
    "s_pin": pin,
    "s_eye": eye,
    "s_layout": layout,
    "s_bars": bars,
    "s_wave": wave,
}


def main():
    if not os.path.isdir(OUT):
        os.makedirs(OUT)

    for name, make in sorted(GLYPHS.items()):
        img = make().resize((SIZE, SIZE), Image.LANCZOS)
        img.save(os.path.join(OUT, name + ".png"))
        print("wrote %s.png" % name)

    print("%d glyphs -> %s" % (len(GLYPHS), OUT))


if __name__ == "__main__":
    main()
