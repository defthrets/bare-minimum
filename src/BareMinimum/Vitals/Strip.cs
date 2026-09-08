using System;
using System.Drawing;
using BareMinimum.Core;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// The three bars lying down, where the game's own strip was. The Strip style.
    ///
    /// A BAR IS A TUBE STOOD ON ITS LEFT END. That is the whole metaphor and everything below
    /// follows from it: the fill is a liquid, the level is its surface at the right-hand end,
    /// and gravity points left. So the surface is what moves -- it bows like a meniscus, it
    /// leans, it drifts back and forth, and it is thrown about when the bar is kicked -- and
    /// the specks inside the fill rise toward it. Nothing waves along the top edge, because a
    /// tube's walls do not.
    ///
    /// The upright bars in Columns are the same liquid turned through ninety degrees, and
    /// the two are meant to stay the same: a number tuned in one belongs in the other.
    ///
    ///   - The fill is not one rectangle. It is bands with a slow tint drifting through them,
    ///     and rows at the surface so the surface can curve. EVERY EDGE IS COMPUTED FROM ONE
    ///     EXPRESSION AND ITS NEIGHBOUR FROM THE SAME ONE, so nothing overlaps: these are
    ///     alpha colours, and two of them on the same pixel is a bright seam.
    ///   - One row per two pixels and one band per five. Sub-pixel rectangles are a lottery
    ///     with the rasteriser and a surface drawn in more strips than it has pixels boils.
    ///   - The specks are deterministic, straight off the clock and their own index, so there
    ///     is no state to keep and no Random pumped sixty times a second.
    /// </summary>
    internal sealed class Strip
    {
        private enum Kind
        {
            Health,
            Armour,
            Third
        }

        public void Draw(Settings cfg, Layout lay, Readings r, Momentum m, float strength)
        {
            var opacity = cfg.VitalsStripOpacity;
            var channel = Ink.Alpha(cfg.VitalsChannel, (int)(cfg.VitalsChannel.A * opacity * strength + 0.5f));

            // One bar for health and armour, as in the upright row. See Columns.Draw.
            One(cfg, lay, lay.Health, r.Armour > 0.002f ? 1f : r.Health,
                Paint.Health(cfg, opacity, r, m, strength), channel,
                m.Health, Kind.Health, m, r, strength);

            if (lay.HasThird)
            {
                One(cfg, lay, lay.Third, r.Third, Paint.Third(cfg, opacity, r, m, strength), channel,
                    m.Third, Kind.Third, m, r, strength);
            }
        }

        // ======================================================================
        // One bar
        // ======================================================================

        private void One(Settings cfg, Layout lay, Layout.Segment seg, float fraction, Color body,
                         Color channel, Momentum.Spring spring, Kind kind, Momentum m, Readings r,
                         float strength)
        {
            var x0 = seg.Left;
            var w = seg.Width;
            var y0 = lay.Top;
            var h = lay.Thick;

            if (w <= 0f || h <= 0f) return;

            var aspect = Ink.Aspect;

            // The thickness as a WIDTH fraction, for anything that has to be square on screen
            // or measured across the bar in the same unit as along it.
            var thickW = h / aspect;

            Ink.Bar(x0, y0, w, h, channel);

            fraction = Ink.Clamp01(fraction);
            if (fraction <= 0.002f) return;

            var level = w * fraction;

            if (!cfg.HudAnimate)
            {
                Ink.Bar(x0, y0, level, h, body);
                return;
            }

            var t = m.Time;
            var phase = (int)kind * 0.37f;
            var wave = Ink.Clamp01(cfg.HudBarWave);

            // ---- the idle motion ----
            //
            // THREE PERIODS THAT DO NOT DIVIDE INTO EACH OTHER, so the surface never settles
            // into a beat you could tap along to. Sized to the bar: the drift is a share of
            // the bar's LENGTH, so the health bar drifts further in pixels than the armour
            // bar and both read as the same thing happening; the bow and the lean are shares
            // of the THICKNESS, because that is the span they bend across.
            // Each bar on its own tempo and about two thirds the travel -- see Columns, which
            // says why.
            var tempo = kind == Kind.Health ? 0.87f : kind == Kind.Armour ? 1f : 1.13f;
            var tt = t * tempo + phase * 11.7f;

            var drift = (float)Math.Sin(tt * (2.0 * Math.PI / 5.3)) * 0.004f * w * wave;
            var bow = (float)Math.Sin(tt * (2.0 * Math.PI / 3.7)) * 0.05f * thickW * wave;
            var tilt = (float)Math.Sin(tt * (2.0 * Math.PI / 7.1)) * 0.04f * thickW * wave;

            // ---- the slosh ----
            //
            // The spring throws the whole surface, and its SPEED bends it: liquid moving fast
            // toward a wall piles up against it, which is a bow in the direction of travel
            // and a lean behind it.
            var thrown = spring.S * w;
            var speed = Ink.Clamp(spring.V * 1.8f, -1f, 1f);

            bow += speed * 0.40f * thickW;
            tilt += speed * 0.25f * thickW;

            var surface = Ink.Clamp(x0 + level + drift + thrown, x0, x0 + w);

            // ---- the surface, as rows ----
            var rows = Rows(h);
            var right = new float[rows];
            var bodyRight = x0 + w;

            var crestW = Math.Max(1.6f / Ink.ScreenWidth, 0.10f * thickW);

            for (var j = 0; j < rows; j++)
            {
                // -1 at the top wall, +1 at the bottom one; 1 in the middle, 0 at both walls.
                var across = ((j + 0.5f) / rows - 0.5f) * 2f;
                var curve = 1f - across * across;

                right[j] = Ink.Clamp(surface + bow * curve + tilt * across, x0, x0 + w);

                if (right[j] - crestW < bodyRight) bodyRight = right[j] - crestW;
            }

            if (bodyRight < x0) bodyRight = x0;

            var glossH = cfg.VitalsGloss > 0f ? h * 0.36f : 0f;
            var glossK = Ink.Clamp01(cfg.VitalsGloss) * 0.45f;
            var white = Color.FromArgb(body.A, 255, 252, 244);

            // ---- the body, as bands ----
            //
            // A slow warmth drifting toward the surface: two broad humps on periods nothing
            // else uses, so the fill reads as something turning over rather than a flat
            // colour with a line at the end.
            var inside = t / 7f + phase * 3f;
            var warmTo = Color.FromArgb(body.A, 255, 250, 235);

            var bodyW = bodyRight - x0;

            if (bodyW > 0f)
            {
                var bands = Bands(bodyW);

                for (var i = 0; i < bands; i++)
                {
                    var bL = x0 + bodyW * i / bands;
                    var bR = x0 + bodyW * (i + 1) / bands;
                    if (bR - bL <= 0f) continue;

                    var u = (i + 0.5f) / bands;

                    var warm = (Paint.Pulse(u - inside * 0.060f, 0.55f) * 0.6f +
                                Paint.Pulse(u - inside * 0.041f + 0.5f, 0.75f) * 0.4f) * 0.16f;

                    var tint = Ink.Mix(body, warmTo, warm);

                    if (glossH > 0f)
                    {
                        Ink.Bar(bL, y0, bR - bL, glossH, Ink.Mix(tint, white, glossK));
                        Ink.Bar(bL, y0 + glossH, bR - bL, h - glossH, tint);
                    }
                    else
                    {
                        Ink.Bar(bL, y0, bR - bL, h, tint);
                    }
                }
            }

            // ---- the surface ----
            var edgeWarm = (Paint.Pulse(1f - inside * 0.060f, 0.55f) * 0.6f +
                            Paint.Pulse(1f - inside * 0.041f + 0.5f, 0.75f) * 0.4f) * 0.16f;
            var edgeTint = Ink.Mix(body, warmTo, edgeWarm);
            var crest = Ink.Mix(body, Color.FromArgb(body.A, 255, 250, 240), 0.55f);

            for (var j = 0; j < rows; j++)
            {
                var rTop = y0 + h * j / rows;
                var rBot = y0 + h * (j + 1) / rows;

                var tint = rTop - y0 < glossH - 0.00001f ? Ink.Mix(edgeTint, white, glossK) : edgeTint;

                var end = right[j] - crestW;
                if (end > bodyRight) Ink.Bar(bodyRight, rTop, end - bodyRight, rBot - rTop, tint);

                var crestL = Math.Max(x0, right[j] - crestW);
                if (right[j] > crestL) Ink.Bar(crestL, rTop, right[j] - crestL, rBot - rTop, crest);
            }

            // ---- what lives inside ----
            if (cfg.VitalsParticles <= 0.001f) return;

            switch (kind)
            {
                case Kind.Health: Bubbles(cfg, x0, y0, h, surface, body, t, strength); break;
                case Kind.Armour: Glints(cfg, x0, y0, h, surface, body, t, strength); break;
                default: Sparkles(cfg, x0, y0, h, surface, t, m.Wall, r.SpecialActive, strength); break;
            }
        }

        // ======================================================================
        // The specks
        // ======================================================================

        /// <summary>HEALTH: bubbles, rising toward the surface, each in a new lane every trip.</summary>
        private static void Bubbles(Settings cfg, float x0, float y0, float h, float surface,
                                    Color body, float t, float strength)
        {
            var count = (int)Math.Round(4f * cfg.VitalsParticles);
            if (count < 1) return;

            var size = h * 0.26f;
            var sizeW = size / Ink.Aspect;

            var span = (surface - x0) - sizeW * 1.5f;
            if (span <= sizeW) return;

            var tint = Ink.Mix(body, Color.FromArgb(body.A, 255, 255, 255), 0.6f);

            for (var i = 0; i < count; i++)
            {
                // Speeds that do not divide into each other, so no two travel together.
                var rate = 0.10f + i * 0.021f;

                var raw = t * rate + i * 0.37f;
                var cycle = (float)Math.Floor(raw);
                var at = raw - cycle;

                var lane = 0.15f + 0.70f * Paint.Scatter(i * 3.1f + cycle * 17.3f);
                var sway = (float)Math.Sin(t * (0.9f + i * 0.17f) + i * 2.1f) * 0.08f;

                var px = x0 + sizeW * 0.5f + span * at;
                var py = Ink.Clamp(y0 + h * (lane + sway) - size / 2f, y0, y0 + h - size);

                // In quicker than out: a bubble appears and is absorbed at the surface.
                var edge = Math.Min(at * 6f, Math.Min((1f - at) * 3f, 1f));

                var alpha = (int)(150f * edge * strength);
                if (alpha <= 4) continue;

                Ink.Bar(px, py, sizeW, size, Ink.Alpha(tint, alpha));
            }
        }

        /// <summary>ARMOUR: glints, sliding along the upper part of the fill like light down a plate.</summary>
        private static void Glints(Settings cfg, float x0, float y0, float h, float surface,
                                   Color body, float t, float strength)
        {
            var count = (int)Math.Round(3f * cfg.VitalsParticles);
            if (count < 1) return;

            var len = h * 1.6f / Ink.Aspect;
            var tall = h * 0.13f;

            var span = (surface - x0) - len;
            if (span <= len * 0.5f) return;

            var tint = Ink.Mix(body, Color.FromArgb(body.A, 255, 255, 255), 0.75f);

            for (var i = 0; i < count; i++)
            {
                var rate = 0.22f + i * 0.07f;

                var raw = t * rate + i * 0.41f;
                var at = raw - (float)Math.Floor(raw);

                var px = x0 + span * at;
                var py = y0 + h * (0.20f + 0.16f * i);

                var edge = Math.Min(at * 4f, Math.Min((1f - at) * 4f, 1f));

                var alpha = (int)(120f * edge * strength);
                if (alpha <= 4) continue;

                Ink.Bar(px, py, len, tall, Ink.Alpha(tint, alpha));
            }
        }

        /// <summary>THE THIRD: sparks, each coming up out of nothing somewhere in the fill and going out again.</summary>
        private static void Sparkles(Settings cfg, float x0, float y0, float h, float surface,
                                     float t, float wall, bool active, float strength)
        {
            var count = (int)Math.Round(5f * cfg.VitalsParticles);
            if (count < 1) return;

            var size = h * 0.24f;
            var sizeW = size / Ink.Aspect;

            var span = (surface - x0) - sizeW;
            if (span <= sizeW) return;

            var hurry = active ? 2.2f : 1f;

            for (var i = 0; i < count; i++)
            {
                var beat = (2.6f + (i % 5) * 0.9f) / hurry;

                var raw = t / beat + i * 0.37f;
                var cycle = (float)Math.Floor(raw);
                var at = raw - cycle;

                // A whole life in one half-sine, cubed, because a bar full of half-lit lamps
                // is a dotted line and sparks are mostly not there.
                var lit = (float)Math.Sin(at * Math.PI);
                lit = lit * lit * lit;

                // Two twinkle rates that are not harmonics of each other, and never fully out
                // mid-life: a spark that goes dark in the middle is a dead pixel.
                var fast = 0.5f + 0.5f * (float)Math.Sin(wall * 7.3f + i * 2.1f);
                var slow = 0.5f + 0.5f * (float)Math.Sin(wall * 4.6f - i * 1.7f);
                var twinkle = 0.34f + 0.66f * (fast * 0.62f + slow * 0.38f);

                var alpha = (int)(240f * lit * twinkle * (active ? 1f : 0.75f) * strength);
                if (alpha <= 6) continue;

                var lane = 0.15f + 0.70f * Paint.Scatter(i * 3.1f + cycle * 17.3f);
                var deep = 0.06f + 0.88f * Paint.Scatter(i * 7.7f + cycle * 29.1f + 5.5f);

                var px = x0 + span * deep;
                var py = Ink.Clamp(y0 + h * lane - size / 2f, y0, y0 + h - size);

                Ink.Bar(px, py, sizeW, size, Color.FromArgb(alpha, 255, 250, 225));
            }
        }

        // ======================================================================
        // Arithmetic
        // ======================================================================

        /// <summary>How many rows the surface is drawn in: one per two pixels of thickness, 4 to 10.</summary>
        private static int Rows(float h)
        {
            var n = (int)Math.Round(h * Ink.ScreenHeight / 2f);

            if (n < 4) n = 4;
            if (n > 10) n = 10;

            return n;
        }

        /// <summary>How many bands the body is drawn in: one per five pixels of length, 1 to 48.</summary>
        private static int Bands(float w)
        {
            var n = (int)(w * Ink.ScreenWidth / 5f);

            if (n < 1) n = 1;
            if (n > 48) n = 48;

            return n;
        }
    }
}
