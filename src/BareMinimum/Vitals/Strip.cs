using System;
using System.Drawing;
using GTA;
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
                case Kind.Health: Heartbeat(cfg, x0, y0, h, surface, body, spring, m.Wall, r.Health, strength); break;
                case Kind.Armour: Plating(cfg, x0, y0, h, surface, body, t, m.Wall, r, strength); break;
                default: Streaks(cfg, x0, y0, h, surface, body, m.Wall, r, strength); break;
            }
        }

        // ======================================================================
        // The specks
        // ======================================================================

        private float _beatPhase;
        private float _beatWall = -1f;
        private float _beatAt = -10f;
        private float _beatPeriod = 1f;
        private float _exertion = 1f;

        /// <summary>HEALTH: the heartbeat, lying down -- the fill flashes and the level is kicked on each beat. See Columns.Heartbeat.</summary>
        private void Heartbeat(Settings cfg, float x0, float y0, float h, float surface,
                               Color body, Momentum.Spring spring, float wall, float health, float strength)
        {
            if (cfg.VitalsParticles <= 0.001f) return;

            var dt = _beatWall < 0f ? 0f : Ink.Clamp(wall - _beatWall, 0f, 0.1f);
            _beatWall = wall;

            var want = 1f;
            try
            {
                var me = Game.Player.Character;
                if (me != null && me.Exists() && !me.IsInVehicle())
                {
                    if (me.IsSprinting) want = 1.65f;
                    else if (me.IsRunning) want = 1.3f;
                }
            }
            catch { want = 1f; }

            _exertion += (want - _exertion) * Math.Min(1f, dt * 2.5f);

            var bpm = (56f + 62f * (1f - Ink.Clamp01(health))) * _exertion;
            _beatPeriod = 60f / bpm;
            _beatPhase += dt / _beatPeriod;

            if (_beatPhase >= 1f)
            {
                _beatPhase -= (float)Math.Floor(_beatPhase);
                _beatAt = wall;
                spring.Kick(0.09f * Ink.Clamp01(cfg.HudBarSlosh) * cfg.VitalsParticles);
            }

            var since = wall - _beatAt;
            var lub = since >= 0f && since < 0.20f ? (1f - since / 0.20f) * (1f - since / 0.20f) : 0f;
            var dubSince = since - _beatPeriod * 0.20f;
            var dub = dubSince >= 0f && dubSince < 0.16f ? (1f - dubSince / 0.16f) * (1f - dubSince / 0.16f) * 0.55f : 0f;
            var glow = Math.Min(1f, lub + dub);

            if (glow <= 0.02f) return;

            var alpha = (int)(58f * glow * cfg.VitalsParticles * strength);
            if (alpha <= 3) return;

            var wide = surface - x0;
            if (wide <= 0.002f) return;

            Ink.Bar(x0, y0, wide, h, Ink.Alpha(Ink.Mix(body, Color.FromArgb(body.A, 255, 255, 255), 0.85f), alpha));
        }

        /// <summary>When the armour last took a hit, on the wall clock. For the flash.</summary>
        private float _plateHitAt = -10f;

        /// <summary>ARMOUR: steel, lying down -- a lit top edge and a shadowed bottom edge, one slanted highlight sweeping toward the surface, the fill flashing on a hit. See Columns.Plating.</summary>
        private void Plating(Settings cfg, float x0, float y0, float h, float surface,
                             Color body, float t, float wall, Readings r, float strength)
        {
            if (cfg.VitalsParticles <= 0.001f) return;

            var span = surface - x0;
            if (span <= 0.004f / Ink.Aspect) return;

            if (r.ArmourDelta < -0.001f) _plateHitAt = wall;

            var white = Color.FromArgb(body.A, 255, 255, 255);

            var edgeH = Math.Max(1f / Ink.ScreenHeight, h * 0.14f);

            Ink.Bar(x0, y0, span, edgeH, Color.FromArgb((int)(60f * strength), 255, 255, 255));
            Ink.Bar(x0, y0 + h - edgeH, span, edgeH, Color.FromArgb((int)(70f * strength), 0, 0, 0));

            var at = t * 0.16f;
            at -= (float)Math.Floor(at);

            var bandW = Math.Max(3f / Ink.ScreenWidth, Math.Min(span * 0.16f, h / Ink.Aspect * 1.2f));
            var slant = bandW * 1.1f;
            var centre = x0 - slant + at * (span + bandW + slant * 2f) - bandW * 0.5f;
            var ends = Math.Min(at * 4f, Math.Min((1f - at) * 4f, 1f));

            const int slices = 6;
            var sliceH = h / slices;
            var sheen = Ink.Mix(body, white, 0.85f);

            for (var i = 0; i < slices; i++)
            {
                var lean = ((i + 0.5f) / slices - 0.5f) * slant;
                var sy = y0 + i * sliceH;

                for (var j = 0; j < 3; j++)
                {
                    var share = j == 1 ? 1f : 0.4f;
                    var sL = centre + lean - bandW * 0.5f + j * (bandW / 3f);
                    var sR = sL + bandW / 3f;

                    sL = Math.Max(x0, sL);
                    sR = Math.Min(surface, sR);
                    if (sR - sL <= 0f) continue;

                    var alpha = (int)(85f * share * ends * strength);
                    if (alpha <= 3) continue;

                    Ink.Bar(sL, sy, sR - sL, sliceH, Ink.Alpha(sheen, alpha));
                }
            }

            var since = wall - _plateHitAt;
            if (since >= 0f && since < 0.32f)
            {
                var f = 1f - since / 0.32f;
                f *= f;

                Ink.Bar(x0, y0, span, h, Ink.Alpha(Ink.Mix(body, Color.FromArgb(body.A, 225, 240, 255), 0.9f), (int)(170f * f * strength)));
            }
        }

        /// <summary>THE THIRD: charge streaks, lying down -- toward the surface while it fills, away while it drains. See Columns.Streaks.</summary>
        private static void Streaks(Settings cfg, float x0, float y0, float h, float surface,
                                    Color body, float wall, Readings r, float strength)
        {
            var count = (int)Math.Round(3f * cfg.VitalsParticles);
            if (count < 1) return;

            var span = surface - x0;
            var len = Math.Min(span * 0.22f, h / Ink.Aspect * 0.9f);
            var tall = h * 0.09f;

            if (span <= len * 1.5f) return;

            var draining = r.ThirdIsEnergy && r.ThirdDelta < -0.00001f;
            var filling = r.ThirdIsEnergy && r.ThirdDelta > 0.00001f;

            var rate = draining ? 1.6f : filling ? 1.0f : 0.5f;
            if (r.Tired) rate = 0.3f;
            if (r.SpecialActive) rate *= 2.2f;

            var tint = Ink.Mix(body, Color.FromArgb(body.A, 255, 255, 255), 0.75f);
            var bright = r.Tired ? 70f : 150f;

            for (var i = 0; i < count; i++)
            {
                var raw = wall * rate * (0.85f + 0.15f * i) + i * 0.37f;
                var cycle = (float)Math.Floor(raw);
                var at = raw - cycle;

                var prog = draining ? 1f - at : at;

                var lane = 0.12f + 0.76f * Paint.Scatter(i * 3.1f + cycle * 17.3f);

                var py = Ink.Clamp(y0 + h * lane - tall / 2f, y0, y0 + h - tall);
                var px = x0 + (span - len) * prog;

                var edge = Math.Min(at * 4f, Math.Min((1f - at) * 4f, 1f));

                var alpha = (int)(bright * edge * strength);
                if (alpha <= 4) continue;

                Ink.Bar(px, py, len, tall, Ink.Alpha(tint, alpha));
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
            // ONE EVERY FOURTEEN PIXELS, WHICH USED TO BE ONE EVERY FIVE. Rectangles come
            // out of one list the whole machine shares, and three columns of them beside
            // the minimap were part of what took the background off Hoodrich's phone in a
            // car. The gradient is broad and slow enough that the count does not show.
            var n = (int)(w * Ink.ScreenWidth / 14f);

            if (n < 1) n = 1;
            if (n > 18) n = 18;

            return n;
        }
    }
}
