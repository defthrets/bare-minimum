using System;
using System.Drawing;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// The screen, the one drawing primitive the vitals need, and the colour arithmetic.
    ///
    /// VITALS' OWN DRAW, KEPT. This was a mod of its own before it was folded into Bare
    /// Minimum, and every number in its bars was tuned through these helpers -- the screen
    /// re-read on its own half-second clock, a bar drawn from its top-left, a mix that keeps
    /// the first colour's alpha. UI.Draw agrees about what a rectangle is; this stays so the
    /// bars that came across came across unchanged.
    /// </summary>
    internal static class Ink
    {
        // ======================================================================
        // The screen
        // ======================================================================

        /// <summary>Screen aspect, width over height. Re-read twice a second; the resolution can change mid-session.</summary>
        public static float Aspect
        {
            get { Refresh(); return _aspect; }
        }

        public static int ScreenWidth
        {
            get { Refresh(); return _width; }
        }

        public static int ScreenHeight
        {
            get { Refresh(); return _height; }
        }

        private static float _aspect = 16f / 9f;
        private static int _width = 1920;
        private static int _height = 1080;
        private static int _at;
        private const int CheckMs = 500;

        private static void Refresh()
        {
            var now = Game.GameTime;
            if (now < _at) return;
            _at = now + CheckMs;

            try
            {
                var a = GTA.UI.Screen.AspectRatio;
                if (a > 0.5f && a < 6f) _aspect = a;

                var res = GTA.UI.Screen.Resolution;
                if (res.Width > 0) _width = res.Width;
                if (res.Height > 0) _height = res.Height;
            }
            catch
            {
                // The last good answer stands.
            }
        }

        // ======================================================================
        // Rectangles
        // ======================================================================

        /// <summary>
        /// A filled rectangle, positioned by its CENTRE -- DRAW_RECT's own convention, worth
        /// stating because every other coordinate in a HUD is a corner, and getting it wrong
        /// shifts everything by half its own size, which looks like rounding.
        /// </summary>
        public static void Rect(float centreX, float centreY, float width, float height, Color colour)
        {
            // THROUGH THE ONE DOOR, NOT ROUND THE BACK OF IT. This called DRAW_RECT itself, so
            // the five bars -- far and away the biggest spender in the mod -- were the one thing
            // the draw counter could not see. It logged a peak of a hundred while the real
            // figure was three times that, which is worse than not counting at all: a budget
            // that reads healthy while the frame is being dropped sends you looking at the
            // wrong mod. Everything now goes through UI.Draw, which counts it, throws away
            // anything under half a pixel, and knows when to stop. See UI.Draw.Counted.
            UI.Draw.Rect(centreX, centreY, width, height, colour);
        }

        /// <summary>A rectangle drawn from its top-left, which is how a bar is actually thought about.</summary>
        public static void Bar(float left, float top, float width, float height, Color colour)
        {
            if (width <= 0f || height <= 0f || colour.A <= 0) return;
            Rect(left + width / 2f, top + height / 2f, width, height, colour);
        }

        // ======================================================================
        // Colours
        // ======================================================================

        /// <summary>Blends towards another colour, keeping the first one's alpha.</summary>
        public static Color Mix(Color a, Color b, float k)
        {
            if (k <= 0f) return a;
            if (k > 1f) k = 1f;

            return Color.FromArgb(a.A,
                                  (int)(a.R + (b.R - a.R) * k),
                                  (int)(a.G + (b.G - a.G) * k),
                                  (int)(a.B + (b.B - a.B) * k));
        }

        /// <summary>The same colour at a different alpha. Clamped, because a product can run past.</summary>
        public static Color Alpha(Color c, int alpha)
        {
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;

            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        /// <summary>The colour with its alpha scaled.</summary>
        public static Color Fade(Color c, float k)
        {
            return Alpha(c, (int)(c.A * k + 0.5f));
        }

        public static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }

        public static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}
