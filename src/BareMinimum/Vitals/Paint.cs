using System;
using System.Drawing;
using BareMinimum.Core;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// What the two renderers share: the colour of each bar in each state, and two bits of
    /// arithmetic. Here rather than in either of them, because a bar lying down and a bar
    /// standing up are the same bar and must agree about what red means.
    ///
    /// THE STOCK BARS KEEP THEIR OWN COLOURS. Health is the game's green, and it runs down
    /// through amber to red with the level, the way the eye expects a health bar to; armour
    /// is the game's blue; the third bar is yellow -- energy -- or the special ability's gold.
    /// The hunger and sleep bars beside these went orange and purple so that no two of the
    /// five share a family.
    /// </summary>
    internal static class Paint
    {
        /// <summary>A bar's fill colour at the HUD's opacity. 235 is the alpha every bar in this family uses at full.</summary>
        public static Color Body(float opacity, Color c, float strength)
        {
            return Ink.Alpha(c, (int)(235f * opacity * strength + 0.5f));
        }

        private static readonly Color Amber = Color.FromArgb(255, 236, 176, 52);
        private static readonly Color Red = Color.FromArgb(255, 214, 69, 58);

        /// <summary>
        /// Green at full, amber at half, red at empty -- and a beat toward red once there is
        /// not much of it left.
        /// </summary>
        public static Color Health(Settings cfg, float opacity, Readings r, Momentum m, float strength)
        {
            // ARMOUR'S BLUE WHILE THERE IS ARMOUR. The column stands full and blue for as long
            // as the plate holds, and only turns to the ramp -- and to the low-health beat --
            // once it is the man himself taking the damage.
            if (r.Armour > 0.002f) return Body(opacity, cfg.VitalsArmour, strength);

            var c = Body(opacity, Ramp(cfg.VitalsHealth, r.Health), strength);

            if (cfg.VitalsLowHealthPulse && r.Health > 0f && r.Health < cfg.VitalsLowHealthAt)
            {
                // Sine rather than a square wave: a hard blink is a fault light, and this is
                // meant to be urgent without being an alarm. On the wall clock, because it is
                // a warning and Pace is for decoration.
                var pulse = (float)Math.Abs(Math.Sin(m.Wall * 3.4));
                c = Ink.Mix(c, Color.FromArgb(c.A, 255, 72, 60), 0.30f + 0.40f * pulse);
            }

            return c;
        }

        /// <summary>The health ramp: the configured green at the top, amber half way, red at the bottom.</summary>
        private static Color Ramp(Color full, float level)
        {
            level = Ink.Clamp01(level);

            return level >= 0.5f
                ? Ink.Mix(Amber, full, (level - 0.5f) * 2f)
                : Ink.Mix(Red, Amber, level * 2f);
        }

        public static Color Armour(Settings cfg, float opacity, float strength)
        {
            return Body(opacity, cfg.VitalsArmour, strength);
        }

        /// <summary>
        /// The third bar. ENERGY: yellow, and while he is too winded to sprint, dimmed and
        /// beating slowly, so the locked state reads before the sprint button does nothing.
        /// The special ability still shows through it -- the bar lights and beats while the
        /// ability runs -- which is the "doubling". Without the energy meter it is the
        /// special's gold, washed out while the game has the ability switched off.
        /// </summary>
        public static Color Third(Settings cfg, float opacity, Readings r, Momentum m, float strength)
        {
            if (r.ThirdIsEnergy)
            {
                var e = Body(opacity, cfg.VitalsEnergyColour, strength);

                if (r.Tired)
                {
                    var beat = 0.5f + 0.5f * (float)Math.Sin(m.Wall * 4.2);
                    e = Ink.Mix(e, Color.FromArgb(e.A, 150, 150, 150), 0.45f);
                    return Ink.Fade(e, 0.62f + 0.18f * beat);
                }

                if (r.SpecialActive && cfg.VitalsActivePulse)
                {
                    var pulse = (float)Math.Abs(Math.Sin(m.Wall * 5.5));
                    return Ink.Mix(e, Color.FromArgb(e.A, 255, 246, 214), 0.25f + 0.30f * pulse);
                }

                return e;
            }

            var c = Body(opacity, cfg.VitalsSpecialColour, strength);

            if (!r.SpecialEnabled)
            {
                c = Ink.Mix(c, Color.FromArgb(c.A, 150, 150, 150), 0.55f);
                return Ink.Fade(c, 0.7f);
            }

            if (r.SpecialActive && cfg.VitalsActivePulse)
            {
                var pulse = (float)Math.Abs(Math.Sin(m.Wall * 5.5));
                return Ink.Mix(c, Color.FromArgb(c.A, 255, 246, 214), 0.25f + 0.30f * pulse);
            }

            if (r.SpecialFull)
            {
                // Barely there: a full meter looks lit rather than printed, and nothing more.
                var breath = 0.5f + 0.5f * (float)Math.Sin(m.Wall * (2.0 * Math.PI / 2.4));
                return Ink.Mix(c, Color.FromArgb(c.A, 255, 250, 230), 0.08f * breath);
            }

            return c;
        }

        /// <summary>
        /// A soft repeating hump: 0 away from the centre of a band, 1 at it, wrapping at 1.
        ///
        /// Wrapped rather than clamped, so a pulse travelling off the end of the bar arrives
        /// back at the start instead of stopping -- which is what makes the contents look
        /// like they are going round rather than draining away.
        /// </summary>
        public static float Pulse(float u, float width)
        {
            u -= (float)Math.Floor(u);

            var d = Math.Abs(u - 0.5f) * 2f;
            if (d >= width) return 0f;

            // Cosine rather than a triangle: a linear falloff has a visible corner at the peak
            // and reads as a chevron rather than as a swell.
            return 0.5f + 0.5f * (float)Math.Cos(d / width * Math.PI);
        }

        /// <summary>
        /// A repeatable 0-to-1 from one number. The usual sine-and-throw-away-the-top trick.
        ///
        /// Not good randomness and does not need to be: it scatters a handful of dots in a
        /// box, and the only thing that matters is that the same input always gives the same
        /// dot and that neighbouring inputs do not give neighbouring dots.
        /// </summary>
        public static float Scatter(float v)
        {
            var x = Math.Sin(v * 12.9898) * 43758.5453;
            return (float)(x - Math.Floor(x));
        }
    }
}
