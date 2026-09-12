# -*- coding: utf-8 -*-
"""
The food, on one picture, for the mod page.

    python tools/showcase.py

Writes release/showcase-food.png: three rows by aisle -- Food, Snacks, Drinks -- of the drawn
items with their menu names under them, on the same dark ground the shop screen uses. Built
from data/icons/i_<id>.png, so it shows what the mod actually ships and nothing it does not;
a picture that is not drawn yet is simply not on it. Drugs are left off on purpose: mod sites
are particular about what a listing image may show, and a food mod's page should not be the
place that finds out.
"""
import io
import json
import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ICONS = os.path.join(HERE, "data", "icons")
OUT = os.path.join(HERE, "release", "showcase-food.png")

# Curated: the clearest silhouettes, a spread of every kind. By item id.
ROWS = [
    ("FOOD", [
        "burger", "ua_baconmelt", "farmer_taco", "chido_tacos", "hotdog", "chilli_dog",
        "deli_sub", "bite_gutbuster", "sandwich", "burrito", "pizza_slice", "wok_chow",
        "mp_plate", "dn_breakfast", "chilli", "nx_ramen", "ad_pasta", "mg_meatloaf",
        "pl_oysters", "bp_fishchips", "pi_ribs", "fm_rotisserie", "fowl_bucket", "pl_platter",
    ]),
    ("SNACKS", [
        "coop_donut", "rb_dozen", "ice_cream", "cb_cookie", "crisps", "ua_fries",
        "wig_rings", "boiled_sweets", "toffees", "choc_meteorite", "bagel", "gb_bun",
        "tb_pastry", "jazz_dessert", "cf_rumcake", "noodles", "mg_pie", "beans_bread",
        # Not the Redwood pack: its art riffs on a real cigarette brand's livery, and a
        # listing image is the wrong place to test a mod site's patience with that.
        "cluck_wings", "cig_debonaire", "kiosk_sundae", "cf_patty", "rb_sprinkle", "fruit",
    ]),
    ("DRINKS", [
        "rb_coffee", "bm_latte", "cb_coldbrew", "coop_shake", "cb_frappe", "sludgie",
        "lm_orange", "bite_smoothie", "lm_smoothie", "beer", "bb_pint", "wine",
        "whiskey", "ecola", "sprunk", "water", "gp_tea", "sm_tea",
        "nx_bubbletea", "beans_milk", "gp_hotchoc", "cf_gingerbeer", "chido_horchata", "dn_icedtea",
    ]),
]

# Layout, in pixels. 1920 wide, which is what every mod site's gallery is happiest with.
W = 1920
MARGIN = 60
PER_ROW = 12
TILE = 132            # the picture
CELL_W = (W - 2 * MARGIN) // PER_ROW
CELL_H = TILE + 46    # picture + name
ROW_GAP = 34
HEAD_H = 170
FOOT_H = 70

BG = (22, 22, 26, 255)
PLATE = (34, 34, 40, 255)
RAIL = (245, 175, 55, 255)          # the mod's amber
TEXT = (240, 240, 246, 255)
DIM = (150, 150, 160, 255)


def font(size, bold=False):
    for name in (["bahnschrift.ttf", "segoeuib.ttf", "arialbd.ttf"] if bold else
                 ["bahnschrift.ttf", "segoeui.ttf", "arial.ttf"]):
        p = os.path.join("C:/Windows/Fonts", name)
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except OSError:
                pass
    return ImageFont.load_default()


def names():
    d = json.loads(io.open(os.path.join(HERE, "data", "foods.json"), encoding="utf-8-sig").read())
    return {e["id"]: e["name"] for e in d["items"] if isinstance(e, dict) and e.get("id")}


def main():
    by_id = names()
    rows = []
    for title, ids in ROWS:
        got = [(i, by_id.get(i, i)) for i in ids if os.path.exists(os.path.join(ICONS, "i_%s.png" % i))]
        missing = [i for i in ids if not os.path.exists(os.path.join(ICONS, "i_%s.png" % i))]
        if missing:
            print("  not drawn yet, left off %s: %s" % (title, ", ".join(missing)))
        rows.append((title, got))

    lines = sum((len(got) + PER_ROW - 1) // PER_ROW for _, got in rows)
    H = HEAD_H + lines * CELL_H + len(rows) * (ROW_GAP + 40) + FOOT_H

    im = Image.new("RGBA", (W, H), BG)
    dr = ImageDraw.Draw(im)

    # ---- the head: the name, set wide the way the menu sets it, and one line under it ----
    title = font(64, bold=True)
    x = MARGIN
    for ch in "BARE MINIMUM":
        dr.text((x, 44), ch, font=title, fill=TEXT)
        x += dr.textlength(ch, font=title) + (14 if ch != " " else 22)
    dr.rectangle([MARGIN, 124, MARGIN + 160, 128], fill=RAIL)
    total = sum(len(g) for _, g in rows)
    drawn = len([f for f in os.listdir(ICONS) if f.startswith("i_") and f[2:-4] in by_id])
    dr.text((MARGIN, 136), "Hunger, thirst and sleep for GTA V.  %d of the 201 things you can buy have a "
                           "picture of their own. Here are %d of them." % (drawn, total),
            font=font(24), fill=DIM)

    # ---- the rows ----
    y = HEAD_H
    label = font(26, bold=True)
    name_font = font(19)

    for title, got in rows:
        dr.text((MARGIN, y), title, font=label, fill=RAIL)
        dr.rectangle([MARGIN + dr.textlength(title, font=label) + 16, y + 16, W - MARGIN, y + 17],
                     fill=(60, 60, 68, 255))
        y += 40

        for k, (iid, nm) in enumerate(got):
            r, c = divmod(k, PER_ROW)
            cx = MARGIN + c * CELL_W
            cy = y + r * CELL_H

            # A quiet plate under each picture, the shop tile's own dark grey.
            dr.rounded_rectangle([cx + 6, cy, cx + CELL_W - 6, cy + TILE + 6], radius=10, fill=PLATE)

            icon = Image.open(os.path.join(ICONS, "i_%s.png" % iid)).convert("RGBA")
            icon = icon.resize((TILE - 16, TILE - 16), Image.NEAREST)
            im.alpha_composite(icon, (cx + (CELL_W - (TILE - 16)) // 2, cy + 11))

            # The name, centred, shrunk if it will not fit the cell.
            f = name_font
            while dr.textlength(nm, font=f) > CELL_W - 14 and f.size > 13:
                f = font(f.size - 1)
            tw = dr.textlength(nm, font=f)
            dr.text((cx + (CELL_W - tw) / 2, cy + TILE + 14), nm, font=f, fill=TEXT)

        y += ((len(got) + PER_ROW - 1) // PER_ROW) * CELL_H + ROW_GAP

    # ---- the foot ----
    dr.text((MARGIN, H - FOOT_H + 14),
            "Every shop, every item and every picture is a plain file you can edit.  spitmux.me",
            font=font(20), fill=DIM)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    im.convert("RGB").save(OUT, optimize=True)
    print("  %s  %dx%d  %d items" % (os.path.relpath(OUT, HERE), W, H, total))


# ====================================================================== the drugs
#
# A SEPARATE PICTURE, ON PURPOSE. The food poster can go on any mod site's gallery; this one
# goes where the site allows it. Keeping them apart means the food one never has to come down
# because of what is on the other.
DRUGS = [
    ("weed", "Weed"), ("meth", "Meth"), ("coke", "Cocaine"), ("crack", "Crack"),
    ("ecstasy", "Ecstasy"), ("lsd", "LSD"), ("xanax", "Xanax"), ("heroin", "Heroin"),
    ("oxycodone", "Oxycodone"),
]

OUT_DRUGS = os.path.join(HERE, "release", "showcase-drugs.png")


def drugs():
    got = [(i, n) for i, n in DRUGS if os.path.exists(os.path.join(ICONS, "i_%s.png" % i))]

    tile = 176
    per_row = len(got)
    cell_w = (W - 2 * MARGIN) // per_row
    cell_h = tile + 52
    H = HEAD_H + 40 + cell_h + 30 + FOOT_H

    im = Image.new("RGBA", (W, H), BG)
    dr = ImageDraw.Draw(im)

    title = font(64, bold=True)
    x = MARGIN
    for ch in "BARE MINIMUM":
        dr.text((x, 44), ch, font=title, fill=TEXT)
        x += dr.textlength(ch, font=title) + (14 if ch != " " else 22)
    dr.rectangle([MARGIN, 124, MARGIN + 160, 128], fill=RAIL)
    dr.text((MARGIN, 136), "The pocket's own pictures for the nine drugs, drawn to sit beside the food. "
                           "What they do to him comes from Hoodrich, over the bridge.",
            font=font(24), fill=DIM)

    y = HEAD_H
    label = font(26, bold=True)
    dr.text((MARGIN, y), "THE POCKET", font=label, fill=RAIL)
    dr.rectangle([MARGIN + dr.textlength("THE POCKET", font=label) + 16, y + 16, W - MARGIN, y + 17],
                 fill=(60, 60, 68, 255))
    y += 40

    name_font = font(22)
    for k, (iid, nm) in enumerate(got):
        cx = MARGIN + k * cell_w
        dr.rounded_rectangle([cx + 6, y, cx + cell_w - 6, y + tile + 6], radius=12, fill=PLATE)
        icon = Image.open(os.path.join(ICONS, "i_%s.png" % iid)).convert("RGBA")
        icon = icon.resize((tile - 20, tile - 20), Image.NEAREST)
        im.alpha_composite(icon, (cx + (cell_w - (tile - 20)) // 2, y + 13))
        tw = dr.textlength(nm, font=name_font)
        dr.text((cx + (cell_w - tw) / 2, y + tile + 16), nm, font=name_font, fill=TEXT)

    dr.text((MARGIN, H - FOOT_H + 14),
            "Comedowns, blackouts and a twelve-minute trip. Sold by Hoodrich; carried, eaten and paid for here.  spitmux.me",
            font=font(20), fill=DIM)

    im.convert("RGB").save(OUT_DRUGS, optimize=True)
    print("  %s  %dx%d  %d drugs" % (os.path.relpath(OUT_DRUGS, HERE), W, H, len(got)))


if __name__ == "__main__":
    main()
    drugs()
