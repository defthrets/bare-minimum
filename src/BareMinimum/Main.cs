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
        private readonly Knock _knock;
        private readonly Catalogue _catalogue;
        private readonly Eating _eating;
        private readonly Counters _counters;
        private readonly Vendors _vendors;

        /// <summary>The shops' voice on Hoodrich's feed. Dormant when Hoodrich is absent.</summary>
        private readonly Social.Socials _socials;

        /// <summary>What the player says over a purchase. Silent when switched off.</summary>
        private readonly Speech _speech;
        private readonly Shop _shop;
        private readonly SettingsPanel _settings;
        private readonly Gauge _gauge;
        private readonly Pantry _pantry;
        private readonly Bag _bag;

        /// <summary>The fridge: where it is, what is in it, and the screen over it.</summary>
        private readonly Fridges _fridges;
        private readonly Larder _larder;
        private readonly FridgeScreen _fridge;

        private int _failures;
        private bool _parked;
        private bool _greeted;

        public Main()
        {
            _cfg = Core.Settings.Load();

            _needs = new Needs.Needs(_cfg);
            _effects = new Effects(_cfg);
            _beds = new Beds();
            _knock = new Knock(_cfg);
            _sleeping = new Sleeping(_cfg, _needs, _beds, _knock);

            _catalogue = new Catalogue(_cfg);

            // The catalogue reads the lines out of foods.json; Speech decides when any of
            // them is worth saying. Handed over AFTER the catalogue has loaded, or the sets
            // are copied while still empty.
            _speech = new Speech(_cfg);
            _speech.Load(_catalogue.Lines);

            _eating = new Eating(_cfg, _catalogue, _needs, _speech);

            // AFTER the catalogue, because the pantry drops anything it is carrying that
            // foods.json no longer defines, and it cannot know that until the list is read.
            _pantry = new Pantry(_cfg, _catalogue);

            // Same rule as the pantry: AFTER the catalogue, because the load drops anything
            // foods.json no longer defines and it cannot know that until the list is read.
            _larder = new Larder(_cfg, _catalogue);
            _fridges = new Fridges();

            _counters = new Counters();
            _socials = new Social.Socials(_cfg);
            _socials.Load();

            _vendors = new Vendors(_cfg, _catalogue, _eating, _needs, _socials, _pantry);
            _shop = new Shop(_cfg, _catalogue, _counters, _eating, _needs, _pantry);

            _bag = new Bag(_cfg, _catalogue, _pantry, _eating);
            _fridge = new FridgeScreen(_cfg, _catalogue, _pantry, _larder, _eating, _fridges);

            // The bridge other mods reach by reflection. Wired LAST, so anything that finds
            // the type finds a working one behind it -- Api.Pantry.Ready is false until this
            // line runs, and the caller is expected to keep asking rather than resolve once.
            Api.Pantry.Wire(_pantry, _catalogue, _eating, _needs);

            _settings = new SettingsPanel(_cfg, _needs);
            _gauge = new Gauge(_cfg);

            Interval = 0;
            Tick += OnTick;
            Aborted += OnAborted;

            Log.Info(Build.Name + " " + Build.Version + " loaded. Interact " +
                     _cfg.InteractKey + ", menu " + _cfg.MenuKey + ", pocket " + _cfg.BagKey + ", " +
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

                // Runs before anything else and regardless of state: it is the tail end of a
                // menu that has ALREADY closed, and its whole job is the frames after.
                UI.Menu.Cooldown();

                // Whether the panels move at all: the same switch the HUD icons obey, read
                // every tick so flipping it in the settings menu takes effect in that menu.
                UI.Theme.Motion = _cfg.HudAnimate;

                // THE SETTINGS MENU RUNS EVEN WHEN THE MOD IS SWITCHED OFF, and it has to:
                // "Mod enabled" is a row inside it, so gating it behind that flag would make
                // turning the mod off a one-way trip that could only be undone by editing the
                // ini and reloading scripts.
                //
                // It stands down while the shop is up. F7 is a RAW KEY, so the menu's
                // control-suppression cannot block it the way it blocks the game's own inputs
                // -- without this, pressing F7 at a till draws both menus on top of each other
                // and every arrow press drives both of them at once.
                _settings.Update(_sleeping.Busy || _shop.IsOpen || _vendors.MenuOpen ||
                                 _bag.IsOpen || _fridge.IsOpen);

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
                var menuOpen = _shop.IsOpen || _settings.IsOpen || _vendors.MenuOpen ||
                               _bag.IsOpen || _fridge.IsOpen;

                // The pocket stands down for every other menu for the same reason the settings
                // panel does: its key is RAW, so control suppression cannot keep it out of a
                // menu that is already up.
                _bag.Update(_sleeping.Busy || _shop.IsOpen || _settings.IsOpen ||
                            _vendors.MenuOpen || _fridge.IsOpen);

                // THE FRIDGE STANDS DOWN FOR EVERY OTHER MENU AND FOR THE VENDORS' PROMPT.
                // It reads the same interact key a stall does, so a fridge somehow within
                // reach of one would otherwise have both of them answering the same press.
                _fridge.Update(_sleeping.Busy || _shop.IsOpen || _settings.IsOpen ||
                               _vendors.Offering || _bag.IsOpen);

                if (!menuOpen && !_vendors.Offering) _sleeping.Update();

                // EVERY OTHER MENU, NEVER ITS OWN. A pass that is told it is suspended closes
                // whatever it has open -- that is what suspended means -- so handing one the
                // combined flag hands it its own shelf, and it shuts that shelf on the frame
                // after opening it, before a single Draw. Every shop with a shelf did exactly
                // this from the pocket commit on: prompt, press, nothing. The counters were
                // never affected only because their line below was never given _shop.IsOpen.
                //
                // Street vendors BEFORE the shop, so a stand standing next to a vending
                // machine wins the interact key rather than both reading it on one frame.
                _vendors.Update(dt, _sleeping.Busy || _shop.IsOpen || _settings.IsOpen ||
                                    _bag.IsOpen || _fridge.IsOpen);

                _shop.Update(_sleeping.Busy || _settings.IsOpen || _bag.IsOpen ||
                             _fridge.IsOpen || _vendors.Offering);
                _eating.Update();

                // While the sleep sequence owns the screen, the effects and the HUD stand
                // down -- a limp applied through a fade is still applied when you wake up,
                // and an icon drawn over black is an icon floating on a black screen.
                //
                // It is also what stops Needs reading OUR OWN clock jump as somebody else's.
                var suspended = _sleeping.Busy;

                _pantry.Update(dt);
                _larder.Update(dt);

                // The officers at the window run their own little scene, and it must tick
                // whatever else is happening -- it is watching for you to drive off.
                _knock.Update();

                _needs.Update(dt, suspended);
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
                // NAMES THE KEY, like every other mod in this scripts\ folder does -- Posted
                // Up says F2 for the phone, Overspray says F3 for the can. A load message that
                // only says a version tells somebody nothing they can act on, and the whole
                // point of the line is to be read once and remembered.
                //
                // The key comes from the settings rather than being written out, so rebinding
                // it in the ini changes what the greeting says.
                GTA.UI.Notification.PostTicker(
                    "~b~" + Build.Name + " " + Build.Version + " - by " + Build.By + "~s~ loaded.  " +
                    "Press ~b~" + _cfg.MenuKey + "~s~ for settings.", false, false);
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
            try { _knock.Shutdown(); } catch (Exception ex) { Log.Error("Knock shutdown", ex); }
            try { _effects.Clear(); } catch (Exception ex) { Log.Error("Clearing effects", ex); }
            // BOTH STORES, AND THE PANTRY WAS MISSING FROM HERE. They save on a ten second
            // timer, so a reload -- which SHVDN does on a keypress -- could drop up to ten
            // seconds of shopping. The needs were written down on the way out and the food
            // was not.
            try { _pantry.SaveNow(); } catch (Exception ex) { Log.Error("Pantry save", ex); }
            try { _larder.SaveNow(); } catch (Exception ex) { Log.Error("Fridge save", ex); }
            try { _needs.SaveNow(); } catch (Exception ex) { Log.Error("Final save", ex); }

            Log.Info(Build.Name + " stopped cleanly.");
        }
    }
}
