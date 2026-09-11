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
# Later lines win over earlier ones for the same target, so a rebind is a new line at the
# bottom with the reason beside it, not an edit of the old one; the history stays readable.
BIND = [
    # a_greasy_burger -- the first batch; these four were bound from the shell before this
    # file existed, and are written down here so the record is whole.
    ("burger", 7,  "Bleeder Burger"),         # the one dripping
    ("burger", 12, "Cart Burger"),            # plain single cheese, griddle food
    ("burger", 5,  "Old Yeller"),             # double cheese, yellow through and through
    ("burger", 11, "Cluckin' Burger"),        # tall bun, grilled patty, lettuce
    ("burger", 10, "Triple Burger"),          # the tallest
    ("burger", 2,  "Bacon Triple Cheese Melt"),  # the bacon
    ("burger", 1,  "Bone-In Burger"),         # sesame bun, dark bits nobody should think about
    ("burger", 3,  "Bison Burger"),           # dark bun, lean
    ("burger", 8,  "Money Shot"),             # double, fried egg, dripping: everything
    ("burger", 13, "Hang Ten Burger"),        # the charred-looking bun
    ("burger", 9,  "Sharkies Burger"),        # plain double (rebound below, to junk 0)
    ("burger", 6,  "group:burger"),           # the classic
    # taco
    ("taco", 2,  "Taco Farmer Taco"),         # lettuce and tomato -- grown
    ("taco", 5,  "Taco Bomb Taco"),           # red salsa, the chain's
    ("taco", 14, "Two Chicken Tacos"),        # chicken, lettuce
    ("taco", 8,  "Heavenly Taco"),            # the guacamole
    ("taco", 13, "Tacos al Pastor"),          # the pineapple
    ("taco", 3,  "Tacos Libres"),             # onion and coriander
    ("taco", 0,  "group:taco"),               # the plain one
    # plate_16_carne_asada_meat
    ("plate", 0,  "Beach Breakfast"),         # smiley pancake (rebound below, to plate2 4)
    ("plate", 1,  "Carne Asada Plate"),       # on the wooden board
    ("plate", 2,  "Mixed Grill"),             # the biggest cut
    ("plate", 3,  "All You Can Eat"),
    ("plate", 4,  "Mama's Meatloaf"),         # the gravy
    ("plate", 5,  "Mexican-American Combo"),
    ("plate", 7,  "Rack of Ribs"),            # orange rim
    ("plate", 8,  "Tasting Plate"),           # yellow rim, arranged
    ("plate", 9,  "Secondo"),                 # green rim
    ("plate", 10, "Plato Chido"),             # the patterned plate
    ("plate", 11, "Diner Plate"),             # mac and cheese on the side
    ("plate", 12, "Blue Plate"),              # blue rim, gravy over everything
    ("plate", 13, "Chef's Garden Plate"),
    ("plate", 14, "Grilled Fish Plate"),      # a steak (rebound below, to plate2 11)
    ("plate", 15, "Catch of the Day"),        # wooden board, seaside
    # beverages
    ("beverages", 0,  "Mint Tea"),            # teacup with lemon
    ("beverages", 1,  "Cup of Coffee"),       # black coffee
    ("beverages", 1,  "group:cup"),
    ("beverages", 3,  "Mount Whiskey"),       # decanter (rebound below, to alcohol 9)
    ("beverages", 3,  "group:flask"),
    ("beverages", 4,  "group:can"),           # the lemon can, no brand of ours
    ("beverages", 6,  "eCola"),               # cola bottle (rebound below, to junk 6, a can)
    ("beverages", 7,  "group:bubbletea"),     # the pearls
    ("beverages", 8,  "Bay Bar Pint"),        # the pint
    ("beverages", 8,  "group:beer"),
    ("beverages", 9,  "Pot of Tea"),          # the teapot
    ("beverages", 9,  "group:teapot"),
    ("beverages", 10, "Raine Water"),         # the water bottle
    ("beverages", 10, "group:water"),
    ("beverages", 11, "Hot Chocolate"),       # cream on top
    ("beverages", 11, "group:mug"),
    ("beverages", 12, "Carton of Milk"),
    ("beverages", 12, "group:carton"),
    ("beverages", 13, "Fresh Orange Juice"),
    ("beverages", 13, "group:juice"),
    ("beverages", 14, "Bottle of Wine"),      # a glass (rebound below, to alcohol 3)
    ("beverages", 14, "group:wine"),
    ("beverages", 17, "Bean Machine Coffee"), # the other black coffee
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
    # pack_of_smokes -- sixteen packs, no cigar and no pipe among them
    ("smokes", 8,  "Redwood"),                # red chevron pack: Redwood's own livery
    ("smokes", 9,  "group:pack"),             # an open pack, cigarettes showing
    ("smokes", 0,  "Debonaire"),              # gold pack with the crest
    ("smokes", 0,  "group:cigbox"),
    # alcohol -- seventeen bottles
    ("alcohol", 0,  "Pisswasser"),            # brown bottle, yellow label
    ("alcohol", 1,  "Logger Lager"),          # brown bottle, the other label
    ("alcohol", 16, "Tramway Beer"),          # the dark one
    ("alcohol", 3,  "Bottle of Wine"),        # a BOTTLE -- REBOUND off a glass; it is the item's name
    ("alcohol", 9,  "Mount Whiskey"),         # a labelled bottle -- REBOUND off a decanter; it is $15.99
    ("alcohol", 5,  "Ginger Beer"),           # the cloudy white bottle: "the proper cloudy kind"
    ("alcohol", 5,  "group:bottle"),
    ("alcohol", 3,  "group:wine"),            # the fallbacks follow the rebinds above
    ("alcohol", 9,  "group:flask"),
    # platter_9_Heavy_Shells_Sp -- sixteen boards and trays
    ("platter", 0,  "Mezze Plate"),           # the tray of dips, breads and olives
    ("platter", 1,  "Half Dozen Oysters"),    # oysters on a board -- the plate group's one gap, filled from here
    ("platter", 2,  "Heavy Shells"),          # tortilla chips, salsa, guac: shells and sides
    ("platter", 3,  "Fish and Chips"),        # in the paper, tartare on the side
    ("platter", 5,  "Small Plates"),          # the charcuterie board
    ("platter", 5,  "group:platter"),
    ("platter", 7,  "Seafood Platter"),       # crab, shrimp, corn, lemon
    ("platter", 8,  "Meat Pie"),              # a whole golden pie -- the Food pie, unpictured till now
    ("platter", 9,  "Spit-Roasted Beef"),     # sliced beef on the board, gravy
    ("platter", 10, "Pipeline Ribs"),         # the rack with the corn
    ("platter", 11, "Fisherman's Fry"),       # fried fish and shrimp with the dips
    ("platter", 15, "The Big Chief Special"), # the big round board with all of it
    # the drugs -- this mod's own pictures for the pocket; the other mod's files are untouched
    ("weed",  7, "drug:weed"),                # a clear baggie of bud
    ("drugs", 0, "drug:heroin"),              # the brown powder bag
    ("drugs", 1, "drug:meth"),                # the blue crystals bag
    ("drugs", 2, "drug:xanax"),               # the white bar
    ("pills", 4, "drug:ecstasy"),             # the pink smiley
    ("pills", 7, "drug:oxycodone"),           # the red oblong pill -- its first picture ever
    # hotdog -- sixteen dogs, no corn dog among them
    ("hotdog", 8, "Hot Dog"),                 # mustard zigzag, the plain one
    ("hotdog", 3, "Chilli Cheese Dog"),       # chilli and cheese
    ("hotdog", 1, "Chihuahua Dog"),           # loaded: peppers, onions, everything
    ("hotdog", 7, "Surf Dog"),                # relish and pickles
    ("hotdog", 4, "group:hotdog"),
    # food -- a mixed batch; what fits a gap is taken, the rest is spare
    ("food", 1,  "Club Sandwich"),            # triangles and toothpicks
    ("food", 3,  "New York Slice"),           # a slice on a plate
    ("food", 5,  "Fresh Fruit"),              # the fruit bowl
    ("food", 6,  "Slice of Apple Pie"),       # lattice slice
    ("food", 7,  "Slice of Pie"),             # the other slice
    ("food", 12, "Loaf of Bread"),            # sliced loaf
    ("food", 12, "group:loaf"),
    ("food", 13, "Rum Cake"),                 # the dark whole cake
    # milk_shake -- sixteen, and they cover three groups
    ("shake", 4,  "Milkshake"),               # vanilla, cherry, striped straw, the glass
    ("shake", 4,  "group:shake"),
    ("shake", 0,  "Iced Latte"),              # iced coffee, cream, plastic cup
    ("shake", 1,  "Frappe"),                  # caramel, whipped cream, in the cup
    ("shake", 2,  "Thick Shake"),             # the metal machine cup
    ("shake", 6,  "Jumbo Shake"),             # strawberry swirl, tall -- "strawberry, chocolate or vanilla"
    ("shake", 9,  "Cold Brew"),               # dark iced coffee in a glass
    ("shake", 3,  "Sludgie"),                 # the blue slush cup with the straw
    ("shake", 3,  "group:slush"),
    ("shake", 10, "Smoothie"),                # banana
    ("shake", 10, "group:smoothie"),
    ("shake", 14, "Berry Smoothie"),          # the dark red one
    ("shake", 15, "Wheatgrass Smoothie"),     # the green one
    # lsd -- one, four tabs of blotter
    ("lsd", 0, "drug:lsd"),
    # sub_sandwich -- seven, one for every sub and one over for the sandwiches
    ("sub", 4, "Foot-Long Sub"),              # the loaded veg sub on the long baguette
    ("sub", 4, "group:sub"),
    ("sub", 0, "Gut Buster"),                 # meatballs and cheese
    ("sub", 2, "Torpedo"),                    # the cheesesteak
    ("sub", 3, "Italian Sub"),                # salami and ham on the baguette
    ("sub", 1, "Crab Roll"),                  # fried seafood in a roll
    ("sub", 5, "Torta"),                      # ham and cheese on the pale seeded roll
    ("sub", 6, "Pastrami Combo"),             # pastrami piled on a round roll -- a sandwich, from the sub batch
    # grocery_bag -- four bags
    ("grocery", 0, "Bag of Groceries"),       # the plastic carrier, full
    ("grocery", 2, "Bag of Produce"),         # paper bag with handles
    ("grocery", 1, "group:bag"),              # plain paper bag; the Organic Veg Box stays white -- it is a box
    # bakery_produce -- eight
    ("bakery", 0, "item:pt_garlic_bread"),    # the baguette -- two items are called Garlic Bread, so by id
    ("bakery", 0, "group:baguette"),
    ("bakery", 1, "Morning Pastry"),          # a croissant, plain
    ("bakery", 3, "Sourdough Loaf"),          # sliced loaf
    ("bakery", 4, "Sprinkle Ring"),           # glazed, sprinkles
    ("bakery", 5, "Golden Bun"),              # the cinnamon roll
    ("bakery", 6, "Bagel"),                   # poppy seed
    ("bakery", 6, "group:bagel"),
    # fast_food_meal -- twenty trays; only where the meal IS the item
    ("fastfood", 0,  "Chow Mein Box"),        # the takeaway carton, with the drink and the roll
    ("fastfood", 2,  "Fresh Wrap"),           # wrap, water, salad
    ("fastfood", 6,  "Aguila Burrito"),       # burrito, guac, cola
    ("fastfood", 11, "Lucky Plucker Box"),    # red basket of nuggets on the check paper
    ("fastfood", 14, "Wing Box"),             # fried chicken pieces, slaw, drink
    # junk_food, the second half (16-32)
    ("junk", 16, "Geronimo Wings"),           # a fried drumstick -- the wing item's nearest thing
    ("junk", 17, "Meteorite Bar"),            # red wrapper
    ("junk", 11, "Ego Chaser Bar"),           # the blue wrapper, which had been the group's fallback
    ("junk", 18, "Liberty Style Slice"),      # a slice
    ("junk", 19, "Donut"),                    # pink glazed
    ("junk", 19, "group:donut"),
    ("junk", 25, "Ice Cream"),                # soft serve cone
    ("junk", 25, "group:cone"),
    ("junk", 28, "Big Cookie"),               # three cookies
    # street_drugs -- ten; the two the pocket still borrowed from the other mod
    ("street", 4, "drug:coke"),               # the pale powder bag
    ("street", 8, "drug:crack"),              # the bag of white rocks
]


def main():
    d = json.loads(io.open(os.path.join(HERE, "data", "foods.json"), encoding="utf-8-sig").read())
    by_name = {}
    for e in d["items"]:
        if isinstance(e, dict) and e.get("id") and e.get("name"):
            by_name.setdefault(e["name"].lower(), []).append(e["id"])

    plan = []
    for batch, n, target in BIND:
        if target.startswith("group:") or target.startswith("drug:"):
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
