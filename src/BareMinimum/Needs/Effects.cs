using System;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Needs
{
    /// <summary>
    /// What being hungry and being exhausted actually DO to the player.
    ///
    /// Two ideas, applied every frame and undone the moment they stop applying:
    ///
    ///  - HUNGER makes you slow, and eventually makes you walk like your stomach hurts. That
    ///    is a movement clipset, not an animation: an animation is a thing a ped plays and
    ///    finishes, a clipset REPLACES the walk/run/idle set for as long as it is set, so the
    ///    player keeps full control and simply moves like somebody in a bad way.
    ///  - NO SLEEP makes you unsteady: a loose walk, a swaying camera and some motion blur.
    ///    Deliberately mild -- the brief was a slight drunk effect, and the game's own
    ///    verydrunk set is a stagger that makes doorways impossible.
    ///  - DRINK does the same thing harder, and escalates from merry to properly gone.
    ///
    /// ONE CLIPSET AT A TIME. All three can apply at once and there is only one movement
    /// clipset slot on a ped, so they are a PRIORITY LIST: drink, then hunger, then sleep.
    /// Drink outranks the rest because it is the one the player chose to do a minute ago --
    /// being shown a hungry limp after four beers reads as the beer having done nothing.
    /// </summary>
    internal sealed class Effects
    {
        private readonly Settings _cfg;

        /// <summary>What we last asked for, so a clipset is only set when it actually changes.</summary>
        private string _applied;

        /// <summary>Whether we are the ones currently shaking the camera.</summary>
        private bool _shaking;

        private bool _drunkFlagged;

        /// <summary>Clipsets already asked for, so the request is not spammed every frame.</summary>
        private readonly System.Collections.Generic.HashSet<string> _requested =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Effects(Settings cfg)
        {
            _cfg = cfg;
        }

        public void Update(Needs needs, bool suspended)
        {
            try
            {
                var me = Game.Player.Character;

                // Suspended covers sleeping and anything else that owns the player for a
                // moment. Clearing rather than merely skipping matters: a clipset left on
                // through a sleep fade is still on when the player wakes up.
                if (suspended || me == null || !me.Exists() || me.IsDead)
                {
                    Clear();
                    return;
                }

                var hunger = needs.Hunger.Value;
                var sleep = needs.Sleep.Value;
                var drunk = _cfg.BoozeEnabled ? needs.Drunk : 0f;

                MoveRate(me, hunger, sleep);
                Clipset(me, hunger, sleep, drunk);
                Unsteady(me, sleep, drunk);
            }
            catch (Exception ex)
            {
                Log.Once("effects", "Could not apply the needs' effects: " + ex.Message);
            }
        }

        // ======================================================================
        // Walking slower
        // ======================================================================

        /// <summary>
        /// Slows the player down, by the worse of the two needs.
        ///
        /// MULTIPLIED, not picked. Being both starving and exhausted should be worse than
        /// either alone, and taking the minimum of the two would make the second one free.
        ///
        /// Applied EVERY FRAME because the override does not stick -- and skipped entirely at
        /// full rate, so a fed and rested player never has this native called at all and
        /// nothing else that wants to set their move rate has to fight us for it.
        /// </summary>
        private void MoveRate(Ped me, float hunger, float sleep)
        {
            var rate = 1f;

            if (_cfg.HungerEnabled && hunger < _cfg.HungerSlowAt)
            {
                rate *= Ramp(hunger, _cfg.HungerSlowAt, _cfg.HungerMinMoveRate);
            }

            if (_cfg.SleepEnabled && sleep < _cfg.SleepTiredAt)
            {
                rate *= Ramp(sleep, _cfg.SleepTiredAt, _cfg.SleepMinMoveRate);
            }

            rate *= WellFed(hunger, sleep);

            // Only when there is actually something to say. A fed and rested player with the
            // bonus turned off never has this native called at all, so nothing else that wants
            // to set their move rate has to fight us for it every frame.
            if (Math.Abs(rate - 1f) < 0.001f) return;

            if (rate < 0.4f) rate = 0.4f;

            Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, me.Handle, rate);
        }

        /// <summary>
        /// The reward for keeping both needs up: a little quicker on foot.
        ///
        /// BOTH, AND THE WORSE ONE DECIDES. Taking the better of the two would let somebody
        /// eat well and never sleep and still collect the bonus, which is exactly backwards --
        /// it is meant to be paid for keeping BOTH up, so the one you have neglected is the
        /// one that sets it.
        ///
        /// It can never overlap a penalty: the threshold it starts from is far above either
        /// slowdown threshold, so by the time this returns anything above 1 both of those have
        /// already returned nothing.
        /// </summary>
        private float WellFed(float hunger, float sleep)
        {
            if (_cfg.WellFedBonus <= 1.0001f) return 1f;

            // A need that is switched off should not be able to hold the bonus back -- with
            // hunger disabled its value never moves and would otherwise pin this at zero.
            var worst = 1f;

            if (_cfg.HungerEnabled) worst = Math.Min(worst, hunger);
            if (_cfg.SleepEnabled) worst = Math.Min(worst, sleep);

            var from = _cfg.WellFedAbove;
            if (worst <= from) return 1f;
            if (from >= 0.9999f) return _cfg.WellFedBonus;

            // Ramps from nothing at the threshold to the full bonus at completely full.
            var t = (worst - from) / (1f - from);
            if (t > 1f) t = 1f;

            return 1f + (_cfg.WellFedBonus - 1f) * t;
        }

        /// <summary>How far BELOW a threshold a falling value has got, 0 at it and 1 at zero.</summary>
        private static float Depth(float value, float threshold)
        {
            if (threshold <= 0.0001f) return 0f;

            var d = 1f - (value / threshold);
            return d < 0f ? 0f : d > 1f ? 1f : d;
        }

        /// <summary>How far ABOVE a threshold a rising value has got, 0 at it and 1 at one.</summary>
        private static float Rise(float value, float threshold)
        {
            var span = 1f - threshold;
            if (span <= 0.0001f) return 1f;

            var r = (value - threshold) / span;
            return r < 0f ? 0f : r > 1f ? 1f : r;
        }

        /// <summary>
        /// Slides from 1 down to floor as the value falls from threshold to zero.
        ///
        /// A ramp rather than a step, so the player feels it coming on instead of falling off
        /// a cliff the instant a number crosses a line.
        /// </summary>
        private static float Ramp(float value, float threshold, float floor)
        {
            if (threshold <= 0.0001f) return 1f;

            var t = value / threshold;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            return floor + (1f - floor) * t;
        }

        // ======================================================================
        // Walking wrong
        // ======================================================================

        /// <summary>
        /// Picks the movement clipset, streams it, and only then applies it.
        ///
        /// THE REQUEST IS THE WHOLE TRICK. SET_PED_MOVEMENT_CLIPSET on a set that is not in
        /// memory does nothing at all -- no error, no exception, no log line, and the ped
        /// simply walks normally. It has to be REQUEST_CLIP_SET'd and then waited for, which
        /// takes a few frames, so this returns and tries again next tick rather than applying
        /// something that will be ignored.
        /// </summary>
        private void Clipset(Ped me, float hunger, float sleep, float drunk)
        {
            string want = null;

            // DRINK FIRST. There is one movement clipset slot on a ped, so these are a
            // priority list rather than a set -- and drink outranks the other two because it
            // is the one the player chose to do a minute ago. Being told you are drunk when
            // you have just had four beers is the game agreeing with you; being shown a
            // hungry limp instead reads as the beer having done nothing.
            //
            // It also escalates: merry, then properly gone.
            if (drunk >= _cfg.BoozeDrunkAt)
            {
                want = drunk >= _cfg.BoozeHeavyAt ? _cfg.BoozeClipsetHeavy : _cfg.BoozeClipset;
            }
            else if (_cfg.HungerEnabled && hunger < _cfg.HungerHurtAt)
            {
                want = _cfg.HungerClipset;
            }
            else if (_cfg.SleepEnabled && sleep < _cfg.SleepDrunkAt)
            {
                want = _cfg.SleepClipset;
            }

            if (string.IsNullOrEmpty(want))
            {
                if (_applied == null) return;

                Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, me.Handle, 0.6f);
                _applied = null;
                return;
            }

            if (want == _applied) return;

            if (!Streamed(want)) return;

            // 0.5 second blend. Instant is a visible pop from one gait to another, and
            // anything longer than about a second reads as the game hitching.
            Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, me.Handle, want, 0.5f);
            _applied = want;

            Log.Debug("Movement clipset -> " + want);
        }

        /// <summary>Asks for a clipset once, and says whether it has arrived yet.</summary>
        private bool Streamed(string set)
        {
            try
            {
                if (Function.Call<bool>(Hash.HAS_CLIP_SET_LOADED, set)) return true;

                if (_requested.Add(set))
                {
                    Function.Call(Hash.REQUEST_CLIP_SET, set);
                    Log.Debug("Requested clipset " + set + ".");
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Once("clipset-" + set, "Could not stream the clipset " + set + ": " +
                                           ex.Message + " - the walk will not change.");
                return false;
            }
        }

        // ======================================================================
        // Being unsteady
        // ======================================================================

        /// <summary>
        /// The camera sway and the stumble, once exhaustion is properly bad.
        ///
        /// SHAKE_GAMEPLAY_CAM is STARTED ONCE and then modulated with
        /// SET_GAMEPLAY_CAM_SHAKE_AMPLITUDE, rather than being called every frame: calling it
        /// again restarts the shake from the beginning of its curve, so a per-frame call is a
        /// shake that never actually gets anywhere and reads as a judder.
        /// </summary>
        private void Unsteady(Ped me, float sleep, float drunk)
        {
            // THE WORSE OF THE TWO CAUSES WINS, rather than the two adding up. Exhaustion and
            // drink produce the same sway, and summing them would double a camera that is
            // already at the edge of playable -- a player who is both is not twice as unsteady
            // as one who is very drunk, they are just unsteady.
            var tired = _cfg.SleepEnabled && sleep < _cfg.SleepDrunkAt
                ? _cfg.SleepCameraShake * Depth(sleep, _cfg.SleepDrunkAt)
                : 0f;

            var sozzled = _cfg.BoozeEnabled && drunk >= _cfg.BoozeDrunkAt
                ? _cfg.BoozeCameraShake * Rise(drunk, _cfg.BoozeDrunkAt)
                : 0f;

            var amplitude = Math.Max(tired, sozzled);

            if (amplitude <= 0.001f)
            {
                StopShaking();
                SetDrunk(me, false);
                return;
            }

            try
            {
                if (!_shaking)
                {
                    Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "DRUNK_SHAKE", amplitude);
                    _shaking = true;
                }
                else
                {
                    Function.Call(Hash.SET_GAMEPLAY_CAM_SHAKE_AMPLITUDE, amplitude);
                }

                SetDrunk(me, true);
            }
            catch (Exception ex)
            {
                Log.Once("effects-shake", "Could not shake the camera: " + ex.Message);
                _shaking = false;
            }
        }

        /// <summary>
        /// The game's own drunk flag, which loosens the ped's balance and adds the blur.
        ///
        /// Toggled only on CHANGE. It is a state flag on the ped rather than a per-frame
        /// override, and setting it every frame fights whatever else may want it -- including
        /// the game's own drinking scenarios.
        /// </summary>
        private void SetDrunk(Ped me, bool on)
        {
            if (on == _drunkFlagged) return;

            try
            {
                Function.Call(Hash.SET_PED_IS_DRUNK, me.Handle, on);
                Function.Call(Hash.SET_PED_MOTION_BLUR, me.Handle, on);
                _drunkFlagged = on;
            }
            catch (Exception ex)
            {
                Log.Once("effects-drunk", "Could not set the drunk flag: " + ex.Message);
            }
        }

        private void StopShaking()
        {
            if (!_shaking) return;

            try { Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true); }
            catch { /* nothing to do about it */ }

            _shaking = false;
        }

        // ======================================================================

        /// <summary>
        /// Puts everything back exactly as it was found.
        ///
        /// Called on abort as well as on shutdown, because SHVDN reloads scripts on a keypress
        /// -- and a mod that leaves the player permanently drunk and limping every time
        /// somebody reloads is a mod that ruins a save quietly.
        /// </summary>
        public void Clear()
        {
            try
            {
                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    if (_applied != null)
                    {
                        Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, me.Handle, 0.4f);
                    }

                    // Unconditionally, not just when we set it: the flag outlives a reload, so
                    // a session that crashed while drunk has left it on with nobody tracking it.
                    Function.Call(Hash.SET_PED_IS_DRUNK, me.Handle, false);
                    Function.Call(Hash.SET_PED_MOTION_BLUR, me.Handle, false);
                    Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, me.Handle, 1f);
                }
            }
            catch (Exception ex)
            {
                Log.Once("effects-clear", "Could not clear the effects: " + ex.Message);
            }

            _applied = null;
            _drunkFlagged = false;
            StopShaking();
        }
    }
}
