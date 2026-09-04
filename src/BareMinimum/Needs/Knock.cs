using System;
using GTA;
using GTA.Chrono;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Needs
{
    /// <summary>
    /// Waking up in a car in the road, with two officers at the windows.
    ///
    /// Sleeping in a car parked in the street is loitering, and the game is full of police who
    /// would have something to say about it. This is the something.
    ///
    /// ONLY IN THE ROAD, which is the whole point of Exposed below. A car tucked behind a
    /// building or sat in a car park is nobody's business and nothing happens; a car stopped in
    /// a live lane with somebody asleep in it gets knocked on. The test is IS_POINT_ON_ROAD
    /// plus the road's own traffic density, so a dirt track in the desert at 3am also gets left
    /// alone -- the road has to be one people actually use.
    ///
    /// IT ENDS BY LETTING GO. Every path out of here -- drive off, get out and talk, or wait
    /// long enough -- finishes by marking the officers and their car as no longer needed. From
    /// that moment they are ordinary police, handled by the game: drive away and the two of
    /// them get in and come after you exactly as any patrol would, because they ARE a patrol by
    /// then. Nothing in this file tries to script a chase, and that is deliberate -- the game
    /// is far better at it, and a hand-written pursuit would fight whatever police overhaul is
    /// installed alongside.
    /// </summary>
    internal sealed class Knock
    {
        private enum Stage
        {
            Off,

            /// <summary>
            /// Models asked for, waiting on the streamer.
            ///
            /// THIS STAGE EXISTS BECAUSE THE FIRST VERSION HAD NO IT AND NOTHING EVER
            /// SPAWNED. Request() does not load a model, it asks for one, and IsLoaded is
            /// false on the line after it every time -- so a Begin that requested and then
            /// immediately built found no model, returned false, and left no trace. Waiting
            /// in a loop is not the answer either: the timer that loop would watch only moves
            /// when a frame renders, and a script tick is part of the frame, so it freezes the
            /// game outright. The only way is to come back next frame, which means a stage.
            /// </summary>
            Staging,

            /// <summary>They are at the windows and nothing has happened yet.</summary>
            Looking,

            /// <summary>You got out. One of them is talking.</summary>
            Talking,

            /// <summary>Over, one way or another. Everyone has been let go.</summary>
            Leaving
        }

        /// <summary>How far in front of your bonnet their car sits.</summary>
        private const float Ahead = 7.5f;

        /// <summary>How far you have to move before it counts as driving off.</summary>
        private const float BoltedMetres = 9f;

        /// <summary>How long they stand there if you do nothing at all.</summary>
        private const int PatienceMs = 30000;

        /// <summary>And a ceiling on the whole thing, so it can never wedge.</summary>
        private const int NeverLongerThanMs = 90000;

        private static readonly string[] CopModels =
        {
            "s_m_y_cop_01", "s_f_y_cop_01"
        };

        private static readonly string[] CarModels =
        {
            "police", "police2", "police3"
        };

        /// <summary>
        /// What one of them says when you get out.
        ///
        /// SAID AS A SUBTITLE, with an ambient line under it for the voice. The game has no
        /// canned speech for "you cannot sleep here" and forcing an unrelated one to carry the
        /// meaning is how mods end up with a policeman shouting about a robbery at somebody
        /// having a nap.
        /// </summary>
        private static readonly string[] Lines =
        {
            "You can't sleep here. Move it along.",
            "This isn't a bedroom. Get moving.",
            "Long night? Not on my street it isn't.",
            "You're blocking a live lane. Wake up and drive.",
            "Next time find a car park. Go on."
        };

        private readonly Core.Settings _cfg;
        private readonly Random _dice = new Random();

        private Stage _stage = Stage.Off;

        private Vehicle _theirCar;
        private Ped _left;
        private Ped _right;

        private Vehicle _yours;

        private Vector3 _wokeAt;
        private int _until;
        private int _hardStop;
        private int _spokeAt;

        public Knock(Core.Settings cfg)
        {
            _cfg = cfg;
        }

        public bool Busy => _stage != Stage.Off;

        /// <summary>
        /// Whether the scene is built and safe to look at.
        ///
        /// FALSE WHILE STAGING, WHICH IS THE POINT. Staging is asking the streamer for two ped
        /// models and a car and coming back next frame until they arrive -- and until they do
        /// there is nothing in the road. Anybody fading a screen in has to wait for this or
        /// the player watches the scene assemble itself.
        /// </summary>
        public bool Staged => _stage == Stage.Looking || _stage == Stage.Talking;

        // ======================================================================

        /// <summary>
        /// Whether this spot is one the police would care about.
        ///
        /// TWO TESTS, AND BOTH HAVE TO PASS.
        ///
        /// IS_POINT_ON_ROAD does the real work: it is false in a car park, false on a
        /// driveway, false behind a building, and true in a lane. It is the difference the
        /// whole feature turns on, and it is why a nap round the back of a warehouse is
        /// nobody's business.
        ///
        /// It is generous about verges and forecourts, though -- a petrol station apron can
        /// pass it -- so the nearest point on the street has to be CLOSE as well. Six metres
        /// puts you in the carriageway rather than beside it.
        ///
        /// Then: is anybody about. The traffic-density figure the game keeps per road node
        /// would be the tidier answer and it comes back through an out-parameter, which in
        /// this codebase means turning on /unsafe for the whole build to earn one integer.
        /// COUNTING WHAT IS ACTUALLY THERE is both cheaper and closer to what was asked for --
        /// a populated area is one with people in it, and a dirt track in Blaine County at 3am
        /// has neither cars nor pedestrians on it whatever the map thinks of the road.
        /// </summary>
        public bool Exposed(Vehicle car)
        {
            if (!_cfg.PoliceWake) return false;
            if (car == null || !car.Exists()) return false;

            try
            {
                var p = car.Position;

                // EVERY REFUSAL SAYS WHICH ONE IT WAS. Four tests that all return the same
                // false is a feature you cannot tell from a broken one, which is exactly how
                // the first go was debugged -- by guessing.
                if (!Function.Call<bool>(Hash.IS_POINT_ON_ROAD, p.X, p.Y, p.Z, car.Handle))
                {
                    Log.Info("Kerbside nap: not on a road. No police.");
                    return false;
                }

                // THE WIDTH IS THE SECOND TEST, not the distance to a road node.
                //
                // This used to measure how far GetNextPositionOnStreet was and refuse
                // anything past six metres, which rejected two naps taken dead in the middle
                // of the road at 7.2m and 6.0m. That native returns the next position ALONG
                // the street -- a node on the centreline, and nodes are spaced ten to twenty
                // metres apart -- so its distance says nothing about whether you are on the
                // carriageway. It was measuring the spacing of a grid.
                //
                // Across() measures the road itself, sideways, and it was already here for
                // the odds. A forecourt or a slip of verge comes back a couple of metres; a
                // lane comes back four or more. It answers the question the node distance was
                // being asked and cannot answer.
                var wide = Across(car);

                if (wide < 4f)
                {
                    Log.Info("Kerbside nap: only " + wide.ToString("0.#") +
                             "m of road across - not a carriageway. No police.");
                    return false;
                }

                var about = Busyness(car);

                if (about < _cfg.PoliceWakeNeighbours)
                {
                    Log.Info("Kerbside nap: only " + about + " about, needs " +
                             _cfg.PoliceWakeNeighbours + ". No police.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Once("knock-exposed", "Could not judge the spot: " + ex.Message);
                return false;
            }

            var odds = Odds(car);
            var rolled = _dice.NextDouble();

            if (rolled >= odds)
            {
                Log.Info("Kerbside nap: rolled " + rolled.ToString("0.00") +
                         " against " + odds.ToString("0.00") + ". Lucky this time.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// How likely they are to turn up here, from how main the road is.
        ///
        /// A BACK STREET IS A COIN FLIP AND A MAIN ROAD IS NEARLY CERTAIN. Something that
        /// happens on every single kerbside nap stops being a hazard and becomes a cutscene
        /// you learn to avoid; something that never happens on a dual carriageway is not
        /// modelling anything. The setting is the floor, not the answer.
        ///
        /// TWO SIGNALS, AND THEY ARE DIFFERENT KINDS OF THING. Width is structural: a road is
        /// four lanes wide whether or not anything is on it at four in the morning, so it says
        /// what KIND of road this is. Busyness is situational and says who is around to notice
        /// right now. Weighted toward the width, because the width is the thing that does not
        /// change between one nap and the next -- a player should be able to learn that the
        /// boulevard is a bad idea and the side street is a gamble.
        /// </summary>
        private float Odds(Vehicle car)
        {
            var floor = Clamp01(_cfg.PoliceWakeChance);

            try
            {
                var wide = Across(car);
                var busy = Busyness(car);

                // Seven metres is about two lanes and a bit of kerb -- an ordinary street.
                // Eighteen is a boulevard.
                var byWidth = Clamp01((wide - 7f) / 11f);

                // And however far past the qualifying threshold the traffic is.
                var span = Math.Max(1f, _cfg.PoliceWakeNeighbours * 2f);
                var byTraffic = Clamp01((busy - _cfg.PoliceWakeNeighbours) / span);

                var main = byWidth * 0.65f + byTraffic * 0.35f;

                // Never quite one. A guaranteed event is a rule, and this is meant to be a
                // risk you take.
                var odds = floor + (0.97f - floor) * main;

                // AT INFO, NOT DEBUG. One line per car nap is not noise, and without it the
                // only way to tell "the spot did not qualify" from "the spot qualified and
                // the scene failed" is to guess -- which is exactly where an evening went.
                Log.Info("Kerbside nap: " + wide.ToString("0.#") + "m across, " + busy +
                         " about, odds " + odds.ToString("0.00") + ".");

                return odds < floor ? floor : odds;
            }
            catch
            {
                return floor;
            }
        }

        /// <summary>
        /// How many metres of road there are across the car, kerb to kerb.
        ///
        /// SAMPLED SIDEWAYS WITH IS_POINT_ON_ROAD, in steps out from the car in both
        /// directions until it stops answering yes. That is a real measurement of the
        /// carriageway and it needs nothing the build cannot compile -- the lane counts the
        /// game keeps come back through out-parameters, which would mean turning on /unsafe.
        ///
        /// Stepping a metre at a time and stopping at the first miss, rather than sampling the
        /// whole span and counting hits: a road is continuous, so the first no IS the kerb,
        /// and a junction mouth twenty metres away should not be read as this road being wide.
        /// </summary>
        private static float Across(Vehicle car)
        {
            var total = 0f;

            try
            {
                var p = car.Position;
                var side = car.RightVector;

                foreach (var way in new[] { 1f, -1f })
                {
                    for (var m = 1f; m <= 14f; m += 1f)
                    {
                        var at = p + side * (m * way);

                        if (!Function.Call<bool>(Hash.IS_POINT_ON_ROAD,
                                                 at.X, at.Y, at.Z, car.Handle))
                        {
                            break;
                        }

                        total += 1f;
                    }
                }
            }
            catch { /* whatever was measured stands */ }

            return total;
        }

        /// <summary>
        /// How many people and cars are near enough to make this a public place.
        ///
        /// Pedestrians AND vehicles, because either one is enough. A road busy with traffic
        /// and empty of people is still a road you cannot park across; a quiet residential
        /// street with somebody walking a dog is still overlooked.
        /// </summary>
        private static int Busyness(Vehicle car)
        {
            var n = 0;

            try
            {
                foreach (var ped in World.GetNearbyPeds(car.Position, 70f))
                {
                    if (ped == null || !ped.Exists() || ped.IsDead) continue;
                    if (ped == Game.Player.Character) continue;
                    n++;
                }

                foreach (var v in World.GetNearbyVehicles(car.Position, 70f))
                {
                    if (v == null || !v.Exists()) continue;
                    if (v.Handle == car.Handle) continue;
                    n++;
                }
            }
            catch { /* whatever was counted stands */ }

            return n;
        }

        // ======================================================================

        /// <summary>Asks for what the scene needs. It is built a frame or two later.</summary>
        public bool Begin(Vehicle car)
        {
            if (Busy) return false;
            if (car == null || !car.Exists()) return false;

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return false;

            _yours = car;
            _wokeAt = car.Position;

            _stage = Stage.Staging;

            var start = Game.GameTime;
            _until = start + 5000;
            _hardStop = start + NeverLongerThanMs;

            // Ask for everything now so the streamer has the whole list in flight at once
            // rather than discovering it one model at a time.
            foreach (var name in CarModels) Ask(name);
            foreach (var name in CopModels) Ask(name);

            Log.Info("Woken in the road - waiting on the models for the squad car.");
            return true;
        }

        private static void Ask(string name)
        {
            try
            {
                var m = new Model(name);
                if (!m.IsLoaded) m.Request();
            }
            catch { /* the next frame will try again */ }
        }

        /// <summary>Builds it, once the models are actually here. False until they are.</summary>
        /// <summary>
        /// Puts one thing on the ground under it, if the ground is known yet.
        ///
        /// GET_GROUND_Z_FOR_3D_COORD answers false where the map has not streamed in, which is
        /// possible on the frame after a long sleep -- so a failure leaves the entity where it
        /// was rather than dropping it to zero, which would put it under the world.
        /// </summary>
        private static void Settle(Entity what)
        {
            if (what == null || !what.Exists()) return;

            try
            {
                var at = what.Position;
                var z = new OutputArgument();

                if (!Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD,
                                         at.X, at.Y, at.Z + 1.5f, z, false))
                {
                    return;
                }

                var ground = z.GetResult<float>();

                if (ground <= 0f) return;

                what.Position = new Vector3(at.X, at.Y, ground + 1.0f);

                Function.Call(Hash.SET_ENTITY_COLLISION, what.Handle, true, true);
            }
            catch
            {
                // Where it is will do.
            }
        }

        private bool Stage_()
        {
            var car = _yours;
            if (car == null || !car.Exists()) return false;

            try
            {
                // NOSE TO NOSE, blocking you in. Parked behind would be politer and would also
                // mean the first thing you see on waking is an empty road.
                var infront = car.Position + car.ForwardVector * Ahead;

                _theirCar = Make(CarModels, infront, car.Heading + 180f);
                if (_theirCar == null) return false;

                Lights(_theirCar);

                // One at each front window, standing just outside the door rather than in it,
                // or they spawn inside the panel and get shoved out by the physics.
                _left = MakeCop(car, -1.6f);
                _right = MakeCop(car, 1.6f);

                // ON THE GROUND, NOT ABOVE IT. A ped created at a coordinate worked out from
                // the car's own position starts at the CAR's height, which on any kerb, ramp
                // or crowned road is above the tarmac -- and what you see is two officers
                // dropping the last few inches as the physics catches them. Reported as
                // exactly that. The car gets the same treatment for the same reason.
                Settle(_theirCar);
                Settle(_left);
                Settle(_right);

                if (_left == null && _right == null)
                {
                    Clean();
                    return false;
                }

                _wokeAt = car.Position;
                _stage = Stage.Looking;
                _until = Game.GameTime + PatienceMs;

                Log.Info("Squad car and two officers placed.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Once("knock-stage", "Could not stage the wake-up: " + ex.Message);
                Clean();
                return false;
            }
        }

        public void Update()
        {
            if (_stage == Stage.Off) return;

            try
            {
                var now = Game.GameTime;
                var me = Game.Player.Character;

                if (me == null || !me.Exists() || now > _hardStop) { Release(); return; }

                switch (_stage)
                {
                    case Stage.Staging:

                        if (Stage_()) return;

                        // Still not here. Keep asking -- a request can be dropped when the
                        // streamer is busy, and re-asking is free.
                        foreach (var name in CarModels) Ask(name);
                        foreach (var name in CopModels) Ask(name);

                        if (now > _until)
                        {
                            Log.Info("The squad car's models never loaded - no wake-up.");
                            Clean();
                        }

                        return;

                    case Stage.Looking:

                        // ---- you drove off ----
                        if (Bolted(me))
                        {
                            Wanted();
                            Release();
                            return;
                        }

                        // ---- you got out ----
                        if (!me.IsInVehicle())
                        {
                            Speak(me);
                            _stage = Stage.Talking;
                            _until = now + 6000;
                            return;
                        }

                        if (now > _until) { Release(); return; }
                        break;

                    case Stage.Talking:

                        // Getting back in and going is still going.
                        if (Bolted(me)) { Wanted(); Release(); return; }

                        if (now > _until) { Release(); return; }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Once("knock-update", "The wake-up failed: " + ex.Message);
                Release();
            }
        }

        // ======================================================================

        private bool Bolted(Ped me)
        {
            try
            {
                if (!me.IsInVehicle()) return false;

                var v = me.CurrentVehicle;
                if (v == null || !v.Exists()) return false;

                return v.Position.DistanceTo(_wokeAt) > BoltedMetres;
            }
            catch { return false; }
        }

        private static void Wanted()
        {
            try
            {
                // ONE STAR. Enough that they come after you and the radio lights up; not
                // enough to bring a helicopter to a parking offence.
                if (Game.Player.WantedLevel < 1) Game.Player.WantedLevel = 1;
            }
            catch (Exception ex) { Log.Debug("Could not set the wanted level: " + ex.Message); }
        }

        private void Speak(Ped me)
        {
            var who = _left != null && _left.Exists() ? _left : _right;
            if (who == null || !who.Exists()) return;

            if (Game.GameTime - _spokeAt < 2000) return;
            _spokeAt = Game.GameTime;

            try
            {
                who.Task.ClearAll();
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, who.Handle, me.Handle, 2000);

                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, who.Handle,
                              "GENERIC_INSULT_HIGH", "SPEECH_PARAMS_FORCE");
            }
            catch { /* the subtitle carries it */ }

            try
            {
                var line = Lines[_dice.Next(Lines.Length)];

                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, "~y~Officer:~s~ " + line);
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP, 0, false, true, 6000);
            }
            catch { /* nothing further to try */ }
        }

        /// <summary>
        /// Hands the pair and their car back to the game and forgets about them.
        ///
        /// NOT DELETED. Deleting two officers and a squad car in front of you is the mod
        /// tidying up in plain sight, and it is also wrong the moment there is a wanted level:
        /// the two men who just watched you drive off are exactly the two who should be
        /// chasing you. Marked as no longer needed, they become ordinary traffic -- the game
        /// drives them, despawns them in its own time, and any police overhaul installed
        /// alongside gets a normal patrol rather than something this mod is still holding.
        /// </summary>
        private void Release()
        {
            _stage = Stage.Leaving;

            try
            {
                foreach (var cop in new[] { _left, _right })
                {
                    if (cop == null || !cop.Exists()) continue;

                    cop.Task.ClearAll();

                    // Back to the car. If they are wanted men now, the game overrides this the
                    // moment it takes them over, which is the correct outcome either way.
                    if (_theirCar != null && _theirCar.Exists())
                    {
                        Function.Call(Hash.TASK_ENTER_VEHICLE, cop.Handle, _theirCar.Handle,
                                      20000, -2, 1.0f, 1, 0);
                    }

                    cop.IsPersistent = false;
                    cop.MarkAsNoLongerNeeded();
                }

                if (_theirCar != null && _theirCar.Exists())
                {
                    _theirCar.IsPersistent = false;
                    _theirCar.MarkAsNoLongerNeeded();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not hand the officers back: " + ex.Message);
            }

            _left = null;
            _right = null;
            _theirCar = null;
            _stage = Stage.Off;
        }

        /// <summary>Deletes everything. Only for a failed setup or a script reload.</summary>
        private void Clean()
        {
            try
            {
                if (_left != null && _left.Exists()) _left.Delete();
                if (_right != null && _right.Exists()) _right.Delete();
                if (_theirCar != null && _theirCar.Exists()) _theirCar.Delete();
            }
            catch { /* going away regardless */ }

            _left = null;
            _right = null;
            _theirCar = null;
            _stage = Stage.Off;
        }

        public void Shutdown()
        {
            if (_stage == Stage.Off) return;
            Clean();
        }

        // ======================================================================

        private Vehicle Make(string[] names, Vector3 where, float heading)
        {
            foreach (var name in names)
            {
                var model = new Model(name);
                if (!Stream(model)) continue;

                var v = World.CreateVehicle(model, where, heading);
                model.MarkAsNoLongerNeeded();

                if (v == null || !v.Exists()) continue;

                v.IsPersistent = true;
                v.PlaceOnGround();
                return v;
            }

            return null;
        }

        private Ped MakeCop(Vehicle yours, float sideways)
        {
            foreach (var name in CopModels)
            {
                var model = new Model(name);
                if (!Stream(model)) continue;

                var at = yours.Position + yours.RightVector * sideways;

                var ped = World.CreatePed(model, at);
                model.MarkAsNoLongerNeeded();

                if (ped == null || !ped.Exists()) continue;

                ped.IsPersistent = true;

                try
                {
                    // A REAL POLICEMAN, not a man in a costume. Without this the game does not
                    // treat him as police, so he takes no part in the wanted level he is about
                    // to be handed, and civilians ignore him.
                    Function.Call(Hash.SET_PED_AS_COP, ped.Handle, true);
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle,
                                  Function.Call<int>(Hash.GET_HASH_KEY, "COP"));

                    ped.BlockPermanentEvents = true;

                    // The torch. Only worth having in his hand when it is dark enough to be
                    // pointing one at somebody.
                    if (Dark())
                    {
                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<int>(Hash.GET_HASH_KEY, "WEAPON_FLASHLIGHT"),
                                      1, false, true);
                    }

                    // Facing in through the glass, and staying that way.
                    ped.Heading = Heading(at, yours.Position);

                    Function.Call(Hash.TASK_LOOK_AT_ENTITY, ped.Handle, yours.Handle,
                                  -1, 2048, 3);
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle,
                                  "CODE_HUMAN_POLICE_INVESTIGATE", 0, true);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not set an officer up properly: " + ex.Message);
                }

                return ped;
            }

            return null;
        }

        /// <summary>
        /// Blue lights, no siren.
        ///
        /// A siren on a parked car is an emergency, and this is a man knocking on a window.
        /// Muting it first and then switching it on is the only way to have the lights without
        /// the noise -- the light bar is driven by the siren flag, not by the headlights.
        /// </summary>
        private static void Lights(Vehicle v)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_HAS_MUTED_SIRENS, v.Handle, true);
                Function.Call(Hash.SET_VEHICLE_SIREN, v.Handle, true);
                Function.Call(Hash.SET_VEHICLE_LIGHTS, v.Handle, 2);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v.Handle, true, true, false);
            }
            catch (Exception ex) { Log.Debug("Could not light the squad car: " + ex.Message); }
        }

        private static bool Dark()
        {
            try
            {
                var h = GameClock.Hour;
                return h >= 20 || h < 7;
            }
            catch { return false; }
        }

        private static float Heading(Vector3 from, Vector3 to)
        {
            var d = to - from;
            return (float)(Math.Atan2(d.Y, d.X) * 180.0 / Math.PI) - 90f;
        }

        /// <summary>
        /// Asks for a model and gives up rather than waiting.
        ///
        /// The same rule the food props follow, and for the reason written out at length in
        /// Eating.Give: spinning on IsLoaded inside a script tick freezes the game outright,
        /// because the timer it would be waiting on only advances when a frame renders.
        /// </summary>
        private static bool Stream(Model model)
        {
            try
            {
                if (model.IsLoaded) return true;

                model.Request();
                return model.IsLoaded;
            }
            catch { return false; }
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }
}
