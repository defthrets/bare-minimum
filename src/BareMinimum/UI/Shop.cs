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
        private readonly Pantry _pantry;
        private readonly Needs.Needs _needs;

        private readonly Menu _ui = new Menu
        {
            // The same two marks as the settings panel, so both menus read as this mod's.
            TitleLeft = new Flipbook("p_burger.png"),
            TitleRight = new Flipbook("p_cup.png"),
            ConfirmWord = "BUY"
        };

        private Counter _at = Counter.None;

        /// <summary>
        /// Bought out of a machine and waiting for the machine's own animation to finish
        /// before he uses it. Null when nothing is waiting. See Vend and Waiting.
        /// </summary>
        private Item _vending;
        private int _vendUntil;

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
                    Needs.Needs needs, Pantry pantry)
        {
            _cfg = cfg;
            _menu = menu;
            _pantry = pantry;
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
                // FIRST, AND WHATEVER ELSE IS HAPPENING. The game's own counter menu has to
                // be shut up before the player is close enough to be offered it, which means
                // before we have decided whether we are offering anything ourselves -- and it
                // has to keep being shut up while our menu is open, or dismissing ours hands
                // the counter straight back to theirs.
                Quieten();
                Waiting();

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
        /// Keeps the game's own counter menu off the screen.
        ///
        /// TWO THINGS AT ONCE, because neither is enough on its own.
        ///
        /// It TERMINATES what the ini names -- ob_cashregister by default, which is the
        /// vanilla store list with Shoplift and Select on it, named off a live counter rather
        /// than guessed. Not shop_controller: that one also runs Ammu-Nation, the clothing
        /// shops and the barbers, so guessing it costs the player three shops to fix one menu.
        ///
        /// And it HOLDS THE CONTEXT CONTROL DOWN, because terminating an object script is not
        /// permanent -- the game starts it again the moment its register is near, and between
        /// one sweep and the next there is a window where it is alive and listening. Our own
        /// reads go through the disabled variant, so the key and the pad still reach us.
        ///
        /// AT CounterHushReach, NOT AT TillReach. This is the fix: it used to run inside our
        /// own 1.9m prompt radius, and the game's trigger is wider than that, so the vanilla
        /// list had already been offered and taken before we ever looked.
        /// </summary>
        private void Quieten()
        {
            var now = Game.GameTime;

            // FOUR TIMES A SECOND FOR THE SCAN, EVERY FRAME FOR THE CONTROL. The prop sweep is
            // a native call per till model and does not need to be exact; the control block is
            // a per-frame promise by definition, so it reads the answer the sweep left behind.
            if (now >= _nextHush)
            {
                _nextHush = now + 250;
                _tillNear = false;

                try
                {
                    var me = Game.Player.Character;

                    if (me != null && me.Exists() && !me.IsDead)
                        _tillNear = _counters.AnyNear(me.Position, _cfg.CounterHushReach);
                }
                catch
                {
                    _tillNear = false;
                }

                if (_tillNear) Terminate();
            }

            if (!_tillNear || !_cfg.BlockVanillaCounter) return;

            try
            {
                Game.DisableControlThisFrame(GTA.Control.Context);
                Game.DisableControlThisFrame(GTA.Control.ContextSecondary);
            }
            catch
            {
                // The termination above is still doing most of the work.
            }
        }

        /// <summary>When the next sweep is due, and what the last one found.</summary>
        private int _nextHush;
        private bool _tillNear;

        /// <summary>Ends the scripts the ini names. Called only with a till actually near.</summary>
        private void Terminate()
        {
            if (_cfg.SuppressScripts.Length == 0) return;

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
            _ui.Title = _at == Counter.Machine ? (_counters.DrinksOnly ? "DRINKS MACHINE" : "VENDING MACHINE")
                      : _at == Counter.Stall ? "FRUIT STALL"
                      : "COUNTER";

            // A machine and a stall are not a chain, so they never wear a shop's logo -- the
            // brand lookup is about which BUILDING you are stood in, and the machine outside
            // the 24/7 is not the 24/7.
            _ui.TitleImage = _at == Counter.Till ? Mark() : null;
            _ui.Tabs.Clear();

            foreach (var c in _menu.Categories)
            {
                // A vending machine has no hot food in it. Offering a club sandwich out of a
                // drinks machine is the sort of detail that quietly says nobody thought about it.
                if (_at == Counter.Machine && IsHotFood(c)) continue;

                // AND A DRINKS MACHINE HAS ONLY DRINKS IN IT. Same argument one step further:
                // the front of a soda machine is a column of cans, and a bag of crisps coming
                // out of it is the detail that says nobody thought about it. See
                // Counters.DrinkMachineModels.
                if (_at == Counter.Machine && _counters.DrinksOnly && !IsDrink(c)) continue;

                // A STALL HAS ONE TAB, because the stock is a named list rather than a
                // category -- an apple is a Snack and a fresh fruit is Food, so filtering by
                // category would either miss half the crate or sell jerky off it.
                if (_at == Counter.Stall) continue;

                _ui.Tabs.Add(c.ToUpperInvariant());
            }

            if (_at == Counter.Stall) _ui.Tabs.Add("PRODUCE");
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

        /// <summary>Whether this category is what a drinks machine has in it.</summary>
        private static bool IsDrink(string category)
        {
            return string.Equals(category, "Drinks", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHotFood(string category)
        {
            return string.Equals(category, "Food", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether a fruit stall has this on the trestle. Counters/StallItems.</summary>
        private bool Stocked(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            foreach (var want in (_cfg.StallItems ?? "").Split(',', ';'))
            {
                if (string.Equals(want.Trim(), id, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// The game's own vending-machine animation, played on OUR purchase.
        ///
        /// mini@sprunk / plyr_buy_drink_pt1 is what the game plays when it sells you a drink
        /// from a soda machine. Borrowing it for the candy machine is the point: the player
        /// has seen this exact animation at the machine next to this one, so a purchase that
        /// used anything else would look like a different game.
        ///
        /// UPPER BODY ONLY and not a task, so it plays over standing and cannot trap him: a
        /// machine that grabs control for two seconds every time you buy a bag of crisps is
        /// worse than no animation.
        /// </summary>
        /// <summary>
        /// The game's own reach-and-press at the machine.
        ///
        /// <paramref name="using_"/> is what he is about to use, or null when it is going in
        /// his pocket and there is nothing to wait for. When it is not null the item is held
        /// here until the animation has had its nine hundred milliseconds, because Eating.Begin
        /// used to be called on the line after this one and replaced the reach instantly -- the
        /// press never played and the can appeared in his hand out of nowhere.
        /// </summary>
        private void Vend(Item using_ = null)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                const string dict = "mini@sprunk";

                Function.Call(Hash.REQUEST_ANIM_DICT, dict);

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
                {
                    // The dictionary is on its way. Without the animation there is nothing to
                    // wait for, so whatever he was going to use he uses now.
                    if (using_ != null) _eating.Begin(using_, true);
                    return;
                }

                // pt1 of three. pt2 and pt3 are the rest of the game's own sequence -- it
                // bends down for the can and drinks it -- and they are deliberately not
                // played: the drinking here is Eating's, with the item's real prop in his
                // hand, and miming a second invisible can before it would look like two.
                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, dict, "plyr_buy_drink_pt1",
                              8f, -8f, 900, 48, 0f, false, false, false);

                if (using_ == null) return;

                _vending = using_;
                _vendUntil = Game.GameTime + 900;
            }
            catch (Exception ex)
            {
                Core.Log.Once("vend-anim", "Could not play the machine animation: " + ex.Message);

                // The reach failed; the drink must not fail with it.
                if (using_ != null) _eating.Begin(using_, true);
            }
        }

        /// <summary>
        /// Uses whatever the machine just sold him, once its animation has finished.
        ///
        /// Called every tick from Update and not from Open, because the menu has CLOSED by
        /// then -- the purchase shuts it -- and a wait that only ran while the menu was up
        /// would never come round.
        /// </summary>
        private void Waiting()
        {
            if (_vending == null) return;
            if (Game.GameTime < _vendUntil) return;

            var item = _vending;
            _vending = null;

            try
            {
                if (!_eating.Busy) _eating.Begin(item, true);
            }
            catch (Exception ex)
            {
                Core.Log.Once("vend-use", "Could not use what the machine sold: " + ex.Message);
            }
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

            // SET EVERY FRAME, not once at construction. "Animate the icons" is a row
            // inside the settings menu, so a value read once would leave the menu you
            // just changed it in ignoring you until the next reload.
            _ui.Shimmer = _cfg.HudAnimate ? _cfg.HudShimmer : 0f;

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
                                        _needs.Sleep.Value, _needs.Drunk,
                                        _needs.Thirst.Value);
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
                // A STALL IGNORES THE CATEGORY AND READS ITS OWN LIST. Everything else on this
                // screen is "show me the Drinks tab"; a trestle with three crates on it is
                // "show me these three things", and the ini says which.
                if (_at == Counter.Stall)
                {
                    if (!Stocked(item.Id)) continue;

                    _ui.Rows.Add(Stock.RowFor(item, money, item.Price, null));
                    continue;
                }

                if (!string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_at == Counter.Machine && IsHotFood(item.Category)) continue;
                if (_at == Counter.Machine && _counters.DrinksOnly && !IsDrink(item.Category)) continue;

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

            // ---- into the pocket ----
            //
            // THE SHOP STAYS OPEN. That is most of the point: buying was eating, so a shop
            // with fifteen things on the shelf sold you exactly one of them per visit -- the
            // second was refused while you were still chewing the first. Shopping is a thing
            // you do once, for several items.
            // A MACHINE IS NOT A SHOP. See Settings.VendingUseNow: you walk to one because
            // you want a drink now, and the game itself has always had him drink it standing
            // there. Tills, shelves and stalls keep the pocket.
            var pocket = _cfg.BuyToPantry &&
                         !(_at == Counter.Machine && _cfg.VendingUseNow);

            if (pocket)
            {
                // FULL POCKETS ARE NOT A REFUSAL, THEY ARE A MEAL. Being told at the counter
                // that you cannot buy a burger because you are already carrying five is the
                // shop answering a question nobody asked: the pocket is for taking food away,
                // and a man with nowhere to put it is a man who eats it standing there. So the
                // last one goes in his hand instead of being turned down.
                if (_pantry.Full) { Spot(item); return; }

                if (!Charge(item.Price)) return;

                if (!_pantry.Add(item.Id))
                {
                    // Paid for, and the pocket took it as far as the shelf and no further.
                    // Eaten rather than refunded, for the reason above.
                    if (!_eating.Busy && _eating.Begin(item, true))
                    {
                        _ui.Close();
                        if (_at == Counter.Machine) Vend();
                        Notify("~g~" + item.Name + "~s~ - no room in your pockets, so you ate it.");
                        return;
                    }

                    Refund(item.Price);
                    Notify("~r~No room for that - refunded.");
                    return;
                }

                // THE MACHINE MOVES WHEN IT SELLS. A candy machine that takes your money and
                // stands there is a menu with a prop behind it; the animation is what makes it
                // the machine you just used.
                if (_at == Counter.Machine) Vend();

                Notify("~g~" + item.Name + "~s~ - in your pocket. " +
                       _pantry.Total + " of " + _pantry.Slots + ".");

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

            // At a machine the reach plays first and Vend starts the eating when it is done.
            if (_at == Counter.Machine) { Vend(item); return; }

            if (!_eating.Begin(item, true))
            {
                // Refunded rather than swallowed. Taking the money and not producing the food
                // is the one failure a shop must never have.
                Refund(item.Price);
                Notify("~r~Could not eat that - refunded.");
            }
        }

        /// <summary>
        /// Bought with nowhere to put it: he eats it where he is standing.
        ///
        /// The whole path a normal purchase takes -- the money, the machine's animation, the
        /// menu closing because you cannot shop with your mouth full -- and the same refund if
        /// the animation will not start. The one thing it does not do is put anything anywhere.
        /// </summary>
        private void Spot(Item item)
        {
            if (_eating.Busy)
            {
                Notify("~y~Your pockets are full, and so are your hands.");
                return;
            }

            if (!Charge(item.Price)) return;

            _ui.Close();

            if (_at == Counter.Machine) { Vend(item); return; }

            if (_eating.Begin(item, true))
            {
                Notify("~g~" + item.Name + "~s~ - no room in your pockets, so you ate it.");
                return;
            }

            Refund(item.Price);
            Notify("~r~Could not eat that - refunded.");
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
            try { Core.Compat.Ticker(message); }
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
            // NOT WHILE A MENU HAS JUST CLOSED. See Menu.Quiet: the pad read below goes
            // through the disabled variant, so the press that dismissed a menu is still
            // plainly visible here and would be answered by re-opening it.
            if (Menu.Quiet) return false;

            try
            {
                // THE DISABLED VARIANT, because we are the ones disabling it -- Quieten holds
                // Context down near a till so the game's own menu never hears the press, and
                // a plain read here would mean we could not hear it either.
                var pad = Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED,
                                              0, (int)GTA.Control.Context);

                if (pad) { _keyWasDown = true; return true; }
            }
            catch
            {
                // Fall through to the key.
            }

            bool down;
            // THE PROMPT ADVERTISES ~INPUT_CONTEXT~, so the context button has to work.
            // It drew the pad's own glyph and then read the keyboard only, which meant a
            // controller player was shown a button and pressed it and nothing happened.
            // Sleeping and the fridge already did both; these two were the ones left out.
            if (Core.Pad.Context()) return true;

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
