# -*- coding: utf-8 -*-
"""
The models you could put in a man's hand, out of the game's own list.

    python tools/props.py

WHY THIS FILE EXISTS. The fitting bench can only offer models it knows the names of, and
until now that was "whatever something in foods.json already holds" -- about sixty. That is
fine for swapping a burger for a taco and useless the moment the right model is one nothing
else uses: the Pizza Slice holds a closed pizza BOX, and the six pizza models this game
actually has were not on the list to choose from.

WHERE THE NAMES COME FROM. menyooStuff/PropList.txt, which is every object name in this
install -- see reference_gtav_name_lists. Nothing here is invented and nothing is guessed;
this script only DECIDES WHICH of them are worth offering, and the mod checks every one
against the build before it spawns it anyway.

WHAT IS KEPT. Anything whose name reads like food, drink or packaging, minus the furniture
those words also appear in: a pizza oven, a drinks machine, a bread rack and a sign are not
things you hold. It errs towards keeping: a wrong entry costs one press of left or right on
the bench, and a missing one costs somebody the model they were looking for.
"""
import io
import os
import re

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

LIST = (r"C:/Program Files (x86)/Steam/steamapps/common/"
        r"Grand Theft Auto V Enhanced/menyooStuff/PropList.txt")

OUT = os.path.join(HERE, "data", "props.txt")

# Reads like something edible, drinkable, or the packaging one comes in.
WORDS = (
    "pizza", "burger", "burg", "taco", "sandwich", "sandw", "hotdog", "hot_dog", "donut",
    "doughnut", "bagel", "bread", "toast", "cake", "pie", "noodle", "rice", "sushi", "chip",
    "fries", "crisp", "candy", "choc", "sweet", "cookie", "cereal", "egg", "fish", "meat",
    "steak", "chicken", "salad", "fruit", "apple", "orange", "banana", "nana", "aple",
    "ornge", "veg", "soup", "bowl", "plate", "tray", "cup", "mug", "glass", "bottle",
    "beer", "wine", "whisky", "coffee", "tea", "milk", "juice", "soda", "drink", "food",
    "meal", "snack", "sauce", "ketchup", "mustard", "kebab", "wrap", "burrito", "pretzel",
    "popcorn", "icecream", "ice_cream", "cone", "lolly", "jar", "tin", "packet", "water",
    "shake", "ciggy", "cigar", "joint", "pipe", "bong",
)

# The furniture those same words live in.
SKIP = (
    "sign", "stand", "oven", "fridge", "machine", "rack", "shelf", "bin", "counter",
    "table", "chair", "cabinet", "door", "wall", "light", "screen", "poster", "banner",
    "menu", "trolley", "cart", "crate", "pallet", "dispenser", "freezer", "grill", "fryer",
    "toaster", "kettle", "blender", "microwave", "urn", "barrel", "keg", "cooler", "vend",
    "window", "roof", "floor", "stairs", "fence", "pillar", "beam", "curtain", "carpet",
    "mirror", "lamp", "clock", "radio", "tv_", "speaker", "sofa", "bed", "desk", "locker",
    "sink", "tap", "bath", "shower", "toilet", "washer", "dryer", "aircon", "duct",
    "glassfix", "generic_water", "waterfall", "fountain", "pool", "pond", "tank", "pump",
    "candle", "dec_plate", "mansion", "_col", "_lod", "_dam", "_fix",
)


def holdable(name):
    low = name.lower()

    if not any(w in low for w in WORDS): return False
    if any(s in low for s in SKIP): return False

    return True


def main():
    names = [l.strip() for l in io.open(LIST, encoding="utf-8", errors="replace") if l.strip()]
    keep = sorted({n for n in names if holdable(n)})

    io.open(OUT, "w", encoding="utf-8", newline="\n").write(
        "; Models the fitting bench can put in his hand, taken from the game's own list by\n"
        "; tools/props.py. Every name here exists in a stock install; the mod checks each one\n"
        "; against the build before it spawns it anyway.\n"
        ";\n"
        "; " + str(len(keep)) + " of " + str(len(names)) + " objects.\n\n" +
        "\n".join(keep) + "\n")

    print("  %d of %d kept -> %s" % (len(keep), len(names), os.path.relpath(OUT, HERE)))

    for word in ("pizza", "cup", "burger", "bottle"):
        n = len([k for k in keep if word in k.lower()])
        print("     %-8s %d" % (word, n))


if __name__ == "__main__":
    main()
