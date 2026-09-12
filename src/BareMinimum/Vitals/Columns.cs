using System;
using System.Drawing;
using GTA;
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
    /// at alpha 228, the channel at 165, the plate under the foot the exact width of the
    /// surround with a white mark inside it. Not the same numbers copied; the same code. That
    /// is the only way five bars come out as one instrument rather than as two families that
    /// nearly agree, and it is what "the border and sizes should be identical" asked for.
    ///
    /// THE INSIDE IS THESE THREE BARS' OWN. It is the strip's liquid stood on end: gravity
    /// points down now, the surface is at the top of the fill, it bows across the width like
    /// a meniscus, it leans, drifts, and is thrown up and down by the same springs a hit or a
    /// hard stop kicks. A PULSE climbs the health bar at the heart's own rate, glints climb the
    /// armour, and charge STREAKS run up the energy while it rebuilds and down while it drains.
    /// The sleep and hunger bars beside these move on their own, slower clock; the five are
    /// meant to be told apart by that as much as by the marks under them.
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
            // BY NAME, from the row: [HUD] RowOrder decides where each stands. See Gauge.Standing.
            // ONE BAR FOR BOTH. While there is armour the bar is armour's blue and stands full
            // -- the plate is taking the hits and the man under it is whole -- and the moment
            // it is gone the same bar is the health, green, running down as it always did. Two
            // columns said the same thing twice, and the second was usually empty. The mark
            // under it says which it is: the shield while it is the shield, the heart after.
            var armoured = r.Armour > 0.002f;

            One(cfg, gauge, row, row.SlotOf("health"), armoured ? 1f : r.Health,
                Paint.Health(cfg, row.Opacity, r, m, strength),
                m.Health, Kind.Health, armoured ? _shield : _heart, m, r, strength);

            if (r.HasThird)
            {
                One(cfg, gauge, row, row.SlotOf("energy"), r.Third, Paint.Third(cfg, row.Opacity, r, m, strength),
                    m.Third, Kind.Third, _bolt, m, r, strength);
            }
        }

        // ======================================================================
        // One column
        // ======================================================================

        private void One(Settings cfg, Gauge gauge, Gauge.Row row, int slot, float fraction,
                         Color body, Momentum.Spring spring, Kind kind, Icon mark,
                         Momentum m, Readings r, float strength)
        {
            if (slot < 0) return;

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
            // HELD STILL, IF THAT IS WHAT WAS ASKED FOR. See Settings.HudBarLevel.
            var wave = cfg.HudBarLevel ? Ink.Clamp01(cfg.HudBarWave) : 0f;

            // The bar's width as a HEIGHT fraction, for anything measured across the bar in
            // the same unit as along it.
            var thick = w * aspect;

            // ---- the idle motion, stood on end ----
            //
            // The drift is a share of the bar's LENGTH, the bow and the lean shares of its
            // WIDTH, as on the strip -- so the same numbers give the same feel turned
            // through ninety degrees. Three periods that do not divide into each other.
            //
            // EACH BAR ON ITS OWN TEMPO. A phase offset alone left the three on the same
            // frequency, and three bars breathing at one rate read as one instrument breathing
            // in unison -- which was reported. Scaling the clock per bar puts them on different
            // periods entirely, so no two are ever in step for long.
            //
            // AND BARELY MOVING. The idle travel is about a quarter of what it first was: a
            // surface at rest that is still plainly liquid, and nothing more. The movement you
            // see is the spring -- a hit, a brake, a landing, running out of breath -- which is
            // what "settle to a barely moving flat state, and the movement makes them slosh"
            // asked for.
            var tempo = kind == Kind.Health ? 0.87f : kind == Kind.Armour ? 1f : 1.13f;
            var tt = t * tempo + phase * 11.7f;

            var drift = (float)Math.Sin(tt * (2.0 * Math.PI / 5.3)) * 0.004f * h * wave;
            var bow = (float)Math.Sin(tt * (2.0 * Math.PI / 3.7)) * 0.05f * thick * wave;
            var tilt = (float)Math.Sin(tt * (2.0 * Math.PI / 7.1)) * 0.04f * thick * wave;

            // ---- the slosh ----
            //
            // Positive is toward the surface, which up here means UP: a hit drops the level
            // and it rebounds; a hard stop throws it up the tube and it settles back.
            var thrown = cfg.HudBarLevel ? spring.S * h : 0f;
            var speed = cfg.HudBarLevel ? Ink.Clamp(spring.V * 1.8f, -1f, 1f) : 0f;

            bow += speed * 0.40f * thick;
            tilt += speed * 0.25f * thick;

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
            var inside = cfg.HudBarInsides ? t / 7f + phase * 3f : 0f;
            var warmTo = Color.FromArgb(body.A, 255, 250, 235);

            var bodyH = floor - bodyTop;

            if (bodyH > 0f)
            {
                var bands = Bands(bodyH, cfg.HudBarInsides);

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

                    // ONE RECTANGLE A BAND. The gloss used to split every one of them in two --
                    // a lit left third and a plain right two thirds -- which doubled the most
                    // expensive loop in the mod to buy an eight per cent lift in colour. It is
                    // laid over the whole fill in a single pass below instead, and looks the
                    // same. Rectangles come out of one list the whole machine shares.
                    Ink.Bar(x, bTop, w, bBot - bTop, Ink.Mix(body, warmTo, warm));
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

                var start = colTop[j] + crestH;
                if (bodyTop > start) Ink.Bar(cL, start, cR - cL, bodyTop - start, edgeTint);

                var crestBot = Math.Min(floor, colTop[j] + crestH);
                if (crestBot > colTop[j]) Ink.Bar(cL, colTop[j], cR - cL, crestBot - colTop[j], crest);
            }

            // THE GLOSS, ONCE, OVER ALL OF IT. A pale strip down the side the light comes from,
            // laid over the fill in one pass instead of one rectangle per band. It starts at the
            // lowest point of the surface so it can never poke out of the liquid at a wall.
            if (glossW > 0f && floor > bodyTop)
            {
                Ink.Bar(x, bodyTop, glossW, floor - bodyTop,
                        Color.FromArgb((int)(body.A * glossK), white.R, white.G, white.B));
            }

            // ---- what lives inside ----
            //
            // AND ONLY IF THE FRAME CAN AFFORD IT. Everything above is the instrument and is
            // drawn whatever happens; everything from here down is decoration. Past this mod's
            // share of the machine's one list of rectangles the decoration stops and the bars
            // carry on, because a bar with no sparkle is a bar and half a bar is a bug. See
            // BareMinimum.UI.Draw.Room, which is where the share is kept.
            if (!cfg.HudBarInsides || !BareMinimum.UI.Draw.Room) return;

            // THE RELIEF ON EVERY BAR, before whatever this one keeps inside it.
            Relief(cfg, x, w, floor, surface, body, t, strength);

            if (cfg.VitalsParticles <= 0.001f) return;

            switch (kind)
            {
                case Kind.Health:
                    // Plate while it is armour, a heartbeat once it is blood.
                    if (r.Armour > 0.002f)
                    {
                        Plating(cfg, x, w, floor, surface, body, t, m.Wall, r, strength);
                    }
                    else
                    {
                        // HEARTBEAT FIRST AND ALWAYS. It is what advances the beat clock every
                        // frame -- the phase, the period and when the last beat fell -- and it
                        // returns early once the flash has faded, so the cells cannot be folded
                        // into it. They read that clock; they must not be the ones running it.
                        Heartbeat(cfg, x, w, floor, surface, body, spring, m.Wall, r.Health, strength);
                        Cells(cfg, x, w, floor, surface, body, m.Wall, strength);
                    }
                    break;
                case Kind.Armour: Plating(cfg, x, w, floor, surface, body, t, m.Wall, r, strength); break;
                default: Streaks(cfg, x, w, floor, surface, body, m.Charge, r, strength); break;
            }
        }

        // ======================================================================
        // The specks
        // ======================================================================

        /// <summary>The heart's phase, in beats, and when the last one fell. Health only; there is one health bar.</summary>
        private float _beatPhase;
        private float _beatWall = -1f;
        private float _beatAt = -10f;
        private float _beatPeriod = 1f;
        private float _exertion = 1f;

        /// <summary>
        /// HEALTH: a heartbeat. On each beat the whole fill flashes brighter for a moment and
        /// the level itself is given a small kick, so the surface throbs and rings down -- a
        /// pulse you see in the liquid rather than a marker travelling through it, which is what
        /// the first version was and what was asked to be different. A fainter second flash a
        /// fifth of a beat behind is the dub.
        ///
        /// THE RATE IS THE HEART'S, and the heart is his. About fifty-six a minute at full
        /// health, near a hundred and twenty as it drains, and QUICKER FOR RUNNING -- a third
        /// again at a jog, two thirds at a sprint, eased in and out the way breath is -- on the
        /// wall clock, so it is a real rate. The phase is accumulated rather than read off the
        /// clock, so a change of rate never skips or doubles a beat.
        /// </summary>
        private void Heartbeat(Settings cfg, float x, float w, float floor, float surface,
                               Color body, Momentum.Spring spring, float wall, float health, float strength)
        {
            if (cfg.VitalsParticles <= 0.001f) return;

            var dt = _beatWall < 0f ? 0f : Ink.Clamp(wall - _beatWall, 0f, 0.1f);
            _beatWall = wall;

            // How hard he is working, eased.
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

                // The throb: a small kick up the spring, scaled by the slosh dial so a player
                // who turned the jolts down gets a quieter heart too.
                spring.Kick(0.09f * Ink.Clamp01(cfg.HudBarSlosh) * cfg.VitalsParticles);
            }

            // The throb: quick in, quick out, and the dub behind it.
            var since = wall - _beatAt;
            var lub = Flash(since, 0.20f);
            var dub = Flash(since - _beatPeriod * 0.20f, 0.16f) * 0.55f;
            var glow = Math.Min(1f, lub + dub);

            if (glow <= 0.02f) return;

            // IT THROBS DARK, AND ONLY GOES WHITE WHEN HE IS LOW.
            //
            // It flashed white on every beat, which was two mistakes at once. On a red bar a
            // white flash reads as the level RISING -- brighter is more, everywhere else in
            // this row -- so a resting heart looked like it was healing him sixty times a
            // minute. And it spent the loudest colour the HUD has on the most ordinary event
            // there is, leaving nothing louder for the moments that are not ordinary.
            //
            // Dark is the honest direction for a beat: a pulse is a squeeze, and the bar
            // dimming and coming back is what a squeeze looks like. White now means one thing
            // -- something is wrong -- and it is used twice, here below the threshold and in
            // Paint the instant he is hit.
            //
            // THIS ALSO KILLED A SECOND CLOCK. Paint used to darken the colour on a sine of its
            // own whenever health was low, so a hurt bar had two pulses at two rates. This one
            // keeps its clock because it is the real one: the period comes from his health and
            // how hard he is working, and you can take his pulse off it.
            var low = cfg.VitalsLowHealthPulse && health > 0f && health < cfg.VitalsLowHealthAt;

            var wash = low
                           ? Color.FromArgb(body.A, 255, 238, 232)
                           : Color.FromArgb(body.A, 0, 0, 0);

            // The alarm is worth more of the bar than the resting throb is.
            var alpha = (int)((low ? 118f : 66f) * glow * cfg.VitalsParticles * strength);
            if (alpha <= 3) return;

            var tall = floor - surface;
            if (tall <= 0.002f) return;

            Ink.Bar(x, surface, w, tall, Ink.Alpha(Ink.Mix(body, wash, 0.85f), alpha));
        }

        /// <summary>
        /// HEALTH: a few cells in the bloodstream, shoved up the column on the beat and drawn
        /// back down between them.
        ///
        /// EVERY OTHER BAR'S SPECKS TRAVEL AND THESE DO NOT. The stomach sinks crumbs, the
        /// drink sends bubbles up, the energy runs charge along its lanes -- all of them go
        /// somewhere and wrap. Blood in a vessel does not travel past you; it surges and falls
        /// back, twice a second, and that difference is the whole reason this is its own method
        /// rather than Streaks with a different tint. A cell that drifted steadily upward would
        /// say "flowing", and what this bar has to say is "pumping".
        ///
        /// PUSHED, THEN SUCKED BACK PAST WHERE IT STARTED. The wave is a swell on the beat and
        /// a gentler undershoot behind it, so each cell rises, falls below its resting height
        /// and settles -- pressure rather than a dot being moved. Squeezing something and
        /// letting go, not lifting it and putting it down.
        ///
        /// AND IT BARELY MOVES. The travel is a twentieth of the column, down from a sixth: at
        /// the old figure they crossed a sixth of the screen every beat, which read as three
        /// dots being thrown about rather than as anything suspended in a liquid. Most of what
        /// you see now is the slow wander they have between beats, with the beat as a nudge on
        /// top of it -- which is what blood in a vessel actually does. The beat that got quieter
        /// in the movement got louder in the BRIGHTNESS to pay for it; two pixels on a
        /// nine-pixel column is nearly nothing, and a lift in light is not.
        ///
        /// AND THE WAVE TRAVELS UP THE COLUMN. A cell higher up feels the beat later, by a
        /// fifth of a beat over the length of the bar. Without that the three of them jump in
        /// perfect unison, which reads as the whole bar twitching; with it there is a visible
        /// front moving through, which is what a pulse is.
        ///
        /// THE CLOCK IS THE HEART'S, not a clock of its own. Heartbeat above works out the
        /// period from health and exertion -- 56 bpm resting and half as fast again at a
        /// sprint, faster the worse the wound -- and everything here hangs off _beatAt and
        /// _beatPeriod. So the cells quicken with him without a second dial to keep in step,
        /// and the bar has a pulse you can take.
        ///
        /// THREE OF THEM. This is decoration on a column nine pixels wide, and it yields with
        /// the rest of the decoration when the frame's share of rectangles has gone.
        /// </summary>
        private void Cells(Settings cfg, float x, float w, float floor, float surface,
                           Color body, float wall, float strength)
        {
            var count = (int)Math.Round(3f * cfg.VitalsParticles);
            if (count < 1) return;
            if (count > 5) count = 5;

            var span = floor - surface;

            // Square ON SCREEN: equal width and height fractions give a rectangle as much
            // wider than it is tall as the screen is.
            var size = w * 0.17f;
            var tall = size * Ink.Aspect;

            // FEWER OF THEM IN A SHORTER COLUMN, and none at all in a puddle.
            //
            // The resting heights spread across whatever room there is rather than across a
            // fixed distance, so at a quarter full three cells are packed into a quarter of the
            // bar -- which reads as grit in the bottom of a glass, not as blood in a vessel.
            // Room for three and a half each is the point where they stop touching under the
            // wander, and below one there is nothing worth drawing at all.
            var room = (int)(span / (tall * 3.5f));
            if (count > room) count = room;
            if (count < 1) return;

            var since = wall - _beatAt;

            // Bright enough to be seen against a bar that is dark at exactly the moment this
            // matters most, and warm rather than white -- these are IN the blood, not on it.
            var tint = Ink.Mix(body, Color.FromArgb(body.A, 255, 214, 206), 0.78f);

            for (var i = 0; i < count; i++)
            {
                // Where it sits between beats: spread up the column, with a slow wander so
                // three cells are not three marks painted on the glass.
                //
                // THE WANDER CARRIES MORE OF THE MOVEMENT THAN THE BEAT DOES NOW, and that is
                // the right way round. Blood between beats is not still, it is drifting; the
                // beat is a nudge on top of a drift. Two periods that do not divide into each
                // other, so a cell never repeats a path you could learn.
                var rest = 0.20f + 0.60f * ((i + 0.5f) / count);

                rest += 0.055f * (float)Math.Sin(wall * (0.17f + i * 0.05f) + i * 2.3f);
                rest += 0.030f * (float)Math.Sin(wall * (0.41f + i * 0.09f) + i * 5.1f);

                var lane = 0.20f + 0.60f * Paint.Scatter(i * 5.7f + 1.3f);

                // THE FRONT MOVES UP THE COLUMN. See the note above.
                var t = since - rest * _beatPeriod * 0.20f;

                // THE SWELL AND THE PULL BACK UNDER IT -- and then the second, smaller one.
                //
                // ON Swell, NOT Flash. Flash is instant at the front, which is right for a
                // light coming on and wrong for something being carried: an impulse reads as
                // the speck being flicked. These ease in and out, and they are slower -- the
                // lub takes a third of a second where it took a fifth -- so what you see is
                // something being moved BY a fluid rather than by a finger.
                //
                // LUB AND DUB, off the same two figures the throb above uses: a beat is two
                // sounds and two pressures, the second a fifth of a period behind the first and
                // about half its size.
                var lub = Swell(t, 0.34f) - 0.38f * Swell(t - 0.34f, 0.46f);

                var dubAt = t - _beatPeriod * 0.20f;
                var dub = (Swell(dubAt, 0.24f) - 0.38f * Swell(dubAt - 0.24f, 0.34f)) * 0.45f;

                var push = lub + dub;

                // A THIRD OF THE TRAVEL IT HAD. It was 0.16 of the column, which on a bar this
                // tall is a speck crossing a sixth of the screen height every beat -- read as
                // three dots being thrown up and down rather than as anything in a liquid. The
                // beat should be felt in them, not performed by them.
                var at = Ink.Clamp01(rest + push * 0.055f);

                var py = floor - tall - (span - tall) * at;
                var px = Ink.Clamp(x + w * lane - size / 2f, x, x + w - size);

                // Brighter as it is driven, so the surge is in the LIGHT as much as in the
                // position -- which matters much more now the travel is small: a couple of
                // pixels of movement is nearly invisible on a nine-pixel column, and the same
                // beat showing as a lift in brightness is not.
                var lit = 0.52f + 0.48f * Ink.Clamp01(push);

                var alpha = (int)(165f * lit * cfg.VitalsParticles * strength);
                if (alpha <= 4) continue;
                if (alpha > 255) alpha = 255;

                Ink.Bar(px, py, size, tall, Ink.Alpha(tint, alpha));
            }
        }

        /// <summary>
        /// A SWELL: nought at both ends and full in the middle, easing in and out.
        ///
        /// FLASH'S OPPOSITE, AND WHY BOTH EXIST. Flash is instant at the front and decays --
        /// right for a light coming on, which is what the beat's own throb is. It is wrong for
        /// something being MOVED: an instant onset is an impulse, and a speck that jumps and
        /// then drifts back reads as being flicked rather than carried. A liquid has no
        /// discontinuities in it, so neither does this.
        /// </summary>
        private static float Swell(float since, float life)
        {
            if (since < 0f || since >= life) return 0f;

            return (float)Math.Sin(Math.PI * since / life);
        }

        /// <summary>One flash: nought before it, full at once, gone after <paramref name="life"/> seconds, eased out.</summary>
        private static float Flash(float since, float life)
        {
            if (since < 0f || since >= life) return 0f;

            var k = 1f - since / life;
            return k * k;
        }

        /// <summary>When the armour last took a hit, on the wall clock. For the flash.</summary>
        private float _plateHitAt = -10f;

        /// <summary>
        /// THE RELIEF: what makes a bar look like a solid thing rather than a coloured strip.
        ///
        /// A BEVEL, and only the bevel -- the wall the light falls on lit, a thin bright edge
        /// down the left of the fill, and the far wall in shadow, a thin dark edge down the
        /// right, so the fill stands off the channel instead of lying flat in it.
        ///
        /// THE SWEEP THAT CAME WITH IT WENT BACK TO THE ARMOUR. Shared out across the row it
        /// made five liquids all look like polished metal, which is the armour's job and not
        /// theirs; see Plating. The bevel is the half that was worth having everywhere.
        ///
        /// Drawn over the fill and under whatever the bar keeps inside it, so a bubble or a
        /// spark still reads as being in the liquid rather than under glass.
        /// </summary>
        private static void Relief(Settings cfg, float x, float w, float floor, float surface,
                                   Color body, float t, float strength)
        {
            if (cfg.VitalsRelief <= 0.001f) return;

            var tall = floor - surface;
            if (tall <= 0.004f) return;

            var k = Ink.Clamp01(cfg.VitalsRelief);

            // ---- the bevel: lit edge on the left, shadow on the right ----
            var edgeW = Math.Max(1f / Ink.ScreenWidth, w * 0.14f);

            Ink.Bar(x, surface, edgeW, tall, Color.FromArgb((int)(60f * strength * k), 255, 255, 255));
            Ink.Bar(x + w - edgeW, surface, edgeW, tall, Color.FromArgb((int)(70f * strength * k), 0, 0, 0));
        }

        /// <summary>
        /// ARMOUR, AND ONLY ARMOUR. A HIGHLIGHT that crosses it now and then -- one soft
        /// slanted band sweeping up the metal every several seconds, the way a reflection
        /// travels over a polished surface as it turns -- and A HIT FLASHES IT, the whole fill
        /// white-blue for a third of a second: the blow landing on the armour rather than on
        /// him, which is the whole point of wearing it.
        ///
        /// THE SWEEP IS WHAT SAYS METAL, so it stays here rather than going on the row. Every
        /// bar wearing it made five liquids all look polished; the bevel is the part that was
        /// worth sharing, and that is Relief.
        /// </summary>
        private void Plating(Settings cfg, float x, float w, float floor, float surface,
                             Color body, float t, float wall, Readings r, float strength)
        {
            if (cfg.VitalsParticles <= 0.001f) return;

            var tall = floor - surface;
            if (tall <= 0.004f) return;

            if (r.ArmourDelta < -0.001f) _plateHitAt = wall;

            // ---- the highlight: one slanted band, up the metal, every ~6 s ----
            var at = t * 0.16f;
            at -= (float)Math.Floor(at);

            var bandH = Math.Max(3f / Ink.ScreenHeight, Math.Min(tall * 0.16f, w * Ink.Aspect * 1.2f));
            var slant = bandH * 1.1f;
            var centre = floor + slant - at * (tall + bandH + slant * 2f) + bandH * 0.5f;
            var ends = Math.Min(at * 4f, Math.Min((1f - at) * 4f, 1f));

            const int slices = 6;
            var sliceW = w / slices;
            var sheen = Ink.Mix(body, Color.FromArgb(body.A, 255, 255, 255), 0.85f);

            for (var i = 0; i < slices; i++)
            {
                // Each column of the band a little higher than the last: the band leans.
                var lift = ((i + 0.5f) / slices - 0.5f) * slant;
                var sx = x + i * sliceW;

                for (var j = 0; j < 3; j++)
                {
                    // Three stacked slices, the middle brightest: a highlight, not a stripe.
                    var share = j == 1 ? 1f : 0.4f;
                    var sTop = centre - lift - bandH * 0.5f + j * (bandH / 3f);
                    var sBot = sTop + bandH / 3f;

                    sTop = Math.Max(surface, sTop);
                    sBot = Math.Min(floor, sBot);
                    if (sBot - sTop <= 0f) continue;

                    var alpha = (int)(85f * share * ends * strength);
                    if (alpha <= 3) continue;

                    Ink.Bar(sx, sTop, sliceW, sBot - sTop, Ink.Alpha(sheen, alpha));
                }
            }

            // ---- the hit: the whole fill flashes ----
            var f = Flash(wall - _plateHitAt, 0.32f);
            if (f > 0f)
            {
                Ink.Bar(x, surface, w, tall, Ink.Alpha(Ink.Mix(body, Color.FromArgb(body.A, 225, 240, 255), 0.9f), (int)(170f * f * strength)));
            }
        }

        /// <summary>
        /// THE THIRD: charge streaks -- short bright dashes racing along the bar in lanes.
        ///
        /// THE DIRECTION IS THE FLOW. They rise while the energy rebuilds and fall while it is
        /// being spent, so a glance says which way the bar is going before the level has moved
        /// far enough to show it; at rest they drift up slowly. Winded, they crawl and dim.
        /// While the special ability runs they race, which is the "doubling" showing inside the
        /// bar as well as in its colour.
        ///
        /// ON THE CHARGE PHASE, NOT THE WALL CLOCK. Everything about how fast and which way
        /// lives in Momentum.Charge now, which is a signed accumulation rather than a clock
        /// multiplied by a rate -- the rate used to be worked out here and applied to a growing
        /// number, which teleported every streak on the screen each time it changed. Momentum's
        /// comment has the whole of it. What is left in here is where a streak IS, which is all
        /// this method was ever meant to decide.
        /// </summary>
        private static void Streaks(Settings cfg, float x, float w, float floor, float surface,
                                    Color body, float charge, Readings r, float strength)
        {
            var count = (int)Math.Round(3f * cfg.VitalsParticles);
            if (count < 1) return;

            // WIRED, THERE ARE MORE OF THEM. A stimulant holds the bar at the top, so the level
            // says nothing for as long as it rides; the charge running through it is what says
            // he is up.
            if (r.Wired) count *= 2;

            // THINNER AND SHORTER, on request: a hair of a dash, not a bar within the bar.
            var span = floor - surface;
            var len = Math.Min(span * 0.22f, w * Ink.Aspect * 0.9f);
            var wide = w * 0.09f;

            if (span <= len * 1.5f) return;

            var tint = Ink.Mix(body, Color.FromArgb(body.A, 255, 255, 255), 0.75f);
            var bright = r.Tired ? 70f : r.Wired ? 215f : 150f;

            for (var i = 0; i < count; i++)
            {
                // Staggered speeds, so the lanes never fall into step. A constant per lane, so
                // scaling the shared phase by it cannot introduce a jump of its own.
                var raw = charge * (0.85f + 0.15f * i) + i * 0.37f;
                var cycle = (float)Math.Floor(raw);

                // Math.Floor rounds toward negative infinity, which is what makes this wrap
                // correctly on the way back: at -0.3 the trip is -1 and the position 0.7.
                var at = raw - cycle;

                var prog = at;

                // A new lane every trip, from the trip's own number.
                var lane = 0.12f + 0.76f * Paint.Scatter(i * 3.1f + cycle * 17.3f);

                var px = Ink.Clamp(x + w * lane - wide / 2f, x, x + w - wide);
                var py = floor - len - (span - len) * prog;

                var edge = Math.Min(at * 4f, Math.Min((1f - at) * 4f, 1f));

                var alpha = (int)(bright * edge * strength);
                if (alpha <= 4) continue;

                Ink.Bar(px, py, wide, len, Ink.Alpha(tint, alpha));
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
        private static int Bands(float h, bool insides)
        {
            // NOTHING TURNING OVER, NOTHING TO BAND. See Settings.HudBarInsides.
            if (!insides) return 1;

            // ONE EVERY THIRTY PIXELS, WHICH USED TO BE ONE EVERY FOURTEEN AND, BEFORE THAT,
            // ONE EVERY FIVE. Rectangles come out of one list the whole machine shares, and
            // three columns of them beside the minimap were part of what took the background
            // off Hoodrich's phone in a car. The gradient is broad and slow enough that the
            // count does not show -- and the row is six bars wide now, where eighteen bands
            // apiece is a hundred and eight rectangles on a gradient nobody can count.
            //
            // The same figures as Gauge.Bands, deliberately: these bars and those are one row
            // and a gradient banded differently on two of them would be visible where the
            // banding itself is not.
            var n = (int)(h * Ink.ScreenHeight / 30f);

            if (n < 1) n = 1;
            if (n > 8) n = 8;

            return n;
        }
    }
}
