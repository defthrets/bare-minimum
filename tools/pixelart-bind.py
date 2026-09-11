# -*- coding: utf-8 -*-
"""
A batch of bindings by NAME, in one go.

    python tools/pixelart-bind.py

Edit the table, run it. Each line is: what is on the sheet -> which item (by the name on the
menu) or which group's fallback. Names rather than ids because the sheet is read by a person
and a person knows "Clam Chowder", not "bp_chowder". Resolved against data/foods.json, and an
unknown name stops the run before anything is written, so a typo cannot bind a picture to
nothing.

This file is the record of what went where, which is why it is committed and the inbox is
not: the zips can be regenerated, the decisions cannot.
"""
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(HERE, "tools"))
import pixelart  # noqa: E402

# (batch, N, target) -- target is an item's menu name, or "group:<icon>" for a fallback.
BIND = [
    # bowl_of_food
    ("bowl", 12, "Ring of Fire Chilli"),      # red chilli, sour cream on top -- the cooling for the fire
    ("bowl", 8,  "Homemade Soup"),            # tomato soup, cream swirl, crouton
    ("bowl", 0,  "Bowl of Menudo"),           # red stew with the white hominy in it
    ("bowl", 3,  "Ancient Grain Bowl"),       # grains and berries
    ("bowl", 7,  "Crazy Horse Soup"),         # gumbo: shrimp, sausage, nobody will say what else
    ("bowl", 6,  "Harvest Bowl"),             # poke-style bowl, tuna, edamame, avocado
    ("bowl", 1,  "Antipasto Salad"),          # green salad, tomatoes
    ("bowl", 9,  "Primo"),                    # spaghetti, parmesan
    ("bowl", 11, "Clam Chowder"),             # white chowder, clams
    ("bowl", 10, "Wonton Soup"),              # clear noodle soup
    ("bowl", 8,  "group:bowl"),
    # plate_of_food
    ("plate2", 0,  "Hearty Taco Plate"),      # three tacos on the patterned plate
    ("plate2", 1,  "Chocolate Torte"),        # cake slice, berries, sauce
    ("plate2", 3,  "Special Lunch Menu"),     # sushi and sides on a plate
    ("plate2", 4,  "Beach Breakfast"),        # eggs, bacon, beans, toast -- REBOUND off the smiley pancake
    ("plate2", 5,  "Rotisserie Chicken"),     # whole roast chicken on a platter
    ("plate2", 6,  "Lodge Stew"),             # stew with bread -- REBOUND, it is the item's own description
    ("plate2", 7,  "Almond Croissant"),       # croissant and jam
    ("plate2", 7,  "group:croissant"),
    ("plate2", 8,  "Heart Stopper"),          # steak, fries, tomatoes on a slate
    ("plate2", 9,  "Mom's Pie"),              # whole pie, one slice out
    ("plate2", 11, "Grilled Fish Plate"),     # salmon and asparagus -- REBOUND off a steak
    ("plate2", 12, "Bowl of Ramen"),          # ramen, egg, pork, nori, chopsticks
    ("plate2", 12, "group:ramen"),
    ("plate2", 13, "Pasta of the Day"),       # spaghetti and meatballs -- REBOUND off mac and cheese
    # bucket_of_fried
    ("bucket", 3,  "Fowl Mouthed Bucket"),    # red FRIED! bucket -- brash name, brash bucket
    ("bucket", 2,  "Shrimp Basket"),          # paper cone of fried shrimp
    ("bucket", 6,  "Hookies Basket"),         # wicker basket, fish, shrimp, lemon
    ("bucket", 10, "Cluckin' Bucket"),        # the striped chicken bucket
    ("bucket", 0,  "group:bucket"),
    ("bucket", 7,  "French Fries"),           # paper cup of fries
    ("bucket", 9,  "Rings of Fire"),          # tin of onion rings with dip
    # junk_food
    ("junk", 0,  "Sharkies Burger"),          # burger in a paper wrap -- REBOUND, it is the item's own line
    ("junk", 3,  "Pizza Slice"),              # a slice on a paper plate
    ("junk", 4,  "Instant Noodles"),          # spicy noodle cup
    ("junk", 6,  "eCola"),                    # red can -- REBOUND off a bottle; it is a can in the game
    ("junk", 9,  "group:chips"),              # bag of tortilla chips, the fallback for the crisps aisle
    ("junk", 10, "Sprunk"),                   # the green bottle with the Z -- REBOUND, brand colour over vessel
    ("junk", 11, "group:bar"),                # a chocolate bar in its wrapper, for all four candy bars
    ("junk", 12, "Box of a Dozen"),           # open box of donuts
    ("junk", 13, "Bag of Crisps"),            # BBQ crisps, the red bag
    ("junk", 14, "Bag of Toffees"),           # bag of gummy sweets
    ("junk", 15, "Boiled Sweets"),            # lollipops: boiled sugar on a stick
]


def main():
    d = json.loads(io.open(os.path.join(HERE, "data", "foods.json"), encoding="utf-8-sig").read())
    by_name = {}
    for e in d["items"]:
        if isinstance(e, dict) and e.get("id") and e.get("name"):
            by_name.setdefault(e["name"].lower(), []).append(e["id"])

    plan = []
    for batch, n, target in BIND:
        if target.startswith("group:"):
            plan.append((batch, n, target))
            continue
        ids = by_name.get(target.lower())
        if not ids:
            raise SystemExit("no item named %r in foods.json" % target)
        if len(ids) > 1:
            raise SystemExit("%r names %d items (%s); use the id" % (target, len(ids), ", ".join(ids)))
        plan.append((batch, n, "item:" + ids[0]))

    for batch, n, target in plan:
        pixelart.use(target, str(n), batch)


if __name__ == "__main__":
    main()
