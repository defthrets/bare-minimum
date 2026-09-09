using System;
using GTA;
using GTA.Math;
using GTA.Native;

using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>
    /// Flies the player over the whole map on a lawnmower path so the machines find themselves.
    /// </summary>
    ///
    /// <remarks>
    /// THE PROBLEM IS THAT A MAP PROP DOES NOT EXIST UNTIL IT IS STREAMED. Vending machines and
    /// produce stalls have no line in vendors.json and never will -- they are found by MODEL,
    /// which is exactly why every one of them in the world works -- and the cost of that is
    /// that nothing knows where any of them are until the game has loaded that piece of map.
    /// So the only way to get a complete list is to visit the whole map, and the only tolerable
    /// way to visit the whole map is to have something else do it.
    ///
    /// IT COLLECTS NOTHING ITSELF. Every machine it finds is found by MachineBlips, which is
    /// already looking, already deduplicating by position and already writing machines.json.
    /// This class moves the player and nothing else, which is what keeps it a tool rather than
    /// a second copy of the thing it is helping -- and it means a survey and an ordinary drive
    /// around town put data in by the same route, so there is one set of rules about what
    /// counts as a machine and where it is.
    ///
    /// BOUSTROPHEDON, WHICH IS THE WORD FOR HOW AN OX PLOUGHS. North up one lane, south down
    /// the next, so the turn at the end of a lane is one step sideways rather than a flight
    /// back across everything already done. On a map this size the difference is not a
    /// nicety: a comb pattern that returns to the start of every lane covers the ground twice
    /// and takes twice as long.
    ///
    /// LANE SPACING IS THE SCAN RADIUS, NOT THE STREAMING RADIUS. What decides whether a
    /// machine is found is whether MachineBlips looked within ShopBlipRange of it, so the lanes
    /// are spaced at that and the pass rate is what keeps the streamer ahead. Spacing them at
    /// whatever the engine happens to load would find machines the scan never asks about.
    ///
    /// THE SPEED IS THE ONE NUMBER THAT DECIDES WHETHER THIS WORKS. Too fast and the props
    /// behind the player have not finished loading when the scan asks about them, and the
    /// survey completes, reports success, and has found a third of the map -- which is the
    /// worst kind of failure because it looks exactly like the good one. The default is 45 m/s,
    /// about 160 km/h, which is a fast car and inside what the engine keeps up with at rooftop
    /// height.
    ///
    /// AND THE WHOLE MAP IS THEREFORE ABOUT TWO HOURS: 26 lanes and 300 km at the default scan
    /// radius. That is the honest figure and it is why the bounds are settings -- nearly every
    /// machine in the game is in Los Santos proper, which is 11 lanes and about twenty minutes,
    /// and the ini has that rectangle written out. The readout says the percentage and the
    /// minutes left, because the alternative is somebody watching a frozen man drift north for
    /// ten minutes wondering whether it has hung.
    ///
    /// EVERYTHING IT DOES TO THE PLAYER IS UNDONE, including on a crash, a reload and a
    /// character switch: collision, gravity, invincibility, the radar and where he was standing
    /// when it started. A tool that leaves the player intangible in the sky is worse than no
    /// tool.
    /// </remarks>
    internal sealed class Survey
    {
        /// <summary>
        /// The rectangle to fly, from the ini, with the edges sorted so a west/east typed the
        /// wrong way round is a survey rather than an infinite loop.
        ///
        /// The playable world runs about -4000..4500 east-west and -4000..8000 north-south,
        /// which is the default. See Settings.SurveyWest for why it is a setting and what the
        /// full map actually costs.
        /// </summary>
        private float WestX { get { return Math.Min(_cfg.SurveyWest, _cfg.SurveyEast); } }
        private float EastX { get { return Math.Max(_cfg.SurveyWest, _cfg.SurveyEast); } }
        private float SouthY { get { return Math.Min(_cfg.SurveySouth, _cfg.SurveyNorth); } }
        private float NorthY { get { return Math.Max(_cfg.SurveySouth, _cfg.SurveyNorth); } }

        /// <summary>How long a lane is. Never zero, so nothing here divides by it.</summary>
        private float Lane { get { return Math.Max(1f, NorthY - SouthY); } }

        /// <summary>
        /// How high above the ground it flies.
        ///
        /// LOW ENOUGH THAT THE GAME STREAMS THE DETAIL. GTA drops small props first as the
        /// camera climbs -- an aircraft at a thousand feet has no vending machines under it in
        /// any meaningful sense -- so this stays at roughly rooftop height, high enough to clear
        /// the buildings it is passing over and low enough that what is on the pavement below
        /// is loaded.
        /// </summary>
        private const float Altitude = 60f;

        private readonly Settings _cfg;

        private bool _running;
        private bool _wasVisible;

        private Vector3 _startedAt;
        private float _startedFacing;

        private Vector3 _at;
        private int _lane;
        private bool _northbound;

        private int _lanes;

        /// <summary>Whether the map is being flown right now, for the menu row and the readout.</summary>
        public bool Running => _running;

        public Survey(Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Starts or stops it. The menu row and the hotkey both come through here.</summary>
        public void Toggle()
        {
            if (_running) Stop("stopped");
            else Start();
        }

        // ======================================================================

        private void Start()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead)
                {
                    Notify("~r~Not now.");
                    return;
                }

                // OUT OF THE CAR FIRST. A frozen ped inside a vehicle takes the vehicle with it
                // in some states and not others, and the one that goes wrong leaves a car
                // hanging in the air over Vinewood for the rest of the session.
                if (me.IsInVehicle())
                {
                    Notify("~y~Get out of the car first.");
                    return;
                }

                _startedAt = me.Position;
                _startedFacing = me.Heading;

                _lane = 0;
                _northbound = true;

                var step = Spacing();
                _lanes = Math.Max(1, (int)Math.Ceiling((EastX - WestX) / step));

                _at = new Vector3(WestX, SouthY, Altitude);

                Hold(me, true);

                _running = true;

                Log.Info("Survey: starting. " + _lanes + " lane(s) at " + step.ToString("0") +
                         " m spacing, " + _cfg.SurveySpeed.ToString("0") + " m/s, " +
                         (_lanes * Lane / 1000f).ToString("0") + " km, roughly " +
                         (Total() / 60f).ToString("0") + " minute(s). Bounds x " +
                         WestX.ToString("0") + ".." + EastX.ToString("0") + ", y " +
                         SouthY.ToString("0") + ".." + NorthY.ToString("0") + ".");

                Notify("~g~Survey started.~s~ " + _lanes + " lanes, about " +
                       (Total() / 60f).ToString("0") + " minutes. Press it again to stop.");
            }
            catch (Exception ex)
            {
                Log.Error("Survey could not start", ex);
                _running = false;
            }
        }

        /// <summary>
        /// Puts everything back. Called on finishing, on stopping, and from the way out of the
        /// script -- a tool that leaves the player intangible in the sky is worse than no tool.
        /// </summary>
        public void Stop(string why)
        {
            if (!_running) return;

            _running = false;

            try
            {
                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    Hold(me, false);

                    // BACK WHERE HE WAS, and on the ground under it rather than at the exact Z
                    // he left from: he may have started on a roof this has since flown over, and
                    // the ground has certainly been unloaded and reloaded in between.
                    var back = _startedAt;

                    float z;
                    if (Ground(back, out z)) back.Z = z;

                    Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me.Handle,
                                  back.X, back.Y, back.Z, false, false, false);
                    Function.Call(Hash.SET_ENTITY_HEADING, me.Handle, _startedFacing);
                }

                Function.Call(Hash.CLEAR_FOCUS);
                Function.Call(Hash.DISPLAY_RADAR, true);
            }
            catch (Exception ex)
            {
                Log.Once("survey-stop", "Could not tidy up after the survey: " + ex.Message);
            }

            Log.Info("Survey " + why + ".");
        }

        // ======================================================================

        public void Update(float dt)
        {
            if (!_running) return;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) { Stop("lost the player"); return; }

                // Dying mid-survey is not a state anything below is written for, and a corpse
                // flying a grid is not a bug anybody should have to report.
                if (me.IsDead) { Stop("the player died"); return; }

                Move(dt);

                if (!_running) return;

                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me.Handle,
                              _at.X, _at.Y, _at.Z, false, false, false);

                // THE STREAMER IS TOLD TO LOOK AHEAD, not merely followed. FOCUS is what the
                // engine loads around, and pointing it a little up the lane rather than at the
                // player means the ground he is about to cross has started loading before he
                // gets there -- which is the whole difference between a survey that finds
                // machines and one that flies over them.
                var ahead = _at;
                ahead.Y += (_northbound ? 1f : -1f) * Math.Max(1f, _cfg.SurveySpeed) * 4f;

                Function.Call(Hash.SET_FOCUS_POS_AND_VEL, ahead.X, ahead.Y, ahead.Z, 0f, 0f, 0f);
                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, _at.X, _at.Y, _at.Z);

                // Nothing should be able to interfere with a man who is not really there.
                Function.Call(Hash.CLEAR_PLAYER_WANTED_LEVEL, Game.Player.Handle);
            }
            catch (Exception ex)
            {
                Log.Once("survey", "The survey stopped: " + ex.Message);
                Stop("failed");
            }
        }

        /// <summary>Along the lane, and across to the next one at the end of it.</summary>
        private void Move(float dt)
        {
            if (dt <= 0f) return;

            // A LONG FRAME IS NOT A LONG FLIGHT. A loading screen, an alt-tab or a hitch would
            // otherwise move him a kilometre in one step, over ground nothing ever scanned.
            if (dt > 0.2f) dt = 0.2f;

            var step = Math.Max(1f, _cfg.SurveySpeed) * dt;

            _at.Y += _northbound ? step : -step;

            var done = _northbound ? _at.Y >= NorthY : _at.Y <= SouthY;
            if (!done) return;

            _lane++;

            if (_lane >= _lanes)
            {
                Stop("finished");
                Notify("~g~Survey finished.~s~ Everything found is on the map and in " +
                       "machines.json.");
                return;
            }

            _northbound = !_northbound;

            _at.X = WestX + _lane * Spacing();
            _at.Y = _northbound ? SouthY : NorthY;

            Log.Info("Survey: lane " + (_lane + 1) + " of " + _lanes + ", x " +
                     _at.X.ToString("0") + ".");
        }

        // ======================================================================

        /// <summary>
        /// How far apart the lanes are: the radius MachineBlips actually scans, so no strip
        /// between two lanes is ever left unasked-about.
        ///
        /// THE SCAN RADIUS AND NOT A SETTING OF ITS OWN. Two numbers that have to agree are two
        /// numbers that stop agreeing, and this one stops agreeing silently -- the survey would
        /// complete, report success, and have missed a strip down the middle of every lane.
        /// </summary>
        private float Spacing()
        {
            var reach = _cfg.ShopBlipRange <= 0f ? 400f : _cfg.ShopBlipRange;

            // A LITTLE UNDER THE DIAMETER. Lanes exactly a diameter apart meet at a point, and
            // a point of overlap is no overlap at all once the player is moving and the scan is
            // on a two-second clock. Three quarters leaves a real margin at both edges.
            return Math.Max(40f, reach * 1.5f);
        }

        /// <summary>How long the whole thing takes, in seconds, at the current speed.</summary>
        public float Total()
        {
            var lanes = Math.Max(1, (int)Math.Ceiling((EastX - WestX) / Spacing()));

            return lanes * Lane / Math.Max(1f, _cfg.SurveySpeed);
        }

        /// <summary>How far through it is, 0 to 1. For the readout.</summary>
        public float Progress()
        {
            if (!_running || _lanes <= 0) return 0f;

            var down = _northbound ? _at.Y - SouthY : NorthY - _at.Y;
            var lane = down / Lane;

            var t = (_lane + lane) / _lanes;

            return t < 0f ? 0f : t > 1f ? 1f : t;
        }

        /// <summary>Seconds left at the current speed, for the readout.</summary>
        public float Left()
        {
            return Math.Max(0f, Total() * (1f - Progress()));
        }

        // ======================================================================

        /// <summary>
        /// Everything done to the player on the way in, and the exact reverse on the way out.
        ///
        /// ONE METHOD FOR BOTH DIRECTIONS, because the failure this class must not have is a
        /// state set on the way in and forgotten on the way out. Written as a pair of calls per
        /// line, it is readable at a glance whether anything is missing.
        /// </summary>
        private void Hold(Ped me, bool on)
        {
            var player = Game.Player;

            Function.Call(Hash.SET_ENTITY_COLLISION, me.Handle, !on, !on);
            Function.Call(Hash.SET_ENTITY_INVINCIBLE, me.Handle, on);
            Function.Call(Hash.FREEZE_ENTITY_POSITION, me.Handle, on);
            Function.Call(Hash.SET_PLAYER_INVINCIBLE, player.Handle, on);

            // OUT OF SIGHT WHILE IT FLIES. A man in a t-shirt in a rigid standing pose gliding
            // over Los Santos is funny once and distracting for the next forty minutes, and the
            // camera is following him the whole way. His visibility is remembered rather than
            // assumed, because something else may have had a reason to hide him.
            if (on) _wasVisible = me.IsVisible;

            Function.Call(Hash.SET_ENTITY_VISIBLE, me.Handle, on ? false : _wasVisible, false);

            // THE RADAR STAYS ON. It is the one thing worth watching while this runs -- the
            // markers appear on it as they are found, which is the survey visibly working.
            Function.Call(Hash.DISPLAY_RADAR, true);

            if (!on) Function.Call(Hash.CLEAR_FOCUS);
        }

        private static bool Ground(Vector3 at, out float z)
        {
            z = at.Z;

            try
            {
                using (var found = new OutputArgument())
                {
                    if (!Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD,
                                             at.X, at.Y, at.Z + 25f, found, false)) return false;

                    z = found.GetResult<float>();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void Notify(string text)
        {
            try { Core.Compat.Ticker(text); }
            catch { /* a missing notification is not worth failing the tool over */ }
        }
    }
}
