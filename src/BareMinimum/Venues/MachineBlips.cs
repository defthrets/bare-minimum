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

                _blips[prop.Handle] = Make(prop.Position);
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

        private Blip Make(Vector3 at)
        {
            var blip = World.CreateBlip(at);

            try
            {
                blip.Sprite = (BlipSprite)52;
                blip.Color = (BlipColor)5;
                blip.Scale = 0.7f;

                // SHORT RANGE, ALWAYS. A machine is somewhere you notice because you are
                // walking past it, not somewhere you plan a journey to -- and thirty of them
                // on the pause map is the clutter the shops are already grouped to avoid.
                blip.IsShortRange = true;

                Function(blip, _cfg.GroupShopBlips ? _cfg.ShopBlipGroupName : "Vending Machine");

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
            if (_cfg.VendingMachines) names.AddRange(Counters.MachineModels);
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
