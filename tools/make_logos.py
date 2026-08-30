# -*- coding: utf-8 -*-
"""
Generates the shop brand marks for the counter header, into data/icons/brand_*.png.

Run:  python tools/make_logos.py

FULL COLOUR, UNLIKE EVERY OTHER ICON IN THIS MOD. The product and HUD icons are drawn
white on alpha and tinted at draw time, because a can of eCola and a can of Sprunk are
one drawing and two colours. A brand mark is the opposite: its colours ARE the brand,
and LTD's red is not a parameter. CustomSprite MULTIPLIES its Color with the texture,
so drawing these in colour and passing white leaves them exactly as painted.

Nothing here is traced from Rockstar's artwork -- these are typeset from Windows fonts
to read as the sign over the door at header size, which is about 40 pixels tall.
"""

import os

from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   "data", "icons")

FONTS = r"C:\Windows\Fonts"

# Supersampled and shrunk, same as the other two generators: PIL has no antialiased
# text at final size, and the letter edges are the whole job here.
SS = 4
W, H = 640, 200


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size * SS)


def tracked(d, text, f, cx, y, fill, track):
    """Draws text centred at cx with extra space between letters."""
    widths = [d.textlength(c, font=f) for c in text]
    total = sum(widths) + track * SS * (len(text) - 1)

    x = cx * SS - total / 2
    for c, w in zip(text, widths):
        d.text((x, y * SS), c, font=f, fill=fill)
        x += w + track * SS


def canvas():
    img = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def save(img, name):
    img = img.resize((W, H), Image.LANCZOS)
    path = os.path.join(OUT, name)
    img.save(path)
    print("  %-20s %d x %d" % (name, img.width, img.height))


# ===========================================================================

def ltd():
    """
    LTD Gasoline. Red wordmark, GASOLINE tracked out underneath.

    The real sign is red letters with a white keyline on navy. There is no navy here
    because the header behind it is already near-black, and a white keyline on a dark
    panel is what stops heavy red type from muddying into it.
    """
    img, d = canvas()

    red = (206, 32, 39, 255)
    keyline = (245, 245, 248, 255)
    grey = (198, 198, 206, 255)

    big = font("ariblk.ttf", 86)

    # One stroked pass rather than eight offset copies: offsets pile up unevenly around
    # tight letterforms like Arial Black's, and the L ends up looking hollow on one side.
    d.text((W / 2 * SS, 18 * SS), "LTD", font=big, fill=red, anchor="ma",
           stroke_width=4 * SS, stroke_fill=keyline)

    small = font("framd.ttf", 30)
    tracked(d, "GASOLINE", small, W / 2, 128, grey, 9)

    return img


def counter():
    """
    The fallback mark, for a till that is not a brand we know.

    Deliberately plain: it says COUNTER in the menu's own accent so that a shop with no
    entry in brands.json still gets a header that looks made rather than missing.
    """
    img, d = canvas()

    amber = (240, 170, 56, 255)
    f = font("framd.ttf", 62)

    tracked(d, "COUNTER", f, W / 2, 62, amber, 14)

    return img


LOGOS = {
    "brand_ltd.png": ltd,
    "brand_counter.png": counter,
}


def main():
    print("Writing brand marks to " + OUT)

    for name, make in sorted(LOGOS.items()):
        save(make(), name)

    print("Done, %d mark(s). Deploy with:  .\build.ps1 -Deploy -FreshData" % len(LOGOS))


if __name__ == "__main__":
    main()
