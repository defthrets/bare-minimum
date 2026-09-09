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
    /// KEYED BY POSITION, WHICH IT WAS NOT. It used to key on the prop's HANDLE and drop the
    /// marker when the handle went -- which sounds tidy and is exactly wrong: the handle is the
    /// game's memory of a streamed object, not the machine. Drive out of range and the prop
    /// unloads and the marker dies; drive back and the same machine returns with a different
    /// number. So the map only ever showed what was already loaded around the player, which is
    /// the one place nobody needs a map to find a drink.
    ///
    /// A machine does not move. That is the fact worth keying on, and the one this now does:
    /// found once, written down, marked for good and marked again next session out of
    /// machines.json. The map fills in as you go, and stays filled in. See
    /// Settings.RememberMachines, and Forget for the way back.
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

        /// <summary>
        /// One machine, as remembered. The position IS the identity -- see the class note.
        ///
        /// A metre and a half apart counts as the same one, which is wider than any wobble in
        /// a streamed prop's reported position and far narrower than the gap between two
        /// machines: they stand in banks of two and three against a wall, and those are two
        /// metres apart at the closest.
        /// </summary>
        private sealed class Known
        {
            public Vector3 At;
            public bool Stall;
            public Blip Mark;
        }

        private const float Same = 1.5f;

        private readonly Settings _cfg;
        private readonly List<Known> _known = new List<Known>();

        /// <summary>Whether the save on disk is safe to write over. See Core.SaveGuard.</summary>
        private readonly SaveGuard _guard = new SaveGuard("Machines");

        private Model[] _models;
        private int _nextScan;
        private bool _loaded;
        private bool _dirty;
        private int _saveAt;
        private bool _toldFull;

        public MachineBlips(Settings cfg)
        {
            _cfg = cfg;
        }

        public void Update()
        {
            int now;
            try { now = Game.GameTime; }
            catch { return; }

            // THE SAVE IS ON ITS OWN CLOCK, above the scan's early return, so a sweep that
            // finds nothing new still lets a pending write land.
            if (_dirty && now >= _saveAt) Save();

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

            try
            {
                Load();
                Scan();
            }
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

            // WITH REMEMBERING OFF, THE LIST IS THIS SWEEP AND NOTHING ELSE. Off has to mean
            // what it meant before the file existed -- only what is loaded, only while it is --
            // and without this it would have meant something new and worse: kept for the
            // session, forgotten at the door. A setting that half does the thing it is named
            // after is harder to explain than either of the two honest answers.
            var seen = _cfg.RememberMachines ? null : new List<Known>();

            foreach (var prop in found)
            {
                if (prop == null || !prop.Exists()) continue;

                var at = prop.Position;

                var had = Find(at);
                if (had != null) { if (seen != null) seen.Add(had); continue; }

                if (_known.Count >= Math.Max(10, _cfg.RememberMachinesMax))
                {
                    if (!_toldFull)
                    {
                        _toldFull = true;
                        Log.Warn("Machine blips: " + _known.Count + " remembered, which is the " +
                                 "cap in [Map] RememberMachinesMax. Nothing further will be " +
                                 "added to the map this session.");
                    }

                    break;
                }

                var model = StallModel(prop);

                var known = new Known { At = at, Stall = model != null };
                _known.Add(known);

                if (seen != null) seen.Add(known);

                // WRITTEN DOWN WHILE YOU DRIVE PAST. A stall has no line in vendors.json -- it
                // is found by model, like the hot dog carts -- so the same harvest that turns
                // the carts into listed entries takes these too, and one drive around the map
                // is the whole list of both. See Vendors.Note.
                //
                // ONCE PER STALL NOW, WHICH IT WAS NOT. This sat inside Make, and Make ran
                // every time a stall streamed back in -- so a lap of Blaine County wrote the
                // same stall into the harvest four or five times.
                if (known.Stall) Vendors.Note(model, "fruit", "Fruit Stall", "fruit", at, prop.Heading);

                Mark(known);

                Touch();
            }

            if (seen == null) return;

            for (var i = _known.Count - 1; i >= 0; i--)
            {
                if (seen.Contains(_known[i])) continue;

                Kill(_known[i].Mark);
                _known.RemoveAt(i);
            }
        }

        /// <summary>The remembered machine at a point, or null. See Known for what "at" means.</summary>
        private Known Find(Vector3 at)
        {
            for (var i = 0; i < _known.Count; i++)
            {
                if (_known[i].At.DistanceTo(at) <= Same) return _known[i];
            }

            return null;
        }

        /// <summary>Gives a remembered machine its marker, if it has not got one.</summary>
        private void Mark(Known known)
        {
            if (known.Mark != null && known.Mark.Exists()) return;

            known.Mark = Make(known.At, known.Stall);
        }

        /// <summary>Whether this blip handle is one of the machine markers, and where it stands.</summary>
        public bool Owns(int blipHandle, out Vector3 at)
        {
            at = Vector3.Zero;
            if (blipHandle == 0) return false;

            for (var i = 0; i < _known.Count; i++)
            {
                var blip = _known[i].Mark;
                if (blip == null) continue;

                try
                {
                    if (blip.Handle != blipHandle) continue;

                    at = _known[i].At;
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

        private Blip Make(Vector3 at, bool stall)
        {
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

            var blip = World.CreateBlip(at);

            try
            {
                blip.Sprite = (BlipSprite)sprite;
                blip.Color = (BlipColor)colour;
                blip.Scale = 0.7f;

                // SHORT RANGE, ALWAYS, AND MORE SO NOW THAT THEY ARE KEPT. A short-range blip
                // is drawn on the minimap when you are near it and on the pause map always,
                // which is exactly the arrangement wanted here: the machine you are walking
                // past is on the minimap, and the several hundred you have walked past over a
                // playthrough are on the pause map without being on the minimap all at once.
                blip.IsShortRange = true;

                Function(blip, label);

                // For the pause map's hover hook, so the card can name it. See MapCard. Set at
                // creation only -- and creation now happens ONCE per machine ever, so unlike
                // every other note in this file that says "the next time they are rebuilt",
                // this setting reaches an existing marker on a reload and not before.
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

        /// <summary>
        /// Takes every marker off the map. What is REMEMBERED is untouched -- see Forget for
        /// that -- so switching the markers off and on again puts them all straight back
        /// without another lap of the map.
        /// </summary>
        public void Clear()
        {
            for (var i = 0; i < _known.Count; i++)
            {
                Kill(_known[i].Mark);
                _known[i].Mark = null;
            }
        }

        /// <summary>
        /// Forgets the lot: the markers, the list and the file.
        ///
        /// FOR THE PLAYER WHO WANTS THE MAP BACK. Several hundred markers accumulated over a
        /// playthrough is the intended end state and it is also a lot of map, and somebody who
        /// decides they have had enough of it should not have to find and delete a file. It is
        /// the "Forget the vending machines" row on the F7 page, which shows the count beside
        /// it so the number is visible before anybody throws it away.
        /// </summary>
        public void Forget()
        {
            Clear();
            _known.Clear();
            _toldFull = false;
            _dirty = true;

            Save();
        }

        /// <summary>How many machines are on the map, for the menu row that offers to forget them.</summary>
        public int Count => _known.Count;

        // ======================================================================
        // Disk
        // ======================================================================

        /// <summary>
        /// The remembered machines, read once, and their markers put back before he has been
        /// anywhere near them.
        ///
        /// AT LOAD, NOT AS THEY STREAM IN. That is the whole difference this file is about: a
        /// marker that waited for its prop would be a marker you can only see once you are
        /// close enough not to need it.
        /// </summary>
        private void Load()
        {
            if (_loaded) return;
            _loaded = true;

            if (!_cfg.RememberMachines) return;

            try
            {
                var doc = _guard.Read(Paths.MachinesFile);
                if (doc == null || doc.IsNull) return;

                var list = doc["machines"];

                foreach (var node in list.Items)
                {
                    var at = new Vector3(node["x"].AsFloat(), node["y"].AsFloat(), node["z"].AsFloat());

                    // A row with no position is a row from a half-written file, and a marker at
                    // the origin of the map is worse than a missing one.
                    if (at.X == 0f && at.Y == 0f) continue;
                    if (Find(at) != null) continue;

                    var known = new Known { At = at, Stall = node["stall"].AsBool(false) };
                    _known.Add(known);

                    Mark(known);
                }

                Log.Info("Machine blips: " + _known.Count + " remembered from a previous session.");
            }
            catch (Exception ex)
            {
                Log.Once("machines-load", "Could not read " + Paths.MachinesFile + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Marks the list as changed, and puts the write a few seconds out.
        ///
        /// NOT WRITTEN PER MACHINE. Driving into a new district finds a dozen in one sweep and
        /// a file rewritten a dozen times in a frame is a stutter for nothing; the sweep only
        /// runs every two seconds anyway, so a five-second delay collects a whole district into
        /// one write.
        /// </summary>
        private void Touch()
        {
            if (!_cfg.RememberMachines) return;

            _dirty = true;

            try { _saveAt = Game.GameTime + 5000; }
            catch { _saveAt = 0; }
        }

        private void Save()
        {
            _dirty = false;

            if (!_cfg.RememberMachines) return;

            // A save that could not be READ is not written over. Core.SaveGuard says why.
            if (!_guard.MayWrite) return;

            try
            {
                var list = Json.Array();

                foreach (var known in _known)
                {
                    list.Add(Json.Object()
                        .Set("x", Math.Round(known.At.X, 2))
                        .Set("y", Math.Round(known.At.Y, 2))
                        .Set("z", Math.Round(known.At.Z, 2))
                        .Set("stall", known.Stall));
                }

                var doc = Json.Object()
                    .Set("_comment", "Every vending machine and produce stall you have been " +
                                     "near. Written by BareMinimum so the map keeps them " +
                                     "between sessions; delete it to start the map over.")
                    .Set("version", 1)
                    .Set("machines", list);

                if (!JsonFile.Write(Paths.MachinesFile, doc))
                {
                    Log.Once("machines-save", "Could not write " + Paths.MachinesFile +
                                              " - the machines found this session will not " +
                                              "survive it.");
                }
            }
            catch (Exception ex)
            {
                Log.Once("machines-save", "Could not save the machines: " + ex.Message);
            }
        }

        /// <summary>Anything still owed to disk, written now. For the way out of the script.</summary>
        public void Shutdown()
        {
            if (_dirty) Save();
        }
    }
}
