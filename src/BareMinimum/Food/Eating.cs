using System;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// Consuming something: the prop in the hand, the animation, and the need it fills.
    ///
    /// THE NEED IS FILLED WHEN THE ANIMATION FINISHES, not when the item is bought. Paying and
    /// eating are two different moments, and crediting the stomach at the till means buying a
    /// sandwich and being full before you have unwrapped it -- which also means a player can
    /// buy five and never watch one.
    ///
    /// EVERY PART OF THE PRESENTATION IS OPTIONAL. A prop this build does not have, an
    /// animation dictionary that will not stream, a bone that is not there -- none of them
    /// stop you eating. The food is the point; the prop is decoration, and decoration that can
    /// fail must never take the mechanic down with it.
    /// </summary>
    internal sealed class Eating
    {
        /// <summary>
        /// PH_L_Hand: the non-deforming prop helper bone on the LEFT hand.
        ///
        /// THE LEFT ONE, and that is the whole point of this constant. The MP eating
        /// interaction brings the LEFT hand to the mouth; the food was originally attached to
        /// PH_R_Hand (28422) and so rode along at the player's side while he mimed eating out
        /// of an empty left hand. Which is also why the hot dog looked like it had not spawned
        /// at all -- it had, it was just down by his hip.
        ///
        /// 60309 is PH_L_Hand and NOT 18905, which is SKEL_L_Hand, the wrist joint. A wrist is
        /// the wrong place for a prop and puts it through the palm; the PH_ bones are prop
        /// helpers, already sitting where a held object should be, which is why the offset and
        /// rotation are all zero. Learned in Overspray and paid for again in Fumes.
        /// </summary>
        private const int LeftHandBone = 60309;

        /// <summary>PH_R_Hand, for anything the animations turn out to hold on the other side.</summary>
        private const int RightHandBone = 28422;

        private readonly Catalogue _menu;
        private readonly Needs.Needs _needs;

        /// <summary>How long one eat or drink stretch of a combo lasts, in milliseconds.</summary>
        private const int PhaseMs = 7000;

        private Item _item;
        private Prop _held;
        private int _finishAt;
        /// <summary>
        /// Told whenever he drinks something, with what it was. Main listens, because the thing
        /// a drink does to the energy bar is the vitals' business and this class has no vitals.
        /// </summary>
        public Action<Item> Drank;

        private bool _animStarted;

        /// <summary>Which of the cycle's clips is playing, and the backstop for one that never finishes.</summary>
        private int _clip;
        private int _clipAt;

        /// <summary>
        /// How many passes of the clip have been played, and when the rest between them ends.
        ///
        /// ANIMATION, BREAK, ANIMATION. See Settings.Bites. A single-clip eat or drink plays
        /// its pass, stands holding the thing for BreakSeconds, plays it again, and the meal
        /// ends with the last one -- so what you watch and how long it takes are the same
        /// thing by construction rather than two numbers somebody has to keep in step.
        ///
        /// Not used by the smoke, which is three different clips in a deliberate order.
        /// </summary>
        private int _passes;
        private int _restUntil;

        /// <summary>
        /// Whether there is NO clip on him right now, so the thing is sitting on a hand bone
        /// in his idle pose rather than being held up to his mouth.
        ///
        /// WHICH HOLD APPLIES. A model fitted against the eating clip is in the wrong spot
        /// on his hand once no clip is on him, because the clip poses the very bone the thing
        /// is attached to. So at rest the thing is held OFF THE WRIST instead, at the place
        /// the fit put it relative to his palm -- see Palm -- or, for a model somebody has
        /// dialled an arm-down fit for by hand, on the prop bone with those numbers.
        ///
        /// NOT SIMPLY !_animStarted, WHICH IS TRUE FOR A FRAME BETWEEN THE SMOKE'S CLIPS. The
        /// smoke is three clips in a deliberate order with no rest in it -- see Puff, which
        /// returns before it sets _restUntil -- and one frame of the cigarette jumping to its
        /// resting place and back is a flicker with no cause anybody could see. This is set
        /// where the REST actually begins.
        /// </summary>
        private bool _resting;

        /// <summary>Whether the thing is on his wrist bone right now rather than the prop bone. See Seat.</summary>
        private bool _onWrist;

        /// <summary>The weapon he had when this started. See Interrupted.</summary>
        private WeaponHash _armed = WeaponHash.Unarmed;

        /// <summary>
        /// The animation actually started, so Cleanup can stop that one.
        ///
        /// KEPT RATHER THAN RE-DERIVED because Cleanup has no item -- it runs when the mod is
        /// shutting down or the meal was interrupted, and at that point the thing being eaten
        /// may already be gone. Stopping the three menu animations by name was fine while
        /// there were only three; an item with its own dictionary would have been left playing.
        /// </summary>
        private AnimRef _playing;

        /// <summary>
        /// True when what is running is a SCENARIO rather than an animation.
        ///
        /// Kept apart because they are stopped by different natives: STOP_ANIM_TASK does
        /// nothing at all to a scenario, and a player left in WORLD_HUMAN_SMOKING when his
        /// cigarette is taken away smokes an empty hand until something else interrupts him.
        /// </summary>
        private bool _scenarioStarted;

        /// <summary>Which half of a combo is in hand. Meaningless for anything else.</summary>
        private bool _drinking;

        /// <summary>When to swap hands between the food and the cup. Zero when not a combo.</summary>
        private int _swapAt;

        /// <summary>What he says about it. Never null; silent when switched off.</summary>
        private readonly Speech _speech;

        /// <summary>For the food spin.</summary>
        private readonly Core.Settings _cfg;

        public Eating(Core.Settings cfg, Catalogue menu, Needs.Needs needs, Speech speech)
        {
            _cfg = cfg;
            _menu = menu;
            _needs = needs;
            _speech = speech;

            _litter = new Litter(cfg);
        }

        /// <summary>What he leaves on the pavement. See Food.Litter.</summary>
        private readonly Litter _litter;

        /// <summary>True while something is being eaten. Stops a second one being started.</summary>
        public bool Busy => _item != null;

        /// <summary>What is in his hands right now, or unarmed if that cannot be read.</summary>
        private static WeaponHash Weapon()
        {
            try
            {
                var me = Game.Player.Character;

                if (me == null || !me.Exists() || me.Weapons == null || me.Weapons.Current == null)
                {
                    return WeaponHash.Unarmed;
                }

                return me.Weapons.Current.Hash;
            }
            catch
            {
                return WeaponHash.Unarmed;
            }
        }

        /// <summary>
        /// Asks for everything an item needs, ahead of time.
        ///
        /// Called while a vendor is offering or a shop row is highlighted, so by the moment
        /// the key is pressed the prop and the animation are already resident and Begin never
        /// has to fall back to empty-handed. Requesting something already loaded is free, so
        /// this can be called every frame without thought.
        ///
        /// This is the OTHER half of removing the spin-waits: without it, the first purchase
        /// of anything would reliably have no prop.
        /// </summary>
        public void Preload(Item item)
        {
            if (item == null) return;

            try
            {
                Ask(item.Prop);
                Ask(item.DrinkProp);

                AskAnim(item.Eat ?? _menu.Eat);
                AskAnim(_menu.Sip);
                AskAnim(_menu.Smoke);
            }
            catch (Exception ex)
            {
                Log.Once("preload", "Could not pre-load an item: " + ex.Message);
            }
        }

        private static void Ask(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            var model = new Model(name);
            if (!model.IsLoaded) model.Request();
        }

        private static void AskAnim(AnimRef anim)
        {
            if (anim == null || !anim.Valid) return;

            if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, anim.Dict))
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, anim.Dict);
            }
        }

        // ======================================================================

        /// <summary>
        /// Starts consuming an item.
        ///
        /// <paramref name="aloud"/> is whether he says anything about it. HE TALKS TO WHOEVER
        /// SERVED HIM, NOT TO HIMSELF: a line at the window of a taco truck or across a shop
        /// counter is him thanking somebody, and the game's bank is full of exactly that. The
        /// same line while he unwraps something he has been carrying around all day is a man
        /// talking to a burrito. So a purchase is aloud and the pocket, the fridge and anything
        /// another mod hands him are not.
        /// </summary>
        public bool Begin(Item item, bool aloud = false)
        {
            if (item == null || Busy) return false;

            var me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead) return false;

            var driving = InVehicle(me);

            // A meal at the wheel runs on its own clock: you pick at it between junctions
            // rather than putting it away in four seconds at a serving window.
            var seconds = driving && item.VehicleSeconds > 0f ? item.VehicleSeconds : item.Seconds;

            // A SMOKE TAKES AS LONG AS A SMOKE TAKES. See Settings.SmokeSeconds: six seconds
            // is a man deciding against it, and Seconds is doing another job as well.
            if (item.Smoke && _cfg.SmokeSeconds > 0.5f) seconds = _cfg.SmokeSeconds;

            // AND EVERYTHING ELSE GETS THE SAME LONGER SIT. Four seconds is enough time for
            // the animation to blend in and out and not much else in between, which reads as
            // a man remembering he is holding something rather than eating it. One number
            // over the whole catalogue rather than two hundred edits: the relative lengths
            // are already right -- a coffee is quicker than a plate of ribs -- and what was
            // wrong is all of them. See Settings.Longer.
            else seconds += _cfg.Longer;

            // BEFORE THE PROP AND THE ANIMATION, so the line lands while his hands are
            // still empty. Said after, he is thanking the cashier around a mouthful.
            // WHAT HE IS HAVING, not only that he paid for it. The game's bank has GENERIC_EAT
            // and GENERIC_DRINK in all three voices, which is a better line over a burger than
            // a thank-you, so the set is picked by what is in his hand. Still only when
            // somebody served him -- see the note on this method.
            if (aloud)
            {
                _speech.Say(item.Smoke ? "smoke"
                          : item.Booze > 0f ? "booze"
                          : item.Drink ? "drink"
                          : "eat");
            }

            _item = item;
            _animStarted = false;
            _clip = 0;
            _clipAt = 0;
            _passes = 0;
            _restUntil = 0;

            // NOTHING IS PLAYING YET. Animate runs a line or two below and usually clears
            // this in the same tick; on a dictionary that has not streamed it stands, which
            // is correct -- he is holding the thing with his arm down.
            _resting = true;

            // What he was holding at the first bite. See Interrupted: a change is the signal,
            // not the weapon itself.
            _armed = Weapon();
            _drinking = false;
            _finishAt = Game.GameTime + (int)(Math.Max(0.5f, seconds) * 1000f);

            // Only a combo swaps hands, and only when there is actually a cup to swap to.
            _swapAt = item.Combo && !string.IsNullOrEmpty(item.DrinkProp)
                ? Game.GameTime + PhaseMs
                : 0;

            Give(me, item, false);
            Animate(me, item, false);

            Log.Debug("Eating " + item.Name + " over " + seconds.ToString("0.#") + "s" +
                      (driving ? " at the wheel" : "") + (_swapAt != 0 ? ", combo" : "") + ".");
            return true;
        }

        public void Update()
        {
            // A GRIP THAT CHANGED IS WRITTEN DOWN, meal or no meal. See Owed.
            Owed();

            if (_item == null) return;

            try
            {
                var now = Game.GameTime;

                if (now >= _finishAt) { Finish(); return; }

                // HANDS ARE FOR ONE THING AT A TIME. A man eating a burger through a gunfight
                // is the kind of detail that makes everything else in the mod look careless,
                // and the upper-body flag that lets him walk while he eats is exactly what
                // lets him do this. So the moment he draws on somebody it goes: the food is
                // lost, which is what dropping it means. See Interrupted.
                if (Interrupted())
                {
                    Log.Info("Put down mid-" + (_item != null && _item.Smoke ? "smoke" : "meal") +
                             ": he reached for a weapon.");
                    Abandon();
                    return;
                }

                // KEEP TRYING TO START IT. Animate asks for a dictionary and gives up for
                // that frame rather than spinning on it, so the very first attempt fails
                // whenever the anim was not already in memory -- which is every purchase from
                // a one-key stall, because only the shelf menu pre-loads as you scroll. One
                // retry a frame for the length of the meal costs nothing and is the difference
                // between an animation that usually plays and one that always does.
                if (!_animStarted)
                {
                    // THE REST IS THE GAP IN ANIMATION, BREAK, ANIMATION. He stands holding
                    // the thing with nothing playing over the top, which is what a beat
                    // between two bites looks like. Asking for the clip again here would
                    // close the gap the moment it opened.
                    var me = now < _restUntil ? null : Game.Player.Character;

                    if (me != null && me.Exists() && !me.IsDead)
                    {
                        Animate(me, _item, _drinking);
                    }
                }
                else
                {
                    Puff(now);
                }

                // AND KEEP TRYING TO PUT IT IN HIS HAND, for exactly the same reason.
                //
                // Give asks for the model and returns empty-handed rather than spinning on it
                // -- the comment in there is right that blocking would freeze the frame -- and
                // it leans on Preload having warmed the model first. Only the two SHELF menus
                // preload, as the cursor moves over a row. The pocket screen does not, and
                // Hoodrich's phone cannot, so the first of anything eaten from either was
                // always mimed: request, return, and by the time the model landed nobody was
                // asking any more. The SECOND one worked, because the model was resident by
                // then -- which is exactly the shape this was reported in.
                //
                // One retry a frame costs nothing and the meal lasts seconds, so the prop
                // appears a frame or two in and nobody can see the difference.
                if (_held == null || !_held.Exists())
                {
                    var him = Game.Player.Character;

                    if (him != null && him.Exists() && !him.IsDead)
                    {
                        _held = null;
                        Give(him, _item, _drinking);
                    }
                }
                else
                {
                    // AND PUT BACK WHERE IT BELONGS EVERY FRAME, so the three rows on the
                    // menu move the thing that is already in his hand. See Seat.
                    Seat(Game.Player.Character, _item, _drinking);
                }

                // Halfway through a stretch, put down the sandwich and pick up the cup, or
                // the other way about. The need is untouched until the whole thing is done.
                if (_swapAt != 0 && now >= _swapAt) Swap();
            }
            catch (Exception ex)
            {
                Log.Once("eating", "Consuming failed: " + ex.Message);
                Abandon();
            }
        }

        /// <summary>
        /// The moment it actually counts: needs filled, prop dropped, animation released.
        ///
        /// The need is applied BEFORE the cleanup, so an exception while tidying up a prop
        /// cannot cost the player the food they already paid for and watched themselves eat.
        /// </summary>
        private void Finish()
        {
            var item = _item;
            _item = null;
            _swapAt = 0;

            // WAKE IS NOT A DRINKS-ONLY PROPERTY. This used to read "if drink, apply food
            // and wake; otherwise apply food" -- so a cigarette, whose whole effect IS its
            // wake, did nothing at all, and so did the wake on nineteen other items: the
            // donuts, the soups, the breakfast, the toastie. Twenty things quietly having no
            // effect, because the branch that applied the value only ran on one kind of item.
            // WHERE THE METERS WERE, KEPT FOR THE CARD. It sweeps its bar from here to
            // wherever the mouthful left it, and by the time the card is built the old value
            // is gone -- so both are taken now, on the line before they change.
            _fedBefore = _needs.Hunger.Value;
            _restedBefore = _needs.Sleep.Value;
            _wateredBefore = _needs.Thirst.Value;

            if (item.Drink)
            {
                _needs.Drink(item.Hunger, item.Wake);
                Drank?.Invoke(item);
            }
            else
            {
                _needs.Eat(item.Hunger);

                // A COUGH AFTER A CIGARETTE. Not a line -- a noise, in his own voice, out of
                // the set the game keeps for coughing. See Speech.Noise.
                if (item.Smoke) _speech.Noise(Speech.Pain.Cough);
                if (item.Wake > 0f) _needs.Wake(item.Wake);
            }

            if (item.Booze > 0f) _needs.Booze(item.Booze);

            // OUTSIDE THE DRINK BRANCH, ON PURPOSE. Food is a bit of a drink and drink is a bit
            // of a meal, and the two branches above already model that in one direction; a
            // thirst that only moved for things flagged Drink would have a bowl of soup doing
            // nothing for it. The figure itself is the item's, and Catalogue.Wetness says where
            // it comes from when the file has not named one.
            _needs.Quench(item.Thirst);

            // ONE LINE PER THING SWALLOWED, at a level somebody will see.
            //
            // Every one of these meters is a number the player can only read as a bar, and
            // "that did nothing" is the single most common thing to be wrong about -- a beer
            // moving the thirst bar half a meter and a beer moving it not at all look identical
            // in the moment. This is the line that settles it without anybody having to
            // reproduce anything.
            Log.Info("Had a " + item.Name + ": fed " + Pct(_needs.Hunger.Value) +
                     "%, watered " + Pct(_needs.Thirst.Value) +
                     "%, rested " + Pct(_needs.Sleep.Value) + "%" +
                     (item.Thirst != 0f ? "  (thirst " + (item.Thirst > 0f ? "+" : "") +
                                          Pct(item.Thirst) + ")" : "") + ".");

            // DROPPED BEFORE THE HAND IS EMPTIED, so the can leaves from where the can was.
            // Cleanup deletes the held prop, and a litter drop after it would come off a hand
            // that is already empty -- which is the same half-second of nothing this is here
            // to remove.
            try
            {
                var me = Game.Player.Character;

                if (me != null && me.Exists() && !InVehicle(me))
                {
                    // FROM WHERE IT WAS, when it is still there to ask. See Litter.Drop.
                    var was = _held != null && _held.Exists()
                            ? (Vector3?)_held.Position
                            : null;

                    // AND IT IS THE THING HE WAS HOLDING. A man finishes a can of Sprunk and
                    // drops a can of Sprunk -- not "a can", which is what a kind default is
                    // and which had him dropping the same anonymous tin whatever he had just
                    // drunk. The model is already in his hand and already fitted; there is no
                    // reason to swap it for a stand-in at the last moment.
                    //
                    // AN ITEM THAT NAMES ITS OWN STILL WINS, because those are the cases where
                    // what is left is genuinely NOT what he held: a cigarette leaves a butt, a
                    // slice of cheesecake leaves the bag it came in, and the smokes were asked
                    // for that way on purpose. See Catalogue.LitterFor.
                    var drops = item != null && item.Litter != null
                              ? _menu.LitterFor(item)
                              : (!string.IsNullOrEmpty(item != null ? item.Prop : null)
                                    ? item.Prop
                                    : _menu.LitterFor(item));

                    _litter.Drop(me, drops, was);
                }
            }
            catch (Exception ex)
            {
                Log.Once("litter", "Could not drop the empty: " + ex.Message);
            }

            Cleanup();
            Report(item);
        }

        /// <summary>
        /// What the ticker says afterwards.
        ///
        /// A BEER DOES NOT REPORT A STOMACH PERCENTAGE. Booze carries a token amount of
        /// hunger -- a Logger Lager is 0.08 -- and leading with "Fed 50%" after one made the
        /// mod look like it thought a lager was a meal. What you want to know after a drink
        /// is how drunk you are, so that is what it says; the couple of per cent of hunger it
        /// happens to add is not news.
        /// </summary>
        /// <summary>Where the two meters stood before the mouthful. The card sweeps from here.</summary>
        private float _fedBefore;
        private float _restedBefore;
        private float _wateredBefore;

        /// <summary>
        /// What he just had, as the same card the wake-up uses.
        ///
        /// NOT THE GAME'S TICKER. That box is the game's own furniture, in the corner, next
        /// to the messages about your car being impounded -- the right place for the game to
        /// talk to you and the wrong one for a mod that draws its own shop menus. The wake-up
        /// moved out of it for that reason and this was the last thing still in it, which is
        /// awkward given eating is the thing that happens most.
        ///
        /// THE ITEM SUPPLIES THE WHOLE CARD. Its icon is the mark, its name is the headline,
        /// its own tint is the accent, and the bar sweeps between the two readings taken
        /// either side of the meal. A cigarette moves the sleep meter rather than the stomach,
        /// so its card sweeps THAT one -- reporting a hunger bar that did not move would be
        /// the mod saying nothing happened when something did.
        /// </summary>
        private void Report(Item item)
        {
            try
            {
                var sleepy = item.Smoke || (item.Wake > 0f && item.Hunger <= 0.001f);

                // WHICHEVER METER THIS ACTUALLY MOVED. The card sweeps one bar and it has to be
                // the one that changed, or the mod is reporting nothing happened when something
                // did -- the same reason a cigarette sweeps sleep rather than the stomach. A can
                // of eCola is 0.06 of a meal and most of a drink, so a stomach bar barely
                // twitching is the wrong thing to be looking at while swallowing it.
                var wet = !sleepy && item.Thirst > item.Hunger;

                var line = item.Smoke ? "That is better."
                         : item.Booze > 0f ? Tipsy()
                         : wet ? Watered()
                         : "Fed " + (int)Math.Round(_needs.Hunger.Value * 100f) + "%.";

                if (!item.Smoke && item.Wake > 0f)
                {
                    line = line.TrimEnd('.') + ", rested " +
                           (int)Math.Round(_needs.Sleep.Value * 100f) + "%.";
                }

                var was = sleepy ? _restedBefore : wet ? _wateredBefore : _fedBefore;
                var now = sleepy ? _needs.Sleep.Value
                        : wet ? _needs.Thirst.Value
                        : _needs.Hunger.Value;

                // TWO AND A HALF, NOT FOUR. The wake-up card can hold four seconds because
                // it happens once a night; eating happens all day, and the same four seconds
                // on every bag of crisps is the mod talking over the game.
                UI.Toast.Show(Mark(item), item.Name.ToUpperInvariant(), line, item.Tint,
                              was, now, 2500);
            }
            catch
            {
                // Not worth failing a meal over.
            }
        }

        /// <summary>
        /// The item's own picture, by the same rule the shelf and the pocket use, so the
        /// thing on the card is the thing that was on the row he bought it from.
        /// </summary>
        private static string Mark(Item item)
        {
            return Art.For(item);
        }

        /// <summary>
        /// What a drink says, which is a percentage like the stomach's rather than words.
        ///
        /// "Quenched" and not "thirst", because this bar reads the same way the other two do:
        /// the number is what you HAVE, not what you need. A drink reporting "thirst 90%" after
        /// a bottle of water would be exactly backwards.
        /// </summary>
        private string Watered()
        {
            return "Quenched " + (int)Math.Round(_needs.Thirst.Value * 100f) + "%.";
        }

        /// <summary>A fraction as whole per cent, for the log line above.</summary>
        private static int Pct(float v)
        {
            return (int)Math.Round(v * 100f);
        }

        /// <summary>How drunk, in words rather than a number.</summary>
        private string Tipsy()
        {
            var d = _needs.Drunk;

            if (d >= 0.85f) return "~o~You are wrecked.";
            if (d >= 0.65f) return "~o~You are hammered.";
            if (d >= 0.40f) return "~y~Properly merry.";
            if (d >= 0.22f) return "~y~Feeling it.";

            return "Barely touched the sides.";
        }

        // ======================================================================
        // The prop
        // ======================================================================

        /// <summary>
        /// Puts the item in the player's hand, if this build has the model.
        ///
        /// The model is STREAMED and waited for with a bounded spin rather than assumed
        /// resident: CREATE_OBJECT on a model that is not in memory returns nothing, so
        /// without the request the item silently never appears.
        /// </summary>
        /// <summary>Swaps the hand between the food and the drink, animation and all.</summary>
        private void Swap()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) { _swapAt = 0; return; }

            _drinking = !_drinking;
            _swapAt = Game.GameTime + PhaseMs;

            // The old one goes before the new one arrives, or there are two things in the
            // hand and the second is attached inside the first.
            DropProp();

            Give(me, _item, _drinking);
            Animate(me, _item, _drinking);
        }

        private void Give(Ped me, Item item, bool drinking)
        {
            var name = drinking ? item.DrinkProp : item.Prop;

            if (string.IsNullOrEmpty(name)) return;
            if (!drinking && !item.PropUsable) return;

            try
            {
                var model = new Model(name);

                if (!model.IsLoaded)
                {
                    // ASK, AND DO NOT WAIT.
                    //
                    // This used to spin on `while (!model.IsLoaded && Game.GameTime < until)`,
                    // which reads like a bounded wait and is in fact an INFINITE LOOP that
                    // hard-freezes the game. Game.GameTime is GET_GAME_TIMER, and that only
                    // advances when the game renders a frame -- but this code runs inside a
                    // script tick, which IS part of the frame. Spinning here stops the frame,
                    // so the timer never moves, so the loop never ends. It froze the game the
                    // first time somebody bought an item whose prop was not already resident.
                    //
                    // Nothing in this mod is worth blocking a frame for. The request is made
                    // and the food is eaten empty-handed this once; Preload below means that
                    // in practice it is already there by the time anybody presses the key.
                    model.Request();

                    Log.Once("prop-slow-" + name,
                             name + " was not loaded yet - eaten empty-handed this time.");
                    return;
                }

                _held = World.CreateProp(model, me.Position, false, false);
                if (_held == null || !_held.Exists()) { _held = null; return; }

                // A NEW OBJECT STARTS WHERE IT BELONGS. Without this the cup half of a combo
                // flies across from wherever the burger was being held. See Eased.
                _satSet = false;
                _onWrist = false;

                Seat(me, item, drinking);

                // The model is released as soon as the object exists; holding the request open
                // pins it in memory for the rest of the session for no reason.
                model.MarkAsNoLongerNeeded();
            }
            catch (Exception ex)
            {
                Log.Once("prop-" + name, "Could not put " + name + " in hand: " +
                                         ex.Message + " - eaten empty-handed.");
                _held = null;
            }
        }

        /// <summary>
        /// Puts what he is holding where it belongs, and does it again every frame.
        ///
        /// AGAIN, BECAUSE OTHERWISE THE NUDGE IS NOT LIVE. The three rows on the menu are
        /// there so somebody can walk a burger into place while holding one -- which only
        /// works if turning the row moves the burger. Attaching happens once, so it did not:
        /// you had to buy another one to see the number you had just typed, which is exactly
        /// the loop those rows exist to avoid.
        ///
        /// ATTACH_ENTITY_TO_ENTITY on something already attached MOVES it rather than
        /// refusing -- the same fact Hoodrich leans on to pass a bag from one hand to another
        /// -- so this is one native call a frame while he is eating and nothing else.
        ///
        /// The last six arguments are Fumes' proven set for a prop in a hand: no soft
        /// pinning, no collision, not treated as a ped, vertex 2, fixed rotation.
        /// </summary>
        private void Seat(Ped me, Item item, bool drinking)
        {
            if (me == null || !me.Exists() || item == null) return;
            if (_held == null || !_held.Exists()) return;

            var anim = AnimFor(item, drinking);

            // WHILE THE CLIP IS ON HIM, LEARN THE GRIP. See Grip: it is what makes the rest
            // below possible, and it costs two bone reads a frame.
            if (!_resting) Grip(me, anim);

            // AT REST, OFF THE WRIST. See Palm. Nothing is eased between the two bones -- the
            // six mean different things on each -- and nothing needs to be: the wrist hold is
            // worked out from where the clip had it, so the switch itself does not move it.
            if (_resting)
            {
                var palm = Palm(me, _held, item, drinking);

                if (palm != null)
                {
                    var wrist = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, (int)palm.Six[0]);

                    Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _held.Handle, me.Handle, wrist,
                                  palm.Six[1], palm.Six[2], palm.Six[3],
                                  palm.Six[4], palm.Six[5], palm.Six[6], false, false, false, false, 2, true);

                    if (!_onWrist) { _onWrist = true; _satSet = false; }
                    else Check(me, _held, anim, palm);

                    return;
                }
            }

            // BACK ON THE PROP BONE. From the wrist that is a snap, for the reason above.
            if (_onWrist) { _onWrist = false; _satSet = false; }

            var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle,
                                          anim.LeftHanded ? LeftHandBone : RightHandBone);

            var spin = SpinFor(item, drinking, _resting);
            var sits = SitsFor(item, drinking, _resting);

            Eased(ref sits, ref spin);

            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _held.Handle, me.Handle, bone,
                          sits.X, sits.Y, sits.Z,
                          spin.X, spin.Y, spin.Z, false, false, false, false, 2, true);
        }

        // ======================================================================
        // The grip, and the hold off the wrist
        // ======================================================================

        /// <summary>
        /// Reads where the prop helper bone sits in the wrist bone's frame while a clip is on
        /// him, and remembers it against the clip.
        ///
        /// THE CLIP POSES THE PROP BONE. Every model in Settings.Fit was dialled against that
        /// posed bone, and with no clip on him the bone is somewhere else -- which is why the
        /// same six put the burger in the wrong spot on his hand the moment his arm dropped.
        /// This is the difference, measured rather than guessed: a fact about the clip and the
        /// skeleton, the same for every model eaten with it. Palm uses it to hold the thing
        /// off the WRIST at the place the fit put it relative to his palm, so it rides his
        /// hand down and sits where he dialled it with his arm at his side.
        ///
        /// RELATIVE MATRICES, so the ped's own transform cancels: prop bone in the ped's frame
        /// against wrist bone in the ped's frame is prop bone in the wrist's frame.
        ///
        /// TAKEN EVERY CLIP FRAME AND THE LAST ONE KEPT, which is the pose his arm drops out
        /// of, so the switch to the wrist does not move the thing. Written to the ini once it
        /// has settled -- see Owed -- so the pocket has it from the first frame of the next
        /// session, before anything has been eaten.
        /// </summary>
        private void Grip(Ped me, AnimRef anim)
        {
            if (anim == null || !anim.Valid || _cfg == null) return;

            try
            {
                var ph = me.Bones[anim.LeftHanded ? Bone.PHLeftHand : Bone.PHRightHand];
                var wr = me.Bones[anim.LeftHanded ? Bone.SkelLeftHand : Bone.SkelRightHand];

                if (ph == null || wr == null || ph.Index < 0 || wr.Index < 0) return;

                var rel = ph.RelativeMatrix * Matrix.Invert(wr.RelativeMatrix);
                var q = Quaternion.RotationMatrix(rel);

                var key = GripKey(anim);
                var seven = new[] { rel.M41, rel.M42, rel.M43, q.X, q.Y, q.Z, q.W };

                float[] had;

                if (_cfg.Grip.TryGetValue(key, out had) && !Moved(had, seven)) return;

                _cfg.Grip[key] = seven;

                // A CHANGE INVALIDATES EVERY HOLD WORKED OUT FROM THE OLD ONE.
                _gripVersion++;
                _palm.Clear();

                _gripOwed = true;
                _gripOwedAt = Game.GameTime;

                if (_gripSaid.Add(key))
                {
                    Log.Info("Grip: measured " + key + " -- everything eaten with that clip is " +
                             "held off his wrist with his arm down from now on.");
                }
            }
            catch (Exception ex)
            {
                Log.Once("grip-read", "Could not read the grip off his hand: " + ex.Message);
            }
        }

        private static string GripKey(AnimRef anim)
        {
            var clip = anim.Cycle != null && anim.Cycle.Length > 0 ? anim.Cycle[0] : anim.Clip;
            return anim.Dict + "/" + clip;
        }

        /// <summary>Two millimetres or a degree, so a hand that trembles does not rewrite the ini.</summary>
        private static bool Moved(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length < 7 || b.Length < 7) return true;

            var dx = a[0] - b[0]; var dy = a[1] - b[1]; var dz = a[2] - b[2];
            if (dx * dx + dy * dy + dz * dz > 0.002f * 0.002f) return true;

            var dot = Math.Abs(a[3] * b[3] + a[4] * b[4] + a[5] * b[5] + a[6] * b[6]);
            return dot < 0.99996f;   // about a degree
        }

        /// <summary>Bumped whenever a grip changes, so a hold worked out before it is not reused.</summary>
        private int _gripVersion;

        private bool _gripOwed;
        private int _gripOwedAt;
        private readonly System.Collections.Generic.HashSet<string> _gripSaid =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>Writes a changed grip down, two seconds after the last change. See Strap.Owed for the pattern.</summary>
        private void Owed()
        {
            if (!_gripOwed) return;

            int now;
            try { now = Game.GameTime; }
            catch { return; }

            if (now - _gripOwedAt < 2000) return;

            SaveGrip();
        }

        private void SaveGrip()
        {
            _gripOwed = false;

            try { IniFile.SetValue(Paths.Ini, "Eating", "Grip", _cfg.GripLine); }
            catch (Exception ex) { Log.Once("grip-save", "Could not write the grip: " + ex.Message); }
        }

        /// <summary>One hold off the wrist, worked out and kept. See Palm.</summary>
        private sealed class Palmed
        {
            /// <summary>Bone tag, then the six for ATTACH_ENTITY_TO_ENTITY on that bone.</summary>
            public float[] Six;

            /// <summary>The same thing as a matrix in the wrist's frame, for Check.</summary>
            public Matrix Rel;

            /// <summary>Which model, for the log.</summary>
            public string Model;

            public bool Checked;
        }

        private readonly System.Collections.Generic.Dictionary<string, Palmed> _palm =
            new System.Collections.Generic.Dictionary<string, Palmed>();

        /// <summary>
        /// Where the pocket holds this thing with his arm down: the bone and the six. Null
        /// when the prop bone and the fit are the right answer -- see Palm.
        /// </summary>
        public float[] PalmFor(Ped me, Prop prop, Item item)
        {
            var palm = Palm(me, prop, item, false);
            return palm == null ? null : palm.Six;
        }

        /// <summary>
        /// The hold off the wrist for this model: fit times grip, as the six numbers the attach
        /// native wants on the wrist bone.
        ///
        /// NULL MEANS THE PROP BONE. Three reasons: a resting fit somebody dialled by hand
        /// for this model wins, because it was made for exactly this and is the one way to
        /// overrule this per model; no grip has been measured for the clip yet, which is every
        /// clip until something has been eaten with it once; or the maths could not be done.
        ///
        /// THE GAME DOES THE EULER WORK. The attach native takes degrees in an order this code
        /// has no business guessing at, so the prop itself is the converter: detached for one
        /// frame, turned to the fit's angles so its matrix gives the fit as a rotation, then
        /// set to the product's quaternion so the game reads back the product's angles. It is
        /// attached again on the line after and nobody sees the frame in between. Done once
        /// per model and clip and kept; a change to the grip or the fit does it again.
        /// </summary>
        private Palmed Palm(Ped me, Prop prop, Item item, bool drinking)
        {
            if (me == null || prop == null || item == null || _cfg == null) return null;
            if (string.IsNullOrEmpty(item.Prop) || !prop.Exists()) return null;

            float[] rest;
            if (_cfg.Rest.TryGetValue(item.Prop, out rest)) return null;

            var anim = AnimFor(item, drinking);
            if (anim == null || !anim.Valid) return null;

            var key = GripKey(anim);

            float[] grip;

            if (!_cfg.Grip.TryGetValue(key, out grip))
            {
                Log.Once("grip-none-" + key, "No grip measured for " + key + " yet, so " + item.Prop +
                         " sits on the prop bone with his arm down. It is measured the first " +
                         "time anything is eaten with that clip.");
                return null;
            }

            var sits = SitsFor(item, drinking, false);
            var spin = SpinFor(item, drinking, false);

            var cacheKey = item.Prop + "|" + key + "|" + _gripVersion + "|" +
                           sits.X.ToString("0.####") + "," + sits.Y.ToString("0.####") + "," + sits.Z.ToString("0.####") + "," +
                           spin.X.ToString("0.#") + "," + spin.Y.ToString("0.#") + "," + spin.Z.ToString("0.#");

            Palmed had;
            if (_palm.TryGetValue(cacheKey, out had)) return had;

            try
            {
                Function.Call(Hash.DETACH_ENTITY, prop.Handle, true, true);

                // The fit as a matrix: its angles as the game reads them, its offset as the row.
                Function.Call(Hash.SET_ENTITY_ROTATION, prop.Handle, spin.X, spin.Y, spin.Z, 2, true);

                var fit = prop.Matrix;
                fit.M41 = sits.X; fit.M42 = sits.Y; fit.M43 = sits.Z; fit.M44 = 1f;

                var g = Matrix.RotationQuaternion(new Quaternion(grip[3], grip[4], grip[5], grip[6]));
                g.M41 = grip[0]; g.M42 = grip[1]; g.M43 = grip[2]; g.M44 = 1f;

                // prop = fit * propBone, propBone = grip * wrist, so prop = (fit * grip) * wrist.
                var rel = fit * g;

                var q = Quaternion.RotationMatrix(rel);
                Function.Call(Hash.SET_ENTITY_QUATERNION, prop.Handle, q.X, q.Y, q.Z, q.W);

                var e = Function.Call<Vector3>(Hash.GET_ENTITY_ROTATION, prop.Handle, 2);

                var made = new Palmed
                {
                    Six = new[]
                    {
                        (float)(int)(anim.LeftHanded ? Bone.SkelLeftHand : Bone.SkelRightHand),
                        rel.M41, rel.M42, rel.M43, e.X, e.Y, e.Z
                    },
                    Rel = rel,
                    Model = item.Prop
                };

                _palm[cacheKey] = made;
                return made;
            }
            catch (Exception ex)
            {
                Log.Once("palm-" + item.Prop, "Could not work out the wrist hold for " + item.Prop +
                         ": " + ex.Message + ". It sits on the prop bone instead.");
                return null;
            }
        }

        /// <summary>
        /// Says, once per hold, how far the thing actually is from where the maths put it.
        ///
        /// THE ONE LINE THAT SETTLES A WRONG-LOOKING HOLD. If the quaternion the game keeps
        /// and the one this code builds ever disagree in handedness, the thing sits mirrored
        /// and looks merely odd; this measures the real prop against the intended place and
        /// writes the gap down in millimetres and degrees. A few of either is the frame's
        /// blend. Tens of degrees is this code, and the line says so.
        /// </summary>
        private void Check(Ped me, Prop prop, AnimRef anim, Palmed palm)
        {
            if (palm == null || palm.Checked) return;
            palm.Checked = true;

            try
            {
                var wr = me.Bones[anim.LeftHanded ? Bone.SkelLeftHand : Bone.SkelRightHand];
                if (wr == null || wr.Index < 0) return;

                var want = palm.Rel * (wr.RelativeMatrix * me.Matrix);
                var got = prop.Matrix;

                var dx = got.M41 - want.M41; var dy = got.M42 - want.M42; var dz = got.M43 - want.M43;
                var mm = Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1000.0;

                var qa = Quaternion.RotationMatrix(got);
                var qb = Quaternion.RotationMatrix(want);
                var dot = Math.Min(1.0, Math.Abs(qa.X * qb.X + qa.Y * qb.Y + qa.Z * qb.Z + qa.W * qb.W));
                var deg = 2.0 * Math.Acos(dot) * 180.0 / Math.PI;

                Log.Info("Palm: " + palm.Model + " off the wrist is " + mm.ToString("0") +
                         " mm and " + deg.ToString("0.#") + " deg from where the clip had it" +
                         (deg > 8.0 || mm > 40.0 ? " -- THAT IS WRONG. Paste this line." : "."));
            }
            catch
            {
                // Diagnostic only.
            }
        }

        /// <summary>The six actually used this frame, easing toward the six that are wanted.</summary>
        private readonly float[] _sat = new float[6];
        private bool _satSet;

        /// <summary>
        /// How fast the hold catches up when the answer changes. Per second.
        ///
        /// ABOUT A TENTH OF A SECOND, which is under the time it takes his arm to come down
        /// and over the time it takes to read as a jump.
        /// </summary>
        private const float Catch = 14f;

        /// <summary>
        /// Moves the hold toward where it should be instead of putting it there.
        ///
        /// BECAUSE THERE ARE TWO ANSWERS NOW AND IT SWAPS BETWEEN THEM MID-MEAL. The eating
        /// six and the resting six are two different places, and a prop that teleports from
        /// one to the other twice a sandwich is the kind of thing that gets reported as the
        /// food glitching in his hand. Eased, the change happens while his arm is already
        /// moving out of the clip and nobody can see it happen at all.
        ///
        /// THE ANGLES TAKE THE SHORT WAY ROUND. Lerping 355 to 5 the straight way spins the
        /// thing all the way through 180 -- a whole revolution to travel ten degrees -- which
        /// is far worse than the pop this is here to remove.
        ///
        /// SNAPPED FOR A NEW PROP, not eased: Give clears the flag when it makes one, so a
        /// cup handed over after a burger starts where the cup belongs rather than flying
        /// across from where the burger was.
        /// </summary>
        private void Eased(ref Vector3 sits, ref Vector3 spin)
        {
            var want = new[] { sits.X, sits.Y, sits.Z, spin.X, spin.Y, spin.Z };

            if (!_satSet)
            {
                for (var i = 0; i < 6; i++) _sat[i] = want[i];
                _satSet = true;
            }
            else
            {
                var dt = 0f;

                try { dt = Game.LastFrameTime; }
                catch { dt = 0f; }

                if (dt < 0f || dt > 0.5f) dt = 0f;

                var k = dt * Catch;
                if (k > 1f) k = 1f;

                for (var i = 0; i < 3; i++) _sat[i] += (want[i] - _sat[i]) * k;

                for (var i = 3; i < 6; i++)
                {
                    // Shortest arc: the difference wrapped into -180..180 before it is taken.
                    var d = ((want[i] - _sat[i]) % 360f + 540f) % 360f - 180f;
                    _sat[i] += d * k;
                }
            }

            sits = new Vector3(_sat[0], _sat[1], _sat[2]);
            spin = new Vector3(_sat[3], _sat[4], _sat[5]);
        }

        /// <summary>
        /// Where this item sits in his hand: what the catalogue says, plus the live nudge.
        ///
        /// THE HAND BONE IS A GRIP POINT AND NOT A SHELF. A prop attached at nought puts its
        /// own origin on that point, and these models are centred on themselves, so the thing
        /// he is holding comes out halfway through his palm. The catalogue carries a number
        /// for each kind and any item may name its own -- see Catalogue.HoldFor -- and the
        /// three on top are the ini's, so somebody with a burger in their hand can walk it
        /// into place on the menu rather than guessing at a json file. Those are ADDED, so
        /// nought on the menu leaves every item exactly where the catalogue put it.
        /// </summary>
        private Vector3 SitsFor(Item item, bool drinking, bool resting)
        {
            // THE MODEL'S OWN ANSWER FIRST, if anybody has fitted this one. See Settings.Fit:
            // a kind default is a guess that has to serve twelve different models, and this
            // is the number somebody dialled with THIS model in his hand.
            var fit = Fitted(item, resting);
            if (fit != null) return new Vector3(fit[0], fit[1], fit[2]);

            var it = _menu.HoldFor(item, drinking);

            // AND A SMOKE IS LOCKED WHERE IT IS. A cigarette at the middle of the hand was
            // right before any of this existed -- it is a small thing between two fingers and
            // the grip point is exactly where it belongs -- so it takes the catalogue's
            // number and nothing else. The nudge is a dial for walking a CUP into place, and
            // a dial that drags the cigarettes out with it is a dial nobody can use.
            if (item != null && item.Smoke) return new Vector3(it[0], it[1], it[2]);

            // AND ONLY THE KIND BEING TUNED. See Settings.HoldWhat: one dial, two things to
            // tune, and a dial that moved both would undo whichever was finished first.
            if (!Tuning(item, drinking)) return new Vector3(it[0], it[1], it[2]);

            return new Vector3(it[0] + _cfg.HoldX, it[1] + _cfg.HoldY, it[2] + _cfg.HoldZ);
        }

        /// <summary>
        /// The six this model is fitted with right now, or null if nobody has fitted it.
        ///
        /// THE RESTING TABLE ONLY WHILE NO CLIP IS ON HIM, and it falls through to the eating
        /// one when a model has no resting line -- which is every model until somebody dials
        /// one, so this changes nothing for anybody who has not asked for it.
        ///
        /// WHETHER HE IS RESTING IS THE CALLER'S TO SAY. For a meal it is _resting; for the
        /// pocket it is whether the clip it holds him in has landed yet. See UI.Bag.Turned.
        /// </summary>
        private float[] Fitted(Item item, bool resting)
        {
            if (item == null || string.IsNullOrEmpty(item.Prop)) return null;

            float[] six;

            if (resting && _cfg.Rest.TryGetValue(item.Prop, out six)) return six;

            return _cfg.Fit.TryGetValue(item.Prop, out six) ? six : null;
        }

        /// <summary>
        /// Where the pocket puts this thing in his hand, and how it is turned: the same six a
        /// meal uses, worked out by the same code.
        ///
        /// THE POCKET HAD ITS OWN COPY OF THIS AND IT HAD FALLEN BEHIND. It still added up the
        /// kind's default and the old nudges and had never heard of the fitting bench -- so
        /// every model Michael fitted sat right while he ate it and wrong while he chose it.
        /// One copy now, here.
        /// </summary>
        public Vector3 HeldAt(Item item, bool resting)
        {
            return SitsFor(item, false, resting);
        }

        /// <summary>See HeldAt.</summary>
        public Vector3 HeldTurn(Item item, bool resting)
        {
            return SpinFor(item, false, resting);
        }

        /// <summary>Whether the six nudges are pointed at this item's kind. See Settings.HoldWhat.</summary>
        private bool Tuning(Item item, bool drinking)
        {
            if (item == null || item.Smoke) return false;

            var drink = item.Drink || drinking;

            return _cfg.HoldWhat == 1 ? !drink : drink;
        }

        /// <summary>
        /// How this item is turned in his hand, in degrees.
        ///
        /// THREE THINGS ADDED UP, and each of them is somewhere different on purpose. The
        /// catalogue's is the permanent answer and belongs in foods.json; [Eating] FoodSpin
        /// is the old food-only setting, kept because somebody may have tuned it; and the
        /// live nudge is the one on the menu, for finding a number with the thing in his
        /// hand. A smoke takes the catalogue's and nothing else -- see SitsFor for why.
        /// </summary>
        private Vector3 SpinFor(Item item, bool drinking, bool resting)
        {
            var fit = Fitted(item, resting);
            if (fit != null) return new Vector3(fit[3], fit[4], fit[5]);


            var it = _menu.TurnFor(item, drinking);

            if (item != null && item.Smoke) return new Vector3(it[0], it[1], it[2]);

            var food = item != null && !item.Drink && !drinking;
            var mine = Tuning(item, drinking);

            return new Vector3(
                it[0] + (mine ? _cfg.TurnX : 0f) + (food ? _cfg.FoodSpinX : 0f),
                it[1] + (mine ? _cfg.TurnY : 0f) + (food ? _cfg.FoodSpinY : 0f),
                it[2] + (mine ? _cfg.TurnZ : 0f) + (food ? _cfg.FoodSpinZ : 0f));
        }

        // ======================================================================
        // The animation
        // ======================================================================

        /// <summary>
        /// The animation this item is had with, checked against this build.
        ///
        /// ONE ANSWER FOR EVERYBODY WHO ASKS. The meal plays it, Seat reads its hand off it,
        /// and the pocket holds the thing up in its last frame. Each of them used to work it
        /// out for itself, and the pocket never did at all -- which is how it came to show
        /// everything in his right hand while he ate with his left.
        ///
        /// CHECKED BEFORE ANYTHING IS ASKED OF IT. A dictionary that is not here never loads,
        /// so without this a caller requests it forever and plays nothing -- which is exactly
        /// how the smoking animation shipped: one guessed name, and a single log line saying
        /// it "was not loaded YET" for good. Resolve does the work once and is free after.
        /// </summary>
        public AnimRef AnimFor(Item item, bool drinking)
        {
            if (item == null) return _menu.Eat;

            var anim = item.Smoke ? _menu.Smoke
                     : (item.Drink || drinking) ? _menu.Sip
                     : (item.Eat ?? _menu.Eat);

            anim.Resolve(item.Smoke ? "Smoking animation"
                                    : (item.Drink || drinking) ? "Drinking animation"
                                    : "Eating animation");

            return anim;
        }

        /// <summary>
        /// Whether a meal is on and playing this animation.
        ///
        /// The pocket asks before it lets go of the clip it was holding him in: eating from
        /// the pocket starts the meal and THEN shuts the pocket, and the meal's clip is that
        /// same clip, so a stop by name there would stop his first bite before it began.
        /// </summary>
        public bool Plays(AnimRef anim)
        {
            return _item != null && anim != null && ReferenceEquals(_playing, anim);
        }

        /// <summary>
        /// Plays the eat or drink loop, if the dictionary will stream.
        ///
        /// FLAG 49 = LOOPING (1) + UPPERBODY (16) + SECONDARY (32).
        ///
        /// SECONDARY is the part that matters and the part that is easy to miss. It makes the
        /// clip a second task running ALONGSIDE whatever the ped is already doing rather than
        /// replacing it -- which is what lets somebody keep walking while they eat, and, more
        /// to the point, keep DRIVING. An earlier version of this method refused to play
        /// anything in a vehicle on the assumption that it would fight the driving pose; with
        /// the secondary flag it does not, and refusing was throwing away the whole point of
        /// buying food at a drive-through.
        ///
        /// UPPERBODY keeps it off the legs and the steering.
        /// </summary>
        private void Animate(Ped me, Item item, bool drinking)
        {
            // A combo drinks from a cup on its drink stretch; a plain drink always drinks.
            // Checked against this build on the way through -- see AnimFor.
            var anim = AnimFor(item, drinking);

            _playing = anim;

            if (!anim.Usable) return;

            try
            {
                if (!anim.Valid)
                {
                    Scenario(me, anim);
                    return;
                }

                // Asked for, never waited on. Same reasoning as the prop above: a spin here
                // stops the frame that would have loaded it.
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, anim.Dict))
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, anim.Dict);
                    return;
                }

                var clip = anim.Cycle.Length > 0 ? anim.Cycle[_clip % anim.Cycle.Length] : anim.Clip;

                // ONE PASS AT A TIME. NOTHING HERE LOOPS ANY MORE.
                //
                // Both flags are upper body and secondary -- the legs stay the game's, which
                // is what lets him walk about with it -- and 49 adds LOOPING.
                //
                // A BITE IS ABOUT A SECOND LONG, and the meal is several. The first answer
                // to that was the LOOPING flag with the meal's remaining time as the length,
                // which does fill the time and reads as a man winding a handle: the same
                // motion three or four times over with no beat between them.
                //
                // SO IT IS ONE PASS, A REST, AND ANOTHER PASS. Flag 48 for everything --
                // upper body and secondary, so his legs stay the game's and he can walk and
                // drive -- and no duration, so the clip runs its own length and ENDS. Puff
                // sees it end, waits BreakSeconds, and asks for the next one.
                //
                // AND LOOPING A LOOP WAS ALWAYS THE JANK. mp_player_intdrink / loop_bottle
                // is already a loop; the LOOPING flag restarted it against a four-frame blend
                // every pass. Nothing on this line loops any more, so that cannot come back.
                //
                // The smoke is three clips in a deliberate order and reaches the same code
                // by the same route: each has to END before the next can start.
                //
                // NOT HELD ON ITS LAST FRAME. That was tried for the break between bites, so
                // the fitted numbers would stay right, and it left him stood with a bottle at
                // his face while he chose the next one. His arm comes down; the thing rides
                // his wrist down with it. See Palm.
                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, anim.Dict, clip,
                              4f, -4f, -1, 48,
                              0f, false, false, false);

                _clipAt = Game.GameTime;
                _animStarted = true;
                _resting = false;
            }
            catch (Exception ex)
            {
                Log.Once("anim-fail-" + anim.Dict, "Could not play " + anim.Dict + ": " + ex.Message);
            }
        }

        /// <summary>
        /// The next clip of the cycle, once the one playing has finished.
        ///
        /// THE CLIP SAYS WHEN IT IS DONE. GET_ENTITY_ANIM_DURATION is not in the vendored
        /// 3.6.0 enum; GET_ENTITY_ANIM_CURRENT_TIME is, and it hands back the phase from 0 to
        /// 1 -- so a clip of any length is followed to its end without anybody writing down
        /// how long it is. Swapped a hair before the end so the two blend rather than snap.
        ///
        /// AND A WALL CLOCK BEHIND IT. A phase that never moves -- a clip name that is not in
        /// the dictionary, which this game plays as silence -- would otherwise leave him
        /// holding it exactly as before, which is the bug this is fixing. Five seconds and it
        /// moves on regardless.
        ///
        /// Nothing here when the cycle is one clip long, which is every animation but the
        /// smoke: it plays, it ends, and the prop stays in his hand until Finish.
        /// </summary>
        private void Puff(int now)
        {
            // ONE CLIP REACHES HERE NOW TOO. This used to leave immediately unless there
            // was a cycle to advance, because the eat and the drink were looped by the game
            // and never ended. They are played one pass at a time instead, so this is what
            // spaces them: see Settings.Bites.
            if (_playing == null) return;

            var me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead) return;

            var done = now - _clipAt > 5000;

            if (!done)
            {
                try
                {
                    var clip = _playing.Cycle.Length > 0
                             ? _playing.Cycle[_clip % _playing.Cycle.Length]
                             : _playing.Clip;

                    done = Function.Call<float>(Hash.GET_ENTITY_ANIM_CURRENT_TIME,
                                                me.Handle, _playing.Dict, clip) >= 0.93f;
                }
                catch
                {
                    // Then the wall clock above is the whole answer.
                }
            }

            if (!done) return;

            // AND HE BREATHES OUT. This is the beat the clip lowers his hand on, which is the
            // beat a man breathing out would be breathing out on -- so the plume goes here
            // rather than on a clock of its own, and it is in step with the animation for
            // free. Only for the things that are actually smoked: everything else in the
            // catalogue has a one-clip cycle and never reaches this line. See Exhale.
            // ONLY OFF A SMOKE. This used to be guarded by "has a cycle", which was true of
            // exactly one thing at the time. The drink now repeats its loop through this same
            // path so it lasts as long as the drink does, and without this line he would
            // breathe out a lungful of smoke every time he lowered a can of Sprunk.
            if (_cfg.Puffs && _item != null && _item.Smoke) Exhale.Now(me);

            _clip++;
            _animStarted = false;

            // A CYCLE IS A SEQUENCE AND RUNS TO THE CLOCK. The smoke is three clips in a
            // deliberate order -- idle_a, idle_b, idle_c -- and a rest inside that would be
            // a man stopping halfway through raising a cigarette. It keeps going until
            // SmokeSeconds is up, exactly as it always has.
            if (_playing.Cycle.Length >= 2) return;

            // AND NEITHER DOES A SMOKE THAT FELL BACK TO ITS ONE-CLIP OPTION. The smoking
            // set's second rung is a single base clip, and without this line a cigarette
            // whose three-clip dictionary did not stream would quietly end after two passes
            // instead of after SmokeSeconds. A smoke is timed, not counted.
            if (_item != null && _item.Smoke) return;

            // ONE CLIP IS A PASS, AND THERE ARE Bites OF THEM WITH A REST BETWEEN.
            _passes++;

            if (_passes < Math.Max(1, _cfg.Bites))
            {
                _restUntil = now + (int)(Math.Max(0f, _cfg.BreakSeconds) * 1000f);

                // AND HIS ARM COMES DOWN WITH THE THING IN HIS HAND. The clip has ended and
                // lets go on its own; from here Seat holds it off the wrist. See Palm.
                _resting = true;
                return;
            }

            // AND THE MEAL ENDS WITH THE LAST PASS, which is what makes the animation and the
            // duration one thing instead of two that have to be kept in step.
            //
            // BY MOVING THE CLOCK, NOT BY CALLING Finish HERE. Finish clears _item, and the
            // rest of this tick still reads it -- the prop block below would hand a null to
            // Give. Bringing the deadline forward lets the top of the next tick take the
            // path it already takes, which is the one that has always worked.
            if (_finishAt > now) _finishAt = now;
        }

        /// <summary>
        /// The floor under the animation: a scenario, when no dictionary exists.
        ///
        /// Scenarios are not asset names that can be missing the way an animation dictionary
        /// can, and WORLD_HUMAN_SMOKING is the same one the hot dog man has been visibly
        /// using on his cigarette break since the day it went in. It is not as good -- it
        /// squares the player up and ignores the prop in his hand -- but a man standing
        /// perfectly still holding a cigarette is worse.
        ///
        /// NOT IN A VEHICLE. A scenario cannot run at the wheel and asking for one there
        /// cancels the driving task, which is a considerably bigger problem than a missing
        /// animation.
        /// </summary>
        private void Scenario(Ped me, AnimRef anim)
        {
            if (string.IsNullOrEmpty(anim.Scenario)) return;
            if (InVehicle(me)) return;

            try
            {
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, me.Handle, anim.Scenario, 0, true);

                _animStarted = true;
                _scenarioStarted = true;
                _resting = false;

                Log.Once("anim-scenario-" + anim.Scenario,
                         "Using the " + anim.Scenario + " scenario instead of an animation.");
            }
            catch (Exception ex)
            {
                Log.Once("anim-scenario-fail", "Could not start " + anim.Scenario + ": " + ex.Message);
            }
        }

        private static bool InVehicle(Ped me)
        {
            try { return me.IsInVehicle(); }
            catch { return false; }
        }

        // ======================================================================

        private void Cleanup()
        {
            // A GRIP STILL OWED IS WRITTEN NOW rather than left to a timer that may not be
            // called again -- this is also the shutdown path.
            if (_gripOwed) SaveGrip();

            if (_animStarted)
            {
                try
                {
                    var me = Game.Player.Character;
                    if (me != null && me.Exists())
                    {
                        if (_scenarioStarted)
                        {
                            // No choice here: STOP_ANIM_TASK does nothing to a scenario, and
                            // there is no stop-this-one-scenario native. Safe enough in
                            // practice, because Scenario() refuses to start one in a vehicle
                            // -- so the task being cleared is a man standing on a pavement.
                            Function.Call(Hash.CLEAR_PED_TASKS, me.Handle);
                        }
                        else
                        {
                            // The SPECIFIC task, not CLEAR_PED_TASKS. Clearing everything
                            // would also cancel whatever else the player happened to be doing
                            // -- which at a shop counter is usually nothing, and while walking
                            // is not.
                            // WHICH CLIP WAS ACTUALLY RUNNING, once a session.
                            //
                            // This is the line that would have found the smoke bug in a
                            // minute rather than in a report. A cycled animation plays a
                            // different clip every pass and the stop below has to name the
                            // live one; nothing was written when a smoke ended except the
                            // "Had a" line, so an animation going on after the cigarette had
                            // been deleted left no trace anywhere. See Stop.
                            if (_playing != null && _playing.Cycle != null &&
                                _playing.Cycle.Length >= 2)
                            {
                                Log.Once("anim-cycle-stop",
                                         "Ending a cycled animation: " + _playing.Dict +
                                         " was on " + Live(me, _playing) + ". All " +
                                         _playing.Cycle.Length + " of its clips are stopped, " +
                                         "not just the one the option is named after.");
                            }

                            // The one that was started first, because an item with its own
                            // dictionary is not any of the three below and would be left running.
                            if (_playing != null) Stop(me, _playing);

                            Stop(me, _menu.Eat);
                            Stop(me, _menu.Sip);
                            Stop(me, _menu.Smoke);
                        }
                    }
                }
                catch
                {
                    // The animation ends on its own soon enough.
                }

                _animStarted = false;
                _playing = null;
                _scenarioStarted = false;
            }

            _onWrist = false;

            DropProp();
        }

        /// <summary>
        /// Which clip of a cycle the game says is running, for the log. "nothing" when none
        /// of them is, which is itself worth reading: it means the clip had already ended on
        /// its own and the stop below had nothing left to do.
        /// </summary>
        private static string Live(Ped me, AnimRef anim)
        {
            try
            {
                foreach (var clip in anim.Cycle)
                {
                    if (string.IsNullOrEmpty(clip)) continue;

                    if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM,
                                            me.Handle, anim.Dict, clip, 3))
                    {
                        return clip;
                    }
                }
            }
            catch
            {
                return "a clip it could not ask about";
            }

            return "nothing";
        }

        /// <summary>
        /// Ends one animation, if it was ever given a real dictionary to play.
        ///
        /// EVERY CLIP IN THE CYCLE, NOT THE ONE THE OPTION IS NAMED AFTER -- and that is the
        /// whole of why the smoking animation outlasted the cigarette.
        ///
        /// STOP_ANIM_TASK TAKES A CLIP NAME AND DOES NOTHING AT ALL WHEN THAT CLIP IS NOT THE
        /// ONE RUNNING. This passed anim.Clip, which is the clip the winning option is named
        /// after -- idle_a for the smoke -- while what is actually playing at any moment is
        /// Cycle[_clip % Cycle.Length], because the smoke is three clips in a deliberate
        /// order. So two times in three the stop was handed the name of a clip that was not
        /// running, did nothing, and the man carried on raising an empty hand to his mouth
        /// after the cigarette in it had been deleted. Reported as exactly that: the animation
        /// going on longer than the cigarette is there.
        ///
        /// It only ever showed on the smoke because the smoke is the only thing in the
        /// catalogue with a cycle longer than one -- see AnimRef.Resolve, where an option
        /// that names no cycle gets a cycle of one containing the clip itself, so the name
        /// always matched. It would have shown on the first food item that grew a cycle.
        ///
        /// THREE NATIVE CALLS ON A MEAL THAT HAS ALREADY ENDED. Stopping a clip that is not
        /// playing is free and silent, which is the property that made this bug invisible and
        /// is also what makes covering the whole cycle the cheap fix rather than tracking
        /// which one is current in another field that has to be kept in step.
        ///
        /// The 3f is the blend out: it eases the arm down rather than snapping it, which is
        /// what it did before and the part that was right.
        /// </summary>
        private static void Stop(Ped me, AnimRef anim)
        {
            if (anim == null || !anim.Valid) return;

            Function.Call(Hash.STOP_ANIM_TASK, me.Handle, anim.Dict, anim.Clip, 3f);

            if (anim.Cycle == null) return;

            foreach (var clip in anim.Cycle)
            {
                if (string.IsNullOrEmpty(clip) || clip == anim.Clip) continue;

                Function.Call(Hash.STOP_ANIM_TASK, me.Handle, anim.Dict, clip, 3f);
            }
        }

        /// <summary>Removes whatever is in the hand. Used mid-meal by Swap as well as at the end.</summary>
        private void DropProp()
        {
            if (_held == null) return;

            try
            {
                if (_held.Exists()) _held.Delete();
            }
            catch (Exception ex)
            {
                Log.Once("prop-delete", "Could not remove a held prop: " + ex.Message);
            }

            _held = null;
        }

        /// <summary>Drops everything without crediting the need. For a failure mid-meal.</summary>
        /// <summary>
        /// Whether he has just picked a fight, and the meal is over.
        ///
        /// NOT SIMPLY "IS HE ARMED". Half the city eats with a pistol on their hip and this
        /// mod is not going to refuse them a sandwich; what matters is a CHANGE -- he has
        /// reached for something he was not holding when he started -- or an act: a shot, a
        /// swing, or sights going up. The weapon he had at the first bite is remembered for
        /// exactly this reason.
        ///
        /// A BARE-HANDED PUNCH IS NOT MELEE COMBAT until it lands, so the button is read as
        /// well. Only when he is unarmed: with a gun in his hand that same button is the
        /// trigger, and IS_PED_SHOOTING has that covered without guessing at intent.
        /// </summary>
        private bool Interrupted()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead) return false;

                var now = me.Weapons.Current != null ? me.Weapons.Current.Hash : WeaponHash.Unarmed;
                // A CHANGE TO SOMETHING, NOT A CHANGE TO NOTHING. The game takes a weapon
                // off him itself for some of these animations -- a synchronised scene empties
                // his hands -- and reading that as "he drew" would cancel the very clip that
                // caused it on its second frame. Putting one away is not drawing one.
                if (now != _armed && now != WeaponHash.Unarmed) return true;

                if (Function.Call<bool>(Hash.IS_PED_SHOOTING, me.Handle)) return true;
                if (Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, me.Handle)) return true;
                if (Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle)) return true;

                // AND NOT AT THE WHEEL. Attack is a driving control as well, and a meal
                // eaten through a drive-through is eaten sitting on it.
                if (now == WeaponHash.Unarmed && !me.IsInVehicle() &&
                    Game.IsControlJustPressed(Control.Attack)) return true;

                return false;
            }
            catch
            {
                // A test that cannot be made is not an interruption.
                return false;
            }
        }

        private void Abandon()
        {
            _item = null;
            _swapAt = 0;
            Cleanup();
        }

        /// <summary>
        /// Called on shutdown and on a script reload.
        ///
        /// Without it, reloading mid-bite leaves a burger welded to the player's hand with
        /// nothing left running that knows about it -- and it survives a save.
        /// </summary>
        public void Shutdown()
        {
            Abandon();

            // The empties are NOT swept up. They are handed back to the game, which clears
            // them on its own terms like any other rubbish in the world -- a reload should not
            // tidy a street somebody spent an afternoon littering. See Litter.Shutdown.
            _litter.Shutdown();
        }
    }
}
