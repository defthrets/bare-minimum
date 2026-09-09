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
    ///  - THIRST slows you as hunger does, and takes your WIND, which is the part you feel.
    ///    It does not take the clipset: three things already queue for the one slot on a ped
    ///    and a fourth limp would only ever be the one nobody sees. What being dry does
    ///    instead is empty the sprint meter far faster -- see Settings.ThirstWindMultiplier
    ///    and Vitals.Energy -- so it shows up on a bar already in the row rather than as a
    ///    walk you cannot tell from the hungry one.
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

        /// <summary>Whether we are the ones bending the picture. See Wobble.</summary>
        private bool _wobbling;

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
                var thirst = needs.Thirst.Value;
                var drunk = _cfg.BoozeEnabled ? needs.Drunk : 0f;

                MoveRate(me, hunger, sleep, thirst);
                Clipset(me, hunger, sleep, drunk);
                Unsteady(me, sleep, drunk);
                Wobble(sleep);
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
        private void MoveRate(Ped me, float hunger, float sleep, float thirst)
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

            // THE THIRD ONE MULTIPLIES LIKE THE OTHER TWO, which is the whole reason the floor
            // below had to move. Three ramps at their minimums is a smaller number than two.
            if (_cfg.ThirstEnabled && thirst < _cfg.ThirstSlowAt)
            {
                rate *= Ramp(thirst, _cfg.ThirstSlowAt, _cfg.ThirstMinMoveRate);
            }

            rate *= WellFed(hunger, sleep, thirst);

            // Only when there is actually something to say. A fed and rested player with the
            // bonus turned off never has this native called at all, so nothing else that wants
            // to set their move rate has to fight us for it every frame.
            if (Math.Abs(rate - 1f) < 0.001f) return;

            // THE BACKSTOP HAS TO SIT UNDER THE INI, NOT ON TOP OF IT. This was 0.4, and the
            // ini accepts either MinMoveRate down to 0.3 -- so every value from 0.30 to 0.39
            // was quietly identical to 0.40, and somebody who set 0.30 because the setting
            // says it is "the slowest hunger alone will make you walk" got 0.40 and no word
            // about it anywhere. A setting that is accepted and then overruled is worse than
            // one that is refused.
            //
            // 0.027 is not a taste decision, it is the three ranges multiplied: 0.3 for
            // hunger times 0.3 for sleep times 0.3 for thirst, which is the slowest the ramps
            // above can legitimately produce. It was 0.09 for two of them, and a third need
            // arriving without moving it would have reintroduced the exact bug the 0.09 was
            // written to fix -- a floor sitting on top of settings the ini accepts, quietly
            // overruling anybody who set all three low on purpose. Reaching it takes an ini
            // deliberately set to every minimum AND being starving, exhausted and parched at
            // the same moment, which MoveRate is explicit about wanting to be worse than any
            // one alone. The floor only catches a rate that got past the ranges some other way,
            // which is all it was ever for.
            if (rate < 0.027f) rate = 0.027f;

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
        private float WellFed(float hunger, float sleep, float thirst)
        {
            if (_cfg.WellFedBonus <= 1.0001f) return 1f;

            // A need that is switched off should not be able to hold the bonus back -- with
            // hunger disabled its value never moves and would otherwise pin this at zero.
            var worst = 1f;

            if (_cfg.HungerEnabled) worst = Math.Min(worst, hunger);
            if (_cfg.SleepEnabled) worst = Math.Min(worst, sleep);
            if (_cfg.ThirstEnabled) worst = Math.Min(worst, thirst);

            var from = _cfg.WellFedAbove;

            // A THRESHOLD OF ONE IS ANSWERED BEFORE THE RAMP, and the order is the whole bug.
            // The ini accepts WellFedAbove up to 1.0, which reads as "only at a completely
            // full meter" -- but a meter never goes ABOVE one, so the "not there yet" test
            // below was true even at full and returned before the line that grants it. The
            // one setting that asks for the strictest bonus was the one setting that switched
            // it off. Tested first, 1.0 now means exactly what it says.
            if (from >= 0.9999f) return worst >= 0.9999f ? _cfg.WellFedBonus : 1f;

            if (worst <= from) return 1f;

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
            //
            // STONE SOBER IS TESTED FIRST, AND SEPARATELY. The ini accepts DrunkAt down to 0,
            // and at 0 the comparison below is 0 >= 0 -- true for a player who has not had a
            // drink in his life, so the drunk walk was pinned on permanently. Worse, drunk is
            // FORCED to 0 up in Update when the drink system is switched off, so turning
            // [Drink] off was the one thing that could not stop it. Nothing above zero is
            // affected: DrunkAt = 0 still means "the very first sip shows", because any drink
            // at all puts drunk above zero and past this guard.
            if (drunk > 0.0001f && drunk >= _cfg.BoozeDrunkAt)
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

        // ======================================================================
        // The picture swimming
        // ======================================================================

        /// <summary>
        /// The wave over everything once the sleep meter is actually empty.
        ///
        /// A TIMECYCLE MODIFIER, not a camera shake -- the shake is already spent on the sway
        /// and adding more of it just makes the camera louder. drug_wobbly is the game's own
        /// "Wobbly" modifier: the picture breathes and bends at the edges, which is what being
        /// unable to keep your eyes open looks like from the inside.
        ///
        /// THE NAME IS CHECKED, NOT GUESSED. It is in menyooStuff\TimecycModifiers.xml on this
        /// machine, captioned "Wobbly". A timecycle name the game does not know is accepted in
        /// silence and does nothing, so the only way to be sure is to look it up first.
        ///
        /// SET AS A TRANSITION so it swims in over a couple of seconds rather than snapping
        /// on, and its strength breathes on a slow sine -- a constant wobble is a filter, one
        /// that comes and goes is somebody fighting to stay awake.
        /// </summary>
        private void Wobble(float sleep)
        {
            var want = _cfg.SleepEnabled && _cfg.SleepWobble && sleep <= 0.0001f;

            if (!want)
            {
                ClearWobble();
                return;
            }

            try
            {
                if (!_wobbling)
                {
                    Function.Call(Hash.SET_TRANSITION_TIMECYCLE_MODIFIER, WobbleCycle, 2.0f);
                    _wobbling = true;
                }

                // Between a bit over half and full, about once every three seconds.
                var swell = 0.5f + 0.5f * (float)Math.Sin(Game.GameTime / 3000.0 * Math.PI * 2.0);

                Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, 0.55f + 0.45f * swell);
            }
            catch (Exception ex)
            {
                Log.Once("effects-wobble", "Could not make the picture swim: " + ex.Message);
                _wobbling = false;
            }
        }

        private void ClearWobble()
        {
            if (!_wobbling) return;
            _wobbling = false;

            try { Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER); }
            catch { /* nothing to do about it */ }
        }

        /// <summary>The game's own "Wobbly". See Wobble for why this is not a guess.</summary>
        private const string WobbleCycle = "drug_wobbly";

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
            ClearWobble();

            // Unconditionally, for the same reason the drunk flag is: a session that reloaded
            // mid-wobble left the modifier on with nobody tracking it.
            try { Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER); }
            catch { /* nothing further to try */ }
        }
    }
}
