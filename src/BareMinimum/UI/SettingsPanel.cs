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

            /// <summary>Where it lives in the ini. Empty for an action row.</summary>
            public string Section = "";
            public string Key = "";

            public Func<string> Show;
            public Action<int> Nudge;

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
            _ui.TitleLeft = new Icon("apple2.png");
            _ui.TitleRight = new Icon("eye2.png");

            Build();
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

                if (_ui.Adjusted != null)
                {
                    var option = _ui.Adjusted.Tag as Option;
                    if (option != null && option.Nudge != null)
                    {
                        option.Nudge(_ui.AdjustBy);
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
                        option.Nudge(1);
                        Touch(option);
                        Refill();
                    }
                }

                if (Due()) Flush();

                Subtitle();
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
                _ui.Subtitle = "~r~Cannot save~s~ - see the log.  left/right to change";
                return;
            }

            _ui.Subtitle = _dirty
                ? "~y~Saving...~s~   left/right to change, enter to toggle"
                : "Saved.  left/right to change, enter to toggle";
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
                var usable = option.Available == null || option.Available();

                _ui.Rows.Add(new Row
                {
                    Left = option.Name,
                    Right = option.Show == null ? "" : option.Show(),
                    Note = usable || string.IsNullOrEmpty(option.Unavailable)
                        ? option.Note
                        : option.Unavailable,
                    Enabled = usable,
                    Tag = option
                });
            }
        }

        // ======================================================================
        // The options
        // ======================================================================

        private void Build()
        {
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

            Float("Bed hours", "Sleeping", "BedHours",
                  () => _cfg.BedHours, v => _cfg.BedHours = v,
                  1f, 1f, 24f, "0", "How long a proper night lasts.");

            Float("Car hours", "Sleeping", "CarHours",
                  () => _cfg.CarHours, v => _cfg.CarHours = v,
                  1f, 1f, 24f, "0", "How long a doze in a car lasts.");

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

            Bool("Show HUD", "HUD", "Show",
                 () => _cfg.ShowHud, v => _cfg.ShowHud = v,
                 "The apple and the eye beside the minimap.");

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
                  "Clear air between the two icons.");

            Float("HUD opacity", "HUD", "Opacity",
                  () => _cfg.HudOpacity, v => _cfg.HudOpacity = v,
                  0.05f, 0.05f, 1f, "0.00", "");

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

            Bool("Hide when fine", "HUD", "HideWhenFine",
                 () => _cfg.HudHideWhenFine, v => _cfg.HudHideWhenFine = v,
                 "Hides an icon while that need is comfortable.");

            Bool("Flash when critical", "HUD", "FlashWhenCritical",
                 () => _cfg.HudFlashWhenCritical, v => _cfg.HudFlashWhenCritical = v,
                 "Pulses an icon once its need is empty.");

            // ---- actions ------------------------------------------------------

            _options.Add(new Option
            {
                Name = "Settings",
                Note = "Saved automatically to BareMinimum.ini, keeping your comments and " +
                       "layout. Press to write them now.",
                Show = () => _saveBroken ? "FAILED" : _dirty ? "saving" : "saved",
                Activate = () => { _saveAt = 0; _saveBroken = false; Flush(); }
            });

            _options.Add(new Option
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

            _options.Add(new Option
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
        }

        // ======================================================================
        // Option builders
        // ======================================================================

        private void Bool(string name, string section, string key,
                          Func<bool> get, Action<bool> set, string note,
                          Func<bool> available = null, string unavailable = "")
        {
            _options.Add(new Option
            {
                Name = name,
                Note = note,
                Section = section,
                Key = key,
                Available = available,
                Unavailable = unavailable,
                Show = () => get() ? "ON" : "OFF",
                Nudge = dir => set(!get()),
                Persist = () => get() ? "true" : "false"
            });
        }

        /// <summary>
        /// A number with a step, a range and a display format.
        ///
        /// The value is ROUNDED TO THE STEP after every nudge. Repeated floating-point addition
        /// drifts -- twenty presses of +0.02 from 0.34 does not land on 0.74, it lands on
        /// 0.7400000000000001, which then formats as the right thing while quietly failing an
        /// equality check and writing a mess into the ini.
        /// </summary>
        private void Float(string name, string section, string key,
                           Func<float> get, Action<float> set,
                           float step, float min, float max, string format, string note,
                           Func<bool> available = null, string unavailable = "")
        {
            _options.Add(new Option
            {
                Name = name,
                Note = note,
                Section = section,
                Key = key,
                Available = available,
                Unavailable = unavailable,
                Show = () => get().ToString(format, CultureInfo.InvariantCulture),
                Nudge = dir =>
                {
                    var v = get() + step * dir;

                    if (v < min) v = min;
                    if (v > max) v = max;

                    if (step > 0f) v = (float)(Math.Round(v / step) * step);

                    set(v);
                },
                Persist = () => get().ToString("0.####", CultureInfo.InvariantCulture)
            });
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
