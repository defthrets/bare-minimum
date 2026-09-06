using System;
using System.Collections.Generic;
using System.Globalization;
using GTA;
using BareMinimum.Core;

namespace BareMinimum.UI
{
    /// <summary>
    /// The settings menu, on F7.
    ///
    /// CHANGES APPLY LIVE AND SAVE THEMSELVES. You nudge the HUD size, watch it change, and
    /// it stays that way -- no alt-tabbing to an ini, and nothing to remember to press.
    ///
    /// The write is careful about the file rather than casual with it, because
    /// BareMinimum.ini is the one the player hand-edits and it ships with a page of comments
    /// explaining every setting:
    ///
    ///  - ONLY THE KEYS THAT ACTUALLY CHANGED are written, tracked per option. The file is
    ///    never serialised over wholesale, so the comments, the ordering and any hand-made
    ///    edits survive untouched.
    ///  - The write waits for the player to STOP. Holding a direction fires a nudge every few
    ///    frames, and IniFile.SetValue re-reads and rewrites the whole file per key.
    ///  - A failed write disables further attempts rather than retrying forever, because the
    ///    likeliest cause is the game folder being read-only and that will not fix itself.
    /// </summary>
    internal sealed class SettingsPanel
    {
        /// <summary>One adjustable thing.</summary>
        private sealed class Option
        {
            public string Name = "";
            public string Note = "";

            /// <summary>A heading, not a setting. Holds nothing and does nothing.</summary>
            public bool Heading;

            /// <summary>Which tab it lives under.</summary>
            public int Tab;

            /// <summary>Where it lives in the ini. Empty for an action row.</summary>
            public string Section = "";
            public string Key = "";

            public Func<string> Show;
            public Action<int, bool> Nudge;

            /// <summary>What to write to the ini. Null for an action row.</summary>
            public Func<string> Persist;

            /// <summary>For a row that DOES something rather than holds a value.</summary>
            public Action Activate;

            /// <summary>
            /// Whether this row can currently be changed at all. Null means always.
            ///
            /// This exists because HUD X and HUD Y are ignored while auto position is on, and
            /// a row that accepts a keypress, plays a sound and moves a number while having no
            /// effect whatsoever is worse than no row at all -- it reads as the mod being
            /// broken. Greyed out, the menu says so instead.
            /// </summary>
            public Func<bool> Available;

            /// <summary>Shown instead of Note while Available is false.</summary>
            public string Unavailable = "";

            /// <summary>
            /// Changed since the last write.
            ///
            /// PER OPTION, not one flag for the whole panel, because the auto-save writes only
            /// what actually moved. IniFile.SetValue re-reads and rewrites the entire file per
            /// key, so saving all twenty-eight every time somebody nudges one would be
            /// twenty-eight passes over the file to record a single number.
            /// </summary>
            public bool Dirty;
        }

        private readonly Core.Settings _cfg;
        private readonly Needs.Needs _needs;

        private readonly Menu _ui = new Menu();
        private readonly List<Option> _options = new List<Option>();

        private bool _dirty;
        private bool _keyWasDown;

        public SettingsPanel(Core.Settings cfg, Needs.Needs needs)
        {
            _cfg = cfg;
            _needs = needs;

            _ui.Title = "BARE MINIMUM";
            _ui.LeftRightAdjusts = true;
            _ui.ConfirmWord = "SET";
            // THE BLINK WENT WITH THE EYE. It was the one place in the mod with room for a
            // moving mark -- a menu is looked AT, so something that stirs every few seconds
            // reads as character rather than as movement in the corner of your vision. A moon
            // does not blink, and a moon that did would be a joke rather than an icon.
            //
            // Flipbook still takes them one file at a time, so a static mark costs nothing and
            // never looks at the clock.
            _ui.TitleLeft = new Flipbook("food0.png");
            // moon0, NOT the middle of the set. The stages run sun through to crescent now, so
            // moon2 is a bare disc -- accurate for the halfway point and useless as a mark.
            // The crescent and its two stars are the thing anybody would recognise.
            _ui.TitleRight = new Flipbook("moon0.png");

            Build();

            // The tab strip, in the order Build named them.
            _ui.Tabs.AddRange(_tabs);
        }

        public bool IsOpen => _ui.IsOpen;

        // ======================================================================

        public void Update(bool suspended)
        {
            try
            {
                if (suspended)
                {
                    if (_ui.IsOpen) { _ui.Close(); Flush(); }
                    return;
                }

                if (Toggled())
                {
                    if (_ui.IsOpen) { _ui.Close(); Flush(); }
                    else { _ui.Open(); Refill(); }
                }

                if (!_ui.IsOpen)
                {
                    // A save can still be owed after the menu has gone -- closing flushes,
                    // but a settle timer left running by anything else must not be stranded.
                    if (Due()) Flush();
                    return;
                }

                _ui.Update();

                if (_ui.JustClosed) { Flush(); return; }

                // A new page of rows. The pending write is NOT flushed here -- paging is not
                // finishing, and a settle timer that survives the page is the whole point of
                // waiting for the player to stop.
                if (_ui.TabChanged) Refill();

                if (_ui.Adjusted != null)
                {
                    var option = _ui.Adjusted.Tag as Option;
                    if (option != null && option.Nudge != null)
                    {
                        option.Nudge(_ui.AdjustBy, _ui.Fine);
                        Touch(option);
                        Refill();
                    }
                }

                if (_ui.Activated != null)
                {
                    var option = _ui.Activated.Tag as Option;

                    if (option != null && option.Activate != null)
                    {
                        option.Activate();
                        Refill();
                    }
                    else if (option != null && option.Nudge != null)
                    {
                        // Enter on a value row nudges it forward, so a toggle can be flipped
                        // without anybody having to discover that left and right do anything.
                        option.Nudge(1, false);
                        Touch(option);
                        Refill();
                    }
                }

                if (Due()) Flush();

                Subtitle();
                // SET EVERY FRAME, not once at construction. "Animate the icons" is a row
                // inside the settings menu, so a value read once would leave the menu you
                // just changed it in ignoring you until the next reload.
                _ui.Shimmer = _cfg.HudAnimate ? _cfg.HudShimmer : 0f;

                _ui.Draw();
            }
            catch (Exception ex)
            {
                Log.Once("settings-panel", "The settings menu failed: " + ex.Message);
                _ui.Close();
            }
        }

        // ======================================================================
        // Saving itself
        // ======================================================================

        /// <summary>
        /// How long after the last keypress the settings are written, in milliseconds.
        ///
        /// A SETTLE TIME, not a delay for its own sake. Holding right on the HUD size fires a
        /// nudge every few frames, and writing the ini on each one is a read-and-rewrite of
        /// the whole file dozens of times a second. Waiting for the player to stop turns a
        /// drag from end to end into a single write.
        /// </summary>
        private const int SettleMs = 900;

        /// <summary>When the pending write comes due. Zero when nothing is owed.</summary>
        private int _saveAt;

        /// <summary>
        /// Set once a write has failed, so a read-only game folder is not retried forever.
        ///
        /// Without it, an unwritable ini means a failed save every time the player nudges
        /// anything, for the rest of the session -- and a log line each time saying so.
        /// </summary>
        private bool _saveBroken;

        /// <summary>Marks one option as needing writing, and restarts the settle timer.</summary>
        private void Touch(Option option)
        {
            if (option == null) return;

            option.Dirty = true;
            _dirty = true;

            if (_saveBroken) return;

            var now = Game.GameTime;
            _saveAt = now + SettleMs;
        }

        private bool Due()
        {
            return _saveAt != 0 && Game.GameTime >= _saveAt;
        }

        private void Subtitle()
        {
            if (_saveBroken)
            {
                _ui.Subtitle = "~r~Cannot save~s~ - see the log.  Q/E tabs";
                return;
            }

            _ui.Subtitle = _dirty
                ? "~y~Saving...~s~   left/right change, ctrl fine, Q/E tabs"
                : "Saved.  left/right change, ctrl fine, Q/E tabs";
        }

        /// <summary>
        /// Edge-detects the menu key.
        ///
        /// Game.IsKeyPressed is a LEVEL, not an edge: held for a fifth of a second it is true
        /// across a dozen frames, which would open and close the menu repeatedly for as long
        /// as the key is down.
        /// </summary>
        private bool Toggled()
        {
            bool down;

            try { down = Game.IsKeyPressed(_cfg.MenuKey); }
            catch { return false; }

            var edge = down && !_keyWasDown;
            _keyWasDown = down;
            return edge;
        }

        private void Refill()
        {
            _ui.Rows.Clear();

            foreach (var option in _options)
            {
                // ONE TAB'S WORTH. Every option still lives in the one list -- the ini write
                // walks all of them and does not care which page a row was on -- so the tab
                // only decides what is drawn.
                if (option.Tab != _ui.Tab) continue;

                var usable = option.Available == null || option.Available();

                string art;
                if (!Art.TryGetValue(option.Name, out art)) art = "";

                _ui.Rows.Add(new Row
                {
                    Left = option.Name,
                    Right = option.Heading || option.Show == null ? "" : option.Show(),
                    Note = usable || string.IsNullOrEmpty(option.Unavailable)
                        ? option.Note
                        : option.Unavailable,
                    Enabled = usable,
                    Header = option.Heading,
                    IconFile = art,
                    Tag = option
                });
            }

            // The rows have just been replaced, so the selection may be sitting on a heading
            // or off the end of a shorter list.
            _ui.Settle();
        }

        /// <summary>
        /// A line naming the group beneath it.
        ///
        /// HEADINGS STILL EXIST, but only INSIDE a tab that has two groups in it -- which
        /// is now just the HUD tab, where the placement settings sit under the look ones.
        ///
        /// This used to say a flat list was forced rather than chosen, because the menu sets
        /// LeftRightAdjusts and so left and right were already spoken for turning values up
        /// and down. That was true about those two keys and wrong about the conclusion: the
        /// shoulder buttons were sitting unused the whole time, and that is where the tabs
        /// went in the end.
        /// </summary>
        private void Heading(string name)
        {
            Add(new Option { Name = name, Heading = true });
        }

        /// <summary>
        /// Which picture goes on which row, keyed by the row's own name.
        ///
        /// ONE TABLE RATHER THAN A PARAMETER ON EVERY HELPER. Float, Bool and Choice already
        /// take between six and ten arguments each, and an eleventh that is nearly always the
        /// same handful of files would bury the numbers that matter in the call. Keyed by name
        /// because the name is what a reader is looking at when they wonder which icon a row
        /// has; a row with no entry simply gets none, so nothing breaks if one is missed.
        ///
        /// DELIBERATELY REUSED. Twelve glyphs cover thirty-nine rows: every hours setting is a
        /// clock, every placement setting is the frame. Rows that share a picture share a kind
        /// of job, and that is a faster read down a list than thirty-nine unique drawings none
        /// of which mean anything yet.
        /// </summary>
        private static readonly Dictionary<string, string> Art =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Mod enabled",              "s_power.png"  },
            { "Empty both needs",         "s_power.png"  },

            { "Hunger",                   "food0.png"    },
            { "Hunger: hours to empty",   "s_clock.png"  },
            { "Sleep",                    "moon0.png"    },
            { "Sleep: hours to empty",    "s_clock.png"  },
            { "Sleep: hours to full",     "s_clock.png"  },

            { "Slow down below",          "s_run.png"    },
            { "Stomach walk below",       "food0.png"    },
            { "Tired below",              "moon0.png"    },
            { "Drunk walk below",         "p_bottle.png" },
            { "Camera sway",              "s_wave.png"   },
            { "Well-fed speed bonus",     "s_run.png"    },
            { "Bonus starts above",       "s_run.png"    },
            { "Starving costs health",    "s_heart.png"  },
            { "Pass out when exhausted",  "s_bed.png"    },
            { "Hours out",                "s_clock.png"  },
            { "Warning before it",        "s_clock.png"  },
            { "Wavy vision when empty",   "s_eye.png"    },

            { "Sleep in beds",            "s_bed.png"    },
            { "Sleep in cars",            "s_car.png"    },
            { "Count other mods' sleep",  "moon0.png"    },
            { "Police wake-up",           "s_car.png"    },
            { "Police wake-up chance",    "s_car.png"    },
            { "How busy the street must be", "s_pin.png" },
            { "Bed hours",                "s_bed.png"    },
            { "Car hours",                "s_car.png"    },

            { "Buy to pocket",            "s_cart.png"   },
            { "Hide the game's shop",     "s_cart.png"   },
            { "Fridge storage",           "s_layout.png" },
            { "Fridge size",              "s_layout.png" },
            { "Pocket size",              "s_cart.png"   },
            { "Prices",                   "s_cart.png"   },
            { "Shop map markers",         "s_pin.png"    },
            { "Marker range",             "s_pin.png"    },
            { "Markers on pause map",     "s_pin.png"    },
            { "One marker group",         "s_pin.png"    },

            { "Show HUD",                 "s_eye.png"    },
            { "HUD style",                "s_layout.png" },
            { "Animate the icons",        "s_wave.png"   },
            { "HUD opacity",              "s_eye.png"    },
            { "Hide when fine",           "s_eye.png"    },
            { "Flash when critical",      "s_wave.png"   },

            { "HUD auto position",        "s_layout.png" },
            { "HUD size",                 "s_layout.png" },
            { "HUD gap",                  "s_layout.png" },
            { "HUD X",                    "s_layout.png" },
            { "HUD Y",                    "s_layout.png" },

            { "Bar height",               "s_bars.png"   },
            { "Bar width",                "s_bars.png"   },
            { "Animation speed",          "s_wave.png"   },
            { "Speeds up when running",   "s_run.png"    },
            { "Surface movement",         "s_wave.png"   },
            { "Bar drift speed",          "s_wave.png"   },
            { "Bar mark size",            "s_bars.png"   },
        };

        /// <summary>Adds a row to whichever tab is currently being built.</summary>
        private void Add(Option option)
        {
            option.Tab = _tab;
            _options.Add(option);
        }

        /// <summary>
        /// Starts a new TAB. Everything added after this lands under it.
        ///
        /// Seven groups and thirty-nine settings used to be one flat list scrolling through a
        /// seven-row window, so reaching the bars meant holding Down through everything else.
        /// Six tabs put every group within a page of its own, and only the HUD tab scrolls at
        /// all.
        /// </summary>
        private void Group(string name)
        {
            if (!_tabs.Contains(name)) _tabs.Add(name);
            _tab = _tabs.IndexOf(name);
        }

        private readonly List<string> _tabs = new List<string>();
        private int _tab;

        // ======================================================================
        // The options
        // ======================================================================

        private void Build()
        {
            Group("NEEDS");


            Bool("Mod enabled", "General", "Enabled",
                 () => _cfg.Enabled, v => _cfg.Enabled = v,
                 "Turns everything off without unloading the script.");

            Bool("Hunger", "Hunger", "Enabled",
                 () => _cfg.HungerEnabled, v => _cfg.HungerEnabled = v,
                 "Whether you get hungry at all.");

            Float("Hunger: hours to empty", "Hunger", "HoursToEmpty",
                  () => _cfg.HungerHoursToEmpty, v => _cfg.HungerHoursToEmpty = v,
                  2f, 4f, 200f, "0",
                  "GAME hours from full to empty. A game hour is about two real minutes.");

            Bool("Sleep", "Sleep", "Enabled",
                 () => _cfg.SleepEnabled, v => _cfg.SleepEnabled = v,
                 "Whether you get tired at all.");

            Float("Sleep: hours to empty", "Sleep", "HoursToEmpty",
                  () => _cfg.SleepHoursToEmpty, v => _cfg.SleepHoursToEmpty = v,
                  2f, 4f, 200f, "0",
                  "44 is just under two game days awake.");

            Float("Sleep: hours to full", "Sleep", "HoursToFull",
                  () => _cfg.SleepHoursToFull, v => _cfg.SleepHoursToFull = v,
                  1f, 1f, 200f, "0",
                  "Hours of sleep that take you from empty to fully rested. 12 makes a " +
                  "six-hour night worth half a meter.");

            Group("FEEL");


            Float("Slow down below", "Effects", "HungerSlowAt",
                  () => _cfg.HungerSlowAt, v => _cfg.HungerSlowAt = v,
                  0.02f, 0f, 1f, "P0",
                  "How empty your stomach gets before you start slowing down.");

            Float("Stomach walk below", "Effects", "HungerHurtAt",
                  () => _cfg.HungerHurtAt, v => _cfg.HungerHurtAt = v,
                  0.02f, 0f, 1f, "P0",
                  "Where the hunched, injured walk starts.");

            Float("Tired below", "Effects", "SleepTiredAt",
                  () => _cfg.SleepTiredAt, v => _cfg.SleepTiredAt = v,
                  0.02f, 0f, 1f, "P0",
                  "Where missed sleep starts to slow you down.");

            Float("Drunk walk below", "Effects", "SleepDrunkAt",
                  () => _cfg.SleepDrunkAt, v => _cfg.SleepDrunkAt = v,
                  0.02f, 0f, 1f, "P0",
                  "Where the unsteady walk and the swaying camera start.");

            Float("Camera sway", "Effects", "SleepCameraShake",
                  () => _cfg.SleepCameraShake, v => _cfg.SleepCameraShake = v,
                  0.02f, 0f, 1f, "0.00",
                  "How hard the camera moves when exhausted. 0 turns it off.");

            Float("Well-fed speed bonus", "Effects", "WellFedBonus",
                  () => _cfg.WellFedBonus, v => _cfg.WellFedBonus = v,
                  0.01f, 1f, 1.5f, "0.00",
                  "How much quicker you move on foot with BOTH needs up. 1.00 turns it off.");

            Float("Bonus starts above", "Effects", "WellFedAbove",
                  () => _cfg.WellFedAbove, v => _cfg.WellFedAbove = v,
                  0.05f, 0.1f, 1f, "P0",
                  "Both needs must be above this. It ramps in from here to full.");

            Bool("Starving costs health", "Effects", "StarvingCostsHealth",
                 () => _cfg.StarvingCostsHealth, v => _cfg.StarvingCostsHealth = v,
                 "Slowly, and it stops at a sixth of your health. It will not kill you.");

            Bool("Pass out when exhausted", "Sleep", "Collapse",
                 () => _cfg.SleepCollapse, v => _cfg.SleepCollapse = v,
                 "An empty sleep meter eventually puts you on the floor where you stand.");

            Float("Hours out", "Sleep", "CollapseHours",
                  () => _cfg.SleepCollapseHours, v => _cfg.SleepCollapseHours = v,
                  0.5f, 0.5f, 24f, "0.#",
                  "How long you are out for when you pass out.",
                  () => _cfg.SleepCollapse,
                  "~y~Turn passing out ON first.");

            Float("Warning before it", "Sleep", "CollapseAfterSeconds",
                  () => _cfg.SleepCollapseAfterSeconds, v => _cfg.SleepCollapseAfterSeconds = v,
                  5f, 0f, 600f, "0",
                  "Seconds between the meter emptying and you going down. Real seconds.",
                  () => _cfg.SleepCollapse,
                  "~y~Turn passing out ON first.");

            Bool("Wavy vision when empty", "Sleep", "Wobble",
                 () => _cfg.SleepWobble, v => _cfg.SleepWobble = v,
                 "The picture swims once the sleep meter is completely gone.");

            Group("SLEEP");


            Bool("Sleep in beds", "Sleeping", "InBeds",
                 () => _cfg.SleepInBeds, v => _cfg.SleepInBeds = v,
                 "Safehouse beds, and any other bed the game will admit to.");

            Bool("Sleep in cars", "Sleeping", "InCars",
                 () => _cfg.SleepInCars, v => _cfg.SleepInCars = v,
                 "Stopped, engine off, no wanted level.");

            Bool("Count other mods' sleep", "Sleeping", "CreditOutsideSleep",
                 () => _cfg.CreditOutsideSleep, v => _cfg.CreditOutsideSleep = v,
                 "Credits rest when anything else skips the clock a few hours -- another " +
                 "mod's bed, or a mission that skips a night.");

            Bool("Police wake-up", "Sleeping", "PoliceWake",
                 () => _cfg.PoliceWake, v => _cfg.PoliceWake = v,
                 "Sleep in a car in the road and you may wake to two officers at the windows.");

            Float("Police wake-up chance", "Sleeping", "PoliceWakeChance",
                  () => _cfg.PoliceWakeChance, v => _cfg.PoliceWakeChance = v,
                  0.05f, 0f, 1f, "0.00",
                  "How often it happens when the spot qualifies. Car parks never qualify.",
                  () => _cfg.PoliceWake, "Turn the police wake-up on first.");

            Float("How busy the street must be", "Sleeping", "PoliceWakeNeighbours",
                  () => _cfg.PoliceWakeNeighbours, v => _cfg.PoliceWakeNeighbours = (int)Math.Round(v),
                  1f, 0f, 60f, "0",
                  "People and cars within seventy metres. Higher means only busy streets.",
                  () => _cfg.PoliceWake, "Turn the police wake-up on first.");

            Float("Bed hours", "Sleeping", "BedHours",
                  () => _cfg.BedHours, v => _cfg.BedHours = v,
                  1f, 1f, 24f, "0", "How long a proper night lasts.");

            Float("Car hours", "Sleeping", "CarHours",
                  () => _cfg.CarHours, v => _cfg.CarHours = v,
                  1f, 1f, 24f, "0", "How long a doze in a car lasts.");

            Group("SHOPS");

            Bool("Hide the game's shop", "Counters", "BlockVanillaMenu",
                 () => _cfg.BlockVanillaCounter, v => _cfg.BlockVanillaCounter = v,
                 "Keeps the game's own counter list off the screen. Turn off if E stops working in a shop.");

            Bool("Buy to pocket", "Money", "BuyToPantry",
                 () => _cfg.BuyToPantry, v => _cfg.BuyToPantry = v,
                 "Buying puts it in your pocket to eat later. Off eats it on the spot.");

            Float("Pocket size", "Money", "PantrySlots",
                  () => _cfg.PantrySlots, v => _cfg.PantrySlots = (int)Math.Round(v),
                  2f, 1f, 200f, "0",
                  "How many items you can carry at once, all kinds counted together.");

            Bool("Fridge storage", "Fridge", "Enabled",
                 () => _cfg.FridgeEnabled, v => _cfg.FridgeEnabled = v,
                 "The fridges in the safehouse kitchens hold food. Walk up to one and press E.");

            Float("Fridge size", "Fridge", "Slots",
                  () => _cfg.FridgeSlots, v => _cfg.FridgeSlots = (int)Math.Round(v),
                  5f, 1f, 500f, "0",
                  "How much a fridge holds. Far more than a pocket -- that is the point of it.",
                  () => _cfg.FridgeEnabled,
                  "~y~Turn fridge storage ON first.");

            Float("Prices", "Money", "PriceMultiplier",
                  () => _cfg.PriceMultiplier, v => _cfg.PriceMultiplier = v,
                  0.1f, 0f, 10f, "0.0",
                  "Scales every price at once. Takes effect on the next script reload.");

            Bool("Shop map markers", "Map", "ShowShopBlips",
                 () => _cfg.ShowShopBlips, v => _cfg.ShowShopBlips = v,
                 "Markers for food shops. They only appear when you are near one.");

            Float("Marker range", "Map", "ShopBlipRange",
                  () => _cfg.ShopBlipRange, v => _cfg.ShopBlipRange = v,
                  20f, 0f, 2000f, "0",
                  "How close before a shop's marker appears, in metres. 0 shows them all, always.",
                  () => _cfg.ShowShopBlips,
                  "~y~Turn shop map markers ON first.");

            Bool("Markers on pause map", "Map", "ShopBlipsOnMainMap",
                 () => _cfg.ShopBlipsOnMainMap, v => _cfg.ShopBlipsOnMainMap = v,
                 "Off keeps the big map clear and leaves them on the minimap only.",
                 () => _cfg.ShowShopBlips,
                 "~y~Turn shop map markers ON first.");

            Bool("One marker group", "Map", "GroupShopBlips",
                 () => _cfg.GroupShopBlips, v => _cfg.GroupShopBlips = v,
                 "All shops share one row in the map legend. Left and right slide through them.",
                 () => _cfg.ShowShopBlips,
                 "~y~Turn shop map markers ON first.");

            Group("HUD");


            Bool("Show HUD", "HUD", "Show",
                 () => _cfg.ShowHud, v => _cfg.ShowHud = v,
                 "The hunger and sleep gauges. Bars or marks, whichever style is set.");

            Choice("HUD style", "HUD", "Style", new[] { "Icons", "Bars" },
                   () => (int)_cfg.Style, v => _cfg.Style = (HudStyle)v,
                   "An apple and an eye that change shape, or two filled bars.");

            Bool("Animate the icons", "HUD", "Animate",
                 () => _cfg.HudAnimate, v => _cfg.HudAnimate = v,
                 "The icons breathe, and sway once a meter is nearly out.");

            Float("HUD opacity", "HUD", "Opacity",
                  () => _cfg.HudOpacity, v => _cfg.HudOpacity = v,
                  0.05f, 0.05f, 1f, "0.00", "");

            Bool("Hide when fine", "HUD", "HideWhenFine",
                 () => _cfg.HudHideWhenFine, v => _cfg.HudHideWhenFine = v,
                 "Hides an icon while that need is comfortable.");

            Bool("Flash when critical", "HUD", "FlashWhenCritical",
                 () => _cfg.HudFlashWhenCritical, v => _cfg.HudFlashWhenCritical = v,
                 "Pulses an icon once its need is empty.");

            // ---- actions ------------------------------------------------------

            Add(new Option
            {
                Name = "Settings",
                Note = "Saved automatically to BareMinimum.ini, keeping your comments and " +
                       "layout. Press to write them now.",
                Show = () => _saveBroken ? "FAILED" : _dirty ? "saving" : "saved",
                Activate = () => { _saveAt = 0; _saveBroken = false; Flush(); }
            });

            Add(new Option
            {
                Name = "Fill both needs",
                Note = "Fed and rested, right now. For testing, or for mercy.",
                Show = () => "",
                Activate = () =>
                {
                    _needs.Hunger.Value = 1f;
                    _needs.Sleep.Value = 1f;
                    _needs.SaveNow();
                    Notify("~g~Fed and rested.");
                }
            });

            Add(new Option
            {
                Name = "Empty both needs",
                Note = "Starving and exhausted, right now. For seeing what the effects look like.",
                Show = () => "",
                Activate = () =>
                {
                    _needs.Hunger.Value = 0f;
                    _needs.Sleep.Value = 0f;
                    _needs.SaveNow();
                    Notify("~r~Starving and exhausted.");
                }
            });

            Heading("PLACEMENT");


            Bool("HUD auto position", "HUD", "AutoPosition",
                 () => _cfg.HudAutoPosition, v => _cfg.HudAutoPosition = v,
                 "Places them against the minimap whatever shape your screen is. " +
                 "Turn off to use X and Y.");

            Float("HUD size", "HUD", "Size",
                  () => _cfg.HudSize, v => _cfg.HudSize = v,
                  0.002f, 0.005f, 0.30f, "0.000",
                  "Icon height as a fraction of screen height. Watch it change as you press.");

            Float("HUD gap", "HUD", "Gap",
                  () => _cfg.HudGap, v => _cfg.HudGap = v,
                  0.02f, 0f, 3f, "0.00",
                  "Clear air between the two. Vertical for icons, horizontal for bars.");

            Float("HUD X", "HUD", "X",
                  () => _cfg.HudX, v => _cfg.HudX = v,
                  0.004f, -0.2f, 1.2f, "0.000",
                  "Across the screen. 0 is the far left, 1 the far right.",
                  () => !_cfg.HudAutoPosition,
                  "~y~Turn HUD auto position OFF first~s~ - this does nothing while it is on.");

            Float("HUD Y", "HUD", "Y",
                  () => _cfg.HudY, v => _cfg.HudY = v,
                  0.004f, -0.2f, 1.2f, "0.000",
                  "Down the screen. 0 is the top, 1 the bottom. The icons sit ABOVE this line.",
                  () => !_cfg.HudAutoPosition,
                  "~y~Turn HUD auto position OFF first~s~ - this does nothing while it is on.");

            Group("BARS");


            Float("Bar height", "HUD", "BarLength",
                  () => _cfg.HudBarLength, v => _cfg.HudBarLength = v,
                  0.004f, 0.010f, 0.400f, "0.0000",
                  "How tall the bars are, as a fraction of the screen.",
                  () => _cfg.Style == HudStyle.Bars, "Bars only.");

            Float("Bar width", "HUD", "BarWidth",
                  () => _cfg.HudBarWidth, v => _cfg.HudBarWidth = v,
                  0.0004f, 0.0010f, 0.0400f, "0.0000",
                  "How wide the bars are. The fuel gauge in Fumes uses 0.0046.",
                  () => _cfg.Style == HudStyle.Bars, "Bars only.");

            Float("Speeds up when running", "HUD", "BarEffort",
                  () => _cfg.HudBarEffort, v => _cfg.HudBarEffort = v,
                  0.25f, 1f, 6f, "0.00",
                  "How much faster the bars move at a sprint. 1 is no change at all.");

            Float("Animation speed", "HUD", "BarPace",
                  () => _cfg.HudBarPace, v => _cfg.HudBarPace = v,
                  2.5f, 0.15f, 120f, "0.00",
                  "How fast everything in both bars moves - surface, fill and all. 42 is normal.");

            Float("Surface movement", "HUD", "BarWave",
                  () => _cfg.HudBarWave, v => _cfg.HudBarWave = v,
                  0.05f, 0f, 1f, "0.00",
                  "How far the levels move. Hunger bows; sleep takes a drop and settles.",
                  () => _cfg.Style == HudStyle.Bars, "Bars only.");

            Float("Bar drift speed", "HUD", "BarDrift",
                  () => _cfg.HudBarDrift, v => _cfg.HudBarDrift = v,
                  0.05f, 0f, 1f, "0.00",
                  "How fast the specks inside the bars move. 0 stops them.",
                  () => _cfg.Style == HudStyle.Bars, "Bars only.");

            Float("Bar mark size", "HUD", "BarIconScale",
                  () => _cfg.HudBarIconScale, v => _cfg.HudBarIconScale = v,
                  0.05f, 0.20f, 1.00f, "0.00",
                  "The mark on each bar's black plate, against the plate.",
                  () => _cfg.Style == HudStyle.Bars, "Bars only.");
        }
        // ======================================================================
        // Option builders
        // ======================================================================

        /// <summary>
        /// One of a short list of named options, cycled with left and right.
        ///
        /// The same Option shape as everything else -- Show, Nudge, Persist -- so the menu
        /// needs no new row type to display it. Wraps at both ends, because a two-item list
        /// that stops at the edges gives you a left arrow that does nothing.
        /// </summary>
        private void Choice(string name, string section, string key, string[] names,
                            Func<int> get, Action<int> set, string note)
        {
            Add(new Option
            {
                Name = name,
                Note = note,
                Section = section,
                Key = key,
                Show = () =>
                {
                    var i = get();
                    return i >= 0 && i < names.Length ? names[i].ToUpperInvariant() : "?";
                },
                Nudge = (dir, fine) =>
                {
                    var i = get() + (dir >= 0 ? 1 : -1);

                    while (i < 0) i += names.Length;

                    set(i % names.Length);
                },
                Persist = () =>
                {
                    var i = get();
                    return i >= 0 && i < names.Length ? names[i] : names[0];
                }
            });
        }

        private void Bool(string name, string section, string key,
                          Func<bool> get, Action<bool> set, string note,
                          Func<bool> available = null, string unavailable = "")
        {
            Add(new Option
            {
                Name = name,
                Note = note,
                Section = section,
                Key = key,
                Available = available,
                Unavailable = unavailable,
                Show = () => get() ? "ON" : "OFF",
                Nudge = (dir, fine) => set(!get()),
                Persist = () => get() ? "true" : "false"
            });
        }

        /// <summary>
        /// A number with a step, a range and a display format.
        ///
        /// The value is ROUNDED after every nudge. Repeated floating-point addition drifts --
        /// twenty presses of +0.02 from 0.34 does not land on 0.74, it lands on
        /// 0.7400000000000001, which then formats as the right thing while quietly failing an
        /// equality check and writing a mess into the ini.
        ///
        /// HOLDING CTRL TAKES THE SMALLER STEP, for lining a HUD up against the minimap where
        /// the coarse step overshoots in both directions.
        ///
        /// THE FINE STEP IS THE FINEST THING THE ROW CAN SHOW, not a flat tenth. A tenth of
        /// this row's step is invisible on almost every row here -- HUD X moves by 0.004 and
        /// prints three decimals, so a tenth changes the fourth and the number on screen does
        /// not move. A press that visibly does nothing is the exact complaint that turned out
        /// to be a real bug in the Gap setting, and it is not worth manufacturing a fresh one
        /// for the sake of a tidier rule.
        ///
        /// It is also the finest step worth having. HUD X at three decimals moves the bars
        /// about two pixels at 1080p; a tenth of the coarse step would be two thirds of one
        /// pixel, which DRAW_RECT cannot resolve, so half those presses would land on the same
        /// pixel and read as a dead key.
        ///
        /// Where a row's format cannot show anything finer -- whole hours, whole metres -- the
        /// two steps come out equal and CTRL simply does nothing, which is the honest answer.
        ///
        /// ROUNDING IS TO THE FINE STEP EVEN ON A COARSE PRESS. Rounding to the coarse step
        /// would put every press back on the coarse grid and quietly throw away the fine
        /// tuning that came before it -- nudge to 0.253, press right, and you would get 0.256
        /// rather than 0.257. The fine step is a clean decimal, so it kills the drift just as
        /// well without deciding the value has to sit on a grid.
        /// </summary>
        private void Float(string name, string section, string key,
                           Func<float> get, Action<float> set,
                           float step, float min, float max, string format, string note,
                           Func<bool> available = null, string unavailable = "")
        {
            Add(new Option
            {
                Name = name,
                Note = note,
                Section = section,
                Key = key,
                Available = available,
                Unavailable = unavailable,
                Show = () => get().ToString(format, CultureInfo.InvariantCulture),
                Nudge = (dir, fine) =>
                {
                    var small = Fine(step, format);

                    var v = get() + (fine ? small : step) * dir;

                    if (v < min) v = min;
                    if (v > max) v = max;

                    if (small > 0f) v = (float)(Math.Round(v / small) * small);

                    set(v);
                },
                Persist = () => get().ToString("0.####", CultureInfo.InvariantCulture)
            });
        }

        /// <summary>
        /// The step CTRL takes: a tenth of the row's own, or the smallest amount its display
        /// format can show, whichever is BIGGER.
        ///
        /// Bigger, because the point is a step you can see happen. See Float for why.
        /// </summary>
        private static float Fine(float step, string format)
        {
            var tick = 1f;

            if (!string.IsNullOrEmpty(format))
            {
                var dot = format.IndexOf('.');

                // No decimal point is a whole-number row -- hours, metres -- and one unit is
                // as fine as it can be shown.
                var places = dot < 0 ? 0 : format.Length - dot - 1;

                for (var i = 0; i < places; i++) tick /= 10f;
            }

            var small = step / 10f;
            if (small < tick) small = tick;

            // Never coarser than the row's own step, which would make CTRL the blunt one.
            if (small > step) small = step;

            return small;
        }

        // ======================================================================

        /// <summary>
        /// Writes the changed settings to the ini, and only the changed ones.
        ///
        /// KEY BY KEY, THROUGH IniFile.SetValue, never by serialising the settings object
        /// over the file. The ini ships with a page of comments explaining what each setting
        /// does and why the defaults are what they are, and rewriting it wholesale would throw
        /// all of that away the first time anybody moved a slider.
        /// </summary>
        private void Flush()
        {
            _saveAt = 0;

            if (!_dirty || _saveBroken) return;

            var written = 0;
            var failed = 0;

            foreach (var option in _options)
            {
                if (!option.Dirty) continue;
                if (option.Persist == null || string.IsNullOrEmpty(option.Section)) continue;

                try
                {
                    if (IniFile.SetValue(Paths.Ini, option.Section, option.Key, option.Persist()))
                    {
                        option.Dirty = false;
                        written++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Once("settings-save-" + option.Key,
                             "Could not write " + option.Section + "." + option.Key + ": " + ex.Message);
                }
            }

            if (failed == 0)
            {
                _dirty = false;
                if (written > 0) Log.Info("Settings: saved " + written + " change(s) to " + Paths.Ini + ".");
                return;
            }

            // STOPS TRYING. The likeliest cause is the game folder being read-only -- GTA5.exe
            // is unelevated and the game usually lives under Program Files -- and that will
            // not fix itself, so retrying on every keypress would only produce a stream of
            // failures. Said once, loudly, and the menu shows FAILED from here on.
            _saveBroken = true;

            Log.Warn("Settings: " + failed + " value(s) could not be written to " + Paths.Ini +
                     ". Is the game folder read-only? Automatic saving is now off for this " +
                     "session; changes still apply until you reload.");

            Notify("~r~Could not save settings~s~ - see the log.");
        }

        private static void Notify(string message)
        {
            try { GTA.UI.Notification.PostTicker(message, false, false); }
            catch { /* nothing to do about it */ }
        }

        public void Shutdown()
        {
            _ui.Close();
        }
    }
}
