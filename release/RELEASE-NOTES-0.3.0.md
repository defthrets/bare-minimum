Hunger, thirst and sleep on the game's own clock, with health, armour and energy standing in the same row. Six bars beside the minimap, a frame round it, and the street you are on written above it.

## What is new in 0.3.0

**Ten languages, off the first row of the settings menu.** English in British and American, Português (BR), Español, Français, Deutsch, Русский, Polski, 中文 and हिन्दी. Every prompt, every screen, every tab and every row label.

**Anything a translation has not covered shows in English**, and that is the design rather than a shortfall. A language file is a plain map from what the mod says to what it should say instead — there are no identifiers in it, so a miss hands back the English and there is no such thing as a missing string. A half-finished file is a working mod in two languages rather than a broken one with blanks in it.

Which also means the food translates the same way the buttons do: a hot dog's name and the joke under it go through the same code as everything else, so filling them in is editing a text file rather than building anything. `lang\en-GB.json` is the template — copy it, fill in the right-hand side, save it under your own code. No rebuild, no tools.

**Whether the letters draw is a separate question from whether the words exist**, and the log says which at load. Latin and Cyrillic are in the game's own fonts always, so six of the ten are safe on any install. Han rides in on the font GTA loads for *its* own language, so Chinese on an English game is likely to come out as empty boxes. Devanagari is not in the game at all — there has never been a Hindi GTA V — so Hindi almost certainly will be. The text is real either way and will draw the day a font with those letters is installed.

**The air meter is back.** The game's own breath bar is drawn in the same part of the minimap as its health and armour strip, so the layout that hides those was hiding it too — and it took going swimming to find out. It cannot be let back on its own, because one layout covers all three, so it is drawn instead, in exactly the space the original used and only while you are under the water. The full mark is measured rather than assumed, so a diving ability or a rebreather is accounted for without being told about. It beats when it is nearly out, and faster the less is left.

**The HUD stopped blinking.** The whole row was going off and on, everywhere, all the time. Four files each asked the game whether its HUD was hidden, each asked fresh every time it drew, and each took everything down the instant the answer came back no — and that is a *this frame* native, the one the game uses for cutscenes and phone calls and the one any other script can call too. One script doing it on one frame in ten took the bars down on one frame in ten. There is one answer now, asked once a frame, and the twitchy tests have to hold for six frames before anything goes.

**The minimap stopped flickering the GPS.** The call that hides the game's health strip is not a switch being flipped, it is a re-layout of the minimap, and it was being made sixty times a second. The map's own picture survives that; the GPS route and the turn arrow do not, because the movie builds them only while a route is set. Asked once now, and again only when something has had a chance to undo it.

**And the hot dogs were being drunk.** Four of them carried an animation whose name says eat, which sits in the eating family, and which is a bottle going to the mouth. Five bar drinks had the opposite problem and were being chewed.

## Which zip

- **`BareMinimum-0.3.0.zip`** — the mod on its own. Take this one if you already run other SHVDN mods.
- **`BareMinimum-0.3.0-full.zip`** — the same, with ScriptHookVDotNet bundled alongside it. Take this one if this is your first script mod.

Neither contains **ScriptHookV**, which is Alexander Blade's and has to come from his site.

## Installing

Unzip and drop the contents into your GTA V folder — the `scripts` folder merges with the one already there.

**Unblock the ZIP before you extract it**, or ScriptHookVDotNet refuses to load the DLL and says nothing about it.

No asset replacement, no `.rpf` edits. Legacy and Enhanced from one build.

Everything is in `BareMinimum.ini` — every threshold, rate, colour and position — and most of it is also on the in-game menu (**F11**), live. Your pocket is **F12**, and **E** is everything else.

spitmux.me
