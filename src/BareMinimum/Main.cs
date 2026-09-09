using System;
using GTA;
using BareMinimum.Core;
using BareMinimum.Food;
using BareMinimum.Needs;
using BareMinimum.UI;
using BareMinimum.Venues;
using BareMinimum.Vitals;

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

        /// <summary>Watches the soda machines this mod deliberately does not sell from.</summary>
        private readonly Sipping _sipping;

        /// <summary>Marks the machines and stalls, which have no coordinate to blip.</summary>
        private readonly MachineBlips _machineBlips;
        private readonly Needs.Parched _parched;
        private readonly Survey _survey;
        private readonly Vendors _vendors;

        /// <summary>The way into a bar's room, and back out. Nothing without a room in vendors.json.</summary>
        private readonly Inside _inside;

        /// <summary>The card naming whichever shop the cursor is over on the pause map.</summary>
        private readonly MapCard _mapCard;

        /// <summary>The shops' voice on Hoodrich's feed. Dormant when Hoodrich is absent.</summary>
        private readonly Social.Socials _socials;

        /// <summary>What the player says over a purchase. Silent when switched off.</summary>
        private readonly Speech _speech;
        private readonly Venues.Whereabouts _whereabouts;
        private readonly Shop _shop;
        private readonly SettingsPanel _settings;
        private readonly Gauge _gauge;

        /// <summary>Health, armour and energy -- the vitals, once a mod of their own. See Vitals.VitalsHud.</summary>
        private readonly VitalsHud _vitals;
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

            // WHERE HE IS, NOW AND THEN. The game has a line for a hundred and one places in
            // each protagonist's own voice; the list of which ones comes out of foods.json with
            // the rest of the speech, so a name is checked rather than guessed.
            _whereabouts = new Venues.Whereabouts(_cfg, _speech);

            string[] places;
            _whereabouts.Load(_catalogue.Lines.TryGetValue("locations", out places) ? places : null);

            _eating = new Eating(_cfg, _catalogue, _needs, _speech);

            // AFTER the catalogue, because the pantry drops anything it is carrying that
            // foods.json no longer defines, and it cannot know that until the list is read.
            _pantry = new Pantry(_cfg, _catalogue);

            // Same rule as the pantry: AFTER the catalogue, because the load drops anything
            // foods.json no longer defines and it cannot know that until the list is read.
            _larder = new Larder(_cfg, _catalogue);
            _fridges = new Fridges(_cfg);

            _counters = new Counters(_cfg);
            _sipping = new Sipping(_cfg, _needs);
            _machineBlips = new MachineBlips(_cfg);
            _parched = new Needs.Parched(_cfg, _speech);
            _survey = new Survey(_cfg);
            _socials = new Social.Socials(_cfg);
            _socials.Load();

            _vendors = new Vendors(_cfg, _catalogue, _eating, _needs, _socials, _pantry);
            _inside = new Inside(_cfg, _vendors);
            _vendors.Doors = _inside;
            _mapCard = new MapCard(_cfg, _vendors, _machineBlips);
            _shop = new Shop(_cfg, _catalogue, _counters, _eating, _needs, _pantry);

            _bag = new Bag(_cfg, _catalogue, _pantry, _eating, _needs);
            _fridge = new FridgeScreen(_cfg, _catalogue, _pantry, _larder, _eating, _fridges);

            // The bridge other mods reach by reflection. Wired LAST, so anything that finds
            // the type finds a working one behind it -- Api.Pantry.Ready is false until this
            // line runs, and the caller is expected to keep asking rather than resolve once.
            Api.Pantry.Wire(_pantry, _catalogue, _eating, _needs);

            _settings = new SettingsPanel(_cfg, _needs);

            // THE MENU ASKS RATHER THAN HOLDS. See SettingsPanel.ForgetMachines -- a menu that
            // took a MachineBlips would be a menu that has to be rebuilt every time that class
            // changes shape, for a row that wants one number and one button.
            _settings.MachineCount = () => _machineBlips.Count;
            _settings.ForgetMachines = () => _machineBlips.Forget();

            // THE SURVEY IS A TOOL FOR FILLING THE ABOVE, so it lives on the same page as the
            // count and the button that empties it. It collects nothing itself -- MachineBlips
            // is already looking; this only moves the player. See Venues.Survey.
            _settings.SurveyRunning = () => _survey.Running;
            _settings.SurveyProgress = () => _survey.Progress();
            _settings.SurveyLeft = () => _survey.Left();
            _settings.ToggleSurvey = () => _survey.Toggle();
            _gauge = new Gauge(_cfg);

            // The vitals stand in the gauge's row, so the gauge is handed them: it asks how
            // many slots they take before placing its own two, and has them draw after.
            _vitals = new VitalsHud(_cfg);
            _gauge.Vitals = _vitals;
            _vitals.Gauge = _gauge;

            // A DRINK HOLDS THE ENERGY BAR FULL -- a soft drink, a coffee, a water, a juice.
            // Not alcohol. Wired here because Eating knows the drink and the vitals own the
            // meter, and neither needs to know the other exists.
            _eating.Drank = item =>
            {
                if (item.Booze <= 0f) _vitals.HoldEnergy(_cfg.EnergyDrinkHoldMinutes);
            };

            // AND A STIMULANT PINS IT AT THE TOP AND SETS IT SHIMMERING, for as long as that
            // one rides -- Dope's table says how long each is worth. Same arrangement as the
            // drink above and for the same reason: Dope knows the drug, the vitals own the
            // meter, and neither has to know the other is there.
            Food.Dope.Rush = minutes => _vitals.HoldEnergy(minutes, true);

            // AND A DRY THROAT COSTS YOU YOUR WIND. The energy bar asks how thirsty he
            // is rather than being told, so the vitals still know nothing about the
            // needs and the needs nothing about the vitals -- the same seam as the two
            // lines above. See Vitals.Energy.Dry.
            Vitals.Energy.Thirst = () => _needs.Thirst.Value;

            // AND WHAT HE SOUNDS LIKE WHEN HIS LEGS GO. A grunt out of the game's own
            // non-verbal set the moment the energy runs out, with an out-of-breath line
            // behind it, and the sound of finishing a workout when he has his wind back.
            _vitals.Winded = out_ =>
            {
                if (out_)
                {
                    _speech.Noise(Food.Speech.Pain.Exhaustion);
                    _speech.Say("winded");
                }
                else
                {
                    _speech.Say("caught");
                }
            };

            // WHAT A DRUG DOES TO HIM OUT LOUD. Dope knows which camp it was; the lines are in
            // foods.json with the rest. Not gated on having been served by anybody -- a man
            // reacting to what he has just taken is not thanking a cashier.
            Food.Dope.Reacted = set => _speech.Say(set);

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
                UI.Kit.PadInteract = _cfg.PadInteractLabel;

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

                // THE VITALS RUN BEFORE THE ENABLED GATE, because the game's own bars are
                // hidden by them and have to be put back when the mod is switched off -- a
                // pass that stopped running would leave the health strip missing. They draw
                // their own strip and the minimap's frame here; their upright columns are
                // drawn by the gauge, which asks them how many slots they take.
                _vitals.Update(_cfg.Enabled && _cfg.VitalsEnabled);

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
                               _vendors.Offering || _inside.Offering || _bag.IsOpen);

                if (!menuOpen && !_vendors.Offering && !_inside.Offering) _sleeping.Update();

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

                // The room after the vendors, and never while the bar's own shelf is up -- the
                // shelf owns the key then, and a hold that leaves with it open is two things
                // reading one press.
                _inside.Update(_sleeping.Busy || _shop.IsOpen || _settings.IsOpen ||
                               _bag.IsOpen || _fridge.IsOpen || _vendors.MenuOpen);

                // NOT GATED ON A MENU. It is watching for the GAME's animation, which the
                // player triggers with nothing of ours open, and a suspended tick would miss
                // the one frame the clip starts on.
                _sipping.Update();
                _machineBlips.Update();

                // Only ever does anything inside the pause menu, which is the one place the
                // rest of the HUD stands down -- so it is not gated on any of it.
                _mapCard.Update();

                _shop.Update(_sleeping.Busy || _settings.IsOpen || _bag.IsOpen ||
                             _fridge.IsOpen || _vendors.Offering || _inside.Offering);
                _eating.Update();
                _whereabouts.Update();

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
                _parched.Update(_needs, suspended);

                // ON THE SAME dt EVERYTHING ELSE USES, so the distance flown is real seconds
                // rather than frames -- a survey that moved per frame would cover twice the
                // ground on a fast machine and outrun the streamer on exactly the machines
                // that could otherwise have kept up.
                _survey.Update(dt);
                _gauge.Draw(_needs, suspended);

                // LAST, so a card rides over the gauge rather than under it -- and outside
                // the menus' own suspension, because a wake-up card is worth seeing whatever
                // else happens to be open.
                UI.Toast.Draw(suspended);

                // UNDER THE CARD, because a card is a result and a hint is an offer, and if
                // the two ever did land on the same frame the result is the one to read.
                UI.Hint.Draw(suspended);

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
                Core.Compat.Ticker(
                    "~b~" + Build.Name + " " + Build.Version + " - by " + Build.By + "~s~ loaded.  " +
                    "Press ~b~" + _cfg.MenuKey + "~s~ for settings.");
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
                Core.Compat.Ticker(
                    "~r~" + Build.Name + " stopped~s~ - see " + Paths.Stem + ".log.");
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
            try { _inside.Shutdown(); } catch (Exception ex) { Log.Error("Room shutdown", ex); }
            try { _vitals.Shutdown(); } catch (Exception ex) { Log.Error("Vitals shutdown", ex); }
            try { _vendors.Shutdown(); } catch (Exception ex) { Log.Error("Vendor shutdown", ex); }
            try { _eating.Shutdown(); } catch (Exception ex) { Log.Error("Eating shutdown", ex); }
            try { UI.Toast.Clear(); } catch { /* a card is not worth a failed shutdown */ }
            try { UI.Hint.Clear(); } catch { /* nor is a hint */ }
            // WRITTEN BEFORE THE BLIPS GO. Clear only takes the markers off the map; what
            // was found this session is on the list and owed to disk, and a reload two
            // seconds after driving past a machine should not lose it.
            // BEFORE ANYTHING ELSE ON THE WAY OUT. It has the player frozen, intangible and
            // invisible sixty metres over Los Santos, and every other line here can afford to
            // fail where this one cannot.
            try { _survey.Stop("the script unloaded"); } catch (Exception ex) { Log.Error("Survey shutdown", ex); }

            try { _machineBlips.Shutdown(); } catch (Exception ex) { Log.Error("Machine shutdown", ex); }
            try { _machineBlips.Clear(); } catch { /* a stray blip is not worth a failed shutdown */ }
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
