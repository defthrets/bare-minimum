using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using BareMinimum.Core;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.UI
{
    /// <summary>One line in a menu.</summary>
    internal sealed class Row
    {
        public string Left = "";

        /// <summary>Right-aligned: a price, a value, an on/off.</summary>
        public string Right = "";

        /// <summary>Shown in the strip under the list while this row is selected.</summary>
        public string Note = "";

        /// <summary>
        /// A heading rather than a setting: a line that names the group under it.
        ///
        /// SKIPPED BY THE SELECTION ENTIRELY, which is the whole reason it is a flag on the
        /// row rather than just a greyed entry. A heading you can land on is a keypress that
        /// does nothing, and by the fourth group that is four dead presses between the top of
        /// the list and the bottom.
        /// </summary>
        public bool Header;

        /// <summary>A greyed row can be selected and read, but not activated.</summary>
        public bool Enabled = true;

        /// <summary>
        /// A picture for this row, by FILE NAME rather than by Icon.
        ///
        /// A name, because an Icon owns a texture handle and the rows are rebuilt every time
        /// the selection moves -- see IconCache. A row holding its own Icon would leak one
        /// handle per row per keypress.
        /// </summary>
        public string IconFile = "";

        /// <summary>What colour to draw it. The products are white art, tinted per item.</summary>
        public Color IconTint = Color.FromArgb(255, 235, 235, 240);

        /// <summary>Whatever the owner needs back when this row is chosen.</summary>
        public object Tag;
    }

    /// <summary>
    /// A list menu with tabs, drawn from rectangles and text.
    ///
    /// HAND-ROLLED, and LemonUI is sitting in the same scripts\ folder unused. That is
    /// deliberate and it is the same rule the rest of this codebase follows: a GTA scripts\
    /// folder is ONE assembly-resolution namespace shared by every mod in it, so taking a
    /// dependency on a UI library means that library's version is now a thing that can fight
    /// with somebody else's copy of it. Bare Minimum has zero external runtime dependencies,
    /// and a list of rows with a highlight is not worth breaking that for.
    ///
    /// The class knows nothing about food or settings -- it draws rows and reports which one
    /// was chosen. Both menus in this mod are built out of it.
    /// </summary>
    internal sealed class Menu
    {
        // ---- layout, all fractions of the screen ------------------------------
        private const float PanelX = 0.5f;      // centre
        private const float Top = 0.180f;

        /// <summary>
        /// How wide the panel is, IN HEIGHT UNITS, turned into a width through the aspect.
        ///
        /// A width given as a share of the screen's width is a third again wider on a 21:9
        /// than on a 16:9: a list that is a list on one monitor is a banner on the other, and
        /// it was. Given in height units it is the same shape everywhere. 0.56 is the 0.315
        /// of a 16:9 screen this panel was designed at.
        /// </summary>
        private const float PanelWH = 0.56f;

        private static float PanelW
        {
            get { return Hud.ToX(PanelWH); }
        }

        /// <summary>Air between the panel's edge and anything in it.</summary>
        private const float Pad = 0.012f;

        private const float SubH = 0.022f;
        private const float TabsH = 0.030f;

        /// <summary>
        /// Row height. Taller than a plain list needs, to leave room for a product picture.
        ///
        /// A menu of food with no pictures is a spreadsheet; a picture squeezed into a row
        /// sized for text is a smudge. The rows are sized for the picture and the text sits
        /// beside it.
        /// </summary>
        private const float RowH = 0.046f;

        private const float CountH = 0.020f;

        private const float NoteScale = 0.26f;
        private const float NoteLine = 0.017f;
        private const float NotePad = 0.010f;

        /// <summary>Rows on screen at once. More than this scrolls.</summary>
        private const int Window = 7;

        public string Title = "";
        public string Subtitle = "";

        /// <summary>
        /// Optional marks either side of the title. Both menus carry the mod's own two icons.
        ///
        /// Set by whoever builds the menu rather than created here, because this class is
        /// deliberately ignorant of what the mod is about -- it draws rows.
        /// </summary>
        public Flipbook TitleLeft;
        public Flipbook TitleRight;

        /// <summary>
        /// A picture to use INSTEAD of the title text: a shop's own sign over its counter.
        ///
        /// When this is set the title text and the two flanking marks are not drawn at all.
        /// A brand mark is already a piece of typography; putting an apple and an eye either
        /// side of the LTD logo would be two designs arguing in the same forty pixels.
        /// </summary>
        public Icon TitleImage;

        /// <summary>
        /// The artwork's own width over its height. Brand marks are wide.
        ///
        /// It has to be told rather than measured: an Icon is a texture handle and a path,
        /// and nothing in SHVDN reports the pixel size of a CustomSprite's source file.
        /// </summary>
        public float TitleImageRatio = 3.2f;

        public readonly List<string> Tabs = new List<string>();
        public readonly List<Row> Rows = new List<Row>();

        public int Tab;
        public int Index;

        public bool IsOpen { get; private set; }

        /// <summary>The row chosen this frame, or null. Read it after Update.</summary>
        public Row Activated { get; private set; }

        /// <summary>True on the frame the tab changed, so the owner can refill the rows.</summary>
        public bool TabChanged { get; private set; }

        /// <summary>True on the frame the menu was closed by the player.</summary>
        public bool JustClosed { get; private set; }

        /// <summary>
        /// Make left and right ADJUST the selected row instead of switching tabs.
        ///
        /// The two menus in this mod want opposite things from the same pair of keys: a shop
        /// has categories to page through, and a settings list has values to turn up and down.
        /// One flag rather than two menu classes, because everything else about them -- the
        /// scrolling, the highlight, the note strip, the sounds -- is identical.
        /// </summary>
        public bool LeftRightAdjusts;

        /// <summary>
        /// What the accept key does here, for the key cap in the footer: BUY at a counter, SET
        /// in the settings. Set by whoever builds the menu -- this class draws rows.
        /// </summary>
        public string ConfirmWord = "CHOOSE";

        /// <summary>
        /// How strongly the row pictures and the title marks catch the light. 0 is off.
        ///
        /// Set by whoever builds the menu, from the same ini dial the HUD uses, because this
        /// class is deliberately ignorant of the mod's settings -- it draws rows.
        /// </summary>
        public float Shimmer;

        /// <summary>
        /// Where the highlight and the tab marker are ACTUALLY drawn, chasing where they
        /// ought to be.
        ///
        /// A list that moves its highlight instantly is readable but tells you nothing about
        /// which way it went; one that slides carries the direction of travel, which is the
        /// whole reason a long settings list feels navigable rather than teleporty.
        ///
        /// EASED PER FRAME AGAINST A WALL CLOCK, not stepped per frame. A fixed step per
        /// frame runs twice as fast at 120fps as at 60, and this mod has been bitten by that
        /// often enough to have a rule about it.
        /// </summary>
        private float _glide;
        private float _tabGlide;
        private int _lastFrame;

        /// <summary>The cursor frame that glides between rows. See UI.Glide.</summary>
        private readonly Glide _frame = new Glide();

        /// <summary>When the menu opened and when the selection last moved, for the entrance and the caption.</summary>
        private int _shownAt;
        private int _pickedAt;

        /// <summary>The row nudged this frame, or null. Read it after Update.</summary>
        public Row Adjusted { get; private set; }

        /// <summary>Which way it was nudged: -1 for left, +1 for right.</summary>
        public int AdjustBy { get; private set; }

        /// <summary>
        /// Whether CTRL was held for the nudge just reported, asking for a finer step.
        /// </summary>
        ///
        /// READ AS A KEY, not as a GTA control. There is no frontend control for "modifier",
        /// and the pad has no spare button worth spending on this -- it is a keyboard nicety
        /// for somebody lining a HUD up against the minimap, and on a pad the coarse step is
        /// the only step. Nothing downstream breaks when it is always false.
        ///
        /// Safe despite CTRL being duck and cover in the game: the menu already suppresses
        /// both for as long as it is open, so holding it does nothing but change this flag.
        public bool Fine { get; private set; }

        /// <summary>Where the first visible row sits in the list.</summary>
        private int _scroll;

        public void Open()
        {
            if (IsOpen) return;

            IsOpen = true;
            Index = 0;
            _scroll = 0;

            _shownAt = Game.GameTime;
            _pickedAt = _shownAt;
            _frame.Reset();

            Sound("SELECT");
        }

        /// <summary>
        /// Until when the game's own controls stay blocked after a menu closes.
        ///
        /// THIS IS WHY CLOSING A SHOP DOES NOT THROW A PUNCH. The key that dismisses the menu
        /// is still physically down on the frame after IsOpen goes false, and by then nothing
        /// is calling Update, so nothing is disabling Attack -- the game reads the tail of that
        /// same press as an input and Franklin swings at the cashier.
        ///
        /// STATIC because only one menu is ever open at a time. The codebase already leans on
        /// that (Vendors keeps ONE menu for every shop), and a per-instance timer would leave
        /// a gap the moment one menu closed while another was about to open.
        /// </summary>
        private static int _quietUntil;

        /// <summary>
        /// How long the block stays up after a close.
        ///
        /// Half a second rather than a frame or two: a key held down through a menu closing is
        /// a human holding a key, and humans hold them for about that long. It was 300ms and
        /// the punch got through anyway -- but that was the missing melee inputs, not the
        /// window being short, so this is only the margin it always should have had.
        /// </summary>
        private const int QuietMs = 500;

        /// <summary>
        /// Whether a menu has only just closed.
        ///
        /// READ BY THE THINGS THAT OPEN MENUS, not by the menu. The interact key is read
        /// through IS_DISABLED_CONTROL_JUST_PRESSED so that a pad still works while we are
        /// holding the game's own controls down -- which means the press that just closed a
        /// menu is still perfectly visible to whatever would open one, and without this it
        /// would be answered by re-opening the thing that was just dismissed.
        /// </summary>
        public static bool Quiet
        {
            get
            {
                try { return Game.GameTime < _quietUntil; }
                catch { return false; }
            }
        }

        /// <summary>
        /// Keeps the block up for a few frames after any menu closes.
        ///
        /// Must be called EVERY TICK, from Main, not from Update: the whole point is the
        /// frames after the menu stopped being updated.
        /// </summary>
        public static void Cooldown()
        {
            if (Game.GameTime < _quietUntil) Suppress();
        }

        public void Close()
        {
            if (!IsOpen) return;

            _quietUntil = Game.GameTime + QuietMs;

            IsOpen = false;
            Activated = null;
            Sound("BACK");
        }

        // ======================================================================
        // Input
        // ======================================================================

        public void Update()
        {
            Activated = null;
            Adjusted = null;
            AdjustBy = 0;
            Fine = false;
            TabChanged = false;
            JustClosed = false;

            if (!IsOpen) return;

            try
            {
                Suppress();
                Navigate();
            }
            catch (Exception ex)
            {
                Log.Once("menu-input", "Menu input failed: " + ex.Message);
                IsOpen = false;
            }
        }

        /// <summary>
        /// Holds off everything the player would otherwise do while a menu is up.
        ///
        /// THE ONE LIST, AND EVERY SCREEN CALLS IT. It used to be copy-pasted into three
        /// files, which is how it came to be missing the melee inputs in all three at once:
        /// the fix for the pocket was never going to reach the till.
        ///
        /// DISABLED PER FRAME rather than switched off wholesale. DisableAllControlsThisFrame
        /// would also kill the frontend controls the menu reads, so the navigation would have
        /// to be re-enabled one by one afterwards -- and it stops the camera, which makes the
        /// world behind the menu feel frozen rather than paused-over.
        ///
        /// THE MELEE FAMILY IS THE HALF THAT WAS MISSING. Attack and MeleeAttack1/2 were
        /// blocked, which covers a trigger pull; MeleeAttackLight, Heavy, Alternate and Block
        /// are separate inputs and they are the ones an unarmed Franklin punches and kicks on.
        /// So closing a shop menu bare-handed swung at the cashier, which is exactly the thing
        /// the quiet window below was written to stop and had never actually stopped.
        ///
        /// THE VEHICLE ATTACKS MATTER FOR THE DRIVE-THROUGHS, which are ordered from the
        /// driver's seat with a menu on screen and a car full of controls underneath it.
        /// </summary>
        public static void Suppress()
        {
            GTA.Control[] blocked =
            {
                // On foot
                GTA.Control.Attack, GTA.Control.Attack2, GTA.Control.Aim,
                GTA.Control.AccurateAim,
                GTA.Control.MeleeAttack1, GTA.Control.MeleeAttack2,
                GTA.Control.MeleeAttackLight, GTA.Control.MeleeAttackHeavy,
                GTA.Control.MeleeAttackAlternate, GTA.Control.MeleeBlock,
                GTA.Control.Jump, GTA.Control.Enter, GTA.Control.Duck,
                GTA.Control.SelectWeapon, GTA.Control.SelectWeaponMelee,
                GTA.Control.VehicleExit,
                GTA.Control.Context, GTA.Control.ContextSecondary,
                GTA.Control.Sprint, GTA.Control.Cover, GTA.Control.Reload,
                GTA.Control.Detonate, GTA.Control.Phone,

                // In a car, for the drive-through windows
                GTA.Control.VehicleAim, GTA.Control.VehicleAttack,
                GTA.Control.VehicleAttack2,
                GTA.Control.VehiclePassengerAim, GTA.Control.VehiclePassengerAttack
            };

            foreach (var c in blocked)
            {
                try { Game.DisableControlThisFrame(c); }
                catch { /* one control the build does not have is not worth the frame */ }
            }
        }

        private void Navigate()
        {
            if (Pressed(GTA.Control.FrontendCancel) || Pressed(GTA.Control.FrontendPause))
            {
                Close();
                JustClosed = true;
                return;
            }

            if (Rows.Count > 0)
            {
                if (Pressed(GTA.Control.FrontendDown)) Move(1);
                if (Pressed(GTA.Control.FrontendUp)) Move(-1);
            }

            // THE SHOULDER BUTTONS ALWAYS PAGE THE TABS, whatever left and right are doing.
            //
            // This is what lets a settings menu have tabs at all. Left and right are spoken
            // for there -- they turn values up and down -- and for a long time that was taken
            // as settling the question, so nearly forty settings lived in one flat list seven
            // rows tall. The keys were the constraint, not the idea, and there is a second
            // pair sitting unused: Q and E on a keyboard, LB and RB on a pad, which is where
            // every other game in the genre puts exactly this.
            if (Tabs.Count > 1)
            {
                if (Pressed(GTA.Control.FrontendRb)) SwitchTab(1);
                if (Pressed(GTA.Control.FrontendLb)) SwitchTab(-1);
            }

            if (LeftRightAdjusts)
            {
                if (Rows.Count > 0)
                {
                    if (Pressed(GTA.Control.FrontendRight)) Nudge(1);
                    if (Pressed(GTA.Control.FrontendLeft)) Nudge(-1);
                }
            }
            else if (Tabs.Count > 1)
            {
                if (Pressed(GTA.Control.FrontendRight)) SwitchTab(1);
                if (Pressed(GTA.Control.FrontendLeft)) SwitchTab(-1);
            }

            if (!Pressed(GTA.Control.FrontendAccept)) return;
            if (Rows.Count == 0) return;

            var row = Rows[Clamp(Index, 0, Rows.Count - 1)];

            if (!row.Enabled)
            {
                // The refusal is AUDIBLE. A dead keypress on a row you cannot afford reads as
                // the menu being broken; the game's own error blip reads as "no".
                Sound("ERROR");
                return;
            }

            Activated = row;
            Sound("SELECT");
        }

        private void Move(int by)
        {
            var was = Index;

            Index = NextSelectable(Index, by);

            if (Index != was) _pickedAt = Game.GameTime;

            // Keep the selection inside the visible window, scrolling only as far as it must.
            if (Index < _scroll) _scroll = Index;
            else if (Index >= _scroll + Window) _scroll = Index - Window + 1;

            // A wrap from the bottom to the top has to take the window with it.
            if (Index == 0) _scroll = 0;
            else if (Index == Rows.Count - 1) _scroll = Math.Max(0, Rows.Count - Window);

            Sound("NAV_UP_DOWN");
        }

        /// <summary>
        /// The next row in that direction that is not a heading.
        ///
        /// BOUNDED BY THE ROW COUNT, so a list that is nothing but headings -- which should
        /// never happen and would hang this otherwise -- comes back where it started instead
        /// of spinning the frame.
        /// </summary>
        private int NextSelectable(int from, int by)
        {
            if (Rows.Count == 0) return 0;

            var step = by >= 0 ? 1 : -1;
            var at = from;

            for (var guard = 0; guard < Rows.Count; guard++)
            {
                at = Wrap(at + step, Rows.Count);

                if (!Rows[at].Header) return at;
            }

            return from;
        }

        /// <summary>The first row that is not a heading. Where a freshly opened list lands.</summary>
        private int FirstSelectable()
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                if (!Rows[i].Header) return i;
            }

            return 0;
        }

        /// <summary>
        /// Puts the selection somewhere legal after the rows have been rebuilt.
        ///
        /// Called by whoever refills the list. A panel that swaps its rows can easily leave
        /// the selection sitting on a heading, and from there every keypress moves it two.
        /// </summary>
        public void Settle()
        {
            if (Rows.Count == 0) { Index = 0; _scroll = 0; return; }

            Index = Clamp(Index, 0, Rows.Count - 1);

            if (Rows[Index].Header) Index = FirstSelectable();

            if (Index < _scroll) _scroll = Index;
            else if (Index >= _scroll + Window) _scroll = Index - Window + 1;

            if (_scroll > Math.Max(0, Rows.Count - Window)) _scroll = Math.Max(0, Rows.Count - Window);
            if (_scroll < 0) _scroll = 0;
        }

        private void Nudge(int by)
        {
            var row = Rows[Clamp(Index, 0, Rows.Count - 1)];

            if (row.Header) return;

            if (!row.Enabled) { Sound("ERROR"); return; }

            Adjusted = row;
            AdjustBy = by;

            // Fully qualified rather than a using, because System.Windows.Forms.Control and
            // GTA.Control would then both be in scope and every GTA.Control in this file is
            // one careless edit from being ambiguous.
            Fine = Game.IsKeyPressed(System.Windows.Forms.Keys.LControlKey)
                || Game.IsKeyPressed(System.Windows.Forms.Keys.RControlKey);

            Sound("NAV_LEFT_RIGHT");
        }

        private void SwitchTab(int by)
        {
            Tab = Wrap(Tab + by, Tabs.Count);
            Index = 0;
            _scroll = 0;
            TabChanged = true;

            // The rows are about to be rebuilt, so the frame lands on the new list rather
            // than gliding across from a row that no longer exists.
            _pickedAt = Game.GameTime;
            _frame.Reset();
            Sound("NAV_LEFT_RIGHT");
        }

        /// <summary>
        /// Reads a frontend control, DISABLED-aware.
        ///
        /// IsDisabledControlJustPressed rather than IsControlJustPressed, because Suppress
        /// above turns some of these off -- and a disabled control still reports its state,
        /// which is the whole mechanism that lets a menu own the keys without the game acting
        /// on them too.
        /// </summary>
        private static bool Pressed(GTA.Control control)
        {
            try
            {
                return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
            }
            catch
            {
                return false;
            }
        }

        private static void Sound(string name)
        {
            try
            {
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, name,
                              "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
            }
            catch
            {
                // A silent menu still works.
            }
        }

        // ======================================================================
        // Drawing
        // ======================================================================

        /// <summary>
        /// Walks the highlight and the tab marker toward where they belong.
        ///
        /// EXPONENTIAL, against the wall clock: a fixed fraction of the REMAINING distance per
        /// millisecond, which lands in the same time whatever the framerate is and never
        /// overshoots. A fixed step per frame would run at double speed on a 120Hz machine.
        ///
        /// It SNAPS on a big jump rather than sliding. Wrapping from the last row to the
        /// first is a jump of the whole list, and sliding through thirty rows to get there
        /// reads as the menu having lost its place -- the eye follows a short slide and gives
        /// up on a long one.
        /// </summary>
        private void Ease()
        {
            var now = Game.GameTime;

            var dt = _lastFrame == 0 ? 16 : now - _lastFrame;
            _lastFrame = now;

            // A paused game, a loading screen or a wrapped clock can hand back nonsense.
            if (dt < 0 || dt > 200) dt = 16;

            if (!Theme.Motion)
            {
                _glide = Index;
                _tabGlide = Tab;
                return;
            }

            _glide = Chase(_glide, Index, dt, 3f);
            _tabGlide = Chase(_tabGlide, Tab, dt, 2f);
        }

        private static float Chase(float at, float to, int dt, float snapOver)
        {
            var gap = to - at;

            if (Math.Abs(gap) > snapOver) return to;
            if (Math.Abs(gap) < 0.001f) return to;

            // 0.016 per ms settles about 95% of the way in a fifth of a second.
            var k = 1f - (float)Math.Pow(1.0 - 0.016, dt);

            return at + gap * k;
        }

        /// <summary>
        /// The panel, built the way every panel in Hoodrich is built and in this mod's amber:
        /// ONE ROUNDED BLACK with a warm wash under its top, a head with a rule under it, the
        /// list, a note, a rule and a row of key caps.
        ///
        /// MEASURED FIRST AND DRAWN SECOND. The panel is the ground under everything and has
        /// to know how tall it is before the first row goes down, so every part's height is
        /// worked out up front -- including how many lines the selected row's note wraps to.
        /// </summary>
        public void Draw()
        {
            if (!IsOpen) return;

            try
            {
                Ease();

                var arrive = Theme.Arrive(_shownAt, Theme.EnterMs);

                var left = PanelX - PanelW / 2f;
                var top = Top + Theme.EnterRise * (1f - arrive);

                var x = left + Pad;
                var right = left + PanelW - Pad;
                var wide = right - x;

                var shown = Rows.Count == 0 ? 1 : Math.Min(Window, Rows.Count);
                var subH = string.IsNullOrEmpty(Subtitle) ? 0f : SubH;
                var tabsH = Tabs.Count > 0 ? TabsH : 0f;
                var countH = Rows.Count > Window ? CountH : 0f;

                var note = NoteLines(wide);
                var noteH = note.Count == 0 ? 0f : NotePad * 2f + note.Count * NoteLine;

                var height = Kit.HeadH + subH + tabsH + shown * RowH + countH + noteH + Kit.FootH;

                Theme.Panel(left, top, PanelW, height, arrive);

                var y = Head(left, top, arrive);

                if (subH > 0f)
                {
                    Hud.Text(Subtitle, x, y + 0.001f, 0.25f,
                             Palette.Alpha(Palette.TextDim, (int)(210f * arrive)), Hud.FontBody);
                    y += subH;
                }

                if (Tabs.Count > 0)
                {
                    TabStrip(x, y, wide, arrive);
                    y += TabsH;
                }

                _frame.Begin();

                y = List(x, y, wide, arrive);

                Note(x, y, wide, note, arrive);

                Foot(x, right, top + height - Kit.FootH, arrive);

                // Last, so it rides over the row it is pointing at.
                _frame.Draw(arrive);
            }
            catch (Exception ex)
            {
                Log.Once("menu-draw", "The menu could not be drawn: " + ex.Message);
            }
        }

        /// <summary>The head: a shop's own sign if it has one, otherwise the title between its two marks.</summary>
        private float Head(float left, float top, float arrive)
        {
            if (TitleImage != null)
            {
                return Kit.HeadLogo(left, top, PanelW, Pad, TitleImage, TitleImageRatio, null, arrive);
            }

            var mark = TitleLeft == null ? null : TitleLeft.Current;
            var tail = TitleRight == null ? null : TitleRight.Current;

            return Kit.Head(left, top, PanelW, Pad, mark, Title, null, tail, null, arrive, Shimmer);
        }

        /// <summary>
        /// The category tabs, split evenly across the panel, with ONE amber marker that slides.
        /// Evenly rather than sized to their text: a strip whose cells move as the labels change
        /// is a strip where the marker appears to jump sideways when you switch.
        /// </summary>
        private void TabStrip(float x, float y, float wide, float arrive)
        {
            var w = wide / Tabs.Count;

            // ONE UNDERLINE THAT SLIDES, rather than a filled cell. A block of amber under a
            // word is a button; a line under it is a tab, which is what this is.
            Theme.Underline(x + _tabGlide * w + 0.006f, y + TabsH - 0.007f, w - 0.012f, arrive);

            var scale = Tabs.Count <= 4 ? 0.28f : Tabs.Count <= 6 ? 0.25f : 0.22f;

            for (var i = 0; i < Tabs.Count; i++)
            {
                // How much of the marker is under THIS label, so the ink crosses over with
                // the marker instead of flicking to black a frame early or late.
                var under = 1f - Math.Min(1f, Math.Abs(_tabGlide - i));

                var ink = Theme.Ink(Palette.Alpha(Palette.TextDim, (int)(230f * arrive)), under);

                Hud.Text(Tabs[i], x + i * w + w / 2f, y + 0.004f + (0.28f - scale) * 0.012f, scale,
                         ink, Hud.FontLabel, true, false, Theme.Outline(under));
            }
        }

        /// <summary>Draws the visible slice of rows and returns the y below them.</summary>
        private float List(float x, float y, float wide, float arrive)
        {
            if (Rows.Count == 0)
            {
                Hud.Text("Nothing here.", x + wide / 2f, y + 0.012f, 0.30f,
                         Palette.Alpha(Palette.TextDim, (int)(200f * arrive)), Hud.FontBody, true);
                return y + RowH;
            }

            var shown = Math.Min(Window, Rows.Count);

            _scroll = Clamp(_scroll, 0, Math.Max(0, Rows.Count - shown));

            // ONE PLATE, DRAWN UNDER EVERYTHING, at the eased position rather than on the
            // selected row's own rectangle. Each row painting its own highlight is what makes a
            // list teleport: the plate can only ever be in one of seven places. Lifted out, it
            // can be between two of them, which is what a slide is.
            var slot = _glide - _scroll;

            if (slot > -1f && slot < shown)
            {
                var plateY = y + slot * RowH;

                Theme.Plate(x, plateY, wide, RowH, arrive);
            }

            for (var i = 0; i < shown; i++)
            {
                var at = _scroll + i;
                if (at >= Rows.Count) break;

                var lit = 1f - Math.Min(1f, Math.Abs(_glide - at));
                var rowY = y + i * RowH;

                DrawRow(Rows[at], x, rowY, wide, lit, i, arrive);

                if (at == Index && !Rows[at].Header) _frame.Target(x, rowY, wide, RowH);
            }

            y += shown * RowH;

            // A count, only when the list is actually longer than the window -- otherwise it
            // is a line of furniture saying "4 of 4".
            if (Rows.Count > Window)
            {
                Hud.TextRight((Index + 1) + " of " + Rows.Count, x + wide, y + 0.002f, 0.24f,
                              Palette.Alpha(Palette.TextDim, (int)(200f * arrive)), Hud.FontLabel);
                y += CountH;
            }

            return y;
        }

        private void DrawRow(Row row, float x, float y, float wide, float lit, int slot, float arrive)
        {
            if (row.Header)
            {
                // The name in the brand colour with a rule under it. It has to read as a break
                // in the list rather than as another entry, or it is a setting you cannot change.
                Hud.Text(row.Left, x + 0.004f, y + (RowH - Hud.Height(0.30f, Hud.FontLabel)) / 2f, 0.30f,
                         Palette.Alpha(Palette.Brand, (int)(235f * arrive)), Hud.FontLabel);

                Theme.Rule(x, y + RowH - 0.004f, wide, arrive);
                return;
            }

            var plain = row.Enabled ? Palette.Text : Palette.TextDisabled;

            // Dim ink on the dark panel, full white on the plate, and every shade between as
            // the plate slides onto the row -- so the words cross over WITH the plate rather
            // than flicking on the frame the index changed.
            var ink = Theme.Ink(Palette.Alpha(plain, (int)(plain.A * arrive)), lit);
            var outline = Theme.Outline(lit);

            // Past the plate's rail, so the picture never sits on the amber.
            var textLeft = x + 0.010f;

            var icon = IconCache.Get(row.IconFile);

            if (icon != null && !icon.Missing)
            {
                var tall = RowH * 0.78f;
                var wideIcon = Hud.ToX(tall);

                // A greyed row's picture greys with it, or an item you cannot afford still
                // looks available at a glance and only the text says otherwise.
                var tint = row.Enabled
                    ? row.IconTint
                    : Color.FromArgb(120, row.IconTint.R / 2 + 60,
                                     row.IconTint.G / 2 + 60, row.IconTint.B / 2 + 60);

                // STAGGERED DOWN THE LIST, off the row's place rather than its index in the
                // data, so the wave stays put while the selection moves. The lit row is left
                // alone: it is on its way to near-black and does not need to breathe as well.
                if (lit < 0.5f && row.Enabled)
                {
                    tint = Sheen.On(tint, slot * 0.125f, Shimmer * 0.7f);
                }

                // Brighter on the plate, the way the words go.
                tint = Theme.Ink(Palette.Alpha(tint, (int)(tint.A * arrive)), lit);

                icon.DrawSized(textLeft + wideIcon / 2f, y + RowH / 2f, wideIcon, tall, tint);

                textLeft += wideIcon + 0.006f;
            }

            var textY = y + (RowH - Hud.Height(0.30f, Hud.FontBody)) / 2f;

            Hud.Text(row.Left, textLeft, textY, 0.30f, ink, Hud.FontBody, false, false, outline);

            if (string.IsNullOrEmpty(row.Right)) return;

            Hud.Text(row.Right, x + wide - 0.004f, textY + 0.001f, 0.28f, ink, Hud.FontBody,
                     false, true, outline);
        }

        /// <summary>What the selected row has to say, wrapped to the panel. Empty when it says nothing.</summary>
        private List<string> NoteLines(float wide)
        {
            if (Rows.Count == 0) return new List<string>();

            var row = Rows[Clamp(Index, 0, Rows.Count - 1)];
            if (string.IsNullOrEmpty(row.Note)) return new List<string>();

            return Wrap(row.Note, NoteScale, wide - 0.004f, 2);
        }

        /// <summary>
        /// The description: a rule, then the lines, the first sliding in with the plate the way
        /// every caption under a chosen thing does, so the words arrive with the cursor rather
        /// than swapping under it.
        /// </summary>
        private void Note(float x, float y, float wide, List<string> lines, float arrive)
        {
            if (lines.Count == 0) return;

            Theme.Rule(x, y + 0.003f, wide, arrive);

            var grown = Theme.Grown(_pickedAt);

            for (var i = 0; i < lines.Count; i++)
            {
                var slide = i == 0 ? Hud.ToX(0.008f) * (1f - grown) : 0f;

                Hud.Text(lines[i], x + slide, y + NotePad + i * NoteLine, NoteScale,
                         Palette.Alpha(Palette.TextDim, (int)((120f + 100f * grown) * arrive)),
                         Hud.FontBody);
            }
        }

        /// <summary>
        /// The footer: a rule and the keys as caps, with the way out in the same corner as
        /// every other screen. Only the keys that would do something here.
        /// </summary>
        private void Foot(float x, float right, float y, float arrive)
        {
            Theme.Rule(x, y, right - x, arrive);

            var ky = y + 0.011f;

            Kit.KeyRight(right, ky, Kit.Back, "DONE", arrive);

            var kx = Kit.Key(x, ky, null, "arrow_updown.png", "PICK", arrive);

            if (LeftRightAdjusts)
            {
                kx = Kit.Key(kx, ky, null, "arrow_leftright.png", "ADJUST", arrive);

                if (Tabs.Count > 1) kx = Kit.Key(kx, ky, Kit.Pages, null, "TABS", arrive);
            }
            else if (Tabs.Count > 1)
            {
                kx = Kit.Key(kx, ky, null, "arrow_leftright.png", "TABS", arrive);
            }

            if (Rows.Count > 0) Kit.Key(kx, ky, Kit.Confirm, null, ConfirmWord, arrive);
        }

        // ======================================================================

        /// <summary>
        /// Splits text into at most `max` lines that each fit a width.
        ///
        /// Measured with Draw.Width rather than counted in characters, because the font is
        /// proportional and "WWW" is three times the width of "iii". Anything past the last
        /// line is dropped with an ellipsis rather than silently cut mid-word.
        /// </summary>
        private static List<string> Wrap(string text, float scale, float width, int max)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            var current = "";

            foreach (var word in text.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;

                if (Hud.Width(candidate, scale, Hud.FontBody) <= width)
                {
                    current = candidate;
                    continue;
                }

                if (current.Length > 0) lines.Add(current);

                if (lines.Count >= max)
                {
                    lines[max - 1] = lines[max - 1] + "...";
                    return lines;
                }

                current = word;
            }

            if (current.Length > 0 && lines.Count < max) lines.Add(current);

            return lines;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (hi < lo) return lo;
            return v < lo ? lo : v > hi ? hi : v;
        }

        /// <summary>Wraps round the ends, so holding down past the last row returns to the first.</summary>
        private static int Wrap(int v, int count)
        {
            if (count <= 0) return 0;
            return ((v % count) + count) % count;
        }
    }
}
