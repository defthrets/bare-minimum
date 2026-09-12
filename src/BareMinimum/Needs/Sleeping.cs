using System;
using GTA;
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
        Car,

        /// <summary>
        /// Not a bunk at all: wherever he was standing when he stopped being able to stay
        /// awake. Same sequence, worse rest, and nobody chose it.
        /// </summary>
        Collapse
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

            /// <summary>
            /// On the floor, still watching. The one phase that happens BEFORE the fade --
            /// a screen that goes black the instant somebody keels over reads as a bug, and
            /// the second and a bit of him actually dropping is the whole point of the beat.
            /// </summary>
            Dropping,

            FadingOut,
            Resting,

            /// <summary>
            /// Still black, building whoever is waiting outside the car.
            ///
            /// The one phase that exists purely so the player never sees the work. See
            /// Phase.Staging in Advance.
            /// </summary>
            Staging,

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

        /// <summary>
        /// The longest the screen stays black waiting for the wake-up scene to build.
        ///
        /// A ceiling rather than a wait, because a model that never streams would otherwise
        /// hold somebody on a black screen for ever. Two seconds is more than the streamer
        /// needs for two peds and a car it has almost certainly got already, and waking up to
        /// an empty road is a far smaller failure than not waking up at all.
        /// </summary>
        private const int StageMs = 2000;

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
        /// Zero, deliberately: it is the exact equivalent of the old setter without the bug.
        /// It lives here as a constant rather than being written out at both call sites
        /// because the documented failure mode is the two calls disagreeing.
        ///
        /// A PLAIN INT THROUGH THE NATIVE, because SetPlayerControlFlags arrived after 3.6.0
        /// and naming it in a signature is enough to stop the whole mod loading there.
        /// </summary>
        private const int ControlFlags = 0;

        /// <summary>Hands control to the player, or takes it. SET_PLAYER_CONTROL, nothing more.</summary>
        private static void Control(bool has)
        {
            try
            {
                GTA.Native.Function.Call(GTA.Native.Hash.SET_PLAYER_CONTROL,
                                         Game.Player, has, ControlFlags);
            }
            catch (Exception ex)
            {
                Log.Once("sleep-control", "Could not set player control: " + ex.Message);
            }
        }

        private readonly Knock _knock;

        /// <summary>How long he lies there before the screen goes, and when it is due.</summary>
        private const int DropMs = 1400;

        /// <summary>When the sleep meter emptied, so the warning has a length. 0 = not empty.</summary>
        private int _emptyAt;

        /// <summary>Whether he has been warned about this spell of it.</summary>
        private bool _warned;

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

                if (Exhausted(me)) return;

                // Out of the car is what resets the once-per-sit offer, not the offer lapsing:
                // driving off and stopping again is the same sit.
                if (_carPrompted && !me.IsInVehicle()) _carPrompted = false;

                var where = Offer(me);

                if (where == Bunk.None)
                {
                    // Walked away or drove off: forget the offer so it shows again next time.
                    if (_offering) { _offering = false; _heldSince = 0; }
                    return;
                }

                if (!_offering)
                {
                    _offering = true;
                    _offeredAt = Game.GameTime;

                    // THE CAR SAYS IT ONCE A SIT. Every stop is an offer -- every set of lights,
                    // with the engine switch on -- and a chip that came up at each of them would
                    // be the mod talking over the drive. The first stop of a sit gets the words;
                    // the key works at every one. A bed says it each time you come to it.
                    _promptDone = where == Bunk.Car && _carPrompted;
                    if (where == Bunk.Car) _carPrompted = true;
                }

                // ASKED BEFORE THE PROMPT IS DRAWN, so the hold meter on screen is this
                // frame's state rather than the previous one's.
                var ready = Ready(where);

                Prompt(where);

                if (ready) Begin(where);
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

                // Stopped AND, unless the ini says otherwise, switched off. Dozing off at the
                // lights is not a nap, and an idling engine is the difference between parking
                // up and pausing; the switch is for anybody who would rather not turn the key.
                if (v.IsEngineRunning && !_cfg.SleepEngineOn) return Bunk.None;
                if (!Function.Call<bool>(Hash.IS_VEHICLE_STOPPED, v.Handle)) return Bunk.None;

                // Not while being shot at or chased. A wanted level is the one state where a
                // fade to black is actively unfair.
                if (Core.Compat.Wanted > 0) return Bunk.None;

                return Bunk.Car;
            }

            if (!_cfg.SleepInBeds) return Bunk.None;
            if (Core.Compat.Wanted > 0) return Bunk.None;

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
        /// <summary>
        /// How long the offer stays on screen before it gets out of the way.
        ///
        /// Three rather than two now it is drawn quietly at the bottom instead of shouted
        /// from the game's help box. Two seconds was the right length for something that
        /// loud; a chip at two thirds opacity can afford to sit there a little longer without
        /// becoming furniture.
        /// </summary>
        private const int PromptMs = 3000;

        /// <summary>
        /// The car's offer is briefer still. It comes up every time you stop somewhere you
        /// could sleep, which is a lot of stops; two seconds is long enough to read "hold" and
        /// short enough to be gone before the lights change.
        /// </summary>
        private const int CarPromptMs = 2000;

        private bool _offering;
        private int _offeredAt;
        private bool _promptDone;

        /// <summary>Whether this sit in a vehicle has had its offer said. See Update.</summary>
        private bool _carPrompted;

        /// <summary>
        /// The offer: the button, the verb, and nothing else.
        ///
        /// It used to append the hours and a line about a car being a poor night. That is
        /// true, and it is also a paragraph pinned to the corner of the screen for as long as
        /// you stand near a bed -- read once on the first night and in the way every night
        /// after. The hours are in the ini and the F11 menu for anybody who wants the number.
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
            // A HOLD IN PROGRESS OUTRANKS THE TIMED OFFER, and outranks _promptDone with it.
            // The offer gets out of the way after two seconds, which is right for a line of
            // text and wrong for a meter: with nothing on screen, a hold is indistinguishable
            // from a button that has stopped working.
            if (_heldSince != 0)
            {
                var need = (int)(_cfg.CarSleepHoldSeconds * 1000f);
                var got = Game.GameTime - _heldSince;

                var at = need <= 0 ? 1f : got / (float)need;

                UI.Hint.Show("Sleeping in the car", Core.Pad.Cap(_cfg.InteractKey), at < 0f ? 0f : at);
                return;
            }

            if (_promptDone) return;

            if (Game.GameTime - _offeredAt >= (where == Bunk.Car ? CarPromptMs : PromptMs))
            {
                _promptDone = true;
                return;
            }

            var hold = where == Bunk.Car && _cfg.CarSleepHoldSeconds > 0f;

            // THE TAG DOES NOT SURVIVE THE MOVE, which is the thing I got wrong. A
            // ~INPUT_~ tag is turned into a button picture by the game's HELP renderer and
            // by nothing else; through DISPLAY_TEXT, which is what our own panels use, the
            // raw token comes out instead -- "t_E" on a keyboard and "b__7" on a pad. So the
            // chip is handed the KEY and draws its own cap for it.
            // FOUR WHOLE SENTENCES, NOT THREE FRAGMENTS GLUED. The pieces were "Hold "/"Press ",
            // then "to ", then the place -- which is English word order written into the code,
            // and it is not every language's. Four phrases is four things to translate and each
            // of them is a sentence somebody can actually put into their own.
            UI.Hint.Show(
                Core.Lingo.Say(where == Bunk.Bed
                                   ? (hold ? "Hold to sleep" : "Press to sleep")
                                   : (hold ? "Hold to sleep in the car" : "Press to sleep in the car")),
                Core.Pad.Cap(_cfg.InteractKey));
        }

        /// <summary>
        /// Whether the interact was pressed, by key OR by the game's own context control.
        ///
        /// BOTH, and that is deliberate. The raw key is what the ini configures and what a
        /// keyboard player will have read; Control.Context is what makes a pad work at all,
        /// and it is what a pad player will press when the chip above says to. Only reading
        /// the key would leave a controller unable to sleep at all.
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

        /// <summary>When the current hold started. Zero when nothing is being held.</summary>
        private int _heldSince;

        /// <summary>
        /// Whether the interact is DOWN -- a level, not an edge, which is what a hold needs.
        /// </summary>
        private bool HeldDown()
        {
            if (Core.Pad.Down(GTA.Control.Context)) return true;

            try { return Game.IsKeyPressed(_cfg.InteractKey); }
            catch { return false; }
        }

        /// <summary>
        /// Whether to start the sleep: a tap at a bed, a HOLD in a car.
        ///
        /// THE CAR IS THE ONE THAT NEEDED IT. In a car the interact is the same button that
        /// orders at a drive-through, so a press meant for lunch put you to sleep for four
        /// hours instead -- reported by jerome74, and easy to do. At a bed nothing else is
        /// competing for the press, and a hold there would be friction bought with nothing.
        ///
        /// CarHoldSeconds = 0 puts the old tap back for anybody who preferred it.
        /// </summary>
        private bool Ready(Bunk where)
        {
            var seconds = where == Bunk.Car ? _cfg.CarSleepHoldSeconds : 0f;

            if (seconds <= 0f)
            {
                _heldSince = 0;
                return Pressed();
            }

            if (!HeldDown())
            {
                _heldSince = 0;
                return false;
            }

            var now = Game.GameTime;

            // First frame of the hold: start the clock, do not act on it yet.
            if (_heldSince == 0)
            {
                _heldSince = now;
                return false;
            }

            if (now - _heldSince < (int)(seconds * 1000f)) return false;

            _heldSince = 0;

            // The button is still down as the sleep begins. Telling the edge detector it has
            // already seen this press stops the release from starting a second one the moment
            // he wakes up.
            _keyWasDown = true;
            return true;
        }

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

            if (!FadeOutNow("sleep-begin")) return;

            Log.Info("Sleeping " + _hours.ToString("0.#") + "h in a " +
                     (where == Bunk.Bed ? "bed" : "car") + ".");
        }

        /// <summary>
        /// Takes the screen and the controls, and starts the fade.
        ///
        /// SHARED BY THE SLEEP AND THE COLLAPSE, because the two differ in everything EXCEPT
        /// this -- and the one part that must never be got wrong twice is the control flags.
        ///
        /// SetControlState, NOT the CanControlCharacter setter. That setter is obsolete in
        /// SHVDN 3.9 for precisely the failure this sequence must never have: it can fail to
        /// switch the controls back ON if the ambient-script flag was set when they were
        /// disabled, which leaves the player frozen behind a screen that has already faded
        /// back in with nothing left to undo it. The flags are passed IDENTICALLY here and in
        /// HandBackControl; asymmetric flags are the whole cause of that bug.
        /// </summary>
        private bool FadeOutNow(string logKey)
        {
            try
            {
                Function.Call(Hash.DO_SCREEN_FADE_OUT, FadeMs);

                Control(false);
                _tookControl = true;
            }
            catch (Exception ex)
            {
                Log.Once(logKey, "Could not start the sleep: " + ex.Message);
                Abandon();
                return false;
            }

            _phase = Phase.FadingOut;

            // A ceiling, not a schedule. The phase normally ends when the fade reports itself
            // done; this is only here so a fade that never completes cannot wedge the mod with
            // the player's control taken away.
            _phaseUntil = Game.GameTime + FadeMs + 1500;
            return true;
        }

        // ======================================================================
        // Running out of sleep altogether
        // ======================================================================

        /// <summary>
        /// Watches an empty sleep meter, warns, and eventually puts him on the floor.
        /// Returns true once it has taken over, so the ordinary bed prompt stands down.
        ///
        /// A WINDOW RATHER THAN AN INSTANT. Being dropped the frame the meter hits zero reads
        /// as the mod crashing; a warning, then the picture starting to swim -- see
        /// Effects.Wobble -- then going down is long enough to pull the car over and short
        /// enough to still be a consequence.
        ///
        /// IT REFUSES IN THE PLACES WHERE IT WOULD BE A DEATH RATHER THAN A NUISANCE: in the
        /// air, in the water, and with the police already after you. This mod does not kill
        /// you over a meter -- see the floor in Needs.Starve for the same rule.
        /// </summary>
        private bool Exhausted(Ped me)
        {
            if (!_cfg.SleepCollapse || !_needs.Sleep.Empty)
            {
                _emptyAt = 0;
                _warned = false;
                return false;
            }

            var now = Game.GameTime;

            if (_emptyAt == 0) _emptyAt = now;

            if (!_warned)
            {
                _warned = true;

                try
                {
                    Core.Compat.Ticker(
                        "~y~You can barely keep your eyes open.~s~ Find somewhere to sleep.");
                }
                catch
                {
                    // He will find out the other way.
                }
            }

            if (now - _emptyAt < (int)(_cfg.SleepCollapseAfterSeconds * 1000f)) return false;

            // Not here. The clock keeps running, so he goes down as soon as he is somewhere
            // that going down is survivable.
            if (!CanDropHere(me)) return false;

            Collapse(me);
            return true;
        }

        /// <summary>Whether passing out where he is standing would be a nuisance rather than a death.</summary>
        private static bool CanDropHere(Ped me)
        {
            try
            {
                if (me.IsInAir || me.IsSwimming || me.IsClimbing || me.IsFalling) return false;
                if (me.IsRagdoll) return false;

                // Unconscious and wanted is an arrest, which is a far bigger punishment than
                // this is meant to be.
                if (Core.Compat.Wanted > 0) return false;

                // In a car is allowed -- he slumps at the wheel, and that is the one place the
                // wake-up scene already knows what to do about. See Waking.
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Puts him on the floor, then runs the ordinary sequence over the top of it.</summary>
        private void Collapse(Ped me)
        {
            _bunk = Bunk.Collapse;
            _hours = _cfg.SleepCollapseHours;

            Hud.ClearHelp();

            _emptyAt = 0;
            _warned = false;

            // ON THE FLOOR FIRST, THEN THE FADE. He is only ragdolled on foot: there is no
            // falling over in a driver's seat, and asking for it there does nothing useful.
            try
            {
                if (!me.IsInVehicle())
                {
                    Function.Call(Hash.SET_PED_TO_RAGDOLL, me.Handle, 4000, 5000, 0, true, true, false);
                }
            }
            catch (Exception ex)
            {
                Log.Once("sleep-collapse-ragdoll", "Could not drop him: " + ex.Message);
            }

            _phase = Phase.Dropping;
            _phaseUntil = Game.GameTime + DropMs;

            Log.Info("Passed out on an empty sleep meter, out for " +
                     _hours.ToString("0.#") + "h.");
        }

        private void Advance()
        {
            var now = Game.GameTime;

            switch (_phase)
            {
                case Phase.Dropping:
                    if (now < _phaseUntil) return;

                    // The controls were his the whole way down -- that is what makes it read
                    // as him falling rather than as a cutscene starting.
                    FadeOutNow("sleep-collapse");
                    return;

                case Phase.FadingOut:
                    if (!FadedOut() && now < _phaseUntil) return;

                    Rest();

                    _phase = Phase.Resting;
                    _phaseUntil = now + RestMs;
                    return;

                case Phase.Resting:
                    if (now < _phaseUntil) return;

                    // ---- WHO IS AT THE WINDOW IS BUILT BEHIND THE BLACK ----
                    //
                    // This used to happen in Finish, which runs after the fade-in has already
                    // finished -- so the screen came up on an empty road, and a second later
                    // two officers and a squad car appeared in it and dropped onto the tarmac.
                    // Reported as exactly that, and it is the right complaint: a scene you
                    // watch being assembled is not a scene.
                    //
                    // Asked for here instead, then the fade waits for it. Knock's Staging is a
                    // streamer request that comes back next frame, which is precisely the kind
                    // of work a black screen is FOR.
                    Waking();

                    _phase = Phase.Staging;
                    _phaseUntil = now + StageMs;
                    return;

                case Phase.Staging:
                    // Ready, or long enough. THE CEILING IS NOT OPTIONAL: a model that never
                    // streams would otherwise hold the player on a black screen for ever, and
                    // waking up with nobody there is a far smaller failure than that.
                    if (_knock != null && _knock.Busy && !_knock.Staged && now < _phaseUntil)
                    {
                        return;
                    }

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

                Core.Clock.Add(whole, minutes);
            }
            catch (Exception ex)
            {
                Log.Once("sleep-clock", "Could not advance the clock: " + ex.Message +
                                        " - the rest still counts.");
            }

            var quality = _bunk == Bunk.Bed ? 1f
                        : _bunk == Bunk.Collapse ? _cfg.SleepCollapseQuality
                        : _cfg.CarRestoreFraction;

            // WHERE THE METER WAS, KEPT FOR THE CARD. The wake-up card sweeps its bar from
            // here to wherever the night left it, and by the time the card is built the old
            // value is gone -- so it is taken now, one line before it changes.
            _restedBefore = _needs.Sleep.Value;

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

        /// <summary>
        /// Where the sleep meter stood before the night. The card sweeps its bar from here.
        /// </summary>
        private float _restedBefore;

        /// <summary>
        /// The two colours a wake-up card can be.
        ///
        /// The awake blue is the far end of the moon's own ramp beside the minimap, so the
        /// card and the mark it is about are the same colour. A collapse is red because it is
        /// not a night's sleep, it is the thing that happens instead of one.
        /// </summary>
        private static readonly System.Drawing.Color Awake =
            System.Drawing.Color.FromArgb(255, 96, 178, 246);

        private static readonly System.Drawing.Color Collapsed =
            System.Drawing.Color.FromArgb(255, 214, 69, 58);

        /// <summary>
        /// Which of the five moon drawings the card wears.
        ///
        /// ASKED OF THE NEED, NOT WORKED OUT HERE. The first cut of this split the range into
        /// five equal parts, and Need.Stage does not -- its bands are 0.80, 0.58, 0.34 and
        /// 0.14, weighted so the bottom two are narrow and urgent. A card claiming to show the
        /// same drawing as the mark beside the minimap, and showing a different one, is worse
        /// than a card with no picture on it.
        /// </summary>
        private string Moon()
        {
            var at = _needs.Sleep.Stage;

            if (at < 0) at = 0;
            if (at > 4) at = 4;

            return "moon" + at + ".png";
        }

        private void Finish()
        {
            HandBackControl();

            // READ BEFORE THEY ARE CLEARED. Both of these were reset on the two lines above
            // the message that used them, so the collapse wording could never appear -- every
            // wake-up said "Slept", including the ones where he had passed out in the street.
            var passedOut = _bunk == Bunk.Collapse;

            _phase = Phase.Idle;
            _bunk = Bunk.None;

            try
            {
                var rested = _needs.Sleep.Value;
                var pct = (int)Math.Round(rested * 100f);

                var hours = _hours.ToString("0.#") + (_hours >= 1.95f ? " HOURS" : " HOUR");

                // The moon at the stage it ended on, which is the same drawing the mark
                // beside the minimap is showing by the time the card fades.
                UI.Toast.Show(Moon(),
                              (passedOut ? "PASSED OUT FOR " : "SLEPT ") + hours,
                              passedOut
                                  ? "You went down where you stood. Rested " + pct + "%."
                                  : "Rested " + pct + "%.",
                              passedOut ? Collapsed : Awake,
                              _restedBefore, rested);
            }
            catch
            {
                // Not worth failing a wake-up over.
            }

            // Who is at the window was built before the fade -- see Phase.Staging.
        }

        /// <summary>
        /// Stages whoever is waiting outside, while the screen is still black.
        ///
        /// Knock decides for itself whether the spot deserves it -- see Exposed. A failure
        /// here is an ordinary wake-up, which is why none of it throws upward.
        /// </summary>
        private void Waking()
        {
            // A COLLAPSE IN A CAR COUNTS TOO. It is the same picture from the outside -- a
            // man asleep at the wheel where he stopped -- and it is the situation the wake-up
            // scene was written for. Knock still decides for itself whether the spot deserves
            // it; the method re-checks he is actually in a vehicle either way.
            if (_bunk != Bunk.Car && _bunk != Bunk.Collapse) return;

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

                // TWO DIFFERENT QUESTIONS, AND THEY ARE NOT THE SAME FLAG. Whether they are
                // CERTAIN to come is about how he got here and whether the setting allows it;
                // what they SAY is about how he got here and nothing else. A collapse that the
                // setting has turned back into a gamble, and then wins the gamble, still gets
                // asked what happened -- because that is what happened.
                var passedOut = _bunk == Bunk.Collapse;
                var certain = passedOut && _cfg.PoliceOnCollapse;

                if (_knock.Exposed(car, certain)) _knock.Begin(car, passedOut);
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

            try { Control(true); }
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
