using System;
using System.Collections.Generic;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// What you are carrying but have not eaten yet.
    ///
    /// Buying used to be eating: press the key at a hot dog stand and Franklin ate a hot dog
    /// on the spot. That is fine for a stand you walked up to on purpose and wrong for
    /// everything else -- you cannot stock up before a long drive, you cannot buy a packet of
    /// cigarettes for later, and a shop with fifteen things on the shelf sells you exactly one
    /// of them per visit because the second one is refused while you are still chewing the
    /// first.
    ///
    /// PER CHARACTER, for the same reason the needs are. Switching to Trevor and finding
    /// Franklin's shopping in your pockets reads as a bug to somebody who could not say why,
    /// and the file already has a shape for this -- see needs.json, which this deliberately
    /// mirrors rather than inventing a second convention for the same idea.
    ///
    /// COUNTS, NOT SLOTS. Three hot dogs are one entry saying three, not three entries. A grid
    /// of identical pictures is a worse read than one picture with a number on it, and it also
    /// means the capacity limit can be a number of ITEMS rather than a number of squares --
    /// which is the thing a player actually cares about running out of.
    ///
    /// The counts are ints and the ids are strings, which is not an accident either: this is
    /// the shape that has to cross the bridge to Hoodrich's phone, and only BCL types can make
    /// that trip. See Api.Pantry.
    /// </summary>
    internal sealed class Pantry
    {
        private readonly Core.Settings _cfg;
        private readonly Catalogue _menu;

        /// <summary>Every character's bag, keyed the way needs.json keys its own.</summary>
        private readonly Dictionary<string, Dictionary<string, int>> _bags =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The order things were first picked up in, per character.
        ///
        /// A Dictionary does not promise an order and the grid has to have one, or the squares
        /// shuffle every time something is eaten. Kept beside the counts rather than sorted at
        /// draw time because "where the taco is" should stay true between one look and the
        /// next -- alphabetical would move everything whenever you bought a burger.
        /// </summary>
        private readonly Dictionary<string, List<string>> _order =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private string _who;

        private bool _dirty;
        private float _sinceSave;

        public Pantry(Core.Settings cfg, Catalogue menu)
        {
            _cfg = cfg;
            _menu = menu;

            Load();
        }

        // ======================================================================

        /// <summary>How many items are being carried in total, all kinds together.</summary>
        public int Total
        {
            get
            {
                var bag = Bag();
                var n = 0;

                foreach (var count in bag.Values) n += count;

                return n;
            }
        }

        public int Slots => _cfg.PantrySlots < 1 ? 1 : _cfg.PantrySlots;

        public bool Full => Total >= Slots;

        public int CountOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;

            int n;
            return Bag().TryGetValue(id, out n) ? n : 0;
        }

        /// <summary>The ids being carried, oldest first. A fresh list -- callers may hold it.</summary>
        public List<string> Ids()
        {
            var bag = Bag();
            var order = Order();

            var ids = new List<string>();

            foreach (var id in order)
            {
                int n;
                if (bag.TryGetValue(id, out n) && n > 0) ids.Add(id);
            }

            return ids;
        }

        /// <summary>
        /// Puts one in the bag. False when there is no room, and the caller must not charge.
        /// </summary>
        public bool Add(string id, int howMany = 1)
        {
            if (string.IsNullOrEmpty(id) || howMany < 1) return false;

            var bag = Bag();

            // Checked against the TOTAL rather than per kind. A pocket holds what it holds; it
            // does not care whether that is twenty tacos or one of everything.
            if (Total + howMany > Slots) return false;

            int have;
            bag.TryGetValue(id, out have);

            bag[id] = have + howMany;

            var order = Order();
            if (!order.Contains(id)) order.Add(id);

            _dirty = true;
            return true;
        }

        /// <summary>Takes one out. False when there was not one to take.</summary>
        public bool Take(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            var bag = Bag();

            int have;
            if (!bag.TryGetValue(id, out have) || have < 1) return false;

            if (have <= 1)
            {
                bag.Remove(id);

                // LEFT IN THE ORDER LIST DELIBERATELY. Eating your last taco and buying another
                // should put it back where it was, not at the end -- the grid holding still is
                // worth more than the list being tidy, and a stale id costs one dictionary miss
                // in Ids().
                //
                // Pruned on save instead, where the file would otherwise grow forever.
            }
            else
            {
                bag[id] = have - 1;
            }

            _dirty = true;
            return true;
        }

        public void Clear()
        {
            Bag().Clear();
            Order().Clear();
            _dirty = true;
        }

        // ======================================================================

        /// <summary>Follows the character switch, and saves when things have settled.</summary>
        public void Update(float realSeconds)
        {
            var now = Needs.Needs.Who();

            if (!string.Equals(now, _who, StringComparison.OrdinalIgnoreCase))
            {
                // Nothing to carry across. The bags are already keyed per character and the
                // switch is just a change of which one is being looked at.
                _who = now;
            }

            if (!_dirty) return;

            _sinceSave += realSeconds;
            if (_sinceSave < 10f) return;

            SaveNow();
        }

        private Dictionary<string, int> Bag()
        {
            if (string.IsNullOrEmpty(_who)) _who = Needs.Needs.Who();

            Dictionary<string, int> bag;
            if (_bags.TryGetValue(_who, out bag)) return bag;

            bag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _bags[_who] = bag;
            return bag;
        }

        private List<string> Order()
        {
            if (string.IsNullOrEmpty(_who)) _who = Needs.Needs.Who();

            List<string> order;
            if (_order.TryGetValue(_who, out order)) return order;

            order = new List<string>();
            _order[_who] = order;
            return order;
        }

        // ======================================================================

        private void Load()
        {
            try
            {
                var doc = JsonFile.Read(Paths.PantryFile);
                if (doc == null || doc.IsNull) return;

                var people = doc["characters"];
                if (people == null || people.IsNull) return;

                foreach (var who in people.Keys)
                {
                    var node = people[who];
                    if (node == null || node.IsNull) continue;

                    var bag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    var order = new List<string>();

                    foreach (var id in node.Keys)
                    {
                        var n = node[id].AsInt(0);
                        if (n < 1) continue;

                        // DROPPED IF THE CATALOGUE NO LONGER HAS IT. foods.json is editable and
                        // a saved bag can outlive the item in it; carrying an id nothing can
                        // name gives a blank square that cannot be eaten or thrown away.
                        if (_menu.Find(id) == null)
                        {
                            Log.Info("Pantry: " + who + " was carrying " + n + " x " + id +
                                     ", which is not in foods.json any more. Dropped.");
                            continue;
                        }

                        bag[id] = n;
                        order.Add(id);
                    }

                    _bags[who] = bag;
                    _order[who] = order;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not read " + Paths.PantryFile + " - starting with empty pockets.", ex);
            }
        }

        public void SaveNow()
        {
            _sinceSave = 0f;

            if (!_dirty) return;
            _dirty = false;

            try
            {
                var people = Json.Object();

                foreach (var pair in _bags)
                {
                    var bag = pair.Value;
                    if (bag.Count == 0) continue;

                    var node = Json.Object();

                    // Walked in ORDER so the file reads the way the grid does, and so the
                    // order survives a restart -- it is part of the state, not a view of it.
                    List<string> order;
                    if (!_order.TryGetValue(pair.Key, out order)) order = new List<string>(bag.Keys);

                    foreach (var id in order)
                    {
                        int n;
                        if (bag.TryGetValue(id, out n) && n > 0) node.Set(id, n);
                    }

                    people.Set(pair.Key, node);
                }

                var doc = Json.Object()
                    .Set("version", 1)
                    .Set("characters", people);

                if (!JsonFile.Write(Paths.PantryFile, doc))
                {
                    Log.Once("pantry-save", "Could not write " + Paths.PantryFile +
                                            " - what you are carrying will not survive this session.");
                }
            }
            catch (Exception ex)
            {
                Log.Once("pantry-save", "Could not save the pantry: " + ex.Message);
            }
        }
    }
}
