using System;
using System.Drawing;
using GTA;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.Needs;

namespace BareMinimum.UI
{
    /// <summary>
    /// The two icons that sit beside the minimap.
    ///
    /// NOT BARS. That was the requirement, and it is the reason the whole thing works the way
    /// it does: an apple with bites out of it and an eye that closes, five drawn states each,
    /// where the SILHOUETTE carries the reading. A bar bent into a ring or hidden behind an
    /// icon is still a bar; a shape that changes is a state.
    ///
    /// Colour is a second, redundant channel on top -- green through amber to red. Redundant
    /// on purpose: it is the part that fails for a colourblind player, and the silhouette
    /// still says everything without it.
    /// </summary>
    internal sealed class Gauge
    {
        private readonly Settings _cfg;

        /// <summary>
        /// Five files per need, held open rather than reloaded.
        ///
        /// CustomSprite keeps a texture handle, so building one per frame leaks the lot. Ten
        /// icons is the entire cost of the HUD.
        /// </summary>
        private readonly Icon[] _apple = new Icon[5];
        private readonly Icon[] _eye = new Icon[5];

        private bool _measured;

        public Gauge(Settings cfg)
        {
            _cfg = cfg;

            for (var i = 0; i < 5; i++)
            {
                _apple[i] = new Icon("apple" + i + ".png");
                _eye[i] = new Icon("eye" + i + ".png");
            }
        }

        // ======================================================================
        // Where it goes
        // ======================================================================

        /// <summary>
        /// The right-hand edge of the minimap, as a fraction of screen WIDTH.
        ///
        /// THIS IS NOT A CONSTANT, and that is the entire reason this method exists. GTA
        /// anchors the minimap to screen HEIGHT: it is the same number of pixels wide on any
        /// monitor of the same height, and therefore a SHRINKING fraction of the width as the
        /// screen gets wider. On 16:9 its right edge sits at about 0.157 of the width; on a
        /// 21:9 ultrawide the same map ends at about 0.117.
        ///
        /// A default written for one is visibly wrong on the other -- floating out in the
        /// middle of the screen, or sitting on top of the map. Scaling the 16:9 figure by the
        /// aspect ratio puts it against the map on both.
        /// </summary>
        private static float MinimapRight()
        {
            const float rightAt169 = 0.157f;
            const float reference = 16f / 9f;

            var aspect = Aspect();
            if (aspect < 1.1f) aspect = reference;

            return rightAt169 * reference / aspect;
        }

        private static float Aspect()
        {
            try
            {
                var a = GTA.UI.Screen.AspectRatio;
                if (a > 0.5f && a < 6f) return a;
            }
            catch
            {
                // Fall through to the safe default.
            }

            return 16f / 9f;
        }

        /// <summary>
        /// Says, once, where the icons actually landed in real pixels.
        ///
        /// Everything here is written in fractions, and a fraction tells nobody how big a
        /// thing looks on somebody else's monitor. One line in the log with the resolution and
        /// the pixel size settles "it is too big" in a single reading, instead of trading
        /// screenshots about it. Fumes learned this the hard way on its own gauge.
        /// </summary>
        private void Measure(float x, float y, float side)
        {
            if (_measured) return;
            _measured = true;

            try
            {
                var res = GTA.UI.Screen.Resolution;

                Log.Info("HUD: icons " + (side * res.Height).ToString("0") + " px square at " +
                         (x * res.Width).ToString("0") + "," + (y * res.Height).ToString("0") +
                         "  (screen " + res.Width + "x" + res.Height +
                         ", aspect " + Aspect().ToString("0.00") +
                         ", minimap ends at " + (MinimapRight() * res.Width).ToString("0") + " px" +
                         ", auto=" + _cfg.HudAutoPosition + ")");
            }
            catch (Exception ex)
            {
                Log.Once("gauge-measure", "Could not read the screen size: " + ex.Message);
            }
        }

        // ======================================================================
        // Drawing
        // ======================================================================

        public void Draw(Needs.Needs needs, bool suspended)
        {
            if (!_cfg.ShowHud || needs == null) return;

            // Nothing over a fade, a cutscene or the pause menu. The HUD is drawn per frame
            // and would otherwise sit on top of a black screen during a sleep.
            if (suspended || !Visible()) return;

            try
            {
                var side = _cfg.HudSize;

                // Square ON SCREEN. Sizes are fractions of the screen's own width and height,
                // so equal numbers give a sprite as much wider than it is tall as the screen
                // is -- a third again too wide on a 21:9. Icon.DrawSized takes both, so the
                // width has the aspect divided back out of it.
                var wide = side / Aspect();

                var gap = side * _cfg.HudGap;

                var x = _cfg.HudAutoPosition
                    ? MinimapRight() + wide * 0.55f
                    : _cfg.HudX;

                // Sat so the PAIR ends level with the foot of the minimap, which is where the
                // health and armour strips are -- the tidiest line to share.
                var bottom = _cfg.HudAutoPosition ? 0.955f : _cfg.HudY + side * 2f + gap;
                var top = bottom - (side * 2f + gap);

                Measure(x, top, side);

                Row(_apple, needs.Hunger, x, top + side / 2f, wide, side);
                Row(_eye, needs.Sleep, x, top + side + gap + side / 2f, wide, side);
            }
            catch (Exception ex)
            {
                Log.Once("gauge", "The HUD could not be drawn: " + ex.Message);
            }
        }

        /// <summary>One need: pick the stage, pick the colour, draw it.</summary>
        private void Row(Icon[] set, Need need, float centreX, float centreY,
                         float wide, float tall)
        {
            if (_cfg.HudHideWhenFine && need.Value > _cfg.HudFineAbove) return;

            var stage = need.Stage;
            if (stage < 0) stage = 0;
            if (stage > 4) stage = 4;

            var icon = set[stage];
            if (icon == null || icon.Missing) return;

            icon.DrawSized(centreX + wide / 2f, centreY, wide, tall, Colour(need));
        }

        /// <summary>
        /// The icon's colour: the ramp, at the HUD's opacity, pulsing when critical.
        ///
        /// The pulse is on WALL CLOCK rather than a frame counter, so it beats at the same
        /// rate whatever the framerate is doing -- a counter makes it race on a fast machine
        /// and crawl on a slow one.
        /// </summary>
        private Color Colour(Need need)
        {
            var c = OnRamp(need.Value);

            if (_cfg.HudFlashWhenCritical && need.Stage == 0)
            {
                // Sine rather than a square wave: a hard blink is a fault light, and this is
                // meant to be urgent without being an alarm.
                var pulse = (float)Math.Abs(Math.Sin(Environment.TickCount / 260.0));
                c = Mix(c, Color.FromArgb(c.A, 255, 235, 225), 0.25f + 0.55f * pulse);
            }

            var a = (int)(c.A * _cfg.HudOpacity + 0.5f);
            if (a < 0) a = 0;
            if (a > 255) a = 255;

            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        // ======================================================================
        // The colour ramp
        // ======================================================================
        //
        // Green at full through amber to red at empty. Five stops rather than a straight
        // two-colour blend, because green to red interpolated directly runs through a muddy
        // brown at the halfway point -- putting real gold and orange in the middle keeps every
        // part of the range a colour somebody would name.

        private static readonly float[] Stops = { 0f, 0.25f, 0.50f, 0.75f, 1f };

        private static readonly Color[] Ramp =
        {
            Color.FromArgb(235, 216,  58,  50),   // empty     red
            Color.FromArgb(235, 228, 108,  46),   // bad       orange
            Color.FromArgb(235, 240, 170,  56),   // middling  amber
            Color.FromArgb(235, 228, 208,  94),   // fine      gold
            Color.FromArgb(235, 152, 216, 132)    // full      green
        };

        private static Color OnRamp(float t)
        {
            if (t <= Stops[0]) return Ramp[0];
            if (t >= Stops[Stops.Length - 1]) return Ramp[Ramp.Length - 1];

            for (var i = 1; i < Stops.Length; i++)
            {
                if (t > Stops[i]) continue;

                var span = Stops[i] - Stops[i - 1];
                var u = span <= 0.0001f ? 0f : (t - Stops[i - 1]) / span;

                return Mix(Ramp[i - 1], Ramp[i], u);
            }

            return Ramp[Ramp.Length - 1];
        }

        private static Color Mix(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;

            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// Whether there is anything on screen worth drawing over.
        ///
        /// The radar being hidden is the useful test: the game turns it off for cutscenes, for
        /// the pause menu and during a fade, which is exactly the set of moments a HUD icon
        /// should not be floating in.
        /// </summary>
        private static bool Visible()
        {
            try
            {
                if (Game.IsPaused) return false;
                if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_IN)) return false;
                if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return false;

                // IS_RADAR_HIDDEN only. There is no IS_RADAR_ENABLED in SHVDN 3.9's Hash
                // enum despite the native existing in some lists -- checked by reflecting
                // the dll rather than assumed, which is the only way to find that out
                // before the compiler does.
                return !Function.Call<bool>(Hash.IS_RADAR_HIDDEN);
            }
            catch
            {
                return true;
            }
        }
    }
}
