using System;
using System.Collections.Generic;
using GTA;
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

        public string[] PropModels = new string[0];
        public string[] PedModels = new string[0];
        public string Scenario = "WORLD_HUMAN_STAND_IMPATIENT";

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

        /// <summary>Resolved once the candidate lists have been checked against this build.</summary>
        public Model? PropModel;
        public Model? PedModel;
        public bool Resolved;

        /// <summary>Whether the player is close enough for this to be live at all.</summary>
        public bool InRange;

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

        private readonly List<Vendor> _vendors = new List<Vendor>();

        /// <summary>The vendor currently within reach, if any.</summary>
        private Vendor _at;

        private bool _keyWasDown;
        private float _sinceScan;

        public Vendors(Core.Settings cfg, Catalogue menu, Eating eating)
        {
            _cfg = cfg;
            _menu = menu;
            _eating = eating;

            Load();
        }

        public int Count => _vendors.Count;

        /// <summary>True while a stand is offering, so nothing else reads the interact key.</summary>
        public bool Offering => _at != null;

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
                        ItemId = node["item"].AsString(""),
                        Position = new Vector3(node["x"].AsFloat(0f),
                                               node["y"].AsFloat(0f),
                                               node["z"].AsFloat(0f)),
                        Heading = node["heading"].AsFloat(0f),
                        PedBack = node["pedBack"].AsFloat(1.1f),
                        SpawnRange = node["spawnRange"].AsFloat(90f),
                        Reach = node["reach"].AsFloat(2.4f),
                        FromVehicle = node["fromVehicle"].AsBool(false),
                        Scenario = node["scenario"].AsString("WORLD_HUMAN_STAND_IMPATIENT"),
                        PropModels = Strings(node["prop"]),
                        PedModels = Strings(node["ped"]),
                        Label = node["label"].AsString(""),
                        Blip = node["blip"].AsBool(true),
                        BlipSprite = node["blipSprite"].AsInt(267),
                        BlipColour = node["blipColour"].AsInt(47)
                    };

                    if (string.IsNullOrEmpty(v.ItemId)) continue;

                    _vendors.Add(v);
                }

                Log.Info("Vendors: " + _vendors.Count + " street stand(s) loaded.");
            }
            catch (Exception ex)
            {
                Log.Error("Could not read " + Paths.VendorsFile + " - no street stands.", ex);
            }
        }

        private static string[] Strings(Json node)
        {
            var list = new List<string>();

            if (node != null && !node.IsNull)
            {
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
                if (suspended) { _at = null; return; }

                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead) { _at = null; return; }

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
                Marker(v);

                var near = v.Position.DistanceTo(from) <= v.SpawnRange;

                // IN RANGE IS SEPARATE FROM SPAWNED. A taco window has nothing to spawn -- the
                // hatch is already part of the building -- so it would never count as live if
                // being live meant holding an entity, and it could never be bought from.
                v.InRange = near;

                if (!v.HasEntities) continue;

                if (near && v.Stand == null && v.Seller == null) Spawn(v);
                else if (!near) Despawn(v);
                else Keep(v);
            }
        }

        /// <summary>
        /// The map marker, created once and left alone.
        ///
        /// Not streamed with the rest: the whole point of a blip is to be visible from across
        /// the map, so tying it to a ninety-metre spawn range would mean it only appeared once
        /// you had already found the place.
        /// </summary>
        private static void Marker(Vendor v)
        {
            if (!v.Blip) return;
            if (v.Marker != null && v.Marker.Exists()) return;

            try
            {
                var blip = World.CreateBlip(v.Position);
                if (blip == null || !blip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, blip.Handle, v.BlipSprite);
                Function.Call(Hash.SET_BLIP_COLOUR, blip.Handle, v.BlipColour);
                Function.Call(Hash.SET_BLIP_SCALE, blip.Handle, 0.75f);
                Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, blip.Handle, true);

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

                // A scenario rather than an idle animation: it comes with its own looping
                // behaviour, transitions and prop handling, and it survives the ped being
                // streamed around in a way a hand-played clip does not.
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, v.Scenario, 0, true);
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

        /// <summary>Puts back anything the game has quietly taken away.</summary>
        private static void Keep(Vendor v)
        {
            if (v.Stand != null && !v.Stand.Exists()) v.Stand = null;
            if (v.Seller != null && !v.Seller.Exists()) v.Seller = null;
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
            var afford = money >= item.Price;

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
            Hud.Help(afford
                ? "Press ~INPUT_CONTEXT~ to buy a " + what + ".  ~c~$" + item.Price
                : "~r~You cannot afford a " + what + ".~s~  ~c~$" + item.Price);

            // THE HORN. In a vehicle the interact key is also the horn, so ordering at a
            // drive-through would blare at the window every time -- and holding the key would
            // hold the horn down. Suppressed only while an offer is actually on screen, so
            // the horn works normally everywhere else, including parked one car length away.
            if (driving)
            {
                try { Game.DisableControlThisFrame(GTA.Control.VehicleHorn); }
                catch { /* nothing to do about it */ }
            }

            if (!afford || !Pressed()) return;

            Hud.ClearHelp();
            Buy(here, item);
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
            // Re-checked here rather than trusted from the prompt: money can fall between the
            // frame that drew the offer and the frame the key was pressed.
            if (Money() < item.Price)
            {
                Notify("~r~Not enough money.");
                return;
            }

            try
            {
                Game.Player.Money = Math.Max(0, Game.Player.Money - item.Price);
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

            if (_eating.Begin(item)) return;

            // Refunded rather than swallowed. Taking the money and producing nothing is the
            // one failure a shop of any kind must never have.
            try { Game.Player.Money += item.Price; }
            catch (Exception ex) { Log.Error("Could not refund " + item.Price, ex); }

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
