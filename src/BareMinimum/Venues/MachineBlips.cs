using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;

using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>
    /// Map markers for the things that have no coordinate: the snack machines and the
    /// roadside stalls.
    ///
    /// EVERY OTHER BLIP IN THIS MOD COMES OFF A LINE IN vendors.json, and these two cannot.
    /// They are found by MODEL, which is the whole reason every one of them in the world
    /// works -- but it also means nothing knows where they are until the game has streamed
    /// them in. So this looks for them the same way Counters does, just over a wider radius,
    /// and puts a marker on whatever it finds.
    ///
    /// KEYED BY PROP HANDLE, not by position. A machine is a map object and does not move,
    /// but it does stream out when you drive away and come back with a different handle, and
    /// a dictionary keyed on the handle drops the stale one for free.
    ///
    /// THEY WEAR THE SHOP SPRITE AND THE SHOP GROUP NAME on purpose. The pause map's legend
    /// is built from the distinct NAMES of the blips on it, so sharing the name is what keeps
    /// thirty machines from becoming thirty rows to scroll past -- exactly the reasoning
    /// behind GroupShopBlips for the shops themselves.
    /// </summary>
    internal sealed class MachineBlips
    {
        /// <summary>
        /// How often the world is asked. Four times slower than the counter scan: this one
        /// asks for EVERY matching prop in a couple of hundred metres rather than the nearest
        /// one in two, and nothing on the map changes fast enough to need it sooner.
        /// </summary>
        private const int ScanMs = 2000;

        private readonly Settings _cfg;
        private readonly Dictionary<int, Blip> _blips = new Dictionary<int, Blip>();

        private Model[] _models;
        private int _nextScan;

        public MachineBlips(Settings cfg)
        {
            _cfg = cfg;
        }

        public void Update()
        {
            int now;
            try { now = Game.GameTime; }
            catch { return; }

            if (now < _nextScan) return;
            _nextScan = now + ScanMs;

            // One switch for the markers, and the two that decide whether the machines and
            // the stalls sell at all. A machine you cannot buy from should not be on the map
            // telling you that you can.
            if (!_cfg.ShowShopBlips || !_cfg.MachineBlips ||
                (!_cfg.VendingMachines && !_cfg.FruitStalls))
            {
                Clear();
                return;
            }

            try { Scan(); }
            catch (Exception ex)
            {
                Log.Once("machine-blips", "Could not mark the machines: " + ex.Message);
            }
        }

        private void Scan()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) { Clear(); return; }

            var models = Models();
            if (models.Length == 0) return;

            var range = _cfg.ShopBlipRange <= 0f ? 400f : _cfg.ShopBlipRange;
            var found = World.GetNearbyProps(me.Position, range, models);

            var alive = new HashSet<int>();

            foreach (var prop in found)
            {
                if (prop == null || !prop.Exists()) continue;

                alive.Add(prop.Handle);

                Blip blip;
                if (_blips.TryGetValue(prop.Handle, out blip) && blip != null && blip.Exists())
                    continue;

                _blips[prop.Handle] = Make(prop);
            }

            // Anything that streamed out, or that walked past our radius, loses its marker.
            // Collected first because a dictionary cannot be edited while it is being read.
            var gone = new List<int>();
            foreach (var pair in _blips)
            {
                if (!alive.Contains(pair.Key)) gone.Add(pair.Key);
            }

            foreach (var handle in gone)
            {
                Kill(_blips[handle]);
                _blips.Remove(handle);
            }
        }

        /// <summary>Whether this blip handle is one of the machine markers, and where it stands.</summary>
        public bool Owns(int blipHandle, out Vector3 at)
        {
            at = Vector3.Zero;
            if (blipHandle == 0) return false;

            foreach (var pair in _blips)
            {
                var blip = pair.Value;
                if (blip == null) continue;

                try
                {
                    if (blip.Handle != blipHandle) continue;

                    at = blip.Position;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// The produce stall model this prop is, or null for a snack machine.
        ///
        /// The NAME rather than a yes: the harvest file is meant to be pasted into vendors.json
        /// and a line saying which model it was is the difference between an entry somebody can
        /// read and a hash nobody can look up.
        /// </summary>
        private static string StallModel(Prop prop)
        {
            try
            {
                var hash = prop.Model.Hash;

                foreach (var name in Counters.StallModels)
                {
                    if (new Model(name).Hash == hash) return name;
                }
            }
            catch { /* a model that will not answer is treated as a machine */ }

            return null;
        }

        private Blip Make(Prop prop)
        {
            var at = prop.Position;
            var model = StallModel(prop);
            var stall = model != null;

            // A ROW OF ITS OWN IN THE LEGEND. These two used to wear whichever name every shop
            // shared, which was fine while that was one name and wrong the moment the legend
            // was sorted by kind: a fruit stall filed under the fallback is the one roadside
            // thing you cannot find by looking for it. The groups are in vendors.json with the
            // rest, so all of this is described in one place.
            var key = stall ? "produce" : "machine";

            string label;
            int sprite, colour;

            if (!Vendors.GroupLook(key, out label, out sprite, out colour))
            {
                label = stall ? "Fruit Stall" : "Vending Machine";
                sprite = 52;
                colour = 5;
            }

            if (!_cfg.GroupShopBlips) label = stall ? "Fruit Stall" : "Vending Machine";

            // WRITTEN DOWN WHILE YOU DRIVE PAST. A stall has no line in vendors.json -- it is
            // found by model, like the hot dog carts -- so the same harvest that turns the
            // carts into listed entries takes these too, and one drive around the map is the
            // whole list of both. See Vendors.Note.
            if (stall) Vendors.Note(model, "fruit", "Fruit Stall", "fruit", at, prop.Heading);

            var blip = World.CreateBlip(at);

            try
            {
                blip.Sprite = (BlipSprite)sprite;
                blip.Color = (BlipColor)colour;
                blip.Scale = 0.7f;

                // SHORT RANGE, ALWAYS. A machine is somewhere you notice because you are
                // walking past it, not somewhere you plan a journey to -- and thirty of them
                // on the pause map is the clutter the shops are already grouped to avoid.
                blip.IsShortRange = true;

                Function(blip, label);

                // For the pause map's hover hook, so the card can name it. See MapCard. Set at
                // creation only: these markers are rebuilt as he moves, so a flipped setting
                // reaches them the next time they are.
                if (_cfg.ShopBlipHoverCard)
                {
                    GTA.Native.Function.Call(GTA.Native.Hash.SET_BLIP_AS_MISSION_CREATOR_BLIP, blip.Handle, true);
                }
            }
            catch (Exception ex)
            {
                Log.Once("machine-blip-style", "Could not style a machine blip: " + ex.Message);
            }

            return blip;
        }

        private static void Function(Blip blip, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            try { blip.Name = name; }
            catch { /* an unnamed blip is still a blip */ }
        }

        private static void Kill(Blip blip)
        {
            try { if (blip != null && blip.Exists()) blip.Delete(); }
            catch { /* nothing to do about it */ }
        }

        /// <summary>
        /// The models to look for, resolved once. Whichever of the two features is switched
        /// on contributes its own list, so turning the stalls off stops marking stalls.
        /// </summary>
        private Model[] Models()
        {
            if (_models != null) return _models;

            var names = new List<string>();

            if (_cfg.VendingMachines)
            {
                names.AddRange(Counters.MachineModels);
                names.AddRange(Counters.DrinkMachineModels);
            }

            if (_cfg.FruitStalls) names.AddRange(Counters.StallModels);

            var good = new List<Model>();
            foreach (var n in names)
            {
                try
                {
                    var m = new Model(n);
                    if (m.IsValid) good.Add(m);
                }
                catch { /* a name this build does not have is simply not looked for */ }
            }

            _models = good.ToArray();
            Log.Info("Machine blips: looking for " + _models.Length + " model(s).");
            return _models;
        }

        public void Clear()
        {
            foreach (var pair in _blips) Kill(pair.Value);
            _blips.Clear();
        }
    }
}
