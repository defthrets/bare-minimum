using System;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// Where the strip goes: the three bars lying under the minimap, in fractions of the
    /// screen -- and where the minimap itself is, for the frame drawn round it.
    ///
    /// ASKED OF THE GAME, THE WAY THE GAME ASKS ITSELF. The minimap is laid out by
    /// common:/data/ui/frontend.xml -- anchored to the bottom-left of the safe zone, 0.150 by
    /// 0.188888 with an offset of (-0.0045, 0.002), in the game's own aligned units -- and
    /// SET_SCRIPT_GFX_ALIGN with GET_SCRIPT_GFX_ALIGN_POSITION is the native that turns those
    /// units into screen fractions for THIS screen and THIS safe-zone slider. It is the same
    /// arithmetic the HUD runs, so the answer is right on 16:9, on 21:9 and on whatever the
    /// player set the safe zone to, without a formula here guessing at any of it.
    ///
    /// Inside that box the VISIBLE map is a little smaller. Measured at 2560x1440 with the
    /// safe zone at maximum, the map is 360 by 256 pixels where the box is 384 by 272, and it
    /// sits on the safe-zone line: the health and armour strip is the bottom of those 256,
    /// flush with the line, with the map proper above it and a hair of clear air between.
    ///
    /// Auto can still be wrong: anything that has resized the radar has moved the goalposts
    /// and the game does not know. Auto off puts the numbers in the ini instead, and Compare
    /// draws the game's strip back over ours so the two can be lined up by eye.
    /// </summary>
    internal sealed class Layout
    {
        // The minimap in frontend.xml: alignment L,B; posX, posY; sizeX, sizeY.
        private const float BoxX = -0.0045f;
        private const float BoxY = 0.002f;
        private const float BoxW = 0.150f;
        private const float BoxH = 0.188888f;

        /// <summary>How far inside the box the visible map starts, left and right: 12 of 384 pixels.</summary>
        private const float MapInset = 12f / 384f;

        /// <summary>The visible map's height, strip included: 256 of 1440 pixels.</summary>
        public const float MapTall = 256f / 1440f;

        /// <summary>The visible map's width, and how far inside the box it starts, as fractions of screen HEIGHT: 360 and 12 of 1440.</summary>
        public const float MapWide = 360f / 1440f;
        public const float MapEdge = 12f / 1440f;

        /// <summary>The stock strip's own height, as a fraction of screen height: ten pixels at 1440.</summary>
        public const float StockThick = 10f / 1440f;

        /// <summary>Clear air between the foot of the map and the top of the stock strip.</summary>
        public const float StockGap = 4f / 1440f;

        // Alignment characters, as the native takes them.
        private const int AlignLeft = 76;    // 'L'
        private const int AlignBottom = 66;  // 'B'

        // BY HASH RATHER THAN BY NAME. These three have been called different things in
        // different builds of the native list -- GET_SCRIPT_GFX_ALIGN_POSITION was
        // _GET_SCRIPT_GFX_POSITION for years -- and a name that is not in the vendored 3.6.0
        // enum is a build error. The hash has never changed.
        private static readonly Hash SetAlign = (Hash)0xB8A850F20A067EB6UL;
        private static readonly Hash ResetAlign = (Hash)0xE3A3DB414A373DABUL;
        private static readonly Hash AlignPosition = (Hash)0x6DD8F5AA635EB4B2UL;

        public struct Segment
        {
            public float Left;
            public float Width;
        }

        /// <summary>The whole strip, top-left and size.</summary>
        public float Left;
        public float Top;
        public float Width;
        public float Thick;

        public Segment Health;
        public Segment Armour;
        public Segment Third;
        public bool HasThird;

        /// <summary>Where the game's own strip is believed to be, for the log and the compare view.</summary>
        public float StockLeft;
        public float StockTop;
        public float StockWidth;

        /// <summary>Whether the game answered when asked where its minimap was.</summary>
        public bool Anchored;

        public static Layout Compute(Settings cfg, bool showThird)
        {
            var lay = new Layout();
            var aspect = Ink.Aspect;

            float mapLeft, mapWidth, safeBottom;
            float boxLeft = 0f, boxBottom = 0f, boxRight = 0f, boxTop = 0f;

            var anchored = cfg.VitalsStripAuto && Anchor(out boxLeft, out boxBottom, out boxRight, out boxTop);

            if (anchored)
            {
                var boxW = boxRight - boxLeft;

                mapLeft = boxLeft + boxW * MapInset;
                mapWidth = boxW * (1f - 2f * MapInset);

                // The box hangs BoxY below the safe-zone line; the strip sits ON the line.
                safeBottom = boxBottom - BoxY;
            }
            else
            {
                mapLeft = cfg.VitalsStripX;
                mapWidth = cfg.VitalsStripWidth / aspect;
                safeBottom = cfg.VitalsStripY;
            }

            lay.Anchored = anchored;

            lay.StockLeft = mapLeft;
            lay.StockWidth = mapWidth;
            lay.StockTop = safeBottom - StockThick;

            // THE THICKNESS IS A WIDTH FRACTION, so that it is the same number as the bars'
            // BarWidth and the two come out the same number of pixels on any screen. Turned
            // into a height fraction here, once, for drawing.
            lay.Thick = cfg.VitalsStripThickness * aspect;

            lay.Left = mapLeft + cfg.VitalsStripOffsetX;
            lay.Width = mapWidth * cfg.VitalsStripWidthScale;

            // TOP-ANCHORED TO THE STOCK STRIP, growing downward, so the clear air between the
            // map and the bars is the game's own. A thicker bar therefore hangs below the
            // safe-zone line by the difference, which is fine everywhere except a safe zone
            // at maximum, where the line IS the screen edge -- so the bottom is held on
            // screen, and the strip climbs instead in that one case.
            lay.Top = lay.StockTop + cfg.VitalsStripOffsetY;

            var floor = 1f - 1f / Math.Max(720f, Ink.ScreenHeight);
            if (lay.Top + lay.Thick > floor) lay.Top = floor - lay.Thick;

            // ---- the three bars ----

            lay.HasThird = showThird;

            var gap = cfg.VitalsStripGap * lay.Width;

            var hs = Math.Max(0.01f, cfg.VitalsHealthShare);
            var ar = 0f;   // no armour segment: armour is a state of the health bar
            var sp = showThird ? Math.Max(0.01f, cfg.VitalsThirdShare) : 0f;

            var total = hs + ar + sp;
            var bars = showThird ? 2 : 1;
            var room = lay.Width - gap * (bars - 1);

            if (room < 0.001f) room = 0.001f;

            var x = lay.Left;

            lay.Health.Left = x;
            lay.Health.Width = room * hs / total;
            x += lay.Health.Width + gap;

            // Kept as a zero-width segment so nothing that names it has to change.
            lay.Armour.Left = x;
            lay.Armour.Width = 0f;

            if (showThird)
            {
                lay.Third.Left = x;
                lay.Third.Width = room * sp / total;
            }

            return lay;
        }

        /// <summary>
        /// The VISIBLE map, not the box, in screen fractions, and the safe-zone line it stands on.
        ///
        /// THE BOX'S LEFT IS RIGHT AND ITS WIDTH IS NOT. The alignment maths anchors the box to
        /// the safe zone's corner correctly on any screen, but its width comes out in units
        /// that stretch with the aspect: on 21:9 the box read a third too wide and the frame ran
        /// under the bars. The radar itself is sized off screen HEIGHT -- 360 by 256 pixels on
        /// any 1440-tall screen, starting 12 in from the box -- so the left edge is taken from
        /// the box and everything else from the height. False when the game will not say where
        /// the box is.
        /// </summary>
        /// <summary>
        /// How tall a line of writing in the plate is, as a fraction of screen height.
        ///
        /// HERE RATHER THAN IN Frame, because the row of bars has to end on the same line the
        /// plate does and the plate's height is half of what decides where that line is. Two
        /// copies of this number is two things that agree until somebody edits one.
        /// </summary>
        public const float LabelH = 0.021f;

        /// <summary>
        /// THE LINE THE WHOLE HUD ENDS ON: the map's bottom edge, plus a plate deep enough to
        /// write in, never above the safe zone's line and never off the bottom of the screen.
        ///
        /// The frame's plate does not climb into the map any more -- the game draws its blips
        /// over anything we draw, so a plate overlapping the map is a plate with blips on it --
        /// which means the foot is decided by where the map ends, not by where the safe zone is.
        /// The row of bars reads the same function so the two end on one line without either
        /// knowing what the other is doing. False when the game will not say where the map is.
        /// </summary>
        public static bool Foot(float plateH, float drop, out float foot)
        {
            foot = 0f;

            float l, top, r, bottom, safeLine;
            if (!Map(out l, out top, out r, out bottom, out safeLine)) return false;

            foot = Math.Max(safeLine, bottom + Math.Max(0f, drop) + Math.Max(LabelH, plateH));

            var floor = 1f - 1f / Math.Max(720f, Ink.ScreenHeight);
            if (foot > floor) foot = floor;

            return true;
        }

        /// <summary>
        /// THE OUTSIDE OF THE MINIMAP FRAME, which is what anything standing beside it has to
        /// clear -- not the map, which is a good deal narrower.
        ///
        /// FRAME.DRAW OWNS THESE TWO EXPRESSIONS AND THIS IS THEM, moved here so the row that
        /// stands next to the frame and the frame itself cannot disagree about where it ends.
        /// They are not obvious: the line is drawn OUTSIDE a gap that is itself outside the
        /// map, so the frame reaches a gap plus a line-width past the map on each side -- and
        /// on the left the gap gives way first, because with the safe zone at its widest the
        /// map sits four pixels from the edge of the screen and a frame drawn outside that
        /// falls off it.
        ///
        /// False when the map cannot be found, and then the caller has nothing to stand beside.
        /// </summary>
        public static bool Outer(float edge, float frameGap, out float left, out float right)
        {
            left = right = 0f;

            float l, top, r, bottom, safeLine;
            if (!Map(out l, out top, out r, out bottom, out safeLine)) return false;

            var gapW = Math.Max(0f, frameGap) / Ink.Aspect;

            var leftGapW = Math.Min(gapW, Math.Max(0f, l - edge));

            left = Math.Max(0f, l - leftGapW - edge);
            right = r + gapW + edge;

            return true;
        }

        /// <summary>
        /// THE FRAME'S TWO INNER LINES: the top of the band above the map, and the top of the
        /// plate below it. What anything standing beside the frame has to reach between.
        ///
        /// FRAME.DRAW OWNS THESE EXPRESSIONS AND THIS IS THEM, here for the same reason Outer
        /// is: the row that stands next to the frame and the frame itself must not each work
        /// out where it ends. That has already gone wrong twice in this pair -- the row's foot
        /// and the frame's plate, and then the gauge's side and the frame's edge -- and both
        /// times the symptom was one of them moving and the other not.
        ///
        /// The band is not a thin line and its height is not a setting alone: the game draws
        /// its blips over anything a script draws, and a blip clamped to the top edge of the
        /// map hangs half above it, so the band has to rise clear of where one can reach before
        /// there is anywhere safe to write. Hence the max. Frame's own comment has the whole of
        /// that argument.
        ///
        /// False when the map cannot be found, and then there is nothing to line up with.
        /// </summary>
        public static bool Frame(float edge, float bandHeight, float frameGap, float drop,
                                 bool wantPlate, out float bandTop, out float plateTop)
        {
            bandTop = plateTop = 0f;

            float l, mapTop, r, mapBottom, safeLine;
            if (!Map(out l, out mapTop, out r, out mapBottom, out safeLine)) return false;

            var edgeH = edge * Ink.Aspect;

            var topGap = Math.Min(Math.Max(0f, frameGap), Math.Max(0f, mapTop - edgeH));
            var bandH = Math.Max(bandHeight, topGap + edgeH);

            bandTop = Math.Max(0f, mapTop - bandH);
            plateTop = mapBottom + (wantPlate ? Math.Max(0f, drop) : 0f);

            return plateTop > bandTop;
        }

        /// <summary>Last frame's answer, and when it is worth asking again. See Map.</summary>
        private static bool _cached;
        private static bool _cachedOk;
        private static float _cl, _ct, _cr, _cb, _cs;
        private static int _askAt;

        public static bool Map(out float left, out float top, out float right, out float bottom, out float safeLine)
        {
            // ASKED A FEW TIMES A SECOND, NOT THREE OR FOUR TIMES A FRAME.
            //
            // Anchor costs four natives and, worse, MUTATES the script's global draw alignment
            // to get its answer. Between the cash readout, the minimap's frame, the row the
            // bars stand in and the row published for Fumes, this was being asked three or four
            // times every frame -- about sixteen alignment natives a frame -- for a number that
            // changes only when the resolution or the safe-zone slider does.
            //
            // The same query is already cached on a 500 ms clock a few classes away, with a
            // comment saying there is no reason to spend it, and Ink does the same for the
            // screen size. This is that, in the one place every caller comes through.
            var now = Environment.TickCount & int.MaxValue;

            if (_cached && now < _askAt)
            {
                left = _cl; top = _ct; right = _cr; bottom = _cb; safeLine = _cs;
                return _cachedOk;
            }

            _cached = true;
            _askAt = now + 250;

            left = top = right = bottom = safeLine = 0f;

            float boxLeft, boxBottom, boxRight, boxTop;

            if (!Anchor(out boxLeft, out boxBottom, out boxRight, out boxTop))
            {
                _cachedOk = false;
                _cl = _ct = _cr = _cb = _cs = 0f;
                return false;
            }

            var aspect = Ink.Aspect;

            left = boxLeft + MapEdge / aspect;
            right = left + MapWide / aspect;

            safeLine = boxBottom - BoxY;
            bottom = safeLine - StockThick - StockGap;
            top = safeLine - MapTall;

            _cachedOk = true;
            _cl = left; _ct = top; _cr = right; _cb = bottom; _cs = safeLine;

            return true;
        }

        /// <summary>
        /// The minimap's box in screen fractions, from the game's own alignment maths.
        ///
        /// False rather than a guess when the answer is not plausible -- a box in the right
        /// half of the screen, or a box wider than half of it -- because the caller has a
        /// perfectly good manual position to fall back on and a plausible-looking wrong answer
        /// is the one nobody would question.
        /// </summary>
        public static bool Anchor(out float left, out float bottom, out float right, out float top)
        {
            left = bottom = right = top = 0f;

            try
            {
                Function.Call(SetAlign, AlignLeft, AlignBottom);

                using (var ox = new OutputArgument())
                using (var oy = new OutputArgument())
                {
                    Function.Call(AlignPosition, BoxX, BoxY, ox, oy);
                    left = ox.GetResult<float>();
                    bottom = oy.GetResult<float>();
                }

                using (var ox = new OutputArgument())
                using (var oy = new OutputArgument())
                {
                    Function.Call(AlignPosition, BoxX + BoxW, BoxY - BoxH, ox, oy);
                    right = ox.GetResult<float>();
                    top = oy.GetResult<float>();
                }

                Function.Call(ResetAlign);
            }
            catch (Exception ex)
            {
                try { Function.Call(ResetAlign); } catch { /* nothing more to do */ }

                Log.Once("vitals-anchor", "Could not ask the game where the minimap is: " + ex.Message +
                                          " - using [VitalsStrip] X, Y and Width instead.");
                return false;
            }

            var w = right - left;
            var h = bottom - top;

            if (left < -0.05f || left > 0.5f || w < 0.05f || w > 0.6f ||
                bottom < 0.5f || bottom > 1.05f || h < 0.05f || h > 0.6f)
            {
                Log.Once("vitals-anchor-odd", "The game put the minimap at " + left.ToString("0.000") + "," +
                                              top.ToString("0.000") + " size " + w.ToString("0.000") + "x" +
                                              h.ToString("0.000") + ", which is not plausible - using [VitalsStrip] X, Y " +
                                              "and Width instead.");
                return false;
            }

            return true;
        }

        /// <summary>One line, in pixels, for the log.</summary>
        public string Describe()
        {
            var pw = Ink.ScreenWidth;
            var ph = Ink.ScreenHeight;

            return "strip " + (Left * pw).ToString("0") + "," + (Top * ph).ToString("0") +
                   " size " + (Width * pw).ToString("0") + "x" + (Thick * ph).ToString("0") + " px" +
                   " (health " + (Health.Width * pw).ToString("0") +
                   ", armour " + (Armour.Width * pw).ToString("0") +
                   (HasThird ? ", third " + (Third.Width * pw).ToString("0") : ", no third") +
                   "); stock strip " + (StockLeft * pw).ToString("0") + "," + (StockTop * ph).ToString("0") +
                   " size " + (StockWidth * pw).ToString("0") + "x" + (StockThick * ph).ToString("0") + " px" +
                   "; screen " + pw + "x" + ph + ", aspect " + Ink.Aspect.ToString("0.00") +
                   ", " + (Anchored ? "anchored to the game's minimap" : "from [VitalsStrip] X, Y and Width");
        }
    }
}
