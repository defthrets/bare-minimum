A point release over 0.4.0. One fix.

## What is fixed

**The game's own health and armour strip could come back under the map and stay.** The layout that hides it was asked for once and then re-asked only on a handful of moments — a pause, a fade, a character switch, a cutscene — and the game undid it on a moment that was none of those, with no re-ask to follow. The log for the session shows it plainly: the ask, the rebuild, then nothing for three minutes while the strip was back on screen.

**Six more moments are watched now**, and every re-ask is written to the log *with the moment that caused it*, so if it ever comes back again the log names the trigger instead of leaving it to guesswork. In and out of a vehicle, the HUD hidden and given back (a menu, the phone, the weapon wheel), the wanted level changing, an interior boundary, a phone call ending.

**And a quiet heartbeat** — every five seconds, only while no waypoint is set. Asking every frame was what made the GPS route strobe, but with no route on the map there is nothing to disturb, so the ask is free. It puts a five-second ceiling on how long the strip can stay back whenever you are not navigating, and it stands down the moment a waypoint goes up, so the strobe cannot return. A mission's own route is not a waypoint and cannot be asked about, so during one the route may blink once every five seconds — a single frame, not sixty a second.

**Nothing else changed.** If you have 0.4.0 and the strip has not come back yet, this is not worth the download — but it will, and then it is.

## Which zip

- **`BareMinimum-0.4.1.zip`** — the mod on its own. Take this one if you already run other SHVDN mods.
- **`BareMinimum-0.4.1-full.zip`** — the same, with ScriptHookVDotNet bundled alongside it. Take this one if this is your first script mod.

Neither contains **ScriptHookV**, which is Alexander Blade's and has to come from his site.

## Installing

Unzip and drop the contents into your GTA V folder — the `scripts` folder merges with the one already there.

**Unblock the ZIP before you extract it**, or ScriptHookVDotNet refuses to load the DLL and says nothing about it.

Everything is in `BareMinimum.ini`, and most of it is also on the in-game menu (**F11**), live. Your pocket is **F12**, and **E** is everything else.

spitmux.me
