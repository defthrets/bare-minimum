using System;
using GTA;
using BareMinimum.Core;
using BareMinimum.Food;
using BareMinimum.Needs;
using BareMinimum.UI;
using BareMinimum.Venues;

namespace BareMinimum
{
    /// <summary>
    /// Script entry point and the only owner of the update loop.
    ///
    /// ONE Script subclass, deliberately. SHVDN instantiates every Script it finds and ticks
    /// them in an order it does not define; a single entry point means the order our own
    /// subsystems run in is ours to decide, and there is exactly one place that has to be
    /// exception-safe.
    /// </summary>
    public sealed class Main : Script
    {
        /// <summary>Consecutive tick failures before the script parks itself rather than spamming.</summary>
        private const int MaxConsecutiveFailures = 10;

        /// <summary>
        /// Core.Settings, spelt out in full every time.
        ///
        /// Script -- the SHVDN base class -- has its own inherited Settings property, and it
        /// shadows our type in expression position. Written bare, Settings.Load() does not
        /// compile and the error points at something else entirely.
        /// </summary>
        private readonly Core.Settings _cfg;

        private readonly Needs.Needs _needs;
        private readonly Effects _effects;
        private readonly Beds _beds;
        private readonly Sleeping _sleeping;
        private readonly Catalogue _catalogue;
        private readonly Eating _eating;
        private readonly Counters _counters;
        private readonly Vendors _vendors;
        private readonly Shop _shop;
        private readonly SettingsPanel _settings;
        private readonly Gauge _gauge;

        private int _failures;
        private bool _parked;
        private bool _greeted;

        public Main()
        {
            _cfg = Core.Settings.Load();

            _needs = new Needs.Needs(_cfg);
            _effects = new Effects(_cfg);
            _beds = new Beds();
            _sleeping = new Sleeping(_cfg, _needs, _beds);

            _catalogue = new Catalogue(_cfg);
            _eating = new Eating(_catalogue, _needs);
            _counters = new Counters();
            _vendors = new Vendors(_cfg, _catalogue, _eating);
            _shop = new Shop(_cfg, _catalogue, _counters, _eating, _needs);

            _settings = new SettingsPanel(_cfg, _needs);
            _gauge = new Gauge(_cfg);

            Interval = 0;
            Tick += OnTick;
            Aborted += OnAborted;

            Log.Info(Build.Name + " " + Build.Version + " loaded. Interact " +
                     _cfg.InteractKey + ", menu " + _cfg.MenuKey + ", " +
                     _catalogue.Count + " item(s), " + _vendors.Count + " vendor(s).");

            if (!_cfg.Enabled)
            {
                Log.Warn("[General] Enabled is false - nothing will run until it is turned on.");
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_parked) return;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                Greet();

                // THE SETTINGS MENU RUNS EVEN WHEN THE MOD IS SWITCHED OFF, and it has to:
                // "Mod enabled" is a row inside it, so gating it behind that flag would make
                // turning the mod off a one-way trip that could only be undone by editing the
                // ini and reloading scripts.
                //
                // It stands down while the shop is up. F7 is a RAW KEY, so the menu's
                // control-suppression cannot block it the way it blocks the game's own inputs
                // -- without this, pressing F7 at a till draws both menus on top of each other
                // and every arrow press drives both of them at once.
                _settings.Update(_sleeping.Busy || _shop.IsOpen);

                if (!_cfg.Enabled) return;

                var dt = Game.LastFrameTime;

                // A paused or hitching game hands back a dt of zero or of several seconds.
                // Only the save timer uses this -- the needs themselves run on the GAME clock,
                // which is the whole point of them -- but a wild value would still make the
                // state file get written on a stutter.
                if (dt < 0f || dt > 1f) dt = 0f;

                _catalogue.CheckProps();

                // A menu owns the interact key while it is up, so nothing else may read it.
                // Without this, pressing E to buy a sandwich at a counter next to a bed would
                // also be pressing E to go to sleep.
                var menuOpen = _shop.IsOpen || _settings.IsOpen;

                if (!menuOpen && !_vendors.Offering) _sleeping.Update();

                // Street vendors BEFORE the shop, so a stand standing next to a vending
                // machine wins the interact key rather than both reading it on one frame.
                _vendors.Update(dt, _sleeping.Busy || menuOpen);

                _shop.Update(_sleeping.Busy || _settings.IsOpen || _vendors.Offering);
                _eating.Update();

                // While the sleep sequence owns the screen, the effects and the HUD stand
                // down -- a limp applied through a fade is still applied when you wake up,
                // and an icon drawn over black is an icon floating on a black screen.
                var suspended = _sleeping.Busy;

                _needs.Update(dt);
                _effects.Update(_needs, suspended);
                _gauge.Draw(_needs, suspended);

                _failures = 0;
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>Says hello once, after the game is running rather than in the constructor.</summary>
        private void Greet()
        {
            if (_greeted) return;
            _greeted = true;

            if (!_cfg.AnnounceOnLoad) return;

            try
            {
                GTA.UI.Notification.PostTicker(
                    "~b~" + Build.Name + "~s~ " + Build.Version + " by " + Build.By, false, false);
            }
            catch
            {
                // Not being able to say hello is not a reason to stop.
            }
        }

        private void Fail(Exception ex)
        {
            _failures++;
            Log.Error("Tick failed (" + _failures + "/" + MaxConsecutiveFailures + ")", ex);

            if (_failures < MaxConsecutiveFailures) return;

            _parked = true;
            Log.Error("Ten ticks in a row have failed. " + Build.Name +
                      " has stopped itself rather than keep throwing. See above for the cause.");

            try
            {
                GTA.UI.Notification.PostTicker(
                    "~r~" + Build.Name + " stopped~s~ - see " + Paths.Stem + ".log.", false, false);
            }
            catch
            {
                // Nothing further to try.
            }

            Cleanup();
        }

        private void OnAborted(object sender, EventArgs e)
        {
            Cleanup();
        }

        /// <summary>
        /// Leaves the player exactly as it found them, and writes the needs down.
        ///
        /// Runs on a reload as well as on shutdown, because SHVDN reloads scripts on a
        /// keypress -- and a mod that leaves somebody permanently drunk and limping every time
        /// a script is reloaded is a mod that ruins a save quietly.
        /// </summary>
        private void Cleanup()
        {
            try { _settings.Shutdown(); } catch (Exception ex) { Log.Error("Settings shutdown", ex); }
            try { _shop.Shutdown(); } catch (Exception ex) { Log.Error("Shop shutdown", ex); }
            try { _vendors.Shutdown(); } catch (Exception ex) { Log.Error("Vendor shutdown", ex); }
            try { _eating.Shutdown(); } catch (Exception ex) { Log.Error("Eating shutdown", ex); }
            try { _sleeping.Shutdown(); } catch (Exception ex) { Log.Error("Sleep shutdown", ex); }
            try { _effects.Clear(); } catch (Exception ex) { Log.Error("Clearing effects", ex); }
            try { _needs.SaveNow(); } catch (Exception ex) { Log.Error("Final save", ex); }

            Log.Info(Build.Name + " stopped cleanly.");
        }
    }
}
