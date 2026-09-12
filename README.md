# Bare Minimum

Hunger, thirst and sleep for GTA V, on a row of upright bars beside the minimap, with health, armour and your sprint energy standing in the same row.

Needs the game never gave Franklin, kept on game-clock time rather than real time — a full game day is roughly forty-eight minutes of play, so eating is something you do while getting on with the game rather than an errand.

**Keys:** E to interact (hold at a door to go in) · **F11** settings · **F12** your pocket · Scroll Lock to survey the map for vending machines. All four in the ini. On a pad: LB + D-pad Up for settings, RB + A for the pocket.

## How it works

### Tuned down on purpose

Fifty-odd game hours from a full stomach to an empty one, standing still. Slowed from twenty-eight, because under an hour from full to starving made eating the thing you were doing instead of the thing you did in passing. Thirst runs in a game day, so it is usually the one that gets you first, and a can fixes it.

### The ini is the file you edit

It is never overwritten by an update. The F11 menu does write to it — but only the single lines whose values you changed, and only once you stop pressing. Your comments, your ordering and anything else you have edited are left exactly as they are.

### Every item is a file, and so is every picture

`foods.json` and `vendors.json` are plain text. Most items have a pixel-art picture of their own in `icons\i_<id>.png`; anything without one shows its group's white shape, tinted. Drop a PNG in and that item has a picture — no rebuild, no field to edit. The mod looks at the file to decide whether it is coloured art or a white shape.

### Ten languages

Off the first row of the settings menu. A language file is a plain map from what the mod says to what it should say instead, so anything a translation has not covered shows in English rather than going blank. Copy `lang\en-GB.json`, fill in the right-hand side, save it under your own code.

### It knows about the others

Drugs carried in Hoodrich show in this mod's pocket, with pictures of their own, and you can take one from there. They never take up a food slot. Fumes puts its fuel gauge on the far side of the map from the row. Both do nothing at all if the other mod is not installed.

## Install

Needs ScriptHookV and ScriptHookVDotNet 3.6 or newer. Drop the contents of the zip into your GTA V folder and merge `scripts\`. Unblock the zip first, or ScriptHookVDotNet refuses to load the DLL and says nothing about it.

No asset replacement, no .rpf edits. Legacy and Enhanced from one build. Nothing is written to your save.

## What changed

The full record, every version, is [`release/CHANGES.txt`](release/CHANGES.txt); the zips are on the [releases page](https://github.com/defthrets/bare-minimum/releases). The recent ones, in detail:

### 0.4.1 — the game's health strip stays hidden

The game's own health and armour strip could come back under the map and stay. The layout that hides it was asked for once and then re-asked only on a handful of moments — a pause, a fade, a switch, a cutscene — and the game undid it on a moment that was none of those, with no re-ask to follow. The log for the session showed it plainly: the ask, the rebuild, then nothing for three minutes while the strip was back on screen.

Six more moments are watched now, and every re-ask is written to the log *with the moment that caused it*, so if it ever comes back again the log names the trigger instead of leaving it to guesswork: in and out of a vehicle, the HUD hidden and given back, the wanted level, an interior boundary, a phone call ending. And a quiet heartbeat re-asks every five seconds while no waypoint is set — free, because with no route on the map there is nothing for a relayout to disturb — so the strip can stay back for five seconds at most whenever you are not navigating, and the heartbeat stands down the instant a waypoint goes up.

### 0.4.0 — every tile gets a picture

157 of the 201 things you can buy have a picture of their own, and so do all nine drugs. Pixel art, in colour, drawn per item rather than per kind: the Bleeder Burger drips, the Bacon Triple Cheese Melt has the bacon, the Money Shot has the egg, the Tacos al Pastor have the pineapple, the Blue Plate has the blue rim, the Sludgie is the blue slush cup. Every tile in the shop, the pocket and the fridge, and the toast when you eat. The white shapes are still there underneath for anything not drawn, tinted as they always were.

Coloured art needed one change in how icons are drawn: the sprite renderer *multiplies* by the tint, which is right for a white shape and wrong for a brown bun with yellow cheese on it. An icon now looks at its own file once and, if it finds colour, keeps its colours and borrows only the alpha from the tint.

`[Minimap] X` and `Y`, for the HUD-mover mods on ultrawide screens: they drag the radar to the corner, the game still reports its original position, and the mod built its frame round an empty rectangle in the middle of the screen. Two live rows under Vitals move the frame, the bars, the strip and the fuel gauge together. And cutscenes hide the HUD by name now, the instant one starts, rather than as a side effect of the game hiding its own. Both were asked for on the mod page.

### 0.3.1 — Hindi in letters the game can draw

0.3.0 shipped Hindi in Devanagari, every word correct, and it drew as a row of empty boxes: GTA V has never had a Hindi release, so no font in the game has those letters. Romanised Hindi is how a great deal of Hindi is actually typed and goes through the same font as English, so it draws everywhere. The Devanagari is kept whole as `hi-Deva` for the day a font has it.

### 0.3.0 — ten languages, and the HUD stops blinking

Ten languages off the first row of the settings menu: English UK and US, Português (BR), Español, Français, Deutsch, Русский, Polski, 中文 and Hindi — every prompt, screen, tab and row label. There are no identifiers in a language file; the English string is the key, so a miss hands back the English and there is no such thing as a missing string. Which also means the food translates the same way the buttons do, with no code involved.

The air meter came back. The game's breath bar lives in the same clip of the minimap as its health strip, so hiding one hid the other — and it took going swimming to find out. It is drawn on the plate under the map now, only while you are under, with the full mark measured rather than assumed so a rebreather counts.

The HUD stopped blinking: four screens each asked the game whether its HUD was hidden and each hid the instant it said yes — a this-frame native that any other script on the machine can flick. One answer now, asked once a frame, held six frames before anything goes. The minimap stopped flickering the GPS: the call that hides the game's health strip is a re-layout of the minimap and was being made sixty times a second; the GPS route is rebuilt by that movie and strobed with it. And four hot dogs carried an animation whose name says eat and which is a bottle going to the mouth.

---

spitmux.me
