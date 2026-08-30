using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>What kind of place is offering to sell you something.</summary>
    internal enum Counter
    {
        None,

        /// <summary>A shop till: the full menu.</summary>
        Till,

        /// <summary>
        /// A vending machine. NOT USED -- machines are left vanilla, deliberately.
        ///
        /// Kept in the enum rather than deleted so the shape of Nearest() still reads as "one
        /// of several kinds of counter", and so that turning them back on is uncommenting a
        /// block rather than reconstructing a concept.
        /// </summary>
        Machine
    }

    /// <summary>
    /// Finds the till you are standing at, or the vending machine.
    ///
    /// BY PROP, for the third time in this codebase and for the same reason each time:
    /// GET_CLOSEST_OBJECT_OF_TYPE sees MAP objects, which is what a till and a vending machine
    /// both are. A coordinate list would need every 24/7, LTD and Rob's Liquor in the game
    /// written down and would still be wrong for anybody running a map mod.
    ///
    /// IT ALSO ANSWERS THE "MORE PLACES TO EAT" PROBLEM ALMOST FOR FREE. Vending machines are
    /// scattered across the whole map -- outside stations, in police stations, in hospitals,
    /// on the pier -- and finding them by model means every one of them works without anybody
    /// listing a single position.
    ///
    /// Both model lists are validated with IS_MODEL_VALID at start-up and the misses are named
    /// in the log, so a wrong guess costs a log line rather than a counter that never opens.
    /// </summary>
    internal sealed class Counters
    {
        /// <summary>
        /// Shop tills. The register is the right thing to look for rather than the shopkeeper:
        /// a ped wanders, gets shot and despawns, and the till is bolted to the counter.
        /// </summary>
        private static readonly string[] TillModels =
        {
            "prop_till_01",
            "prop_till_02",
            "prop_till_03",
            "v_ret_gc_till",
            "v_ret_ta_till",
            "v_ret_247_till",
            "prop_cash_reg_01",
            "prop_cashregister_01"
        };

        // VENDING MACHINES ARE LEFT VANILLA and the model list has gone with them, rather
        // than sitting here unused: the game already sells a drink out of one and plays its
        // own animation, and a second purchase on top is two mods fighting over one machine.
        //
        // If they are ever wanted back, the models were: prop_vend_soda_01, prop_vend_soda_02,
        // prop_vend_water_01, prop_vend_coffe_01, prop_vend_snak_01, prop_vend_snak_01_tu,
        // prop_vend_fridge01 -- all seven of which exist in this build.

        private int[] _tills;

        private int _nextScan;
        private Counter _kind = Counter.None;
        private Prop _found;

        /// <summary>How close you have to stand. A till is behind a counter, so it is generous.</summary>
        public float TillReach = 1.9f;

        /// <summary>The prop currently being offered, or null.</summary>
        public Prop Found => _found;

        // ======================================================================

        private int[] Resolve(string[] names, string what, ref int[] cache)
        {
            if (cache != null) return cache;

            var good = new List<int>();
            var missing = new List<string>();

            foreach (var name in names)
            {
                try
                {
                    var model = new Model(name);

                    if (Function.Call<bool>(Hash.IS_MODEL_VALID, model.Hash)) good.Add(model.Hash);
                    else missing.Add(name);
                }
                catch
                {
                    missing.Add(name);
                }
            }

            cache = good.ToArray();

            Log.Info(what + ": " + cache.Length + " of " + names.Length +
                     " model(s) exist in this build.");

            if (missing.Count > 0)
            {
                Log.Info(what + ": not in this build, ignored - " + string.Join(", ", missing.ToArray()));
            }

            return cache;
        }

        /// <summary>
        /// What is within reach, if anything. Re-scanned a few times a second.
        ///
        /// TILLS WIN OVER MACHINES when both are in range, which happens constantly -- petrol
        /// stations have a drinks machine a couple of metres from the counter. The till sells
        /// strictly more, so preferring it can never leave the player unable to buy something
        /// the machine would have had.
        /// </summary>
        public Counter Nearest(Vector3 from)
        {
            var now = Game.GameTime;

            if (now < _nextScan && _found != null && _found.Exists())
            {
                if (_found.Position.DistanceTo(from) <= TillReach) return _kind;
            }

            _nextScan = now + 200;
            _found = null;
            _kind = Counter.None;

            try
            {
                var till = Closest(from, TillReach, Resolve(TillModels, "Tills", ref _tills));
                if (till != null)
                {
                    _found = till;
                    _kind = Counter.Till;
                    return _kind;
                }

                // VENDING MACHINES ARE LEFT ALONE. The game already sells you a drink out of
                // one and plays its own animation for it, and putting a second, different
                // purchase on top of that is two mods fighting over the same machine -- ours
                // silently doing nothing about the health the vanilla one restores.
                //
                // The model list below is kept because it costs nothing and the next person
                // to want them will look for exactly this comment.
            }
            catch (Exception ex)
            {
                Log.Once("counters-scan", "Could not look for a counter: " + ex.Message);
            }

            return Counter.None;
        }

        private static Prop Closest(Vector3 from, float radius, int[] hashes)
        {
            Prop best = null;
            var bestD = float.MaxValue;

            foreach (var hash in hashes)
            {
                var handle = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,
                                                from.X, from.Y, from.Z, radius, hash,
                                                false, false, false);
                if (handle == 0) continue;

                // SHVDN 3.9 has no public pool-object constructors: new Prop(handle) does not
                // compile. Entity.FromHandle and a cast is the supported route.
                var prop = Entity.FromHandle(handle) as Prop;
                if (prop == null || !prop.Exists()) continue;

                var d = prop.Position.DistanceTo(from);
                if (d >= bestD) continue;

                bestD = d;
                best = prop;
            }

            return best;
        }
    }
}
