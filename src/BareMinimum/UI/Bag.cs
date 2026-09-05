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
        private const float Top = 0.170f;

        /// <summary>
        /// How tall a tile is, as a fraction of the screen's height. Its width follows through
        /// the aspect, so it is square on screen.
        ///
        /// SIZED BY HEIGHT, NOT BY SHARE OF THE PANEL. A tile that was a fifth of a panel a
        /// third of the screen wide was a fifth of a third of a 21:9 monitor, which is a tile
        /// the size of a hand with a small picture lost in the middle of it. The panel is now
        /// as wide as five of these plus its padding, and no wider.
        /// </summary>
        private const float TileH = 0.105f;

        /// <summary>Air between the panel's edge and anything in it, and above and below the grid.</summary>
        private const float Pad = 0.012f;
        private const float GridPad = 0.006f;

        /// <summary>The card under the grid: the chosen thing, said properly.</summary>
        private const float CardH = 0.052f;

        /// <summary>The gap between tiles, as an x fraction. Turned into y through the aspect.</summary>
        private const float Gap = 0.0018f;

        /// <summary>How much the chosen picture swells as its plate comes up.</summary>
        private const float PickGrow = 0.10f;

        private const int Columns = 5;

        /// <summary>Rows on screen. Twenty tiles, which is the default pocket exactly.</summary>
        private const int Rows = 4;

        private readonly Core.Settings _cfg;
        private readonly Catalogue _menu;
        private readonly Pantry _pantry;
        private readonly Eating _eating;

        private List<string> _ids = new List<string>();

        private int _index;
        private int _page;

        private bool _down;
        private int _quietUntil;

        /// <summary>The cursor frame that glides between tiles. See UI.Glide.</summary>
        private readonly Glide _frame = new Glide();

        /// <summary>When the pocket opened, when the cursor last moved, and where it moved from.</summary>
        private int _shownAt;
        private int _pickedAt;
        private int _last = -1;

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

            // The same 300ms hush the other menus use. A key held through a closing menu is a
            // human holding a key, and humans hold them for about that long.
            _quietUntil = Game.GameTime + 300;

            Sound("BACK");
        }

        private void Refill()
        {
            _ids = _pantry.Ids();

            if (_index >= _ids.Count) _index = Math.Max(0, _ids.Count - 1);

            Warm();

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

            _last = _index;
            _index = to;
            _pickedAt = Game.GameTime;

            var perPage = Columns * Rows;
            _page = perPage <= 0 ? 0 : _index / perPage;

            Warm();

            Sound("NAV_UP_DOWN");
        }

        /// <summary>
        /// Asks for the selected item's prop and animation ahead of time.
        ///
        /// The same thing the shop shelf does as the cursor moves over a row, and for the same
        /// reason: a model that is not resident cannot be put in his hand, and the mod will not
        /// block a frame waiting for one. Eating retries every frame now so nothing is ever
        /// mimed for long -- but a prop that is already there when the key is pressed appears
        /// on the first frame rather than the third, and that is the difference between right
        /// and nearly right.
        /// </summary>
        private void Warm()
        {
            if (_ids.Count == 0) return;

            var id = _ids[Clamp(_index, 0, _ids.Count - 1)];

            _eating.Preload(_menu.Find(id));
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
            // ONE LIST, IN Menu. This used to be its own copy of the same seventeen controls,
            // and all three copies were missing the melee inputs -- so closing this panel
            // bare-handed threw a punch and no amount of fixing it here would have reached
            // the till or the pocket.
            Menu.Suppress();
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

        /// <summary>
        /// The panel: the rounded black every panel in this mod and in Hoodrich is, a head with
        /// the pocket's mark and its count, the tiles, a card naming the chosen thing, and the
        /// keys as caps. Measured first, drawn second, because the panel is the ground.
        /// </summary>
        private void Paint()
        {
            var arrive = Theme.Arrive(_shownAt, Theme.EnterMs);

            var tileH = TileH;
            var tileW = Hud.ToX(TileH);

            // The panel is the width of its tiles, not the other way round.
            var wide = tileW * Columns;
            var panelW = wide + Pad * 2f;

            var left = 0.5f - panelW / 2f;
            var top = Top + Theme.EnterRise * (1f - arrive);

            var x = left + Pad;
            var right = left + panelW - Pad;

            var perPage = Columns * Rows;
            var first = _page * perPage;

            var onPage = Math.Max(0, Math.Min(perPage, _ids.Count - first));
            var shownRows = onPage == 0 ? 1 : (onPage + Columns - 1) / Columns;

            var height = Kit.HeadH + GridPad + shownRows * tileH + GridPad + CardH + Kit.FootH;

            Theme.Panel(left, top, panelW, height, arrive);

            var y = Kit.Head(left, top, panelW, Pad, IconCache.Get("p_bag.png"), "POCKET",
                             "what you are carrying", null, _pantry.Total + " of " + _pantry.Slots,
                             arrive, 0f);

            y += GridPad;

            _frame.Begin();

            Item item = null;

            if (onPage == 0)
            {
                Hud.Text("Nothing on you.", x + wide / 2f, y + tileH / 2f - 0.012f, 0.30f,
                         Palette.Alpha(Palette.TextDim, (int)(200f * arrive)), Hud.FontBody, true);
            }
            else
            {
                var age = Game.GameTime - _shownAt;
                var grown = Theme.Grown(_pickedAt);

                for (var i = 0; i < onPage; i++)
                {
                    var at = first + i;

                    var col = i % Columns;
                    var row = i / Columns;

                    // Staggered a frame or two apart, so the pocket is unpacked rather than
                    // switched on.
                    var land = Kit.Landed(age, i * 35, Theme.EnterMs);

                    var show = arrive * land;
                    if (show <= 0.01f) continue;

                    var tx = x + col * tileW;
                    var ty = y + row * tileH + Theme.EnterRise * 0.5f * (1f - land);

                    var picked = at == _index;
                    var lit = Theme.Lit(at, _index, _last, grown);

                    Tile(_ids[at], tx, ty, tileW, tileH, lit, show, i, picked, grown);

                    if (picked)
                    {
                        _frame.Target(tx + Gap, ty + Gap * Hud.Aspect,
                                      tileW - Gap * 2f, tileH - Gap * 2f * Hud.Aspect);
                    }
                }

                item = _menu.Find(_ids[Clamp(_index, 0, _ids.Count - 1)]);
            }

            y += shownRows * tileH + GridPad;

            Card(x, y, wide, item, arrive);

            Foot(x, right, top + height - Kit.FootH, item, arrive);

            // Last, so it rides over the tile it is pointing at.
            _frame.Draw(arrive);
        }

        /// <summary>
        /// One square: the dark ground with a hairline along its top, the amber coming up under
        /// the cursor and going down where it left, the light crossing the lit one. The same
        /// tile Hoodrich draws, in this mod's colour.
        /// </summary>
        private void Tile(string id, float x, float y, float w, float h, float lit, float show,
                          int slot, bool picked, float grown)
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

            var icon = IconCache.Get("p_" + item.Icon + ".png");

            if (icon != null && !icon.Missing)
            {
                var swell = picked ? 1f + PickGrow * grown : 1f;

                var tall = th * 0.56f * swell;
                var wideIcon = Hud.ToX(tall);

                // The item's own colour on the dark tile, brightening toward white as the
                // plate comes up. The art is white and CustomSprite MULTIPLIES, so one file
                // does all of it.
                var tint = Sheen.On(item.Tint, slot * 0.11f, lit < 0.5f ? Shimmer() : 0f);

                var ink = Theme.Ink(Palette.Alpha(tint, (int)(238f * show)), lit);

                icon.DrawSized(tx + tw / 2f, ty + th * 0.46f, wideIcon, tall, ink);
            }

            // How many, on a chip in the corner. Drawn for every stack including a single one:
            // "1" says this is the last taco, and a badge that appears only at two reads as an
            // error state the first time it shows up.
            var n = _pantry.CountOf(id);

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

        /// <summary>
        /// The card under the tiles: a tile is a glance, this is where it becomes words. The
        /// name slides in from the left as the plate comes up under the new tile, so the word
        /// arrives with the cursor rather than swapping under it.
        /// </summary>
        private void Card(float x, float y, float wide, Item item, float arrive)
        {
            // The same plate every chosen thing sits on, faint, so the name and the line under
            // it read as one card rather than two lines of text loose on the panel.
            Theme.Plate(x, y, wide, CardH - 0.006f, 0.55f * arrive);

            var tx = x + 0.010f;

            if (item == null)
            {
                Hud.Text("Buy something and it turns up here.", tx, y + 0.010f, 0.27f,
                         Palette.Alpha(Palette.TextDim, (int)(190f * arrive)), Hud.FontBody);
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

        /// <summary>The keys as caps, with the way out in the same corner as every other screen.</summary>
        private void Foot(float x, float right, float y, Item item, float arrive)
        {
            Theme.Rule(x, y, right - x, arrive);

            var ky = y + 0.011f;

            Kit.KeyRight(right, ky, Kit.Back, "DONE", arrive);

            if (_ids.Count == 0) return;

            var kx = Kit.Key(x, ky, null, "arrow_leftright.png", "PICK", arrive);

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
