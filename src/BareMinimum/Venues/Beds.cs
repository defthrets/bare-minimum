using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>
    /// Finds the bed you are standing next to.
    ///
    /// BY PROP, NOT BY COORDINATE. A hardcoded list of safehouse bed positions is wrong the
    /// moment anybody installs an interior mod, and -- more to the point here -- it is a list
    /// somebody has to be certain of in the first place. Asking the game which bed object is
    /// nearest is exact, needs nothing written down, and picks up every bed the base game
    /// places including ones in interiors nobody has enumerated. The same reasoning that put
    /// Fumes on prop-based pumps rather than a forecourt list.
    ///
    /// GET_CLOSEST_OBJECT_OF_TYPE is the native that makes it possible, and the reason it has
    /// to be that one: it sees MAP objects. World.GetNearbyProps walks the object pool, which
    /// placed map props are not in, so it finds nothing in a bedroom.
    ///
    /// THE MODEL LIST IS SELF-DIAGNOSING. Every name is checked with IS_MODEL_VALID at
    /// start-up and the ones this build does not have are named in the log. A wrong guess
    /// therefore costs one log line rather than a bed that silently never appears -- which
    /// matters, because a bed model name is exactly the sort of thing that is easy to get
    /// slightly wrong and impossible to notice.
    /// </summary>
    internal sealed class Beds
    {
        /// <summary>
        /// Candidate bed models, base game and DLC.
        ///
        /// Deliberately broad. A name this build does not know never matches anything, so the
        /// cost of listing one too many is a single line in the log; the cost of listing one
        /// too few is a safehouse where sleeping does not work and no clue as to why.
        /// </summary>
        private static readonly string[] Candidates =
        {
            // Safehouses
            "v_res_mbbed",              // Michael's master bedroom
            "v_res_msonbed",            // Jimmy's
            "v_res_msdaubed",           // Tracey's
            "v_res_fh_bed01",           // Franklin's Vinewood Hills house
            "v_res_fa_bed",             // Franklin's aunt's, Forum Drive
            "v_res_tre_bed1",           // Trevor's trailer
            "v_res_d_bed01",
            "v_res_j_bed",
            "v_res_r_bedtidy",
            "v_res_r_bedmess",
            "p_lestersbed_s",

            // Motels, apartments and DLC interiors
            "apa_mp_h_bed_double_01",
            "apa_mp_h_bed_single_01",
            "bkr_prop_clubhouse_bed_01a",
            "ex_prop_offbed_01a",
            "hei_heist_bed_01",
            "miss_rub_bed01",
            "prop_rub_bed01",
            "v_res_bed_01"
        };

        private int[] _hashes;

        /// <summary>The scan is not free, so it runs on a clock rather than every frame.</summary>
        private int _nextScan;
        private Prop _found;

        /// <summary>
        /// Model hashes, resolved once and lazily.
        ///
        /// Lazily because a Model cannot be asked anything useful until the game is actually
        /// running -- doing this in a constructor means asking during script construction,
        /// which happens before the world exists.
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

            Log.Info("Beds: " + _hashes.Length + " of " + Candidates.Length +
                     " model(s) exist in this build.");

            if (missing.Count > 0)
            {
                // Named rather than counted, so a bad guess in the list above can be corrected
                // from somebody else's log without them having to reproduce anything.
                Log.Info("Beds: not in this build, ignored - " + string.Join(", ", missing.ToArray()));
            }

            return _hashes;
        }

        /// <summary>The bed within reach, or null. Re-scanned a few times a second.</summary>
        public Prop Nearest(Vector3 from, float radius)
        {
            var now = Game.GameTime;

            // A bed already held on to stays valid between scans, so the prompt does not
            // flicker while you shuffle about at the edge of the radius.
            //
            // BUT IT IS STILL RANGE-CHECKED, or the cache hands back a bed the player has
            // already walked away from for up to a fifth of a second.
            //
            // AND AN EMPTY WINDOW COUNTS AS AN ANSWER TOO. The clock used to be honoured only
            // when there was a bed to hand back, and there is no bed to hand back nearly all
            // the time -- a player is next to one for a few seconds of a session. So the
            // throttle threw itself away in exactly the case that mattered and the full model
            // sweep ran on every single tick, all session, to keep saying no. "Nothing here"
            // is worth keeping for the rest of the window every bit as much as a bed is.
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
                Log.Once("beds-scan", "Could not look for a bed: " + ex.Message);
                _found = null;
            }

            return _found;
        }
    }
}
