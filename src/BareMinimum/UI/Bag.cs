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

        /// <summary>
        /// The card under the grid: the chosen thing, said properly.
        ///
        /// MEASURED, NOT EYEBALLED. It was a flat 0.052, which is a number somebody looked at
        /// once and thought was about right -- and the line of flavour under the name hung out
        /// of the bottom of the plate it is drawn on, because a line of body text at this scale
        /// is taller than the room that was left for it. Asked of the game instead: the name's
        /// line, the description's own height, and a breath under it.
        /// </summary>
        private static float CardH
        {
            get { return DescTop + Hud.Height(DescScale, Hud.FontBody) + 0.008f; }
        }

        /// <summary>Where the line of flavour starts, from the top of the card, and how big it is.</summary>
        private const float DescTop = 0.031f;
        private const float DescScale = 0.25f;

        /// <summary>The gap between tiles, as an x fraction. Turned into y through the aspect.</summary>
        private const float Gap = 0.0018f;

        /// <summary>How much the chosen picture swells as its plate comes up.</summary>
        private const float PickGrow = 0.10f;

        /// <summary>The band of the item's own colour along the foot of the chosen tile.</summary>
        private const float FootLight = 0.0026f;

        private const int Columns = 5;

        /// <summary>Rows on screen. Twenty tiles, which is the default pocket exactly.</summary>
        private const int Rows = 4;

        private readonly Core.Settings _cfg;
        private readonly Catalogue _menu;

        /// <summary>
        /// The thing under the cursor, held up while the pocket is open. See UI.Peek.
        ///
        /// The icons on the tiles are good and they are all the same size and weight -- a hot
        /// dog, a sandwich and a burrito are three warm rectangles until you read the words.
        /// The actual object in his hand settles it before you have read anything.
        /// </summary>
        private readonly Peek _peek = new Peek();
        private readonly Pantry _pantry;
        private readonly Eating _eating;

        /// <summary>
        /// The meters, which this class never had to touch before.
        ///
        /// Eating a sandwich goes through Eating, which owns the animation and moves them when
        /// the mouthful actually lands. A drug's animation belongs to the other mod, so there
        /// is nothing here to own it -- what is left is the part this mod does own, and Dope
        /// needs the meters handed to it to do that part.
        /// </summary>
        private readonly Needs.Needs _needs;

        private List<string> _ids = new List<string>();

        /// <summary>
        /// What Posted Up has you carrying, if it is installed. Empty otherwise, which is what
        /// this pocket looked like before the bridge existed.
        /// </summary>
        private List<string> _dope = new List<string>();

        /// <summary>Tiles in the grid: the food, and then the product after it.</summary>
        private int Places { get { return _ids.Count + _dope.Count; } }

        /// <summary>Whether that tile is a bag rather than a sandwich.</summary>
        private bool IsDope(int at) { return at >= _ids.Count && at < Places; }

        /// <summary>
        /// Puts whatever the cursor is on into his hand.
        ///
        /// EVERY FRAME, not only when the selection moves. A model that was not in memory when
        /// it was asked for arrives some frames later, and Peek is built to be asked again --
        /// so this is the thing doing the asking.
        ///
        /// NOTHING IN HAND FOR THE PRODUCT. Dope has no prop of its own here and a bag of it
        /// is the other mod's object; showing a sandwich for it would be worse than showing
        /// nothing.
        /// </summary>
        private void Holding()
        {
            try
            {
                var id = IdAt(_index);

                var item = string.IsNullOrEmpty(id) || IsDope(_index) ? null : _menu.Find(id);

                _peek.Show(item == null ? "" : item.Prop);
                _peek.Tick();
            }
            catch (Exception ex)
            {
                Log.Once("bag-hold", "Could not hold that up: " + ex.Message);
            }
        }

        /// <summary>The id under that tile, from whichever list it falls in.</summary>
        private string IdAt(int at)
        {
            if (at < 0 || at >= Places) return null;
            return at < _ids.Count ? _ids[at] : _dope[at - _ids.Count];
        }

        private int _index;
        private int _page;

        private bool _down;
        private int _quietUntil;

        /// <summary>The cursor frame that glides between tiles. See UI.Glide.</summary>
        private readonly Glide _frame = new Glide();

        /// <summary>
        /// The other mod's reason for saying no, and how long it stays up.
        ///
        /// HELD RATHER THAN POSTED. Hint fades when nothing is still asking for it, which is
        /// the behaviour that suits this: the pocket does not close on a refusal, so the chip
        /// is drawn under the open panel for as long as this clock runs and then goes.
        /// </summary>
        private string _refused;
        private int _refusedUntil;

        /// <summary>When the pocket opened, when the cursor last moved, and where it moved from.</summary>
        private int _shownAt;
        private int _pickedAt;
        private int _last = -1;

        public bool IsOpen { get; private set; }

        public Bag(Core.Settings cfg, Catalogue menu, Pantry pantry, Eating eating,
                   Needs.Needs needs)
        {
            _cfg = cfg;
            _menu = menu;
            _pantry = pantry;
            _eating = eating;
            _needs = needs;

            // The same turn it takes when it is eaten, so a thing looks the same in the pocket
            // as it does going in. See Eating.SpinFor and [Eating] FoodSpinX.
            _peek.Spin = () => _cfg == null
                ? GTA.Math.Vector3.Zero
                : new GTA.Math.Vector3(_cfg.FoodSpinX, _cfg.FoodSpinY, _cfg.FoodSpinZ);
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

                Chording();

                if (Toggled())
                {
                    if (IsOpen) Close();
                    else Open();
                }

                if (!IsOpen) return;

                Suppress();
                Navigate();

                if (IsOpen)
                {
                    Holding();
                    Paint();
                }
            }
            catch (Exception ex)
            {
                Log.Once("bag", "The pocket failed: " + ex.Message);
                IsOpen = false;
            }
        }

        public void Open()
        {
            if (_byChord) OutOfCover();

            Refill();

            _index = 0;
            _page = 0;

            _shownAt = Game.GameTime;
            _pickedAt = _shownAt;
            _last = -1;
            _refused = null;
            _frame.Reset();

            // Opened on RB + A, the A is still down and Navigate runs later in this same tick.
            // Dead until it comes back up. Opened from the key, it is already up and this is
            // true immediately, so the keyboard path is unchanged.
            _acceptArmed = !Core.Pad.Down(GTA.Control.FrontendAccept);

            IsOpen = true;
            Sound("SELECT");
        }

        public void Close()
        {
            IsOpen = false;

            // OUT OF HIS HAND WITH THE PANEL. It is attached, not given -- a burger left on
            // somebody after they shut their pocket is a burger they carry for the session.
            _peek.Clear();

            // The same 300ms hush the other menus use. A key held through a closing menu is a
            // human holding a key, and humans hold them for about that long.
            _quietUntil = Game.GameTime + 300;

            Sound("BACK");
        }

        private void Refill()
        {
            _ids = _pantry.Ids();

            // ASKED EVERY REFILL RATHER THAN CACHED. What is in the other mod's pockets
            // changes without this one being told -- a sale, a bust, a dose taken from its own
            // screen -- and a list held from the last time the pocket was open would offer a
            // bag that has already gone.
            _dope.Clear();

            if (_cfg.DrugsInPocket)
            {
                foreach (var id in Food.Dope.Ids()) _dope.Add(id);
            }

            if (_index >= Places) _index = Math.Max(0, Places - 1);

            Warm();

            var perPage = Columns * Rows;
            _page = perPage <= 0 ? 0 : _index / perPage;
        }

        // ======================================================================
        // Input
        // ======================================================================

        private bool _padWas;

        /// <summary>Whether the toggle that just fired came off the pad chord rather than the key.</summary>
        private bool _byChord;

        /// <summary>
        /// Whether Accept is allowed to eat yet. False from the moment the pocket opens with
        /// that button already held, true again once it has come back up.
        /// </summary>
        ///
        /// <remarks>
        /// BECAUSE THE CHORD'S SECOND HALF IS THE CONFIRM BUTTON. Tick() opens the pocket and
        /// then runs Navigate() in the SAME frame, and Navigate reads Accept with
        /// IS_DISABLED_CONTROL_JUST_PRESSED -- which is still true on that frame, because the
        /// press that opened the pocket is the press it is asking about. RB + A would have
        /// opened the pocket and eaten whatever was under the cursor in one motion.
        ///
        /// The 300ms hush on _quietUntil does not cover this: it gates TOGGLING, so it stops
        /// a held button re-opening a pocket that just closed, and Navigate never consults it.
        ///
        /// A RELEASE RATHER THAN A TIMER. A hush long enough to outlast the chord is a hush
        /// the player can feel when they meant to eat immediately, and one short enough not to
        /// be felt is one a slow release beats. The button coming up is the actual event being
        /// waited for, so that is what gets waited for.
        /// </remarks>
        private bool _acceptArmed = true;

        /// <summary>
        /// The frames on which both halves of the chord are down: cover and sprint are
        /// switched off for that frame.
        /// </summary>
        ///
        /// <remarks>
        /// RB IS THE COVER BUTTON ON FOOT AND A IS SPRINT, and the game acts on each on the
        /// frame it is pressed. A control disabled during a script's tick is disabled for the
        /// rest of that frame, which is how every menu in this mod keeps a gun from firing --
        /// so when the two are pressed together, the frame they land on has cover and sprint
        /// off and he does neither. It cannot help when RB lands a frame or two before A: by
        /// the time both are down he is already dropping. That case is OutOfCover's.
        ///
        /// SPRINT RATHER THAN JUMP BECAUSE THE SECOND HALF MOVED. This chord was RB + X, and X
        /// is the jump button -- so jump is what had to be held off. A is the sprint button,
        /// and a chord that suppressed jump would now be suppressing a button nobody in it is
        /// pressing while the one they ARE pressing ran.
        ///
        /// Only the frames with BOTH down. Disabling cover whenever RB is held would take the
        /// cover button away from a man who is only holding it to take cover.
        /// </remarks>
        private void Chording()
        {
            if (!_cfg.BagPad) return;

            try
            {
                if (!Core.Pad.Down(GTA.Control.FrontendRb) || !Core.Pad.Down(GTA.Control.FrontendAccept)) return;

                Game.DisableControlThisFrame(GTA.Control.Cover);
                Game.DisableControlThisFrame(GTA.Control.Sprint);
            }
            catch { /* then the frame plays as the game would have it */ }
        }

        /// <summary>
        /// If the RB half got in first and he is already going into cover, stand him back up.
        ///
        /// Cover is a task, and clearing it a frame or two into the drop is a twitch rather
        /// than a duck. Called only when the pocket opened off the chord: a man who took cover
        /// on purpose and then opened his pocket with the key is left where he chose to be.
        /// </summary>
        private static void OutOfCover()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var dropping = GTA.Native.Function.Call<bool>(GTA.Native.Hash.IS_PED_GOING_INTO_COVER, me.Handle);
                var down = GTA.Native.Function.Call<bool>(GTA.Native.Hash.IS_PED_IN_COVER, me.Handle, false);

                if (dropping || down) GTA.Native.Function.Call(GTA.Native.Hash.CLEAR_PED_TASKS, me.Handle);
            }
            catch { /* he ducks, which is what he did yesterday */ }
        }

        private bool Toggled()
        {
            _byChord = false;

            var down = false;

            try { down = Game.IsKeyPressed(_cfg.BagKey); }
            catch { /* a key that cannot be read is a key that is not pressed */ }

            var edge = down && !_down;
            _down = down;

            // RB + A. Checked even when the key already fired, so the chord's own memory
            // stays in step and releasing it cannot fire a second time.
            //
            // BOTH HALVES ARE BUSY ON FOOT -- RB takes cover and A sprints -- which is true
            // of every pair on a pad and is why this is a chord at all. Holding RB and
            // tapping A is not a thing the game does together, which is the test that
            // matters. What it used to cost was the cover press registering underneath --
            // see Chording and OutOfCover for the two halves of not paying that.
            //
            // A IS ALSO THE BUTTON THAT EATS, which X was not, and that is the one new cost
            // of moving the chord. See _acceptArmed.
            if (_cfg.BagPad &&
                Core.Pad.Chord(GTA.Control.FrontendRb, GTA.Control.FrontendAccept, ref _padWas))
            {
                edge = true;
                _byChord = true;
            }

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

            if (Places > 0)
            {
                if (Pressed(GTA.Control.FrontendRight)) Move(1);
                if (Pressed(GTA.Control.FrontendLeft)) Move(-1);
                if (Pressed(GTA.Control.FrontendDown)) Move(Columns);
                if (Pressed(GTA.Control.FrontendUp)) Move(-Columns);
            }

            // Placed AFTER the d-pad so the cursor still moves while the chord's A is held --
            // it is only eating that waits.
            if (!_acceptArmed)
            {
                if (!Core.Pad.Down(GTA.Control.FrontendAccept)) _acceptArmed = true;
                return;
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
            if (to < 0 || to >= Places) return;

            _last = _index;
            _index = to;
            _pickedAt = Game.GameTime;

            // Moving off the thing that was refused takes the reason with it. It was about
            // that tile, and it is not about this one.
            _refused = null;

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
            // Food only. A drug's animation belongs to the other mod and is loaded by it;
            // asking Eating to warm a prop for an id it has never heard of would find nothing
            // and log a miss for something that was never going to be in his hand.
            if (_ids.Count == 0 || IsDope(_index)) return;

            var id = _ids[Clamp(_index, 0, _ids.Count - 1)];

            _eating.Preload(_menu.Find(id));
        }

        private void Eat()
        {
            if (Places == 0) return;

            if (IsDope(_index)) { Take(); return; }

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
                // BACK, not added -- see Store.Return: the cap can refuse an ordinary Add
                // and the item would be gone.
                _pantry.Return(id);
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
        /// Takes a drug, which is almost nothing to do with this class.
        /// </summary>
        ///
        /// <remarks>
        /// NOTHING IS TAKEN OFF HIM HERE and nothing is put back. Eat above does that dance
        /// because this mod owns the pantry and can be left holding a sandwich that never got
        /// eaten; the product belongs to the other mod, and Dope.Take is one call that either
        /// does the whole thing or does none of it. Reaching in to remove a gram first would
        /// be this mod inventing a half-state in somebody else's inventory.
        ///
        /// A REFUSAL IS A SENTENCE, not a beep. "You've had enough of that" is the other mod's
        /// own wording for its own rule, and showing it is the difference between a button
        /// that is broken and a button that is telling you something.
        /// </remarks>
        private void Take()
        {
            var id = IdAt(_index);
            if (id == null) { Sound("ERROR"); return; }

            if (!Food.Dope.Enough(id)) { Sound("ERROR"); return; }

            var no = Food.Dope.Take(_needs, id);

            if (no != null)
            {
                Sound("ERROR");

                _refused = no;
                _refusedUntil = Game.GameTime + 2600;
                return;
            }

            Sound("SELECT");

            // The same exit as a meal, for the same reason: the ritual it starts is the whole
            // point of having pressed the button, and none of it can be seen from behind a
            // panel.
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

            var onPage = Math.Max(0, Math.Min(perPage, Places - first));
            var shownRows = onPage == 0 ? 1 : (onPage + Columns - 1) / Columns;

            var height = Kit.HeadH + GridPad + shownRows * tileH + GridPad + CardH + Kit.FootH;

            Theme.Panel(left, top, panelW, height, arrive);

            // FOOD ONLY IN THE COUNT. Slots is this mod's rule about how many sandwiches fit
            // in a pocket; the product has a capacity of its own over in the other mod and is
            // reported beside it rather than folded into it.
            var held = _pantry.Total + " of " + _pantry.Slots;

            if (_dope.Count > 0)
            {
                held += "  \u00b7  " + Food.Dope.Carried.ToString("0.#") + "g";
            }

            var y = Kit.Head(left, top, panelW, Pad, IconCache.Get("p_bag.png"), "POCKET",
                             "what you are carrying", null, held,
                             arrive, 0f);

            y += GridPad;

            _frame.Begin();

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

                    // CENTRED ON WHAT IS IN THE ROW, not filled from the left. The grid is
                    // five wide whatever you are carrying, so three things used to sit in the
                    // corner of a panel built for twenty with the rest of it empty. The panel
                    // cannot shrink -- the head has to fit "what you are carrying" and the
                    // count -- so the tiles move to the middle of it instead.
                    var rowFirst = row * Columns;
                    var inRow = Math.Min(Columns, onPage - rowFirst);

                    var tx = x + (wide - inRow * tileW) * 0.5f + col * tileW;
                    var ty = y + row * tileH + Theme.EnterRise * 0.5f * (1f - land);

                    var picked = at == _index;
                    var lit = Theme.Lit(at, _index, _last, grown);

                    Tile(at, tx, ty, tileW, tileH, lit, show, i, picked, grown);

                    if (picked)
                    {
                        _frame.Target(tx + Gap, ty + Gap * Hud.Aspect,
                                      tileW - Gap * 2f, tileH - Gap * 2f * Hud.Aspect);
                    }
                }
            }

            y += shownRows * tileH + GridPad;

            Card(x, y, wide, _index, arrive);

            Foot(x, right, top + height - Kit.FootH, _index, arrive);

            // Last, so it rides over the tile it is pointing at.
            _frame.Draw(arrive);

            if (_refused == null) return;

            if (Game.GameTime >= _refusedUntil) { _refused = null; return; }

            Hint.Show(_refused);
        }

        /// <summary>
        /// One square: the dark ground with a hairline along its top, the amber coming up under
        /// the cursor and going down where it left, the light crossing the lit one. The same
        /// tile Hoodrich draws, in this mod's colour.
        /// </summary>
        private void Tile(int at, float x, float y, float w, float h, float lit, float show,
                          int slot, bool picked, float grown)
        {
            var id = IdAt(at);
            if (id == null) return;

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

            var dope = IsDope(at);

            var item = dope ? null : _menu.Find(id);
            if (!dope && item == null) return;

            // A DRUG'S PICTURE IS THE OTHER MOD'S FILE, handed over as a full path. Icon builds
            // its own path with Path.Combine against this mod's icon folder, and Path.Combine
            // gives a rooted path straight back -- so a bag of crack drawn out of somebody
            // else's folder needs nothing changed in Icon at all.
            var icon = dope ? IconCache.Get(Food.Dope.IconOf(id))
                            : IconCache.Get(Food.Art.For(item));

            var tint = dope ? Food.Dope.Effect(id).Tint : item.Tint;

            // A LINE OF ITS OWN COLOUR ALONG THE FOOT of the chosen tile. One rectangle, and
            // it does the job the cursor frame cannot: the frame says WHICH, this says WHAT,
            // because it is the only place a taco is orange and a bag of meth is ice blue
            // before you have read a word.
            if (picked && lit > 0.01f)
            {
                Hud.Bar(tx, ty + th - FootLight, tw, FootLight,
                        Palette.Alpha(tint, (int)(225f * show * lit)));
            }

            if (icon != null && !icon.Missing)
            {
                var swell = picked ? 1f + PickGrow * grown : 1f;

                // A LITTLE MORE OF THE TILE THAN IT USED TO TAKE. At 56% there was as much
                // dead square around the picture as there was picture.
                var tall = th * 0.62f * swell;
                var wideIcon = Hud.ToX(tall);

                // The item's own colour on the dark tile, brightening toward white as the
                // plate comes up. The art is white and CustomSprite MULTIPLIES, so one file
                // does all of it.
                var lively = Sheen.On(tint, slot * 0.11f, lit < 0.5f ? Shimmer() : 0f);

                var ink = Theme.Ink(Palette.Alpha(lively, (int)(238f * show)), lit);

                // THE CHOSEN ONE DRIFTS. A slow half-millimetre rise and fall, which is
                // under the threshold you would call movement and over the one that makes a
                // grid of pictures look printed on. Off with the rest of the motion.
                var drift = picked && Theme.Motion
                    ? (float)Math.Sin(Game.GameTime / 620.0) * 0.0011f * grown
                    : 0f;

                icon.DrawSized(tx + tw / 2f, ty + th * 0.46f + drift, wideIcon, tall, ink);
            }

            // How many, on a chip in the corner. Drawn for every stack including a single one:
            // "1" says this is the last taco, and a badge that appears only at two reads as an
            // error state the first time it shows up.
            //
            // A drug counts itself: whole pills for the things that come as pills, and grams
            // to one place for everything else. Asked rather than worked out here, because
            // which is which lives in the other mod's drugs.json.
            var n = dope ? Food.Dope.Chip(id) : _pantry.CountOf(id).ToString();

            // SIZED TO THE NUMBER. It was a fixed box, which is too wide round a 1 and too
            // tight round a 32 -- and a drug counts itself in grams, so three characters and
            // a decimal point happen.
            const float chipH = 0.013f;

            var chipW = Hud.Width(n, 0.22f, Hud.FontLabel) + Hud.ToX(0.007f);

            Hud.Bar(tx + tw - chipW, ty + th - chipH, chipW, chipH,
                    Color.FromArgb((int)(215f * show), 12, 12, 15));

            Hud.TextRight(n, tx + tw - 0.0022f, ty + th - chipH + 0.0005f, 0.22f,
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
        private void Card(float x, float y, float wide, int at, float arrive)
        {
            // The same plate every chosen thing sits on, faint, so the name and the line under
            // it read as one card rather than two lines of text loose on the panel.
            Theme.Plate(x, y, wide, CardH - 0.006f, 0.55f * arrive);

            var tx = x + 0.010f;

            var id = IdAt(at);

            if (id == null)
            {
                Hud.Text("Buy something and it turns up here.", tx, y + 0.010f, 0.27f,
                         Palette.Alpha(Palette.TextDim, (int)(190f * arrive)), Hud.FontBody);
                return;
            }

            string name, desc;

            if (IsDope(at))
            {
                // HOW MUCH, ON THE CARD RATHER THAN THE TILE. The chip in the corner has room
                // for a number and this has room for the word after it, so the tile says "3"
                // and the card says three pills -- which is the pair of readings the grid was
                // built for in the first place.
                name = Food.Dope.NameOf(id) + " -- " + Food.Dope.Label(id);
                desc = Food.Dope.Effect(id).Desc;
            }
            else
            {
                var item = _menu.Find(id);
                if (item == null) return;

                name = item.Name;
                desc = item.Desc;
            }

            var grown = Theme.Grown(_pickedAt);

            Theme.Caption(name, tx, y + 0.008f, grown, 0.32f);

            // WHAT IT DOES, on the right, in its own colour.
            //
            // THE ONE FACT THE PANEL DID NOT CARRY. A name and a line of flavour is what a
            // shelf says; a pocket is opened by somebody deciding which of these to eat, and
            // the number that decides it was on no screen in this mod outside the shop. It is
            // right-aligned rather than under the name because the card has one line of room
            // left and the name already has it.
            var says = Says(at);
            var room = wide - 0.014f;

            if (!string.IsNullOrEmpty(says))
            {
                var tone = IsDope(at) ? Food.Dope.Effect(id).Tint
                                      : (_menu.Find(id) ?? new Item()).Tint;

                var sw = Hud.Width(says, 0.26f, Hud.FontLabel);

                Hud.TextRight(says, x + wide - 0.004f, y + 0.010f, 0.26f,
                              Palette.Alpha(tone, (int)((150f + 105f * grown) * arrive)),
                              Hud.FontLabel);

                room -= sw + 0.010f;
            }

            if (!string.IsNullOrEmpty(desc) && room > 0.02f)
            {
                Hud.Text(Kit.Fit(desc, room, DescScale, Hud.FontBody), tx, y + DescTop, DescScale,
                         Palette.Alpha(Palette.TextDim, (int)((110f + 90f * grown) * arrive)),
                         Hud.FontBody);
            }
        }

        /// <summary>
        /// The one line that says what taking this will do to you. "" when it does nothing
        /// worth a word.
        /// </summary>
        ///
        /// <remarks>
        /// THE METER THAT MOVES MOST, and only that one. Every item touches more than one
        /// number -- a beer is food and drink and drunk at once -- and a card that listed all
        /// of them would be a spreadsheet in a corner nobody reads. Whichever moves hardest is
        /// the reason you would pick this tile over the one beside it.
        /// </remarks>
        private string Says(int at)
        {
            var id = IdAt(at);
            if (id == null) return "";

            try
            {
                if (IsDope(at))
                {
                    var d = Food.Dope.Effect(id);

                    // Whole meter means it fills it, however empty it was -- see Dope.
                    if (d.Wake >= 1f) return "WIDE AWAKE";

                    if (Math.Abs(d.Wake) >= Math.Abs(d.Hunger))
                        return d.Wake > 0f ? "+" + Pct(d.Wake) + "% RESTED"
                             : d.Wake < 0f ? Pct(d.Wake) + "% RESTED" : "";

                    return d.Hunger > 0f ? "+" + Pct(d.Hunger) + "% FED"
                         : d.Hunger < 0f ? Pct(d.Hunger) + "% FED" : "";
                }

                var item = _menu.Find(id);
                if (item == null) return "";

                if (item.Booze > 0f && item.Hunger <= 0.02f) return "A DRINK";

                // WHICHEVER OF THE THREE IT MOVES MOST. This ranked hunger against wake, and
                // once thirst existed a can of eCola -- six per cent of a meal and most of a
                // drink -- carried the badge "+6% FED", which is true, useless, and the least
                // interesting thing about it. The badge is one line and it should spend it on
                // the meter the player would actually buy the thing for.
                var fed = item.Hunger;
                var wet = item.Thirst;
                var up = item.Wake;

                if (fed >= Math.Abs(up) && fed >= wet && fed > 0.001f)
                    return "+" + Pct(fed) + "% FED";

                if (wet >= Math.Abs(up) && wet > 0.001f) return "+" + Pct(wet) + "% WATERED";

                if (up > 0.001f) return "+" + Pct(up) + "% RESTED";

                return "";
            }
            catch { return ""; }
        }

        private static string Pct(float v)
        {
            return ((int)Math.Round(v * 100f)).ToString();
        }

        /// <summary>The keys as caps, with the way out in the same corner as every other screen.</summary>
        private void Foot(float x, float right, float y, int at, float arrive)
        {
            Theme.Rule(x, y, right - x, arrive);

            var ky = y + 0.011f;

            Kit.KeyRight(right, ky, Kit.Back, "DONE", arrive);

            if (Places == 0) return;

            var kx = Kit.Key(x, ky, null, "arrow_leftright.png", "PICK", arrive);

            var item = IsDope(at) ? null : _menu.Find(IdAt(at));

            var verb = IsDope(at) ? "TAKE IT"
                     : item == null ? "EAT IT"
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
