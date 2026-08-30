using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>One shop chain, and how to tell you are standing in one.</summary>
    internal sealed class Brand
    {
        public string Id = "";
        public string Name = "";
        public string Logo = "";

        /// <summary>
        /// Interior room keys this chain's shops use. THE GOOD SIGNAL.
        ///
        /// Every LTD in the game is the same interior asset, so one key here covers all of
        /// them at once -- which a list of coordinates can never do, because it only ever
        /// covers the ones somebody has stood in and written down.
        /// </summary>
        public readonly List<int> RoomKeys = new List<int>();

        /// <summary>Positions known to be inside one, as a fallback until a key is known.</summary>
        public readonly List<Vector3> At = new List<Vector3>();

        public float Radius = 30f;
    }

    /// <summary>
    /// Works out whose shop the till you are standing at belongs to.
    ///
    /// WHY THIS EXISTS: the counter is found by till prop, which is what makes it work in
    /// every shop in the game without a coordinate list. The cost of that generality is that
    /// the counter has no idea whose shop it is in, so its header said COUNTER everywhere.
    ///
    /// BY ROOM KEY FIRST. GET_KEY_FOR_ENTITY_IN_ROOM hashes the interior room the player is
    /// standing in, and every branch of a chain shares one interior asset -- so a single key
    /// brands every LTD in Los Santos. Positions are the fallback for a chain whose key
    /// nobody has captured yet.
    ///
    /// SELF-COMPLETING, like the model lists elsewhere in this mod. The key of whatever room
    /// you are in is written to the log the first time you use each counter. Reading it out
    /// of the log and adding it to brands.json turns a one-shop coordinate match into a
    /// whole-chain match, with no rebuild.
    /// </summary>
    internal sealed class Brands
    {
        private readonly List<Brand> _brands = new List<Brand>();
        private readonly HashSet<int> _logged = new HashSet<int>();

        public bool Any => _brands.Count > 0;

        public void Load()
        {
            _brands.Clear();

            try
            {
                var doc = JsonFile.Read(Paths.BrandsFile, out var how);

                if (doc == null)
                {
                    Log.Info("No " + Paths.BrandsFile + " - counters keep the plain header.");
                    return;
                }

                var list = doc["brands"];

                for (var i = 0; i < list.Count; i++)
                {
                    var node = list[i];

                    var brand = new Brand
                    {
                        Id = node["id"].AsString(""),
                        Name = node["name"].AsString(""),
                        Logo = node["logo"].AsString(""),
                        Radius = node["radius"].AsFloat(30f)
                    };

                    if (string.IsNullOrEmpty(brand.Logo)) continue;

                    var keys = node["roomKeys"];
                    for (var k = 0; k < keys.Count; k++)
                    {
                        var key = keys[k].AsInt(0);
                        if (key != 0) brand.RoomKeys.Add(key);
                    }

                    var at = node["at"];
                    for (var a = 0; a < at.Count; a++)
                    {
                        brand.At.Add(new Vector3(at[a]["x"].AsFloat(0f),
                                                 at[a]["y"].AsFloat(0f),
                                                 at[a]["z"].AsFloat(0f)));
                    }

                    _brands.Add(brand);
                }

                Log.Info("Brands: " + _brands.Count + " loaded from " + how + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Could not read " + Paths.BrandsFile, ex);
            }
        }

        /// <summary>
        /// Whose shop this is, or null.
        ///
        /// The room key is ALSO logged here, once per distinct room, whether or not it
        /// matched. That log line is the entire mechanism for adding the next chain.
        /// </summary>
        public Brand At(Ped me)
        {
            if (_brands.Count == 0 || me == null || !me.Exists()) return null;

            var key = RoomKey(me);
            Note(key);

            if (key != 0)
            {
                foreach (var brand in _brands)
                {
                    if (brand.RoomKeys.Contains(key)) return brand;
                }
            }

            var here = me.Position;

            foreach (var brand in _brands)
            {
                foreach (var spot in brand.At)
                {
                    if (spot.DistanceTo(here) <= brand.Radius) return brand;
                }
            }

            return null;
        }

        private static int RoomKey(Ped me)
        {
            try
            {
                return Function.Call<int>(Hash.GET_KEY_FOR_ENTITY_IN_ROOM, me.Handle);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Writes each new room key to the log, once, so it can be adopted.</summary>
        private void Note(int key)
        {
            if (key == 0 || !_logged.Add(key)) return;

            // Bounded, because a player who spends an hour walking through interiors should
            // not end up with a thousand lines of this.
            if (_logged.Count > 40) return;

            Log.Info("Counter room key: " + key +
                     "  (put this in data/brands.json under roomKeys to brand this chain)");
        }
    }
}
