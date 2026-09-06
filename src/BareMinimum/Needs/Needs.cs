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

                // EVERY TICK, NOT ONLY ON A CLOCK STEP. The drains are charged in game hours
                // and two of those may be several frames apart, but the HUD reads this every
                // frame -- and a value that only moved when the clock did would have the bars
                // winding up a beat behind his feet.
                Effort = Working();

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
        /// <summary>
        /// Take some off both, as fractions of the whole meter.
        ///
        /// FOR THINGS THAT HAPPEN TO A BODY rather than for time passing. The drain in Tick is
        /// the clock doing its work at a rate; this is an event -- a beating, a bender, six
        /// hours face down after an overdose -- and an event does not have a rate, it has a
        /// cost. Subtracted rather than set so two of them in the same minute both land.
        ///
        /// Written straight to the meters and marked dirty, so it is on the disk with
        /// everything else at the next save rather than being a number that vanishes on a
        /// reload.
        /// </summary>
        public void Drain(float hunger, float sleep)
        {
            if (hunger > 0f) Hunger.Value = Hunger.Value - hunger;
            if (sleep > 0f) Sleep.Value = Sleep.Value - sleep;

            _dirty = true;

            Log.Info("Drained by something: Hunger " +
                     ((int)Math.Round(Hunger.Value * 100f)) + "%, Sleep " +
                     ((int)Math.Round(Sleep.Value * 100f)) + "%.");
        }

        /// <summary>
        /// Set by another mod that is about to move the clock and knows it was not rest.
        /// See Api.Pantry.NotSleep, which is the only thing that sets it.
        /// </summary>
        internal static bool NotSleepNext;

        private void Outside(float hours, bool suspended)
        {
            // SOMEBODY TOLD US. The jump is real and the hours passed -- they were simply not
            // spent asleep, so they drain like any other waking hours rather than crediting a
            // night's rest. The step has already been applied by the caller; all this has to
            // do is not hand back the sleep.
            if (NotSleepNext)
            {
                NotSleepNext = false;

                Log.Info("Something else advanced the clock " + hours.ToString("0.#") +
                         "h and says it was not rest. Counted as time awake.");

                return;
            }

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

            // Taken before Slept moves it, so the card's bar can sweep from where the meter
            // stood to where the night left it.
            var before = Sleep.Value;

            Slept(hours, _cfg.OutsideSleepQuality);

            try
            {
                // THE SAME CARD OUR OWN BED PUTS UP.
                //
                // This path is a night in SOMEBODY ELSE'S bed -- their mod moves the clock and
                // we read the jump -- and it is the path most people are actually on, because
                // most people already have a sleep mod. It was still posting the old grey
                // ticker while our own bed put up the card, so the card looked broken to
                // anybody who never used our bed: they slept, and got the thing it replaced.
                var rested = Sleep.Value;

                var whole = hours.ToString("0.#") +
                            (hours >= 1.95f ? " HOURS" : " HOUR");

                UI.Toast.Show(Moon(), "SLEPT " + whole,
                              "Rested " + ((int)Math.Round(rested * 100f)) + "%.",
                              Awake, before, rested);
            }
            catch
            {
                // Nothing to do about it.
            }
        }

        /// <summary>
        /// The blue at the far end of the moon's own ramp beside the minimap, so a wake-up
        /// card and the mark it is about are the same colour. Sleeping.cs holds the twin of
        /// this for its own bed.
        /// </summary>
        private static readonly System.Drawing.Color Awake =
            System.Drawing.Color.FromArgb(255, 96, 178, 246);

        /// <summary>
        /// Which of the five moon drawings the card wears.
        ///
        /// Asked of the need rather than worked out, because Need.Stage weights its bands --
        /// 0.80, 0.58, 0.34, 0.14 -- and a card claiming to show the same drawing as the mark
        /// beside the minimap has to actually show it.
        /// </summary>
        private string Moon()
        {
            var at = Sleep.Stage;

            if (at < 0) at = 0;
            if (at > 4) at = 4;

            return "moon" + at + ".png";
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
                // AND WORKING MAKES YOU TIRED, WHICH IT DID NOT UNTIL NOW. Sprinting cost
                // food and nothing else, so running the length of the city left him starving
                // and perfectly well rested. Stacked on the drink multiplier rather than
                // replacing it: a drunk sprint is worse than either on its own.
                Sleep.Drain(hours, _cfg.SleepHoursToEmpty, Sober() * Tiring());
            }

            Sober(hours);

            if (_cfg.StarvingCostsHealth && Hunger.Empty)
            {
                Starve(hours);
            }
            else
            {
                // Off zero again: the debt is forgiven and he will be told afresh next time.
                _healthDebt = 0f;

                // AND ANY PAIN LINE STILL RUNNING IS CUT OFF, ONCE, ON THE WAY OUT OF IT.
                // This is the frame he stopped starving, so it is the last chance to end a
                // sound we caused -- and the game has no reason of its own to stop one, which
                // is how it came to be still going long after the sandwich.
                if (_toldStarving) Hush();

                _toldStarving = false;
            }

            _dirty = true;
        }

        /// <summary>
        /// How hard he is working right now, nought to one. Read every tick.
        ///
        /// ONE READING, THREE CONSUMERS: it makes hunger go faster, it makes sleep go faster,
        /// and it winds the gauge up -- see Settings.HudBarEffort. Each of the three scales it
        /// by a dial of its own, which is exactly why this is a bare fraction and not any one
        /// of their multipliers. It used to BE the hunger multiplier, so anything else wanting
        /// to know how hard he was working would have been reading a hunger setting to decide
        /// how fast to draw.
        /// </summary>
        public float Effort { get; private set; }

        /// <summary>What he is actually doing, as a fraction. See Effort.</summary>
        private static float Working()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return 0f;

                // Swimming and climbing are all of it: there is no gentle version of either.
                if (me.IsSprinting || me.IsSwimming || me.IsClimbing) return 1f;

                // A jog is not half a sprint -- it is a day's walking done faster -- and 0.45
                // is where the hunger drain has had it since that was written.
                if (me.IsRunning) return 0.45f;
            }
            catch
            {
                // Treat an unreadable ped as standing still.
            }

            return 0f;
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
        /// How much harder than standing about he is working, as a HUNGER multiplier.
        ///
        /// A reading of Effort on hunger's own dial. Sitting in a car is not exertion and is
        /// not treated as rest either -- a long drive should still make you hungry, just no
        /// faster than walking would.
        /// </summary>
        private float Exertion()
        {
            return 1f + Effort * (_cfg.HungerExertionMultiplier - 1f);
        }

        /// <summary>The same reading on SLEEP's dial, which is a gentler one.</summary>
        private float Tiring()
        {
            return 1f + Effort * (_cfg.SleepExertionMultiplier - 1f);
        }

        /// <summary>
        /// An empty stomach costing health, slowly.
        ///
        /// ACCUMULATED RATHER THAN APPLIED PER TICK. A frame is worth a few thousandths of a
        /// health point and Ped.Health is an integer, so rounding every frame either loses the
        /// lot to truncation or costs a whole point sixty times a second. The debt is kept as
        /// a float and spent in bites.
        ///
        /// It will never kill: the floor is deliberately well above zero, because dying of a
        /// meter the player may not have noticed is the fastest way for a mod like this to be
        /// uninstalled. It takes you to the edge and leaves you there.
        /// </summary>
        /// <summary>Whether he has already been told, this spell of it. Reset when he eats.</summary>
        private bool _toldStarving;

        /// <summary>
        /// How big a bite it takes, and how far down it is willing to go.
        ///
        /// BOTH OF THESE ARE ABOUT THE SOUND, not the arithmetic.
        ///
        /// Writing a ped's health is a damage event as far as the game is concerned, and it
        /// answers with a pain grunt. Spending the debt the moment it was worth a SINGLE point
        /// therefore had him wincing every eight seconds for as long as he was hungry, which
        /// nobody heard as a health system -- they heard a sound stuck on. Five at a time is
        /// the same damage a fifth as often.
        ///
        /// The floor was a sixth of full health, which parks him inside the band where the
        /// game plays its own laboured breathing -- permanently, because starving holds him
        /// exactly there and nothing else moves him off it. That is the loop that would not
        /// end. A third of the way up is above the band and still plainly dangerous: it is one
        /// gunfight from dying, which is what "the edge" was always meant to mean.
        /// </summary>
        private const float StarveBite = 5f;
        private const float StarveFloor = 0.35f;

        private void Starve(float hours)
        {
            _healthDebt += _cfg.StarvingHealthPerHour * hours;
            if (_healthDebt < StarveBite) return;

            // SAID ONCE, THE FIRST TIME IT ACTUALLY COSTS ANYTHING -- not when the meter hits
            // zero. Health going down with no explanation is the mod looking broken, and the
            // moment it starts is the moment worth saying it. The latch clears above, so a
            // second spell of it is announced afresh.
            if (!_toldStarving)
            {
                _toldStarving = true;

                try
                {
                    GTA.UI.Notification.PostTicker(
                        "~r~You are starving.~s~ It is costing you health. Eat something.",
                        false, false);
                }
                catch
                {
                    // The health still goes.
                }
            }

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var floor = (int)Math.Max(20f, me.MaxHealth * StarveFloor);
                if (me.Health <= floor) { _healthDebt = 0f; return; }

                var take = (int)_healthDebt;
                _healthDebt -= take;

                me.Health = Math.Max(floor, me.Health - take);

                // AND THE GRUNT THAT CAUSED IS CUT OFF BEHIND US. We asked for the damage, so
                // we clean up after it rather than leaving him wincing at nothing: from the
                // player's side that sound has no event to belong to, and one that keeps
                // arriving out of nowhere is precisely the one that reads as broken.
                Hush();
            }
            catch (Exception ex)
            {
                _healthDebt = 0f;
                Log.Once("needs-starve", "Could not apply starvation damage: " + ex.Message);
            }
        }

        /// <summary>
        /// Stops whatever the player is saying this instant.
        ///
        /// Only ever used to end a line WE caused. It is not a general mute -- it kills the
        /// one speech playing right now and nothing after it, so anything the game has a real
        /// reason to say next still gets said.
        /// </summary>
        private static void Hush()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, me.Handle);
            }
            catch
            {
                // A grunt too many is not worth an exception.
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
