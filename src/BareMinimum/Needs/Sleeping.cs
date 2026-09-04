using System;
using GTA;
using GTA.Chrono;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.Venues;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.Needs
{
    /// <summary>Where the player is sleeping, which decides how much good it does.</summary>
    internal enum Bunk
    {
        None,
        Bed,
        Car
    }

    /// <summary>
    /// Going to sleep: the prompt, the fade, the hours, and the state put back.
    ///
    /// A STATE MACHINE RATHER THAN Script.Wait(). SHVDN runs each script on its own fiber, so
    /// waiting inside a tick is legal and would read far more simply -- and it would also stop
    /// the needs, the effects and the HUD dead for the whole sequence, because they are ticked
    /// from the same handler. Every subsystem keeps running through a sleep this way, which is
    /// what lets the HUD hide itself and the effects stand down cleanly rather than freezing
    /// mid-limp.
    /// </summary>
    internal sealed class Sleeping
    {
        private enum Phase
        {
            Idle,
            FadingOut,
            Resting,
            FadingIn
        }

        /// <summary>Milliseconds of fade each way. The game's own default is about this.</summary>
        private const int FadeMs = 800;

        /// <summary>
        /// How long the black screen is held, in real milliseconds.
        ///
        /// Long enough to read as a night passing, short enough that nobody reaches for the
        /// keyboard. It is NOT scaled by the hours slept: eight hours and four hours want the
        /// same beat, and a sleep that takes twice as long in real time to skip twice as much
        /// game time is just a longer wait for the same thing.
        /// </summary>
        private const int RestMs = 2200;

        private readonly Core.Settings _cfg;
        private readonly Needs _needs;
        private readonly Beds _beds;

        private Phase _phase = Phase.Idle;
        private int _phaseUntil;

        private Bunk _bunk;
        private float _hours;

        /// <summary>Whether the player's control was taken, so it is only ever handed back once.</summary>
        private bool _tookControl;

        /// <summary>
        /// The flags used to take control away and to give it back, in ONE place.
        ///
        /// None, deliberately: it is the exact equivalent of the old setter without the bug.
        /// It lives here as a constant rather than being written out at both call sites
        /// because the documented failure mode is the two calls disagreeing.
        /// </summary>
        private const SetPlayerControlFlags ControlFlags = SetPlayerControlFlags.None;

        private readonly Knock _knock;

        public Sleeping(Core.Settings cfg, Needs needs, Beds beds, Knock knock)
        {
            _cfg = cfg;
            _needs = needs;
            _beds = beds;
            _knock = knock;
        }

        /// <summary>True while the sequence owns the screen. The HUD and effects stand down.</summary>
        public bool Busy => _phase != Phase.Idle;

        // ======================================================================

        public void Update()
        {
            try
            {
                if (_phase != Phase.Idle) { Advance(); return; }

                if (!_cfg.SleepEnabled) return;

                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead) return;

                var where = Offer(me);

                if (where == Bunk.None)
                {
                    // Walked away or drove off: forget the offer so it shows again next time.
                    if (_offering) { _offering = false; Hud.ClearHelp(); }
                    return;
                }

                if (!_offering)
                {
                    _offering = true;
                    _offeredAt = Game.GameTime;
                    _promptDone = false;
                }

                Prompt(where);

                if (Pressed()) Begin(where);
            }
            catch (Exception ex)
            {
                Log.Once("sleeping", "The sleep check failed: " + ex.Message);
                Abandon();
            }
        }

        /// <summary>
        /// Whether anything here is offering a sleep, and what kind.
        ///
        /// The car is tested FIRST and returns outright. Testing the bed first would mean
        /// scanning for bed props on every frame the player spends driving, which is most of
        /// them -- and a car parked in a garage is near enough to a bed in the flat above to
        /// find one.
        /// </summary>
        private Bunk Offer(Ped me)
        {
            if (me.IsInVehicle())
            {
                if (!_cfg.SleepInCars) return Bunk.None;

                var v = me.CurrentVehicle;
                if (v == null || !v.Exists()) return Bunk.None;

                // NOT ON A BIKE. You cannot doze off sitting astride a motorcycle, and being
                // offered it there is the mod not knowing what it is looking at. The test is
                // for a thing you can SIT BACK in -- a car, a van, a bus, a truck -- rather
                // than a list of what to exclude, because a list of exclusions misses the
                // next odd vehicle and a whitelist just declines it.
                if (!Enclosed(v)) return Bunk.None;

                // Stopped AND switched off. Dozing off at the lights is not a nap, and an
                // idling engine is the difference between parking up and pausing.
                if (v.IsEngineRunning) return Bunk.None;
                if (!Function.Call<bool>(Hash.IS_VEHICLE_STOPPED, v.Handle)) return Bunk.None;

                // Not while being shot at or chased. A wanted level is the one state where a
                // fade to black is actively unfair.
                if (Game.Player.Wanted.WantedLevel > 0) return Bunk.None;

                return Bunk.Car;
            }

            if (!_cfg.SleepInBeds) return Bunk.None;
            if (Game.Player.Wanted.WantedLevel > 0) return Bunk.None;

            var bed = _beds.Nearest(me.Position, _cfg.BedReach);
            return bed != null ? Bunk.Bed : Bunk.None;
        }

        /// <summary>
        /// Whether this is something you could actually sleep in.
        ///
        /// A WHITELIST, not a list of exclusions. Bikes, bicycles and quads are the obvious
        /// ones to rule out, but so are jet skis, and asking "is it a car or a van" answers
        /// all of them at once -- including the next odd vehicle nobody thought of, which a
        /// blacklist would silently allow.
        /// </summary>
        private static bool Enclosed(Vehicle v)
        {
            try
            {
                var m = v.Model;

                if (m.IsBike || m.IsBicycle || m.IsQuadBike || m.IsJetSki) return false;

                // IsBigVehicle, not IsTruck -- SHVDN 3.9 has no IsTruck, and a lorry cab
                // is exactly the sort of thing somebody would expect to sleep in.
                return m.IsCar || m.IsVan || m.IsBus || m.IsBigVehicle;
            }
            catch
            {
                // Unreadable model: decline rather than offer a nap on something odd.
                return false;
            }
        }

        /// <summary>
        /// The offer, in the game's own help box.
        ///
        /// It says what you will GET, not just that you can sleep: a player looking at a full
        /// meter needs a reason not to bother, and one looking at an empty one needs to know a
        /// car is worth less than a bed before they settle for the car.
        /// </summary>
        /// <summary>How long the offer stays on screen before it gets out of the way.</summary>
        private const int PromptMs = 2000;

        private bool _offering;
        private int _offeredAt;
        private bool _promptDone;

        /// <summary>
        /// The offer: the button, the verb, and nothing else.
        ///
        /// It used to append the hours and a line about a car being a poor night. That is
        /// true, and it is also a paragraph pinned to the corner of the screen for as long as
        /// you stand near a bed -- read once on the first night and in the way every night
        /// after. The hours are in the ini and the F7 menu for anybody who wants the number.
        ///
        /// AND IT LEAVES AFTER TWO SECONDS. The key keeps working the whole time you are in
        /// range; only the words go. A prompt that never leaves stops being a prompt and
        /// becomes furniture.
        ///
        /// CLEAR_ALL_HELP_MESSAGES is what actually removes it. Simply not redrawing is not
        /// enough -- the game's help box has its own several-second lifetime once fed, so it
        /// would sit there long past its welcome and fade on a schedule of its own.
        /// </summary>
        private void Prompt(Bunk where)
        {
            if (_promptDone) return;

            if (Game.GameTime - _offeredAt >= PromptMs)
            {
                _promptDone = true;
                Hud.ClearHelp();
                return;
            }

            Hud.Help("Press ~INPUT_CONTEXT~ to " +
                     (where == Bunk.Bed ? "Sleep" : "Doze off") + ".");
        }

        /// <summary>
        /// Whether the interact was pressed, by key OR by the game's own context control.
        ///
        /// BOTH, and that is deliberate. The raw key is what the ini configures and what a
        /// keyboard player will have read; Control.Context is what makes a pad work at all,
        /// and it is also what ~INPUT_CONTEXT~ in the prompt above actually resolves to. Only
        /// reading the key would show a pad user a button glyph that does nothing.
        /// </summary>
        private bool Pressed()
        {
            try
            {
                if (Game.IsControlJustPressed(GTA.Control.Context)) return true;
            }
            catch
            {
                // Fall through to the key.
            }

            return Keyed();
        }

        private bool _keyWasDown;

        /// <summary>
        /// Edge-detects the configured key.
        ///
        /// Game.IsKeyPressed is a LEVEL, not an edge -- held down for a fifth of a second it
        /// is true across a dozen frames, which would start a sleep and then instantly start
        /// another one the moment the first finished.
        /// </summary>
        private bool Keyed()
        {
            bool down;

            try { down = Game.IsKeyPressed(_cfg.InteractKey); }
            catch { return false; }

            var edge = down && !_keyWasDown;
            _keyWasDown = down;
            return edge;
        }

        // ======================================================================
        // The sequence
        // ======================================================================

        private void Begin(Bunk where)
        {
            _bunk = where;
            _hours = where == Bunk.Bed ? _cfg.BedHours : _cfg.CarHours;

            Hud.ClearHelp();

            try
            {
                Function.Call(Hash.DO_SCREEN_FADE_OUT, FadeMs);

                // SetControlState, NOT the CanControlCharacter setter.
                //
                // That setter is obsolete in SHVDN 3.9 for a reason that is precisely the
                // failure this sequence must never have: it can fail to switch the controls
                // back ON if the ambient-script flag was set when they were disabled -- which
                // leaves the player frozen behind a screen that has already faded back in,
                // with nothing left to undo it.
                //
                // The flags are passed IDENTICALLY here and in HandBackControl. Asymmetric
                // flags are the whole cause of that bug.
                Game.Player.SetControlState(false, ControlFlags);
                _tookControl = true;
            }
            catch (Exception ex)
            {
                Log.Once("sleep-begin", "Could not start the sleep: " + ex.Message);
                Abandon();
                return;
            }

            _phase = Phase.FadingOut;

            // A ceiling, not a schedule. The phase normally ends when the fade reports itself
            // done; this is only here so a fade that never completes cannot wedge the mod with
            // the player's control taken away.
            _phaseUntil = Game.GameTime + FadeMs + 1500;

            Log.Info("Sleeping " + _hours.ToString("0.#") + "h in a " +
                     (where == Bunk.Bed ? "bed" : "car") + ".");
        }

        private void Advance()
        {
            var now = Game.GameTime;

            switch (_phase)
            {
                case Phase.FadingOut:
                    if (!FadedOut() && now < _phaseUntil) return;

                    Rest();

                    _phase = Phase.Resting;
                    _phaseUntil = now + RestMs;
                    return;

                case Phase.Resting:
                    if (now < _phaseUntil) return;

                    try { Function.Call(Hash.DO_SCREEN_FADE_IN, FadeMs); }
                    catch { /* the finish below still runs */ }

                    _phase = Phase.FadingIn;
                    _phaseUntil = now + FadeMs + 1500;
                    return;

                case Phase.FadingIn:
                    if (now < _phaseUntil && !FadedIn()) return;

                    Finish();
                    return;
            }
        }

        /// <summary>
        /// The night itself: move the clock, then tell the needs what happened.
        ///
        /// ORDER MATTERS. The clock is advanced first and Needs.Slept is told afterwards,
        /// because Slept re-primes its own record of the clock -- doing it the other way round
        /// leaves the needs holding a timestamp from before the jump, and the next tick sees
        /// the whole night as one step.
        /// </summary>
        private void Rest()
        {
            try
            {
                var whole = (int)_hours;
                var minutes = (int)Math.Round((_hours - whole) * 60f);

                GameClock.AddToCurrentTime(whole, minutes, 0);
            }
            catch (Exception ex)
            {
                Log.Once("sleep-clock", "Could not advance the clock: " + ex.Message +
                                        " - the rest still counts.");
            }

            var quality = _bunk == Bunk.Bed ? 1f : _cfg.CarRestoreFraction;
            _needs.Slept(_hours, quality);

            // Waking up rested is also waking up with your wind back. Free, and it is the
            // difference between a sleep that reads as a time skip and one that reads as rest.
            try
            {
                Function.Call(Hash.RESTORE_PLAYER_STAMINA, Game.Player.Handle, 1f);

                var me = Game.Player.Character;
                if (_bunk == Bunk.Bed && me != null && me.Exists() && me.Health < me.MaxHealth)
                {
                    // A proper bed patches you up a little. A car does not.
                    me.Health = Math.Min(me.MaxHealth, me.Health + me.MaxHealth / 5);
                }
            }
            catch
            {
                // Cosmetic.
            }
        }

        private void Finish()
        {
            // READ BEFORE IT IS CLEARED. Two lines down _bunk is None again, and whether this
            // was a car matters to everything after it.
            var inACar = _bunk == Bunk.Car;

            HandBackControl();

            _phase = Phase.Idle;
            _bunk = Bunk.None;

            try
            {
                var pct = (int)Math.Round(_needs.Sleep.Value * 100f);
                GTA.UI.Notification.PostTicker(
                    "~b~Slept~s~ " + _hours.ToString("0.#") + "h.  Rested " + pct + "%.", false, false);
            }
            catch
            {
                // Not worth failing a wake-up over.
            }

            // ---- and who is at the window ----
            //
            // AFTER the control is handed back and the ticker has posted, so a failure to
            // stage the scene leaves an ordinary wake-up rather than a frozen one. Knock
            // decides for itself whether the spot deserves it -- see Exposed.
            if (!inACar) return;

            // LOUD, NOT SILENT. This read `if (!inACar || _knock == null) return;` and the
            // constructor had gained the parameter without ever assigning the field -- so
            // every car nap took the null branch and said nothing, and the feature looked
            // like it was choosing not to fire. A guard against something that should never
            // be null has to say so when it is.
            if (_knock == null)
            {
                Log.Once("sleep-knock-null", "No wake-up scene is wired in. That is a bug.");
                return;
            }

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || !me.IsInVehicle()) return;

                var car = me.CurrentVehicle;
                if (car == null || !car.Exists()) return;

                if (_knock.Exposed(car)) _knock.Begin(car);
            }
            catch (Exception ex)
            {
                Log.Once("sleep-knock", "Could not stage the wake-up: " + ex.Message);
            }
        }

        /// <summary>Gives up mid-sequence without leaving the screen black or the player frozen.</summary>
        private void Abandon()
        {
            if (_phase == Phase.Idle) return;

            try { Function.Call(Hash.DO_SCREEN_FADE_IN, 400); }
            catch { /* nothing further to try */ }

            HandBackControl();
            _phase = Phase.Idle;
            _bunk = Bunk.None;
        }

        /// <summary>
        /// Hands control back, and only if it was taken.
        ///
        /// Unconditionally setting CanControlCharacter = true would stamp on any OTHER mod or
        /// mission that has legitimately taken control while this one was mid-fade.
        /// </summary>
        private void HandBackControl()
        {
            if (!_tookControl) return;
            _tookControl = false;

            try { Game.Player.SetControlState(true, ControlFlags); }
            catch (Exception ex) { Log.Error("Could not hand control back after sleeping", ex); }
        }

        /// <summary>
        /// Called on shutdown and on a script reload.
        ///
        /// A reload mid-sleep would otherwise leave the player frozen behind a black screen
        /// with nothing left running to undo it -- the worst failure this mod can produce, and
        /// the one most likely to happen while developing it.
        /// </summary>
        public void Shutdown()
        {
            Abandon();
        }

        private static bool FadedOut()
        {
            try { return Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT); }
            catch { return true; }
        }

        private static bool FadedIn()
        {
            try { return Function.Call<bool>(Hash.IS_SCREEN_FADED_IN); }
            catch { return true; }
        }
    }
}
