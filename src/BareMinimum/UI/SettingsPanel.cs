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
    /// CHANGES APPLY LIVE BUT SAVE ONLY WHEN ASKED. The live half is the point of having it
    /// in-game at all -- you nudge the HUD size and watch it change, rather than alt-tabbing to
    /// an ini and reloading scripts to see three pixels. The explicit save is because
    /// BareMinimum.ini is the file the player hand-edits, with their own comments and their own
    /// tuning in it, and silently rewriting that because somebody scrolled past a row would
    /// undo the one guarantee the deploy makes about it.
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
                    if (_ui.IsOpen) _ui.Close();
                    return;
                }

                if (Toggled())
                {
                    if (_ui.IsOpen) _ui.Close();
                    else { _ui.Open(); Refill(); }
                }

                if (!_ui.IsOpen) return;

                _ui.Update();

                if (_ui.Adjusted != null)
                {
                    var option = _ui.Adjusted.Tag as Option;
                    if (option != null && option.Nudge != null)
                    {
                        option.Nudge(_ui.AdjustBy);
                        _dirty = true;
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
                        _dirty = true;
                        Refill();
                    }
                }

                Subtitle();
                _ui.Draw();
            }
            catch (Exception ex)
            {
                Log.Once("settings-panel", "The settings menu failed: " + ex.Message);
                _ui.Close();
            }
        }

        private void Subtitle()
        {
            _ui.Subtitle = _dirty
                ? "~y~Unsaved~s~   left/right to change, enter to toggle"
                : "left/right to change, enter to toggle";
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
                _ui.Rows.Add(new Row
                {
                    Left = option.Name,
                    Right = option.Show == null ? "" : option.Show(),
                    Note = option.Note,
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

            Bool("Starving costs health", "Effects", "StarvingCostsHealth",
                 () => _cfg.StarvingCostsHealth, v => _cfg.StarvingCostsHealth = v,
                 "Slowly, and it stops at a sixth of your health. It will not kill you.");

            Bool("Sleep in beds", "Sleeping", "InBeds",
                 () => _cfg.SleepInBeds, v => _cfg.SleepInBeds = v,
                 "Safehouse beds, and any other bed the game will admit to.");

            Bool("Sleep in cars", "Sleeping", "InCars",
                 () => _cfg.SleepInCars, v => _cfg.SleepInCars = v,
                 "Stopped, engine off, no wanted level.");

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
                  "Only used when auto position is off.");

            Float("HUD Y", "HUD", "Y",
                  () => _cfg.HudY, v => _cfg.HudY = v,
                  0.004f, -0.2f, 1.2f, "0.000",
                  "Only used when auto position is off.");

            Bool("Hide when fine", "HUD", "HideWhenFine",
                 () => _cfg.HudHideWhenFine, v => _cfg.HudHideWhenFine = v,
                 "Hides an icon while that need is comfortable.");

            Bool("Flash when critical", "HUD", "FlashWhenCritical",
                 () => _cfg.HudFlashWhenCritical, v => _cfg.HudFlashWhenCritical = v,
                 "Pulses an icon once its need is empty.");

            // ---- actions ------------------------------------------------------

            _options.Add(new Option
            {
                Name = "Save to BareMinimum.ini",
                Note = "Writes these values to the ini, keeping your comments and layout.",
                Show = () => _dirty ? "unsaved" : "saved",
                Activate = Save
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
                          Func<bool> get, Action<bool> set, string note)
        {
            _options.Add(new Option
            {
                Name = name,
                Note = note,
                Section = section,
                Key = key,
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
                           float step, float min, float max, string format, string note)
        {
            _options.Add(new Option
            {
                Name = name,
                Note = note,
                Section = section,
                Key = key,
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
        /// Writes every value back to the ini, one key at a time.
        ///
        /// KEY BY KEY, THROUGH IniFile.SetValue, rather than serialising the whole settings
        /// object over the file. The ini ships with a page of comments explaining what each
        /// setting does and why the defaults are what they are, and rewriting it wholesale
        /// would throw all of that away the first time anybody pressed save.
        /// </summary>
        private void Save()
        {
            var written = 0;
            var failed = 0;

            foreach (var option in _options)
            {
                if (option.Persist == null || string.IsNullOrEmpty(option.Section)) continue;

                try
                {
                    if (IniFile.SetValue(Paths.Ini, option.Section, option.Key, option.Persist()))
                        written++;
                    else
                        failed++;
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
                Log.Info("Settings: wrote " + written + " value(s) to " + Paths.Ini + ".");
                Notify("~g~Saved~s~ " + written + " setting(s).");
                return;
            }

            // Left dirty on purpose. The most likely cause is the game folder being unwritable
            // -- GTA5.exe is unelevated and the game usually lives under Program Files -- and
            // saying "saved" over that would be a lie the player only finds out about later.
            Log.Warn("Settings: " + failed + " value(s) could not be written to " + Paths.Ini +
                     ". Is the game folder read-only?");
            Notify("~r~Could not save~s~ - see the log.");
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
