using System;
using System.Drawing;
using GTA;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.UI
{
    /// <summary>
    /// The look every panel in this mod shares. Hoodrich's Theme, in this mod's amber.
    ///
    /// THE PANEL IS THE ROUNDED BLACK AND NOTHING ROUND IT. No frame, no stripe along the top,
    /// no corner ticks: the content is the point, and the colour lives inside. An ember wash
    /// fading down from under the head, a short ember stroke on every rule, amber on the marks
    /// beside a title, and an amber-to-ember plate under whatever is chosen.
    ///
    /// SELECTION IS MOTION, NOT A SWAP. The plate under the newly chosen thing comes up over
    /// PickMs while the plate under the thing the cursor left goes down; the ink on both runs
    /// through every shade between light and dark on the way; a caption slides in from the
    /// left; and a frame with a breathing glow -- see Glide -- travels from the old place to
    /// the new one. Every screen gets all of that from here, which is why the screens agree.
    ///
    /// THE CHOSEN THING SITS ON A DARK PLATE WITH THE AMBER ON ITS RAIL, as Hoodrich's does.
    /// The first cut of this put it on the full amber-to-ember fill these panels had always
    /// highlighted with, on the theory that a bright plate works in gold. It does on a row.
    /// On a tile the size of a hand -- which is what a fifth of a panel a third of the screen
    /// wide comes to on a 21:9 -- it was a striped orange block with a picture in the middle,
    /// and the whole screen read as heavy. The plate is dark now, with a faint glaze of the
    /// amber over it and the amber solid on a rail down its left, and the words on it go to
    /// white. The colour is still the first thing you see; it is just not a wall of it.
    ///
    /// EVERYTHING HERE IS STATELESS except Glide, which is a class because a cursor has a
    /// position, and each screen owns one.
    /// </summary>
    internal static class Theme
    {
        /// <summary>The ground of every panel.</summary>
        public static readonly Color Body = Color.FromArgb(255, 12, 12, 15);

        /// <summary>Ink on a lit plate: a warm near-black, the way the vanilla menus punch text out of a highlight.</summary>
        public static readonly Color Dark = Color.FromArgb(255, 20, 18, 14);

        /// <summary>The grey rule every panel draws under its headings.</summary>
        public static readonly Color Hairline = Color.FromArgb(44, 200, 205, 200);

        /// <summary>How long the plate takes to come up under a newly chosen thing.</summary>
        public const int PickMs = 150;

        /// <summary>How far down the panel the wash under the head reaches.</summary>
        public const float WashDepth = 0.062f;

        /// <summary>One lap of the light that crosses a lit plate.</summary>
        public const int SweepMs = 2400;

        /// <summary>How long a panel takes to arrive, and how far it rises on the way.</summary>
        public const int EnterMs = 170;
        public const float EnterRise = 0.014f;

        /// <summary>
        /// Whether anything here moves at all.
        ///
        /// Set every tick from the same "Animate" switch the HUD icons obey, because motion on a
        /// screen is exactly the sort of thing some people cannot stand and some cannot see
        /// past. Off, a panel is simply there: plates snap, the cursor lands, nothing sweeps.
        /// </summary>
        public static bool Motion = true;

        // ======================================================================
        // The panel
        // ======================================================================

        /// <summary>
        /// The panel: rounded black, no stripe, no frame, an ember wash under its top.
        /// Arrive is the screen's own entrance, nought to one; pass one for a screen without.
        /// </summary>
        public static void Panel(float left, float top, float w, float h, float arrive = 1f)
        {
            // An accent with no alpha is Hud.Panel's way of being told there is no stripe.
            Hud.Panel(left, top, w, h,
                      Color.FromArgb((int)(222f * arrive), Body.R, Body.G, Body.B),
                      Color.FromArgb(0, 0, 0, 0));

            // A hint of ember at the top, not an ember top. It is the only colour on the panel
            // that is not the cursor, so it has to stay under the threshold of notice.
            Wash(left, top, w, WashDepth, (int)(12f * arrive));
        }

        /// <summary>
        /// A warm wash fading downward from the top of a panel: a few bands, each fainter than
        /// the last. The panel's corners are round, so the bands level with the corners are
        /// pulled in by the ARC rather than by the whole radius -- a quarter circle only takes
        /// the full radius off the very top row, and pulling every band in by all of it left a
        /// square of bare body inside each top corner on every panel.
        /// </summary>
        public static void Wash(float left, float top, float width, float height, int alpha)
        {
            if (alpha <= 0) return;

            const int bands = 7;
            var bandH = height / bands;

            var r = Hud.PanelRound;

            for (var i = 0; i < bands; i++)
            {
                var a = (int)(alpha * (1f - i / (float)bands));
                if (a <= 0) continue;

                var bandTop = top + i * bandH;

                // For a band whose top edge sits d above the corner's centre line, the circle
                // has come in by r - sqrt(r^2 - d^2), and that is what the band gives up.
                // Measured at the band's TOP edge, the widest the arc is anywhere in the band,
                // so nothing leaks past the curve either.
                var inset = 0f;

                var d = r - (bandTop - top);

                if (d > 0f)
                {
                    var reach = d >= r ? 0f : (float)Math.Sqrt(r * r - d * d);
                    inset = Hud.ToX(r - reach);
                }

                Hud.Bar(left + inset, bandTop, width - inset * 2f, bandH,
                        Palette.Alpha(Palette.BrandDeep, a));
            }
        }

        /// <summary>
        /// A rule with some heat in it: the grey hairline, with a short ember stroke at its left
        /// end like the tab on a folder. Under every heading, so the sections of every screen
        /// share one mark rather than each having its own idea.
        /// </summary>
        public static void Rule(float x, float y, float width, float arrive = 1f)
        {
            Rule(x, y, width, Palette.BrandDeep, arrive);
        }

        /// <summary>The same, with the stroke in another colour: the fridge's cold side has its own.</summary>
        public static void Rule(float x, float y, float width, Color stroke, float arrive)
        {
            Hud.Bar(x, y, width, 0.0012f, Palette.Alpha(Hairline, (int)(Hairline.A * arrive)));

            Hud.Bar(x, y - 0.0004f, width * 0.14f, 0.0020f,
                    Palette.Alpha(stroke, (int)(215f * arrive)));
        }

        /// <summary>
        /// A small card -- a readout, a banner -- as the same rounded black the panels are,
        /// with a rail down its left in whatever colour the card means by. The rail is inset by
        /// the corner radius top and bottom, or it pokes out past the curve at both ends.
        /// </summary>
        public const float CardRound = 0.010f;

        public static void Card(float left, float top, float w, float h, Color back, Color rail, float railW)
        {
            Hud.RoundRect(left, top, w, h, CardRound, back);

            if (rail.A <= 0 || railW <= 0f) return;

            var r = Math.Min(CardRound, h * 0.5f);

            Hud.Bar(left, top + r, railW, h - r * 2f, rail);
        }

        // ======================================================================
        // Arriving
        // ======================================================================

        /// <summary>
        /// How far a panel has arrived: nought the frame it opened, one after EnterMs, eased so
        /// it settles rather than stops. Simply one with motion switched off.
        /// </summary>
        public static float Arrive(int shownAt, int ms)
        {
            if (!Motion || ms <= 0) return 1f;

            var age = Game.GameTime - shownAt;
            if (age < 0) return 1f;

            var a = age >= ms ? 1f : age / (float)ms;

            return 1f - (1f - a) * (1f - a);
        }

        // ======================================================================
        // What is chosen
        // ======================================================================

        /// <summary>
        /// How far the newly chosen thing has come up: nought the moment it was picked, one
        /// after PickMs, eased so it settles rather than stops.
        /// </summary>
        public static float Grown(int pickedAt)
        {
            if (!Motion) return 1f;

            var held = Game.GameTime - pickedAt;
            var grown = held >= PickMs ? 1f : held / (float)PickMs;

            if (grown < 0f) grown = 1f;

            return 1f - (1f - grown) * (1f - grown);
        }

        /// <summary>
        /// How lit thing number i is, nought to one: coming up under the cursor, going down
        /// on the thing the cursor just left, dark everywhere else.
        /// </summary>
        public static float Lit(int i, int selected, int lastSelected, float grown)
        {
            if (i == selected) return grown;
            if (i == lastSelected) return 1f - grown;
            return 0f;
        }

        /// <summary>
        /// The brand as a fill, amber at the top running to ember at the bottom, in bands: for
        /// a bar that MEASURES something. The plate under a chosen row and a chosen tile used
        /// to be this and is not any more -- see Plate -- because at tile size the bands were
        /// stripes and the block was the heaviest thing on the screen.
        /// </summary>
        public static void Fill(float x, float y, float w, float h, float strength)
        {
            if (strength <= 0.01f || w <= 0f || h <= 0f) return;

            // Fewer bands on a thin row, or the bands are thinner than a pixel and the
            // rectangle count is spent on nothing anybody can see.
            var bands = h > 0.04f ? 6 : 4;
            var bandH = h / bands;

            for (var i = 0; i < bands; i++)
            {
                var c = Lerp(Palette.Brand, Palette.BrandDeep, (i + 0.5f) / bands);

                Hud.Bar(x, y + i * bandH, w, bandH, Palette.Alpha(c, (int)(240f * strength)));
            }
        }

        /// <summary>
        /// The plate under a chosen thing: a warm dark slab a shade up from the panel, a faint
        /// glaze of the amber over it, and the amber solid on a rail down its left. Strength is
        /// how lit it is, nought to one, which is what lets it come up as the cursor arrives and
        /// go down as it leaves instead of snapping either way. Three rectangles.
        ///
        /// The glaze is what keeps it in the scheme. Without it the slab is grey-brown and the
        /// screen reads as a dark mod with an orange stripe on it; with it the plate is plainly
        /// the same colour as the rail, only far back.
        /// </summary>
        public static void Plate(float x, float y, float w, float h, float strength)
        {
            if (strength <= 0.01f || w <= 0f || h <= 0f) return;

            Hud.Bar(x, y, w, h, Palette.Alpha(PlateBack, (int)(235f * strength)));
            Hud.Bar(x, y, w, h, Palette.Alpha(Palette.Brand, (int)(28f * strength)));

            var rail = Math.Min(Hud.ToX(RailW), w);
            Hud.Bar(x, y, rail, h, Palette.Alpha(Palette.Brand, (int)(255f * strength)));
        }

        private static readonly Color PlateBack = Color.FromArgb(255, 40, 34, 28);
        private const float RailW = 0.0045f;

        /// <summary>
        /// A band of light crossing a lit plate. Clipped to the plate rather than drawn over
        /// it, or it is a stripe on the panel that happens to pass a plate on its way.
        /// </summary>
        public static void Sweep(float x, float y, float w, float h, float strength)
        {
            if (!Motion || strength <= 0.01f) return;

            var sweep = (Game.GameTime % SweepMs) / (float)SweepMs;

            var bandW = w * 0.34f;
            if (w > 0.2f) bandW = w * 0.16f;

            var bandAt = x - bandW + (w + bandW) * sweep;

            var lo = Math.Max(x, bandAt);
            var hi = Math.Min(x + w, bandAt + bandW);

            if (hi > lo)
            {
                Hud.Bar(lo, y, hi - lo, h, Color.FromArgb((int)(46f * strength), 255, 255, 255));
            }
        }

        /// <summary>
        /// What ink to use on a thing that is this lit: its ordinary colour on the dark, full
        /// white on the plate, and every shade between while the plate is on its way. The
        /// ordinary colour's alpha is kept.
        ///
        /// Brighter, not darker. This went to the warm near-black when the plate was a block of
        /// amber; the plate is dark now, so the chosen word comes up to white instead.
        /// </summary>
        public static Color Ink(Color ordinary, float lit)
        {
            if (lit <= 0f) return ordinary;

            return Lerp(ordinary, Color.FromArgb(ordinary.A, 255, 255, 255), lit);
        }

        /// <summary>
        /// Whether text this lit should carry the game's black outline. Always, now: every
        /// word on these panels is light on dark, and the outline is what keeps it readable
        /// over whatever the street is doing behind a translucent panel. Kept as a call so
        /// the rule lives in one place if a bright plate ever comes back.
        /// </summary>
        public static bool Outline(float lit)
        {
            return true;
        }

        /// <summary>
        /// The name of the chosen thing, sliding in from the left and brightening as its plate
        /// comes up, so the word arrives with the plate rather than swapping under it.
        /// </summary>
        public static void Caption(string words, float x, float y, float grown, float scale = 0.28f)
        {
            Hud.Text(words, x + Hud.ToX(0.010f) * (1f - grown), y, scale,
                     Palette.Alpha(Palette.Text, (int)(90f + 165f * grown)), Hud.FontBody);
        }

        /// <summary>
        /// The marker on a tab strip: a short amber line under the chosen tab. One rectangle.
        /// A block of amber under a word is a button; a line under it is a tab.
        /// </summary>
        public static void Underline(float x, float y, float w, float strength)
        {
            if (strength <= 0.01f || w <= 0f) return;

            Hud.Bar(x, y, w, 0.0026f, Palette.Alpha(Palette.Brand, (int)(240f * strength)));
        }

        // ======================================================================
        // Odds and ends
        // ======================================================================

        public static Color Lerp(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            return Color.FromArgb((int)(a.A + (b.A - a.A) * t),
                                  (int)(a.R + (b.R - a.R) * t),
                                  (int)(a.G + (b.G - a.G) * t),
                                  (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Four strokes round a box. Left and right are the rule turned into an x-width.</summary>
        public static void Rim(float x, float y, float w, float h, float rule, Color c)
        {
            var rX = Hud.ToX(rule);

            Hud.Bar(x, y, w, rule, c);
            Hud.Bar(x, y + h - rule, w, rule, c);
            Hud.Bar(x, y + rule, rX, h - rule * 2f, c);
            Hud.Bar(x + w - rX, y + rule, rX, h - rule * 2f, c);
        }

        /// <summary>The rim of the cursor frame: brighter than the plate's top, so it reads on it.</summary>
        public static readonly Color RimInk = Lerp(Palette.Brand, Color.White, 0.45f);
    }

    /// <summary>
    /// The cursor: a frame that GLIDES from the thing it was on to the thing it is going to,
    /// rather than appearing there. One per screen, because it has a position.
    ///
    /// Each frame the screen calls Begin, the thing under the cursor calls Target with its
    /// box, and the screen calls Draw last so the frame rides over everything else. Nothing
    /// calling Target means no cursor -- an empty list, say -- and the next Target after that
    /// puts the frame straight onto the box rather than gliding in from wherever it was left.
    /// </summary>
    internal sealed class Glide
    {
        /// <summary>How thick the rim is, how far the glow reaches past it, one breath of that glow.</summary>
        public const float Rule = 0.0016f;
        public const float Glow = 0.0030f;
        public const int PulseMs = 1500;

        /// <summary>What share of the remaining distance it closes each sixtieth of a second.</summary>
        public const float Chase = 0.26f;

        public Color RimInk = Theme.RimInk;
        public Color GlowInk = Palette.BrandDeep;

        private float _x, _y, _w, _h;
        private bool _on;

        private bool _target;
        private float _tx, _ty, _tw, _th;

        /// <summary>Nothing wants the cursor until something asks for it this frame.</summary>
        public void Begin()
        {
            _target = false;
        }

        /// <summary>The thing under the cursor, saying where the frame belongs this frame.</summary>
        public void Target(float x, float y, float w, float h)
        {
            _target = true;
            _tx = x;
            _ty = y;
            _tw = w;
            _th = h;
        }

        /// <summary>Forget where it was, so the next Draw lands rather than glides.</summary>
        public void Reset()
        {
            _on = false;
            _target = false;
        }

        /// <summary>Draws the frame where it IS, which for a sixth of a second after every press is not where the cursor is -- that is the point.</summary>
        public void Draw(float arrive = 1f)
        {
            if (!_target)
            {
                _on = false;
                return;
            }

            if (!_on || !Theme.Motion)
            {
                // Straight onto the first thing. Gliding in from wherever it was left last time
                // would be a frame arriving from somewhere off the screen.
                _x = _tx;
                _y = _ty;
                _w = _tw;
                _h = _th;
                _on = true;
            }
            else
            {
                // The same share of what is left every sixtieth of a second whatever the frame
                // time is, so it lands in the same time at thirty frames and at a hundred and
                // forty.
                var dt = Game.LastFrameTime;
                if (dt <= 0f || dt > 0.25f) dt = 1f / 60f;

                var k = 1f - (float)Math.Pow(1f - Chase, dt * 60f);

                _x += (_tx - _x) * k;
                _y += (_ty - _y) * k;
                _w += (_tw - _w) * k;
                _h += (_th - _h) * k;

                if (Math.Abs(_tx - _x) < 0.0002f) _x = _tx;
                if (Math.Abs(_ty - _y) < 0.0002f) _y = _ty;
                if (Math.Abs(_tw - _w) < 0.0002f) _w = _tw;
                if (Math.Abs(_th - _h) < 0.0002f) _h = _th;
            }

            // It breathes: a slow rise and fall in the glow and a little in the rim, so a
            // cursor left alone still reads as the live thing on the screen.
            var pulse = Theme.Motion
                ? 0.5f + 0.5f * (float)Math.Sin(Game.GameTime / (double)PulseMs * Math.PI * 2.0)
                : 0.5f;

            var gX = Hud.ToX(Glow);

            Theme.Rim(_x - gX, _y - Glow, _w + gX * 2f, _h + Glow * 2f, Glow,
                      Palette.Alpha(GlowInk, (int)((16f + 24f * pulse) * arrive)));

            Theme.Rim(_x, _y, _w, _h, Rule,
                      Palette.Alpha(RimInk, (int)((185f + 50f * pulse) * arrive)));
        }
    }
}
