using System;
using System.Globalization;
using GTA;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.Food;
using BareMinimum.Venues;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.UI
{
    /// <summary>
    /// The counter: the prompt at the till, the menu, and paying for things.
    ///
    /// BUILT FROM SCRATCH rather than replacing anything. Vanilla singleplayer 24/7, LTD and
    /// Rob's Liquor counters have no buy-food UI at all -- the only interaction is robbery --
    /// so there is nothing to displace. And the one native that could have taken the counter
    /// wholesale, TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME("shop_controller"), is off the table
    /// on purpose: that single script also runs Ammu-Nation, the clothing shops, barbers and
    /// tattooists, which are explicitly being kept.
    /// </summary>
    internal sealed class Shop
    {
        private readonly Core.Settings _cfg;
        private readonly Catalogue _menu;
        private readonly Counters _counters;
        private readonly Eating _eating;
        private readonly Needs.Needs _needs;

        private readonly Menu _ui = new Menu
        {
            // The same two marks as the settings panel, so both menus read as this mod's.
            TitleLeft = new Flipbook("p_burger.png"),
            TitleRight = new Flipbook("p_cup.png")
        };

        private Counter _at = Counter.None;

        /// <summary>
        /// The chains we know how to brand, and their marks. See Brands.
        ///
        /// The counter is found by till prop, which is what lets it work in every shop in
        /// the game without a coordinate list -- and the price of that is that it does not
        /// know whose shop it is in. This puts the sign back over the counter.
        /// </summary>
        private readonly Venues.Brands _brands = new Venues.Brands();

        /// <summary>One Icon per logo file. An Icon owns a texture handle, so it is kept.</summary>
        private readonly System.Collections.Generic.Dictionary<string, Icon> _marks =
            new System.Collections.Generic.Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);

        public Shop(Core.Settings cfg, Catalogue menu, Counters counters, Eating eating,
                    Needs.Needs needs)
        {
            _cfg = cfg;
            _menu = menu;
            _counters = counters;
            _eating = eating;
            _needs = needs;

            // Loaded here rather than on first use: reading a file the frame a menu opens is
            // a stutter at exactly the moment the player is looking at the screen.
            _brands.Load();
        }

        public bool IsOpen => _ui.IsOpen;

        // ======================================================================

        public void Update(bool suspended)
        {
            try
            {
                if (suspended) { if (_ui.IsOpen) _ui.Close(); return; }

                if (_ui.IsOpen) { Open(); return; }

                Closed();
            }
            catch (Exception ex)
            {
                Log.Once("shop", "The counter failed: " + ex.Message);
                _ui.Close();
            }
        }

        /// <summary>Walking about: look for a counter and offer it.</summary>
        private void Closed()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead) return;

            // Not from a car, and not mid-meal. Buying a second sandwich while still eating
            // the first is how a hunger meter gets filled by a queue of overlapping timers.
            if (me.IsInVehicle() || _eating.Busy) return;

            _at = _counters.Nearest(me.Position);
            if (_at == Counter.None) return;

            NameTheScripts();
            Hush();

            Hud.Help("Press ~INPUT_CONTEXT~ to buy something.");

            if (!Pressed()) return;

            Hud.ClearHelp();
            Begin();
        }

        /// <summary>
        /// Lists every running game script, once.
        ///
        /// THIS IS HOW THE VANILLA SHOP MENU GETS NAMED. Enhanced opens its own convenience
        /// store UI at an LTD counter -- I was wrong earlier when I said singleplayer had
        /// none -- and it cannot be switched off without knowing which .ysc owns it.
        /// Terminating shop_controller on a guess is not on: that one also runs Ammu-Nation,
        /// the clothing shops and the barbers, all of which are staying.
        ///
        /// So the list goes in the log the first time a counter is used, and the name comes
        /// out of it. SCRIPT_THREAD_ITERATOR walks the running threads; GET_NAME_OF_SCRIPT
        /// turns an id into something readable.
        /// </summary>
        private void NameTheScripts()
        {
            if (_listedScripts) return;
            _listedScripts = true;

            try
            {
                Function.Call(Hash.SCRIPT_THREAD_ITERATOR_RESET);

                var names = new System.Collections.Generic.List<string>();

                // Bounded. An iterator that never returns 0 would otherwise spin the frame,
                // and a spinning frame is the freeze this codebase has already had once.
                for (var i = 0; i < 400; i++)
                {
                    var id = Function.Call<int>(Hash.SCRIPT_THREAD_ITERATOR_GET_NEXT_THREAD_ID);
                    if (id == 0) break;

                    var name = Function.Call<string>(Hash.GET_NAME_OF_SCRIPT_WITH_THIS_ID, id);
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }

                names.Sort();

                Log.Info("Scripts running at this counter (" + names.Count + "): " +
                         string.Join(", ", names.ToArray()));
            }
            catch (Exception ex)
            {
                Log.Once("shop-scripts", "Could not list the running scripts: " + ex.Message);
            }
        }

        private bool _listedScripts;

        /// <summary>
        /// Terminates whatever the ini names, while a counter is in reach.
        ///
        /// BY NAME FROM THE INI, and empty by default. The one script anybody would guess --
        /// shop_controller -- also runs Ammu-Nation and the clothing shops, so guessing costs
        /// the player three shops to fix one menu. Once the log above has named the right one,
        /// putting it in the ini is a one-line change and no rebuild.
        /// </summary>
        /// <summary>When the next sweep is due. Real milliseconds.</summary>
        private int _nextHush;

        private void Hush()
        {
            if (_cfg.SuppressScripts.Length == 0) return;

            // FOUR TIMES A SECOND, NOT EVERY FRAME. The game restarts an object script the
            // moment you are near its object again, so this is a standing argument rather
            // than a one-off -- and having it out at sixty frames a second is a native call
            // per name per frame to accomplish exactly what four of them do.
            var now = Game.GameTime;
            if (now < _nextHush) return;
            _nextHush = now + 250;

            foreach (var name in _cfg.SuppressScripts)
            {
                try
                {
                    // No existence check first. Both of the natives that would do one take a
                    // HASH, and the only in-date way to make one here is StringHash, which is
                    // a lot of surface to add for a guard that buys nothing: terminating a
                    // script that is not running is a no-op.
                    Function.Call(Hash.TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME, name);
                    Log.Once("hush-" + name, "Terminated '" + name + "' at a counter, as configured.");
                }
                catch (Exception ex)
                {
                    Log.Once("hush-fail-" + name, "Could not terminate '" + name + "': " + ex.Message);
                }
            }
        }

        private void Begin()
        {
            _ui.Title = "COUNTER";
            _ui.TitleImage = Mark();
            _ui.Tabs.Clear();

            foreach (var c in _menu.Categories)
            {
                // A vending machine has no hot food in it. Offering a club sandwich out of a
                // drinks machine is the sort of detail that quietly says nobody thought about it.
                if (_at == Counter.Machine && IsHotFood(c)) continue;

                _ui.Tabs.Add(c.ToUpperInvariant());
            }

            if (_ui.Tabs.Count == 0) _ui.Tabs.Add("DRINKS");

            _ui.Tab = 0;
            _ui.Open();
            Refill();
        }

        /// <summary>
        /// The sign for the shop we are standing in, or null for the plain text header.
        ///
        /// NULL IS A FINE ANSWER. Most tills in the game belong to a chain nobody has added
        /// to brands.json yet, and those keep the word COUNTER -- which is honest, rather
        /// than putting somebody else's logo over a shop it does not belong to.
        /// </summary>
        private Icon Mark()
        {
            try
            {
                var brand = _brands.At(Game.Player.Character);
                if (brand == null) return null;

                Icon icon;
                if (_marks.TryGetValue(brand.Logo, out icon)) return icon;

                icon = new Icon(brand.Logo);
                _marks[brand.Logo] = icon;

                Log.Once("brand-" + brand.Id, "Counter branded as " + brand.Name + ".");

                return icon;
            }
            catch (Exception ex)
            {
                Log.Once("brand-mark", "Could not brand the counter: " + ex.Message);
                return null;
            }
        }

        private static bool IsHotFood(string category)
        {
            return string.Equals(category, "Food", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The menu is up: run it, and act on whatever was chosen.</summary>
        private void Open()
        {
            _ui.Update();

            if (_ui.JustClosed) return;

            if (_ui.TabChanged) Refill();

            // Walking away closes it. Without this the menu stays up while the player strolls
            // out of the shop, and they can buy from the till from across the street.
            if (!StillThere()) { _ui.Close(); return; }

            Subtitle();

            // The highlighted row, asked for ahead of the keypress. Scrolling a list is
            // exactly the moment to be streaming what is about to be bought.
            if (_ui.Rows.Count > 0)
            {
                var at = _ui.Index;
                if (at >= 0 && at < _ui.Rows.Count) _eating.Preload(_ui.Rows[at].Tag as Item);
            }

            if (_ui.Activated != null) Buy(_ui.Activated.Tag as Item);

            _ui.Draw();
        }

        private bool StillThere()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead) return false;

                return _counters.Nearest(me.Position) == _at;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The header line: what you have, and how you are doing.
        ///
        /// The money is there because a price list without a balance makes the player check
        /// the pause menu; the needs are there because "how hungry am I actually" is the
        /// question the whole menu exists to answer, and the HUD icon gives a band, not a number.
        /// </summary>
        private void Subtitle()
        {
            _ui.Subtitle = Stock.Header(Money(), _needs.Hunger.Value,
                                        _needs.Sleep.Value, _needs.Drunk);
        }

        /// <summary>Rebuilds the row list for the current tab.</summary>
        private void Refill()
        {
            _ui.Rows.Clear();

            if (_ui.Tab < 0 || _ui.Tab >= _ui.Tabs.Count) return;

            var category = _ui.Tabs[_ui.Tab];
            var money = Money();

            foreach (var item in _menu.Items)
            {
                if (!string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_at == Counter.Machine && IsHotFood(item.Category)) continue;

                // Vendor-only items never reach a shelf.
                if (!item.InShop) continue;

                _ui.Rows.Add(Stock.RowFor(item, money, item.Price, null));
            }
        }

        // ======================================================================
        // Paying
        // ======================================================================

        private void Buy(Item item)
        {
            if (item == null) return;

            var money = Money();

            if (money < item.Price)
            {
                // Reachable despite the Enabled check: the rows are built once per tab and the
                // player's money can fall while the menu is open -- a parking ticket, another
                // mod, a wanted level. Re-checked at the moment of payment, which is the only
                // moment that counts.
                Notify("~r~Not enough money.");
                Refill();
                return;
            }

            if (_eating.Busy)
            {
                Notify("~y~You are still eating.");
                return;
            }

            if (!Charge(item.Price)) return;

            _ui.Close();

            if (!_eating.Begin(item))
            {
                // Refunded rather than swallowed. Taking the money and not producing the food
                // is the one failure a shop must never have.
                Refund(item.Price);
                Notify("~r~Could not eat that - refunded.");
            }
        }

        private static int Money()
        {
            try { return Game.Player.Money; }
            catch { return 0; }
        }

        private static bool Charge(int amount)
        {
            try
            {
                Game.Player.Money = Math.Max(0, Game.Player.Money - amount);
                return true;
            }
            catch (Exception ex)
            {
                Log.Once("shop-charge", "Could not take payment: " + ex.Message);
                return false;
            }
        }

        private static void Refund(int amount)
        {
            try { Game.Player.Money += amount; }
            catch (Exception ex) { Log.Error("Could not refund " + amount, ex); }
        }

        private static void Notify(string message)
        {
            try { GTA.UI.Notification.PostTicker(message, false, false); }
            catch { /* nothing to do about it */ }
        }

        private bool _keyWasDown;

        /// <summary>
        /// The interact, by the game's context control OR the configured key.
        ///
        /// The key half is edge-detected by hand because Game.IsKeyPressed is a LEVEL: held
        /// for a fifth of a second it is true across a dozen frames, which would open the menu
        /// and instantly re-open it after a close.
        /// </summary>
        private bool Pressed()
        {
            try
            {
                if (Game.IsControlJustPressed(GTA.Control.Context)) { _keyWasDown = true; return true; }
            }
            catch
            {
                // Fall through to the key.
            }

            bool down;
            try { down = Game.IsKeyPressed(_cfg.InteractKey); }
            catch { return false; }

            var edge = down && !_keyWasDown;
            _keyWasDown = down;
            return edge;
        }

        public void Shutdown()
        {
            _ui.Close();
        }
    }
}
