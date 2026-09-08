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

        private float _phase;
        private float _wall;

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

            Health.Step(dt, force);
            Armour.Step(dt, force);
            Third.Step(dt, force);
        }

        /// <summary>Everything stops moving. For a frame the HUD was not drawn on, so nothing rings on unseen.</summary>
        public void Rest()
        {
            Health.Rest();
            Armour.Rest();
            Third.Rest();
            _haveAlong = false;
            Accel = 0f;
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
                    Armour.Kick(-(0.28f + 0.52f * hit) * k);
                }
                else if (r.ArmourDelta > 0.001f)
                {
                    Armour.Kick(0.24f * Math.Min(1f, r.ArmourDelta * 4f) * k);
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
