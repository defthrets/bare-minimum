using System;
using System.Collections.Generic;
using System.Globalization;
using GTA;
using GTA.Chrono;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Needs
{
    /// <summary>
    /// The two needs, the game clock they run on, and the save file they live in.
    ///
    /// EVERYTHING HERE IS MEASURED IN GAME HOURS, not real seconds. That is the single
    /// decision the rest of the mod hangs off, and it is worth the paragraph:
    ///
    ///  - It is the clock the player is already living on. They sleep by it and shops open by
    ///    it; a hunger meter running on real minutes drifts out of step with all of it.
    ///  - It PAUSES WHEN THE GAME DOES, for free. A real-time meter drains through the pause
    ///    menu, through a loading screen and through an alt-tab, and the player comes back to
    ///    a starving character having done nothing.
    ///  - It follows the timescale. Anybody running a mod that slows or speeds the clock gets
    ///    needs that still match their world, with nothing to reconfigure.
    /// </summary>
    internal sealed class Needs
    {
        /// <summary>
        /// The most game time one tick is allowed to be worth.
        ///
        /// A frame is a fraction of a game minute; anything past half an hour is not a frame,
        /// it is a JUMP -- a loading screen, a mission cutscene, a fast travel, or this mod's
        /// own sleep. Draining across it would empty the meter in a single frame for reasons
        /// that have nothing to do with the player.
        ///
        /// Sleep is not lost by this: Sleeping calls Slept() explicitly, which applies the
        /// drain for those hours deliberately, at the sleeping rate.
        /// </summary>
        private const float MaxStepHours = 0.5f;

        private readonly Settings _cfg;

        public readonly Need Hunger = new Need(Kind.Hunger);
        public readonly Need Sleep = new Need(Kind.Sleep);

        /// <summary>Every character's saved state, keyed by the model name below.</summary>
        private readonly Dictionary<string, float[]> _saved =
            new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whose needs are currently in Hunger and Sleep.</summary>
        private string _who;

        /// <summary>
        /// The game clock as of the previous tick, for working out the step.
        ///
        /// GameClockDateTime rather than DateTime: World.CurrentDate is obsolete in SHVDN 3.9
        /// because DateTime cannot represent the year range the game supports, and building on
        /// a deprecation that already warns at compile time is borrowing a problem.
        /// </summary>
        private GameClockDateTime _lastClock;
        private bool _clockPrimed;

        private bool _dirty;
        private float _sinceSave;

        /// <summary>Health carried over between ticks, so a fractional loss is not rounded away.</summary>
        private float _healthDebt;

        /// <summary>
        /// How drunk, 0 to 1. NOT a Need, and deliberately not modelled as one.
        ///
        /// Every Need in this mod runs 1 = good, 0 = trouble, and the whole HUD, colour ramp
        /// and threshold vocabulary is built on that. Drunkenness runs the other way -- 0 is
        /// fine and 1 is trouble -- so making it a Need would have put a sign inversion inside
        /// a class whose entire job is that the sign never varies.
        /// </summary>
        public float Drunk { get; private set; }

        public Needs(Settings cfg)
        {
            _cfg = cfg;
            Load();
        }

        // ======================================================================
        // The tick
        // ======================================================================

        public void Update(float realSeconds, bool suspended)
        {
            try
            {
                SwitchCharacterIfNeeded();

                bool jumped;
                var hours = Step(out jumped);

                // A JUMP IS NOT A TICK, AND IT IS USUALLY A NIGHT. Anything past MaxStepHours
                // did not happen frame by frame, so it must not be drained frame by frame --
                // but throwing it away entirely means another mod's bed leaves the player just
                // as tired as they lay down. Outside() decides which it was.
                if (jumped) Outside(hours, suspended);
                else if (hours > 0f) Drain(hours);

                Save(realSeconds);
            }
            catch (Exception ex)
            {
                Log.Once("needs-update", "The needs could not be updated: " + ex.Message);
            }
        }

        /// <summary>
        /// How many GAME hours have passed since the last tick, ignoring jumps.
        ///
        /// Returns 0 rather than a negative number when the clock goes backwards, which it
        /// does whenever anything sets the time -- a mission, a trainer, or the player.
        /// </summary>
        private float Step(out bool jumped)
        {
            jumped = false;

            GameClockDateTime now;

            try
            {
                now = GameClock.Now;
            }
            catch (Exception ex)
            {
                Log.Once("needs-clock", "Could not read the game clock: " + ex.Message +
                                        " - the needs will not move.");
                return 0f;
            }

            if (!_clockPrimed)
            {
                _lastClock = now;
                _clockPrimed = true;
                return 0f;
            }

            var span = now - _lastClock;
            _lastClock = now;

            var hours = (float)span.TotalHours;

            if (hours <= 0f) return 0f;

            if (hours > MaxStepHours)
            {
                jumped = true;
                return hours;
            }

            return hours;
        }

        /// <summary>
        /// A jump in the clock that this mod did not cause. Usually somebody else's bed.
        ///
        /// THIS IS HOW ANOTHER MOD'S SLEEP COUNTS. Posted Up and Hoodrich both put Franklin to
        /// bed and neither shares an API with this one -- but both move the clock, and so does
        /// a mission that skips a night. Reading the CLOCK rather than the mod means it works
        /// for all of them at once, including ones not written yet, with no dependency on
        /// anybody's dll.
        ///
        /// Bounded at both ends: under the floor it is a cutscene nudging the time along,
        /// over the ceiling it is a fast travel or a trainer, and crediting those would make
        /// the sleep half of this mod free to anybody with a time-of-day slider.
        /// </summary>
        private void Outside(float hours, bool suspended)
        {
            // Our own sleep never reaches here -- Sleeping calls Slept() and re-primes the
            // clock inside the same tick -- but a fade we are staging is not the moment to be
            // interpreting the clock either.
            if (suspended || !_cfg.CreditOutsideSleep)
            {
                Log.Debug("Ignoring a " + hours.ToString("0.0") + " game-hour jump.");
                return;
            }

            if (hours < _cfg.OutsideSleepMinHours || hours > _cfg.OutsideSleepMaxHours)
            {
                Log.Debug("Ignoring a " + hours.ToString("0.0") +
                          " game-hour jump - outside the sleep window.");
                return;
            }

            Log.Info("Something else advanced the clock " + hours.ToString("0.#") +
                     "h - counting it as sleep.");

            Slept(hours, _cfg.OutsideSleepQuality);

            try
            {
                GTA.UI.Notification.PostTicker(
                    "~b~Slept~s~ " + hours.ToString("0.#") + "h.  Rested " +
                    ((int)Math.Round(Sleep.Value * 100f)) + "%.", false, false);
            }
            catch
            {
                // Nothing to do about it.
            }
        }

        private void Drain(float hours)
        {
            if (_cfg.HungerEnabled)
            {
                Hunger.Drain(hours, _cfg.HungerHoursToEmpty, Exertion());
            }

            if (_cfg.SleepEnabled)
            {
                // DRINK MAKES YOU TIRED FASTER. Not a separate timer -- it scales the sleep
                // drain itself, so one beer barely registers and a session of them costs most
                // of a day's rest without any new meter to read.
                Sleep.Drain(hours, _cfg.SleepHoursToEmpty, Sober());
            }

            Sober(hours);

            if (_cfg.StarvingCostsHealth && Hunger.Empty) Starve(hours);

            _dirty = true;
        }

        /// <summary>How much faster tiredness comes on, given how drunk they are.</summary>
        private float Sober()
        {
            if (!_cfg.BoozeEnabled || Drunk <= 0f) return 1f;

            return 1f + Drunk * (_cfg.BoozeSleepMultiplier - 1f);
        }

        /// <summary>
        /// Wearing off, over game hours.
        ///
        /// On the game clock like everything else here, which means SLEEPING IT OFF WORKS for
        /// free: a night in a bed advances the clock eight hours and Slept() runs this for
        /// those hours, so you wake up sober without a line of code saying so.
        /// </summary>
        private void Sober(float hours)
        {
            if (Drunk <= 0f || _cfg.BoozeHoursToSober <= 0.0001f) return;

            Drunk = Clamp01(Drunk - hours / _cfg.BoozeHoursToSober);
            _dirty = true;
        }

        /// <summary>Another drink. Returns how drunk it left them.</summary>
        public float Booze(float amount)
        {
            if (amount <= 0f || !_cfg.BoozeEnabled) return Drunk;

            Drunk = Clamp01(Drunk + amount);
            _dirty = true;
            SaveNow();

            return Drunk;
        }

        /// <summary>
        /// How much harder than standing about the player is currently working.
        ///
        /// Sitting in a car is NOT exertion and is not treated as rest either -- a long drive
        /// should still make you hungry, just no faster than walking would.
        /// </summary>
        private float Exertion()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return 1f;

                if (me.IsSprinting || me.IsSwimming || me.IsClimbing)
                    return _cfg.HungerExertionMultiplier;

                if (me.IsRunning) return 1f + (_cfg.HungerExertionMultiplier - 1f) * 0.45f;
            }
            catch
            {
                // Treat an unreadable ped as standing still.
            }

            return 1f;
        }

        /// <summary>
        /// An empty stomach costing health, slowly.
        ///
        /// ACCUMULATED RATHER THAN APPLIED PER TICK. A frame is worth a few thousandths of a
        /// health point, and Ped.Health is an integer -- so rounding every frame either loses
        /// the lot to truncation or costs a whole point sixty times a second. The debt is kept
        /// as a float and spent only once it is worth a point.
        ///
        /// It will never kill: the floor is deliberately above zero, because dying of a meter
        /// the player may not have noticed is the fastest way for a mod like this to be
        /// uninstalled. It takes you to the edge and leaves you there.
        /// </summary>
        private void Starve(float hours)
        {
            _healthDebt += _cfg.StarvingHealthPerHour * hours;
            if (_healthDebt < 1f) return;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var floor = Math.Max(20, me.MaxHealth / 6);
                if (me.Health <= floor) { _healthDebt = 0f; return; }

                var take = (int)_healthDebt;
                _healthDebt -= take;

                me.Health = Math.Max(floor, me.Health - take);
            }
            catch (Exception ex)
            {
                _healthDebt = 0f;
                Log.Once("needs-starve", "Could not apply starvation damage: " + ex.Message);
            }
        }

        // ======================================================================
        // Sleeping
        // ======================================================================

        /// <summary>
        /// Applied when the player has actually slept, after the clock has been moved on.
        ///
        /// The tick deliberately ignores the jump this makes, so the hunger those hours cost
        /// has to be charged here or a night's sleep would be free.
        /// </summary>
        public void Slept(float gameHours, float restoreFraction)
        {
            if (gameHours <= 0f) return;

            if (_cfg.HungerEnabled)
            {
                Hunger.Drain(gameHours, _cfg.HungerHoursToEmpty, _cfg.HungerSleepMultiplier);
            }

            if (_cfg.SleepEnabled)
            {
                // AGAINST HoursToFull, NOT HoursToEmpty. Recovery is not the reverse of
                // decline at the same rate: you wear down over forty-four waking hours and
                // recover over about twelve in bed. Dividing by the drain figure made a
                // six-hour night worth a seventh of a meter, which reads as sleeping being
                // broken rather than as sleeping being slow.
                var gained = (gameHours / _cfg.SleepHoursToFull) * restoreFraction;
                Sleep.Restore(gained);
            }

            // Slept off, at the same rate it would have worn off awake.
            Sober(gameHours);

            // The jump has already happened by the time this is called, so the clock has to be
            // re-primed or the NEXT tick sees the whole night as its own step and ignores it --
            // which is harmless, but it also would not re-prime, and the step after that would
            // be wrong too.
            try { _lastClock = GameClock.Now; } catch { _clockPrimed = false; }

            _dirty = true;
            SaveNow();

            Log.Info("Slept " + gameHours.ToString("0.#") + "h (quality " +
                     restoreFraction.ToString("0.00") + "). " + Hunger + ", " + Sleep + ".");
        }

        // ======================================================================
        // Eating
        // ======================================================================

        /// <summary>Puts food in. Returns what actually landed, so the caller can say so.</summary>
        public float Eat(float amount)
        {
            var got = Hunger.Restore(amount);
            if (got > 0f) { _dirty = true; SaveNow(); }
            return got;
        }

        /// <summary>
        /// A drink, which tops up hunger a little and takes the edge off tiredness.
        ///
        /// Caffeine is the reason sleep gets anything at all: a coffee or an energy drink that
        /// did nothing for tiredness would be a strictly worse sandwich. It is deliberately
        /// small and it is NOT a substitute for sleeping -- see the cap in Wake().
        /// </summary>
        public void Drink(float food, float wake)
        {
            if (food > 0f) Hunger.Restore(food);
            if (wake > 0f) Wake(wake);

            _dirty = true;
            SaveNow();
        }

        /// <summary>
        /// Caffeine, with a ceiling.
        ///
        /// It will carry you up to the point where the effects stop, and no further. Without
        /// the cap a pocketful of energy drinks is a permanent replacement for sleep, and the
        /// entire sleep half of the mod becomes a shopping list.
        /// </summary>
        /// <summary>
        /// Puts some of the sleep meter back. Coffee, sugar, nicotine.
        ///
        /// PUBLIC because it is not only drinks. It used to be called from Drink alone, which
        /// silently threw away the wake value on twenty solid food items -- every donut,
        /// every cigarette, the soups, the breakfast. See Eating.Finish.
        /// </summary>
        public void Wake(float amount)
        {
            var ceiling = Math.Min(1f, _cfg.SleepTiredAt + 0.12f);
            if (Sleep.Value >= ceiling) return;

            Sleep.Value = Math.Min(ceiling, Sleep.Value + amount);
        }

        // ======================================================================
        // Whose needs these are
        // ======================================================================

        /// <summary>
        /// The player's model name, which is how the three protagonists are told apart.
        ///
        /// Kept PER CHARACTER because they are three different people. Switching from Franklin
        /// to Trevor and inheriting Franklin's empty stomach is the kind of detail that reads
        /// as a bug even to somebody who could not say why.
        /// </summary>
        /// <summary>
        /// INTERNAL rather than private, because the pantry keys on it too.
        ///
        /// One definition of "which character is this" for the whole mod. Two would drift the
        /// first time somebody added a fourth playable ped, and a bag that disagrees with a
        /// stomach about whose it is would be a very confusing bug to read.
        /// </summary>
        internal static string Who()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return "unknown";

                switch ((uint)me.Model.Hash)
                {
                    case 0x9B22DBAFu: return "michael";    // player_zero
                    case 0x9B810FA2u: return "franklin";   // player_one
                    case 0x9B0083C0u: return "trevor";     // player_two
                }

                return "ped_" + ((uint)me.Model.Hash).ToString("x8", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>Parks the current character's numbers and picks up the new one's.</summary>
        private void SwitchCharacterIfNeeded()
        {
            var now = Who();
            if (now == _who) return;

            if (_who != null)
            {
                _saved[_who] = new[] { Hunger.Value, Sleep.Value, Drunk };
                SaveNow();
            }

            _who = now;

            if (_saved.TryGetValue(now, out var v) && v.Length >= 2)
            {
                Hunger.Value = v[0];
                Sleep.Value = v[1];

                // Length-checked separately: a needs.json written before drink existed has
                // two entries, and reading a third would throw on every old save.
                Drunk = v.Length >= 3 ? v[2] : 0f;
            }
            else
            {
                Hunger.Value = 1f;
                Sleep.Value = 1f;
                Drunk = 0f;
            }

            // A switch is also a teleport in time as far as the clock is concerned.
            _clockPrimed = false;

            Log.Info("Now playing as " + now + ": " + Hunger + ", " + Sleep + ".");
        }

        // ======================================================================
        // Disk
        // ======================================================================

        private void Load()
        {
            try
            {
                var doc = JsonFile.Read(Paths.StateFile, out var how);

                if (how != ReadResult.Ok || doc == null || doc.IsNull)
                {
                    Log.Info("No saved needs yet - starting fed and rested.");
                    return;
                }

                var people = doc["characters"];

                foreach (var key in people.Keys)
                {
                    var node = people[key];
                    _saved[key] = new[]
                    {
                        Clamp01(node["hunger"].AsFloat(1f)),
                        Clamp01(node["sleep"].AsFloat(1f)),
                        Clamp01(node["drunk"].AsFloat(0f))
                    };
                }

                Log.Info("Loaded needs for " + _saved.Count + " character(s).");
            }
            catch (Exception ex)
            {
                Log.Error("Could not read " + Paths.StateFile + " - starting fresh.", ex);
            }
        }

        /// <summary>Writes at most once every few seconds, and only when something moved.</summary>
        private void Save(float realSeconds)
        {
            if (!_dirty) return;

            _sinceSave += realSeconds;
            if (_sinceSave < 10f) return;

            SaveNow();
        }

        public void SaveNow()
        {
            _sinceSave = 0f;

            if (!_dirty) return;
            _dirty = false;

            try
            {
                if (_who != null) _saved[_who] = new[] { Hunger.Value, Sleep.Value, Drunk };

                var people = Json.Object();
                foreach (var pair in _saved)
                {
                    people.Set(pair.Key, Json.Object()
                        .Set("hunger", Math.Round(pair.Value[0], 4))
                        .Set("sleep", Math.Round(pair.Value[1], 4))
                        .Set("drunk", Math.Round(pair.Value.Length >= 3 ? pair.Value[2] : 0f, 4)));
                }

                var doc = Json.Object()
                    .Set("version", 1)
                    .Set("characters", people);

                if (!JsonFile.Write(Paths.StateFile, doc))
                {
                    Log.Once("needs-save", "Could not write " + Paths.StateFile +
                                           " - needs will not survive this session.");
                }
            }
            catch (Exception ex)
            {
                Log.Once("needs-save-ex", "Saving the needs failed: " + ex.Message);
            }
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return float.IsNaN(v) ? 1f : v;
        }
    }
}
