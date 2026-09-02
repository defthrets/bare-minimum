using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using BareMinimum.Core;
using BareMinimum.Food;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.UI
{
    /// <summary>
    /// The pocket: a grid of what you are carrying, and the way to eat it.
    ///
    /// A GRID RATHER THAN A LIST, which is the one structural decision here and the opposite
    /// of the one the settings menu made. The rule both follow is the same: a grid is for
    /// things you recognise by shape and a list is for things you read. Every item in this mod
    /// already has a picture drawn for it -- a taco, a can, a packet of cigarettes -- so a
    /// pocket of eight things is eight silhouettes you learn the position of, and a caption is
    /// only needed for the one under the cursor. Thirty-nine settings are thirty-nine
    /// sentences and get a list.
    ///
    /// NOT BUILT ON Menu. That class is a list from top to bottom -- one index, one scroll
    /// offset, a row height and a note strip -- and a grid needs two axes, a page rather than a
    /// scroll, and tiles rather than rows. Bending it into both shapes would have left one
    /// class doing two jobs badly and made every future change to either one a question about
    /// the other. What is shared is the drawing vocabulary underneath, which is where the
    /// sharing is worth having.
    ///
    /// It draws NOTHING when the pocket is empty except a line saying so. An empty grid of
    /// twenty outlined squares looks like a thing that is broken rather than a thing that is
    /// empty.
    /// </summary>
    internal sealed class Bag
    {
        private const float PanelW = 0.34f;
        private const float Top = 0.170f;

        private const float HeaderH = 0.052f;
        private const float NoteH = 0.062f;

        private const int Columns = 5;

        /// <summary>Rows on screen. Twenty tiles, which is the default pocket exactly.</summary>
        private const int Rows = 4;

        private const int Plain = 4;

        private static readonly Color Accent = Color.FromArgb(255, 240, 170, 56);

        private readonly Core.Settings _cfg;
        private readonly Catalogue _menu;
        private readonly Pantry _pantry;
        private readonly Eating _eating;

        private List<string> _ids = new List<string>();

        private int _index;
        private int _page;

        private bool _down;
        private int _quietUntil;

        private float _aspect;

        public bool IsOpen { get; private set; }

        public Bag(Core.Settings cfg, Catalogue menu, Pantry pantry, Eating eating)
        {
            _cfg = cfg;
            _menu = menu;
            _pantry = pantry;
            _eating = eating;
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

                if (Toggled())
                {
                    if (IsOpen) Close();
                    else Open();
                }

                if (!IsOpen) return;

                Suppress();
                Navigate();

                if (IsOpen) Paint();
            }
            catch (Exception ex)
            {
                Log.Once("bag", "The pocket failed: " + ex.Message);
                IsOpen = false;
            }
        }

        public void Open()
        {
            Refill();

            _index = 0;
            _page = 0;

            IsOpen = true;
            Sound("SELECT");
        }

        public void Close()
        {
            IsOpen = false;

            // The same 300ms hush the other menus use. A key held through a closing menu is a
            // human holding a key, and humans hold them for about that long.
            _quietUntil = Game.GameTime + 300;

            Sound("BACK");
        }

        private void Refill()
        {
            _ids = _pantry.Ids();

            if (_index >= _ids.Count) _index = Math.Max(0, _ids.Count - 1);

            var perPage = Columns * Rows;
            _page = perPage <= 0 ? 0 : _index / perPage;
        }

        // ======================================================================
        // Input
        // ======================================================================

        private bool Toggled()
        {
            var down = false;

            try { down = Game.IsKeyPressed(_cfg.BagKey); }
            catch { /* a key that cannot be read is a key that is not pressed */ }

            var edge = down && !_down;
            _down = down;

            if (!edge) return false;
            if (Game.GameTime < _quietUntil) return false;

            return true;
        }

        private void Navigate()
        {
            if (Pressed(GTA.Control.FrontendCancel) || Pressed(GTA.Control.FrontendPause))
            {
                Close();
                return;
            }

            if (_ids.Count > 0)
            {
                if (Pressed(GTA.Control.FrontendRight)) Move(1);
                if (Pressed(GTA.Control.FrontendLeft)) Move(-1);
                if (Pressed(GTA.Control.FrontendDown)) Move(Columns);
                if (Pressed(GTA.Control.FrontendUp)) Move(-Columns);
            }

            if (!Pressed(GTA.Control.FrontendAccept)) return;

            Eat();
        }

        /// <summary>
        /// Moves the cursor, CLAMPED rather than wrapped.
        ///
        /// A list wraps because it has one axis and the ends are unambiguous. A grid does not:
        /// pressing Right on the last tile of a row could reasonably go to the start of the
        /// same row or the start of the next one, and both are wrong half the time. Stopping is
        /// the one behaviour nobody has to learn.
        /// </summary>
        private void Move(int by)
        {
            var to = _index + by;
            if (to < 0 || to >= _ids.Count) return;

            _index = to;

            var perPage = Columns * Rows;
            _page = perPage <= 0 ? 0 : _index / perPage;

            Sound("NAV_UP_DOWN");
        }

        private void Eat()
        {
            if (_ids.Count == 0) return;

            if (_eating.Busy)
            {
                Sound("ERROR");
                return;
            }

            var id = _ids[Clamp(_index, 0, _ids.Count - 1)];

            var item = _menu.Find(id);
            if (item == null) { Sound("ERROR"); return; }

            // TAKEN FIRST, AND PUT BACK IF IT WILL NOT START. Begin can refuse -- mid-meal, in
            // a vehicle the animation cannot play in -- and eating a thing that stayed in your
            // pocket is a duplication bug, while losing one to a refused animation is a theft
            // bug. Neither, this way.
            if (!_pantry.Take(id)) { Sound("ERROR"); return; }

            if (!_eating.Begin(item))
            {
                _pantry.Add(id);
                Sound("ERROR");
                return;
            }

            Refill();

            // OUT OF THE WAY WHILE YOU EAT. The animation is the point of the purchase and it
            // happens behind this panel; a grid sitting over your own hands is the wrong thing
            // to be looking at.
            Close();
        }

        /// <summary>
        /// The same suppression list the other menus use, and for the same reason: opening a
        /// pocket with a gun out must not fire it.
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

        private static bool Pressed(GTA.Control control)
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

        private void Paint()
        {
            var left = 0.5f - PanelW / 2f;
            var y = Top;

            // ---- header ----
            Hud.Bar(left, y, PanelW, HeaderH, Color.FromArgb(238, 12, 12, 15));

            Hud.Text("POCKET", 0.5f, y + 0.006f, 0.52f,
                     Color.FromArgb(255, 240, 240, 246), Plain, true);

            var carried = _pantry.Total + " of " + _pantry.Slots;

            Hud.Text(carried, 0.5f, y + 0.030f, 0.28f,
                     Color.FromArgb(210, 196, 196, 204), Plain, true);

            Hud.Bar(left, y + HeaderH - 0.0022f, PanelW, 0.0022f, Accent);

            y += HeaderH;

            // ---- the tiles ----
            var tileW = PanelW / Columns;
            var tileH = tileW * Aspect();

            if (_ids.Count == 0)
            {
                Hud.Bar(left, y, PanelW, tileH, Color.FromArgb(228, 18, 18, 22));

                Hud.Text("Nothing on you.", 0.5f, y + tileH / 2f - 0.012f, 0.34f,
                         Color.FromArgb(190, 180, 180, 188), Plain, true);

                y += tileH;
                Footer(left, y, null);
                return;
            }

            var perPage = Columns * Rows;
            var first = _page * perPage;

            var shownRows = (int)Math.Ceiling(Math.Min(perPage, _ids.Count - first) / (float)Columns);
            if (shownRows < 1) shownRows = 1;

            Hud.Bar(left, y, PanelW, shownRows * tileH, Color.FromArgb(228, 18, 18, 22));

            for (var i = 0; i < perPage; i++)
            {
                var at = first + i;
                if (at >= _ids.Count) break;

                var col = i % Columns;
                var row = i / Columns;

                Tile(_ids[at], left + col * tileW, y + row * tileH, tileW, tileH, at == _index, i);
            }

            y += shownRows * tileH;

            Footer(left, y, _menu.Find(_ids[Clamp(_index, 0, _ids.Count - 1)]));
        }

        private void Tile(string id, float x, float y, float w, float h, bool selected, int slot)
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
                // CustomSprite MULTIPLIES, so the same file does both -- and a taco's own warm
                // brown over a full-strength amber highlight is mud, which is the whole reason
                // the selected tile ignores the tint.
                var ink = selected
                    ? Color.FromArgb(255, 20, 18, 14)
                    : Sheen.On(item.Tint, slot * 0.11f, _cfg.HudAnimate ? _cfg.HudShimmer * 0.6f : 0f);

                icon.DrawSized(x + w / 2f, y + h * 0.44f, wide, tall, ink);
            }

            // ---- how many ----
            //
            // Drawn for every stack including a single one. "1" is information -- it says this
            // is the last taco -- and a badge that appears only at two reads as an error state
            // the first time it shows up.
            var n = _pantry.CountOf(id);

            Hud.Text(n.ToString(), x + w - 0.006f, y + h - 0.021f, 0.30f,
                     selected ? Color.FromArgb(255, 26, 22, 16)
                              : Color.FromArgb(235, 238, 238, 244),
                     Plain, false, true, !selected);
        }

        private void Footer(float left, float y, Item item)
        {
            Hud.Bar(left, y, PanelW, NoteH, Color.FromArgb(236, 12, 12, 15));
            Hud.Bar(left, y, PanelW, 0.0018f, Color.FromArgb(190, 240, 170, 56));

            if (item != null)
            {
                Hud.Text(item.Name, left + 0.010f, y + 0.006f, 0.36f,
                         Color.FromArgb(245, 240, 240, 246), Plain);

                var says = item.Smoke ? "Enter to smoke it."
                         : item.Drink ? "Enter to drink it."
                                      : "Enter to eat it.";

                Hud.Text(says, left + 0.010f, y + 0.030f, 0.28f,
                         Color.FromArgb(200, 190, 190, 198), Plain);
            }
            else
            {
                Hud.Text("Buy something and it turns up here.", left + 0.010f, y + 0.006f, 0.30f,
                         Color.FromArgb(200, 190, 190, 198), Plain);
            }

            Hud.Text("BACKSPACE to close", left + PanelW - 0.010f, y + 0.030f, 0.26f,
                     Color.FromArgb(170, 170, 170, 180), Plain, false, true);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}
