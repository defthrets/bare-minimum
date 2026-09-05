using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>
    /// Finds the fridge you are stood in front of.
    ///
    /// BY PROP, NOT BY COORDINATE -- the same call the beds and the tills make, for the same
    /// reasons. It was asked for at one fridge, the one in the kitchen on Forum Drive, and a
    /// single coordinate would have given exactly that one and nothing else: not Michael's
    /// kitchen, not Trevor's trailer, and nothing at all for anybody running an interior mod.
    /// Asking the game which fridge object is nearest costs the same amount of code and works
    /// everywhere the base game put one.
    ///
    /// GET_CLOSEST_OBJECT_OF_TYPE is what makes it possible, and it has to be that native:
    /// it sees MAP objects. World.GetNearbyProps walks the object pool, which placed map
    /// props are not in, so it finds nothing in a kitchen.
    ///
    /// EVERY FRIDGE A CHARACTER OWNS OPENS THE SAME STORE. See Larder. Not because that is
    /// tidy but because the alternative is worse: a per-fridge store means a player who has to
    /// remember which of four kitchens they left the chicken in, and a player who has to
    /// remember that stops using the feature by the second time they get it wrong.
    ///
    /// SHOP FRIDGES ARE DELIBERATELY NOT IN THE LIST. The retail chillers -- v_ret_ml_fridge,
    /// prop_vend_fridge01, the bar ones -- are all over the 24/7s and the liquor stores, which
    /// are exactly where the till counter already offers you a shelf. Two prompts fighting
    /// over one key in a shop is worse than not being able to raid the drinks chiller.
    /// </summary>
    internal sealed class Fridges
    {
        /// <summary>
        /// Candidate fridge models: kitchens people live in, and nothing behind a counter.
        ///
        /// Deliberately broad within that. A name this build does not know never matches
        /// anything, so listing one too many costs a line in the log; listing one too few
        /// costs a kitchen where the fridge does nothing and no clue as to why.
        /// </summary>
        private static readonly string[] Candidates =
        {
            // Safehouse kitchens
            "v_res_tt_fridge",          // Franklin's aunt's, Forum Drive
            "v_res_fridgemoda",
            "v_res_fridgemodsml",
            "v_res_tre_fridge",         // Trevor's trailer
            "v_ilev_mm_fridge_l",       // Michael's, the double door
            "v_ilev_mm_fridge_r",
            "v_ilev_mm_fridgeint",

            // Generic and interior kitchens
            "prop_fridge_01",
            "prop_fridge_03",
            "prop_cs_fridge",
            "prop_trailr_fridge",
            "v_61_ktn_mesh_fridge",
            "v_med_cor_minifridge"
        };

        private int[] _hashes;

        /// <summary>The scan is not free, so it runs on a clock rather than every frame.</summary>
        private int _nextScan;
        private Prop _found;

        /// <summary>
        /// Model hashes, resolved once and lazily.
        ///
        /// Lazily because a Model cannot be asked anything useful until the game is running,
        /// and a constructor runs during script construction -- before the world exists.
        /// </summary>
        private int[] Hashes()
        {
            if (_hashes != null) return _hashes;

            var good = new List<int>();
            var missing = new List<string>();

            foreach (var name in Candidates)
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

            _hashes = good.ToArray();

            Log.Info("Fridges: " + _hashes.Length + " of " + Candidates.Length +
                     " model(s) exist in this build.");

            if (missing.Count > 0)
            {
                // Named rather than counted, so a bad guess above can be corrected from
                // somebody else's log without them having to reproduce anything.
                Log.Info("Fridges: not in this build, ignored - " +
                         string.Join(", ", missing.ToArray()));
            }

            return _hashes;
        }

        /// <summary>The fridge within reach, or null. Re-scanned a few times a second.</summary>
        public Prop Nearest(Vector3 from, float radius)
        {
            var now = Game.GameTime;

            // One already held on to stays valid between scans, so the prompt does not
            // flicker while you shuffle about at the edge of the radius -- but it is STILL
            // range-checked, or the cache hands back a fridge already walked away from.
            if (now < _nextScan && _found != null && _found.Exists() &&
                _found.Position.DistanceTo(from) <= radius)
            {
                return _found;
            }

            _nextScan = now + 200;
            _found = null;

            try
            {
                var best = float.MaxValue;

                foreach (var hash in Hashes())
                {
                    var handle = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,
                                                    from.X, from.Y, from.Z, radius, hash,
                                                    false, false, false);
                    if (handle == 0) continue;

                    // SHVDN 3.9 has no public pool-object constructors: new Prop(handle) does
                    // not compile. Entity.FromHandle and a cast is the supported route.
                    var prop = Entity.FromHandle(handle) as Prop;
                    if (prop == null || !prop.Exists()) continue;

                    var d = prop.Position.DistanceTo(from);
                    if (d >= best) continue;

                    best = d;
                    _found = prop;
                }
            }
            catch (Exception ex)
            {
                Log.Once("fridges-scan", "Could not look for a fridge: " + ex.Message);
                _found = null;
            }

            return _found;
        }
    }
}
