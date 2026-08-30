using System;
using System.Drawing;

namespace BareMinimum.UI
{
    /// <summary>
    /// The slow lift in brightness that keeps a drawn icon from looking printed on.
    ///
    /// ONE PLACE, because it is now on three sets of pictures -- the two beside the minimap,
    /// the product icons down a shop list, and the marks either side of a menu title -- and
    /// three copies of a sine would drift apart the first time one of them was tuned.
    ///
    /// IT ONLY EVER LIFTS. The ramp colour on a HUD icon is carrying the actual state, and a
    /// breath that dipped darker would spend half its cycle reporting the need as worse than
    /// it is. Going up toward a warm white says "lit" without saying anything else.
    ///
    /// ON THE WALL CLOCK, so it runs at a human rate whatever the framerate is doing.
    /// TickCount is masked positive because it wraps after about twenty-five days of uptime
    /// and a negative remainder would quietly stop everything with nothing to say why.
    /// </summary>
    internal static class Sheen
    {
        /// <summary>How long one breath takes.</summary>
        public const float PeriodMs = 2200f;

        /// <summary>The warm white it lifts toward. Not pure white: that reads as a blowout.</summary>
        private static readonly Color Lit = Color.FromArgb(255, 255, 252, 244);

        /// <summary>
        /// A sine from -1 to 1, shifted by a fraction of a cycle.
        ///
        /// The offset is what stops a column of icons pulsing in unison. In step, eight rows
        /// of a shop list read as the whole panel throbbing; a fraction apart they read as
        /// eight separate things catching the light.
        /// </summary>
        public static float Wave(float offset)
        {
            var now = (Environment.TickCount & int.MaxValue) % (int)PeriodMs;
            return (float)Math.Sin((now / PeriodMs + offset) * 2.0 * Math.PI);
        }

        /// <summary>The colour, lifted. <paramref name="strength"/> 0 leaves it alone.</summary>
        public static Color On(Color c, float offset, float strength)
        {
            if (strength <= 0f) return c;
            if (strength > 1f) strength = 1f;

            var lift = 0.5f + 0.5f * Wave(offset);

            return Mix(c, Lit, strength * lift);
        }

        /// <summary>Blends two colours, keeping the first one's alpha.</summary>
        public static Color Mix(Color a, Color b, float k)
        {
            if (k <= 0f) return a;
            if (k > 1f) k = 1f;

            return Color.FromArgb(a.A,
                                  (int)(a.R + (b.R - a.R) * k),
                                  (int)(a.G + (b.G - a.G) * k),
                                  (int)(a.B + (b.B - a.B) * k));
        }
    }
}
