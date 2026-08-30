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
        /// PH_R_Hand: the non-deforming prop helper bone on the right hand.
        ///
        /// 28422, and NOT 57005 -- that is SKEL_R_Hand, the wrist joint, which is wrong for
        /// props and puts the item through the palm. Zero offset and zero rotation, which is
        /// the whole point of a prop helper: it is already where a held object should be.
        /// Learned in Overspray and paid for again in Fumes.
        /// </summary>
        private const int PropBone = 28422;

        private readonly Catalogue _menu;
        private readonly Needs.Needs _needs;

        /// <summary>How long one eat or drink stretch of a combo lasts, in milliseconds.</summary>
        private const int PhaseMs = 7000;

        private Item _item;
        private Prop _held;
        private int _finishAt;
        private bool _animStarted;

        /// <summary>Which half of a combo is in hand. Meaningless for anything else.</summary>
        private bool _drinking;

        /// <summary>When to swap hands between the food and the cup. Zero when not a combo.</summary>
        private int _swapAt;

        public Eating(Catalogue menu, Needs.Needs needs)
        {
            _menu = menu;
            _needs = needs;
        }

        /// <summary>True while something is being eaten. Stops a second one being started.</summary>
        public bool Busy => _item != null;

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

            if (item.Drink) _needs.Drink(item.Hunger, item.Wake);
            else _needs.Eat(item.Hunger);

            Cleanup();
            Report(item);
        }

        private void Report(Item item)
        {
            try
            {
                var pct = (int)Math.Round(_needs.Hunger.Value * 100f);

                var msg = "~g~" + item.Name + "~s~.  Fed " + pct + "%";

                if (item.Wake > 0f)
                {
                    msg += ", rested " + (int)Math.Round(_needs.Sleep.Value * 100f) + "%";
                }

                GTA.UI.Notification.PostTicker(msg + ".", false, false);
            }
            catch
            {
                // Not worth failing a meal over.
            }
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
                    model.Request();

                    // Bounded, and deliberately short. This runs inside the tick, so a real
                    // wait would stall every other subsystem; a prop that has not arrived in
                    // a few milliseconds is simply skipped and the food is eaten by hand.
                    var until = Game.GameTime + 120;
                    while (!model.IsLoaded && Game.GameTime < until) { }

                    if (!model.IsLoaded)
                    {
                        Log.Once("prop-slow-" + name,
                                 name + " did not stream in time - eaten empty-handed.");
                        return;
                    }
                }

                _held = World.CreateProp(model, me.Position, false, false);
                if (_held == null || !_held.Exists()) { _held = null; return; }

                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, PropBone);

                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _held.Handle, me.Handle, bone,
                              0f, 0f, 0f, 0f, 0f, 0f, true, true, false, true, 1, true);

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
            var anim = (item.Drink || drinking) ? _menu.Sip : _menu.Eat;
            if (!anim.Valid) return;

            try
            {
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, anim.Dict))
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, anim.Dict);

                    var until = Game.GameTime + 120;
                    while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, anim.Dict) &&
                           Game.GameTime < until) { }
                }

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, anim.Dict))
                {
                    Log.Once("anim-" + anim.Dict,
                             "Animation " + anim.Dict + " would not stream - eating without it.");
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
                        // The SPECIFIC task, not CLEAR_PED_TASKS. Clearing everything would
                        // also cancel whatever else the player happened to be doing -- which
                        // at a shop counter is usually nothing, and while walking is not.
                        Function.Call(Hash.STOP_ANIM_TASK, me.Handle,
                                      _menu.Eat.Dict, _menu.Eat.Clip, 3f);
                        Function.Call(Hash.STOP_ANIM_TASK, me.Handle,
                                      _menu.Sip.Dict, _menu.Sip.Clip, 3f);
                    }
                }
                catch
                {
                    // The animation ends on its own soon enough.
                }

                _animStarted = false;
            }

            DropProp();
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
