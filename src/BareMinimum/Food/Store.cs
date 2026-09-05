using System;
using System.Collections.Generic;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// Somewhere food is kept: a pocket, a fridge, or whatever comes next.
    ///
    /// EXTRACTED WHEN THE FRIDGE ARRIVED, not designed up front. The pocket was written first
    /// and on its own, and the fridge turned out to need the same hundred and fifty lines
    /// exactly -- per-character bags, an order list so the tiles do not shuffle, a dirty flag,
    /// a save on a timer, and a load that drops anything foods.json has stopped defining. Two
    /// copies of that would have been two places to fix the next bug in it.
    ///
    /// WHAT A SUBCLASS SUPPLIES is only what actually differs: how much fits, which file it is
    /// written to, and what to call it in the log. Everything else is here.
    ///
    /// PER CHARACTER, and that is not an implementation detail. Michael's fridge is in
    /// Rockford Hills and Franklin's is on Forum Drive; they are not the same fridge, and one
    /// shared bag would have Trevor eating somebody else's leftovers from across the map.
    /// The pocket has the same rule for the more obvious reason.
    ///
    /// THE ORDER LIST IS WHY THIS IS NOT JUST A DICTIONARY. A Dictionary has no order worth
    /// relying on, and a grid whose tiles rearrange themselves every time you take something
    /// out is a grid you cannot learn the shape of. Order records the sequence things first
    /// arrived in and nothing ever re-sorts it.
    /// </summary>
    internal abstract class Store
    {
        protected readonly Catalogue Menu;

        private readonly Dictionary<string, Dictionary<string, int>> _bags =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<string>> _order =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private string _who;
        private bool _dirty;
        private float _sinceSave;

        protected Store(Catalogue menu)
        {
            Menu = menu;
        }

        // ---- what a subclass has to answer ----------------------------------

        /// <summary>How many items fit, all kinds counted together.</summary>
        public abstract int Slots { get; }

        /// <summary>The file it is written to.</summary>
        protected abstract string File { get; }

        /// <summary>What to call it in the log, in words. "Pantry", "Fridge".</summary>
        protected abstract string What { get; }

        /// <summary>The once-only log key, so two stores do not silence each other.</summary>
        private string Key => What.ToLowerInvariant();

        // ---- reading --------------------------------------------------------

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

        public bool Full => Total >= Slots;

        /// <summary>How much more will fit. Never negative, even if the cap was lowered.</summary>
        public int Room
        {
            get
            {
                var left = Slots - Total;
                return left < 0 ? 0 : left;
            }
        }

        public int CountOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;

            int n;
            return Bag().TryGetValue(id, out n) ? n : 0;
        }

        /// <summary>What is in it, oldest first. Never null.</summary>
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

        // ---- changing -------------------------------------------------------

        public bool Add(string id, int howMany = 1)
        {
            if (string.IsNullOrEmpty(id) || howMany < 1) return false;

            var bag = Bag();

            if (Total + howMany > Slots) return false;

            int have;
            bag.TryGetValue(id, out have);
            bag[id] = have + howMany;

            var order = Order();
            if (!order.Contains(id)) order.Add(id);

            _dirty = true;
            return true;
        }

        public bool Take(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            var bag = Bag();

            int have;
            if (!bag.TryGetValue(id, out have) || have < 1) return false;

            if (have <= 1)
            {
                bag.Remove(id);
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

        // ---- the clock ------------------------------------------------------

        public void Update(float realSeconds)
        {
            var now = Needs.Needs.Who();

            if (!string.Equals(now, _who, StringComparison.OrdinalIgnoreCase))
            {
                _who = now;
            }

            if (!_dirty) return;

            _sinceSave += realSeconds;
            if (_sinceSave < 10f) return;

            SaveNow();
        }

        // ---- whose ----------------------------------------------------------

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

        // ---- the file -------------------------------------------------------

        /// <summary>
        /// Called by the subclass's constructor, AFTER the catalogue exists.
        ///
        /// Not from this constructor, because the load drops anything foods.json no longer
        /// defines and it cannot know that until the list has been read.
        /// </summary>
        protected void Load()
        {
            try
            {
                var doc = JsonFile.Read(File);
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

                        if (Menu.Find(id) == null)
                        {
                            Log.Info(What + ": " + who + " had " + n + " x " + id +
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
                Log.Error("Could not read " + File + " - starting " + What.ToLowerInvariant() +
                          " empty.", ex);
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

                if (!JsonFile.Write(File, doc))
                {
                    Log.Once(Key + "-save", "Could not write " + File +
                                            " - what is in it will not survive this session.");
                }
            }
            catch (Exception ex)
            {
                Log.Once(Key + "-save", "Could not save the " + What.ToLowerInvariant() +
                                        ": " + ex.Message);
            }
        }
    }
}
