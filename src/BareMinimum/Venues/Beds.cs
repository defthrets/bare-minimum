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
        /// Couches, sofas and loungers: a decent sleep, not a bed. See Settings.CouchHours.
        ///
        /// ALL THE GAME'S OWN, checked against the two lists like the beds -- the story's
        /// houses, the Online apartments, yachts, offices and clubhouses, and the patio and
        /// deck loungers. A couch dumped in the street is not on here; it is on Rough.
        /// </summary>
        private static readonly string[] Couches =
        {
            "v_res_tt_sofa", "v_res_fh_sofa", "v_res_m_h_sofa", "v_res_m_h_sofa_sml",
            "v_res_mp_sofa", "v_res_d_sofa", "v_res_j_sofa", "v_res_r_sofa",
            "v_res_tre_sofa", "v_res_tre_sofa_s", "v_res_tre_sofa_mess_a", "v_res_tre_sofa_mess_b",
            "v_res_tre_sofa_mess_c", "v_tre_sofa_mess_a_s", "v_tre_sofa_mess_b_s", "v_tre_sofa_mess_c_s",
            "v_ilev_m_sofa", "v_med_p_sofa", "p_v_med_p_sofa_s", "p_lev_sofa_s",
            "p_res_sofa_l_s", "v_16_v_sofa", "v_16_study_sofa", "v_16_low_lng_mesh_sofa1",
            "v_16_low_lng_mesh_sofa2", "v_24_lgb_mesh_sofa", "v_club_officesofa", "prop_couch_01",
            "prop_couch_03", "prop_couch_04", "prop_couch_lg_02", "prop_couch_lg_05",
            "prop_couch_lg_06", "prop_couch_lg_07", "prop_couch_lg_08", "prop_couch_sm1_07",
            "prop_couch_sm2_07", "prop_couch_sm_02", "prop_couch_sm_05", "prop_couch_sm_06",
            "prop_couch_sm_07", "prop_t_sofa", "prop_t_sofa_02", "prop_yaught_sofa_01",
            "p_yacht_sofa_01_s", "apa_mp_h_stn_sofa2seat_02", "apa_mp_h_stn_sofacorn_01", "apa_mp_h_stn_sofacorn_05",
            "apa_mp_h_stn_sofacorn_06", "apa_mp_h_stn_sofacorn_07", "apa_mp_h_stn_sofacorn_08", "apa_mp_h_stn_sofacorn_09",
            "apa_mp_h_stn_sofacorn_10", "apa_mp_h_yacht_sofa_01", "apa_mp_h_yacht_sofa_02", "h4_mp_h_yacht_sofa_01",
            "h4_mp_h_yacht_sofa_02", "sf_mp_h_yacht_sofa_01", "sf_mp_h_yacht_sofa_02", "sum_mp_h_yacht_sofa_01",
            "sum_mp_h_yacht_sofa_02", "hei_heist_stn_sofa2seat_02", "hei_heist_stn_sofa2seat_03", "hei_heist_stn_sofa2seat_06",
            "hei_heist_stn_sofa3seat_01", "hei_heist_stn_sofa3seat_02", "hei_heist_stn_sofa3seat_06", "hei_heist_stn_sofacorn_05",
            "hei_heist_stn_sofacorn_06", "ex_mp_h_off_sofa_003", "ex_mp_h_off_sofa_01", "ex_mp_h_off_sofa_02",
            "bkr_prop_clubhouse_sofa_01a", "imp_prop_impexp_sofabed_01a", "h4_prop_h4_couch_01a", "sf_prop_sf_sofa_chefield_01a",
            "sf_prop_sf_sofa_chefield_02a", "sf_prop_sf_sofa_studio_01a", "xm_lab_sofa_01", "xm_lab_sofa_02",
            "xm3_int3_carware_basic_sofa", "m23_2_int4_m232_sofa_01", "m24_1_prop_m41_sofa_01a", "m25_1_prop_m51_sofa_01a",
            "m25_1_prop_m51_couchlarge_01a", "m25_1_prop_m51_couchlarge_02a", "m25_1_prop_m51_couchsmall_01a", "m25_1_prop_m51_couchsmall_02a",
            "m25_1_int_02_03_heli_sofa", "m25_1_int_02_03_smoke_sofa", "m25_1_int_04_low_lng_mesh_sofa1", "m25_1_int_04_low_lng_mesh_sofa2",
            "m25_2_int_01_cine_sofas", "m25_2_int_01_gr_sofa_01", "m25_2_prop_m52_c_sofa_01", "m25_2_prop_m52_l_sofa_01",
            "m25_2_prop_m52_r_sofa_01", "m25_2_prop_m52_sofa_01a", "m25_2_prop_m52_sofa_corner_01a", "m25_2_prop_m52_sofa_round_01a",
            "m25_2_prop_m52_sofa_small_01a", "m26_1_int_01_sofa_001", "prop_patio_lounger1", "prop_patio_lounger1b",
            "prop_patio_lounger_2", "prop_patio_lounger_3", "p_patio_lounger1_s", "prop_yacht_lounger",
            "hei_prop_yah_lounger", "gr_dlc_gr_yacht_props_lounger", "m24_1_prop_m24_1_carrier_yah_lounger", "m24_1_prop_m41_lounger_01a"
        };

        /// <summary>
        /// Sleeping rough: park, bus-stop and hospital benches, tents, homeless shelters, the
        /// mattresses and sleeping bags on the ground, and the couches dumped outside. See
        /// Settings.RoughHours.
        ///
        /// BENCHES YOU WOULD LIE ON, NOT BENCHES YOU WORK AT. The game's lists hold sixty-odd
        /// names with "bench" in them and a good share are workbenches, grinders, a gym bench
        /// and a piano stool. Those are left out, as are the hobo stoves and seat that sit
        /// beside the shelters, and the level-of-detail and collision copies of all of it.
        /// </summary>
        private static readonly string[] Rough =
        {
            "prop_bench_01a", "prop_bench_01b", "prop_bench_01c", "prop_bench_02",
            "prop_bench_03", "prop_bench_04", "prop_bench_05", "prop_bench_06",
            "prop_bench_07", "prop_bench_08", "prop_bench_09", "prop_bench_10",
            "prop_bench_11", "prop_ld_bench01", "prop_wait_bench_01", "prop_air_bench_01",
            "prop_air_bench_02", "prop_snow_bench_01", "prop_byard_bench01", "prop_byard_bench02",
            "prop_byard_benchset", "prop_fib_3b_bench", "prop_pris_bench_01", "v_ilev_ph_bench",
            "v_med_bench1", "v_med_bench2", "v_med_benchcentr", "v_med_benchset1",
            "hei_prop_hei_med_benchset1", "v_med_cor_wheelbench", "v_res_fh_benchlong", "v_res_fh_benchshort",
            "hei_heist_stn_benchshort", "v_16_shitbench", "reh_int3_officebench", "xm3_prop_xm3_bench_03b",
            "xm3_prop_xm3_bench_04b", "m24_1_int_01_m241_ent_bench", "m24_2_int_01_bench_01b", "m25_2_prop_m52_c_bench_01",
            "m25_2_prop_m52_c_bench_02", "m25_2_prop_m52_l_bench_01", "m25_2_prop_m52_l_bench_02", "m25_2_prop_m52_r_bench_01",
            "m25_2_prop_m52_r_bench_02", "prop_skid_tent_01", "prop_skid_tent_01b", "prop_skid_tent_03",
            "prop_skid_tent_cloth", "m23_2_prop_m32_tent_01a", "xm3_prop_xm3_tent_01a", "ba_prop_battle_tent_01",
            "ba_prop_battle_tent_02", "prop_homeles_shelter_01", "prop_homeles_shelter_02", "prop_homeless_matress_01",
            "prop_homeless_matress_02", "prop_rub_matress_01", "prop_rub_matress_02", "prop_rub_matress_03",
            "prop_rub_matress_04", "xm3_prop_xm3_rub_matress_01a", "v_med_mattress", "prop_skid_sleepbag_1",
            "m23_2_prop_m32_sleepbag_01a", "prop_rub_couch01", "prop_rub_couch02", "prop_rub_couch03",
            "prop_rub_couch04", "miss_rub_couch_01", "prop_ld_farm_couch01", "prop_ld_farm_couch02"
        };

        /// <summary>What a found thing is, which decides how good a night it is.</summary>
        public enum Kind
        {
            Bed,
            Couch,
            Rough
        }

        /// <summary>The kind of each entry in _hashes, in the same order.</summary>
        private Kind[] _kinds;

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
            var kinds = new List<Kind>();
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

            // THE THREE LISTS IN ONE PASS, each name carrying its kind. Beds first, so the
            // player's own ExtraModels are beds, which is what that key has always meant.
            var all = new List<KeyValuePair<string, Kind>>();

            foreach (var name in wanted) all.Add(new KeyValuePair<string, Kind>(name, Kind.Bed));
            foreach (var name in Couches) all.Add(new KeyValuePair<string, Kind>(name, Kind.Couch));
            foreach (var name in Rough) all.Add(new KeyValuePair<string, Kind>(name, Kind.Rough));

            foreach (var pair in all)
            {
                var name = pair.Key;

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
                    kinds.Add(pair.Value);

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
            _kinds = kinds.ToArray();

            // "LOOKING FOR", NOT "EXIST". The old line said "19 of 19 model(s) exist in this
            // build" about a list of which fifteen did not exist anywhere, because every name
            // goes in whether or not the game will spawn it. Fridges' wording is the honest
            // one and this is it.
            Log.Info("Beds: looking for " + _hashes.Length + " model(s) to sleep on -- " +
                     wanted.Count + " bed(s)" +
                     (wanted.Count > Candidates.Length
                          ? " (" + (wanted.Count - Candidates.Length) + " from the ini)"
                          : "") +
                     ", " + Couches.Length + " couch(es) and " + Rough.Length +
                     " bench(es), tent(s), shelter(s) and mattress(es).");

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

        /// <summary>How many models a frame asks the game about. See Nearest.</summary>
        private const int PerFrame = 24;

        /// <summary>The rest between two whole sweeps.</summary>
        private const int SweepEveryMs = 250;

        /// <summary>Where the sweep in progress has got to, and the best it has found so far.</summary>
        private int _cursor;
        private Prop _sweepBest;
        private float _sweepDist;
        private Kind _sweepKind;

        /// <summary>What the last whole sweep found.</summary>
        private Kind _foundKind;

        /// <summary>Script time one whole sweep costs, measured and said once. See Nearest.</summary>
        private long _sweepTicks;
        private int _sweepFrames;
        private bool _costSaid;

        /// <summary>
        /// The nearest thing to sleep on within reach, and what kind it is, or null.
        ///
        /// A SWEEP SPREAD OVER FRAMES. The list is over two hundred models now, and every one is a
        /// GET_CLOSEST_OBJECT_OF_TYPE -- which is the only native that sees MAP objects, and a
        /// bench in a park is a map object. Asked all at once that is two hundred calls in one
        /// frame, five times a second, on a mod people have praised for not costing frames. So
        /// each frame asks PerFrame of them, the sweep takes a handful of frames, and the one
        /// before it stands until it finishes. The first whole sweep says in the log what it
        /// cost, so the number is known rather than hoped.
        /// </summary>
        public Prop Nearest(Vector3 from, float radius, out Kind kind)
        {
            kind = _foundKind;

            var hashes = Hashes();
            if (hashes.Length == 0) return null;

            var now = Game.GameTime;

            // BETWEEN SWEEPS, the last answer while it is still in reach.
            if (_cursor == 0 && now < _nextScan)
            {
                if (_found == null) return null;
                if (_found.Exists() && _found.Position.DistanceTo(from) <= radius) return _found;

                // Walked away from it: start looking again now rather than at the next tick.
                _found = null;
            }

            if (_cursor == 0)
            {
                _sweepBest = null;
                _sweepDist = float.MaxValue;
                _sweepTicks = 0;
                _sweepFrames = 0;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var end = Math.Min(hashes.Length, _cursor + PerFrame);

            try
            {
                for (var i = _cursor; i < end; i++)
                {
                    var handle = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,
                                                    from.X, from.Y, from.Z, radius, hashes[i],
                                                    false, false, false);
                    if (handle == 0) continue;

                    var prop = Entity.FromHandle(handle) as Prop;
                    if (prop == null || !prop.Exists()) continue;

                    var d = prop.Position.DistanceTo(from);
                    if (d >= _sweepDist) continue;

                    _sweepDist = d;
                    _sweepBest = prop;
                    _sweepKind = _kinds[i];
                }
            }
            catch (Exception ex)
            {
                Log.Once("beds-scan", "Could not look for somewhere to sleep: " + ex.Message);
            }

            _sweepTicks += watch.ElapsedTicks;
            _sweepFrames++;
            _cursor = end;

            // MID-SWEEP, the last answer stands while it is still in reach, so the prompt
            // does not blink off for the frames it takes to look again.
            if (_cursor < hashes.Length)
            {
                if (_found != null && _found.Exists() && _found.Position.DistanceTo(from) <= radius)
                {
                    return _found;
                }

                return null;
            }

            _cursor = 0;
            _nextScan = now + SweepEveryMs;
            _found = _sweepBest;
            _foundKind = _sweepKind;
            kind = _foundKind;

            if (!_costSaid)
            {
                _costSaid = true;

                var ms = _sweepTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

                Log.Info("Beds: one sweep of " + hashes.Length + " model(s) costs " +
                         ms.ToString("0.00") + " ms of script time, spread over " + _sweepFrames +
                         " frame(s), once every " + SweepEveryMs + " ms while he is on foot and not running.");
            }

            return _found;
        }

        /// <summary>The bed within reach, or null. For callers that do not care what kind.</summary>
        public Prop Nearest(Vector3 from, float radius)
        {
            Kind kind;
            return Nearest(from, radius, out kind);
        }
    }
}
