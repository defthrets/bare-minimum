using System;
using System.Drawing;
using BareMinimum.Core;
using BareMinimum.UI;
using Icon = BareMinimum.UI.Icon;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// The three bars, upright, in the row with the sleep and hunger bars.
    ///
    /// THE FRAME IS THE GAUGE'S, TO THE PIXEL. Every column in the row -- health, armour,
    /// energy, sleep, food -- is framed by the same two methods on Gauge: the black surround
    /// at alpha 205, the channel at 165, the plate under the foot the exact width of the
    /// surround with a white mark inside it. Not the same numbers copied; the same code. That
    /// is the only way five bars come out as one instrument rather than as two families that
    /// nearly agree, and it is what "the border and sizes should be identical" asked for.
    ///
    /// THE INSIDE IS THESE THREE BARS' OWN. It is the strip's liquid stood on end: gravity
    /// points down now, the surface is at the top of the fill, it bows across the width like
    /// a meniscus, it leans, drifts, and is thrown up and down by the same springs a hit or a
    /// hard stop kicks. Bubbles rise through health, glints climb the armour, sparks come and
    /// go in the energy. The sleep and hunger bars beside these move on their own, slower
    /// clock; the five are meant to be told apart by that as much as by the marks under them.
    ///
    /// Every edge is computed from one expression and its neighbour from the same one, so no
    /// pixel is covered twice -- these are alpha colours, and an overlap is a bright seam.
    /// </summary>
    internal sealed class Columns
    {
        private enum Kind
        {
            Health,
            Armour,
            Third
        }

        /// <summary>The marks under the bars. Held open; a CustomSprite keeps a texture handle.</summary>
        private readonly Icon _heart = new Icon("heart.png");
        private readonly Icon _shield = new Icon("shield.png");
        private readonly Icon _bolt = new Icon("bolt.png");

        /// <summary>Draws the three, in slots 0, 1 and 2 of the row -- the left of it.</summary>
        public void Draw(Settings cfg, Gauge gauge, Gauge.Row row, Readings r, Momentum m, float strength)
        {
            One(cfg, gauge, row, 0, r.Health, Paint.Health(cfg, row.Opacity, r, m, strength),
                m.Health, Kind.Health, _heart, m, r, strength);

            One(cfg, gauge, row, 1, r.Armour, Paint.Armour(cfg, row.Opacity, strength),
                m.Armour, Kind.Armour, _shield, m, r, strength);

            if (r.HasThird)
            {
                One(cfg, gauge, row, 2, r.Third, Paint.Third(cfg, row.Opacity, r, m, strength),
                    m.Third, Kind.Third, _bolt, m, r, strength);
            }
        }

        // ======================================================================
        // One column
        // ======================================================================

        private static void One(Settings cfg, Gauge gauge, Gauge.Row row, int slot, float fraction,
                                Color body, Momentum.Spring spring, Kind kind, Icon mark,
                                Momentum m, Readings r, float strength)
        {
            var aspect = Ink.Aspect;

            var w = row.BarW;
            var h = row.BarH;
            var centreX = row.Centre(slot);
            var x = centreX - w / 2f;
            var top = row.Top;

            // ---- the frame and the plate: the gauge's own, so the row is one row ----
            gauge.Frame(row, slot, strength);
            gauge.Plate(row, slot, mark, strength);

            fraction = Ink.Clamp01(fraction);
            if (fraction <= 0.002f) return;

            var level = h * fraction;
            var floor = top + h;

            if (!cfg.HudAnimate)
            {
                Ink.Bar(x, floor - level, w, level, body);
                return;
            }

            var t = m.Time;
            var phase = (int)kind * 0.37f;
            var wave = Ink.Clamp01(cfg.HudBarWave);

            // The bar's width as a HEIGHT fraction, for anything measured across the bar in
            // the same unit as along it.
            var thick = w * aspect;

            // ---- the idle motion, stood on end ----
            //
            // The drift is a share of the bar's LENGTH, the bow and the lean shares of its
            // WIDTH, as on the strip -- so the same numbers give the same feel turned
            // through ninety degrees. Three periods that do not divide into each other.
            var drift = (float)Math.Sin((t + phase * 5.3f) * (2.0 * Math.PI / 5.3)) * 0.020f * h * wave;
            var bow = (float)Math.Sin((t + phase * 3.7f) * (2.0 * Math.PI / 3.7)) * 0.26f * thick * wave;
            var tilt = (float)Math.Sin((t + phase * 7.1f) * (2.0 * Math.PI / 7.1)) * 0.20f * thick * wave;

            // ---- the slosh ----
            //
            // Positive is toward the surface, which up here means UP: a hit drops the level
            // and it rebounds; a hard stop throws it up the tube and it settles back.
            var thrown = spring.S * h;
            var speed = Ink.Clamp(spring.V * 1.8f, -1f, 1f);

            bow += speed * 0.55f * thick;
            tilt += speed * 0.35f * thick;

            var surface = Ink.Clamp(floor - level - drift - thrown, top, floor);

            // ---- the surface, as columns across the width ----
            var cols = Across(w);
            var colTop = new float[cols];
            var bodyTop = top;

            var crestH = Math.Max(1.6f / Ink.ScreenHeight, 0.10f * thick);

            for (var j = 0; j < cols; j++)
            {
                // -1 at the left wall, +1 at the right one; 1 in the middle, 0 at both walls.
                var across = ((j + 0.5f) / cols - 0.5f) * 2f;
                var curve = 1f - across * across;

                colTop[j] = Ink.Clamp(surface - bow * curve - tilt * across, top, floor);

                if (colTop[j] + crestH > bodyTop) bodyTop = colTop[j] + crestH;
            }

            if (bodyTop > floor) bodyTop = floor;

            var glossW = cfg.VitalsGloss > 0f ? w * 0.36f : 0f;
            var glossK = Ink.Clamp01(cfg.VitalsGloss) * 0.45f;
            var white = Color.FromArgb(body.A, 255, 252, 244);

            // ---- the body, as bands stacked from the surface to the floor ----
            //
            // A slow warmth drifting UP toward the surface: two broad humps on periods
            // nothing else uses, so the fill reads as something turning over rather than a
            // flat colour with a line across it.
            var inside = t / 7f + phase * 3f;
            var warmTo = Color.FromArgb(body.A, 255, 250, 235);

            var bodyH = floor - bodyTop;

            if (bodyH > 0f)
            {
                var bands = Bands(bodyH);

                for (var i = 0; i < bands; i++)
                {
                    var bTop = bodyTop + bodyH * i / bands;
                    var bBot = bodyTop + bodyH * (i + 1) / bands;
                    if (bBot - bTop <= 0f) continue;

                    // 1 at the surface, 0 at the floor: the same u the strip used along its
                    // length, so the humps travel toward the surface here too.
                    var u = 1f - (i + 0.5f) / bands;

                    var warm = (Paint.Pulse(u - inside * 0.060f, 0.55f) * 0.6f +
                                Paint.Pulse(u - inside * 0.041f + 0.5f, 0.75f) * 0.4f) * 0.16f;

                    var tint = Ink.Mix(body, warmTo, warm);

                    if (glossW > 0f)
                    {
                        Ink.Bar(x, bTop, glossW, bBot - bTop, Ink.Mix(tint, white, glossK));
                        Ink.Bar(x + glossW, bTop, w - glossW, bBot - bTop, tint);
                    }
                    else
                    {
                        Ink.Bar(x, bTop, w, bBot - bTop, tint);
                    }
                }
            }

            // ---- the surface ----
            var edgeWarm = (Paint.Pulse(1f - inside * 0.060f, 0.55f) * 0.6f +
                            Paint.Pulse(1f - inside * 0.041f + 0.5f, 0.75f) * 0.4f) * 0.16f;
            var edgeTint = Ink.Mix(body, warmTo, edgeWarm);
            var crest = Ink.Mix(body, Color.FromArgb(body.A, 255, 250, 240), 0.55f);

            for (var j = 0; j < cols; j++)
            {
                var cL = x + w * j / cols;
                var cR = x + w * (j + 1) / cols;

                var tint = cL - x < glossW - 0.00001f ? Ink.Mix(edgeTint, white, glossK) : edgeTint;

                var start = colTop[j] + crestH;
                if (bodyTop > start) Ink.Bar(cL, start, cR - cL, bodyTop - start, tint);

                var crestBot = Math.Min(floor, colTop[j] + crestH);
                if (crestBot > colTop[j]) Ink.Bar(cL, colTop[j], cR - cL, crestBot - colTop[j], crest);
            }

            // ---- what lives inside ----
            if (cfg.VitalsParticles <= 0.001f) return;

            switch (kind)
            {
                case Kind.Health: Bubbles(cfg, x, w, floor, surface, body, t, strength); break;
                case Kind.Armour: Glints(cfg, x, w, floor, surface, body, t, strength); break;
                default: Sparks(cfg, x, w, floor, surface, t, m.Wall, r.SpecialActive, strength); break;
            }
        }

        // ======================================================================
        // The specks
        // ======================================================================

        /// <summary>HEALTH: bubbles, rising to the surface, each in a new lane every trip.</summary>
        private static void Bubbles(Settings cfg, float x, float w, float floor, float surface,
                                    Color body, float t, float strength)
        {
            var count = (int)Math.Round(4f * cfg.VitalsParticles);
            if (count < 1) return;

            var sizeW = w * 0.26f;
            var size = sizeW * Ink.Aspect;

            var span = (floor - surface) - size * 1.5f;
            if (span <= size) return;

            var tint = Ink.Mix(body, Color.FromArgb(body.A, 255, 255, 255), 0.6f);

            for (var i = 0; i < count; i++)
            {
                var rate = 0.10f + i * 0.021f;

                var raw = t * rate + i * 0.37f;
                var cycle = (float)Math.Floor(raw);
                var at = raw - cycle;

                var lane = 0.15f + 0.70f * Paint.Scatter(i * 3.1f + cycle * 17.3f);
                var sway = (float)Math.Sin(t * (0.9f + i * 0.17f) + i * 2.1f) * 0.08f;

                var py = floor - size * 0.5f - span * at - size;
                var px = Ink.Clamp(x + w * (lane + sway) - sizeW / 2f, x, x + w - sizeW);

                var edge = Math.Min(at * 6f, Math.Min((1f - at) * 3f, 1f));

                var alpha = (int)(150f * edge * strength);
                if (alpha <= 4) continue;

                Ink.Bar(px, py, sizeW, size, Ink.Alpha(tint, alpha));
            }
        }

        /// <summary>ARMOUR: glints, climbing the left of the fill like light up a plate.</summary>
        private static void Glints(Settings cfg, float x, float w, float floor, float surface,
                                   Color body, float t, float strength)
        {
            var count = (int)Math.Round(3f * cfg.VitalsParticles);
            if (count < 1) return;

            var len = w * Ink.Aspect * 1.6f;
            var wide = w * 0.13f;

            var span = (floor - surface) - len;
            if (span <= len * 0.5f) return;

            var tint = Ink.Mix(body, Color.FromArgb(body.A, 255, 255, 255), 0.75f);

            for (var i = 0; i < count; i++)
            {
                var rate = 0.22f + i * 0.07f;

                var raw = t * rate + i * 0.41f;
                var at = raw - (float)Math.Floor(raw);

                var py = floor - len - span * at;
                var px = x + w * (0.20f + 0.16f * i);

                var edge = Math.Min(at * 4f, Math.Min((1f - at) * 4f, 1f));

                var alpha = (int)(120f * edge * strength);
                if (alpha <= 4) continue;

                Ink.Bar(px, py, wide, len, Ink.Alpha(tint, alpha));
            }
        }

        /// <summary>THE THIRD: sparks, coming up out of nothing somewhere in the fill and going out again.</summary>
        private static void Sparks(Settings cfg, float x, float w, float floor, float surface,
                                   float t, float wall, bool active, float strength)
        {
            var count = (int)Math.Round(5f * cfg.VitalsParticles);
            if (count < 1) return;

            var sizeW = w * 0.24f;
            var size = sizeW * Ink.Aspect;

            var span = (floor - surface) - size;
            if (span <= size) return;

            var hurry = active ? 2.2f : 1f;

            for (var i = 0; i < count; i++)
            {
                var beat = (2.6f + (i % 5) * 0.9f) / hurry;

                var raw = t / beat + i * 0.37f;
                var cycle = (float)Math.Floor(raw);
                var at = raw - cycle;

                var lit = (float)Math.Sin(at * Math.PI);
                lit = lit * lit * lit;

                var fast = 0.5f + 0.5f * (float)Math.Sin(wall * 7.3f + i * 2.1f);
                var slow = 0.5f + 0.5f * (float)Math.Sin(wall * 4.6f - i * 1.7f);
                var twinkle = 0.34f + 0.66f * (fast * 0.62f + slow * 0.38f);

                var alpha = (int)(240f * lit * twinkle * (active ? 1f : 0.75f) * strength);
                if (alpha <= 6) continue;

                var lane = 0.15f + 0.70f * Paint.Scatter(i * 3.1f + cycle * 17.3f);
                var deep = 0.06f + 0.88f * Paint.Scatter(i * 7.7f + cycle * 29.1f + 5.5f);

                var px = Ink.Clamp(x + w * lane - sizeW / 2f, x, x + w - sizeW);
                var py = floor - size - span * deep;

                Ink.Bar(px, py, sizeW, size, Color.FromArgb(alpha, 255, 250, 225));
            }
        }

        // ======================================================================
        // Arithmetic
        // ======================================================================

        /// <summary>
        /// How many columns the surface is drawn in across the bar: one per two pixels, 4 to
        /// 10. Sub-pixel rectangles are a lottery with the rasteriser, and a surface drawn in
        /// more strips than it has pixels boils -- that was the food bar's flicker.
        /// </summary>
        private static int Across(float w)
        {
            var n = (int)Math.Round(w * Ink.ScreenWidth / 2f);

            if (n < 4) n = 4;
            if (n > 10) n = 10;

            return n;
        }

        /// <summary>How many bands the body is drawn in: one per five pixels of height, 1 to 48.</summary>
        private static int Bands(float h)
        {
            var n = (int)(h * Ink.ScreenHeight / 5f);

            if (n < 1) n = 1;
            if (n > 48) n = 48;

            return n;
        }
    }
}
