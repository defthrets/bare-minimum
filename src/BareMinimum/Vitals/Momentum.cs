using System;
using GTA;
using GTA.Math;
using BareMinimum.Core;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// The clock the vitals run on, and the liquid's momentum.
    ///
    /// Two kinds of movement, kept apart on purpose. The idle motion -- the surface bowing and
    /// drifting on its own -- is a function of the clock and nothing else, which is what makes
    /// it smooth: a sine read off a clock cannot stutter. The SLOSH is the opposite: a spring
    /// per bar, kicked when something happens and left to ring down, so a hit lands as a
    /// jolt and a hard stop in a car leans the whole row and lets it settle back.
    ///
    /// The hunger and sleep bars beside these run the same spring in Gauge, off the same two
    /// dials -- BarSlosh and BarLean -- so the five agree about what a jolt is.
    /// </summary>
    internal sealed class Momentum
    {
        /// <summary>
        /// One bar's worth of momentum: how far the surface is thrown, in fractions of the
        /// bar's length, and how fast it is moving.
        ///
        /// A damped spring, integrated semi-implicitly so it stays stable at any framerate
        /// the game hands us. Under a second a swing, and lightly damped, so a kick rings
        /// three or four times before it is gone -- which reads as liquid rather than as a
        /// cursor being nudged.
        /// </summary>
        public sealed class Spring
        {
            public float S;
            public float V;

            private const float Omega = 7.4f;    // 2 pi over 0.85 seconds
            private const float Damping = 2.1f;  // 2 zeta omega, zeta about 0.14

            private const float Reach = 0.09f;

            public void Kick(float velocity)
            {
                V += velocity;
            }

            public void Step(float dt, float force)
            {
                V += (-Omega * Omega * S - Damping * V + force) * dt;
                S += V * dt;

                if (S > Reach) { S = Reach; if (V > 0f) V = 0f; }
                if (S < -Reach) { S = -Reach; if (V < 0f) V = 0f; }
            }

            public void Rest()
            {
                S = 0f;
                V = 0f;
            }
        }

        public readonly Spring Health = new Spring();
        public readonly Spring Armour = new Spring();
        public readonly Spring Third = new Spring();

        /// <summary>The paced clock, in seconds. Everything periodic reads this.</summary>
        public float Time => _phase;

        /// <summary>Real seconds, unpaced, for the things that are warnings rather than decoration.</summary>
        public float Wall => _wall;

        /// <summary>
        /// THE CHARGE PHASE for the third bar's streaks, in bars travelled. SIGNED: it counts
        /// backwards while the energy is being spent.
        ///
        /// AN ACCUMULATED PHASE, NOT A CLOCK TIMES A RATE, and the difference is the whole
        /// reason it exists. The streaks used to be positioned from wall * rate, and their rate
        /// changes constantly -- 1.6 while spending, 1.0 while rebuilding, 0.5 at rest, doubled
        /// for the ability, a crawl when winded. Multiplying a growing clock by a rate that
        /// changes rescales everything already accumulated, so the instant the rate moved the
        /// streaks did not speed up, they TELEPORTED to wherever the new rate said they ought
        /// to have got to by now. At the end of a sprint the delta crosses zero several times a
        /// second and every crossing was a jump, each one also handing every lane a fresh
        /// random position. Gauge.Clock has the same comment about the same mistake.
        ///
        /// Integrating instead makes a change of rate continuous by construction: the phase
        /// never jumps, only the speed at which it is growing.
        ///
        /// AND THE SIGN CARRIES THE DIRECTION, which used to be a bool that mirrored the
        /// streaks' progress along the bar -- a second, larger jump on the same frame as the
        /// first. Running the phase backwards means a reversal is a reversal: they slow, stop
        /// and go the other way, which is what a flow doing that actually looks like.
        /// </summary>
        public float Charge => _charge;

        /// <summary>
        /// HOW RECENTLY HE WAS HIT, 1 the instant it lands and 0 a third of a second later.
        ///
        /// AN EVENT NEEDS A DECAY, NOT A LEVEL. HealthDelta is this frame's change and is
        /// therefore non-zero for exactly one frame per hit -- at sixty frames a second that is
        /// sixteen milliseconds of white, which is under what most people reliably see and
        /// nothing at all on a screen that happens to drop a frame. Held and decayed, one hit
        /// is one visible flash however the frame times fall.
        ///
        /// IT IS NOT A SPRING. The springs beside it ring -- overshoot, come back, overshoot
        /// less -- which is right for a liquid being knocked about and wrong for an alarm: a
        /// flash that pulsed twice per hit would say two hits.
        /// </summary>
        public float Hurt => _hurt;

        private float _hurt;

        private float _phase;
        private float _wall;
        private float _charge;
        private float _chargeRate;

        /// <summary>How hard the player is being pushed along their own length, m/s², eased.</summary>
        public float Accel;

        private float _lastAlong;
        private bool _haveAlong;
        private int _lastPed;

        private bool _wasActive;
        private bool _wasFull;

        /// <summary>
        /// The equilibrium lean per m/s² of acceleration, in fractions of a bar, times the
        /// spring's stiffness -- so a steady 10 m/s² of braking leans the liquid about three
        /// per cent of the way along and holds it there until the braking stops.
        /// </summary>
        private const float LeanGain = 0.003f * 7.4f * 7.4f;

        public void Update(Settings cfg, float dt, Readings r, Energy energy)
        {
            if (dt < 0f) dt = 0f;
            if (dt > 0.1f) dt = 0.1f;

            _wall += dt;
            _phase += dt * Math.Max(0.05f, cfg.VitalsPace);

            // Kept bounded. Every period in here divides into a whole number of seconds far
            // short of this, so the wrap is invisible -- and a float grown past a million has
            // lost the precision the shortest of them need.
            if (_phase > 1000000f) _phase -= 1000000f;
            if (_wall > 1000000f) _wall -= 1000000f;

            var force = 0f;

            if (cfg.HudAnimate)
            {
                force = Lean(cfg, dt);
                Kicks(cfg, r, energy);
            }

            Charged(cfg, dt, r);
            Hit(dt, r);

            Health.Step(dt, force);
            Armour.Step(dt, force);
            Third.Step(dt, force);
        }

        /// <summary>
        /// Moves the charge phase on. See Charge for why this is an integration and not a
        /// multiplication.
        ///
        /// THE RATE IS CHASED RATHER THAN SET, which is the second half of the same fix. Even
        /// integrated, a rate that snaps from 1 to -1.6 the frame a sprint starts reads as a
        /// switch being thrown -- and ThirdDelta is a raw per-frame difference, so at the edges
        /// it flickers between "draining" and "at rest" on alternate frames as the meter's
        /// movement falls under what a float can hold. Chased, both of those come out as the
        /// streaks winding up and winding down, and a frame of noise cannot be seen at all.
        /// </summary>
        /// <summary>
        /// Moves the hit-flash on. See Hurt.
        ///
        /// ARMOUR COUNTS. A round stopped by a plate is still a round that hit you, and the bar
        /// is showing armour at the time -- so the flash has to fire off either meter or it
        /// would be silent for exactly as long as the armour lasts, which is the part of a
        /// fight where knowing you are being shot at matters most.
        ///
        /// THE THRESHOLD IS SMALLER THAN THE SPRINGS'. Those want a real knock before they
        /// slosh; this wants to catch a graze, because the question it answers is "am I taking
        /// fire", not "how hard".
        /// </summary>
        private void Hit(float dt, Readings r)
        {
            if (r != null)
            {
                var worst = Math.Min(r.HealthDelta, r.ArmourDelta);

                if (worst < -0.0004f)
                {
                    // Bigger hits flash harder, but even the smallest is plainly visible: this
                    // is a warning and a warning nobody notices is not one.
                    var hit = 0.55f + 0.45f * Math.Min(1f, -worst * 6f);
                    if (hit > _hurt) _hurt = hit;
                }
            }

            if (_hurt <= 0f) return;

            // A THIRD OF A SECOND, linear. An exponential decay has a long dim tail, and a bar
            // that stays faintly pale for a second after every graze reads as being permanently
            // slightly wrong rather than as having just been hit.
            _hurt -= dt * 3f;
            if (_hurt < 0f) _hurt = 0f;
        }

        private void Charged(Settings cfg, float dt, Readings r)
        {
            // Bars per second, near enough: quick spending, steady rebuilding, a drift at rest;
            // a crawl when winded; a race while the ability runs or a stimulant is riding.
            var want = 0.5f;

            if (r != null)
            {
                if (r.ThirdIsEnergy && r.ThirdDelta < -0.00001f) want = -1.6f;
                else if (r.ThirdIsEnergy && r.ThirdDelta > 0.00001f) want = 1.0f;

                // KEEPING THE SIGN. Winded and the ability are about how FAST, not which way,
                // and a crawl that also reversed would say the bar was filling while it drained.
                if (r.Tired) want = want < 0f ? -0.3f : 0.3f;
                if (r.SpecialActive) want *= 2.2f;
                if (r.Wired) want = 2.8f;
            }

            _chargeRate += (want - _chargeRate) * (1f - (float)Math.Pow(0.02, dt));
            _charge += _chargeRate * dt;

            // Kept bounded at both ends, because this one runs backwards.
            if (_charge > 1000000f) _charge -= 1000000f;
            if (_charge < -1000000f) _charge += 1000000f;
        }

        /// <summary>Everything stops moving. For a frame the HUD was not drawn on, so nothing rings on unseen.</summary>
        public void Rest()
        {
            Health.Rest();
            Armour.Rest();
            Third.Rest();
            _haveAlong = false;
            Accel = 0f;
            _hurt = 0f;
        }

        /// <summary>
        /// The player's acceleration along their own facing, as a force on all three springs.
        ///
        /// Read off the vehicle when there is one, because that is what the player feels; off
        /// the ped otherwise, so a landing or a shove still registers. A body swap, a
        /// teleport or a long frame is thrown away rather than turned into a jolt from
        /// nowhere.
        /// </summary>
        private float Lean(Settings cfg, float dt)
        {
            var motion = Ink.Clamp01(cfg.HudBarLean);
            if (motion <= 0f || dt <= 0.0001f) return 0f;

            try
            {
                var ped = Game.Player.Character;
                Entity body = ped;

                if (ped.IsInVehicle())
                {
                    var veh = ped.CurrentVehicle;
                    if (veh != null && veh.Exists()) body = veh;
                }

                var handle = body.Handle;
                var v = body.Velocity;
                var f = body.ForwardVector;

                var along = v.X * f.X + v.Y * f.Y + v.Z * f.Z;

                if (handle != _lastPed || !_haveAlong || dt > 0.09f)
                {
                    _lastPed = handle;
                    _lastAlong = along;
                    _haveAlong = true;
                    Accel = 0f;
                    return 0f;
                }

                var a = (along - _lastAlong) / dt;
                _lastAlong = along;

                if (a > 30f) a = 30f;
                if (a < -30f) a = -30f;

                // Eased, so a physics tick that stutters is a lean rather than a rattle.
                Accel += (a - Accel) * Math.Min(1f, dt * 12f);

                // BRAKING DROPS THE LEVEL, ACCELERATING LIFTS IT. It was the other way round
                // and the player felt it as backwards: the whole row jumping UP under braking
                // reads as the car shoving the liquid the wrong way. Positive acceleration is a
                // positive throw toward the surface, and a hard stop is a dip that rings back.
                return Accel * LeanGain * motion;
            }
            catch
            {
                _haveAlong = false;
                return 0f;
            }
        }

        /// <summary>The jolts: a hit, a top-up, the energy running out or coming back, the ability going off or coming back.</summary>
        private void Kicks(Settings cfg, Readings r, Energy energy)
        {
            var k = Ink.Clamp01(cfg.HudBarSlosh);

            if (k > 0f)
            {
                // About four fifths of the first kicks: "a little less aggressive".
                if (r.HealthDelta < -0.001f)
                {
                    var hit = Math.Min(1f, -r.HealthDelta * 4f);
                    Health.Kick(-(0.28f + 0.52f * hit) * k);
                }
                else if (r.HealthDelta > 0.001f)
                {
                    Health.Kick(0.24f * Math.Min(1f, r.HealthDelta * 4f) * k);
                }

                if (r.ArmourDelta < -0.001f)
                {
                    var hit = Math.Min(1f, -r.ArmourDelta * 4f);
                    Health.Kick(-(0.28f + 0.52f * hit) * k);
                }
                else if (r.ArmourDelta > 0.001f)
                {
                    Health.Kick(0.24f * Math.Min(1f, r.ArmourDelta * 4f) * k);
                }

                if (r.ThirdIsEnergy)
                {
                    // Run dry and the last of it slaps the floor; get your breath back and it lifts.
                    if (energy.JustEmptied) Third.Kick(-0.36f * k);
                    if (energy.JustRecovered) Third.Kick(0.28f * k);
                }
                else if (r.HasThird)
                {
                    if (r.SpecialActive && !_wasActive) Third.Kick(-0.36f * k);
                    if (r.SpecialFull && !_wasFull) Third.Kick(0.28f * k);
                }
            }

            _wasActive = r.SpecialActive;
            _wasFull = r.SpecialFull;
        }
    }
}
