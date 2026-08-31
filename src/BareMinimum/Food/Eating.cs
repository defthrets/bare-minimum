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
        private bool _animStarted;

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

        public Eating(Catalogue menu, Needs.Needs needs, Speech speech)
        {
            _menu = menu;
            _needs = needs;
            _speech = speech;
        }

        /// <summary>True while something is being eaten. Stops a second one being started.</summary>
        public bool Busy => _item != null;

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

                AskAnim(_menu.Eat);
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

        /// <summary>Starts consuming an item that has already been paid for.</summary>
        public bool Begin(Item item)
        {
            if (item == null || Busy) return false;

            var me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead) return false;

            var driving = InVehicle(me);

            // A meal at the wheel runs on its own clock: you pick at it between junctions
            // rather than putting it away in four seconds at a serving window.
            var seconds = driving && item.VehicleSeconds > 0f ? item.VehicleSeconds : item.Seconds;

            // BEFORE THE PROP AND THE ANIMATION, so the line lands while his hands are
            // still empty. Said after, he is thanking the cashier around a mouthful.
            _speech.Say(item.Smoke ? "smoke" : item.Booze > 0f ? "booze" : "buy");

            _item = item;
            _animStarted = false;
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
            if (_item == null) return;

            try
            {
                var now = Game.GameTime;

                if (now >= _finishAt) { Finish(); return; }

                // KEEP TRYING TO START IT. Animate asks for a dictionary and gives up for
                // that frame rather than spinning on it, so the very first attempt fails
                // whenever the anim was not already in memory -- which is every purchase from
                // a one-key stall, because only the shelf menu pre-loads as you scroll. One
                // retry a frame for the length of the meal costs nothing and is the difference
                // between an animation that usually plays and one that always does.
                if (!_animStarted)
                {
                    var me = Game.Player.Character;

                    if (me != null && me.Exists() && !me.IsDead)
                    {
                        Animate(me, _item, _drinking);
                    }
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
            if (item.Drink)
            {
                _needs.Drink(item.Hunger, item.Wake);
            }
            else
            {
                _needs.Eat(item.Hunger);
                if (item.Wake > 0f) _needs.Wake(item.Wake);
            }

            if (item.Booze > 0f) _needs.Booze(item.Booze);

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
        private void Report(Item item)
        {
            try
            {
                var msg = "~g~" + item.Name + "~s~.  ";

                if (item.Smoke)
                {
                    // No stomach reading to give and no drunk state to report -- but it does
                    // DO something, and saying so is the difference between a cigarette that
                    // works and one the player has no reason to believe in.
                    msg += "That is better.";

                    if (item.Wake > 0f)
                    {
                        msg += "  Rested " + (int)Math.Round(_needs.Sleep.Value * 100f) + "%.";
                    }
                }
                else if (item.Booze > 0f)
                {
                    msg += Tipsy();
                }
                else
                {
                    msg += "Fed " + (int)Math.Round(_needs.Hunger.Value * 100f) + "%";

                    if (item.Wake > 0f)
                    {
                        msg += ", rested " + (int)Math.Round(_needs.Sleep.Value * 100f) + "%";
                    }

                    msg += ".";
                }

                GTA.UI.Notification.PostTicker(msg, false, false);
            }
            catch
            {
                // Not worth failing a meal over.
            }
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

                var anim = item.Smoke ? _menu.Smoke
                         : (item.Drink || drinking) ? _menu.Sip
                         : _menu.Eat;

                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle,
                                              anim.LeftHanded ? LeftHandBone : RightHandBone);

                // The last six arguments are Fumes' proven set for a prop in a hand:
                // no soft pinning, no collision, not treated as a ped, vertex 2, fixed
                // rotation. The version here used to pass a different combination copied from
                // a general-purpose example, which is not what a held object wants.
                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _held.Handle, me.Handle, bone,
                              0f, 0f, 0f, 0f, 0f, 0f, false, false, false, false, 2, true);

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

        // ======================================================================
        // The animation
        // ======================================================================

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
            var anim = item.Smoke ? _menu.Smoke
                     : (item.Drink || drinking) ? _menu.Sip
                     : _menu.Eat;

            // Checked against this build before anything is asked of it. A dictionary that
            // is not here never loads, so without this the code below requests it forever and
            // plays nothing -- which is exactly how the smoking animation shipped: one
            // guessed name, and a single log line saying it "was not loaded YET" for good.
            anim.Resolve(item.Smoke ? "Smoking animation"
                                    : (item.Drink || drinking) ? "Drinking animation"
                                    : "Eating animation");

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

                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, anim.Dict, anim.Clip,
                              4f, -4f, -1, 49, 0f, false, false, false);

                _animStarted = true;
            }
            catch (Exception ex)
            {
                Log.Once("anim-fail-" + anim.Dict, "Could not play " + anim.Dict + ": " + ex.Message);
            }
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
                _scenarioStarted = false;
            }

            DropProp();
        }

        /// <summary>Ends one animation, if it was ever given a real dictionary to play.</summary>
        private static void Stop(Ped me, AnimRef anim)
        {
            if (!anim.Valid) return;

            Function.Call(Hash.STOP_ANIM_TASK, me.Handle, anim.Dict, anim.Clip, 3f);
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
        }
    }
}
