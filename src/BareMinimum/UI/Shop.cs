using System;
using System.Globalization;
using GTA;
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
            TitleLeft = new Icon("apple2.png"),
            TitleRight = new Icon("eye2.png")
        };

        private Counter _at = Counter.None;

        public Shop(Core.Settings cfg, Catalogue menu, Counters counters, Eating eating,
                    Needs.Needs needs)
        {
            _cfg = cfg;
            _menu = menu;
            _counters = counters;
            _eating = eating;
            _needs = needs;
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

            Hud.Help(_at == Counter.Till
                ? "Press ~INPUT_CONTEXT~ to buy something."
                : "Press ~INPUT_CONTEXT~ to use the machine.");

            if (!Pressed()) return;

            Hud.ClearHelp();
            Begin();
        }

        private void Begin()
        {
            _ui.Title = _at == Counter.Till ? "COUNTER" : "VENDING MACHINE";
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
            var money = Money();

            _ui.Subtitle = "$" + money.ToString("N0", CultureInfo.InvariantCulture) +
                           "     Fed " + Pct(_needs.Hunger.Value) +
                           "     Rested " + Pct(_needs.Sleep.Value);
        }

        private static string Pct(float v)
        {
            return ((int)Math.Round(v * 100f)).ToString(CultureInfo.InvariantCulture) + "%";
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

                var afford = money >= item.Price;

                _ui.Rows.Add(new Row
                {
                    Left = item.Name,
                    Right = "$" + item.Price.ToString(CultureInfo.InvariantCulture),
                    Note = Describe(item, afford),
                    Enabled = afford,
                    Tag = item
                });
            }
        }

        /// <summary>
        /// What a row says about itself.
        ///
        /// In PLAIN WORDS rather than numbers. "+0.42 hunger" is the internal figure and it
        /// means nothing to somebody standing at a till; "a proper meal" is the same
        /// information in the units the player actually thinks in.
        /// </summary>
        private static string Describe(Item item, bool afford)
        {
            if (!afford) return "~r~You cannot afford this.";

            string size;

            if (item.Hunger >= 0.40f) size = "A proper meal.";
            else if (item.Hunger >= 0.25f) size = "A decent feed.";
            else if (item.Hunger >= 0.12f) size = "Takes the edge off.";
            else size = "Barely a mouthful.";

            if (item.Wake >= 0.10f) return size + " Will wake you up a bit.";
            if (item.Wake > 0f) return size + " A slight lift.";

            return size;
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
