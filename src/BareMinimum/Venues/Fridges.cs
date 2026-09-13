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
        /// <summary>
        /// The settings, for the one thing this class asks them: the player's own extra
        /// fridge model names. It had no constructor at all before, because until ExtraModels
        /// there was nothing here that could be configured.
        /// </summary>
        private readonly Settings _cfg;

        public Fridges(Settings cfg)
        {
            _cfg = cfg;
        }

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

            // THE SHIPPED LIST PLUS WHATEVER THE PLAYER ADDED. A fridge is found by model
            // name, so a kitchen this build has never heard of -- an interior mod's house, a
            // room added to the game after this was written -- simply has no fridge as far as
            // the mod is concerned, and until now there was nothing the player could do but
            // wait for me to add it. jerome74's is the one that prompted this.
            var wanted = new List<string>(Candidates);

            foreach (var extra in (_cfg.FridgeExtraModels ?? "").Split(',', ';'))
            {
                var name = extra.Trim();

                if (name.Length == 0) continue;
                if (wanted.Contains(name)) continue;

                wanted.Add(name);
            }

            // IS_MODEL_VALID IS THE WRONG QUESTION AND IT COST THE BEST FRIDGE IN THE GAME.
            //
            // It asks whether a model can be STREAMED IN AND SPAWNED, which is the right test
            // for something this mod is about to create. Nothing here is created: the fridge
            // is somebody else's object, already standing in a kitchen, and it is found by
            // hash with GET_CLOSEST_OBJECT_OF_TYPE -- a native that searches what is in the
            // world and does not care whether the model could also be spawned.
            //
            // A lot of interior fixtures are map-only and answer NO to that question while
            // sitting right there in front of you. v_res_tt_fridge is one, and it is the
            // fridge at his aunt's on Forum Drive -- so the one kitchen Franklin actually
            // lives in was struck off the list at startup, by name, with a line in the log
            // saying the game did not have it. It is in PropList.txt. It was always there.
            //
            // So every name is searched for. A name the game has never heard of hashes to
            // something no object in the world has, GET_CLOSEST_OBJECT_OF_TYPE returns
            // nothing, and that costs one native call per scan -- which is the whole price of
            // not second-guessing this.
            foreach (var name in wanted)
            {
                try
                {
                    var model = new Model(name);

                    good.Add(model.Hash);

                    // Still worth saying, because a typed-in name that answers no here is
                    // MUCH more likely to be a typo than a map-only fixture -- see the log
                    // line below, which says which it is rather than pretending to know.
                    if (!Function.Call<bool>(Hash.IS_MODEL_VALID, model.Hash)) missing.Add(name);
                }
                catch
                {
                    missing.Add(name);
                }
            }

            _hashes = good.ToArray();

            // THE NAMES THAT DID NOT RESOLVE ARE LISTED, not just counted. The whole point of
            // ExtraModels is that somebody types a model name into an ini, and a typo and a
            // model this game does not have look identical from the kitchen -- no fridge,
            // either way. Reading them back is the only way to tell which one it was.
            Log.Info("Fridges: looking for " + _hashes.Length + " model(s).");

            if (missing.Count > 0)
            {
                // NAMED, AND NOT WRITTEN OFF. These are the ones the game will not spawn,
                // which for a fixture bolted into an interior is normal and means nothing --
                // they are searched for like all the rest. It is worth reading only when a
                // name typed into ExtraModels never finds anything: then it is probably a
                // typo, and this is the line that shows it.
                Log.Info("Fridges: these are map-only or unknown, and are searched for anyway - " +
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
            //
            // AND AN EMPTY WINDOW IS CACHED AS WELL. The clock used to be honoured only when
            // there was a fridge to hand back, and there is no fridge to hand back nearly all
            // the time -- the player is in a kitchen for a few seconds of a session. So the
            // throttle threw itself away in exactly the case that mattered and the whole model
            // sweep ran on every single tick, all session, only ever to say no again. "Nothing
            // here" is an answer worth holding for the rest of the window too.
            if (now < _nextScan)
            {
                if (_found == null) return null;

                if (_found.Exists() && _found.Position.DistanceTo(from) <= radius) return _found;
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
