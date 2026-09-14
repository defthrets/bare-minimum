# Working on this mod

Read this before touching anything. Every rule here is written because it was broken and it
cost a day.

## The order of work

1. **Read the log first.** `scripts/BareMinimum/BareMinimum.log` in the GTA folder. It has the
   answer far more often than the code does — beds silently missing (`Beds: 4 of 19 model(s)`),
   a trim that never released, a load failure that stops the log dead at the last line that
   worked. Never diagnose before reading it.
2. **One change at a time** when the user is reporting something visual. Two changes and
   neither of you knows which one did it.
3. **Build, deploy, then READ THE LOG AGAIN.** A build that compiles is not a mod that loads,
   and a mod that loads is not a mod that worked. Look for `=== Bare Minimum … started ===`
   with a later timestamp than the deploy, and no truncation.
4. **Commit with explicit paths.** Never `git add -A`. Another session is often in the same
   tree; `git status --short` before and after, and say which files were left alone.

## Things that will bite

- **`IS_MODEL_VALID` asks "can this be SPAWNED".** Anything merely FOUND in the world —
  fridges, beds, counters — must not be filtered by it. Map-only interior fixtures answer no
  while standing in front of you, and the answer is not even stable between launches.
  `GET_CLOSEST_OBJECT_OF_TYPE` finds them by hash regardless.
- **A ptfx effect belongs to an ASSET.** A real effect name asked for out of the wrong asset is
  refused in silence. Menyoo.asi carries the whole particle library as plain strings — see
  `reference_gtav_ptfx_names` — and an asset takes a frame or two to stream, so judging a rung
  on the frame you requested it makes whichever asset is already resident win every time.
- **Anim dictionaries, props and scenarios are all in Menyoo's dumps.** `PedAnimList.txt`,
  `PropList.txt`. Never write a name this repo has not checked against them.
- **Names are checked, numbers are not.** A prop name can be verified from a file. Where a
  thing SITS can only be verified by looking at it — see below.

## Never guess a position

Where a prop sits in a hand, where a plume comes out of a face, where an empty is dropped:
these cannot be reasoned about from here and every attempt has been wrong.

- **The unit is the MODEL, not the kind.** The forty drinks hold twelve different models and
  each has its own origin. A number that stands a cup up lays a mug on its side. `[Eating] Fit`
  holds six numbers per model name; the kind defaults in `foods.json` are only a starting point.
- **Fit it in the pose it is seen in.** His hand is at his side when idle and at his mouth when
  eating. `UI/FitScreen.cs` plays the item's real clip on a loop for exactly this reason.
- **Never change a number the user set.** Not to fix something else, not as part of a revert
  they did not ask for, not "back to zero while we work it out". If something looks wrong, say
  so and leave it. This was done twice in one day and it destroyed work the user had done.

## Editing

- Anchored replacements only: find the exact text, assert it appears **once**, replace, assert
  it landed. Never slice a file by index or line number — it silently eats the code next door.
- A patch script writes per edit and prints per edit. An "ok" at the end is not proof.
- Check construction order in `Main.cs` by hand: a field set on another field that is built
  thirty lines below compiles perfectly and throws at load.

## The house style

- Comments explain WHY, in full sentences, including what was tried and failed. The codebase is
  written to be read; match it rather than trimming it.
- Settings are documented in the ini in the same voice. The ini is shipped documentation.
- No emojis anywhere.
- Every release updates the GitHub release page with the built zips.
