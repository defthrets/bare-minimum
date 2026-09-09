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
    /// EVERY BAR IS ONE FAMILY, DEEP AT EMPTY AND VIVID AT FULL. Health is red, armour is the
    /// game's blue, the third bar is energy's fluorescent green-yellow or the special ability's
    /// gold, and hunger and sleep are orange and purple beside them -- no two sharing a family,
    /// so hue says WHICH meter and brightness says how it is doing.
    ///
    /// HEALTH USED TO BREAK THAT RULE and it was the only one that did: green through amber to
    /// red, hue moving with the level, so at half full it was a colour belonging to no meter at
    /// all. It is red the whole way now, and the low-health beat is what carries the urgency
    /// the colour change used to.
    /// </summary>
    internal static class Paint
    {
        /// <summary>A bar's fill colour at the HUD's opacity. 235 is the alpha every bar in this family uses at full.</summary>
        public static Color Body(float opacity, Color c, float strength)
        {
            return Ink.Alpha(c, (int)(235f * opacity * strength + 0.5f));
        }

        // RED THE WHOLE WAY NOW, AND THE HUE STOPS CARRYING ANYTHING.
        //
        // It ran green through amber to red, which is what a health bar has done since health
        // bars existed and is also the one thing in this row that worked differently from
        // everything beside it. Every other bar here is ONE family, deep at empty and vivid at
        // full, on the stated rule that darker is worse and hue says WHICH meter. Health was
        // the exception: its hue moved with its level, so at half full it was a colour that
        // belonged to no meter at all, and the two habits the eye had learned did not both
        // hold across the row.
        //
        // Now it does. Vivid red at full, dark red at nothing, and the whole journey is
        // brightness -- which is the same reading the other five give, and a red bar is not
        // ambiguous about which meter it is whatever shade it happens to be.
        //
        // THE MIDDLE STOP IS NOT HALFWAY BETWEEN THE OTHER TWO. A linear ramp between a vivid
        // red and a very dark one spends its whole upper half looking full, because brightness
        // is not perceived linearly; pulling the midpoint down below the arithmetic middle puts
        // the visible change where the meter is actually worth watching.
        //
        // AND THE BOTTOM STOP IS NOT THE PULSE'S RED. The beat below goes to a hot 255,110,95,
        // far brighter than any stop here -- if they were close the warning would be invisible
        // in exactly the state it exists for, which is the whole reason a dark bottom end is
        // safe to have at all.
        private static readonly Color Amber = Color.FromArgb(255, 158, 24, 28);
        private static readonly Color Red = Color.FromArgb(255, 74, 10, 14);

        /// <summary>
        /// Red the whole way -- vivid at full, dark at empty -- and a beat once there is not
        /// much of it left.
        /// </summary>
        public static Color Health(Settings cfg, float opacity, Readings r, Momentum m, float strength)
        {
            // ARMOUR'S BLUE WHILE THERE IS ARMOUR. The column stands full and blue for as long
            // as the plate holds, and only turns to the ramp -- and to the low-health beat --
            // once it is the man himself taking the damage.
            //
            // THE HIT FLASH STILL FIRES ON IT. Being shot while wearing a plate is still being
            // shot, and it is drawn below so the blue flashes white the same as the red does.
            var c = r.Armour > 0.002f
                        ? Body(opacity, cfg.VitalsArmour, strength)
                        : Body(opacity, Ramp(cfg.VitalsHealth, r.Health), strength);

            // NO SECOND PULSE HERE. The low-health beat used to live on this line, darkening
            // the colour on a sine of its own -- which meant a hurt bar had TWO rhythms running
            // at once, this one and the real heartbeat over in Columns, at two unrelated rates.
            // Two rhythms on one bar is not a heartbeat, it is a fault.
            //
            // The beat that survived is the one with the honest clock: its period comes from
            // his health and how hard he is working, so you can take his pulse off the bar. It
            // throbs dark normally and washes WHITE below the threshold, which is where the
            // warning that used to be here now lives. See Columns.Heartbeat.

            // ---- and white the moment he is hit ----
            //
            // OVER THE TOP OF EVERYTHING ABOVE, on purpose. The beat is a STATE -- you are low,
            // it has been true for a while and goes on being true -- and this is an EVENT,
            // which happened just now and is gone in a third of a second. With both running the
            // event is the one worth seeing, so it is applied last and wins.
            //
            // Not pure white: 255,238,238 keeps a breath of the bar's own colour, so a hit on
            // the red bar and a hit on the blue armour do not both flash to the same flat
            // nothing. See Momentum.Hurt for why it is held rather than read per frame.
            if (m != null && m.Hurt > 0f)
            {
                c = Ink.Mix(c, Color.FromArgb(c.A, 255, 238, 238), Ink.Clamp01(m.Hurt));
            }

            return c;
        }

        /// <summary>The health ramp: the configured red at the top, and two darker ones under it.</summary>
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

                // WIRED: pinned at the top by whatever he took, and saying so. A shimmer over
                // the whole bar, about a second a breath -- brighter than the ability's beat
                // and quicker than anything else in the row, because nothing else in the row is
                // a drug. The bar is not going anywhere while this runs, so the movement is the
                // only thing left to read it by. [HUD] Shimmer scales it; 0 leaves it plain.
                if (r.Wired)
                {
                    var shim = 0.5f + 0.5f * (float)Math.Sin(m.Wall * 6.6);
                    var lift = (0.20f + 0.50f * Ink.Clamp01(cfg.HudShimmer)) * shim;
                    return Ink.Mix(e, Color.FromArgb(e.A, 255, 252, 244), lift);
                }

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
