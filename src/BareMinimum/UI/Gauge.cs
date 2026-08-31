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
        /// Five phases of moon, full down to a thin crescent.
        ///
        /// IT WAS AN EYE THAT CLOSED. The moon says the same thing with none of the trouble:
        /// an eye has to stay an eye at every width, which is why it needed a lid modelled as
        /// two half-ellipses and an iris that shrank to match, and it still only ever meant
        /// sleep by convention. A moon means night to everybody and comes with its own way of
        /// running down.
        /// </summary>
        private readonly Icon[] _moon = new Icon[5];

        /// <summary>
        /// The same two marks WITHOUT their black rim, for standing in the foot of a bar.
        ///
        /// The bars draw their mark as a flat silhouette the way the fuel gauge in Fumes
        /// draws its pump -- near-black over the fill, near-white over the empty channel --
        /// and that needs art with no outline of its own. CustomSprite MULTIPLIES, so a black
        /// rim stays black whatever ink it is given: a black silhouette comes out as a shape
        /// inside a halo, and a white one gets cut up by it.
        ///
        /// The HUD icons keep their rim, because they sit on the world and need it.
        /// </summary>
        private readonly Icon[] _appleFlat = new Icon[5];
        private readonly Icon[] _moonFlat = new Icon[5];

        private bool _measured;

        public Gauge(Settings cfg)
        {
            _cfg = cfg;

            for (var i = 0; i < 5; i++)
            {
                _apple[i] = new Icon("apple" + i + ".png");
                _moon[i] = new Icon("moon" + i + ".png");

                _appleFlat[i] = new Icon("apple" + i + "_flat.png");
                _moonFlat[i] = new Icon("moon" + i + "_flat.png");
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
                Mark(_moon, needs.Sleep, x, top + side + gap + side / 2f, wide, side, 0.37f, true);
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
            // THE FLOOR HAS TO BE BELOW THE DEFAULT OR THE SETTING DOES NOTHING. It was
            // max(barW * 1.6, gap * barW * 2.4), and Gap defaults to 0.22 -- which comes to
            // 0.53 and loses to the floor, so the number moved and the bars did not until you
            // got past 0.67. A floor is meant to stop two frames overlapping, not to sit on
            // top of the value it is guarding.
            //
            // 0.45 is exactly two edges wide, so at Gap = 0 the black surrounds touch and
            // nothing overlaps. Everything above that is the setting doing its job.
            var pitch = barW * (1f + Math.Max(0.45f, _cfg.HudGap * 2.4f));

            Column(needs.Hunger, _apple, x + barW / 2f, barTop, barH, barW, false);
            Column(needs.Sleep, _moon, x + barW / 2f + pitch, barTop, barH, barW, true);
        }

        /// <summary>One upright bar: the mark above it, the channel, and the level inside.</summary>
        private void Column(Need need, Icon[] set, float centreX, float top, float h,
                            float w, bool sleep)
        {
            if (_cfg.HudHideWhenFine && need.Value > _cfg.HudFineAbove) return;

            var body = Colour(need, sleep);

            var flat = (sleep ? _moonFlat : _appleFlat)[Stage(need)];

            // Falls back to the outlined art if the flat copy did not deploy. A mark with a
            // rim on it is a great deal better than no mark at all.
            if (flat == null || flat.Missing) flat = set[Stage(need)];

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
                var still = h * fraction;
                Hud.Bar(x, top + h - still, w, still, body);
                return;
            }

            if (sleep) Night(x, top, w, h, fraction, body);
            else Churn(x, top, w, h, fraction, body);

            Foot(flat, centreX, top, w, h, fraction);
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

            // THE INK FLIPS WITH THE LEVEL, exactly as the pump does in Fumes: near-black
            // where the fill is behind it, near-white where the empty channel is. A mark
            // drawn in one colour is invisible for half the range it exists to label, and
            // that was learnt over there on a pump that spent every empty tank unreadable.
            //
            // It works here only because the art handed in has no rim of its own -- see
            // _appleFlat. With one, the black pass would be a shape inside a halo.
            var covered = h * fraction >= iconH + inset;

            icon.DrawSized(centreX, top + h - inset - iconH / 2f, iconW, iconH,
                           Fade(covered ? Color.FromArgb(240, 10, 10, 12)
                                        : Color.FromArgb(225, 235, 235, 240)));
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
        private int Bands(float h)
        {
            if (_screenH <= 0)
            {
                try { _screenH = GTA.UI.Screen.Resolution.Height; }
                catch { _screenH = 1080; }
            }

            var n = (int)(h * _screenH / 4f);

            if (n < 12) n = 12;
            if (n > 64) n = 64;

            return n;
        }

        /// <summary>Cached; the resolution cannot change without a reload anyway.</summary>
        private int _screenH;

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
            var t = Clock();

            var level = h * fraction;
            var surfaceY = y + h - level;

            var empty = 1f - fraction;

            // HOW FAR THE SURFACE TRAVELS. 0.007 of the bar's height was the number that
            // came out of killing the jitter, and it was an overcorrection: on a bar two
            // hundred and thirty-five pixels tall that is under two pixels, which is
            // certainly smooth and very nearly invisible.
            //
            // The jitter was never the amplitude anyway -- it was a wave rolling ACROSS
            // fifteen pixels of width. Now that the surface swells and tips as a whole
            // instead, it can travel a proper distance and stay perfectly smooth, because
            // a straight line moving slowly is smooth however far it moves.
            // AMPLITUDE, NOT ONLY PERIOD. Six rounds of "still too fast" have all been
            // answered by halving the clock, and that has clearly not been the whole story: a
            // crest travelling five pixels reads as quick even when it takes a minute to do
            // it, because what the eye measures is how far the line has jumped since it last
            // looked. Cutting the distance calms it as much as cutting the rate.
            var swing = h * 0.018f * Clamp01(_cfg.HudBarWave) * (0.35f + 0.65f * empty);

            var floor = y + h;

            // Below the lowest the surface can reach. The bow and the drift can push down
            // together, so the body has to start under BOTH or the columns above it draw over
            // ground the body has already covered -- which is a seam, in alpha, every frame.
            var bodyTop = surfaceY + swing * 1.45f;
            if (bodyTop > floor) bodyTop = floor;

            // ---- the contents ----
            //
            // VERY SLOW AND VERY BROAD. Two humps drifting up the column, one about a
            // fourteen-second lap and one about twenty-two, and both wide enough that any two
            // neighbouring bands differ by a hair. That is what keeps it a gradient sliding
            // rather than a set of stripes stepping past each other.
            var bands = Bands(h);

            for (var i = 0; i < bands; i++)
            {
                var bTop = bodyTop + (floor - bodyTop) * i / bands;
                var bBot = bodyTop + (floor - bodyTop) * (i + 1) / bands;

                if (bBot - bTop <= 0f) continue;

                var u = (i + 0.5f) / bands;

                // A SIX-MINUTE LAP AND A TEN. The contents are not travelling in any
                // sense you could time; they are a slow change of shade that happens to be
                // going somewhere. Which is the point -- this is the fifth halving, and the
                // brief the whole way has been that a bar beside the minimap should reward a
                // second look and do nothing at all to the first.
                var a = Pulse(u - t * 0.00138f, 0.58f);
                var b = Pulse(u - t * 0.00085f + 0.5f, 0.76f);

                var warm = (a * 0.6f + b * 0.4f) * (0.09f + 0.13f * empty);

                Hud.Bar(x, bTop, w, bBot - bTop,
                        Mix(body, Color.FromArgb(body.A, 255, 245, 220), warm));
            }

            if (level <= 0.002f) return;

            // ---- the surface ----
            //
            // NOT A TRAVELLING WAVE. That was the mistake: a wave rolling ACROSS a bar this
            // narrow is eight two-pixel columns taking turns to jump, which reads as
            // jittering however smooth the maths behind it is. There is simply not enough
            // width for a wavelength to live in.
            //
            // So the surface does the two things a settling liquid actually does in a narrow
            // glass: it rises and falls as a whole, and it tips. Both slow -- five and a half
            // seconds and seven and a bit, which do not divide into each other, so the pair
            // never quite repeats without either of them being fast.
            //
            // The tilt is LINEAR across the width, so the surface is a straight line leaning
            // one way. A straight line cannot have a kink in it, which is the other half of
            // why this is smooth where a sine sampled at eight points was not.
            // SIXTEEN SECONDS AND TWENTY-THREE. The swell is the slowest thing in either
            // bar now, which is right: it is a stomach settling, not a pulse.
            //
            // THE RATE STILL CLIMBS AS THE METER EMPTIES, and that is deliberately kept --
            // hunger drains faster while you sprint (see Needs.Exertion), so running yourself
            // hungry makes this visibly livelier. That link is worth more than the base speed
            // ever was and it is the one part of the timing nobody has asked to slow.
            // Still climbs as the meter empties. Hunger drains faster while you sprint
            // (Needs.Exertion), so running yourself hungry makes this visibly livelier --
            // the one part of the timing that is meant to move.
            var hurry = 1f + 0.85f * empty;

            // A MENISCUS THAT BREATHES, NOT A PLANE THAT TILTS.
            //
            // The tilt had to go. A straight surface leaning one way and then the other is
            // two hard diagonals swapping over, and on a bar fifteen pixels wide the
            // changeover is a corner rather than a curve -- smooth in the maths and angular
            // on the screen, which is exactly how it was reported.
            //
            // This bows instead: a parabola pinned at both walls and pushed up or down in the
            // middle, which is how liquid actually sits in something narrow and which cannot
            // have a corner in it at any amplitude. Under it the whole surface drifts a
            // little, on a period that does not divide into the bow's.
            //
            // Twenty-two seconds and thirty-one, and the bow is now the slowest thing in
            // either bar. It is a stomach settling, not a pulse.
            // A HUNDRED AND FORTY-FOUR SECONDS AND TWO HUNDRED AND EIGHT. Two and a half
            // minutes for one bow, on half the travel it had before.
            //
            // [HUD] BarPace lifts the whole instrument back up in one number if this has gone
            // past the point of being worth drawing at all, and [HUD] BarWave the distance.
            var bow = (float)Math.Sin(t * hurry * (2.0 * Math.PI / 144.0)) * swing;
            var lift = (float)Math.Sin(t * hurry * (2.0 * Math.PI / 208.0)) * swing * 0.40f;

            var crest = Mix(body, Color.FromArgb(body.A, 255, 240, 205), 0.55f);

            var columns = Columns(w);

            for (var i = 0; i < columns; i++)
            {
                var left = x + w * i / columns;
                var right = x + w * (i + 1) / columns;

                // -1 at one wall, +1 at the other.
                var across = ((i + 0.5f) / columns - 0.5f) * 2f;

                // 1 in the middle, 0 at both walls: the curve itself, and the reason there is
                // no seam anywhere along the surface.
                var curve = 1f - across * across;

                var topY = surfaceY + lift + bow * curve;

                if (topY < y) topY = y;
                if (topY > y + h - h * 0.007f) topY = y + h - h * 0.007f;

                if (topY < bodyTop) Hud.Bar(left, topY, right - left, bodyTop - topY, body);

                Hud.Bar(left, topY, right - left, h * 0.007f, crest);
            }

            Sediment(x, y, w, h, surfaceY + lift, t, empty);
        }

        /// <summary>
        /// FOOD: bits settling through it.
        ///
        /// FUMES SENDS BUBBLES UP; THIS SENDS CRUMBS DOWN, and the direction is the point.
        /// Gas rising through petrol is a tank; something heavier than what it is in, sinking
        /// slowly and drifting as it goes, is a stomach. It also runs AGAINST the churn, which
        /// travels upward -- so the two together read as contents turning over rather than as
        /// one thing scrolling.
        ///
        /// DETERMINISTIC, straight off the clock and the mote's own index: no state to keep
        /// between frames and no Random being pumped sixty times a second for three specks.
        /// That trick is the fuel gauge's and it is the only sane way to do particles here.
        /// </summary>
        private void Sediment(float x, float y, float w, float h, float surfaceY,
                              float t, float empty)
        {
            const int count = 3;

            var level = y + h - surfaceY;
            if (level < h * 0.10f) return;

            // Square ON SCREEN. A rectangle given equal width and height fractions is as wide
            // as the screen is wider than it is tall, which at three pixels reads as a dash.
            var size = w * 0.22f;
            var tall = size * Aspect();

            // A THIRD OF WHAT THIS WAS. Three specks crossing a bar in six seconds is
            // traffic; these are meant to be something you notice on the second look. At the
            // default that is most of a minute to sink the length of the bar.
            var drift = Clamp01(_cfg.HudBarDrift);
            if (drift <= 0.001f) return;

            for (var i = 0; i < count; i++)
            {
                // A little quicker when there is less to sink through. Speeds that do not
                // divide into each other, so the three never fall in formation.
                // Halved again: three minutes to sink the bar at the default drift. A
                // crumb suspended rather than a crumb falling.
                var speed = (0.006f + i * 0.0015f + empty * 0.0035f) * drift;
                var phase = (t * speed + i * 0.37f) % 1f;

                var py = surfaceY + level * phase;

                // A drift across as it falls, a different width and rate each. This is what
                // separates a crumb settling from a dot being lowered on a string.
                var lane = 0.30f + i * 0.20f;
                var sway = (float)Math.Sin(t * (0.5f + i * 0.13f) * drift + i * 2.1f) * 0.16f;

                var px = x + w * (lane + sway) - size / 2f;

                // Faded in off the surface and out at the floor, rather than appearing and
                // vanishing. The in is quicker than the out: something drops into view and
                // settles out of it.
                var edge = Math.Min(phase * 5f, Math.Min((1f - phase) * 3f, 1f));

                var alpha = (int)(120 * Math.Max(edge, 0f));
                if (alpha <= 4) continue;

                Hud.Bar(px, py, size, tall, Fade(Color.FromArgb(alpha, 60, 40, 24)));
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
        /// SLEEP: the sky in it, and a waterline you can actually read.
        ///
        /// THE LEVEL COMES FIRST AND EVERYTHING ELSE GETS OUT OF ITS WAY. The last version
        /// put a soft glow sixteen per cent of the bar deep under the surface, and a soft
        /// edge is exactly what you cannot take a reading off -- the boundary smeared into
        /// the fill and the honest answer was that you could not tell where the level was.
        /// That is the one job this whole object has.
        ///
        /// So the waterline is now a hard bright cap with a dark line above it, and the fill
        /// beneath is darkest right under that cap. A light edge against a dark edge is the
        /// most legible boundary there is, and it is legible at a glance rather than after a
        /// second's staring.
        ///
        /// The animation lives in the BRIGHTNESS of that cap and in the sky below it. Nothing
        /// moves, because travel is what this bar has failed at twice: in the corner of the
        /// eye, anything crossing the screen is the one thing that cannot be ignored.
        /// </summary>
        private void Night(float x, float y, float w, float h, float fraction, Color body)
        {
            var tired = 1f - fraction;

            var t = Clock();

            var floor = y + h;

            // Forty seconds a cycle, for the brightness of the cap.
            var pulse = 0.5f + 0.5f * (float)Math.Sin(t * (2.0 * Math.PI / 40.0));

            // A DROP LANDING, AND THE WATER SETTLING AFTER IT.
            //
            // On a bar fifteen pixels wide, the only surface movement anybody can actually see
            // is VERTICAL -- horizontal shape barely survives the width, which is what sank
            // the travelling wave and the tilt. So the difference between this bar and the
            // food bar cannot be the shape of the motion. It has to be the RHYTHM of it.
            //
            // Food breathes: one long sine, always going, never still. This is the opposite
            // shape of event -- nothing at all for most of the cycle, then a knock, then a
            // bob that dies away and leaves the surface flat again. A drip into a still pool.
            //
            // TWO THINGS FALL OUT OF THAT FOR FREE. The surface rests at exactly the true
            // level for most of every cycle, so the reading is not merely honest on average,
            // it is exact most of the time. And the rings below are no longer three unrelated
            // timers -- they leave from the moment of the knock, which is what a ring is.
            var beat = 13f;

            var strike = (t / beat) % 1f;

            // The bob: a couple of oscillations under an exponential decay, then flat. Fifteen
            // hundredths of the cycle to happen in, so the still is much longer than the
            // moving -- get that ratio wrong and it is a wobble rather than an event.
            var bob = 0f;

            if (strike < 0.42f)
            {
                var u = strike / 0.42f;

                bob = (float)(Math.Sin(u * Math.PI * 2.6) * Math.Exp(-u * 3.4));
            }

            // About three pixels at its worst on a bar two hundred and thirty-five tall, and
            // it is back to nothing within four seconds. Honest as well as visible.
            var level = h * fraction - bob * h * 0.013f;

            if (level < 0f) level = 0f;
            if (level > h) level = h;

            var surfaceY = y + h - level;

            // ---- the fill ----
            //
            // DARKEST AT THE TOP, brightening downward -- the opposite way round from before.
            // The old gradient was brightest at the surface, which put the lightest part of
            // the fill immediately under the lightest part of the bar and blurred the two
            // together. Dark under the cap is what makes the cap an edge.
            var bands = Bands(h);

            for (var i = 0; i < bands; i++)
            {
                var bTop = surfaceY + (floor - surfaceY) * i / bands;
                var bBot = surfaceY + (floor - surfaceY) * (i + 1) / bands;

                if (bBot - bTop <= 0f) continue;

                var u = (i + 0.5f) / bands;

                // A short, sharp shade under the cap and then flat: enough to separate the
                // two, not so much that the bar looks half empty.
                var shade = u < 0.14f ? 0.38f * (1f - u / 0.14f) : 0f;

                var deep = Mix(body, Color.FromArgb(body.A,
                                                    (int)(body.R * 0.45f),
                                                    (int)(body.G * 0.45f),
                                                    (int)(body.B * 0.55f)), shade);

                Hud.Bar(x, bTop, w, bBot - bTop, deep);
            }

            // ---- what is IN the sky ----
            //
            // STARS AT EVERY STAGE. They were held back to the last two for a while, on the
            // grounds that the icon showed a sun above those and stars in daylight are just
            // dots. The sun is gone; it is a crescent moon at every stage now, so it is night
            // in this meter the whole way down and the sky is never empty.
            if (level > h * 0.06f)
            {
                Stars(x, y, w, h, surfaceY, t, tired);
                Ripples(x, w, h, level, y + h - h * fraction, strike, body);
            }

            if (level <= 0.002f) return;

            // ---- the waterline ----
            //
            // A dark line ABOVE and a bright cap BELOW, drawn last so nothing can paint over
            // them. Two hard edges a pixel apart, which is a boundary you read rather than
            // one you estimate.
            //
            // The cap lifts toward a COOL white. It was a warm one, left over from when this
            // meter ran daylight at the top of its ramp -- and a warm highlight on a blue
            // fill is the one bit of gold that would have survived taking the sun out.
            var cap = h * 0.008f;

            Hud.Bar(x, surfaceY - cap * 0.6f, w, cap * 0.6f,
                    Fade(Color.FromArgb(210, 6, 6, 8)));

            Hud.Bar(x, surfaceY, w, cap,
                    Mix(body, Color.FromArgb(body.A, 244, 248, 255), 0.55f + 0.30f * pulse));
        }

        /// <summary>
        /// Rings spreading down from the waterline.
        ///
        /// THE LEVEL ANIMATION THAT DOES NOT TOUCH THE LEVEL. Everything else tried here moved
        /// the surface or softened it, and both of those cost the one reading this object
        /// exists to give. These start AT the surface and travel away from it, so the line
        /// they leave is exactly where the number says it is.
        ///
        /// A stone dropped on a still lake, which is the same picture as the moon and the
        /// stars above it. Each ring is born bright and thin at the waterline, spreads down a
        /// short way, and is gone well before the floor -- so the bottom of the bar stays as
        /// quiet as a sleeping meter should be.
        ///
        /// Three of them, on periods that do not divide into each other, so there is never a
        /// beat you could count. Deterministic off the clock and the index, like everything
        /// else that moves in these bars.
        /// </summary>
        private void Ripples(float x, float w, float h, float level, float surfaceY,
                             float strike, Color body)
        {
            const int count = 3;

            // Never more than a fifth of the bar, and never more than half of what is in it --
            // on a nearly empty meter a ring the size of the fill is a flash, not a ripple.
            var reach = Math.Min(h * 0.20f, level * 0.5f);
            if (reach < h * 0.02f) return;

            var thick = Math.Max(h * 0.005f, 0.0008f);

            for (var i = 0; i < count; i++)
            {
                // ALL THREE LEAVE FROM THE SAME KNOCK, a fifth of a cycle apart, rather than
                // running on three timers of their own. Rings that arrive unrelated to the
                // thing that made them are not rings, they are stripes -- and the surface
                // above is already carrying the event, so this only has to agree with it.
                var phase = strike - i * 0.13f;

                if (phase < 0f || phase > 1f) continue;

                // Eased out: quick away from the surface, slowing as it goes, the way a ring
                // on water actually travels. Linear reads as a scanner.
                var travel = 1f - (1f - phase) * (1f - phase);

                // From the RESTING surface, not the bobbing one: a ring is already in the
                // water and does not ride the splash that launched it.
                var ry = surfaceY + reach * travel;

                // Brightest as it leaves and gone by the end. Squared, so most of its life is
                // spent faint -- a ring that stays bright to the end is a bar, not a ripple.
                var fade = 1f - phase;
                fade = fade * fade;

                var alpha = (int)(150f * fade);
                if (alpha <= 5) continue;

                Hud.Bar(x, ry, w, thick,
                        Fade(Color.FromArgb(alpha, 226, 236, 255)));
            }
        }

        /// <summary>
        /// Stars coming out over the sky, each in a new place every time.
        ///
        /// THEY DO NOT MOVE AND THEY DO NOT STAY PUT EITHER. Nothing travels -- travel is what
        /// this bar has failed at twice, and in the corner of the eye anything crossing the
        /// screen is the one thing that cannot be ignored. But nine lamps blinking at nine
        /// fixed points is a switchboard, not a sky.
        ///
        /// So each star lives a cycle: up out of nothing, a moment lit, back down to nothing.
        /// Its POSITION comes from which cycle it is on, so the next time it appears it is
        /// somewhere else entirely. The jump happens while it is completely dark, so what you
        /// see is stars coming out over a sky -- never one sliding to a new seat.
        ///
        /// </summary>
        private void Stars(float x, float y, float w, float h, float surfaceY,
                           float t, float tired)
        {
            const int count = 9;

            var level = y + h - surfaceY;

            var size = Math.Max(w * 0.16f, 0.0008f);
            var tall = size * Aspect();

            for (var i = 0; i < count; i++)
            {
                // TWO SPEEDS, AND THE FAST ONE IS THE TWINKLE.
                //
                // A star used to have one: a nine-to-nineteen second rise and fall, which is
                // a slow pulse and reads as a lamp on a dimmer. Real twinkling is
                // scintillation -- a quick, irregular flicker on top of a star that is
                // otherwise just there.
                //
                // So the slow curve stays and now only decides WHETHER a star is out and how
                // strongly, and a fast pair of waves on top does the actual twinkling. The
                // life is shorter with it, four and a half to ten seconds, because a star that
                // scintillates for twenty is a fault light.
                var beat = 4.5f + (i % 5) * 1.3f;

                var raw = t / beat + i * 0.37f;

                var cycle = (float)Math.Floor(raw);
                var phase = raw - cycle;

                // A whole life in one half-sine: nothing at both ends, brightest in the
                // middle. Cubed, because a sky of nine half-lit lamps is a dotted line and
                // stars are mostly not there.
                var lit = (float)Math.Sin(phase * Math.PI);
                lit = lit * lit * lit;

                // THE TWINKLE. Two waves about a second apart in period and not harmonics of
                // each other, so no star settles into a rhythm you could tap along to. Held
                // above a third rather than allowed to reach nothing: a star that goes fully
                // out mid-life is not twinkling, it is a dead pixel.
                var fast = 0.5f + 0.5f * (float)Math.Sin(t * 7.3f + i * 2.1f);
                var slow = 0.5f + 0.5f * (float)Math.Sin(t * 4.6f - i * 1.7f);

                var twinkle = 0.34f + 0.66f * (fast * 0.62f + slow * 0.38f);

                var alpha = (int)(240f * lit * twinkle * (0.55f + 0.45f * tired));
                if (alpha <= 6) continue;

                // Kept off both walls and clear of the waterline, so no star is ever half a
                // star or sitting on the one line that has to stay readable.
                var lane = 0.20f + 0.60f * Scatter(i * 3.1f + cycle * 17.3f);
                var deep = 0.10f + 0.84f * Scatter(i * 7.7f + cycle * 29.1f + 5.5f);

                var sy = y + h - level * deep;

                Hud.Bar(x + w * lane - size / 2f, sy - tall / 2f, size, tall,
                        Fade(Color.FromArgb(alpha, 250, 250, 255)));
            }
        }

        /// <summary>
        /// A repeatable 0-to-1 from one number. The usual sine-and-throw-away-the-top trick.
        ///
        /// NOT called Hash, obvious as that name is: GTA.Native.Hash is the native enum this
        /// whole file calls through, and a private method by that name shadows it and breaks
        /// every Function.Call in the class.
        ///
        /// Not good randomness and does not need to be: it scatters nine dots in a box, and
        /// the only thing that matters is that the same input always gives the same dot and
        /// that neighbouring inputs do not give neighbouring dots.
        /// </summary>
        private static float Scatter(float v)
        {
            var x = Math.Sin(v * 12.9898) * 43758.5453;
            return (float)(x - Math.Floor(x));
        }

        /// <summary>
        /// The clock the bars run on: real seconds, scaled by the pace dial.
        ///
        /// EVERYTHING PERIODIC IN A BAR TAKES ITS TIME FROM HERE, so one number moves the
        /// swell, the tilt, the breath, the bands and the specks together and none of them
        /// can be left behind when the others are slowed.
        /// </summary>
        private float Clock()
        {
            return (Environment.TickCount & int.MaxValue) / 1000f * PaceOf();
        }

        private float PaceOf()
        {
            var pace = _cfg.HudBarPace;
            return pace < 0.05f ? 0.05f : pace;
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

        // SLEEP IS BLUE AWAKE, DEEP PURPLE EXHAUSTED. It ran sunlight-through-to-night for
        // a while, alongside a sun icon; both are gone and the ramp goes back with them --
        // there is no sun in this meter any more and nothing in it should be gold.
        //
        // It shares nothing with the hunger ramp, which is the point: a glance at colour
        // alone says WHICH meter as well as how it is doing, and neither of them has to be
        // read against the other.
        //
        // It also darkens as it goes, where the food ramp stays bright the whole way. A
        // tired icon should be a dim one; a hungry one should not.
        private static readonly Color[] SleepRamp =
        {
            Color.FromArgb(235,  74,  40, 104),   // empty     deep purple
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
