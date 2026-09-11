using System;
using System.Drawing;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.UI
{
    /// <summary>
    /// The drawing primitives this mod needs, and nothing else.
    ///
    /// No LemonUI, no NativeUI, no menu framework. A GTA scripts\ folder is one shared
    /// assembly-resolution namespace and every UI library in it is a version fight waiting to
    /// happen with somebody else's mod -- and all Bare Minimum ever draws is a rectangle, a
    /// couple of lines of text and some PNGs.
    ///
    /// THE ROUNDED PANEL IS THE ONE SHAPE THAT IS NOT A RECTANGLE, and it is built out of them:
    /// three flats and four quarter-circles stacked from rows. It came across from Hoodrich
    /// with Theme and Kit, so that a panel here is the same panel as a panel there.
    /// </summary>
    internal static class Draw
    {
        // ======================================================================
        // The screen
        // ======================================================================

        /// <summary>Screen aspect, width over height. Re-read twice a second; the resolution can change mid-session.</summary>
        public static float Aspect
        {
            get { Refresh(); return _aspect; }
        }

        /// <summary>Screen height in real pixels. The corner filler has to land its rows on whole pixels.</summary>
        public static int ScreenHeight
        {
            get { Refresh(); return _height; }
        }

        private static float _aspect = 16f / 9f;
        private static int _height = 1080;
        private static int _screenAt;
        private const int ScreenCheckMs = 500;

        private static void Refresh()
        {
            var now = Game.GameTime;
            if (now < _screenAt) return;
            _screenAt = now + ScreenCheckMs;

            try
            {
                var a = GTA.UI.Screen.AspectRatio;
                if (a > 0.5f && a < 6f) _aspect = a;

                var res = GTA.UI.Screen.Resolution;
                if (res.Height > 0) _height = res.Height;
            }
            catch
            {
                // The last good answer stands.
            }
        }

        /// <summary>
        /// A height fraction turned into a width fraction, so a thing comes out SQUARE ON
        /// SCREEN. Sizes are fractions of the screen's own width and height, so equal numbers
        /// give a shape as much wider than it is tall as the screen is -- a third again on a
        /// 21:9.
        /// </summary>
        public static float ToX(float heightFraction)
        {
            return heightFraction / Aspect;
        }

        /// <summary>
        /// Whether the last thing the player touched was a controller rather than a keyboard.
        ///
        /// Every key cap in this mod names a keyboard key, and on a pad every one of them is a
        /// lie. Game.LastInputMethod is the game's own answer -- the same question Rockstar's
        /// scripts ask before choosing between a key name and a button glyph -- and it flips
        /// the instant you touch the other device. Asked four times a second: it is read from
        /// every footer of every open panel and cannot meaningfully change between two frames.
        /// </summary>
        public static bool OnPad
        {
            get
            {
                var now = Game.GameTime;

                if (now >= _padAt)
                {
                    _padAt = now + PadCheckMs;

                    try { _pad = Game.LastInputMethod == InputMethod.GamePad; }
                    catch { /* keyboard, which is what the caps said before this existed */ }
                }

                return _pad;
            }
        }

        private static bool _pad;
        private static int _padAt;
        private const int PadCheckMs = 250;

        // ======================================================================
        // Fonts
        // ======================================================================

        /// <summary>Chalet London: the standard HUD face. Anything meant to be READ.</summary>
        public const int FontBody = 0;

        /// <summary>Chalet Comprime Cologne, condensed: titles, tabs, key caps, anything that has to fit.</summary>
        public const int FontLabel = 4;

        // ======================================================================
        // Rectangles

        // ======================================================================
        // The budget
        // ======================================================================

        /// <summary>
        /// How many rectangles this frame, and the worst frame so far.
        ///
        /// THERE IS A CEILING AND IT IS NOT OURS. The game keeps ONE list of these for the
        /// whole machine and drops whatever is handed to it once that list is full -- so the
        /// script that pays for a busy frame is whichever draws LAST, whatever it drew. A HUD
        /// spending three hundred rectangles on gradients beside the minimap is not a slow
        /// HUD; it is a HUD that takes somebody else's panel off the screen, and that is
        /// exactly what happened: Hoodrich's phone came up in a car with no background at all, because its
        /// screen is a rectangle and this HUD had already spent the list.
        ///
        /// So it is counted, and the peak is logged when it gets worse -- a handful of lines
        /// in a session rather than sixty a second. The frame is told apart by the game's own
        /// frame number, so nothing has to remember to call a Begin.
        /// </summary>
        public static int RectsThisFrame { get; private set; }

        public static int PeakRects { get; private set; }

        /// <summary>Where this install starts dropping them. The real figure is the game's and is not published.</summary>
        private const int Ceiling = 350;

        /// <summary>
        /// WHAT THIS MOD IS ALLOWED TO SPEND IN A FRAME, and why it cannot be more than that.
        ///
        /// THERE IS NO SUCH THING AS OUR OWN LIST. The rectangles come out of ONE list the whole
        /// machine shares -- every script, and the game's own HUD with them -- and it is emptied
        /// once a frame by the engine, not by us. Nothing a script can call partitions it. So a
        /// mod cannot be given a private budget; it can only be held to a small share of the
        /// one budget there is, and that is what this number is: the most this mod will hand
        /// over before it starts leaving things out, chosen well under the ceiling so there is
        /// always room left for whoever draws after us.
        ///
        /// WHAT GOES FIRST IS THE TRIMMING, NEVER THE INSTRUMENT. Past the allowance the specks,
        /// the sheen, the sediment and the stars stop and the bars, their frames and their
        /// levels carry on, because a bar with no sparkle is a bar and half a bar is a bug. See
        /// Room, which is the question every decorative draw in this mod asks first.
        /// </summary>
        public static int Budget = 200;

        /// <summary>
        /// Whether there is room in this frame's share for something that is only decoration.
        ///
        /// Asked BEFORE the work, not before each rectangle: a speck that draws three of them
        /// should not get one and stop.
        ///
        /// ONE ANSWER FOR THE WHOLE FRAME, WHICH IT DID NOT USED TO BE. This was
        /// `RectsThisFrame &lt; Budget` -- a live comparison against a number that grows as the
        /// frame is drawn -- so on a frame that ended over the allowance the decorations asked
        /// EARLY got their rectangles and the ones asked LATE did not, and which is which
        /// shifts with whatever else happens to be on screen that frame. That is not trimming,
        /// it is a different subset of the sparkle appearing every frame at sixty a second,
        /// which is exactly what it looks like: the charge streaks and the bars' relief
        /// flickering while everything solid stays put.
        ///
        /// So the decision is made once, when the frame rolls over, from the frame BEFORE it --
        /// see Latch. Trimming is now all of it or none of it, and a frame's worth of sparkle
        /// vanishing when the screen genuinely gets busy is a thing you can see and understand.
        /// </summary>
        public static bool Room
        {
            get
            {
                Sync();

                // AND THE MACHINE'S ANSWER, which knows what everybody else drew. This mod's
                // own allowance above is one guard; the shared one is the other. See Ledger.
                return !_trim && Ledger.Room;
            }
        }

        /// <summary>How many of this frame's share are left. For anything that wants to draw fewer rather than none.</summary>
        public static int Spare
        {
            get
            {
                Sync();

                var left = Math.Min(Budget - RectsThisFrame, Ledger.Spare);
                return left < 0 ? 0 : left;
            }
        }

        /// <summary>
        /// Rolls the count over when the frame has. Called by the counter and by anybody asking
        /// how much room is left, so a question asked before this frame's first rectangle gets
        /// this frame's answer rather than the last one's.
        /// </summary>
        private static void Sync()
        {
            int frame;

            try { frame = Game.FrameCount; }
            catch { return; }

            if (frame == _frame) return;

            _frame = frame;

            if (RectsThisFrame > PeakRects)
            {
                PeakRects = RectsThisFrame;

                if (PeakRects >= Ceiling)
                {
                    Log.Warn("Draw budget: " + PeakRects + " rectangles in a frame. Past about " +
                             Ceiling + " the game drops the rest of the frame's -- for every " +
                             "script on the machine, not only this one.");
                }
                else
                {
                    Log.Info("Draw budget: " + PeakRects + " rectangles in a frame (new peak).");
                }
            }

            Latch();

            RectsThisFrame = 0;
        }

        /// <summary>
        /// Decides whether the frame about to start draws its decoration, from the one that
        /// just finished.
        ///
        /// HYSTERESIS, OR IT WOULD OSCILLATE INSTEAD OF FLICKERING. Trimming makes the count
        /// drop -- that is its whole job -- so a plain "over the line, trim; under it, do not"
        /// turns on and off on alternate frames the moment the untrimmed load sits above the
        /// allowance. It goes off above the allowance and does not come back until well under
        /// it, and the band is wide enough to hold the cost of the decoration itself.
        ///
        /// AND IT STAYS OFF FOR A WHILE ONCE IT GOES. A busy screen is busy for longer than one
        /// frame -- a menu opening, a phone coming up, a cutscene bar -- and something that
        /// came back the instant it was allowed to would still blink on the way in and out of
        /// every one of them. Half a second is long enough that the eye reads it as the sparkle
        /// having stopped rather than as the sparkle faulting.
        /// </summary>
        private static void Latch()
        {
            if (_hold > 0) { _hold--; return; }

            if (RectsThisFrame > Budget)
            {
                _trim = true;
                _hold = HoldFrames;
                return;
            }

            if (RectsThisFrame < Budget * 4 / 5) _trim = false;
        }

        /// <summary>Whether decoration is off this frame. One answer for the whole frame. See Room.</summary>
        private static bool _trim;

        /// <summary>Frames left before the latch will look again.</summary>
        private static int _hold;

        private const int HoldFrames = 30;

        private static int _frame;

        private static void Counted()
        {
            Sync();
            RectsThisFrame++;
            Ledger.Count();
        }

        /// <summary>
        /// The screen's height in pixels, for the sub-pixel test.
        ///
        /// THE SAME NUMBER ScreenHeight ALREADY KEEPS, rather than a second copy read once and
        /// held for the session. ScreenHeight's own note says the resolution can change while
        /// the game is running and the text-advance cache is thrown away when it does; this was
        /// the one place that did not care, so changing from 4K to 1080p left the cull threshold
        /// at half its proper size and every row now under half a pixel was still handed to the
        /// game -- spending budget on rows nobody can see.
        /// </summary>
        private static int Tall
        {
            get { return ScreenHeight; }
        }

        /// <summary>
        /// A filled rectangle, positioned by its CENTRE.
        ///
        /// That is DRAW_RECT's own convention and it is worth stating, because every other
        /// coordinate in a HUD is a corner and getting it wrong shifts everything by half its
        /// own size -- which looks like a rounding error rather than a mistake.
        /// </summary>
        public static void Rect(float centreX, float centreY, float width, float height, Color colour)
        {
            // NOTHING UNDER HALF A PIXEL. A band a fifth of a pixel tall is not a faint line,
            // it is nothing at all -- and it costs exactly as much of the frame's one list of
            // rectangles as a band you can see. See Counted for who pays.
            if (height * Tall < 0.5f) return;

            try
            {
                Counted();

                Function.Call(Hash.DRAW_RECT, centreX, centreY, width, height,
                              colour.R, colour.G, colour.B, colour.A, false);
            }
            catch (Exception ex)
            {
                Log.Once("draw-rect", "DRAW_RECT failed: " + ex.Message);
            }
        }

        /// <summary>A rectangle drawn from its top-left, which is how a bar is actually thought about.</summary>
        public static void Bar(float left, float top, float width, float height, Color colour)
        {
            if (colour.A <= 0 || width <= 0f || height <= 0f) return;

            Rect(left + width / 2f, top + height / 2f, width, height, colour);
        }

        /// <summary>
        /// A rectangle with round corners, from rows: three flats and four quarter-circles.
        ///
        /// THE CORNERS ARE ONLY THE QUARTER THAT IS MISSING. A whole disc under each corner
        /// would land three quarters of itself on fill that is already there, and two coats of
        /// a translucent colour is darker than one -- a blotch at every corner of every panel.
        /// Filling only the ground no flat reaches lays the colour down exactly once, so a
        /// translucent panel is one shade all over and fades as one thing.
        /// </summary>
        public static void RoundRect(float left, float top, float w, float h, float r, Color c, int steps = 40)
        {
            if (c.A <= 0 || w <= 0f || h <= 0f) return;

            r = Math.Max(0f, Math.Min(r, h * 0.5f));

            var rX = ToX(r);

            // A corner cannot be wider than half the box, or the two sides cross over.
            if (rX * 2f > w)
            {
                Bar(left, top, w, h, c);
                return;
            }

            Bar(left, top + r, w, h - r * 2f, c);
            Bar(left + rX, top, w - rX * 2f, r, c);
            Bar(left + rX, top + h - r, w - rX * 2f, r, c);

            var band = Bands(r, steps);

            Quarter(left + rX, top + r, r, c, band, false, false);
            Quarter(left + w - rX, top + r, r, c, band, true, false);
            Quarter(left + rX, top + h - r, r, c, band, false, true);
            Quarter(left + w - rX, top + h - r, r, c, band, true, true);
        }

        /// <summary>
        /// The body of a panel: the rounded black, and a stripe along its top if the accent
        /// has any alpha. The stripe is INSET by the corner radius -- it is a square bar laid
        /// over a rounded shape, and at full width its ends hang off into the space the
        /// rounding just removed.
        /// </summary>
        public static void Panel(float left, float top, float w, float h, Color body, Color accent)
        {
            RoundRect(left, top, w, h, PanelRound, body, PanelSteps);

            if (accent.A <= 0) return;

            var inset = ToX(PanelRound);
            Bar(left + inset, top, w - inset * 2f, PanelStripe, accent);
        }

        /// <summary>
        /// How round a panel corner is. Matched to Hoodrich's panels rather than picked, so a
        /// panel here is the same shape as a panel there.
        /// </summary>
        public const float PanelRound = 0.018f;
        public const float PanelStripe = 0.0028f;

        /// <summary>How finely a panel corner is stepped. Forty is about a pixel a band at this radius.</summary>
        private const int PanelSteps = 40;

        /// <summary>
        /// How tall each band of a stacked corner is, in screen pixels. Returns a HEIGHT, not
        /// a count -- getting those the wrong way round gives a corner of two enormous steps.
        /// </summary>
        private static int Bands(float r, int steps)
        {
            if (steps <= 0) return 1;

            // THE COUNT IS WHAT COSTS, NOT THE HEIGHT. This returned a band height rounded to
            // the nearest pixel, so on a taller screen the same corner was drawn in more bands:
            // at a radius of 0.018 and forty steps it is nineteen rows at 1080p and twenty-six
            // at 1440p, and a panel went from about seventy-nine rectangles to a hundred and
            // seven for no visible gain. Rounding UP the height of one band instead fixes the
            // count at half the step number on every screen, which is what the number was
            // always meant to mean. Rectangles come out of one list the whole machine shares.
            return Math.Max(1, (int)Math.Ceiling(r * ScreenHeight / (steps * 0.5f)));
        }

        /// <summary>
        /// One corner as the quarter of a circle that is actually missing, in rows. Anchored
        /// on the centre row, so the two quadrants of a side meet on a row boundary rather than
        /// half a row apart with a line of ground showing through.
        /// </summary>
        private static void Quarter(float cx, float cy, float r, Color c, int rowPx, bool right, bool below)
        {
            if (r <= 0f || c.A <= 0) return;

            var r2 = r * r;
            var rows = Math.Max(1, rowPx);

            var screenH = ScreenHeight;

            var pxCentre = (int)Math.Round(cy * screenH);

            var pxTop = below ? pxCentre : (int)Math.Floor((cy - r) * screenH);
            var pxBottom = below ? (int)Math.Ceiling((cy + r) * screenH) : pxCentre;

            pxTop -= ((pxTop - pxCentre) % rows + rows) % rows;

            var rowHeight = rows / (float)screenH;

            for (var py = pxTop; py < pxBottom; py += rows)
            {
                var rowY = (py + rows * 0.5f) / screenH;

                // The row's far edge from the equator rather than its centre: the difference
                // between a clean arc and a row of notches.
                var edge = Math.Abs(cy - rowY) + rowHeight * 0.5f;
                var e2 = edge * edge;
                if (e2 >= r2) continue;

                var half = (float)Math.Sqrt(r2 - e2);
                if (half <= 0f) continue;

                var w = ToX(half);
                Rect(right ? cx + w * 0.5f : cx - w * 0.5f, rowY, w, rowHeight, c);
            }
        }

        // ======================================================================
        // Pictures
        // ======================================================================

        /// <summary>
        /// A PNG from the icon folder, centred on a point, SQUARE ON SCREEN at a height. True
        /// if it drew. The file is looked up through IconCache, so a picture asked for every
        /// frame costs one texture for the session rather than one a frame.
        /// </summary>
        public static bool Sprite(string file, float centreX, float centreY, float heightFraction, Color tint)
        {
            var icon = IconCache.Get(file);
            if (icon == null || icon.Missing) return false;

            icon.DrawSized(centreX, centreY, ToX(heightFraction), heightFraction, tint);

            return !icon.Missing;
        }

        // ======================================================================
        // Text
        // ======================================================================

        /// <summary>
        /// One line of text.
        ///
        /// ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME is the right component despite the name:
        /// it is the one that takes a literal string rather than a label from the game's text
        /// table. It also has a hard limit of 99 characters, so anything longer is cut here
        /// rather than silently drawing nothing at all.
        ///
        /// THE WRAP REGION AND THE JUSTIFICATION ARE SET ON EVERY CALL, and put back after a
        /// right-aligned one. Both are sticky across draws: a right-aligned price left a
        /// region ending at its own right edge, and the next left-aligned label inherited it
        /// and re-flowed inside it -- text wandering out of its box for no visible reason.
        /// </summary>
        public static void Text(string text, float x, float y, float scale, Color colour,
                                int font = FontLabel, bool centre = false, bool rightAlign = false,
                                bool outline = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (text.Length > 99) text = text.Substring(0, 99);

            try
            {
                Function.Call(Hash.SET_TEXT_FONT, font);
                Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
                Function.Call(Hash.SET_TEXT_COLOUR, colour.R, colour.G, colour.B, colour.A);

                // BOTH OF THESE DRAW IN BLACK, which is fine on light text over a dark panel
                // and actively harmful on dark text over a light one: a black outline round
                // black digits fills in the holes in 8, 9 and 0 until all three are one blob.
                if (outline)
                {
                    Function.Call(Hash.SET_TEXT_DROP_SHADOW);
                    Function.Call(Hash.SET_TEXT_OUTLINE);
                }

                Function.Call(Hash.SET_TEXT_CENTRE, centre);

                // 0 centre, 1 left, 2 right. A right-justified string is laid out against the
                // RIGHT edge of the wrap window, so that edge is the x asked for.
                Function.Call(Hash.SET_TEXT_JUSTIFICATION, rightAlign ? 2 : centre ? 0 : 1);
                Function.Call(Hash.SET_TEXT_WRAP, 0f, rightAlign ? x : 1f);

                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y, 0);

                if (rightAlign)
                {
                    Function.Call(Hash.SET_TEXT_JUSTIFICATION, 0);
                    Function.Call(Hash.SET_TEXT_WRAP, 0f, 1f);
                }
            }
            catch (Exception ex)
            {
                Log.Once("draw-text", "Text drawing failed: " + ex.Message);
            }
        }

        /// <summary>
        /// A line drawn one character at a time, with a gap put between them by hand.
        /// Returns how wide it ended up.
        /// </summary>
        ///
        /// <remarks>
        /// BECAUSE THE GAME HAS NO LETTER-SPACING. There is no native for tracking, and it is
        /// the one thing that separates a sign from a label -- every painted shopfront in the
        /// world has air between its capitals and every UI label has none. A title set solid
        /// in the condensed face reads as a field name however large it is drawn.
        ///
        /// The cost is one draw and one measure per character instead of one of each. A title
        /// is a dozen characters on a panel that is already drawing tiles and rules, so it is
        /// nothing next to the rectangle budget -- but it is why this is for titles and not
        /// for anything with a paragraph in it.
        ///
        /// A CHARACTER THAT MEASURES AS NOTHING IS A SPACE, and is stepped over rather than
        /// drawn. The measure native is asked to include spaces and does, but a zero coming
        /// back would otherwise collapse every word in the line into the one before it, which
        /// is a worse failure than a slightly wide gap.
        /// </remarks>
        public static float TextTracked(string text, float x, float y, float scale, Color colour,
                                        int font, float track, bool outline = true)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            var at = x;
            var blank = Height(scale, font) * 0.26f;

            foreach (var ch in text)
            {
                var wide = Advance(ch, scale, font);

                if (wide <= 0f) wide = blank;
                else Text(ch.ToString(), at, y, scale, colour, font, false, false, outline);

                at += wide + track;
            }

            // No trailing gap: the width is up to the right edge of the last letter, not past
            // it, or every measurement built on this would be one space too long.
            return at - x - track;
        }

        /// <summary>How wide TextTracked would draw that line, without drawing it.</summary>
        public static float WidthTracked(string text, float scale, int font, float track)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            var total = 0f;
            var blank = Height(scale, font) * 0.26f;

            foreach (var ch in text)
            {
                var wide = Advance(ch, scale, font);
                total += (wide <= 0f ? blank : wide) + track;
            }

            return total - track;
        }

        /// <summary>
        /// How wide one character is, asked for once and then remembered.
        /// </summary>
        ///
        /// <remarks>
        /// WITHOUT THIS THE HEAD MEASURED ITS TITLE THREE TIMES A FRAME -- once to decide
        /// whether the name fitted, once for the amber pass and once for the white one -- so
        /// eleven letters were thirty-three measure natives per frame for a string that had
        /// not changed since the shop opened. A letter's width in a given face at a given size
        /// is a constant, and this is where that gets used.
        ///
        /// THROWN AWAY IF THE WINDOW CHANGES. These come back as a fraction of the SCREEN, not
        /// in pixels, so an alt-tab into a different resolution moves every one of them. It is
        /// one float comparison to be right about that, against a cache that would otherwise
        /// quietly mis-space every title for the rest of the session.
        /// </remarks>
        private static float Advance(char ch, float scale, int font)
        {
            var aspect = Aspect;

            if (aspect != _advanceAspect)
            {
                Advances.Clear();
                _advanceAspect = aspect;
            }

            // Scale is quantised to a thousandth, which is finer than any difference the eye
            // could find and coarse enough that a shrunk-to-fit title does not mint a fresh
            // entry per frame as it settles.
            var key = ((long)font << 48) | ((long)(int)(scale * 1000f) << 24) | ch;

            float wide;
            if (Advances.TryGetValue(key, out wide)) return wide;

            wide = Width(ch.ToString(), scale, font);
            Advances[key] = wide;

            return wide;
        }

        private static readonly System.Collections.Generic.Dictionary<long, float> Advances =
            new System.Collections.Generic.Dictionary<long, float>();

        private static float _advanceAspect;

        /// <summary>Text ending at a right edge. What every figure on the right of a row uses.</summary>
        public static void TextRight(string text, float rightX, float y, float scale, Color colour,
                                     int font = FontBody, bool outline = true)
        {
            Text(text, rightX, y, scale, colour, font, false, true, outline);
        }

        /// <summary>
        /// How TALL a line of text is at a given scale, as a fraction of the screen.
        ///
        /// Asked for rather than worked out. Placing text by its bottom edge means subtracting
        /// its height from where the foot should sit, and END_TEXT_COMMAND_DISPLAY_TEXT takes
        /// the TOP -- so a wrong height is a number drawn half out of the bar, which reads as a
        /// positioning bug rather than as a bad constant. The game knows this one, so it gets
        /// asked.
        /// </summary>
        public static float Height(float scale, int font = FontLabel)
        {
            try
            {
                return Function.Call<float>(Hash.GET_RENDERED_CHARACTER_HEIGHT, scale, font);
            }
            catch (Exception ex)
            {
                Log.Once("draw-height", "Could not measure text height: " + ex.Message +
                                        " - estimating it instead.");
                return scale * 0.035f;
            }
        }

        /// <summary>
        /// How wide a string will be, as a fraction of the screen.
        ///
        /// The font and scale have to be set BEFORE the measuring command begins, exactly as
        /// they do before drawing -- the game measures with whatever is currently selected,
        /// not with anything passed to the measure call. Getting that order wrong returns the
        /// width the string would have had in the previous font, which is a very quiet way to
        /// mis-centre a line.
        /// </summary>
        public static float Width(string text, float scale, int font = FontLabel)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            if (text.Length > 99) text = text.Substring(0, 99);

            try
            {
                Function.Call(Hash.SET_TEXT_FONT, font);
                Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);

                Function.Call(Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);

                return Function.Call<float>(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, true);
            }
            catch (Exception ex)
            {
                Log.Once("text-width", "Could not measure text: " + ex.Message);
                return 0f;
            }
        }

        // ======================================================================
        // The game's own help box
        // ======================================================================

        /// <summary>
        /// The game's own help box, top left.
        ///
        /// Used rather than drawn text for anything that is an INSTRUCTION, because this is
        /// where the player already looks for one, and because it is the only place where
        /// ~INPUT_...~ resolves to the button they have actually got bound.
        /// </summary>
        public static void Help(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP, "STRING");

                // FED IN CHUNKS RATHER THAN TRUNCATED. One text component takes at most 99
                // characters, and this used to just cut the string there -- which is fine for
                // a sentence and ruinous for a prompt, because a cut landing inside a
                // ~INPUT_CONTEXT~ tag leaves half a tag on screen as literal tildes and drops
                // the button glyph entirely.
                //
                // Chunks are split on SPACES, which is what makes it safe: a formatting tag
                // never contains one, so no split can ever land inside a tag.
                foreach (var chunk in Chunks(text, 96))
                {
                    Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, chunk);
                }

                // playSound = FALSE. This is called every frame for as long as a prompt is
                // on screen, and with the sound on that is the help chime sixty times a
                // second for as long as you stand near a pump.
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP, 0, false, false, -1);
            }
            catch (Exception ex)
            {
                Log.Once("draw-help", "Help text failed: " + ex.Message);
            }
        }

        /// <summary>Splits on spaces into pieces no longer than the limit. Never splits a ~tag~.</summary>
        private static System.Collections.Generic.List<string> Chunks(string text, int limit)
        {
            var pieces = new System.Collections.Generic.List<string>();
            var current = "";

            foreach (var word in text.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;

                if (candidate.Length <= limit) { current = candidate; continue; }

                if (current.Length > 0) pieces.Add(current);

                // A single word longer than the limit can only be cut, but at least it is cut
                // here and not through the middle of the sentence.
                current = word.Length <= limit ? word : word.Substring(0, limit);
            }

            if (current.Length > 0) pieces.Add(current);
            return pieces;
        }

        /// <summary>Clears a help box early, so a prompt does not linger after you walk away.</summary>
        public static void ClearHelp()
        {
            try { Function.Call(Hash.CLEAR_ALL_HELP_MESSAGES); }
            catch { /* nothing to do about it */ }
        }
    }
}
