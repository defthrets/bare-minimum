using System;
using System.Drawing;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// HOW MUCH AIR IS LEFT, across the minimap's plate, only while he is under the water.
    ///
    /// THE GAME'S OWN ONE WENT WHEN THE HEALTH STRIP DID. The breath meter is not a thing of
    /// its own: it lives in the same clip of the minimap's movie as the health and armour bars,
    /// so the golf layout that takes those takes it as well -- see Stock. That was not noticed
    /// for as long as it was because you have to go swimming to find out.
    ///
    /// IT COULD NOT SIMPLY BE LET BACK. Bringing the strip back underwater means bringing the
    /// health and armour bars back with it, since one layout covers all three -- and the place
    /// the game drew them is not empty any more: the plate is there, with the street on it. So
    /// the meter is drawn rather than restored, and drawn in exactly the space the original
    /// used, which is the space the eye already goes to.
    ///
    /// IT TAKES THE PLATE WHILE IT IS UP. The plate carries the speed and the dash lights, and
    /// both of those want a vehicle, so underwater there is nothing there to displace. What it
    /// does displace it displaces on purpose: a man with nine seconds of air does not need to
    /// know what street he is over.
    ///
    /// THE FULL IS MEASURED, NOT ASSUMED. GET_PLAYER_UNDERWATER_TIME_REMAINING hands back a
    /// number without saying what the top of it is, and the top MOVES -- the diving ability
    /// raises it, and so does a rebreather. So the highest reading of the dive is the full, and
    /// the first reading of a dive is the highest one there will be. Nothing has to be told
    /// what a lungful is worth.
    ///
    /// IT BEATS WHEN IT IS NEARLY OUT, and faster the less there is -- the one bar in this mod
    /// where the level is a countdown to something that kills you in a few seconds, so the
    /// warning is worth more than the tidiness of a bar that only darkens.
    /// </summary>
    internal sealed class Breath
    {
        /// <summary>Below this the bar beats. A quarter is about three seconds on a standard pair of lungs.</summary>
        private const float LowAir = 0.28f;

        /// <summary>How long the bar stays up after he surfaces, and how long it takes to go.</summary>
        private const float HoldSeconds = 0.7f;
        private const float FadeSeconds = 0.45f;
        private const float RiseSeconds = 0.18f;

        /// <summary>The ramp: pale at a full chest, deep at nothing, and the midpoint pulled low for the same reason health's is.</summary>
        private static readonly Color Mid = Color.FromArgb(255, 54, 130, 196);
        private static readonly Color Low = Color.FromArgb(255, 14, 36, 72);

        /// <summary>What the beat washes toward. Nearly white, because at this end of the bar being subtle is not a virtue.</summary>
        private static readonly Color Gasp = Color.FromArgb(255, 226, 246, 255);

        /// <summary>The most air seen this dive. The full mark, measured. Zero between dives.</summary>
        private float _full;

        /// <summary>0 while it is away, 1 while it is up. Eased, so it arrives and leaves rather than appearing.</summary>
        private float _show;

        /// <summary>How long since he surfaced, in seconds. Held before the fade starts.</summary>
        private float _up = 999f;

        /// <summary>
        /// THE BEAT'S PHASE, ACCUMULATED -- not the clock times a rate.
        ///
        /// The rate rises as the air runs out, and a phase worked out as time x rate jumps every
        /// time the rate changes, because the whole history behind it is rescaled along with it.
        /// The same mistake has been made twice in this mod already, on the energy streaks and
        /// on the heartbeat. It is not going to be made a third time.
        /// </summary>
        private float _phase;

        private int _last;

        private bool _said;

        /// <summary>
        /// Draws the meter across the plate. TRUE while it is on screen at all, so the caller
        /// knows the plate is spoken for.
        ///
        /// The bounds are the plate's own -- the frame's outer edges and its top and foot -- so
        /// it is the width of the map and the frame either side of it, which is the width the
        /// game's own breath bar was.
        /// </summary>
        public bool Draw(Settings cfg, float outerL, float outerR, float top, float bottom,
                         float pad, float strength)
        {
            var dt = Step();

            if (!cfg.MinimapBreath) { _show = 0f; return false; }

            float air;
            var under = Read(out air);

            // THE FULL IS THE HIGHEST READING OF THE DIVE, and it is reset between dives rather
            // than kept, so a rebreather picked up between them is noticed.
            if (under)
            {
                _up = 0f;
                if (air > _full) _full = air;
            }
            else
            {
                _up += dt;
                if (_up > HoldSeconds + FadeSeconds) _full = 0f;
            }

            // Up while he is under; held a moment after he surfaces so the last of it can be
            // read, then gone.
            var want = under || _up < HoldSeconds ? 1f
                     : Ink.Clamp01(1f - (_up - HoldSeconds) / FadeSeconds);

            var toward = want > _show ? dt / RiseSeconds : dt / FadeSeconds;
            _show += Ink.Clamp(want - _show, -toward, toward);
            _show = Ink.Clamp01(_show);

            if (_show <= 0.004f) return false;

            var level = _full > 0.01f ? Ink.Clamp01(air / _full) : 1f;

            var h = bottom - top;
            if (h < 0.010f) return false;

            if (!_said)
            {
                _said = true;
                Log.Info("Air meter: the game's own breath bar goes with its health strip -- one " +
                         "minimap layout covers both -- so it is drawn on the plate instead.");
            }

            // ---- the bar's own box, inside the frame ----
            //
            // A bar as tall as the plate would BE the plate; a little under half of it leaves
            // the black either side reading as the frame it is part of.
            var barH = Math.Max(0.0040f, h * 0.42f);
            var y = top + (h - barH) * 0.5f;

            var inset = pad * 0.5f;
            var x = outerL + inset;
            var w = (outerR - inset) - x;
            if (w <= 0.004f) return false;

            var k = cfg.HudOpacity * strength * _show;
            var aspect = Ink.Aspect;
            var edge = Math.Max(0.0009f, barH * 0.14f / aspect);

            // THE ROW'S OWN SURROUND AND CHANNEL, 228 over 165 -- see Gauge.Frame. It is the
            // same instrument lying down and it has to be built out of the same two numbers, or
            // it reads as something else's bar that happens to be nearby.
            Ink.Bar(x - edge, y - edge * aspect, w + edge * 2f, barH + edge * 2f * aspect,
                    Ink.Alpha(Color.FromArgb(228, 0, 0, 0), (int)(228f * k + 0.5f)));

            Ink.Bar(x, y, w, barH, Ink.Alpha(Color.FromArgb(165, 28, 28, 32), (int)(165f * k + 0.5f)));

            var fill = w * level;
            if (fill <= 0.0006f) return true;

            // ---- the colour, and the beat at the bottom of it ----
            var body = Ramp(cfg.MinimapBreathColour, level);

            if (level < LowAir)
            {
                // FASTER THE LESS THERE IS: about one and a half a second with a quarter left,
                // about four a second on the last of it. Accumulated, see _phase.
                var urgency = 1f - level / LowAir;
                _phase += dt * (1.5f + urgency * 2.6f);

                var beat = (float)((Math.Sin(_phase * Math.PI * 2.0) + 1.0) * 0.5);
                body = Ink.Mix(body, Gasp, 0.12f + 0.62f * beat * urgency);
            }
            else
            {
                _phase = 0f;
            }

            Ink.Bar(x, y, fill, barH, Ink.Alpha(body, (int)(235f * k + 0.5f)));

            return true;
        }

        /// <summary>Seconds since the last frame, clamped: a pause or a load must not advance anything by a minute.</summary>
        private float Step()
        {
            var now = Game.GameTime;
            var dt = _last == 0 ? 0f : (now - _last) / 1000f;
            _last = now;

            return Ink.Clamp(dt, 0f, 0.25f);
        }

        /// <summary>
        /// Whether he is under, and how much air the game says is left.
        ///
        /// The ped is asked whether it is swimming under water rather than the player being
        /// asked for its air, because the air reading is a number the game keeps whether or not
        /// anyone is holding their breath, and a bar that appeared on dry land would be a bar
        /// that meant nothing.
        /// </summary>
        private static bool Read(out float air)
        {
            air = 0f;

            try
            {
                var me = Game.Player == null ? null : Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead) return false;

                air = Function.Call<float>(Hash.GET_PLAYER_UNDERWATER_TIME_REMAINING, Game.Player.Handle);

                return Function.Call<bool>(Hash.IS_PED_SWIMMING_UNDER_WATER, me.Handle);
            }
            catch (Exception ex)
            {
                Log.Once("breath-read", "Could not read the air left, so the meter stays away: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Pale at a full chest, deep at nothing. The midpoint sits BELOW the arithmetic middle
        /// for the reason Paint.Ramp's does: brightness is not perceived linearly, and a
        /// straight line between two colours spends its whole top half looking full.
        /// </summary>
        private static Color Ramp(Color full, float level)
        {
            level = Ink.Clamp01(level);

            return level >= 0.5f
                ? Ink.Mix(Mid, full, (level - 0.5f) * 2f)
                : Ink.Mix(Low, Mid, level * 2f);
        }
    }
}
