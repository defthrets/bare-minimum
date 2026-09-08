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
        /// A SNACK machine -- the candy box, not the soda machine.
        ///
        /// The distinction is the whole reason this is on again. The game sells a drink out
        /// of a soda machine and plays its own animation for it; putting a second purchase on
        /// top of that would be two mods fighting over one prop. It sells NOTHING out of a
        /// candy machine -- that one is scenery -- so there is nothing there to fight with.
        ///
        /// The soda machines are handled by watching instead. See Venues/Sipping.
        /// </summary>
        Machine,

        /// <summary>
        /// A roadside fruit and veg stall.
        ///
        /// BY MODEL, because the game repeats the same stall from the Chumash coast to
        /// Chiliad -- two of them 2.2km apart wearing the identical canopy. Listing those by
        /// coordinate would mean finding every one by hand and still missing the rest, which
        /// is the mistake the fridges and the tills already avoid.
        /// </summary>
        Stall
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
        /// <summary>
        /// The settings. Counters had no constructor until the machines and stalls arrived --
        /// before them there was nothing here a player could turn off.
        /// </summary>
        private readonly Settings _cfg;

        public Counters(Settings cfg)
        {
            _cfg = cfg;
        }

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

        /// <summary>
        /// SNACK machines only. The soda ones are deliberately absent.
        ///
        /// prop_vend_soda_01, prop_vend_soda_02, prop_vend_water_01, prop_vend_coffe_01 and
        /// prop_vend_fridge01 all exist in this build and are all left alone: the game sells
        /// a drink from those itself. Venues/Sipping watches them instead of selling from
        /// them, so the vanilla purchase stays the only purchase.
        /// </summary>
        internal static readonly string[] MachineModels =
        {
            "prop_vend_snak_01",
            "prop_vend_snak_01_tu"
        };

        /// <summary>
        /// Roadside produce stalls. Every one of these is the same canopy in a different
        /// county, which is exactly why they are found by model and not by coordinate.
        /// </summary>
        internal static readonly string[] StallModels =
        {
            "prop_fruitstand_b",
            "prop_fruitstand_b_nite",
            "prop_fruitstand_01",
            "prop_fruit_stand_01",
            "prop_fruit_stand_02",
            "prop_fruit_stand_03",
            "v_73_p_ap_banostall_az"
        };

        /// <summary>
        /// Shops that are NOT food shops. While one of these is running, there is no counter.
        ///
        /// THE PROP LIST CANNOT TELL THESE APART AND NEVER COULD. Of the eight till models
        /// above, this build has three -- prop_till_01, 02 and 03 -- and the five that were
        /// specific to somewhere (v_ret_247_till, v_ret_gc_till and the rest) do not exist at
        /// all. So every counter the mod finds is a generic register, and a generic register
        /// sits behind the desk at Ammu-Nation exactly as readily as in a 24/7. Deleting the
        /// three would not fix it, it would turn the feature off.
        ///
        /// So the discriminator is the SHOP'S OWN SCRIPT. GTA launches one when you walk into
        /// a shop and stops it when you leave, which makes "is gunclub_shop running" a direct
        /// answer to "am I standing in an Ammu-Nation" -- far better than a coordinate list
        /// and immune to map mods moving things around.
        ///
        /// Taken from a log rather than guessed. The counter that opened over the pistol case
        /// had gunclub_shop, clothes_shop_sp and launcher_Range in its script list; the three
        /// legitimate shop counters in the same log had none of them.
        /// </summary>
        private static readonly string[] NotFood =
        {
            "gunclub_shop",       // Ammu-Nation
            "clothes_shop_sp",    // Binco, Suburban, Ponsonbys, Discount
            "barber_shop",        // Bob Mulet, Herr Kutz
            "tattoo_shop",        // the tattooists
            "carmod_shop"         // Los Santos Customs, Benny's
        };

        private int _nextShopCheck;
        private bool _inNotFood;

        /// <summary>
        /// Whether a non-food shop's script is running, cached for a fifth of a second.
        ///
        /// Cached because this is asked every frame the player is near a till and the native
        /// is not free. A fifth of a second is far quicker than anybody can walk from a gun
        /// counter to a food one.
        /// </summary>
        private bool InNotFoodShop()
        {
            var now = Game.GameTime;
            if (now < _nextShopCheck) return _inNotFood;

            _nextShopCheck = now + 200;
            _inNotFood = false;

            // WALKED WITH THE THREAD ITERATOR, not asked about by name.
            //
            // GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT is the obvious native and this build's
            // SHVDN does not expose it. The iterator is what the shop already uses to list the
            // running scripts into the log, so it is known to work here rather than hoped to.
            try
            {
                Function.Call(Hash.SCRIPT_THREAD_ITERATOR_RESET);

                // Bounded for the same reason the shop's own walk is: an iterator that never
                // returns 0 would otherwise hang the frame. Sixty-odd scripts run at a counter,
                // so 400 is generous.
                for (var i = 0; i < 400; i++)
                {
                    var id = Function.Call<int>(Hash.SCRIPT_THREAD_ITERATOR_GET_NEXT_THREAD_ID);
                    if (id == 0) break;

                    var name = Function.Call<string>(Hash.GET_NAME_OF_SCRIPT_WITH_THIS_ID, id);
                    if (string.IsNullOrEmpty(name)) continue;

                    foreach (var shop in NotFood)
                    {
                        if (!string.Equals(name, shop, StringComparison.OrdinalIgnoreCase)) continue;

                        _inNotFood = true;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                // A counter that opens in a gun shop is a smaller fault than one that never
                // opens anywhere, so a failure here says "not a gun shop" and carries on.
                Log.Once("counter-shopcheck", "Could not read the running scripts: " + ex.Message);
            }

            return false;
        }

        private int[] _tills;
        private int[] _machines;
        private int[] _stalls;

        private int _nextScan;
        private Counter _kind = Counter.None;
        private Prop _found;

        /// <summary>How close you have to stand. A till is behind a counter, so it is generous.</summary>
        public float TillReach = 1.9f;

        /// <summary>
        /// How close you stand to a machine, and to a stall.
        ///
        /// A machine is a box on a wall and you step right up to it, so it is tighter than a
        /// till. A stall is a trestle with crates in front of it and the seller behind, so it
        /// is looser -- the same figure the hot dog stands use.
        /// </summary>
        public float MachineReach = 1.5f;

        // A TRESTLE IS WIDE AND ITS ORIGIN IS IN THE MIDDLE OF IT. Two and a half metres is
        // measured from that origin, so standing at the end of a produce stall -- which is
        // where the crates are, and where anybody would stand -- was often out of reach of a
        // stall you were touching. The hot dog carts ask for 2.6 and are half the width.
        public float StallReach = 3.4f;

        /// <summary>The prop currently being offered, or null.</summary>
        public Prop Found => _found;

        // ======================================================================

        /// <summary>
        /// Writes each till model to the log the first time one is stood at.
        ///
        /// WHICH SHOPS THESE PROPS ACTUALLY LIVE IN IS NOT KNOWABLE FROM HERE. The intent
        /// above is every 24/7, LTD and Rob's Liquor -- but prop_till_01 and the two cash
        /// registers are generic fittings, and a generic fitting turns up in clothing shops,
        /// barbers and Ammu-Nation just as readily as in a shop that sells food. Finding one
        /// there offers a club sandwich at a gun counter.
        ///
        /// Guessing which to drop is the wrong move in both directions: leave a bad one in and
        /// the food shop appears where it should not, take a good one out and a real shop
        /// stops working. So the model gets named in the log instead, next to the room key
        /// Brands already writes, and one walk through a wrong counter says exactly which
        /// entry to remove.
        /// </summary>
        private void Name(Prop till)
        {
            if (till == null || !till.Exists()) return;

            int hash;
            try { hash = till.Model.Hash; }
            catch { return; }

            if (!_named.Add(hash)) return;

            // Bounded for the same reason the room keys are: a long session should not fill
            // the log with this.
            if (_named.Count > 12) return;

            // Matched by re-hashing the NAMES rather than by index into _tills, which holds
            // only the models that exist in this build and so does not line up with the list
            // above the moment one of them is missing.
            var name = "unrecognised";

            foreach (var candidate in TillModels)
            {
                try
                {
                    if (Function.Call<int>(Hash.GET_HASH_KEY, candidate) != hash) continue;
                }
                catch { continue; }

                name = candidate;
                break;
            }

            Log.Info("Counter prop: " + name +
                     "  (if this counter is not a food shop, remove that model from TillModels)");
        }

        private readonly HashSet<int> _named = new HashSet<int>();

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

            // NOT IN A GUN SHOP, A CLOTHES SHOP OR A BARBER'S. Checked before the prop scan
            // rather than after, because the answer does not depend on which register is
            // nearest -- if you are inside one of those, there is no food counter anywhere in
            // the building.
            if (InNotFoodShop()) return Counter.None;

            try
            {
                var till = Closest(from, TillReach, Resolve(TillModels, "Tills", ref _tills));
                if (till != null)
                {
                    _found = till;
                    _kind = Counter.Till;
                    Name(till);
                    return _kind;
                }

                // AFTER THE TILL, ALWAYS. A 24/7 has a candy machine by the door and a till
                // eight feet behind it, and the till is the one with the whole menu on it --
                // being handed a bag of crisps because you stood slightly to the left of the
                // counter would read as the mod picking wrong.
                if (_cfg.VendingMachines)
                {
                    var machine = Closest(from, MachineReach,
                                          Resolve(MachineModels, "Snack machines", ref _machines));
                    if (machine != null)
                    {
                        _found = machine;
                        _kind = Counter.Machine;
                        return _kind;
                    }
                }

                if (_cfg.FruitStalls)
                {
                    var stall = Closest(from, StallReach,
                                        Resolve(StallModels, "Fruit stalls", ref _stalls));
                    if (stall != null)
                    {
                        _found = stall;
                        _kind = Counter.Stall;
                        return _kind;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Once("counters-scan", "Could not look for a counter: " + ex.Message);
            }

            return Counter.None;
        }

        /// <summary>
        /// Is there a till within `radius`? Nothing is remembered and nothing is offered.
        ///
        /// SEPARATE FROM Nearest BECAUSE IT ASKS A DIFFERENT QUESTION. Nearest is "may the
        /// player buy something here", which is a question about arm's reach and has to be
        /// narrow. This is "is the game about to offer its own shop menu", which is a question
        /// about the game's trigger volume and has to be wider -- and answering the second
        /// with the first is why the vanilla list kept getting in.
        ///
        /// The not-a-food-shop check still applies: there is no reason to be shutting scripts
        /// up inside Ammu-Nation, where we were never going to sell anything anyway.
        /// </summary>
        public bool AnyNear(Vector3 from, float radius)
        {
            try
            {
                if (InNotFoodShop()) return false;

                return Closest(from, radius, Resolve(TillModels, "Tills", ref _tills)) != null;
            }
            catch (Exception ex)
            {
                Log.Once("counters-anynear", "Could not sweep for a till: " + ex.Message);
                return false;
            }
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
