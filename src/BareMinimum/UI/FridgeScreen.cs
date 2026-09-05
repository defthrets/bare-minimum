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
        private const float PanelW = 0.46f;
        private const float Top = 0.150f;

        private const float HeaderH = 0.052f;
        private const float CapH = 0.028f;
        private const float NoteH = 0.070f;

        /// <summary>The dark seam between the two panes, so they read as two things.</summary>
        private const float Gutter = 0.008f;

        private const int Columns = 4;

        /// <summary>
        /// Rows on screen at once, per side.
        ///
        /// A CEILING, not a fixed height -- see Rows(). Four is what keeps the whole panel
        /// inside the middle half of the screen at the tile size the pocket screen settled on.
        /// </summary>
        private const int MaxRows = 4;

        private const int Plain = 4;

        private static readonly Color Accent = Color.FromArgb(255, 240, 170, 56);

        /// <summary>The fridge's own colour, so the two sides are told apart at a glance.</summary>
        private static readonly Color Cold = Color.FromArgb(255, 120, 198, 226);

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

        private float _aspect;

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

        /// <summary>The prompt, when you are stood at one on foot with your hands free.</summary>
        private void Offer()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead) return;

            // Not from a car -- there is no drive-through fridge -- and not mid-meal, for the
            // same reason the counter refuses: a second sandwich while still chewing the first
            // is how a hunger meter gets filled by a queue of overlapping timers.
            if (me.IsInVehicle() || _eating.Busy) return;

            if (_fridges.Nearest(me.Position, _cfg.FridgeReach) == null) return;

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

            _side = side;
            Settle(side, Count(side));

            Warm();
            Sound("NAV_LEFT_RIGHT");
        }

        private void Land(int side, int to)
        {
            _index[side] = to;

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
            try { GTA.UI.Notification.PostTicker(message, false, false); }
            catch { /* the sound already said no */ }
        }

        /// <summary>
        /// The same suppression list the other menus use, and for the same reason: standing at
        /// the fridge with a gun out must not fire it, and SPACE must not vault the counter.
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

        private float Aspect()
        {
            if (_aspect > 0f) return _aspect;

            try
            {
                var res = GTA.UI.Screen.Resolution;
                if (res.Height > 0) _aspect = res.Width / (float)res.Height;
            }
            catch { /* fall through */ }

            if (_aspect <= 0f) _aspect = 16f / 9f;

            return _aspect;
        }

        /// <summary>How many rows the grid is this frame: the fuller side, floored at two.</summary>
        private int Shown()
        {
            var most = Math.Max(_mine.Count, _cold.Count);

            var rows = (most + Columns - 1) / Columns;

            if (rows < 2) rows = 2;
            if (rows > MaxRows) rows = MaxRows;

            return rows;
        }

        private void Paint()
        {
            var left = 0.5f - PanelW / 2f;
            var y = Top;

            var rows = Shown();

            var paneW = (PanelW - Gutter) / 2f;
            var tileW = paneW / Columns;
            var tileH = tileW * Aspect();

            // ---- header ----
            Hud.Bar(left, y, PanelW, HeaderH, Color.FromArgb(238, 12, 12, 15));

            Hud.Text("THE FRIDGE", 0.5f, y + 0.006f, 0.52f,
                     Color.FromArgb(255, 240, 240, 246), Plain, true);

            Hud.Text("what you are carrying, and what is keeping cold", 0.5f, y + 0.030f, 0.26f,
                     Color.FromArgb(200, 190, 190, 198), Plain, true);

            Hud.Bar(left, y + HeaderH - 0.0022f, PanelW, 0.0022f, Accent);

            y += HeaderH;

            // ---- the two captions ----
            Hud.Bar(left, y, PanelW, CapH, Color.FromArgb(232, 16, 16, 20));

            Caption(left, y, paneW, "POCKET", _pantry, 0, Accent);
            Caption(left + paneW + Gutter, y, paneW, "FRIDGE", _larder, 1, Cold);

            y += CapH;

            // ---- the two grids ----
            Pane(left, y, paneW, tileW, tileH, rows, 0);
            Pane(left + paneW + Gutter, y, paneW, tileW, tileH, rows, 1);

            // The seam, drawn last so neither pane's ground creeps over it.
            Hud.Bar(left + paneW, y - CapH, Gutter, CapH + rows * tileH,
                    Color.FromArgb(238, 8, 8, 10));

            y += rows * tileH;

            Footer(left, y);
        }

        private void Caption(float x, float y, float w, string label, Store store, int side,
                             Color tint)
        {
            var live = _side == side;

            Hud.Text(label, x + 0.008f, y + 0.005f, 0.30f,
                     live ? tint : Color.FromArgb(170, 180, 180, 188), Plain);

            // Carried out of capacity. A store CAN read over its own cap -- the slot counts are
            // settings and lowering one does not take anything off you -- so this is amber when
            // it does rather than pretending the number is impossible.
            var held = store.Total;
            var slots = store.Slots;

            Hud.Text(held + " of " + slots, x + w - 0.008f, y + 0.005f, 0.28f,
                     held >= slots ? Color.FromArgb(235, 240, 170, 56)
                                   : Color.FromArgb(205, 196, 196, 204),
                     Plain, false, true);
        }

        private void Pane(float x, float y, float w, float tileW, float tileH, int rows, int side)
        {
            var list = List(side);

            Hud.Bar(x, y, w, rows * tileH, Color.FromArgb(228, 18, 18, 22));

            if (list.Count == 0)
            {
                Hud.Text(side == 0 ? "Nothing on you." : "The fridge is empty.",
                         x + w / 2f, y + rows * tileH / 2f - 0.012f, 0.32f,
                         Color.FromArgb(180, 175, 175, 184), Plain, true);
                return;
            }

            var perPage = Columns * rows;
            var first = _page[side] * perPage;

            for (var i = 0; i < perPage; i++)
            {
                var at = first + i;
                if (at >= list.Count) break;

                var col = i % Columns;
                var row = i / Columns;

                var here = side == _side && at == _index[side];

                Tile(list[at], Bin(side), x + col * tileW, y + row * tileH, tileW, tileH, here, i);
            }

            // ---- which page ----
            //
            // Only when there is more than one. A fridge of forty is three screenfuls and the
            // grid on its own has no way at all of saying so.
            var pages = (list.Count + perPage - 1) / perPage;
            if (pages <= 1) return;

            Hud.Text((_page[side] + 1) + " / " + pages,
                     x + w - 0.006f, y + rows * tileH - 0.020f, 0.24f,
                     Color.FromArgb(190, 190, 190, 200), Plain, false, true);
        }

        private void Tile(string id, Store store, float x, float y, float w, float h,
                          bool selected, int slot)
        {
            var item = _menu.Find(id);

            Hud.Bar(x + 0.0016f, y + 0.0016f * Aspect(), w - 0.0032f, h - 0.0032f * Aspect(),
                    selected ? Color.FromArgb(242, 240, 170, 56)
                             : Color.FromArgb(215, 30, 30, 36));

            if (item == null) return;

            var icon = IconCache.Get("p_" + item.Icon + ".png");

            if (icon != null && !icon.Missing)
            {
                var tall = h * 0.56f;
                var wide = tall / Aspect();

                // ITEM TINT ON A DARK TILE, NEAR-BLACK ON THE AMBER ONE. The art is white and
                // CustomSprite MULTIPLIES, so one file does both -- and a taco's own warm brown
                // over a full-strength amber highlight is mud.
                var ink = selected
                    ? Color.FromArgb(255, 20, 18, 14)
                    : Sheen.On(item.Tint, slot * 0.11f, _cfg.HudAnimate ? _cfg.HudShimmer * 0.6f : 0f);

                icon.DrawSized(x + w / 2f, y + h * 0.44f, wide, tall, ink);
            }

            // How many, for every stack including a single one: "1" says this is the last one,
            // and a badge that only appears at two reads as an error the first time it shows.
            var n = store.CountOf(id);

            Hud.Text(n.ToString(), x + w - 0.006f, y + h - 0.021f, 0.30f,
                     selected ? Color.FromArgb(255, 26, 22, 16)
                              : Color.FromArgb(235, 238, 238, 244),
                     Plain, false, true, !selected);
        }

        private void Footer(float left, float y)
        {
            Hud.Bar(left, y, PanelW, NoteH, Color.FromArgb(236, 12, 12, 15));
            Hud.Bar(left, y, PanelW, 0.0018f, Color.FromArgb(190, 240, 170, 56));

            var id = Picked();
            var item = id == null ? null : _menu.Find(id);

            if (item != null)
            {
                Hud.Text(item.Name, left + 0.010f, y + 0.005f, 0.36f,
                         Color.FromArgb(245, 240, 240, 246), Plain);

                var verb = item.Smoke ? "ENTER smoke it"
                         : item.Drink ? "ENTER drink it"
                                      : "ENTER eat it";

                Hud.Text(verb, left + 0.010f, y + 0.028f, 0.27f,
                         Color.FromArgb(205, 196, 196, 204), Plain);

                // THE DIRECTION IS SPELLED OUT rather than left to the arrow, because which way
                // SPACE moves things is the one thing about this screen that is not obvious
                // from looking at it.
                Hud.Text(_side == 0 ? "SPACE put it in the fridge" : "SPACE take it out",
                         left + 0.010f, y + 0.048f, 0.27f,
                         Color.FromArgb(205, 196, 196, 204), Plain);
            }
            else
            {
                Hud.Text("Nothing here to take.", left + 0.010f, y + 0.005f, 0.34f,
                         Color.FromArgb(210, 196, 196, 204), Plain);

                Hud.Text("Buy something and it turns up in your pocket.",
                         left + 0.010f, y + 0.030f, 0.27f,
                         Color.FromArgb(190, 180, 180, 188), Plain);
            }

            Hud.Text("LEFT / RIGHT cross over", left + PanelW - 0.010f, y + 0.028f, 0.25f,
                     Color.FromArgb(175, 175, 175, 185), Plain, false, true);

            Hud.Text("BACKSPACE to close", left + PanelW - 0.010f, y + 0.048f, 0.25f,
                     Color.FromArgb(175, 175, 175, 185), Plain, false, true);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}
