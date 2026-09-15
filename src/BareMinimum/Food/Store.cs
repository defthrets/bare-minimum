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

        /// <summary>
        /// How many uses are left in the OPEN one, per character, per item. See Use.
        ///
        /// ONLY THE ONE BEING USED IS IN HERE. An unopened pack is not listed at all and is
        /// worth its item's full Uses -- so this file stays empty for everybody who never
        /// smokes, and an item whose Uses changes in foods.json does not strand a saved
        /// number that no longer means anything.
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, int>> _lefts =
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

        /// <summary>
        /// Places taken by something this store does not itself hold.
        ///
        /// THE POCKET HAS DRUGS IN IT THAT BELONG TO ANOTHER MOD. They are drawn in the same
        /// grid as the food, one tile a kind, and until now they cost nothing: the screen
        /// showed five tiles over a header reading "2 of 5", and you could fill all five food
        /// slots on top of them. A pocket with eight things in it is not a pocket.
        ///
        /// COUNTED HERE RATHER THAN SUBTRACTED FROM Slots, because Slots is the player's own
        /// setting and a store that quietly reports a smaller cap than the one they typed is
        /// a store nobody can reason about. The cap is what they set; this is what is already
        /// in the way.
        ///
        /// Return DELIBERATELY IGNORES ALL OF IT, as it always has. Putting back something
        /// this mod took out of your hand must never fail, or a pocket that filled up with
        /// drugs mid-meal would eat the sandwich.
        /// </summary>
        protected virtual int Reserved => 0;

        /// <summary>Places used: what is in it, plus what is in the way.</summary>
        public int Taken => Total + Reserved;

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

        public bool Full => Taken >= Slots;

        /// <summary>How much more will fit. Never negative, even if the cap was lowered.</summary>
        public int Room
        {
            get
            {
                var left = Slots - Taken;
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

        /// <summary>
        /// Puts something BACK that this bag just handed out, past the cap if it has to.
        ///
        /// THE CAP IS READ LIVE AND CAN BE LOWERED UNDER WHAT YOU ARE ALREADY CARRYING, which
        /// makes the take-then-put-back pattern the shops and the pocket use unsafe: the take
        /// works, the meal is refused, and the put-back is turned away by a cap that was not in
        /// the way a moment ago. The item is then simply gone. Returning something to where it
        /// came from is not the same act as acquiring it, and it is never refused.
        /// </summary>
        public void Return(string id, int howMany = 1)
        {
            if (string.IsNullOrEmpty(id) || howMany < 1) return;

            var bag = Bag();

            int have;
            bag.TryGetValue(id, out have);
            bag[id] = have + howMany;

            _dirty = true;
        }

        public bool Add(string id, int howMany = 1)
        {
            if (string.IsNullOrEmpty(id) || howMany < 1) return false;

            var bag = Bag();

            if (Taken + howMany > Slots) return false;

            int have;
            bag.TryGetValue(id, out have);
            bag[id] = have + howMany;

            var order = Order();
            if (!order.Contains(id)) order.Add(id);

            _dirty = true;
            return true;
        }

        /// <summary>How many uses are left in the one he would use next. 0 if he has none.</summary>
        public int LeftOf(string id)
        {
            if (CountOf(id) < 1) return 0;

            var uses = UsesOf(id);
            if (uses <= 1) return CountOf(id);

            int left;
            return Left().TryGetValue(id, out left) ? left : uses;
        }

        /// <summary>What the catalogue says one of these is good for. 1 when it says nothing.</summary>
        private int UsesOf(string id)
        {
            try
            {
                var item = Menu == null ? null : Menu.Find(id);
                return item == null || item.Uses < 1 ? 1 : item.Uses;
            }
            catch
            {
                return 1;
            }
        }

        private Dictionary<string, int> Left()
        {
            Dictionary<string, int> left;
            if (_lefts.TryGetValue(_who, out left)) return left;

            left = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _lefts[_who] = left;
            return left;
        }

        /// <summary>
        /// CONSUMES ONE USE. The one call for eating, drinking or smoking something.
        ///
        /// NOT Take, AND THAT IS THE WHOLE POINT OF IT BEING A SEPARATE METHOD. Take is a
        /// MOVE -- it is what carrying a packet from the pocket to the fridge calls, and
        /// what putting it back calls -- and charging a cigarette for walking one to the
        /// kitchen would be absurd. Every eat path calls this; every move path still calls
        /// Take, and neither has to know about the other.
        ///
        /// For anything whose Uses is 1, which is everything but the packets, this IS Take.
        /// </summary>
        public bool Use(string id)
        {
            var uses = UsesOf(id);

            if (uses <= 1) return Take(id);

            if (CountOf(id) < 1) return false;

            var left = Left();

            int have;
            if (!left.TryGetValue(id, out have) || have < 1 || have > uses) have = uses;

            have--;

            if (have > 0)
            {
                left[id] = have;
                _dirty = true;

                _undoId = id;
                _undoTook = false;
                return true;
            }

            // The last one out of the packet: the packet goes with it.
            left.Remove(id);

            var went = Take(id);

            // WHICH OF THE TWO THINGS THIS DID, for Unuse. Without it, undoing cannot tell a
            // cigarette coming off the count from a packet leaving the pocket -- and it
            // guesses wrong in both directions: a refused light on the last cigarette of a
            // spare packet hands back a FULL one, and on the only packet hands back twenty.
            // Set beside the call that did it rather than worked out afterwards.
            _undoId = went ? id : null;
            _undoTook = went;

            return went;
        }

        private string _undoId;
        private bool _undoTook;

        /// <summary>
        /// Puts back exactly what Use took, for when the thing it was taken for refused.
        ///
        /// THE MIRROR OF Use AND NOT OF Take. If the packet is still in the pocket then a use
        /// came off the count and the count is what goes back; if the last one emptied it,
        /// the packet itself comes back -- through Return, which ignores the cap, because a
        /// refused put-back destroys what the player was holding. See Return.
        /// </summary>
        public void Unuse(string id)
        {
            var uses = UsesOf(id);

            if (uses <= 1) { Return(id); return; }

            var left = Left();

            // THE PACKET ITSELF WENT, so the packet comes back -- with ONE use in it, which
            // is the one that emptied it. Not a full packet: that is the bug this flag
            // exists to stop, and it is worth a whole cigarette every time an animation
            // refuses. Read from the matching Use rather than inferred from the count, which
            // cannot tell "the last one of my only packet" from "the last one of two".
            var tookThePacket = _undoTook &&
                                string.Equals(_undoId, id, StringComparison.OrdinalIgnoreCase);

            _undoId = null;
            _undoTook = false;

            if (tookThePacket)
            {
                // Return, not Add: the cap can refuse an Add and the packet would be gone.
                Return(id);
                left[id] = 1;
                _dirty = true;
                return;
            }

            int have;
            if (!left.TryGetValue(id, out have)) have = 0;

            have++;

            if (have >= uses) left.Remove(id);
            else left[id] = have;

            _dirty = true;
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
                var doc = _guard.Read(File);
                if (doc == null || doc.IsNull) return;

                var version = doc["version"].AsInt(1);

                var people = doc["characters"];
                if (people == null || people.IsNull) return;

                // The version-1 protagonist names were wrong in this file too -- it is keyed
                // by exactly the same Who() that was mislabelling them. Same plan, so a bag
                // and a stomach cannot end up disagreeing about whose they are.
                var plan = version < Needs.Needs.StateVersion
                    ? Needs.Needs.RenamePlan(new List<string>(people.Keys))
                    : null;

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

                    var key = who;

                    if (plan != null)
                    {
                        string renamed;
                        if (!plan.TryGetValue(who, out renamed)) continue;   // dropped by the plan

                        if (!string.Equals(renamed, who, StringComparison.OrdinalIgnoreCase))
                        {
                            Log.Info(What + ": \"" + who + "\" was really " + renamed +
                                     " - renamed.");
                        }

                        key = renamed;
                    }

                    _bags[key] = bag;
                    _order[key] = order;
                }

                // AND WHAT IS LEFT IN THE OPEN PACKETS. Read after the pockets and through
                // the same rename plan, so a saved packet and the pocket holding it cannot
                // end up filed under two different spellings of the same man.
                //
                // ABSENT IS NORMAL, not a fault: this node is only written once somebody has
                // part-smoked something, and every save made before packets existed has no
                // node at all. A missing count means a full one.
                var opened = doc["opened"];

                if (opened != null && !opened.IsNull)
                {
                    foreach (var who in opened.Keys)
                    {
                        var node = opened[who];
                        if (node == null || node.IsNull) continue;

                        var key = who;

                        if (plan != null)
                        {
                            string renamed;
                            if (!plan.TryGetValue(who, out renamed)) continue;

                            key = renamed;
                        }

                        var left = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                        foreach (var id in node.Keys)
                        {
                            var n = node[id].AsInt(0);
                            if (n > 0) left[id] = n;
                        }

                        if (left.Count > 0) _lefts[key] = left;
                    }
                }

                // So the corrected names reach the file rather than only this session.
                if (plan != null) _dirty = true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not read " + File + " - starting " + What.ToLowerInvariant() +
                          " empty.", ex);
            }
        }

        /// <summary>Whether the save on disk is safe to write over. See Core.SaveGuard.</summary>
        private readonly SaveGuard _guard = new SaveGuard("Pocket");

        public void SaveNow()
        {
            _sinceSave = 0f;

            // As the needs: everything you were carrying is in this file, and losing all three
            // characters' pockets to a locked file is worse than not saving for one session.
            if (!_guard.MayWrite) return;

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

                // WHAT IS LEFT IN THE OPEN PACKETS, beside the pockets rather than inside
                // them: the pocket node is a plain id -> count and half a dozen things read
                // it, so a second number per row would be a format change for the sake of a
                // feature only the packets use. Absent for anybody who has not opened one.
                var opened = Json.Object();

                foreach (var pair in _lefts)
                {
                    if (pair.Value == null || pair.Value.Count == 0) continue;

                    var node = Json.Object();
                    var any = false;

                    foreach (var one in pair.Value)
                    {
                        if (one.Value < 1) continue;

                        node.Set(one.Key, one.Value);
                        any = true;
                    }

                    if (any) opened.Set(pair.Key, node);
                }

                var doc = Json.Object()
                    .Set("version", Needs.Needs.StateVersion)
                    .Set("characters", people)
                    .Set("opened", opened);

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
