using System;
using System.Collections.Generic;
using GTA;
using GTA.Chrono;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.Food;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.Venues
{
    /// <summary>One street vendor: where they stand, what they sell, and what they look like.</summary>
    internal sealed class Vendor
    {
        public string Id = "";
        public string Name = "Stand";
        /// <summary>
        /// Everything this vendor might sell. One of them is on offer at a time.
        ///
        /// A market stall with three things on the board is a menu, and menus were ruled out
        /// for street vendors. A stall that has ONE thing today is a different idea: you take
        /// what they have got, and coming back later is how you get something else.
        /// </summary>
        public Offer[] Offers = new Offer[0];

        /// <summary>Which one is on offer, chosen when the player walks up. Empty until then.</summary>
        public string ItemId = "";

        public Vector3 Position;
        public float Heading;

        public float PedBack = 1.1f;
        public float SpawnRange = 90f;
        public float Reach = 2.4f;

        /// <summary>
        /// Whether you can order without getting out. A drive-through.
        ///
        /// Off everywhere else on purpose: a serving hatch you can buy from while sitting in
        /// a car parked vaguely near it is a hatch that stops meaning anything.
        /// </summary>
        public bool FromVehicle;

        /// <summary>
        /// Open a shop MENU rather than the one-key prompt.
        ///
        /// The split is between a stall and a shop. A hot dog stand sells hot dogs: a menu in
        /// front of one item is ceremony, and the prompt was asked for precisely to avoid it.
        /// A liquor store has a shelf, and picking off a shelf is what a menu is for.
        /// </summary>
        public bool UseMenu;

        /// <summary>
        /// What fraction of the marked price this place actually charges. 1 is full price.
        ///
        /// FOR THE SHOP THAT IS CLOSING DOWN. Its windows are papered with BIG SALE, FINAL
        /// DAY and 70% OFF, and a shop that says 70% off while charging the same as the one
        /// down the road is a sign, not a shop. Putting it on the VENDOR rather than the item
        /// is what lets the same bag of crisps be three dollars everywhere and ninety cents
        /// here, without a second copy of every snack in the catalogue.
        /// </summary>
        public float Discount = 1f;

        /// <summary>What this vendor charges for an item. Never less than a dollar.</summary>
        public int PriceOf(Item item)
        {
            if (item == null) return 0;
            if (Discount >= 0.999f) return item.Price;

            return Math.Max(1, (int)Math.Round(item.Price * Discount));
        }

        public string[] PropModels = new string[0];
        public string[] PedModels = new string[0];
        public string Scenario = "WORLD_HUMAN_STAND_IMPATIENT";

        /// <summary>
        /// A looping animation for the vendor, preferred over the scenario when it will load.
        ///
        /// An animation rather than a scenario for one reason: it can be CHECKED. There is no
        /// native that asks whether a scenario name is real, so a typo in one is a ped standing
        /// perfectly still with nothing in the log; an animation dictionary answers
        /// HAS_ANIM_DICT_LOADED, so a bad name says so and the scenario is used instead.
        /// </summary>
        public string AnimDict = "";
        public string AnimClip = "";

        /// <summary>Whether the looping animation is running, so it is started once.</summary>
        public bool AnimStarted;

        /// <summary>Whether the stand and the vendor have been dropped onto the real ground.</summary>
        public bool Settled;

        // ---- trading hours ----------------------------------------------------

        /// <summary>
        /// When he opens up and when he goes home, as hours of the day. 0 and 24 = always.
        ///
        /// The whole pitch goes, not just the man: a cart standing in the street all night
        /// that cannot be bought from is worse than an empty patch of pavement, because it
        /// looks like the mod is broken rather than like the man has gone home.
        /// </summary>
        public int OpenHour;
        public int CloseHour = 24;

        // ---- the smoke break --------------------------------------------------

        /// <summary>GAME hours between smokes. 0 means he never stops.</summary>
        public float SmokeEveryHours;

        /// <summary>
        /// How long a smoke lasts, in REAL seconds.
        ///
        /// Real rather than game, deliberately, and the two units in these four fields are
        /// each measuring what they should. How OFTEN he goes is a fact about his working day,
        /// so it belongs on the game clock. How LONG the player watches him stand there is a
        /// fact about watching, so it belongs on the real one -- five game minutes would be
        /// ten real seconds, which is not a cigarette, it is a twitch.
        /// </summary>
        public float SmokeSeconds = 45f;

        /// <summary>How far behind his post he wanders to smoke, in metres.</summary>
        public float SmokeBack = 4f;

        public string SmokeScenario = "WORLD_HUMAN_SMOKING";

        /// <summary>Where he stands to serve, and where he stands to smoke. Set when he spawns.</summary>
        public Vector3 PostAt;
        public Vector3 SmokeAt;

        public Duty Doing = Duty.Working;

        /// <summary>When the next smoke is due, on the game clock.</summary>
        public GameClockDateTime NextSmoke;
        public bool SmokeScheduled;

        /// <summary>When the current smoke ends, or a walk gives up. Real milliseconds.</summary>
        public int PhaseUntil;

        /// <summary>
        /// Whether this vendor is supposed to have somebody behind it at all.
        ///
        /// A hot dog stand is: no man, no hot dog. A taco window is not -- the hatch is part
        /// of the building and there has never been anybody modelled at it.
        ///
        /// Read off the CONFIGURED list until the models have been checked, and off the
        /// resolved model afterwards. Before resolution the intent in vendors.json is the only
        /// thing to go on; after it, a vendor whose every candidate ped turned out not to
        /// exist in this build correctly needs nobody, and sells the way it always did rather
        /// than going quiet with only a log line to say why.
        /// </summary>
        public bool NeedsSeller => Resolved ? PedModel.HasValue : PedModels.Length > 0;

        /// <summary>
        /// Whether there is actually somebody there, alive, to take the money.
        ///
        /// THIS IS THE ONE THAT WAS WRONG. AtPost used to read "Seller == null || working",
        /// so a stand whose man had been culled by the engine, or whose ped model failed to
        /// stream, counted as open -- and you could buy a hot dog from an empty cart.
        /// The null was there to let the taco window trade with nobody at it; NeedsSeller now
        /// carries that case, so absence can mean absence again.
        /// </summary>
        public bool Manned
        {
            get
            {
                if (!NeedsSeller) return true;

                return Seller != null && Seller.Exists() && !Seller.IsDead;
            }
        }

        /// <summary>True when he is at his post and able to serve.</summary>
        public bool AtPost => Manned && Doing == Duty.Working;

        /// <summary>What the prompt calls it. Falls back to the item's own name.</summary>
        public string Label = "";

        /// <summary>Whether to put a marker on the map. Sprite and colour are the game's own ids.</summary>
        public bool Blip = true;
        public int BlipSprite = 267;        // the food/burger marker
        public int BlipColour = 47;         // orange-yellow

        // ---- live state ----
        public Prop Stand;
        public Ped Seller;
        public Blip Marker;

        /// <summary>
        /// The display mode currently ON the blip, or 0 if it has never been set.
        ///
        /// Kept because SET_BLIP_DISPLAY used to be called once, at creation, and never
        /// again -- so turning the pause-map setting on did nothing to a blip that already
        /// existed. Remembering what was applied is what lets the next sweep notice the
        /// setting has changed underneath it.
        /// </summary>
        public int Display;

        /// <summary>Resolved once the candidate lists have been checked against this build.</summary>
        public Model? PropModel;
        public Model? PedModel;
        public bool Resolved;

        /// <summary>Whether the player is close enough for this to be live at all.</summary>
        public bool InRange;

        /// <summary>
        /// Whether the shutters were up last time anybody looked, or null before the first.
        ///
        /// Only so that opening and closing can be POSTS. InRange folds distance and hours
        /// together on purpose -- closed is the same as far away to everything downstream --
        /// which means it cannot tell "he has gone home" from "you have walked off", and those
        /// are very different things to say on a timeline.
        /// </summary>
        public bool? WasOpen;

        /// <summary>
        /// Whether this vendor has anything to CREATE.
        ///
        /// A hot dog stand does: there is no stand in the world until this mod makes one. A
        /// taco window does not -- the hatch, the menu board and the building are already
        /// there, and all this adds is a place to stand and a price. Spawning a chef in front
        /// of somebody else's serving window would be worse than adding nothing.
        /// </summary>
        public bool HasEntities => PropModels.Length > 0 || PedModels.Length > 0;
    }

    /// <summary>
    /// One thing a vendor might sell, and when.
    ///
    /// The hours are what let a diner serve breakfast until eleven and something else after.
    /// An offer with no hours is available all day, which is every vendor that has never
    /// needed to care -- so the plain "item": "taco" form still means exactly what it did.
    /// </summary>
    internal sealed class Offer
    {
        public string Id = "";

        /// <summary>Hours of the day this is on the board. Both -1 means always.</summary>
        public int From = -1;
        public int To = -1;

        public bool Always => From < 0 || To < 0;

        /// <summary>Whether it is on the board at this hour. Wraps past midnight.</summary>
        public bool AtHour(int hour)
        {
            if (Always) return true;
            if (From == To) return true;

            return From < To ? hour >= From && hour < To
                             : hour >= From || hour < To;
        }
    }

    /// <summary>What the vendor is currently up to.</summary>
    internal enum Duty
    {
        Working,
        WalkingOff,
        Smoking,
        WalkingBack
    }

    /// <summary>
    /// Street food stands: a prop, somebody behind it, and one thing to buy.
    ///
    /// NO MENU HERE, deliberately. The counter at a shop is a list of things at different
    /// prices, which needs a menu; a hot dog stand sells hot dogs. Walking up, being told the
    /// price in the game's own help box and pressing one key is the whole interaction, and
    /// putting a menu in front of it would be ceremony for a single item.
    ///
    /// EVERYTHING IS SPAWNED AND REMOVED BY DISTANCE. A stand that exists for the whole
    /// session is a ped and a prop sitting in the world pools forever, on top of whatever
    /// else the game is streaming; and a ped spawned once and left alone will eventually be
    /// culled by the engine, leaving a stand with nobody behind it and no way to notice.
    /// </summary>
    internal sealed class Vendors
    {
        private readonly Core.Settings _cfg;
        private readonly Catalogue _menu;
        private readonly Eating _eating;
        private readonly Needs.Needs _needs;

        /// <summary>
        /// The shops' voice on Hoodrich's timeline. Never null; dormant without Hoodrich.
        ///
        /// Passed in rather than made here so that the counter and the stalls share one rate
        /// limit. Two instances would each think they were the only one posting, and the feed
        /// would get twice what the ini asked for.
        /// </summary>
        private readonly Social.Socials _socials;

        private readonly List<Vendor> _vendors = new List<Vendor>();

        /// <summary>
        /// ONE menu for every shop vendor, not one each.
        ///
        /// Only one can be open at a time -- you cannot stand at two counters -- so a menu per
        /// vendor would be twenty-odd idle objects and twenty-odd chances for two of them to
        /// disagree about how a shop looks. It is refilled when a shop is opened.
        /// </summary>
        private readonly UI.Menu _ui = new UI.Menu
        {
            TitleLeft = new UI.Icon("p_burger.png"),
            TitleRight = new UI.Icon("p_cup.png")
        };

        /// <summary>The vendor whose shelf is currently on screen.</summary>
        private Vendor _shopping;

        /// <summary>The vendor currently within reach, if any.</summary>
        private Vendor _at;

        /// <summary>
        /// One shared Random for the whole class.
        ///
        /// Not a fresh one per roll: System.Random seeds from the clock, and two created
        /// inside the same millisecond produce identical sequences -- which is precisely what
        /// happens when a sweep touches several vendors at once.
        /// </summary>
        private static readonly Random Roll = new Random();

        private bool _keyWasDown;
        private float _sinceScan;

        public Vendors(Core.Settings cfg, Catalogue menu, Eating eating, Needs.Needs needs,
                       Social.Socials socials)
        {
            _cfg = cfg;
            _menu = menu;
            _eating = eating;
            _needs = needs;
            _socials = socials;

            Load();
        }

        public int Count => _vendors.Count;

        /// <summary>True while a stand is offering, so nothing else reads the interact key.</summary>
        public bool Offering => _at != null || _ui.IsOpen;

        /// <summary>True while a shop's shelf is on screen.</summary>
        public bool MenuOpen => _ui.IsOpen;

        // ======================================================================
        // Loading
        // ======================================================================

        private void Load()
        {
            try
            {
                var doc = JsonFile.Read(Paths.VendorsFile, out var how);

                if (how != ReadResult.Ok || doc == null || doc.IsNull)
                {
                    Log.Info("No vendors.json - no street stands.");
                    return;
                }

                foreach (var node in doc["vendors"].Items)
                {
                    var v = new Vendor
                    {
                        Id = node["id"].AsString(""),
                        Name = node["name"].AsString("Stand"),
                        Offers = ReadOffers(node["item"]),
                        Position = new Vector3(node["x"].AsFloat(0f),
                                               node["y"].AsFloat(0f),
                                               node["z"].AsFloat(0f)),
                        Heading = node["heading"].AsFloat(0f),
                        PedBack = node["pedBack"].AsFloat(1.1f),
                        SpawnRange = node["spawnRange"].AsFloat(90f),
                        Reach = node["reach"].AsFloat(2.4f),
                        FromVehicle = node["fromVehicle"].AsBool(false),
                        UseMenu = node["ui"].AsBool(false),
                        Discount = Clamp(node["discount"].AsFloat(1f), 0.05f, 1f),
                        Scenario = node["scenario"].AsString("WORLD_HUMAN_STAND_IMPATIENT"),
                        AnimDict = node["anim"]["dict"].AsString(""),
                        AnimClip = node["anim"]["clip"].AsString(""),
                        OpenHour = node["hours"]["open"].AsInt(0),
                        CloseHour = node["hours"]["close"].AsInt(24),
                        SmokeEveryHours = node["smoke"]["everyHours"].AsFloat(0f),
                        SmokeSeconds = node["smoke"]["seconds"].AsFloat(45f),
                        SmokeBack = node["smoke"]["back"].AsFloat(4f),
                        SmokeScenario = node["smoke"]["scenario"].AsString("WORLD_HUMAN_SMOKING"),
                        PropModels = Strings(node["prop"]),
                        PedModels = Strings(node["ped"]),
                        Label = node["label"].AsString(""),
                        Blip = node["blip"].AsBool(true),
                        BlipSprite = node["blipSprite"].AsInt(267),
                        BlipColour = node["blipColour"].AsInt(47)
                    };

                    if (v.Offers.Length == 0) continue;

                    _vendors.Add(v);
                }

                Log.Info("Vendors: " + _vendors.Count + " street stand(s) loaded.");
            }
            catch (Exception ex)
            {
                Log.Error("Could not read " + Paths.VendorsFile + " - no street stands.", ex);
            }
        }

        /// <summary>
        /// Reads "item" in any of its shapes: one name, a list of names, or a list of
        /// objects carrying hours -- and any mixture of the last two.
        ///
        /// Three spellings rather than one because most vendors sell one thing and should not
        /// have to be written as an array of objects to say so. The diner needs the long form;
        /// the taco window should not pay for it.
        /// </summary>
        private static Offer[] ReadOffers(Json node)
        {
            var list = new List<Offer>();

            if (node == null || node.IsNull) return list.ToArray();

            var single = node.AsString("");
            if (!string.IsNullOrEmpty(single))
            {
                list.Add(new Offer { Id = single });
                return list.ToArray();
            }

            foreach (var entry in node.Items)
            {
                var name = entry.AsString("");

                if (!string.IsNullOrEmpty(name))
                {
                    list.Add(new Offer { Id = name });
                    continue;
                }

                var id = entry["id"].AsString("");
                if (string.IsNullOrEmpty(id)) continue;

                list.Add(new Offer
                {
                    Id = id,
                    From = entry["from"].AsInt(-1),
                    To = entry["to"].AsInt(-1)
                });
            }

            return list.ToArray();
        }

        private static string[] Strings(Json node)
        {
            var list = new List<string>();

            if (node != null && !node.IsNull)
            {
                // A single bare name is allowed as well as a list, so the common case --
                // one vendor, one thing -- does not have to be written as an array.
                var single = node.AsString("");
                if (!string.IsNullOrEmpty(single)) return new[] { single };

                foreach (var item in node.Items)
                {
                    var s = item.AsString("");
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                }
            }

            return list.ToArray();
        }

        /// <summary>
        /// Picks the first candidate model this build actually has, and names the misses.
        ///
        /// Deferred until the game is running -- a Model cannot be asked anything useful
        /// during script construction, because the world does not exist yet.
        /// </summary>
        private static Model? Pick(string[] candidates, string what, string who)
        {
            var missing = new List<string>();

            foreach (var name in candidates)
            {
                try
                {
                    var model = new Model(name);

                    if (Function.Call<bool>(Hash.IS_MODEL_VALID, model.Hash))
                    {
                        if (missing.Count > 0)
                        {
                            Log.Info(who + ": " + what + " - not in this build, skipped: " +
                                     string.Join(", ", missing.ToArray()));
                        }

                        Log.Info(who + ": using " + what + " " + name + ".");
                        return model;
                    }

                    missing.Add(name);
                }
                catch
                {
                    missing.Add(name);
                }
            }

            Log.Warn(who + ": NO usable " + what + " in this build. Tried: " +
                     string.Join(", ", missing.ToArray()) +
                     ". Put a model name that exists into vendors.json.");
            return null;
        }

        // ======================================================================
        // The tick
        // ======================================================================

        public void Update(float dt, bool suspended)
        {
            if (_vendors.Count == 0) return;

            try
            {
                if (suspended) { _at = null; Close(); return; }

                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead) { _at = null; Close(); return; }

                if (_ui.IsOpen) { Shelf(me); return; }

                // Spawning is checked on a clock, not every frame. Nothing here can change
                // faster than a player can walk, and each check is a distance per vendor.
                _sinceScan += dt;
                if (_sinceScan >= 0.75f)
                {
                    _sinceScan = 0f;
                    Stream(me.Position);
                }

                Offer(me);
            }
            catch (Exception ex)
            {
                Log.Once("vendors", "The stands failed: " + ex.Message);
                _at = null;
            }
        }

        /// <summary>Creates what is near, removes what is not, and keeps the map markers.</summary>
        private void Stream(Vector3 from)
        {
            foreach (var v in _vendors)
            {
                Marker(v, from);

                // CLOSED IS THE SAME AS FAR AWAY as far as everything downstream is
                // concerned: nothing spawns, nothing is offered, the pitch is empty. The two
                // are still worked out separately, because only one of them is worth a post.
                var within = v.Position.DistanceTo(from) <= v.SpawnRange;
                var open = Trading(v);

                if (within && v.WasOpen.HasValue && v.WasOpen.Value != open)
                {
                    _socials.About(v.Id, open ? Social.Chirp.Opening : Social.Chirp.Closing,
                                   "", v.Name);
                }

                if (within) v.WasOpen = open;

                var near = within && open;

                // IN RANGE IS SEPARATE FROM SPAWNED. A taco window has nothing to spawn -- the
                // hatch is already part of the building -- so it would never count as live if
                // being live meant holding an entity, and it could never be bought from.
                //
                // The transition is logged at Debug. A vendor placed at the wrong coordinate
                // is completely silent otherwise -- it simply never offers, and there is
                // nothing to tell you whether the position is wrong, the reach is too small or
                // the item id is a typo. Set LogLevel = Debug to see them come and go.
                if (near != v.InRange)
                {
                    // CHOSEN ON ARRIVAL, not per frame. Rolling every tick would have the
                    // prompt flickering between bagels and fruit sixty times a second; rolling
                    // once as you walk up means what is on the board stays on the board for as
                    // long as you are stood at it, and is something else next time.
                    if (near)
                    {
                        v.ItemId = Today(v);

                        // ON ARRIVAL, not on a timer. A post about a shop you are nowhere near
                        // is a notification for nothing; one that lands as you walk up reads
                        // like the place is alive.
                        //
                        // ALWAYS Ambient here, never Away. Nothing has spawned yet on the
                        // frame a vendor comes into range -- Keep runs later in this same
                        // sweep -- so asking whether anybody is behind the counter would get
                        // "no" every single time and every arrival would read as a shop that
                        // had just stepped out. Away is posted from the smoke break, which is
                        // the only place that actually knows he has gone.
                        _socials.About(v.Id, Social.Chirp.Ambient, "", v.Name);
                    }

                    Log.Debug(v.Name + (near ? " in range at " : " out of range at ") +
                              v.Position.DistanceTo(from).ToString("0.0") + "m" +
                              (near && v.Offers.Length > 1 ? ", selling " + v.ItemId : "") + ".");
                }

                v.InRange = near;

                if (!v.HasEntities) continue;

                // WHATEVER IS MISSING, not only an entirely empty pitch. The old test was
                // "both null", so a stand that survived while its man did not would keep the
                // cart on the pavement for the rest of the session with nobody behind it --
                // and, before AtPost was fixed, still sell from it.
                if (near) Keep(v);
                else Despawn(v);
            }
        }

        /// <summary>
        /// The map marker: created when you are near, destroyed when you are not.
        ///
        /// CREATED AND DESTROYED BY DISTANCE rather than merely hidden. Twenty-five permanent
        /// blips is twenty-five icons competing with the ones the game already puts there,
        /// and the useful message is "there is food near you" -- not a permanent directory of
        /// every taco in the county.
        ///
        /// It also stays OFF THE PAUSE MAP by default. That map is where somebody plans a
        /// journey, and a screenful of identical shop icons is the sort of clutter that makes
        /// people turn a mod off.
        /// </summary>
        private void Marker(Vendor v, Vector3 from)
        {
            var near = _cfg.ShopBlipRange <= 0f ||
                       v.Position.DistanceTo(from) <= _cfg.ShopBlipRange;

            // A BLIP THAT DOES NOT EXIST CANNOT BE ON THE PAUSE MAP. This is what was wrong:
            // the range gate destroyed every blip further than ShopBlipRange away, so turning
            // "markers on pause map" on could only ever reveal the two or three shops already
            // within a couple of hundred metres -- which from a full-map view looks like the
            // setting does nothing at all.
            //
            // So the range gate now decides which MAP a blip appears on, not whether it is
            // allowed to exist. With the setting off it is exactly as before: near shops only,
            // minimap only, pause map clear.
            var wanted = v.Blip && _cfg.ShowShopBlips && (near || _cfg.ShopBlipsOnMainMap);

            if (!wanted)
            {
                if (v.Marker != null)
                {
                    try { if (v.Marker.Exists()) v.Marker.Delete(); }
                    catch { /* nothing to do about it */ }

                    v.Marker = null;
                    v.Display = 0;
                }

                return;
            }

            // 5  minimap only -- the default, and what keeps the pause map uncluttered.
            // 2  both maps, for a shop close enough to be worth steering towards.
            // 3  pause map only, for the rest of the city once the setting is on: they belong
            //    on the map somebody plans a journey with, not crowding the minimap from a
            //    kilometre away.
            var display = near ? (_cfg.ShopBlipsOnMainMap ? 2 : 5) : 3;

            if (v.Marker != null && v.Marker.Exists())
            {
                // RE-APPLIED WHEN IT CHANGES, which is the second half of the same bug: the
                // display used to be set once at creation, so toggling the setting left every
                // existing blip on whatever it was made with until it happened to be destroyed
                // and rebuilt by walking away and back.
                if (v.Display != display)
                {
                    try
                    {
                        Function.Call(Hash.SET_BLIP_DISPLAY, v.Marker.Handle, display);
                        v.Display = display;
                    }
                    catch (Exception ex)
                    {
                        Log.Once("vendor-blip-display-" + v.Id,
                                 "Could not restyle the blip for " + v.Name + ": " + ex.Message);
                    }
                }

                return;
            }

            try
            {
                var blip = World.CreateBlip(v.Position);
                if (blip == null || !blip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, blip.Handle, v.BlipSprite);
                Function.Call(Hash.SET_BLIP_COLOUR, blip.Handle, v.BlipColour);
                Function.Call(Hash.SET_BLIP_SCALE, blip.Handle, 0.75f);

                // Short range keeps it off the edge of the minimap when you are far from it.
                Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, blip.Handle, true);

                Function.Call(Hash.SET_BLIP_DISPLAY, blip.Handle, display);
                v.Display = display;

                Function.Call(Hash.BEGIN_TEXT_COMMAND_SET_BLIP_NAME, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, v.Name);
                Function.Call(Hash.END_TEXT_COMMAND_SET_BLIP_NAME, blip.Handle);

                v.Marker = blip;
            }
            catch (Exception ex)
            {
                Log.Once("vendor-blip-" + v.Id, "Could not blip " + v.Name + ": " + ex.Message);
                v.Blip = false;
            }
        }

        /// <summary>
        /// Rebuilds whichever half of the pitch is not there.
        ///
        /// The engine culls entities this mod did not expect it to, models occasionally fail
        /// to stream on the first sweep, and another script can delete anything at all. All
        /// three end the same way: something that should be on the pavement is not, and the
        /// next sweep quietly puts it back.
        ///
        /// SETTLED IS CLEARED whenever anything is made, because a fresh entity is at the
        /// coordinate from the file rather than on the ground, and Settle only ever runs once.
        /// Without this the replacement hangs in the air exactly the way the first one did
        /// before it was grounded.
        /// </summary>
        private void Restore(Vendor v)
        {
            if (!v.Resolved)
            {
                v.PropModel = Pick(v.PropModels, "stand prop", v.Name);
                v.PedModel = Pick(v.PedModels, "vendor ped", v.Name);
                v.Resolved = true;
            }

            try
            {
                if (v.Stand == null && v.PropModel.HasValue)
                {
                    v.Stand = MakeStand(v);
                    if (v.Stand != null) v.Settled = false;
                }

                if (v.Seller == null && v.PedModel.HasValue)
                {
                    v.Seller = MakeSeller(v);

                    if (v.Seller != null)
                    {
                        v.Settled = false;
                        v.AnimStarted = false;

                        // Back on duty and back on the clock. A man who was rebuilt part way
                        // through a cigarette would otherwise stay in Duty.Smoking forever,
                        // stood at his post and refusing to serve anybody.
                        v.Doing = Duty.Working;
                        v.SmokeScheduled = false;

                        Log.Debug(v.Name + ": put somebody back behind the counter.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Once("vendor-restore-" + v.Id, "Could not rebuild " + v.Name + ": " + ex.Message);
            }
        }

        /// <summary>How far he can drift from his post before he is fetched back. Metres.</summary>
        private const float StrayedAt = 2.5f;

        /// <summary>
        /// Walks him back if he has ended up somewhere he should not be.
        ///
        /// SEPARATE FROM Break, and it has to be: Break returns immediately for any vendor
        /// with no smoke break configured, so recovery living in there would only ever apply
        /// to the one man who smokes.
        ///
        /// ONLY WHILE HE IS WORKING. The whole point of the other three states is that he is
        /// legitimately away from the counter, and dragging him back mid-cigarette would undo
        /// the break rather than fix anything.
        ///
        /// It reuses Duty.WalkingBack instead of teleporting him, so he walks to the cart like
        /// somebody who wandered off -- and so AtPost stays false the whole way, which keeps
        /// the stand shut until he is actually stood at it again.
        /// </summary>
        private static void Stray(Vendor v)
        {
            if (v.Seller == null || !v.Seller.Exists()) return;
            if (v.Doing != Duty.Working) return;
            if (v.PostAt == Vector3.Zero) return;

            try
            {
                if (v.Seller.Position.DistanceTo(v.PostAt) <= StrayedAt) return;

                Walk(v, v.PostAt);

                v.Doing = Duty.WalkingBack;
                v.PhaseUntil = Game.GameTime + 12000;
                v.AnimStarted = false;

                Log.Debug(v.Name + ": drifted off his post, walking back.");
            }
            catch (Exception ex)
            {
                Log.Once("vendor-stray-" + v.Id, "Could not send him back: " + ex.Message);
            }
        }

        private void Spawn(Vendor v)
        {
            if (!v.Resolved)
            {
                v.PropModel = Pick(v.PropModels, "stand prop", v.Name);
                v.PedModel = Pick(v.PedModels, "vendor ped", v.Name);
                v.Resolved = true;
            }

            try
            {
                if (v.PropModel.HasValue) v.Stand = MakeStand(v);
                if (v.PedModel.HasValue) v.Seller = MakeSeller(v);
            }
            catch (Exception ex)
            {
                Log.Once("vendor-spawn-" + v.Id, "Could not build " + v.Name + ": " + ex.Message);
            }
        }

        private static Prop MakeStand(Vendor v)
        {
            var model = v.PropModel.Value;
            if (!Stream(model)) return null;

            var prop = World.CreateProp(model, v.Position, false, false);
            if (prop == null || !prop.Exists()) return null;

            prop.Heading = v.Heading;

            // ON THE GROUND, not at the z from the file. A coordinate read off a HUD is the
            // PLAYER's z, which is their feet -- close, but a stand half-sunk into the
            // pavement or floating a hand's width above it is exactly the sort of thing that
            // looks broken. The native puts it on whatever is actually there.
            Function.Call(Hash.PLACE_OBJECT_ON_GROUND_PROPERLY, prop.Handle);

            // Frozen and indestructible: a stand that can be shoved down the street by a car
            // stops being a landmark, and the mod would have no idea it had moved.
            prop.IsPositionFrozen = true;
            prop.IsInvincible = true;
            prop.IsPersistent = true;

            model.MarkAsNoLongerNeeded();
            return prop;
        }

        private static Ped MakeSeller(Vendor v)
        {
            var model = v.PedModel.Value;
            if (!Stream(model)) return null;

            // Behind the stand, facing back over it. GTA headings run anticlockwise from
            // north, so forward is (-sin, cos).
            var rad = v.Heading * (float)Math.PI / 180f;
            var forward = new Vector3(-(float)Math.Sin(rad), (float)Math.Cos(rad), 0f);

            var where = v.Position + forward * v.PedBack;

            v.PostAt = where;
            v.SmokeAt = v.Position + forward * (v.PedBack + v.SmokeBack);

            var ped = World.CreatePed(model, where, v.Heading + 180f);
            if (ped == null || !ped.Exists()) return null;

            ped.IsPersistent = true;
            ped.BlockPermanentEvents = true;     // ignores gunfire, panic, ambient events
            ped.CanRagdoll = false;
            ped.IsInvincible = true;

            try
            {
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, false);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, ped.Handle, false);

                // The scenario is the FALLBACK, started immediately so the vendor is never
                // just standing there while the animation streams. Work() replaces it the
                // moment the real one is ready.
                if (string.IsNullOrEmpty(v.AnimDict))
                {
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, v.Scenario, 0, true);
                }
            }
            catch (Exception ex)
            {
                Log.Once("vendor-ped-" + v.Id, "Could not settle the vendor: " + ex.Message);
            }

            model.MarkAsNoLongerNeeded();
            return ped;
        }

        /// <summary>
        /// Streams a model in, with a bounded wait.
        ///
        /// Bounded and short, because this runs inside the tick: a real wait stalls every
        /// other subsystem. A model that has not arrived is skipped and tried again on the
        /// next sweep, which is three quarters of a second away.
        /// </summary>
        private static bool Stream(Model model)
        {
            try
            {
                if (model.IsLoaded) return true;

                // ASKED FOR, NOT WAITED ON. A spin on Game.GameTime here is an infinite
                // loop: the timer only moves when the game renders a frame, and this runs
                // inside one. Nothing is lost by returning false -- the sweep that calls this
                // runs again in three quarters of a second, by which time it will have
                // arrived, and the stand appears a moment later than it might have.
                model.Request();
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Puts back anything the game has quietly taken away, and keeps him working.</summary>
        private void Keep(Vendor v)
        {
            if (v.Stand != null && !v.Stand.Exists()) v.Stand = null;

            if (v.Seller != null && !v.Seller.Exists())
            {
                v.Seller = null;
                v.AnimStarted = false;
            }

            // A CORPSE IS NOT A SELLER. He is spawned invincible and with events blocked, so
            // this is rare -- but a script that ignores IsInvincible, or a mission that kills
            // everything in a radius, will do it. He is left where he fell rather than
            // vanishing under the player's nose; Despawn clears the body when you walk away,
            // and a new man is on the cart when you come back.
            if (v.Seller != null && v.Seller.IsDead) return;

            Restore(v);

            Settle(v);
            Stray(v);

            // Watched across the call rather than posted from inside it, so that Break stays
            // a state machine that knows nothing about social feeds.
            var before = v.Doing;
            Break(v);

            if (before == Duty.Working && v.Doing == Duty.WalkingOff)
            {
                _socials.About(v.Id, Social.Chirp.Away, "", v.Name);
            }

            // Only tend the grill while actually at the grill. Re-applying the cooking loop to
            // a man walking away from it is how you get somebody moonwalking to a cigarette.
            if (v.Doing == Duty.Working) Work(v);
        }

        /// <summary>
        /// Whether he is open for business right now.
        ///
        /// Handles a window that wraps past midnight as well as one that does not. A stall
        /// open 18:00 to 02:00 is an ordinary thing to want, and the naive
        /// hour >= open && hour < close quietly means "never" for it.
        /// </summary>
        /// <summary>
        /// What is on the board right now: whatever the hour allows, picked at random.
        ///
        /// FALLS BACK TO EVERYTHING if nothing matches the hour. A vendor whose windows have a
        /// gap in them would otherwise be silently unbuyable at 3am with no way to tell that
        /// from a wrong coordinate -- and a shop with nothing to sell is a bug either way, so
        /// it is better to sell something and have the hours be slightly wrong.
        /// </summary>
        private static string Today(Vendor v)
        {
            if (v.Offers.Length == 0) return "";
            if (v.Offers.Length == 1) return v.Offers[0].Id;

            int hour;
            try { hour = GameClock.Hour; }
            catch { hour = 12; }

            var open = new List<Offer>();
            foreach (var o in v.Offers)
            {
                if (o.AtHour(hour)) open.Add(o);
            }

            if (open.Count == 0)
            {
                Log.Once("vendor-hours-" + v.Id,
                         v.Name + ": nothing on the board at " + hour +
                         ":00 - selling from the whole list instead. Check its item hours.");

                return v.Offers[Roll.Next(v.Offers.Length)].Id;
            }

            return open.Count == 1 ? open[0].Id : open[Roll.Next(open.Count)].Id;
        }

        private static bool Trading(Vendor v)
        {
            if (v.OpenHour == v.CloseHour) return true;

            int hour;
            try { hour = GameClock.Hour; }
            catch { return true; }

            if (v.OpenHour < v.CloseHour) return hour >= v.OpenHour && hour < v.CloseHour;

            return hour >= v.OpenHour || hour < v.CloseHour;
        }

        /// <summary>
        /// The smoke break: off he goes, stands there a while, comes back.
        ///
        /// EVERY TRANSITION HAS A TIMEOUT as well as an arrival test. A walk task can fail to
        /// finish for reasons that have nothing to do with this mod -- somebody parks on the
        /// spot, the pathfinder gives up, a passer-by is in the way -- and without a timeout he
        /// would stand halfway to his cigarette for the rest of the session with no way back.
        /// </summary>
        private static void Break(Vendor v)
        {
            if (v.Seller == null || !v.Seller.Exists()) return;
            if (v.SmokeEveryHours <= 0f) return;

            GameClockDateTime now;
            try { now = GameClock.Now; }
            catch { return; }

            if (!v.SmokeScheduled)
            {
                v.NextSmoke = now + GameClockDuration.FromMinutes((long)(v.SmokeEveryHours * 60f));
                v.SmokeScheduled = true;
                return;
            }

            var ms = Game.GameTime;

            switch (v.Doing)
            {
                case Duty.Working:
                    if (now < v.NextSmoke) return;

                    Walk(v, v.SmokeAt);
                    v.Doing = Duty.WalkingOff;
                    v.PhaseUntil = ms + 12000;
                    v.AnimStarted = false;
                    return;

                case Duty.WalkingOff:
                    if (ms < v.PhaseUntil && v.Seller.Position.DistanceTo(v.SmokeAt) > 1.3f) return;

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, v.Seller.Handle);
                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, v.Seller.Handle,
                                      v.SmokeScenario, 0, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Once("vendor-smoke-" + v.Id, "Could not start the smoke: " + ex.Message);
                    }

                    v.Doing = Duty.Smoking;
                    v.PhaseUntil = ms + (int)(Math.Max(2f, v.SmokeSeconds) * 1000f);
                    return;

                case Duty.Smoking:
                    if (ms < v.PhaseUntil) return;

                    Walk(v, v.PostAt);
                    v.Doing = Duty.WalkingBack;
                    v.PhaseUntil = ms + 12000;
                    return;

                case Duty.WalkingBack:
                    if (ms < v.PhaseUntil && v.Seller.Position.DistanceTo(v.PostAt) > 1.0f) return;

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, v.Seller.Handle);
                        v.Seller.Heading = v.Heading + 180f;
                    }
                    catch
                    {
                        // He faces wherever he ended up; the work loop still plays.
                    }

                    v.Doing = Duty.Working;

                    // False so Work() puts him back on the grill on the next sweep.
                    v.AnimStarted = false;

                    v.NextSmoke = now + GameClockDuration.FromMinutes((long)(v.SmokeEveryHours * 60f));
                    return;
            }
        }

        private static void Walk(Vendor v, Vector3 to)
        {
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, v.Seller.Handle);

                // Speed 1.0 is a walk. Jogging to a cigarette and jogging back would be funny
                // exactly once.
                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, v.Seller.Handle,
                              to.X, to.Y, to.Z, 1.0f, 10000, v.Heading, 0.2f);
            }
            catch (Exception ex)
            {
                Log.Once("vendor-walk-" + v.Id, "Could not send him for a smoke: " + ex.Message);
                v.Doing = Duty.Working;
            }
        }

        /// <summary>
        /// Drops the stand and the vendor onto the actual ground, once.
        ///
        /// NOT AT SPAWN, and that is the point. A coordinate read off a coord HUD is the
        /// PLAYER's z -- their feet, on whatever they were standing on -- and the ground a
        /// metre behind the stand is rarely the same height. Worse, at the instant an entity
        /// is created the collision around it may not be streamed in yet, so
        /// PLACE_OBJECT_ON_GROUND_PROPERLY has nothing to place it against and leaves it
        /// hanging. That is the stand hovering and the chef falling a metre.
        ///
        /// So it is done on a later sweep, by which time the map is there, and only once it
        /// gets a sensible answer.
        /// </summary>
        private static void Settle(Vendor v)
        {
            if (v.Settled) return;

            try
            {
                var done = true;

                if (v.Stand != null && v.Stand.Exists())
                {
                    done &= Ground(v.Stand, true);
                }

                if (v.Seller != null && v.Seller.Exists())
                {
                    done &= Ground(v.Seller, false);
                }

                if (done) v.Settled = true;
            }
            catch (Exception ex)
            {
                Log.Once("vendor-settle-" + v.Id, "Could not settle " + v.Name + ": " + ex.Message);
                v.Settled = true;
            }
        }

        /// <summary>Puts one entity on the ground beneath it. False if the ground is not known yet.</summary>
        private static bool Ground(Entity what, bool frozen)
        {
            var at = what.Position;

            // OutputArgument, not a pointer. GET_GROUND_Z_FOR_3D_COORD writes its answer
            // through a float*, and taking &z in C# would need the whole assembly compiled
            // unsafe; SHVDN provides this wrapper for exactly that case.
            var slot = new OutputArgument();

            // Probed from a metre ABOVE where it currently is: starting at or below the
            // surface can find the floor of whatever is underneath instead.
            var found = Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD,
                                            at.X, at.Y, at.Z + 1.0f, slot, false);

            if (!found) return false;

            var z = slot.GetResult<float>();
            if (z <= 0f) return false;

            // Unfrozen to move it, then frozen again. A frozen entity ignores a position set
            // in some cases, and the whole reason it is frozen is so nothing can shove it.
            if (frozen) what.IsPositionFrozen = false;

            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, what.Handle, at.X, at.Y, z, false, false, false);

            if (frozen) what.IsPositionFrozen = true;

            return true;
        }

        /// <summary>
        /// Starts the vendor's looping work animation, once it has streamed.
        ///
        /// REQUESTED WITHOUT WAITING, and retried on the next sweep. Spinning on a stream
        /// inside a tick is an infinite loop, not a wait -- Game.GameTime only advances when
        /// the game renders a frame, and this runs inside one. That mistake froze the game
        /// once already; it is not being made again for an idle animation.
        /// </summary>
        private static void Work(Vendor v)
        {
            if (v.AnimStarted) return;
            if (string.IsNullOrEmpty(v.AnimDict) || string.IsNullOrEmpty(v.AnimClip)) return;
            if (v.Seller == null || !v.Seller.Exists()) return;

            try
            {
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, v.AnimDict))
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, v.AnimDict);
                    return;
                }

                // The scenario has to go first, or it keeps re-asserting its own idle over
                // the top of this one and the vendor twitches between the two.
                Function.Call(Hash.CLEAR_PED_TASKS, v.Seller.Handle);

                // Flag 1 = LOOPING. Not upper-body and not secondary: this IS what he is
                // doing, unlike the player's eating clip which has to sit over walking.
                Function.Call(Hash.TASK_PLAY_ANIM, v.Seller.Handle, v.AnimDict, v.AnimClip,
                              4f, -4f, -1, 1, 0f, false, false, false);

                v.AnimStarted = true;
                Log.Debug(v.Name + ": working animation " + v.AnimDict + " / " + v.AnimClip + ".");
            }
            catch (Exception ex)
            {
                Log.Once("vendor-work-" + v.Id,
                         v.Name + ": could not play " + v.AnimDict + " - " + ex.Message +
                         ". Falling back to the scenario.");

                v.AnimDict = "";

                try
                {
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, v.Seller.Handle, v.Scenario, 0, true);
                }
                catch
                {
                    // He will simply stand there.
                }
            }
        }

        private static void Despawn(Vendor v)
        {
            try
            {
                if (v.Stand != null && v.Stand.Exists()) v.Stand.Delete();
                if (v.Seller != null && v.Seller.Exists()) v.Seller.Delete();
            }
            catch (Exception ex)
            {
                Log.Once("vendor-despawn-" + v.Id, "Could not remove " + v.Name + ": " + ex.Message);
            }

            v.Stand = null;
            v.Seller = null;
            v.AnimStarted = false;
            v.Settled = false;
            v.Doing = Duty.Working;
            v.SmokeScheduled = false;
        }

        // ======================================================================
        // Buying
        // ======================================================================

        /// <summary>The prompt, and the purchase. No menu, by design.</summary>
        private void Offer(Ped me)
        {
            _at = null;

            if (_eating.Busy) return;

            var driving = me.IsInVehicle();

            var here = Nearest(me.Position, driving);
            if (here == null) return;

            // A drive-through you can use at speed is a drive-BY. The car has to have
            // essentially stopped at the window, which is what the real thing asks of you.
            if (driving && !Stopped(me)) return;

            var item = Find(here.ItemId);
            if (item == null) return;

            _at = here;

            // Ask for the prop and the animation NOW, while the prompt is up. By the time
            // the key is pressed they are resident, so nothing has to be waited for -- which
            // is what makes the non-blocking loader above good enough.
            _eating.Preload(item);

            var money = Money();
            var price = here.PriceOf(item);
            var afford = money >= price;

            // What it is CALLED here, which is not always what the item is called. The
            // catalogue's taco is a branded thing from a shop shelf; at a street window it is
            // just a taco, and the prompt should say so.
            var what = string.IsNullOrEmpty(here.Label)
                ? item.Name.ToLowerInvariant()
                : here.Label.ToLowerInvariant();

            // The game's own help box, top left. That is where a player already looks for an
            // instruction, and it is the only place where ~INPUT_CONTEXT~ resolves to the
            // button they have actually got bound -- so it reads E on a keyboard and the
            // right glyph on a pad, without this code knowing which they are using.
            if (here.UseMenu)
            {
                Hud.Help("Press ~INPUT_CONTEXT~ to shop at ~b~" + here.Name + "~s~.");
            }
            else
            {
                Hud.Help(afford
                    ? "Press ~INPUT_CONTEXT~ to buy a " + what + ".  ~c~$" + price
                    : "~r~You cannot afford a " + what + ".~s~  ~c~$" + price);
            }

            // THE HORN. In a vehicle the interact key is also the horn, so ordering at a
            // drive-through would blare at the window every time -- and holding the key would
            // hold the horn down. Suppressed only while an offer is actually on screen, so
            // the horn works normally everywhere else, including parked one car length away.
            if (driving)
            {
                try { Game.DisableControlThisFrame(GTA.Control.VehicleHorn); }
                catch { /* nothing to do about it */ }
            }

            if (!Pressed()) return;

            Hud.ClearHelp();


            if (here.UseMenu) { OpenShelf(here); return; }

            if (afford) Buy(here, item);
        }

        // ======================================================================
        // The shelf
        // ======================================================================

        /// <summary>Opens a shop's shelf: every item it stocks, in one list.</summary>
        private void OpenShelf(Vendor v)
        {
            _shopping = v;

            _ui.Title = v.Name.ToUpperInvariant();
            _ui.Tabs.Clear();
            _ui.Open();

            Refill();
        }

        /// <summary>
        /// Rebuilds the shelf.
        ///
        /// EVERYTHING THE VENDOR STOCKS, not just what the hour allows. The hours pick what a
        /// one-key stall is selling today; a shop with a menu shows its shelf, and a bagel you
        /// can see but not buy before eleven would just be a row that does nothing.
        /// </summary>
        private void Refill()
        {
            _ui.Rows.Clear();

            if (_shopping == null) return;

            var money = Money();

            var hour = 12;
            try { hour = GameClock.Hour; }
            catch { /* the shelf is worth more than the hours */ }

            foreach (var offer in _shopping.Offers)
            {
                var item = Find(offer.Id);
                if (item == null) continue;

                // OUT-OF-HOURS ITEMS ARE SHOWN AND GREYED, not dropped. A lunch menu that
                // simply is not in the list at midnight teaches the player nothing; one that
                // is there saying "11am to 3pm" tells them to come back.
                var closed = offer.AtHour(hour) ? null : "Only " + Window(offer) + ".";

                _ui.Rows.Add(UI.Stock.RowFor(item, money, _shopping.PriceOf(item), closed));
            }
        }

        /// <summary>Runs the shelf while it is up.</summary>
        private void Shelf(Ped me)
        {
            _ui.Update();

            if (_ui.JustClosed) { _shopping = null; return; }

            // Walking away shuts it, or you could buy from a shelf across the street.
            if (_shopping == null || !InReach(me, _shopping)) { Close(); return; }

            _ui.Subtitle = UI.Stock.Header(Money(), _needs.Hunger.Value,
                                           _needs.Sleep.Value, _needs.Drunk);

            if (_ui.Rows.Count > 0)
            {
                var at = _ui.Index;
                if (at >= 0 && at < _ui.Rows.Count) _eating.Preload(_ui.Rows[at].Tag as Item);
            }

            if (_ui.Activated != null)
            {
                var item = _ui.Activated.Tag as Item;

                if (item != null && !_eating.Busy)
                {
                    Buy(_shopping, item);
                    Close();
                    return;
                }
            }

            _ui.Draw();
        }

        /// <summary>An offer's hours, as somebody would say them out loud.</summary>
        private static string Window(Offer offer)
        {
            return "from " + Oclock(offer.From) + " to " + Oclock(offer.To);
        }

        private static string Oclock(int hour)
        {
            var h = ((hour % 24) + 24) % 24;

            if (h == 0) return "midnight";
            if (h == 12) return "midday";

            return (h % 12) + (h < 12 ? "am" : "pm");
        }

        private bool InReach(Ped me, Vendor v)
        {
            try
            {
                if (!v.InRange || !v.AtPost) return false;

                var at = v.Stand != null && v.Stand.Exists() ? v.Stand.Position : v.Position;
                return at.DistanceTo(me.Position) <= v.Reach + 0.6f;
            }
            catch
            {
                return false;
            }
        }

        private void Close()
        {
            if (_ui.IsOpen) _ui.Close();
            _shopping = null;
        }

        private Vendor Nearest(Vector3 from, bool driving)
        {
            Vendor best = null;
            var bestD = float.MaxValue;

            foreach (var v in _vendors)
            {
                if (!v.InRange) continue;

                // On foot you may use any of them, including the drive-through window.
                // From a car, only the ones that say so.
                if (driving && !v.FromVehicle) continue;

                // Nobody behind the stand, nobody to serve you. He is a few metres away
                // with a cigarette and will be back.
                if (!v.AtPost) continue;

                // Measured to the STAND rather than to the written coordinate, because the
                // stand is what the player can see and it has been dropped onto the ground,
                // which may be a little away from the z in the file.
                var at = v.Stand != null && v.Stand.Exists() ? v.Stand.Position : v.Position;

                var d = at.DistanceTo(from);
                if (d > v.Reach || d >= bestD) continue;

                bestD = d;
                best = v;
            }

            return best;
        }

        private Item Find(string id)
        {
            foreach (var item in _menu.Items)
            {
                if (string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) return item;
            }

            Log.Once("vendor-item-" + id,
                     "No item called '" + id + "' in foods.json - that stand cannot sell anything.");
            return null;
        }

        private void Buy(Vendor v, Item item)
        {
            var price = v.PriceOf(item);

            // Re-checked here rather than trusted from the prompt: money can fall between the
            // frame that drew the offer and the frame the key was pressed.
            if (Money() < price)
            {
                Notify("~r~Not enough money.");
                return;
            }

            try
            {
                Game.Player.Money = Math.Max(0, Game.Player.Money - price);
            }
            catch (Exception ex)
            {
                Log.Once("vendor-charge", "Could not take payment: " + ex.Message);
                return;
            }

            // The vendor looks up at whoever just bought something. Two seconds, then their
            // scenario has them back.
            try
            {
                if (v.Seller != null && v.Seller.Exists())
                {
                    v.Seller.Task.LookAt(Game.Player.Character, 2000);
                }
            }
            catch
            {
                // Cosmetic.
            }

            _socials.About(v.Id, Social.Chirp.Bought, item.Name, v.Name);

            if (_eating.Begin(item)) return;

            // Refunded rather than swallowed. Taking the money and producing nothing is the
            // one failure a shop of any kind must never have.
            try { Game.Player.Money += price; }
            catch (Exception ex) { Log.Error("Could not refund " + price, ex); }

            Notify("~r~Could not eat that - refunded.");
        }

        /// <summary>
        /// Whether the car has actually stopped rolling.
        ///
        /// A speed test rather than IS_VEHICLE_STOPPED, which is strict enough that creeping
        /// forward at walking pace fails it -- and creeping forward is exactly what somebody
        /// does at a drive-through window. Half a metre a second is a car that has arrived.
        /// </summary>
        private static bool Stopped(Ped me)
        {
            try
            {
                var v = me.CurrentVehicle;
                if (v == null || !v.Exists()) return false;

                return v.Speed <= 0.5f;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Keeps an ini or json number inside the range that makes sense.</summary>
        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }

        private static int Money()
        {
            try { return Game.Player.Money; }
            catch { return 0; }
        }

        private static void Notify(string message)
        {
            try { GTA.UI.Notification.PostTicker(message, false, false); }
            catch { /* nothing to do about it */ }
        }

        /// <summary>
        /// The interact, by the game's context control OR the configured key.
        ///
        /// The key half is edge-detected by hand because Game.IsKeyPressed is a LEVEL: held
        /// for a fifth of a second it is true across a dozen frames, which would buy a dozen
        /// hot dogs.
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

        /// <summary>
        /// Removes every stand and vendor.
        ///
        /// Runs on a reload as well as on shutdown. Without it, every script reload leaves
        /// another chef standing in the street behind another stand, and they accumulate.
        /// </summary>
        public void Shutdown()
        {
            Close();

            foreach (var v in _vendors)
            {
                Despawn(v);

                // The blips too. They are not streamed, so nothing else would ever remove
                // them -- and a reload would then add a second one on top of the first.
                try
                {
                    if (v.Marker != null && v.Marker.Exists()) v.Marker.Delete();
                }
                catch (Exception ex)
                {
                    Log.Once("vendor-blip-del-" + v.Id, "Could not remove a blip: " + ex.Message);
                }

                v.Marker = null;
                v.InRange = false;
            }

            _at = null;
        }
    }
}
