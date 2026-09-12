# Art with nowhere to go yet

Three PixelLab batches that were made for something that does not exist in the mod yet.
They are kept HERE, in the repository, because `tools/pixelart-in/` is ignored and the
zips they came out of live in a Downloads folder that will be cleared one day.

| batch | candidates | what it is for |
| --- | --- | --- |
| `bong` | 16 | No bong item exists, and this build has **no bong prop** — a smoke item with no prop in his hand looks broken. This belongs in Hoodrich, beside the weed. |
| `ket` | 6 | Ketamine is not in Hoodrich's dose table, so the pocket has nothing to draw it for. It is a new drug in Hoodrich first — the same job LSD was — and then the picture comes back here as `drug:ketamine`. |
| `parcel` | 8 | Nothing in this mod is a parcel. Hoodrich's package art. |

## Putting one back to work

The folders are in the exact shape `tools/pixelart.py` expects, so restoring a batch to the
inbox is a copy and nothing else:

    cp -r tools/pixelart-keep/bong tools/pixelart-in/bong

Then the usual loop — `python tools/pixelart.py sheet ...` is only needed for NEW zips; these
are already unpacked and numbered, so go straight to binding:

    python tools/pixelart.py use item:<id> <N> bong

and add the line to `tools/pixelart-bind.py` so the decision is written down with the rest.
The `-sheet.png` beside each folder is the contact sheet, numbered the same way.
