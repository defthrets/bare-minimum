using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using BareMinimum.Core;
using BareMinimum.Food;
using BareMinimum.Venues;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.UI
{
    /// <summary>
    /// The fridge: what is on you on the left, what is in it on the right, and the two keys
    /// that move things between them.
    ///
    /// TWO GRIDS RATHER THAN ONE LIST, for the reason the pocket screen already argued: every
    /// item in this mod has a picture, so a shelf of them is a set of silhouettes you learn
    /// the position of rather than a set of names you read. What is new here is the second
    /// grid, and the whole design follows from putting them SIDE BY SIDE: a transfer is a
    /// thing with a direction, and two columns make the direction obvious without a word of
    /// explanation. One list with an in/out toggle would have needed a sentence.
    ///
    /// THE CURSOR CROSSES AT THE EDGE. Pressing right off the end of the pocket lands in the
    /// fridge and pressing left off the start of the fridge comes back, which is the only
    /// arrangement nobody has to be taught. Each side remembers where its cursor was, so
    /// crossing back does not dump you at the top of a full fridge.
    ///
    /// IT REFUSES TO CROSS TO AN EMPTY SIDE, deliberately. There is nothing to point at over
    /// there and nothing you could do once you arrived -- you fill a fridge from the pocket
    /// side, not from the fridge side.
    ///
    /// THE PANEL IS THE HEIGHT OF WHAT IS IN IT. The grid is sized to the fuller of the two
    /// sides rather than to the fridge's capacity, because forty outlined squares with three
    /// things in them reads as broken rather than as empty -- the pocket screen learned that
    /// one first.
    /// </summary>
    internal sealed class FridgeScreen
    {
        private const float Top = 0.150f;

        /// <summary>
        /// How tall a tile is, as a fraction of the screen's height; its width follows through
        /// the aspect. Sized by height for the reason the pocket gives, and a touch smaller
        /// than the pocket's because there are two panes of them side by side.
        /// </summary>
        private const float TileH = 0.095f;

        /// <summary>Air between the panel's edge and anything in it, and below the grids.</summary>
        private const float Pad = 0.012f;
        private const float GridPad = 0.006f;

        /// <summary>The caption over each pane: its name, its count, a rule.</summary>
        private const float CapH = 0.028f;

        /// <summary>The card under the grids: the chosen thing, said properly.</summary>
        /// <summary>
        /// The card under the grid, as tall as what goes on it.
        ///
        /// MEASURED, NOT EYEBALLED -- the same fix the pocket had, which was applied there and
        /// not here. A flat 0.052 leaves less room than a line of body text at this scale
        /// actually occupies, so the line of flavour under an item's name was drawn below the
        /// bottom of its own plate and onto the footer rule.
        /// </summary>
        private static float CardH
        {
            get { return DescTop + Hud.Height(DescScale, Hud.FontBody) + 0.008f; }
        }

        /// <summary>Where the line under the name starts, from the top of the card, and how big it is.</summary>
        private const float DescTop = 0.030f;
        private const float DescScale = 0.25f;

        /// <summary>The seam between the two panes, so they read as two things.</summary>
        private const float Gutter = 0.010f;

        /// <summary>The gap between tiles, as an x fraction. Turned into y through the aspect.</summary>
        private const float Gap = 0.0018f;

        /// <summary>How much the chosen picture swells as its plate comes up.</summary>
        private const float PickGrow = 0.10f;

        private const int Columns = 4;

        /// <summary>
        /// Rows on screen at once, per side.
        ///
        /// A CEILING, not a fixed height -- see Shown(). Four is what keeps the whole panel
        /// inside the middle half of the screen at the tile size the pocket screen settled on.
        /// </summary>
        private const int MaxRows = 4;

        private readonly Core.Settings _cfg;
        private readonly Catalogue _menu;
        private readonly Pantry _pantry;
        private readonly Larder _larder;
        private readonly Eating _eating;
        private readonly Fridges _fridges;

        /// <summary>What each side is showing, rebuilt after anything moves.</summary>
        private List<string> _mine = new List<string>();
        private List<string> _cold = new List<string>();

        /// <summary>0 = the pocket, 1 = the fridge.</summary>
        private int _side;

        /// <summary>Where the cursor is on each side, so crossing back returns to it.</summary>
        private readonly int[] _index = new int[2];
        private readonly int[] _page = new int[2];

        private bool _keyWasDown;
        private int _quietUntil;

        /// <summary>The cursor frame that glides between tiles, across the gutter too. See UI.Glide.</summary>
        private readonly Glide _frame = new Glide();

        /// <summary>When the fridge opened, when the cursor last moved, and where it moved from -- see Slot.</summary>
        private int _shownAt;
        private int _pickedAt;
        private int _last = -1;

        public bool IsOpen { get; private set; }

        public FridgeScreen(Core.Settings cfg, Catalogue menu, Pantry pantry, Larder larder,
                            Eating eating, Fridges fridges)
        {
            _cfg = cfg;
            _menu = menu;
            _pantry = pantry;
            _larder = larder;
            _eating = eating;
            _fridges = fridges;
        }

        // ======================================================================

        public void Update(bool suspended)
        {
            try
            {
                if (suspended)
                {
                    if (IsOpen) Close();
                    return;
                }

                if (!_cfg.FridgeEnabled)
                {
                    if (IsOpen) Close();
                    return;
                }

                if (IsOpen)
                {
                    // Walking away shuts it, or you could stand across the kitchen and still
                    // be reaching into the fridge.
                    if (!StillThere()) { Close(); return; }

                    Suppress();
                    Navigate();

                    if (IsOpen) Paint();
                    return;
                }

                Offer();
            }
            catch (Exception ex)
            {
                Log.Once("fridge", "The fridge failed: " + ex.Message);
                IsOpen = false;
            }
        }

        /// <summary>
        /// The interact key's edge, kept true on the frames that do not reach the prompt.
        ///
        /// Pressed() is the only thing that writes it and Offer returns above Pressed three
        /// times, so it went stale the moment you were anywhere but at a fridge. E is also the
        /// game's own context button, so walking with it held is ordinary -- and walking into a
        /// fridge's reach with it held opened the fridge off a press made minutes earlier.
        /// </summary>
        private void Forget()
        {
            _keyWasDown = Game.IsKeyPressed(_cfg.InteractKey);
        }

        /// <summary>The prompt, when you are stood at one on foot with your hands free.</summary>
        private void Offer()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead) { Forget(); return; }

            // Not from a car -- there is no drive-through fridge -- and not mid-meal, for the
            // same reason the counter refuses: a second sandwich while still chewing the first
            // is how a hunger meter gets filled by a queue of overlapping timers.
            if (me.IsInVehicle() || _eating.Busy) { Forget(); return; }

            if (_fridges.Nearest(me.Position, _cfg.FridgeReach) == null) { Forget(); return; }

            Hud.Help("Press ~INPUT_CONTEXT~ to open the fridge.");

            if (!Pressed()) return;

            Hud.ClearHelp();
            Open();
        }

        private bool StillThere()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead || me.IsInVehicle()) return false;

                // A little more than the reach that opened it, so shifting your feet at the
                // fridge door does not slam it in your face.
                return _fridges.Nearest(me.Position, _cfg.FridgeReach + 0.6f) != null;
            }
            catch
            {
                return false;
            }
        }

        public void Open()
        {
            Refill();

            _side = _mine.Count > 0 || _cold.Count == 0 ? 0 : 1;

            _shownAt = Game.GameTime;
            _pickedAt = _shownAt;
            _last = -1;
            _frame.Reset();

            IsOpen = true;
            Sound("SELECT");
        }

        public void Close()
        {
            IsOpen = false;

            // The same 300ms hush the other menus use: a key held through a closing menu is a
            // human holding a key, and humans hold them for about that long.
            _quietUntil = Game.GameTime + 300;

            Hud.ClearHelp();
            Sound("BACK");
        }

        /// <summary>Both lists, and the cursors clamped back into whatever is left.</summary>
        private void Refill()
        {
            _mine = _pantry.Ids();
            _cold = _larder.Ids();

            Settle(0, _mine.Count);
            Settle(1, _cold.Count);

            // If the side the cursor is on has just emptied, move it to the one that has not.
            if (Count(_side) == 0 && Count(1 - _side) > 0) _side = 1 - _side;

            Warm();
        }

        private void Settle(int side, int count)
        {
            if (_index[side] >= count) _index[side] = Math.Max(0, count - 1);
            if (_index[side] < 0) _index[side] = 0;

            var perPage = Columns * Shown();
            _page[side] = perPage <= 0 ? 0 : _index[side] / perPage;
        }

        private List<string> List(int side) => side == 0 ? _mine : _cold;

        private int Count(int side) => List(side).Count;

        private Store Bin(int side) => side == 0 ? (Store)_pantry : _larder;

        /// <summary>The id under the cursor, or null.</summary>
        private string Picked()
        {
            var list = List(_side);
            if (list.Count == 0) return null;

            return list[Clamp(_index[_side], 0, list.Count - 1)];
        }

        // ======================================================================
        // Input
        // ======================================================================

        private bool Pressed()
        {
            var down = false;

            try { down = Game.IsControlJustPressed(GTA.Control.Context); }
            catch { /* fall through to the raw key */ }

            if (!down)
            {
                try { down = Game.IsKeyPressed(_cfg.InteractKey) && !_keyWasDown; }
                catch { down = false; }
            }

            try { _keyWasDown = Game.IsKeyPressed(_cfg.InteractKey); }
            catch { _keyWasDown = false; }

            if (!down) return false;

            return Game.GameTime >= _quietUntil;
        }

        private void Navigate()
        {
            if (Held(GTA.Control.FrontendCancel) || Held(GTA.Control.FrontendPause))
            {
                Close();
                return;
            }

            if (Held(GTA.Control.FrontendRight)) Move(1, 0);
            if (Held(GTA.Control.FrontendLeft)) Move(-1, 0);
            if (Held(GTA.Control.FrontendDown)) Move(0, 1);
            if (Held(GTA.Control.FrontendUp)) Move(0, -1);

            // SPACE MOVES IT, ENTER EATS IT. Two keys, and which direction "move" means is
            // decided by the side the cursor is on rather than by a third key -- there is only
            // one sensible direction from either column.
            if (Held(GTA.Control.Jump)) Shift();

            if (Held(GTA.Control.FrontendAccept)) Eat();
        }

        /// <summary>
        /// Moves the cursor, clamped within a side and CROSSING at the outer edges.
        ///
        /// Clamped rather than wrapped for the reason the pocket screen gives: a grid has two
        /// axes and wrapping either of them is wrong half the time. Crossing is the exception,
        /// and only at the edge that faces the other pane.
        /// </summary>
        private void Move(int dx, int dy)
        {
            var list = List(_side);
            if (list.Count == 0) return;

            var at = Clamp(_index[_side], 0, list.Count - 1);
            var col = at % Columns;

            if (dy != 0)
            {
                var to = at + dy * Columns;
                if (to < 0 || to >= list.Count) return;

                Land(_side, to);
                return;
            }

            // ---- sideways ----
            var last = col == Columns - 1 || at + 1 >= list.Count;

            if (dx > 0)
            {
                if (!last) { Land(_side, at + 1); return; }
                if (_side == 0) Cross(1);
                return;
            }

            if (col > 0) { Land(_side, at - 1); return; }
            if (_side == 1) Cross(0);
        }

        /// <summary>Over to the other pane, to wherever its cursor was left.</summary>
        private void Cross(int side)
        {
            // Nothing to point at and nothing to do once you got there.
            if (Count(side) == 0) return;

            _last = Slot(_side, _index[_side]);
            _side = side;
            Settle(side, Count(side));
            _pickedAt = Game.GameTime;

            Warm();
            Sound("NAV_LEFT_RIGHT");
        }

        private void Land(int side, int to)
        {
            _last = Slot(side, _index[side]);
            _index[side] = to;
            _pickedAt = Game.GameTime;

            var perPage = Columns * Shown();
            _page[side] = perPage <= 0 ? 0 : to / perPage;

            Warm();
            Sound("NAV_UP_DOWN");
        }

        /// <summary>
        /// Asks for the selected item's prop and animation ahead of time.
        ///
        /// The same thing the shelf and the pocket do as the cursor moves, and for the same
        /// reason: a model that is not resident cannot be put in his hand, and nothing in this
        /// mod will block a frame waiting for one.
        /// </summary>
        private void Warm()
        {
            var id = Picked();
            if (id == null) return;

            _eating.Preload(_menu.Find(id));
        }

        // ======================================================================
        // The two things you can do
        // ======================================================================

        /// <summary>
        /// In or out, depending which side the cursor is on.
        ///
        /// TAKEN FIRST AND PUT BACK IF THE OTHER END REFUSES. A full destination is the
        /// ordinary case here -- the pocket holds three things -- so the failure path is not
        /// an edge case, it is most of them, and an item that left one store without arriving
        /// in the other is gone for good.
        /// </summary>
        private void Shift()
        {
            var id = Picked();
            if (id == null) { Sound("ERROR"); return; }

            var from = Bin(_side);
            var to = Bin(1 - _side);

            if (to.Full)
            {
                Notify(_side == 0
                    ? "~y~The fridge is full."
                    : "~y~Your pockets are full.");

                Sound("ERROR");
                return;
            }

            if (!from.Take(id)) { Sound("ERROR"); return; }

            if (!to.Add(id))
            {
                from.Add(id);
                Sound("ERROR");
                return;
            }

            Refill();
            Sound("SELECT");
        }

        private void Eat()
        {
            var id = Picked();
            if (id == null) { Sound("ERROR"); return; }

            if (_eating.Busy) { Sound("ERROR"); return; }

            var item = _menu.Find(id);
            if (item == null) { Sound("ERROR"); return; }

            var from = Bin(_side);

            // Taken first and put back on refusal, exactly as the pocket does it. Eating a
            // thing that stayed in the fridge is a duplication bug and losing one to a refused
            // animation is a theft bug; this has neither.
            if (!from.Take(id)) { Sound("ERROR"); return; }

            if (!_eating.Begin(item))
            {
                from.Add(id);
                Sound("ERROR");
                return;
            }

            // OUT OF THE WAY WHILE HE EATS. The animation is the point and it happens behind
            // this panel; a grid sitting over his own hands is the wrong thing to look at.
            Close();
        }

        private static void Notify(string message)
        {
            try { Core.Compat.Ticker(message); }
            catch { /* the sound already said no */ }
        }

        /// <summary>
        /// The same suppression list the other menus use, and for the same reason: standing at
        /// the fridge with a gun out must not fire it, and SPACE must not vault the counter.
        /// </summary>
        private static void Suppress()
        {
            // ONE LIST, IN Menu. This used to be its own copy of the same seventeen controls,
            // and all three copies were missing the melee inputs -- so closing this panel
            // bare-handed threw a punch and no amount of fixing it here would have reached
            // the till or the pocket.
            Menu.Suppress();
        }

        private static bool Held(GTA.Control control)
        {
            try
            {
                return GTA.Native.Function.Call<bool>(
                    GTA.Native.Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
            }
            catch { return false; }
        }

        private static void Sound(string name)
        {
            try
            {
                GTA.Native.Function.Call(GTA.Native.Hash.PLAY_SOUND_FRONTEND,
                                         -1, name, "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
            }
            catch { /* a menu without a click is still a menu */ }
        }

        // ======================================================================
        // Drawing
        // ======================================================================

        /// <summary>How many rows the grid is this frame: the fuller side, floored at two.</summary>
        private int Shown()
        {
            var most = Math.Max(_mine.Count, _cold.Count);

            var rows = (most + Columns - 1) / Columns;

            if (rows < 2) rows = 2;
            if (rows > MaxRows) rows = MaxRows;

            return rows;
        }

        /// <summary>
        /// One index space across both panes, so a plate can go down on one side while another
        /// comes up on the other as the cursor crosses the gutter.
        /// </summary>
        private static int Slot(int side, int i)
        {
            return side * 1000 + i;
        }

        /// <summary>
        /// The panel: the rounded black, a head, a caption over each pane, the two grids, a card
        /// naming the chosen thing, and the keys as caps. Measured first, drawn second.
        /// </summary>
        private void Paint()
        {
            var arrive = Theme.Arrive(_shownAt, Theme.EnterMs);

            var rows = Shown();

            var tileH = TileH;
            var tileW = Hud.ToX(TileH);

            // The panel is the width of its two panes, not the other way round.
            var paneW = tileW * Columns;
            var wide = paneW * 2f + Gutter;
            var panelW = wide + Pad * 2f;

            var left = 0.5f - panelW / 2f;
            var top = Top + Theme.EnterRise * (1f - arrive);

            var x = left + Pad;
            var right = left + panelW - Pad;

            var height = Kit.HeadH + CapH + rows * tileH + GridPad + CardH + Kit.FootH;

            Theme.Panel(left, top, panelW, height, arrive);

            var y = Kit.Head(left, top, panelW, Pad, IconCache.Get("s_layout.png"), "THE FRIDGE",
                             "what you are carrying, and what is keeping cold", null, null, arrive, 0f);

            _frame.Begin();

            var coldX = x + paneW + Gutter;

            Caption(x, y, paneW, "POCKET", _pantry, 0, Palette.Brand, arrive);
            Caption(coldX, y, paneW, "FRIDGE", _larder, 1, Palette.Cold, arrive);

            y += CapH;

            Pane(x, y, paneW, tileW, tileH, rows, 0, arrive);
            Pane(coldX, y, paneW, tileW, tileH, rows, 1, arrive);

            y += rows * tileH + GridPad;

            Card(x, y, wide, arrive);

            Foot(x, right, top + height - Kit.FootH, arrive);

            // Last, so it rides over the tile it is pointing at.
            _frame.Draw(arrive);
        }

        /// <summary>
        /// A pane's name and count, with a rule under them carrying the pane's own colour on
        /// its stroke: amber for the pocket, cold blue for the fridge. The live side is lit and
        /// the other dimmed, which is what says where the cursor is when no tile is under it.
        /// </summary>
        private void Caption(float x, float y, float w, string label, Store store, int side,
                             Color tint, float arrive)
        {
            var live = _side == side;

            Hud.Text(label, x, y + 0.003f, 0.28f,
                     Palette.Alpha(live ? tint : Palette.TextDim, (int)((live ? 245f : 170f) * arrive)),
                     Hud.FontLabel);

            // Carried out of capacity. A store CAN read over its own cap -- the slot counts are
            // settings and lowering one does not take anything off you -- so this is amber when
            // it does rather than pretending the number is impossible.
            var held = store.Total;
            var slots = store.Slots;

            Hud.TextRight(held + " of " + slots, x + w, y + 0.004f, 0.25f,
                          Palette.Alpha(held >= slots ? Palette.Warn : Palette.TextDim, (int)(215f * arrive)),
                          Hud.FontLabel);

            Theme.Rule(x, y + CapH - 0.005f, w, tint, arrive);
        }

        private void Pane(float x, float y, float w, float tileW, float tileH, int rows, int side,
                          float arrive)
        {
            var list = List(side);

            if (list.Count == 0)
            {
                Hud.Text(side == 0 ? "Nothing on you." : "The fridge is empty.",
                         x + w / 2f, y + rows * tileH / 2f - 0.012f, 0.30f,
                         Palette.Alpha(Palette.TextDim, (int)(180f * arrive)), Hud.FontBody, true);
                return;
            }

            var perPage = Columns * rows;
            var first = _page[side] * perPage;

            var age = Game.GameTime - _shownAt;
            var grown = Theme.Grown(_pickedAt);

            var selected = Slot(_side, _index[_side]);

            for (var i = 0; i < perPage; i++)
            {
                var at = first + i;
                if (at >= list.Count) break;

                var col = i % Columns;
                var row = i / Columns;

                // The fridge unpacks a beat after the pocket, so the two sides arrive as two things.
                var land = Kit.Landed(age, i * 35 + side * 40, Theme.EnterMs);

                var show = arrive * land;
                if (show <= 0.01f) continue;

                var tx = x + col * tileW;
                var ty = y + row * tileH + Theme.EnterRise * 0.5f * (1f - land);

                var here = side == _side && at == _index[side];
                var lit = Theme.Lit(Slot(side, at), selected, _last, grown);

                Tile(list[at], Bin(side), tx, ty, tileW, tileH, lit, show, i, here, grown);

                if (here)
                {
                    _frame.Target(tx + Gap, ty + Gap * Hud.Aspect,
                                  tileW - Gap * 2f, tileH - Gap * 2f * Hud.Aspect);
                }
            }

            // Which page, only when there is more than one. A fridge of forty is three
            // screenfuls and the grid on its own has no way at all of saying so.
            var pages = (list.Count + perPage - 1) / perPage;
            if (pages <= 1) return;

            Hud.TextRight((_page[side] + 1) + " / " + pages, x + w - 0.004f, y + rows * tileH - 0.018f,
                          0.22f, Palette.Alpha(Palette.TextDim, (int)(190f * arrive)), Hud.FontLabel);
        }

        /// <summary>The same tile the pocket draws, so a thing looks the same on both sides of the gutter.</summary>
        private void Tile(string id, Store store, float x, float y, float w, float h, float lit,
                          float show, int slot, bool picked, float grown)
        {
            var gx = Gap;
            var gy = Gap * Hud.Aspect;

            var tx = x + gx;
            var ty = y + gy;
            var tw = w - gx * 2f;
            var th = h - gy * 2f;

            // The dark ground, and the plate coming up under the cursor. No hairline along the
            // top and no light crossing it: at tile size those were chrome, not information.
            Hud.Bar(tx, ty, tw, th, Color.FromArgb((int)(26f * show), 255, 255, 255));

            Theme.Plate(tx, ty, tw, th, lit * show);

            var item = _menu.Find(id);
            if (item == null) return;

            var icon = IconCache.Get(Food.Art.For(item));

            if (icon != null && !icon.Missing)
            {
                var swell = picked ? 1f + PickGrow * grown : 1f;

                var tall = th * 0.56f * swell;
                var wideIcon = Hud.ToX(tall);

                // The item's own colour on the dark tile, brightening toward white as the
                // plate comes up. One white file does all of it.
                var tint = Sheen.On(item.Tint, slot * 0.11f, lit < 0.5f ? Shimmer() : 0f);

                var ink = Theme.Ink(Palette.Alpha(tint, (int)(238f * show)), lit);

                icon.DrawSized(tx + tw / 2f, ty + th * 0.46f, wideIcon, tall, ink);
            }

            // How many, for every stack including a single one: "1" says this is the last one,
            // and a badge that only appears at two reads as an error the first time it shows.
            var n = store.CountOf(id);

            var chipW = Hud.ToX(0.013f);
            const float chipH = 0.013f;

            Hud.Bar(tx + tw - chipW, ty + th - chipH, chipW, chipH,
                    Color.FromArgb((int)(215f * show), 12, 12, 15));

            Hud.TextRight(n.ToString(), tx + tw - 0.0015f, ty + th - chipH + 0.0005f, 0.22f,
                          Palette.Alpha(Palette.Text, (int)(255f * show)), Hud.FontLabel, false);
        }

        private float Shimmer()
        {
            return _cfg.HudAnimate ? _cfg.HudShimmer * 0.6f : 0f;
        }

        /// <summary>The card under the grids: the chosen thing named, sliding in with its plate.</summary>
        private void Card(float x, float y, float wide, float arrive)
        {
            // The same plate every chosen thing sits on, faint, so the name and the line under
            // it read as one card rather than two lines of text loose on the panel.
            Theme.Plate(x, y, wide, CardH - 0.006f, 0.55f * arrive);

            var tx = x + 0.010f;

            var id = Picked();
            var item = id == null ? null : _menu.Find(id);

            if (item == null)
            {
                Hud.Text("Nothing here to take.", tx, y + 0.008f, 0.30f,
                         Palette.Alpha(Palette.TextDim, (int)(210f * arrive)), Hud.FontBody);

                Hud.Text("Buy something and it turns up in your pocket.", tx, y + DescTop, DescScale,
                         Palette.Alpha(Palette.TextDim, (int)(170f * arrive)), Hud.FontBody);
                return;
            }

            var grown = Theme.Grown(_pickedAt);

            Theme.Caption(item.Name, tx, y + 0.008f, grown, 0.32f);

            if (!string.IsNullOrEmpty(item.Desc))
            {
                Hud.Text(Kit.Fit(item.Desc, wide - 0.014f, 0.25f, Hud.FontBody), tx, y + 0.031f, 0.25f,
                         Palette.Alpha(Palette.TextDim, (int)((110f + 90f * grown) * arrive)),
                         Hud.FontBody);
            }
        }

        /// <summary>
        /// The keys as caps. WHICH WAY SPACE MOVES THINGS IS SPELLED OUT rather than left to an
        /// arrow, because it is the one thing about this screen that is not obvious from
        /// looking at it -- and it changes with the side the cursor is on.
        /// </summary>
        private void Foot(float x, float right, float y, float arrive)
        {
            Theme.Rule(x, y, right - x, arrive);

            var ky = y + 0.011f;

            Kit.KeyRight(right, ky, Kit.Back, "DONE", arrive);

            var id = Picked();
            if (id == null) return;

            var item = _menu.Find(id);

            var kx = Kit.Key(x, ky, null, "arrow_leftright.png", "PICK", arrive);

            kx = Kit.Key(kx, ky, Kit.Drop, null, _side == 0 ? "PUT IT IN" : "TAKE IT OUT", arrive);

            var verb = item == null ? "EAT IT"
                     : item.Smoke ? "SMOKE IT"
                     : item.Drink ? "DRINK IT"
                                  : "EAT IT";

            Kit.Key(kx, ky, Kit.Confirm, null, verb, arrive);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}
