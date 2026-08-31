using System;
using System.Drawing;
using GTA;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.Needs;
using Hud = BareMinimum.UI.Draw;

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

        /// <summary>
        /// Five stages of eye. THE RESTING FRAME ONLY.
        ///
        /// The blink frames exist and are used -- by the settings panel's title mark, which is
        /// a thing you look at. Beside the minimap they were wrong: that icon lives in
        /// peripheral vision for the whole session, and something that shuts and reopens there
        /// reads as movement in the corner of your eye, over and over, forever.
        ///
        /// What is left is quieter by design. See Shimmer and Bob.
        /// </summary>
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
        /// How wide the minimap is, as a fraction of screen WIDTH.
        ///
        /// GTA sizes the minimap off screen HEIGHT, so it is a constant number of pixels wide
        /// on any monitor of the same height and therefore a shrinking fraction of the width
        /// as the screen gets wider. 0.2785 of the height is the measured figure: 401 px on a
        /// 1440-tall screen, which is also the familiar ~0.157 of the width at 16:9.
        /// </summary>
        private static float MinimapWidth()
        {
            var aspect = Aspect();
            if (aspect < 1.1f) aspect = 16f / 9f;

            return 0.2785f / aspect;
        }

        /// <summary>
        /// Where the minimap's LEFT edge is, asked of the game rather than worked out.
        ///
        /// THE PREVIOUS VERSION MODELLED THIS AND WAS WRONG. It assumed the map starts at the
        /// left edge of the screen and scaled a 16:9 right-edge figure by the aspect ratio,
        /// which put the icons at 0.117 -- and on a 3440x1440 screen 0.117 is where the map
        /// BEGINS, not where it ends. The icons landed on the far side of the minimap from the
        /// one they were asked for, over the health bar.
        ///
        /// The map does not start at zero: the HUD sits inside the safe zone, and how far in
        /// depends on the player's own safe-zone slider, which no formula here can know.
        /// GET_HUD_COMPONENT_POSITION reports the real anchor of component 13, HUD_MINIMAP,
        /// whatever the screen and whatever that slider says.
        ///
        /// Falls back to the safe-zone size if the native gives something implausible, and the
        /// whole thing is overridable from the ini and the F7 menu.
        /// </summary>
        private static float MinimapLeft()
        {
            try
            {
                var at = Function.Call<GTA.Math.Vector3>(Hash.GET_HUD_COMPONENT_POSITION, 13);

                // A minimap in the left half of the screen is the only sane answer. Anything
                // else means the native reported something this code should not build on.
                if (at.X > 0.0001f && at.X < 0.5f) return at.X;
            }
            catch
            {
                // Fall through to the safe-zone estimate.
            }

            return SafeZoneInset();
        }

        /// <summary>
        /// How far in from the screen edge the HUD sits, from the safe-zone size.
        ///
        /// GET_SAFE_ZONE_SIZE runs about 0.85 to 1.0. The inset is half of what is missing,
        /// measured in HEIGHT and converted to a fraction of width, because the safe zone is
        /// square in screen terms rather than proportional to the width.
        /// </summary>
        private static float SafeZoneInset()
        {
            try
            {
                var safe = Function.Call<float>(Hash.GET_SAFE_ZONE_SIZE);

                if (safe > 0.5f && safe <= 1f)
                {
                    var aspect = Aspect();
                    if (aspect < 1.1f) aspect = 16f / 9f;

                    return ((1f - safe) * 0.5f) * (1f / aspect) * 2f;
                }
            }
            catch
            {
                // Fall through.
            }

            return 0.0f;
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

                var mapL = MinimapLeft();
                var mapW = MinimapWidth();

                // The minimap's measured bounds go in the log alongside the icons' position.
                // This is the line that would have caught the icons landing on the wrong side
                // of the map in one reading, instead of needing a screenshot to notice.
                Log.Info("HUD: icons " + (side * res.Height).ToString("0") + " px square at " +
                         (x * res.Width).ToString("0") + "," + (y * res.Height).ToString("0") +
                         "  (screen " + res.Width + "x" + res.Height +
                         ", aspect " + Aspect().ToString("0.00") +
                         ", minimap " + (mapL * res.Width).ToString("0") + ".." +
                         ((mapL + mapW) * res.Width).ToString("0") + " px" +
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

                // Just clear of the minimap's RIGHT-hand edge: its left, plus its width.
                //
                // The width has to be added. The previous version used a single figure it
                // believed was the right edge and which was actually about where the map
                // BEGINS on a 21:9, so the icons sat on the far side of the minimap from the
                // one they were meant to be on.
                var x = _cfg.HudAutoPosition
                    ? MinimapLeft() + MinimapWidth() + wide * 0.45f
                    : _cfg.HudX;

                // Sat so the PAIR ends level with the foot of the minimap, which is where the
                // health and armour strips are -- the tidiest line to share.
                var bottom = _cfg.HudAutoPosition ? 0.955f : _cfg.HudY + side * 2f + gap;
                var top = bottom - (side * 2f + gap);

                Measure(x, top, side);

                // Offset phases so the two never move together. In step they read as one
                // object with two halves; a little apart they read as two things.
                if (_cfg.Style == HudStyle.Bars)
                {
                    Bars(needs, x, bottom);
                    return;
                }

                Mark(_apple, needs.Hunger, x, top + side / 2f, wide, side, 0f, false);
                Mark(_eye, needs.Sleep, x, top + side + gap + side / 2f, wide, side, 0.37f, true);
            }
            catch (Exception ex)
            {
                Log.Once("gauge", "The HUD could not be drawn: " + ex.Message);
            }
        }

        // ======================================================================
        // The bars
        // ======================================================================

        /// <summary>
        /// The other HUD: two upright bars, in the manner of the fuel gauge in Fumes.
        ///
        /// UPRIGHT BECAUSE THE FILL IS A LEVEL. A horizontal bar is a length and reads as a
        /// progress meter; a vertical one is a column of something with a surface on top, and
        /// a surface is what lets it slosh, settle and breathe. The animations below are the
        /// whole reason for this style existing, and half of them do not mean anything lying
        /// on their side.
        ///
        /// The stage icon sits above each bar. Without it two coloured columns beside the
        /// minimap are indistinguishable from each other, and the entire point of this mod's
        /// HUD is knowing at a glance which meter you are looking at.
        /// </summary>
        private void Bars(Needs.Needs needs, float x, float bottom)
        {
            var barW = Math.Max(0.001f, _cfg.HudBarWidth);
            var barH = Math.Max(0.004f, _cfg.HudBarLength);

            // ANCHORED TO THE FOOT, and to nothing else. It used to be reconstructed as
            // top + side * 2 + gap, which is the same number in auto position and is NOT in
            // manual -- so Gap, whose job here is the space BETWEEN the two bars, was also
            // sliding the pair up and down the screen. One thing, one effect.
            var barTop = bottom - barH;

            // GAP IS THE SPACE BETWEEN THEM. In the icon layout it is the vertical space
            // between two stacked pictures; upright and side by side, the same setting means
            // the horizontal one, which is what somebody reaching for "gap" while looking at
            // two bars is reaching for.
            //
            // A MINIMUM under it, because at the fuel gauge's width -- nine pixels on a 1080p
            // screen -- a gap of nothing leaves two bars touching, and two bars touching read
            // as one bar with a line down it.
            var pitch = barW + Math.Max(barW * 1.6f, _cfg.HudGap * barW * 2.4f);

            Column(needs.Hunger, _apple, x + barW / 2f, barTop, barH, barW, false);
            Column(needs.Sleep, _eye, x + barW / 2f + pitch, barTop, barH, barW, true);
        }

        /// <summary>One upright bar: the mark above it, the channel, and the level inside.</summary>
        private void Column(Need need, Icon[] set, float centreX, float top, float h,
                            float w, bool sleep)
        {
            if (_cfg.HudHideWhenFine && need.Value > _cfg.HudFineAbove) return;

            var body = Colour(need, sleep);
            var icon = set[Stage(need)];

            var x = centreX - w / 2f;

            // THE FUEL GAUGE'S OWN NUMBERS, alphas included. A surround at 205 over a channel
            // at 165, and an edge of 0.22 of the bar's width with a floor under it -- a
            // proportional edge alone becomes a hairline on a narrow bar, and a fixed one
            // becomes a frame thicker than the gauge. Both of those were argued out in Fumes
            // and there is no reason to argue them again here.
            var edge = w * 0.22f;
            if (edge < 0.0005f) edge = 0.0005f;

            Hud.Bar(x - edge, top - edge * Aspect(), w + edge * 2f, h + edge * 2f * Aspect(),
                    Fade(Color.FromArgb(205, 0, 0, 0)));

            Hud.Bar(x, top, w, h, Fade(Color.FromArgb(165, 28, 28, 32)));

            var fraction = Clamp01(need.Value);
            if (fraction <= 0.002f) return;

            if (!_cfg.HudAnimate)
            {
                var flat = h * fraction;
                Hud.Bar(x, top + h - flat, w, flat, body);
                return;
            }

            if (sleep) Breathe(x, top, w, h, fraction, body);
            else Churn(x, top, w, h, fraction, body);

            Foot(icon, centreX, top, w, h, fraction);
        }

        /// <summary>
        /// The stage mark, standing in the bottom of the bar.
        ///
        /// INSIDE IT, the way the pump sits in the fuel gauge in Fumes -- which is the right
        /// place for it and not only because it matches. Above the bar it was a third object
        /// floating near the minimap with a gap to explain; in the foot it is part of the
        /// instrument, and the two gauges take up less room for it.
        ///
        /// THE INK FLIPS WHEN THE LEVEL COVERS IT. Black on the fill, white on the empty
        /// channel -- a mark drawn in one colour is invisible for half of the range it is
        /// there to label. Straight from the fuel gauge, where it was learnt on a mark that
        /// spent every empty tank unreadable.
        ///
        /// Drawn LAST, over the level, so the fill does not paint across it.
        /// </summary>
        private void Foot(Icon icon, float centreX, float top, float w, float h, float fraction)
        {
            if (icon == null || icon.Missing) return;

            // Square on screen. A share of the bar's width, so it sits in the channel.
            var iconW = w * Math.Max(0.2f, _cfg.HudBarIconScale);
            var iconH = iconW * Aspect();

            var inset = w * 0.16f;

            var covered = h * fraction >= iconH + inset;

            icon.DrawSized(centreX, top + h - inset - iconH / 2f, iconW, iconH,
                           Fade(covered ? Color.FromArgb(245, 10, 10, 12)
                                        : Color.FromArgb(225, 232, 232, 238)));
        }

        /// <summary>
        /// How many slices the filled part is drawn in.
        ///
        /// THE FILL IS NOT ONE RECTANGLE, which is the difference between a bar with an
        /// animated surface and a bar that is animated. Fumes fills its body in a single
        /// draw because its body genuinely is one flat colour; here the body itself carries
        /// the movement, so it is built from bands and each band gets its own tint.
        ///
        /// EXACTLY TILING. One band's bottom edge is computed from the same expression as the
        /// next one's top, so no pixel is ever covered twice -- these are alpha colours, and
        /// two of them overlapping is a bright seam. That lesson came from the fuel gauge and
        /// there is no reason to learn it twice.
        /// </summary>
        private const int Bands = 22;

        /// <summary>
        /// HUNGER: a restless surface, and the contents turning over underneath it.
        ///
        /// A STOMACH, NOT A TANK. Fumes draws a liquid because a fuel tank holds one. This
        /// borrows the method and changes what it means: the surface is unsettled, and inside
        /// the fill a band travels UP and wraps -- which is what a gut does, and reads as
        /// "working on it" without a word.
        ///
        /// AND IT GETS WORSE AS IT EMPTIES, the opposite of the fuel gauge. A tank sloshes as
        /// you fill it and settles when full; it is the EMPTY stomach that rumbles. So every
        /// amplitude here is scaled by how little is left.
        /// </summary>
        private void Churn(float x, float y, float w, float h, float fraction, Color body)
        {
            var t = (Environment.TickCount & int.MaxValue) / 1000f;

            var level = h * fraction;
            var surfaceY = y + h - level;

            var empty = 1f - fraction;
            var swing = h * 0.014f * (0.20f + empty);

            var floor = y + h;

            // The body starts at the lowest the surface can swing, so the columns above it
            // have nothing to overlap.
            var bodyTop = surfaceY + swing;
            if (bodyTop > floor) bodyTop = floor;

            // ---- the contents, in bands, each one its own colour ----
            //
            // Two travelling pulses at different speeds and lengths. One alone reads as a
            // stripe going round; two crossing read as something turning over.
            for (var i = 0; i < Bands; i++)
            {
                var bTop = bodyTop + (floor - bodyTop) * i / Bands;
                var bBot = bodyTop + (floor - bodyTop) * (i + 1) / Bands;

                if (bBot - bTop <= 0f) continue;

                // 0 at the surface, 1 at the bottom of the glass.
                var u = (i + 0.5f) / Bands;

                var a = Pulse(u - t * 0.21f, 0.30f);
                var b = Pulse(u - t * 0.13f + 0.5f, 0.44f);

                var lift = (a * 0.65f + b * 0.35f) * (0.10f + 0.16f * empty);

                Hud.Bar(x, bTop, w, bBot - bTop,
                        Mix(body, Color.FromArgb(body.A, 255, 245, 220), lift));
            }

            if (level <= 0.002f) return;

            // ---- the surface: A ROLLING SWELL, NOT THE FUEL GAUGE'S CHOP ----
            //
            // Fumes adds two sines at frequencies that do not divide into each other, which
            // is right for petrol: it gives a restless, broken surface that never repeats,
            // and fuel in a moving tank IS broken water.
            //
            // Food is not petrol and should not move like it. This is ONE long wave with a
            // wavelength longer than the bar is wide, travelling steadily across -- so at any
            // instant the surface is a single smooth arc tilting one way, and over time it
            // rolls. No interference, no chop, nothing that can beat against itself.
            //
            // Under it, the whole surface rises and falls slowly, which is the part that
            // makes it look like a quantity settling rather than a texture playing.
            var crest = Mix(body, Color.FromArgb(body.A, 255, 240, 205), 0.55f);

            var columns = Columns(w);

            // Slow, and slower still when there is plenty. A full stomach is a quiet one.
            var roll = t * (0.55f + 0.85f * empty);
            var breath = (float)Math.Sin(t * 0.62f) * swing * 0.30f;

            for (var i = 0; i < columns; i++)
            {
                var left = x + w * i / columns;
                var right = x + w * (i + 1) / columns;

                // Centre of the column, not its edge: sampling at the edge puts the wave half
                // a column out of step with the rectangle that is drawing it, which is a
                // visible stagger once the columns are thin.
                var u = (i + 0.5f) / columns;

                // Just over one wavelength across the bar. Any more and it is chop again.
                var wave = (float)Math.Sin((roll + u * 1.15f) * Math.PI * 2.0) * swing + breath;

                var topY = surfaceY + wave;
                if (topY < y) topY = y;

                if (topY < bodyTop) Hud.Bar(left, topY, right - left, bodyTop - topY, body);

                Hud.Bar(left, topY, right - left, h * 0.008f, crest);
            }
        }

        /// <summary>
        /// How many columns to draw a surface in, for a bar this wide.
        ///
        /// SMOOTHNESS IS RESOLUTION. A wave drawn in eight columns is eight steps whatever
        /// the maths behind it says, and on a bar thirty pixels wide those steps are four
        /// pixels each and plainly visible. One column per two pixels is under what anybody
        /// can pick out.
        ///
        /// Bounded at both ends: sub-pixel rectangles are a lottery with the rasteriser, and
        /// forty of them on a bar nobody can see is work for nothing.
        /// </summary>
        private int Columns(float w)
        {
            if (_screenW <= 0)
            {
                try { _screenW = GTA.UI.Screen.Resolution.Width; }
                catch { _screenW = 1920; }
            }

            var px = w * _screenW;
            var n = (int)(px / 2f);

            if (n < 8) n = 8;
            if (n > 40) n = 40;

            return n;
        }

        /// <summary>Cached, because the resolution cannot change without a reload anyway.</summary>
        private int _screenW;

        /// <summary>
        /// SLEEP: the whole column breathing, in and out.
        ///
        /// NOT A LIQUID, deliberately. Two bars side by side doing the same slosh is one
        /// animation drawn twice, and the reason for having two is that they are different
        /// things. Hunger turns over; sleep swells and fades as a whole, on the one rhythm
        /// everybody reads without being told what it is.
        ///
        /// THE BREATH SLOWS AND DEEPENS AS IT EMPTIES -- a shallow four-second cycle rested,
        /// a long heavy seven-second one exhausted -- so the animation carries the state
        /// rather than decorating it. Inside the fill a soft wash rises with the breath and
        /// falls back with it, which is what stops a breathing bar being a dimmer switch.
        /// </summary>
        private void Breathe(float x, float y, float w, float h, float fraction, Color body)
        {
            var tired = 1f - fraction;

            var period = 4000f + 3000f * tired;
            var now = (Environment.TickCount & int.MaxValue) % (int)period;

            // Not a plain sine: a breath draws in quicker than it lets out. Raising the phase
            // to a power before the sine leans the curve without a second function.
            var phase = now / period;
            var eased = (float)Math.Sin(Math.Pow(phase, 0.78) * Math.PI * 2.0);

            var depth = 0.06f + 0.16f * tired;

            var level = h * fraction;

            // The level rises and falls very slightly with the breath. Alone it is too small
            // to see; with the wash below it, it is the difference between a bar that is
            // breathing and one that is being dimmed.
            var swell = level * (1f + depth * 0.10f * eased);
            if (swell > h) swell = h;

            var surfaceY = y + h - swell;
            var floor = y + h;

            // ---- the contents, in bands ----
            for (var i = 0; i < Bands; i++)
            {
                var bTop = surfaceY + (floor - surfaceY) * i / Bands;
                var bBot = surfaceY + (floor - surfaceY) * (i + 1) / Bands;

                if (bBot - bTop <= 0f) continue;

                var u = (i + 0.5f) / Bands;

                // A broad, soft band centred where the breath has got to -- high in the glass
                // at the top of the inhale, sunk to the bottom at the end of the let-out.
                var centre = 0.5f - 0.42f * eased;

                var wash = Pulse(u - centre + 0.5f, 0.62f);

                var lift = depth * (0.35f + 0.65f * wash) * (0.5f + 0.5f * eased);

                Hud.Bar(x, bTop, w, bBot - bTop,
                        Mix(body, Color.FromArgb(body.A, 255, 250, 238), Math.Max(0f, lift)));
            }

            if (swell <= 0.002f) return;

            // A brighter skin on the surface, strongest at the top of the breath. It is the
            // only part clearly moving when the meter is nearly full.
            var cap = Mix(body, Color.FromArgb(body.A, 255, 248, 232), 0.30f + 0.35f * eased);

            Hud.Bar(x, surfaceY, w, h * 0.010f, cap);
        }

        /// <summary>
        /// A soft repeating hump: 0 away from the centre of a band, 1 at it, wrapping at 1.
        ///
        /// Wrapped rather than clamped, so a pulse travelling off the top of the bar arrives
        /// back at the bottom instead of stopping -- which is what makes the contents look
        /// like they are going round rather than draining away.
        /// </summary>
        private static float Pulse(float u, float width)
        {
            u -= (float)Math.Floor(u);

            var d = Math.Abs(u - 0.5f) * 2f;
            if (d >= width) return 0f;

            // Cosine rather than a triangle: a linear falloff has a visible corner at the peak
            // and reads as a chevron rather than as a swell.
            return 0.5f + 0.5f * (float)Math.Cos(d / width * Math.PI);
        }

        /// <summary>The colour at the HUD's opacity, for parts that do not go through Colour.</summary>
        private Color Fade(Color c)
        {
            var a = (int)(c.A * _cfg.HudOpacity + 0.5f);

            if (a < 0) a = 0;
            if (a > 255) a = 255;

            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        /// <summary>
        /// One need's icon: the picture, the colour, and whatever small movement it has.
        ///
        /// TWO ANIMATIONS, AND THEY DO DIFFERENT JOBS.
        ///
        /// The SHIMMER runs the whole time and is meant to be almost invisible -- a slow lift
        /// and fall in brightness, four seconds a cycle, a few per cent either way. It is not
        /// there to tell you anything. It is there so the icon looks lit rather than printed,
        /// because a completely static sprite next to the game's own animated HUD reads as an
        /// overlay that has got stuck.
        ///
        /// The BOB is the opposite: it only exists on the LAST stage, and there it is meant to
        /// be caught out of the corner of your eye. By then the icon has run out of silhouette
        /// to spend -- the apple is a core and the eye is shut, and neither can get any worse
        /// looking -- so movement is the only channel left to say it is still getting worse.
        ///
        /// <paramref name="offset"/> shifts a mark's phase so the two never move in step. In
        /// step they read as one object with two halves rather than two separate things.
        /// </summary>
        private void Mark(Icon[] set, Need need, float centreX, float centreY,
                          float wide, float tall, float offset, bool sleep)
        {
            if (_cfg.HudHideWhenFine && need.Value > _cfg.HudFineAbove) return;

            var stage = Stage(need);

            var icon = set[stage];
            if (icon == null || icon.Missing) return;

            var colour = Colour(need, sleep);

            var y = centreY;
            var turn = 0f;
            var grow = 1f;

            if (_cfg.HudAnimate)
            {
                colour = Shimmer(colour, need, offset);

                // A BREATH IN SIZE AS WELL AS BRIGHTNESS. Brightness alone is the weakest
                // signal on a small sprite over a moving world -- especially on the green
                // end of the ramp, which is already bright -- and it was reported invisible.
                // A couple of per cent of scale is what the eye actually picks up.
                grow = 1f + 0.030f * Clamp01(_cfg.HudShimmer) * Sheen.Wave(offset);

                // THE BOTTOM TWO STAGES, not just the last. Waiting for a meter to bottom out
                // completely means most players never see this at all, and the point of it is
                // to be seen before things are already as bad as they get. Stage 1 gets half
                // of it, so there is a build rather than a switch being thrown.
                var lean = stage == 0 ? 1f : stage == 1 ? 0.5f : 0f;

                if (lean > 0f)
                {
                    var strength = lean * Clamp01(_cfg.HudSway);

                    // A drift up and down, and a tilt that lags a third of a cycle behind it.
                    // The lag is what makes it a sway rather than a rocking horse: the two
                    // moving together is a rigid thing being waggled.
                    y += tall * 0.095f * strength * Wave(SwayMs, offset);
                    turn = 9f * strength * Wave(SwayMs, offset + 0.33f);
                }
            }

            icon.DrawSized(centreX + wide / 2f, y, wide * grow, tall * grow, colour, turn);
        }

        /// <summary>
        /// The lit look: a few per cent of brightness, breathing.
        ///
        /// DELIBERATELY UNDER THE THRESHOLD OF NOTICE. Anything you can consciously watch
        /// happening in the corner of the screen is a distraction for the rest of the session,
        /// and this icon is on screen the entire time.
        ///
        /// It stands aside for the critical flash. That flash is a real signal and is already
        /// swinging the colour hard; a second, slower modulation underneath it just makes the
        /// beat wander.
        /// </summary>
        private Color Shimmer(Color c, Need need, float offset)
        {
            if (_cfg.HudFlashWhenCritical && need.Stage == 0) return c;

            // SHARED WITH THE MENUS. The shop rows and the title marks breathe on the same
            // curve at the same rate, and two copies of a sine would come apart the first
            // time either was tuned.
            return Sheen.On(c, offset, 0.22f * Clamp01(_cfg.HudShimmer));
        }

        /// <summary>How long one sway takes. Faster than the shimmer, because it is a warning.</summary>
        private const float SwayMs = 1600f;

        private static int Stage(Need need)
        {
            var stage = need.Stage;
            if (stage < 0) stage = 0;
            if (stage > 4) stage = 4;
            return stage;
        }

        /// <summary>
        /// A sine from -1 to 1 over <paramref name="periodMs"/>, shifted by a fraction.
        ///
        /// ON THE WALL CLOCK, like the colour pulse, so it runs at the same rate whatever the
        /// framerate is doing -- a frame counter races on a fast machine and crawls on a slow
        /// one. TickCount is masked positive because it wraps to negative after about
        /// twenty-five days of uptime.
        /// </summary>
        private static float Wave(float periodMs, float offset)
        {
            if (periodMs <= 1f) return 0f;

            var now = (Environment.TickCount & int.MaxValue) % (int)periodMs;
            var t = now / periodMs + offset;

            return (float)Math.Sin(t * 2.0 * Math.PI);
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }

        /// <summary>
        /// The icon's colour: the ramp, at the HUD's opacity, pulsing when critical.
        ///
        /// The pulse is on WALL CLOCK rather than a frame counter, so it beats at the same
        /// rate whatever the framerate is doing -- a counter makes it race on a fast machine
        /// and crawl on a slow one.
        /// </summary>
        private Color Colour(Need need, bool sleep = false)
        {
            var c = OnRamp(need.Value, sleep ? SleepRamp : Ramp);

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

        // SLEEP GETS ITS OWN RAMP: blue awake, dark purple exhausted.
        //
        // Sharing the food ramp made a rested character green and a starving one red, and
        // that is the same sentence twice -- two meters saying "bad" in identical colours is
        // two meters you have to read the shape of to tell apart. Blue is awake and night is
        // purple, and neither is in the hunger ramp anywhere, so a glance at the colour alone
        // now says WHICH meter as well as how it is doing.
        //
        // It also darkens as it goes, where the food ramp stays bright the whole way. A
        // tired icon should be a dim one; a hungry one should not.
        private static readonly Color[] SleepRamp =
        {
            Color.FromArgb(235,  74,  40, 104),   // empty     dark purple
            Color.FromArgb(235, 102,  62, 148),   // bad       purple
            Color.FromArgb(235, 124, 100, 200),   // middling  violet
            Color.FromArgb(235, 110, 146, 228),   // fine      blue-violet
            Color.FromArgb(235,  96, 178, 246)    // full      awake blue
        };

        private static Color OnRamp(float t)
        {
            return OnRamp(t, Ramp);
        }

        private static Color OnRamp(float t, Color[] ramp)
        {
            if (t <= Stops[0]) return ramp[0];
            if (t >= Stops[Stops.Length - 1]) return ramp[ramp.Length - 1];

            for (var i = 1; i < Stops.Length; i++)
            {
                if (t > Stops[i]) continue;

                var span = Stops[i] - Stops[i - 1];
                var u = span <= 0.0001f ? 0f : (t - Stops[i - 1]) / span;

                return Mix(ramp[i - 1], ramp[i], u);
            }

            return ramp[ramp.Length - 1];
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
