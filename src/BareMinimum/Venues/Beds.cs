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
    /// THE NAMES ARE THE GAME'S OWN, NOT GUESSED. Fifteen of the nineteen on the first list
    /// did not exist: written from memory, and the game's object lists -- Menyoo's PropList
    /// and Rampage's ObjectList, twenty-three thousand names between them -- have never heard
    /// of v_res_fa_bed, v_res_fh_bed01 or apa_mp_h_bed_double_01. The log said "19 of 19
    /// exist" because every name is searched regardless, and the "not in this build" line
    /// named the same fifteen every launch and read as fixtures answering no. So for three
    /// weeks a bed was found by FOUR models -- Michael's, Jimmy's, Trevor's and Lester's --
    /// and Franklin's two, v_res_tt_bed at his aunt's and v_res_mdbed in the hills, were never
    /// on it. Reported by Vitalezzzz as sleeping not working in M8T's houses, which stand up
    /// exactly the apartment and DLC beds that were missing. Every name below was checked
    /// against those two files on 2026-09-25, and anything that is not is a line in the log.
    /// </summary>
    internal sealed class Beds
    {
        /// <summary>
        /// Every bed-shaped object in the game's own lists, base game and DLC.
        ///
        /// BEDS, NOT BEDROOMS. The lists hold a hundred and seventy-two names with "bed" in
        /// them and most are not a bed: bedside tables, bed lamps, the overlays and decals a
        /// room's bed is dressed with, flatbed truck ramps, dog beds, the seabed. Those cost a
        /// native call each on every scan and could never be slept on, so they are left out.
        /// What is left is fifty-odd, and a name that is a bed somewhere else goes in the ini.
        /// </summary>
        private static readonly string[] Candidates =
        {
            // The three men's own, and the beds in the story's houses
            "v_res_mbbed",                  // Michael's master bedroom
            "v_res_mbbed_mess",             // ...unmade
            "v_res_msonbed",                // Jimmy's
            "v_res_msonbed_s",
            "v_res_mdbed",                  // Franklin's Vinewood Hills house
            "v_res_tt_bed",                 // Franklin's aunt's, Forum Drive
            "v_res_tre_bed1",               // Trevor's trailer
            "v_res_tre_bed1_messy",
            "v_res_tre_bed2",
            "v_res_d_bed",
            "v_res_lestersbed",             // Lester's
            "p_lestersbed_s",               // cutscene copies, stood up while one plays
            "p_mbbed_s",
            "p_v_res_tt_bed_s",

            // Motels, the hospital, and the story's other interiors
            "v_16_bdr_mesh_bed",            // the motel room
            "v_16_mid_bed_bed",
            "v_24_bdr_mesh_bed",
            "v_61_bd2_mesh_bed",
            "v_med_bed1",                   // hospital
            "v_med_bed2",
            "v_med_emptybed",

            // Online apartments, yachts, offices, clubhouses and bunkers -- the interiors a
            // housing mod stands up, which is where the missing names were felt
            "apa_mp_h_bed_double_08",
            "apa_mp_h_bed_double_09",
            "apa_mp_h_bed_wide_05",
            "apa_mp_h_bed_with_table_02",
            "apa_mp_h_stn_sofa_daybed_01",
            "apa_mp_h_stn_sofa_daybed_02",
            "apa_mp_h_yacht_bed_01",
            "apa_mp_h_yacht_bed_02",
            "h4_mp_h_yacht_bed_01",
            "h4_mp_h_yacht_bed_02",
            "sf_mp_h_yacht_bed_01",
            "sf_mp_h_yacht_bed_02",
            "sum_mp_h_yacht_bed_01",
            "sum_mp_h_yacht_bed_02",
            "sum_mpapyacht_d2beds_bed",
            "sum_bedathpl3",                // named like a bed; harmless if it is not
            "hei_heist_bed_double_08",
            "ex_prop_exec_bed_01",
            "bkr_prop_biker_campbed_01",
            "gr_prop_gr_campbed_01",
            "gr_prop_bunker_bed_01",
            "imp_prop_impexp_campbed_01",
            "m24_2_prop_m42_bunkerbed_01a",
            "m25_1_prop_m51_bunkerbed_01a",
            "m25_1_prop_m51_bed_01a",
            "m25_1_prop_m51_bed_02a",

            // The newest DLC interiors, whose beds are pieces of the room rather than props
            "m23_2_int4_m232_int_sub_bed",
            "m24_1_int_01_m241_bed",
            "m25_1_int_05_mid_bed_bed",
            "m25_2_int_01_main_bed_01",
            "m25_2_int_01_c_gr_bed",
            "m25_2_int_01_l_gr_bed",
            "m25_2_int_01_l_mb_bed",
            "m25_2_int_01_r_gr_bed",
            "m25_2_int_01_r_mb_bed"
        };

        /// <summary>
        /// The settings, for the one thing this class asks them: the player's own extra bed
        /// model names. The same arrangement as Fridges, for the same reason -- a list in a
        /// dll cannot be complete, and a bed from a mod this one has never heard of should
        /// not have to wait for a build.
        /// </summary>
        private readonly Settings _cfg;

        public Beds(Settings cfg)
        {
            _cfg = cfg;
        }

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

            // THE PLAYER'S OWN NAMES ON TOP OF THE LIST. See Settings.BedExtraModels: a bed
            // from an interior mod this build has never heard of is one line in the ini
            // rather than a wait for the next release.
            var wanted = new List<string>(Candidates);

            foreach (var extra in (_cfg == null || _cfg.BedExtraModels == null ? "" : _cfg.BedExtraModels).Split(',', ';'))
            {
                var name = extra.Trim();

                if (name.Length == 0) continue;
                if (wanted.Contains(name)) continue;

                wanted.Add(name);
            }

            foreach (var name in wanted)
            {
                try
                {
                    var model = new Model(name);

                    // SEARCHED FOR, NOT SPAWNED, so IS_MODEL_VALID is the wrong question --
                    // see Fridges.Hashes, where this cost the one fridge Franklin owns. It
                    // asks whether a model can be streamed in and created; these are found by
                    // hash in the world with GET_CLOSEST_OBJECT_OF_TYPE, which does not care.
                    // Map-only interior fixtures answer no while standing in front of you,
                    // and its answer is not even stable between launches: this mod's own log
                    // has "1 of 19" on one start and "4 of 19" on the next, same build.
                    good.Add(model.Hash);

                    // Kept for the log only. A name typed into the ini that answers no is far
                    // more likely to be a typo than a fixture, and that is worth reading.
                    if (!Function.Call<bool>(Hash.IS_MODEL_VALID, model.Hash)) missing.Add(name);
                }
                catch
                {
                    missing.Add(name);
                }
            }

            _hashes = good.ToArray();

            // "LOOKING FOR", NOT "EXIST". The old line said "19 of 19 model(s) exist in this
            // build" about a list of which fifteen did not exist anywhere, because every name
            // goes in whether or not the game will spawn it. Fridges' wording is the honest
            // one and this is it.
            Log.Info("Beds: looking for " + _hashes.Length + " model(s)" +
                     (wanted.Count > Candidates.Length
                          ? ", " + (wanted.Count - Candidates.Length) + " of them from the ini"
                          : "") + ".");

            if (missing.Count > 0)
            {
                // NAMED, AND NOT WRITTEN OFF. Map-only fixtures answer no here and are found
                // regardless; the line is worth reading when a name typed into ExtraModels
                // never finds a bed, because then it is probably a typo.
                Log.Info("Beds: these are map-only or unknown, and are searched for anyway - " +
                         string.Join(", ", missing.ToArray()));
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
