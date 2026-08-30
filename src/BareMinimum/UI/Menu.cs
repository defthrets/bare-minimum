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

        /// <summary>A greyed row can be selected and read, but not activated.</summary>
        public bool Enabled = true;

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
        private const float PanelW = 0.30f;
        private const float Top = 0.180f;

        private const float HeaderH = 0.052f;
        private const float TabsH = 0.030f;
        private const float RowH = 0.036f;
        private const float NoteH = 0.044f;

        /// <summary>Rows on screen at once. More than this scrolls.</summary>
        private const int Window = 8;

        /// <summary>Chalet Comprime Cologne, the game's own block font.</summary>
        private const int Plain = 4;

        public string Title = "";
        public string Subtitle = "";

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

        /// <summary>The row nudged this frame, or null. Read it after Update.</summary>
        public Row Adjusted { get; private set; }

        /// <summary>Which way it was nudged: -1 for left, +1 for right.</summary>
        public int AdjustBy { get; private set; }

        /// <summary>Where the first visible row sits in the list.</summary>
        private int _scroll;

        public void Open()
        {
            if (IsOpen) return;

            IsOpen = true;
            Index = 0;
            _scroll = 0;
            Sound("SELECT");
        }

        public void Close()
        {
            if (!IsOpen) return;

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
        /// Holds off everything the player would otherwise do while the menu is up.
        ///
        /// DISABLED PER FRAME rather than switched off wholesale. DisableAllControlsThisFrame
        /// would also kill the frontend controls this menu reads, so the navigation would have
        /// to be re-enabled one by one afterwards -- and it stops the camera, which makes the
        /// world behind the menu feel frozen rather than paused-over.
        ///
        /// The aim and attack pair matter most: without them, opening a shop menu with a gun
        /// out fires it.
        /// </summary>
        private static void Suppress()
        {
            GTA.Control[] blocked =
            {
                GTA.Control.Attack, GTA.Control.Attack2, GTA.Control.Aim,
                GTA.Control.MeleeAttack1, GTA.Control.MeleeAttack2,
                GTA.Control.Jump, GTA.Control.Enter, GTA.Control.Duck,
                GTA.Control.SelectWeapon, GTA.Control.VehicleExit,
                GTA.Control.Context, GTA.Control.ContextSecondary,
                GTA.Control.Sprint, GTA.Control.Cover, GTA.Control.Reload,
                GTA.Control.Detonate, GTA.Control.Phone
            };

            foreach (var c in blocked) Game.DisableControlThisFrame(c);
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
            Index = Wrap(Index + by, Rows.Count);

            // Keep the selection inside the visible window, scrolling only as far as it must.
            if (Index < _scroll) _scroll = Index;
            else if (Index >= _scroll + Window) _scroll = Index - Window + 1;

            // A wrap from the bottom to the top has to take the window with it.
            if (Index == 0) _scroll = 0;
            else if (Index == Rows.Count - 1) _scroll = Math.Max(0, Rows.Count - Window);

            Sound("NAV_UP_DOWN");
        }

        private void Nudge(int by)
        {
            var row = Rows[Clamp(Index, 0, Rows.Count - 1)];

            if (!row.Enabled) { Sound("ERROR"); return; }

            Adjusted = row;
            AdjustBy = by;
            Sound("NAV_LEFT_RIGHT");
        }

        private void SwitchTab(int by)
        {
            Tab = Wrap(Tab + by, Tabs.Count);
            Index = 0;
            _scroll = 0;
            TabChanged = true;
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

        public void Draw()
        {
            if (!IsOpen) return;

            try
            {
                var left = PanelX - PanelW / 2f;
                var y = Top;

                Header(left, y);
                y += HeaderH;

                if (Tabs.Count > 0)
                {
                    TabStrip(left, y);
                    y += TabsH;
                }

                y = List(left, y);

                Note(left, y);
            }
            catch (Exception ex)
            {
                Log.Once("menu-draw", "The menu could not be drawn: " + ex.Message);
            }
        }

        private void Header(float left, float y)
        {
            Hud.Bar(left, y, PanelW, HeaderH, Color.FromArgb(238, 12, 12, 15));

            // A thin accent along the bottom of the header, which is what separates it from
            // the tab strip without spending a whole row of height on a gap.
            Hud.Bar(left, y + HeaderH - 0.0022f, PanelW, 0.0022f,
                    Color.FromArgb(255, 240, 170, 56));

            Hud.Text(Title, PanelX, y + 0.008f, 0.58f,
                     Color.FromArgb(245, 245, 245, 248), Plain, true);

            if (string.IsNullOrEmpty(Subtitle)) return;

            Hud.Text(Subtitle, PanelX, y + 0.032f, 0.28f,
                     Color.FromArgb(210, 190, 190, 198), Plain, true);
        }

        /// <summary>
        /// The category tabs, split evenly across the panel.
        ///
        /// Evenly rather than sized to their text: a tab strip whose cells move as the labels
        /// change is a strip where the selected cell appears to jump sideways when you switch,
        /// and these labels are short enough that the even split never crowds them.
        /// </summary>
        private void TabStrip(float left, float y)
        {
            Hud.Bar(left, y, PanelW, TabsH, Color.FromArgb(230, 22, 22, 27));

            var w = PanelW / Tabs.Count;

            for (var i = 0; i < Tabs.Count; i++)
            {
                var x = left + i * w;
                var on = i == Tab;

                if (on) Hud.Bar(x, y, w, TabsH, Color.FromArgb(240, 240, 170, 56));

                Hud.Text(Tabs[i], x + w / 2f, y + 0.005f, 0.30f,
                         on ? Color.FromArgb(255, 16, 16, 18)
                            : Color.FromArgb(210, 200, 200, 208),
                         Plain, true, false, !on);
            }
        }

        /// <summary>Draws the visible slice of rows and returns the y below them.</summary>
        private float List(float left, float y)
        {
            var shown = Math.Min(Window, Rows.Count);

            if (Rows.Count == 0)
            {
                Hud.Bar(left, y, PanelW, RowH, Color.FromArgb(225, 20, 20, 24));
                Hud.Text("Nothing here.", PanelX, y + 0.008f, 0.32f,
                         Color.FromArgb(190, 180, 180, 188), Plain, true);
                return y + RowH;
            }

            _scroll = Clamp(_scroll, 0, Math.Max(0, Rows.Count - shown));

            for (var i = 0; i < shown; i++)
            {
                var at = _scroll + i;
                if (at >= Rows.Count) break;

                DrawRow(Rows[at], left, y + i * RowH, at == Index);
            }

            y += shown * RowH;

            // A count, only when the list is actually longer than the window -- otherwise it
            // is a line of furniture saying "4 of 4".
            if (Rows.Count > Window)
            {
                Hud.Bar(left, y, PanelW, 0.020f, Color.FromArgb(230, 16, 16, 20));
                Hud.Text((Index + 1) + " of " + Rows.Count, left + PanelW - 0.008f, y + 0.0015f,
                         0.26f, Color.FromArgb(200, 190, 190, 198), Plain, false, true);
                y += 0.020f;
            }

            return y;
        }

        private static void DrawRow(Row row, float left, float y, bool selected)
        {
            Hud.Bar(left, y, PanelW, RowH,
                    selected ? Color.FromArgb(242, 240, 170, 56)
                             : Color.FromArgb(222, 20, 20, 24));

            // Black ink on the amber highlight, light ink on the dark rows. The outline is
            // turned OFF for the dark-on-light case: both of GTA's text decorations draw in
            // BLACK, so an outline round black text fills in the holes in 8, 9 and 0 until
            // they are one blob. Learned on Fumes' gauge.
            var ink = selected
                ? Color.FromArgb(255, 14, 14, 16)
                : row.Enabled ? Color.FromArgb(235, 232, 232, 238)
                              : Color.FromArgb(160, 150, 150, 158);

            Hud.Text(row.Left, left + 0.010f, y + 0.007f, 0.34f, ink, Plain, false, false, !selected);

            if (string.IsNullOrEmpty(row.Right)) return;

            Hud.Text(row.Right, left + PanelW - 0.010f, y + 0.007f, 0.34f, ink,
                     Plain, false, true, !selected);
        }

        /// <summary>The description strip: whatever the selected row wants to say.</summary>
        private void Note(float left, float y)
        {
            if (Rows.Count == 0) return;

            var row = Rows[Clamp(Index, 0, Rows.Count - 1)];
            if (string.IsNullOrEmpty(row.Note)) return;

            Hud.Bar(left, y, PanelW, NoteH, Color.FromArgb(236, 12, 12, 15));
            Hud.Bar(left, y, PanelW, 0.0018f, Color.FromArgb(190, 240, 170, 56));

            Hud.Text(row.Note, left + 0.010f, y + 0.009f, 0.28f,
                     Color.FromArgb(220, 205, 205, 212), Plain);
        }

        // ======================================================================

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
