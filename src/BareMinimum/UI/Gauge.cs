using System;
using System.Collections.Generic;
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
        private readonly Icon[] _food = new Icon[5];

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
        /// The same two marks WITHOUT their rim, for the logos under the bars.
        ///
        /// The outlined set stays the ICON HUD's: that one sits on the world at whatever size
        /// HudSize says and needs a rim to survive a bright sky. The bar marks are a plain
        /// black silhouette, and a black rim on black art does nothing but fatten the shape --
        /// CustomSprite MULTIPLIES, so rim and fill come out the same colour.
        /// </summary>
        private readonly Icon[] _foodFlat = new Icon[5];
        private readonly Icon[] _moonFlat = new Icon[5];


        private bool _measured;

        /// <summary>
        /// The vitals -- health, armour, energy -- which stand in this row's first slots. Set
        /// by Main; null leaves the row as the two bars it was.
        /// </summary>
        public global::BareMinimum.Vitals.VitalsHud Vitals { get; set; }

        /// <summary>
        /// One row of upright bars: where it is, and every number a bar in it is drawn from.
        ///
        /// COMPUTED ONCE, HANDED TO EVERY COLUMN. The health, armour and energy columns are
        /// drawn by Vitals.Columns and the sleep and food ones here, and the only way five
        /// bars from two files come out as one instrument is if all five read the same
        /// numbers -- not the same formulas copied, the same numbers.
        /// </summary>
        internal sealed class Row
        {
            public float X;
            public float BarW, Edge, PlateW, PlateH, Breath, BarH, Top, Foot, Pitch;
            public float Opacity, IconScale;

            /// <summary>The bars standing this frame, left to right, by name. See Gauge.Standing.</summary>
            public List<string> Names = new List<string>();

            /// <summary>Which slot a bar has, by name -- or -1 when it is not standing this frame.</summary>
            public int SlotOf(string name) { return Names.IndexOf(name); }

            /// <summary>The centre line of a slot. 0 is the leftmost bar.</summary>
            public float Centre(int slot) { return X + BarW / 2f + slot * Pitch; }
        }

        /// <summary>The five, in the order they take when the ini says nothing else.</summary>
        private static readonly string[] Defaults = { "health", "sleep", "food", "energy" };

        /// <summary>
        /// The bars standing this frame, in the ini's order.
        ///
        /// THE ORDER IS THE INI'S -- [HUD] RowOrder -- and it has been asked for three ways in
        /// one afternoon, which is what makes it a setting rather than a number. Each column
        /// asks the row for its slot BY NAME, so the two files that draw the row never have to
        /// agree about a position, only about the names. Whatever the ini leaves out stands at
        /// the end in the default order; whatever is not on screen this frame -- the vitals
        /// lying down, a third bar nobody has -- is simply not in the list, and the rest close
        /// up. A misspelt name is ignored rather than fatal.
        /// </summary>
        private List<string> Standing()
        {
            var upright = Vitals != null && Vitals.Upright;
            var third = upright && Vitals.HasThird;

            var names = new List<string>();

            foreach (var raw in (_cfg.HudRowOrder ?? "").Split(','))
            {
                var name = raw.Trim().ToLowerInvariant();
                if (name == "armor") name = "armour";
                if (name == "hunger") name = "food";

                Stand(names, name, upright, third);
            }

            foreach (var name in Defaults) Stand(names, name, upright, third);

            return names;
        }

        private static void Stand(List<string> names, string name, bool upright, bool third)
        {
            if (name.Length == 0 || names.Contains(name)) return;

            if (name == "sleep" || name == "food") names.Add(name);
            // NO ARMOUR COLUMN. Armour is a state of the health bar now -- blue and full while
            // the plate holds -- so an "armour" in RowOrder is read and ignored rather than
            // stood, and an old ini that names it changes nothing.
            else if (name == "health" && upright) names.Add(name);
            else if (name == "energy" && third) names.Add(name);
        }

        /// <summary>
        /// The black surround and the channel of one slot: the fuel gauge's own numbers, alphas
        /// included. A surround at 228 -- it was 205, and was asked a bit more solid -- over a channel at 165, and an edge of 0.22 of the bar's
        /// width with a floor under it -- a proportional edge alone becomes a hairline on a
        /// narrow bar, and a fixed one becomes a frame thicker than the gauge. Both of those
        /// were argued out in Fumes. Every column in the row comes through here.
        /// </summary>
        internal void Frame(Row row, int slot, float strength = 1f)
        {
            var x = row.Centre(slot) - row.BarW / 2f;
            var aspect = Aspect();

            Hud.Bar(x - row.Edge, row.Top - row.Edge * aspect,
                    row.BarW + row.Edge * 2f, row.BarH + row.Edge * 2f * aspect,
                    Fade(Color.FromArgb(228, 0, 0, 0), strength));

            Hud.Bar(x, row.Top, row.BarW, row.BarH, Fade(Color.FromArgb(165, 28, 28, 32), strength));
        }

        /// <summary>The plate under one slot's foot, with a mark on it. See Badge.</summary>
        internal void Plate(Row row, int slot, Icon mark, float strength = 1f)
        {
            Badge(mark, row.Centre(slot), row.Foot, row.PlateW, row.PlateH, row.Breath, strength);
        }

        public Gauge(Settings cfg)
        {
            _cfg = cfg;

            for (var i = 0; i < 5; i++)
            {
                _food[i] = new Icon("food" + i + ".png");
                _moon[i] = new Icon("moon" + i + ".png");

                _foodFlat[i] = new Icon("food" + i + "_flat.png");
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
        internal static float MinimapWidth()
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
        internal static float MinimapLeft()
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

                // The minimap's measured bounds go in the log alongside the position.
                // This is the line that would have caught the icons landing on the wrong side
                // of the map in one reading, instead of needing a screenshot to notice.
                //
                // AND IT NAMES THE STYLE, because the first thing anybody reported was "no
                // bars, only icons" -- which is the default working correctly, and was not
                // answerable from this line as it stood. The size is only an icon size on the
                // icon style; on bars it is the offset that sets the foot and nothing else,
                // so it is not called a square unless it is one.
                var style = _cfg.Style.ToString();

                Log.Info("HUD: " + style + " " +
                         (_cfg.Style == HudStyle.Icons
                              ? (side * res.Height).ToString("0") + " px square"
                              : "bar " + (_cfg.HudBarLength * res.Height).ToString("0") + " px") +
                         " at " +
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
                // ONCE A FRAME, BEFORE ANYTHING READS THE CLOCK. See Clock.
                Advance(needs);

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
                float x, bottom;
                Anchor(out x, out bottom);
                var top = bottom - (side * 2f + gap);

                Measure(x, top, side);

                // Offset phases so the two never move together. In step they read as one
                // object with two halves; a little apart they read as two things.
                if (_cfg.Style == HudStyle.Bars)
                {
                    Bars(needs, x, bottom);
                    return;
                }

                Mark(_food, needs.Hunger, x, top + side / 2f, wide, side, 0f, false);
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
        /// <summary>
        /// Where the row starts and the line its plates end on.
        ///
        /// Just clear of the minimap's RIGHT-hand edge: its left, plus its width. The width has
        /// to be added -- a single figure believed to be the right edge was actually about
        /// where the map BEGINS on a 21:9, and the icons sat on the far side of the minimap.
        /// The foot is level with the foot of the minimap, which is where the game's own health
        /// strip was -- the tidiest line to share, and the line the minimap's frame stands on.
        /// </summary>
        private void Anchor(out float x, out float bottom)
        {
            var side = _cfg.HudSize;
            var wide = side / Aspect();
            var gap = side * _cfg.HudGap;

            x = _cfg.HudAutoPosition ? MinimapLeft() + MinimapWidth() + wide * 0.45f : _cfg.HudX;
            bottom = _cfg.HudAutoPosition ? 0.955f : _cfg.HudY + side * 2f + gap;

            // THE WHOLE LOT AS ONE. The group offset moves the row with the cash readout, on
            // top of whatever the row's own position says. See Settings.HudGroupX.
            x += _cfg.HudGroupX;
            bottom += _cfg.HudGroupY;
        }

        /// <summary>The row as it stands right now, for anything that has to line up with it without drawing it.</summary>
        internal Row Rack()
        {
            float x, bottom;
            Anchor(out x, out bottom);
            return RowFor(x, bottom);
        }

        /// <summary>Every number a bar in the row is drawn from, from where the row starts and the line its plates end on.</summary>
        internal Row RowFor(float x, float bottom)
        {
            var barW = Math.Max(0.001f, _cfg.HudBarWidth);

            // THE MARKS SIT UNDER THE BARS NOW, not in the foot of them. In the channel they
            // could never be wider than the bar -- fifteen pixels on an ultrawide, nine on a
            // 1080p screen -- and an apple with five states to tell apart does not survive
            // that. Out here it is bounded by nothing but taste.
            // THE PLATE IS EXACTLY THE BAR'S OUTLINE WIDTH. Same expression as the surround
            // in Column, not a number that happens to match -- these two have to stay equal
            // through any change to the edge, and the way to guarantee that is to compute
            // them the same way from the same input.
            var edge = barW * 0.22f;
            if (edge < 0.0005f) edge = 0.0005f;

            var plateW = barW + edge * 2f;
            var plateH = plateW * Aspect();

            // THE PLATE STARTS WHERE THE SURROUND ENDS, not where the BAR ends.
            //
            // Those are not the same line and that was the bug. The bar's black frame hangs
            // edge * Aspect() below the foot of the bar itself -- about two pixels -- so a
            // plate placed at the foot sat ON that overhang. Two rectangles at alpha 228
            // stacked in the same two pixels, and 205 over 205 is far darker than either, so
            // the join read as a black bruise across both gauges.
            //
            // Butting them was still the right call and this keeps it: the plate begins at the
            // exact pixel the surround stops, so there is no seam and no gap either. It is the
            // same lesson as the tiling rectangles in Fumes -- adjacent alpha has to be
            // computed from one expression, not from two that look like they agree.
            var breath = edge * Aspect();

            // BARLENGTH IS THE WHOLE THING, PLATE INCLUDED -- which is what makes it directly
            // comparable to the fuel gauge's Height, where the pump lives inside the same
            // figure. It used to be the bar alone, so an identical number gave a taller
            // instrument here than there and the two would not line up however carefully
            // either was set.
            var barH = Math.Max(0.004f, _cfg.HudBarLength - plateH - breath);

            // ANCHORED TO THE FOOT, and to nothing else. It used to be reconstructed as
            // top + side * 2 + gap, which is the same number in auto position and is NOT in
            // manual -- so Gap, whose job here is the space BETWEEN the two bars, was also
            // sliding the pair up and down the screen. One thing, one effect.
            //
            // The mark comes out of the SAME allowance, so the pair still ends on the line the
            // icons ended on and switching style does not shunt the HUD up or down the screen.
            var barTop = bottom - plateH - breath - barH;

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
            // 0.45 is exactly two edges, which put the black surrounds -- and now the black
            // PLATES under them, which are the same width -- flush against each other. Two
            // plates touching read as one wide plate with a line down it, so the floor is a
            // little over that and the pair always has daylight between them.
            var pitch = barW * (1f + Math.Max(0.56f, _cfg.HudGap * 2.4f));

            var row = new Row
            {
                X = x, BarW = barW, Edge = edge, PlateW = plateW, PlateH = plateH, Breath = breath,
                BarH = barH, Top = barTop, Foot = barTop + barH, Pitch = pitch,
                Opacity = _cfg.HudOpacity, IconScale = _cfg.HudBarIconScale
            };

            row.Names = Standing();
            return row;
        }

        private void Bars(Needs.Needs needs, float x, float bottom)
        {
            var row = RowFor(x, bottom);

            // THE ORDER OF THE ROW IS THE INI'S. See Standing: each column takes its slot by
            // name, and the vitals' three do the same in Columns.
            Column(needs.Sleep, _moon, row, row.SlotOf("sleep"), true);
            Column(needs.Hunger, _food, row, row.SlotOf("food"), false);

            if (Vitals != null) Vitals.DrawColumns(this, row);
        }

        /// <summary>One upright bar: the mark above it, the channel, and the level inside.</summary>
        private void Column(Need need, Icon[] set, Row row, int slot, bool sleep)
        {
            if (slot < 0) return;
            if (_cfg.HudHideWhenFine && need.Value > _cfg.HudFineAbove) return;

            var centreX = row.Centre(slot);
            var top = row.Top;
            var h = row.BarH;
            var w = row.BarW;

            var body = Colour(need, sleep);

            var flat = (sleep ? _moonFlat : _foodFlat)[Stage(need)];

            // Falls back to the outlined art if the rim-free copy did not deploy. A logo with
            // a rim on it is a great deal better than no logo at all.
            if (flat == null || flat.Missing) flat = set[Stage(need)];

            var x = centreX - w / 2f;

            // THE FRAME, through the one method every column in the row is framed by -- the
            // vitals' three as much as these two. See Frame for the numbers.
            Frame(row, slot);

            // THE MARK IS DRAWN HERE, BEFORE THE LEVEL, AND UNCONDITIONALLY.
            //
            // It used to be the last line of this method, past two early returns -- so the
            // badge disappeared whenever the bar was EMPTY, and whenever the animation was
            // switched off. Both are exactly when it is most needed: an empty bar with no
            // mark under it is a black column that could be either meter, and the one thing
            // a player at zero needs to know is which of the two just ran out.
            //
            // It sits BELOW the bar -- plateY is footY + breath -- so it cannot overlap the
            // level however full that is, and drawing it first costs nothing.
            Plate(row, slot, flat);

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
        }

        /// <summary>
        /// The logo under the bar: a white mark on a black plate.
        ///
        /// NOT called Mark -- that name already belongs to the icon HUD's own draw, and two
        /// methods by one name meaning two different things is a trap even when it compiles.
        ///
        /// A PLATE, BECAUSE A BLACK LOGO ON THE WORLD IS ONLY LEGIBLE HALF THE TIME. It was a
        /// plain black silhouette and it disappeared against tarmac and at night -- which was
        /// flagged as the cost when it went in, and it turned out to matter. Giving it its own
        /// dark ground and inverting the ink fixes it in both directions at once: white on
        /// black reads on any background there is, because the background is no longer part
        /// of the problem.
        ///
        /// The plate is EXACTLY the width of the bar's outline, computed from the same
        /// expression rather than a matching number, so the instrument stays one column from
        /// top to bottom.
        ///
        /// Still the rim-free art. The rim exists to hold a mark against the world, and there
        /// is no world behind this one any more -- on a black plate a black outline is
        /// invisible and only fattens the shape.
        /// </summary>
        private void Badge(Icon icon, float centreX, float footY,
                           float plateW, float plateH, float breath, float strength = 1f)
        {
            var plateX = centreX - plateW / 2f;
            var plateY = footY + breath;

            Hud.Bar(plateX, plateY, plateW, plateH,
                    Fade(Color.FromArgb(228, 0, 0, 0), strength));

            if (icon == null || icon.Missing) return;

            // Inside the plate rather than filling it. A mark flush to its own edges reads as
            // a cropped picture; the margin is what makes it a badge.
            var markW = plateW * Clamp(_cfg.HudBarIconScale, 0.2f, 1f);
            var markH = markW * Aspect();

            icon.DrawSized(centreX, plateY + plateH / 2f, markW, markH,
                           Fade(Color.FromArgb(240, 242, 246, 252), strength));
        }

        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
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

            // ONE EVERY FOURTEEN PIXELS, WHICH USED TO BE ONE EVERY FOUR.
            //
            // The gradient this draws is two humps on a fourteen- and a twenty-two-second lap,
            // wide enough that neighbouring bands differ by a hair -- which is the whole
            // argument for banding it, and also the reason the number of bands hardly shows.
            // Six bands and twenty-four look the same on a bar this narrow.
            //
            // What they do not cost the same is the frame. Rectangles come out of one list the
            // whole machine shares and the game drops whatever is handed over once it is full,
            // so a bar spending twenty-four on a gradient nobody can count is twenty-four
            // another script does not get. Six bars of them took the phone's background off
            // the screen in a car.
            var n = (int)(h * _screenH / 14f);

            if (n < 5) n = 5;
            if (n > 18) n = 18;

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

            // The waterline reads the clock straight; everything inside the fill reads it
            // slowed, so one dial still drives both and the ratio between them is fixed.
            var inside = t / InsideSlow;

            var level = h * fraction;
            var empty = 1f - fraction;

            // THE THROW. The spring's offset is a share of the bar's length, up when positive,
            // and it moves the whole surface; its speed bends the surface as well. See Slosh.
            var thrown = _foodSpring.S * h;
            var speed = Clamp(_foodSpring.V * 1.8f, -1f, 1f);

            var surfaceY = Clamp(y + h - level - thrown, y, y + h);

            var floor = y + h;

            // THE SURFACE FIRST, THEN THE BODY UNDER IT. Every column's top is worked out
            // before anything is drawn and the body starts at the LOWEST of them, so nothing
            // above it can draw over ground it has already covered -- a seam, in alpha, every
            // frame. It used to be allowed for with a margin under the swing; the spring can
            // throw the surface a good deal further than the swing, so it is computed now.
            //
            //
            // THE VITALS' MOTION, TO THE NUMBER -- see Surface -- on this bar's own tempo, so
            // no two bars in the row breathe in step.
            var tops = Surface(x, y, w, h, surfaceY, 1.06f, 1.48f, speed, h * 0.007f);

            var bodyTop = Lowest(tops, floor);

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
                var a = Pulse(u - inside * 0.00138f, 0.58f);
                var b = Pulse(u - inside * 0.00085f + 0.5f, 0.76f);

                var warm = (a * 0.6f + b * 0.4f) * (0.09f + 0.13f * empty);

                Hud.Bar(x, bTop, w, bBot - bTop,
                        Mix(body, Color.FromArgb(body.A, 255, 245, 220), warm));
            }

            if (level <= 0.002f) return;

            // ---- the surface ----
            //
            // Column by column, from the tops worked out above. See Surface for what moves it.
            var crestH = h * 0.007f;
            var crest = Mix(body, Color.FromArgb(body.A, 255, 240, 205), 0.55f);

            for (var i = 0; i < tops.Length; i++)
            {
                var left = x + w * i / tops.Length;
                var right = x + w * (i + 1) / tops.Length;
                var topY = tops[i];

                if (topY < bodyTop) Hud.Bar(left, topY, right - left, bodyTop - topY, body);

                Hud.Bar(left, topY, right - left, crestH, crest);
            }

            Relief(x, w, floor, surfaceY, body, t / PaceOf());

            Sediment(x, y, w, h, surfaceY, inside, empty);
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

            // ONE COLUMN PER SIX PIXELS, AND NEVER FEWER THAN THE BAR CAN SHOW.
            //
            // This had a FLOOR of eight, which is where the flicker came from. The bar is
            // about nine pixels wide, so eight columns made every strip 1.1px -- and the
            // comment above already says sub-pixel rectangles are a lottery with the
            // rasteriser. Each one rounded to 1px or 2px independently, every frame, at
            // slightly different heights, and the surface boiled instead of moving. Speeding
            // the wave up made it re-roll that lottery more often, which is why it got worse
            // rather than better.
            //
            // A floor is the wrong shape of guard here. It was there to keep the curve smooth,
            // and on a narrow bar it guaranteed the opposite: more columns than there are
            // pixels to put them in. One column is not a degraded curve, it is the honest
            // answer -- at nine pixels across, a parabola whose ends differ from its middle by
            // under a pixel is not a curve anybody can see, and drawing it as a single strip
            // that moves up and down is exactly what that width can express.
            //
            // Wide bars keep the curve, which is what the six is for.
            var n = (int)(px / 2f);

            if (n < 8) n = 8;
            if (n > 40) n = 40;

            return n;
        }

        /// <summary>Cached, because the resolution cannot change without a reload anyway.</summary>
        private int _screenW;

        /// <summary>
        /// SLEEP: the same surface the food bar has, running the other way.
        ///
        /// THE DROP IS GONE. It was the fifth thing tried on this level and the most involved
        /// -- a drop falling, an impact, a damped bob, rings spreading -- and it made the two
        /// bars two different instruments rather than one instrument with two channels. This
        /// takes the food bar's meniscus, which was arrived at over about as many goes, and
        /// gets its calm for free.
        ///
        /// REVERSED, so they are not the same thing twice. Food bows UP in the middle and this
        /// bows DOWN; food drifts one way and this drifts the other. Two bars side by side
        /// moving in opposite phase read as a pair, where two moving together read as one wide
        /// thing, and it costs a minus sign.
        ///
        /// AND IT QUICKENS AS THE METER EMPTIES, exactly as the food bar's does. On that side
        /// the link is to sprinting -- running burns food, so running makes it livelier. Here
        /// it is simply that the more tired you are the less still this sits, which is the same
        /// idea and needed no extra wiring: both read `empty` and nothing else.
        ///
        /// What stays is the hard waterline and the dark band above it. That was never about
        /// the drop -- it is the fix for not being able to tell where the level was, and it
        /// outlives every animation that has been hung off it.
        /// </summary>
        private void Night(float x, float y, float w, float h, float fraction, Color body)
        {
            var tired = 1f - fraction;

            var t = Clock();

            // The waterline reads the clock straight; everything inside the fill reads it
            // slowed, so one dial still drives both and the ratio between them is fixed.
            var inside = t / InsideSlow;

            var level = h * fraction;
            var floor = y + h;

            var empty = tired;

            // THE THROW, as the food bar has it, off this bar's own spring. See Slosh.
            var thrown = _sleepSpring.S * h;
            var speed = Clamp(_sleepSpring.V * 1.8f, -1f, 1f);

            var surfaceY = Clamp(y + h - level - thrown, y, floor);

            // THE SURFACE FIRST, THEN THE BODY UNDER IT: the body starts at the lowest point
            // of the surface, so nothing above it can draw over ground it has covered. Churn
            // says why this is computed rather than allowed for. The tilt runs on its own
            // phase here so the two bars never lean together.
            var cap = h * 0.008f;
            // THE VITALS' MOTION, on this bar's own tempo -- see Surface.
            var tops = Surface(x, y, w, h, surfaceY, 0.94f, 1.11f, speed, cap);

            var bodyTop = Lowest(tops, floor);

            // ---- the fill ----
            //
            // DARKEST AT THE TOP, brightening downward. The lightest part of the fill must not
            // sit immediately under the lightest part of the bar, or the cap stops being an
            // edge and the level goes back to being unreadable.
            var bands = Bands(h);

            for (var i = 0; i < bands; i++)
            {
                var bTop = bodyTop + (floor - bodyTop) * i / bands;
                var bBot = bodyTop + (floor - bodyTop) * (i + 1) / bands;

                if (bBot - bTop <= 0f) continue;

                var u = (i + 0.5f) / bands;

                var shade = u < 0.14f ? 0.38f * (1f - u / 0.14f) : 0f;

                var deep = Mix(body, Color.FromArgb(body.A,
                                                    (int)(body.R * 0.45f),
                                                    (int)(body.G * 0.45f),
                                                    (int)(body.B * 0.55f)), shade);

                Hud.Bar(x, bTop, w, bBot - bTop, deep);
            }

            // ---- the sky ----
            if (level > h * 0.06f) Stars(x, y, w, h, surfaceY, inside, tired);

            if (level <= 0.002f) return;

            // ---- the surface ----
            //
            // The food bar's meniscus, running the other way, from the tops worked out above.
            // The dark line above the cap is drawn per column so it follows the curve.
            var shadow = Fade(Color.FromArgb(210, 6, 6, 8));
            var capC = Mix(body, Color.FromArgb(body.A, 244, 248, 255), 0.62f);

            for (var i = 0; i < tops.Length; i++)
            {
                var left = x + w * i / tops.Length;
                var right = x + w * (i + 1) / tops.Length;
                var topY = tops[i];

                if (topY < bodyTop) Hud.Bar(left, topY, right - left, bodyTop - topY, body);

                Hud.Bar(left, topY - cap * 0.6f, right - left, cap * 0.6f, shadow);
                Hud.Bar(left, topY, right - left, cap, capC);
            }

            Relief(x, w, y + h, surfaceY, body, t / PaceOf());
        }

        /// <summary>
        /// The bevel, for the food and sleep bars. See Vitals.Columns.Relief,
        /// which is the same drawing against the vitals' own primitives -- these two are drawn
        /// through Hud and Fade, so the code cannot simply be shared, and the numbers are kept
        /// identical instead. A change to one wants the same change to the other.
        /// </summary>
        private void Relief(float x, float w, float floor, float surface, Color body, float t)
        {
            if (_cfg.VitalsRelief <= 0.001f) return;

            var tall = floor - surface;
            if (tall <= 0.004f) return;

            var k = Clamp01(_cfg.VitalsRelief);

            var edgeW = Math.Max(1f / (_screenW > 0 ? _screenW : 1920f), w * 0.14f);

            Hud.Bar(x, surface, edgeW, tall, Fade(Color.FromArgb((int)(60f * k), 255, 255, 255)));
            Hud.Bar(x + w - edgeW, surface, edgeW, tall, Fade(Color.FromArgb((int)(70f * k), 0, 0, 0)));
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
                // ---- how fast a star twinkles ----
                //
                // A SIXTH OF THE CONTENTS CLOCK, which is a sixth of a fifth of BarPace. The
                // raw terms were sized when the whole gauge ran at 1x; carried up to 30 and
                // then divided by five for the contents, they came out at 7.0 Hz and 4.4 Hz.
                // That is candle-flicker territory -- a real star twinkles about once a
                // second, and anything much past two reads as a fault in the panel rather
                // than as light.
                //
                // Divided rather than rewritten, so the two terms keep their ratio: 7.3 and
                // 4.6 do not divide into each other, which is what stops the pair beating
                // together into one obvious pulse. At a sixth they land near 1.2 Hz and
                // 0.7 Hz.
                //
                // Only the twinkle. Each star's LIFE -- coming up, sitting lit, going out
                // somewhere else -- still runs on the contents clock, because that is the
                // sky filling and emptying rather than any one star flickering.
                var twinkleT = t / StarSlow;

                var fast = 0.5f + 0.5f * (float)Math.Sin(twinkleT * 7.3f + i * 2.1f);
                var slow = 0.5f + 0.5f * (float)Math.Sin(twinkleT * 4.6f - i * 1.7f);

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
        /// <summary>
        /// Seconds since the HUD first drew, not seconds since the machine booted.
        ///
        /// MEASURED FROM AN EPOCH BECAUSE A FLOAT RUNS OUT OF DIGITS. TickCount is
        /// milliseconds since boot, and dividing it into a float gives a number with only
        /// about seven digits to spend: at two days of uptime the smallest step it can
        /// represent is already 0.016s, at ten days 0.06s, and at twenty-five 0.25s. The
        /// animation would then advance four times a second however smooth the maths above
        /// it -- a stutter that arrives gradually, on machines nobody reboots, and never
        /// reproduces on a freshly started one.
        ///
        /// Not the cause of anything seen so far -- this was found while looking for a
        /// stutter that turned out to be sub-pixel columns, on a machine three hours up,
        /// where the step is a thousandth of a second. It is a fault waiting for a long
        /// uptime, and it costs one subtraction to never have.
        ///
        /// The wrap is handled for the same reason the mask exists: TickCount turns over
        /// every 24.9 days, and a session running across that would otherwise see the clock
        /// jump backwards.
        /// </summary>
        /// <summary>
        /// How much slower the CONTENTS run than the waterline.
        ///
        /// The level -- the waterline and its bow and drift -- and the stuff moving through
        /// the fill had always shared one clock, so BarPace moved them together and there was
        /// no way to like one rate and not the other.
        ///
        /// A RATIO RATHER THAN A SECOND DIAL. One number in the menu that speeds the whole
        /// gauge up is worth owning; two that have to be balanced against each other is not.
        /// This fixes the relationship instead, so BarPace still means "faster" and the
        /// contents stay a fifth of it whatever it is set to.
        ///
        /// Seven, so the shipped BarPace of 42 leaves the waterline at 42x and puts the
        /// contents at 6x -- which is where they were wanted, and where they were before
        /// the waterline was asked to move. RAISED IN STEP WITH BarPace on purpose: the
        /// one dial drives both, so speeding the level up without touching this would have
        /// dragged the contents along with it.
        /// </summary>
        private const float InsideSlow = 7f;

        /// <summary>
        /// How much slower a star's TWINKLE runs than the rest of the contents.
        ///
        /// Stacked on top of InsideSlow rather than replacing it, so BarPace still moves
        /// everything and the stars keep their place in the order: waterline fastest, contents
        /// a fifth of that, a star's flicker a sixth of THAT again.
        /// </summary>
        private const float StarSlow = 6f;

        /// <summary>
        /// The animation clock: A PHASE THAT ACCUMULATES, not elapsed time times a rate.
        ///
        /// It used to be `ms / 1000 * pace`, which is correct only for as long as the pace
        /// never changes -- and the instant it does, the whole accumulated phase is rescaled
        /// with it. Going from 42 to 84 does not make the surface speed up, it teleports it to
        /// twice as far along as it was. That was invisible while the only way to change the
        /// pace was a settings row you could not see a bar from. It stopped being invisible
        /// the moment the pace started tracking his feet.
        ///
        /// Integrating the rate instead makes a change of pace continuous by construction: the
        /// phase never jumps, only the speed at which it is growing.
        ///
        /// READ-ONLY, AND ADVANCED ONCE A FRAME FROM Draw. Both bars call this and neither may
        /// move the clock, or the second would be drawn a frame further on than the first and
        /// the two would drift apart over a session.
        /// </summary>
        private float Clock()
        {
            return _phase;
        }

        /// <summary>Moves the clock on. Called exactly once a frame, at the top of Draw.</summary>
        private void Advance(Needs.Needs needs)
        {
            var now = Environment.TickCount & int.MaxValue;

            if (!_ticking) { _ticking = true; _last = now; }

            var step = now - _last;

            // TickCount wraps to negative every 24.9 days, and a session running across that
            // would otherwise see the clock jump backwards.
            if (step < 0) step += int.MaxValue;

            // A hitch, a loading screen, a pause or a suspended HUD is not animation. Anything
            // past a quarter second is thrown away rather than fast-forwarded through.
            if (step > 250) step = 250;

            _last = now;

            // ---- how hard he is working, eased ----
            //
            // The RATE is what changes here, so nothing can jump -- but a rate that snaps from
            // one to two still reads as a switch being thrown. Chased instead, so the gauge
            // winds up as he breaks into a run and winds back down as he stops, which is the
            // shape the movement itself has.
            var want = needs == null ? 0f : Clamp01(needs.Effort);

            _effort += (want - _effort) * (1f - (float)Math.Pow(0.12, step / 1000.0));

            if (Math.Abs(want - _effort) < 0.002f) _effort = want;

            var lift = 1f + _effort * (Clamp(_cfg.HudBarEffort, 1f, 6f) - 1f);

            _phase += step / 1000f * PaceOf() * lift;

            // AND THE VITALS' CLOCK, for the surfaces: seconds at the vitals' pace, the very
            // thing Momentum.Time is over there, so the five surfaces share one tempo scale.
            _liquid += step / 1000f * Math.Max(0.05f, _cfg.VitalsPace);

            // AND THE LIQUID, on the same step. See Slosh.
            Momentum(needs, step / 1000f);

            // Kept bounded. Every period in here divides into a whole number of seconds far
            // short of this, so the wrap is invisible -- and a float grown past a million has
            // lost the precision the shortest of them need.
            if (_phase > 1000000f) _phase -= 1000000f;
            if (_liquid > 1000000f) _liquid -= 1000000f;
        }

        /// <summary>Where the animation has got to, and when it was last moved on.</summary>
        private float _phase;

        /// <summary>The surfaces' clock: seconds at the vitals' pace. See Surface.</summary>
        private float _liquid;
        private int _last;
        private bool _ticking;

        /// <summary>The eased effort the gauge is currently running at. See Advance.</summary>
        private float _effort;

        // ======================================================================
        // The liquid's momentum
        // ======================================================================

        /// <summary>
        /// One bar's worth of momentum: how far the surface is thrown, as a share of the bar's
        /// length, and how fast it is moving.
        ///
        /// VITALS' SPRING, BROUGHT HOME. Vitals turned this gauge on its side for the health
        /// bar and gave the liquid a spring -- kicked when something happens, and left to ring
        /// down. It is the part of that HUD that makes the levels feel wet, and it is the one
        /// thing this bar did not have: a level here moved to where it was told and stopped.
        ///
        /// A damped spring, integrated semi-implicitly so it stays stable at any framerate the
        /// game hands us. Under a second a swing, and lightly damped, so a kick rings three or
        /// four times before it is gone -- which reads as liquid rather than as a cursor being
        /// nudged. The reach is a hard stop so nothing can throw the surface out of the bar.
        /// </summary>
        private sealed class Slosh
        {
            public float S;
            public float V;

            private const float Omega = 7.4f;    // 2 pi over 0.85 seconds
            private const float Damping = 2.1f;  // 2 zeta omega, zeta about 0.14
            private const float Reach = 0.09f;

            public void Kick(float velocity)
            {
                V += velocity;
            }

            public void Step(float dt, float force)
            {
                V += (-Omega * Omega * S - Damping * V + force) * dt;
                S += V * dt;

                if (S > Reach) { S = Reach; if (V > 0f) V = 0f; }
                if (S < -Reach) { S = -Reach; if (V < 0f) V = 0f; }
            }
        }

        private readonly Slosh _foodSpring = new Slosh();
        private readonly Slosh _sleepSpring = new Slosh();

        /// <summary>Last frame's readings, for the kicks. Below zero until there has been a frame.</summary>
        private float _lastFood = -1f;
        private float _lastSleep = -1f;

        /// <summary>How hard he is being pushed along his own length, and upward, m/s², eased.</summary>
        private float _accel;
        private float _heave;

        private float _lastAlong;
        private float _lastUp;
        private bool _haveAlong;
        private int _lastBody;

        /// <summary>The column tops of whichever surface is being drawn. One buffer; the two bars take turns.</summary>
        private float[] _tops = new float[0];

        /// <summary>
        /// The equilibrium throw per m/s² of acceleration, as a share of the bar, times the
        /// spring's stiffness -- so a steady 10 m/s² of braking lifts the liquid about three
        /// per cent of the way up the bar and holds it there until the braking stops.
        /// </summary>
        private const float LeanGain = 0.003f * 7.4f * 7.4f;

        /// <summary>The springs, moved on by this frame's step. Called from Advance.</summary>
        private void Momentum(Needs.Needs needs, float dt)
        {
            if (dt < 0f) dt = 0f;
            if (dt > 0.1f) dt = 0.1f;

            var live = _cfg.HudAnimate && _cfg.Style == HudStyle.Bars;

            var force = live ? Lean(dt) : 0f;

            if (!live)
            {
                _haveAlong = false;
                _accel = 0f;
                _heave = 0f;
            }

            Kicks(needs, live);

            _foodSpring.Step(dt, force);
            _sleepSpring.Step(dt, force);
        }

        /// <summary>
        /// The jolts: what changed since last frame, thrown at the springs.
        ///
        /// A top-up throws the level up a little; a loss slaps it down harder, the way a hit
        /// does in Vitals. THE DRAIN DOES NOT KICK -- a meter emptying over game hours moves
        /// a few millionths a frame, far under the threshold -- so this is meals, pills and
        /// waking up, which are exactly the moments worth a slosh. Read even while the bars
        /// are not animating, so switching the animation on does not land a stale kick.
        /// </summary>
        private void Kicks(Needs.Needs needs, bool live)
        {
            if (needs == null) return;

            var k = live ? Clamp01(_cfg.HudBarSlosh) : 0f;

            var food = Clamp01(needs.Hunger.Value);
            var sleep = Clamp01(needs.Sleep.Value);

            if (_lastFood >= 0f) Jolt(_foodSpring, food - _lastFood, k);
            if (_lastSleep >= 0f) Jolt(_sleepSpring, sleep - _lastSleep, k);

            _lastFood = food;
            _lastSleep = sleep;
        }

        private static void Jolt(Slosh spring, float delta, float k)
        {
            if (k <= 0f) return;

            if (delta > 0.001f) spring.Kick(0.32f * Math.Min(1f, delta * 4f) * k);
            else if (delta < -0.001f) spring.Kick(-(0.28f + 0.52f * Math.Min(1f, -delta * 4f)) * k);
        }

        /// <summary>
        /// The player's own motion as a force on both springs: a hard stop throws the liquid
        /// up the glass, a landing slaps it down. Read off the vehicle when there is one,
        /// because that is what the player feels; off the ped otherwise, so a fall still
        /// registers. A body swap, a teleport or a long frame is thrown away rather than turned
        /// into a jolt from nowhere. The eased forward acceleration also tips the surface --
        /// see Surface.
        /// </summary>
        private float Lean(float dt)
        {
            var motion = Clamp01(_cfg.HudBarLean);

            if (motion <= 0f || dt <= 0.0001f)
            {
                _accel = 0f;
                _heave = 0f;
                return 0f;
            }

            try
            {
                var ped = Game.Player.Character;
                Entity body = ped;

                if (ped.IsInVehicle())
                {
                    var veh = ped.CurrentVehicle;
                    if (veh != null && veh.Exists()) body = veh;
                }

                var v = body.Velocity;
                var f = body.ForwardVector;

                var along = v.X * f.X + v.Y * f.Y + v.Z * f.Z;
                var up = v.Z;

                if (body.Handle != _lastBody || !_haveAlong || dt > 0.09f)
                {
                    _lastBody = body.Handle;
                    _lastAlong = along;
                    _lastUp = up;
                    _haveAlong = true;
                    _accel = 0f;
                    _heave = 0f;
                    return 0f;
                }

                var a = Clamp((along - _lastAlong) / dt, -30f, 30f);
                var az = Clamp((up - _lastUp) / dt, -30f, 30f);

                _lastAlong = along;
                _lastUp = up;

                // Footsteps bob the ped a little; that is not a landing.
                if (Math.Abs(az) < 2f) az = 0f;

                // Eased, so a physics tick that stutters is a lean rather than a rattle.
                _accel += (a - _accel) * Math.Min(1f, dt * 12f);
                _heave += (az - _heave) * Math.Min(1f, dt * 12f);

                // BRAKING DROPS IT, ACCELERATING LIFTS IT, and a landing drops it. The brake
                // was the other way for a day and was felt as backwards -- the whole row
                // jumping up under braking reads as the liquid going the wrong way. All through
                // the spring, so they ring down rather than snapping back.
                return (_accel * 0.6f - _heave) * LeanGain * motion;
            }
            catch
            {
                _haveAlong = false;
                return 0f;
            }
        }

        /// <summary>
        /// Where the surface is, column by column: the vitals' liquid, exactly.
        ///
        /// THE SAME MOTION AS THE HEALTH AND ENERGY BARS, TO THE NUMBER. These two bars had a
        /// surface of their own -- a bow and a drift on the paced clock, sized by how empty the
        /// meter was, hurried as it emptied, run backwards for sleep -- tuned over many rounds
        /// and, stood in a row with the vitals, visibly a different liquid: slower, smaller,
        /// wetter in a different way. "Same flow and movements" ends that. What is here is
        /// Vitals.Columns.One's idle and slosh, copied: the drift is a share of the bar's
        /// LENGTH, the bow and the lean shares of its WIDTH, on three periods in seconds that
        /// do not divide into each other, read off a seconds clock that runs at the vitals'
        /// pace and nothing else's. The spring's speed heaps the surface toward the wall it
        /// is moving at and leans it behind, off the same two numbers.
        ///
        /// EACH BAR ON ITS OWN TEMPO. The clock is scaled per bar -- health 0.87, energy 1.13,
        /// sleep 0.94, food 1.06 -- so the five are on different periods entirely and no two
        /// are in step for long; the phase offsets them further. A row breathing in unison
        /// reads as one instrument breathing, which was reported once and is not wanted.
        ///
        /// AND BARELY MOVING. The idle travel is a surface at rest that is still plainly
        /// liquid, and nothing more; the movement you notice is the spring -- a meal, a pill,
        /// a brake, a landing -- as it is on the bars beside these.
        /// </summary>
        private float[] Surface(float x, float y, float w, float h, float surfaceY,
                                float tempo, float phase, float speed, float capH)
        {
            var floor = y + h;
            var wave = Clamp01(_cfg.HudBarWave);

            // The width as a HEIGHT fraction, for anything measured across the bar in the same
            // unit as along it.
            var thick = w * Aspect();

            var tt = _liquid * tempo + phase * 11.7f;

            var drift = (float)Math.Sin(tt * (2.0 * Math.PI / 5.3)) * 0.004f * h * wave;
            var bow = (float)Math.Sin(tt * (2.0 * Math.PI / 3.7)) * 0.05f * thick * wave;
            var tilt = (float)Math.Sin(tt * (2.0 * Math.PI / 7.1)) * 0.04f * thick * wave;

            // Positive is toward the surface, which is UP: a surface thrown upward heaps in the
            // middle and leans behind its own travel.
            bow += speed * 0.40f * thick;
            tilt += speed * 0.25f * thick;

            var columns = Columns(w);
            if (_tops.Length != columns) _tops = new float[columns];

            for (var i = 0; i < columns; i++)
            {
                // -1 at one wall, +1 at the other; 1 in the middle, 0 at both walls.
                var across = ((i + 0.5f) / columns - 0.5f) * 2f;
                var curve = 1f - across * across;

                var topY = surfaceY - drift - bow * curve - tilt * across;

                if (topY < y) topY = y;
                if (topY > floor - capH) topY = floor - capH;

                _tops[i] = topY;
            }

            return _tops;
        }

        /// <summary>The lowest point of a surface, which is where the body under it starts.</summary>
        private static float Lowest(float[] tops, float floor)
        {
            var low = 0f;

            for (var i = 0; i < tops.Length; i++)
            {
                if (tops[i] > low) low = tops[i];
            }

            return low > floor ? floor : low;
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
        private Color Fade(Color c, float strength = 1f)
        {
            var a = (int)(c.A * _cfg.HudOpacity * strength + 0.5f);

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
        // GREEN THE WHOLE WAY, deep at empty and light at full.
        //
        // It ran red-orange-amber-gold-green, which is very nearly the fuel gauge's own ramp:
        // Fumes runs red to yellow through the same oranges, and four of these five stops sat
        // inside that range. Measured in Lab the two bars came within dE 0.6 of each other at
        // 44% -- not similar, the SAME COLOUR -- so a glance at the HUD found two amber
        // columns and had to work out which was which by position.
        //
        // Green is the only family left. Fuel owns red through yellow; sleep owns blue through
        // purple; the gap between them is green one way and magenta the other, and magenta
        // cannot be reached. Any ramp from magenta to green passes through neutral grey
        // BETWEEN its stops however vivid the stops themselves are -- measured at a minimum
        // chroma of 4 to 8 across every version tried -- and the only routes around that
        // neutral run through yellow, which is fuel, or through cyan, which is sleep.
        //
        // So this trades range for separation. Green to green spans dE 58 where red to green
        // spanned 101, and the level is a little less legible from colour alone -- but the
        // fill HEIGHT is what says how full a bar is, and colour was only ever confirming it.
        // Being unmistakably not the fuel gauge is worth more than the second opinion.
        //
        // Deep at empty rather than alarming at empty, because the alarm is elsewhere: below
        // stage 1 Colour pulses this toward white, which is a far louder signal than any hue.
        //
        // THE BOTTOM TWO STOPS ARE BROWN, so the ramp runs brown through to mint rather than
        // green through to mint. Food going off is a brown business and a clean green deep end
        // looked synthetic.
        //
        // 73 degrees of hue at the empty stop, from 154 where this started. Worth knowing that
        // the untinted green was already leaning teal, so the first third of that journey only
        // brings it to a neutral green -- small warm shifts here do not read as warm at all,
        // they read as green, which is why this is not a gentle tint.
        //
        // THIS IS THE DIRECTION THAT HAS TO BE WATCHED, because brown is dark orange and
        // orange is the fuel gauge. It stops at 73 for that reason: measured against every
        // pair of levels the two bars can be at, this holds dE 51 from fuel, where one more
        // step of warmth drops it to 45 and starts reading as rust rather than as brown.
        //
        // The closest approach also MOVED when the brown went in. Up to olive the worst case
        // was at the full end of both ramps and warming the bottom cost literally nothing;
        // past that it is this stop against fuel at a fifth of a tank, so any further warmth
        // is now paid for directly. Lightness is what keeps the margin: L* 33 here against
        // fuel's darkest of 48.
        //
        // THE TOP END IS FREE, and that is worth knowing before anybody tunes it. Once the
        // bottom went brown the closest approach to fuel moved down there, so every one of
        // these greens measures exactly dE 51 from fuel and 63 from sleep whatever is done to
        // them -- the full end stopped being the binding constraint and can be chosen on looks
        // alone, as long as it stays green. It was a pale mint at chroma 42 and is a vivid
        // green at 66.
        //
        // Never lighter at the bottom than the channel behind it, and never washed out --
        // every point holds a chroma of at least 25, so no part of the range goes grey.

        private static readonly float[] Stops = { 0f, 0.25f, 0.50f, 0.75f, 1f };

        // ORANGE NOW, NOT GREEN. Green went to the health bar when the vitals moved into the
        // row -- a health bar is green to everybody, and two green columns side by side would
        // have been the very confusion this ramp was once moved to avoid. Food is orange the
        // whole way, deep at empty and bright at full: a warm, edible colour, and the one
        // family none of the other four uses -- health runs green to red, armour is blue,
        // energy is yellow, sleep is purple. It still darkens as it empties, so the two habits
        // the eye has learned -- darker is worse, hue says which -- both hold.
        private static readonly Color[] Ramp =
        {
            Color.FromArgb(235, 112,  48,  28),   // empty     deep burnt orange
            Color.FromArgb(235, 166,  72,  36),   // bad       burnt orange
            Color.FromArgb(235, 206,  96,  44),   // middling  orange
            Color.FromArgb(235, 232, 118,  52),   // fine      bright orange
            Color.FromArgb(235, 246, 138,  58)    // full      vivid orange
        };

        // SLEEP IS BLUE AWAKE, DEEP PURPLE EXHAUSTED. It ran sunlight-through-to-night for
        // a while, alongside a sun icon; both are gone and the ramp goes back with them --
        // there is no sun in this meter any more and nothing in it should be gold.
        //
        // It shares nothing with the hunger ramp, which is the point: a glance at colour
        // alone says WHICH meter as well as how it is doing, and neither of them has to be
        // read against the other.
        //
        // BOTH RAMPS DARKEN AS THEY EMPTY NOW, which they did not when this comment first
        // claimed the food ramp stayed bright -- food has since moved into the greens to get
        // clear of the fuel gauge, and it darkens too. That leaves hue carrying the whole
        // distinction between the two meters, which it does easily: green against blue-violet
        // measures dE 72 at the closest approach any two levels can produce, where anything
        // above about 30 is already unmistakable.
        // PURPLE THE WHOLE WAY NOW. The full end used to be an awake blue, and blue is the
        // armour bar's; two slots apart the two read as the same thing at a glance. Sleep is
        // lavender at full and deep purple at empty, and never blue.
        private static readonly Color[] SleepRamp =
        {
            Color.FromArgb(235,  58,  32, 104),   // empty     deep purple
            Color.FromArgb(235,  86,  52, 156),   // bad       purple
            Color.FromArgb(235, 112,  74, 196),   // middling  violet
            Color.FromArgb(235, 134,  96, 222),   // fine      light violet
            Color.FromArgb(235, 156, 120, 240)    // full      lavender
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

                // THE WASTED AND BUSTED SCREENS. The bars stayed up over "WASTED" because the
                // radar is still shown through the first moments of it; the dead and the
                // arrested have no needs on screen. IS_HUD_HIDDEN catches the rest of the game's
                // own reasons to take its HUD away.
                if (Function.Call<bool>(Hash.IS_PLAYER_DEAD, Game.Player.Handle)) return false;
                if (Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, true)) return false;
                if (Function.Call<bool>(Hash.IS_HUD_HIDDEN)) return false;

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
