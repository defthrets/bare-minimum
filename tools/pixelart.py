# -*- coding: utf-8 -*-
"""
PixelLab sprites into the mod's icons.

    python tools/pixelart.py sheet <group> <zip-or-png> [<zip-or-png> ...]
    python tools/pixelart.py use   <group> <N>
    python tools/pixelart.py list

SHEET unpacks every zip (PixelLab's export: one PNG under rotations/ plus metadata.json) or
takes bare PNGs, numbers them in the order given, and writes tools/pixelart-in/<group>-sheet.png
so a batch of fifteen candidate burgers can be looked at as one picture and one of them chosen
by its number. Nothing in the mod changes.

USE installs candidate N as data/icons/p_<group>.png: the sprite scaled by the LARGEST WHOLE
NUMBER that leaves an eight-pixel margin on a 256 canvas, NEAREST, centred. Whole numbers
because the game's sprite draw is bilinear and a 48-pixel sprite drawn at ninety on screen
goes to soup unless its pixels are already fat and square; 256 because that is what every
other product icon is, so the tiles neither know nor care which kind they got. The white
glyph it replaces goes to tools/icons-previous/ the first time, and only the first time, so
the original is always one copy away and never overwritten twice.

The mod draws these in their OWN colours -- see UI/Icon.Coloured -- so nothing here turns
them white. Groups are the icon field in data/foods.json; LIST prints them with counts.
"""
import io
import json
import os
import shutil
import sys
import zipfile

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ICONS = os.path.join(HERE, "data", "icons")
INBOX = os.path.join(HERE, "tools", "pixelart-in")
PREVIOUS = os.path.join(HERE, "tools", "icons-previous")

CANVAS = 256
MARGIN = 8


def groups():
    d = json.loads(io.open(os.path.join(HERE, "data", "foods.json"), encoding="utf-8-sig").read())
    out = {}
    for e in d["items"]:
        if isinstance(e, dict) and e.get("icon"):
            out.setdefault(e["icon"], []).append(e["name"])
    return out


def unpack(src, into):
    """One candidate's PNG, whatever it arrived as. Zips are PixelLab's; anything runnable
    inside one is refused rather than extracted."""
    os.makedirs(into, exist_ok=True)
    if src.lower().endswith(".png"):
        dst = os.path.join(into, os.path.basename(src))
        shutil.copyfile(src, dst)
        return dst
    with zipfile.ZipFile(src) as z:
        names = z.namelist()
        bad = [n for n in names if n.lower().endswith((".exe", ".dll", ".bat", ".cmd", ".ps1", ".scr", ".js", ".vbs"))]
        if bad:
            raise SystemExit("refusing %s: it contains %s" % (src, ", ".join(bad)))
        pngs = [n for n in names if n.lower().endswith(".png")]
        if not pngs:
            raise SystemExit("%s has no PNG in it" % src)
        # PixelLab puts the sprite under rotations/; take that one if there is a choice.
        pngs.sort(key=lambda n: ("rotations/" not in n, n))
        z.extract(pngs[0], into)
        return os.path.join(into, pngs[0])


def sheet(group, sources):
    inbox = os.path.join(INBOX, group)
    if os.path.isdir(inbox):
        shutil.rmtree(inbox)
    picks = []
    for i, src in enumerate(sources):
        png = unpack(src, os.path.join(inbox, str(i)))
        im = Image.open(png).convert("RGBA")
        picks.append((i, im))
        print("  %2d  %dx%d  %s" % (i, im.width, im.height, os.path.basename(src)))

    tile, pad = 192, 12
    w = pad + len(picks) * (tile + pad)
    h = pad + 24 + tile + pad
    out = Image.new("RGBA", (w, h), (28, 28, 32, 255))
    dr = ImageDraw.Draw(out)
    for i, im in picks:
        x = pad + i * (tile + pad)
        big = im.resize((tile, tile), Image.NEAREST)
        out.alpha_composite(big, (x, pad + 24))
        dr.text((x + 4, 6), str(i), fill=(240, 240, 246, 255))

    path = os.path.join(INBOX, "%s-sheet.png" % group)
    out.save(path)
    print("\n  %d candidate(s) -> %s" % (len(picks), os.path.relpath(path, HERE)))
    print("  then: python tools/pixelart.py use %s <N>" % group)
    return path


def items():
    d = json.loads(io.open(os.path.join(HERE, "data", "foods.json"), encoding="utf-8-sig").read())
    return {e["id"]: e for e in d["items"] if isinstance(e, dict) and e.get("id")}


def candidate(batch, n):
    folder = os.path.join(INBOX, batch, str(n))
    pngs = []
    for root, _, files in os.walk(folder):
        pngs += [os.path.join(root, f) for f in files if f.lower().endswith(".png")]
    if not pngs:
        raise SystemExit("no candidate %s in batch '%s' -- run sheet first" % (n, batch))
    return pngs[0]


def framed(png):
    """The sprite on the 256 canvas at the largest whole-number scale, NEAREST, centred."""
    im = Image.open(png).convert("RGBA")
    scale = max(1, (CANVAS - 2 * MARGIN) // max(im.width, im.height))
    big = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
    canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    canvas.alpha_composite(big, ((CANVAS - big.width) // 2, (CANVAS - big.height) // 2))
    return canvas, im, scale


def use(target, n, batch=None):
    """TARGET is an icon group (-> p_<group>.png, the group's shared picture) or an item id
    (-> i_<id>.png, that one item's own picture, which the mod prefers -- see Food/Art.cs).
    BATCH is the sheet the candidate came from; it defaults to the target, which is right for
    a group and usually wrong for an item, so: use <id> <N> <batch>."""
    known = groups()
    every = items()

    # AN ID CAN BE A GROUP'S NAME AS WELL. Bleeder Burger's id is "burger" and Taco Bomb's is
    # "taco" -- the first item in a group tends to have been named before the group was --
    # so a bare "burger" cannot say which is meant, and guessing put a group's shared
    # picture where one item's own should have gone. "item:burger" or "group:burger" says.
    want = None
    if target.startswith("item:"):
        want, target = "item", target[5:]
    elif target.startswith("group:"):
        want, target = "group", target[6:]
    elif target in known and target in every:
        raise SystemExit("'%s' is both an icon group and an item id. Say item:%s or group:%s."
                         % (target, target, target))

    batch = batch or target

    png = candidate(batch, n)
    canvas, im, scale = framed(png)

    if want == "item" and target not in every:
        raise SystemExit("no item with id '%s'" % target)
    if want == "group" and target not in known:
        raise SystemExit("no icon group '%s'" % target)

    if want != "item" and target in known:
        out = os.path.join(ICONS, "p_%s.png" % target)
        kept = os.path.join(PREVIOUS, "p_%s.png" % target)

        # THE FIRST TIME ONLY. A second use of the same group would otherwise archive the
        # pixel art it is replacing, over the white glyph that was the thing worth keeping.
        if os.path.exists(out) and not os.path.exists(kept):
            os.makedirs(PREVIOUS, exist_ok=True)
            shutil.copyfile(out, kept)
            print("  kept the old one at %s" % os.path.relpath(kept, HERE))

        who = "group of %d: %s" % (len(known[target]), ", ".join(known[target][:5]) +
                                    (" ..." if len(known[target]) > 5 else ""))
    elif target in every:
        out = os.path.join(ICONS, "i_%s.png" % target)
        who = "item: %s  (was sharing p_%s.png)" % (every[target]["name"], every[target].get("icon", "?"))
    else:
        raise SystemExit("'%s' is neither an icon group nor an item id in foods.json. "
                         "Try: python tools/pixelart.py list   or   items <group>" % target)

    canvas.save(out, optimize=True)
    print("  %-28s <- %s #%s  (%dx%d at x%d)  %s" % (
        os.path.relpath(out, HERE), batch, n, im.width, im.height, scale, who))


def main(argv):
    if len(argv) >= 3 and argv[0] == "sheet":
        sheet(argv[1], argv[2:])
    elif argv[:1] == ["use"] and len(argv) in (3, 4):
        use(argv[1], argv[2], argv[3] if len(argv) == 4 else None)
    elif argv[:1] == ["items"] and len(argv) == 2:
        for i in items().values():
            if i.get("icon") == argv[1]:
                print("  %-22s %-26s %s" % (i["id"], i["name"], (i.get("desc") or "")[:70]))
    elif argv[:1] == ["list"]:
        done = set()
        for g, names in sorted(groups().items(), key=lambda kv: -len(kv[1])):
            mark = "*" if os.path.exists(os.path.join(PREVIOUS, "p_%s.png" % g)) else " "
            print(" %s %-12s %3d" % (mark, g, len(names)))
        print("\n * = pixel art installed (the white glyph is in tools/icons-previous)")
    else:
        print(__doc__)
        sys.exit(2)


if __name__ == "__main__":
    main(sys.argv[1:])
