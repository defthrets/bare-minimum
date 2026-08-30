using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>One thing you can buy and consume.</summary>
    internal sealed class Item
    {
        public string Id = "";
        public string Name = "";
        public string Category = "Food";

        public int Price;
        public float Hunger;

        /// <summary>Caffeine. Capped in Needs.Wake so it can never replace sleeping.</summary>
        public float Wake;

        public bool Drink;

        /// <summary>
        /// Whether shop counters and vending machines stock it.
        ///
        /// False for anything a single named vendor sells. A branded burger belongs at that
        /// vendor's door, not on the shelf of every 24/7 in the state -- and without this the
        /// only way to keep it off them would be a category nobody could buy from, which is
        /// worse than a flag.
        /// </summary>
        public bool InShop = true;

        /// <summary>The model held while consuming it. May be absent or invalid; see Eating.</summary>
        public string Prop = "";

        public float Seconds = 4f;

        /// <summary>False once the prop is known not to exist in this build, so it is tried once.</summary>
        public bool PropUsable = true;
    }

    /// <summary>A movement of animation dictionary and clip, as read from the file.</summary>
    internal sealed class AnimRef
    {
        public string Dict = "";
        public string Clip = "";

        public bool Valid => !string.IsNullOrEmpty(Dict) && !string.IsNullOrEmpty(Clip);
    }

    /// <summary>
    /// Everything on sale, read from foods.json.
    ///
    /// DATA RATHER THAN CODE, and specifically because of the prop and animation names. Those
    /// are the parts of this mod most likely to be subtly wrong -- a model name that does not
    /// exist in this build fails silently, and the only way to fix a hardcoded one is a
    /// rebuild. In a file, anybody can correct it, and the log names the ones that failed.
    ///
    /// There is a built-in fallback list so a missing or broken file still leaves a working
    /// shop rather than an empty counter with no explanation.
    /// </summary>
    internal sealed class Catalogue
    {
        private readonly List<Item> _items = new List<Item>();
        private readonly List<string> _categories = new List<string>();

        public readonly AnimRef Eat = new AnimRef { Dict = "mp_player_inteat@burger",
                                                    Clip = "mp_player_int_eat_burger" };
        public readonly AnimRef Sip = new AnimRef { Dict = "mp_player_intdrink",
                                                    Clip = "loop_bottle" };

        public IList<Item> Items => _items;
        public IList<string> Categories => _categories;
        public int Count => _items.Count;

        public Catalogue(Core.Settings cfg)
        {
            Load(cfg);
        }

        /// <summary>Everything in one category, in file order.</summary>
        public List<Item> InCategory(string category)
        {
            var hit = new List<Item>();

            foreach (var item in _items)
            {
                if (string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
                {
                    hit.Add(item);
                }
            }

            return hit;
        }

        private void Load(Core.Settings cfg)
        {
            try
            {
                var doc = JsonFile.Read(Paths.FoodsFile, out var how);

                if (how != ReadResult.Ok || doc == null || doc.IsNull || doc["items"].Count == 0)
                {
                    Log.Warn("No usable " + Paths.FoodsFile + " - falling back to a built-in menu.");
                    Fallback();
                    return;
                }

                foreach (var node in doc["items"].Items)
                {
                    var item = new Item
                    {
                        Id = node["id"].AsString(""),
                        Name = node["name"].AsString(""),
                        Category = node["category"].AsString("Food"),
                        Price = node["price"].AsInt(0),
                        Hunger = node["hunger"].AsFloat(0f),
                        Wake = node["wake"].AsFloat(0f),
                        Drink = node["drink"].AsBool(false),
                        InShop = node["shop"].AsBool(true),
                        Prop = node["prop"].AsString(""),
                        Seconds = node["seconds"].AsFloat(4f)
                    };

                    if (string.IsNullOrEmpty(item.Name)) continue;

                    // A price of zero is legal (somebody may want a free-food game); a negative
                    // one is not, and would pay the player to eat.
                    if (item.Price < 0) item.Price = 0;

                    item.Price = (int)Math.Round(item.Price * cfg.PriceMultiplier);

                    _items.Add(item);
                }

                foreach (var c in doc["categories"].Items)
                {
                    var name = c.AsString("");
                    if (!string.IsNullOrEmpty(name)) _categories.Add(name);
                }

                ReadAnim(doc["animations"]["eat"], Eat);
                ReadAnim(doc["animations"]["drink"], Sip);

                if (_items.Count == 0)
                {
                    Log.Warn(Paths.FoodsFile + " has no usable items - falling back.");
                    Fallback();
                    return;
                }

                // Any category used by an item but not listed at the top would otherwise be a
                // tab that never appears, hiding those items completely.
                foreach (var item in _items)
                {
                    if (!_categories.Contains(item.Category)) _categories.Add(item.Category);
                }

                Log.Info("Catalogue: " + _items.Count + " item(s) across " +
                         _categories.Count + " categor(y/ies).");
            }
            catch (Exception ex)
            {
                Log.Error("Could not read " + Paths.FoodsFile + " - falling back to a built-in menu.", ex);
                _items.Clear();
                _categories.Clear();
                Fallback();
            }
        }

        private static void ReadAnim(Json node, AnimRef into)
        {
            if (node == null || node.IsNull) return;

            var dict = node["dict"].AsString("");
            var clip = node["clip"].AsString("");

            if (!string.IsNullOrEmpty(dict)) into.Dict = dict;
            if (!string.IsNullOrEmpty(clip)) into.Clip = clip;
        }

        /// <summary>
        /// Enough to run the shop with no data file at all.
        ///
        /// Not a token gesture: a player whose foods.json failed to deploy should still be able
        /// to buy a sandwich and a coffee, because the alternative is a counter that offers
        /// nothing and reads as a broken mod rather than a missing file.
        /// </summary>
        private void Fallback()
        {
            _items.Add(new Item { Id = "sandwich", Name = "Sandwich", Category = "Food",
                                  Price = 8, Hunger = 0.42f, Prop = "prop_sandwich_01" });
            _items.Add(new Item { Id = "crisps", Name = "Crisps", Category = "Snacks",
                                  Price = 3, Hunger = 0.14f, Prop = "prop_food_bs_chips" });
            _items.Add(new Item { Id = "ecola", Name = "eCola", Category = "Drinks",
                                  Price = 2, Hunger = 0.06f, Wake = 0.04f, Drink = true,
                                  Prop = "prop_ecola_can" });
            _items.Add(new Item { Id = "coffee", Name = "Coffee", Category = "Drinks",
                                  Price = 4, Hunger = 0.05f, Wake = 0.10f, Drink = true,
                                  Prop = "prop_amb_coffeecup_01" });

            _categories.Add("Food");
            _categories.Add("Snacks");
            _categories.Add("Drinks");
        }

        private bool _checked;

        /// <summary>
        /// Says, once, which item props this build actually has.
        ///
        /// Deferred until the game is running rather than done at load: a Model cannot be
        /// asked anything useful during script construction, because the world does not exist
        /// yet. Items whose prop is missing are marked so Eating never asks again, and they
        /// still work -- you simply eat empty-handed rather than not at all.
        /// </summary>
        public void CheckProps()
        {
            if (_checked) return;
            _checked = true;

            var missing = new List<string>();

            foreach (var item in _items)
            {
                if (string.IsNullOrEmpty(item.Prop)) { item.PropUsable = false; continue; }

                try
                {
                    var model = new Model(item.Prop);

                    if (Function.Call<bool>(Hash.IS_MODEL_VALID, model.Hash)) continue;

                    item.PropUsable = false;
                    missing.Add(item.Prop);
                }
                catch
                {
                    item.PropUsable = false;
                    missing.Add(item.Prop);
                }
            }

            if (missing.Count == 0)
            {
                Log.Info("Catalogue: every item prop exists in this build.");
                return;
            }

            // NAMED, not counted. These are the guesses most likely to be wrong, and a name in
            // somebody's log is the whole repair path -- the item still works without it.
            Log.Info("Catalogue: prop(s) not in this build, those items are eaten empty-handed - " +
                     string.Join(", ", missing.ToArray()));
        }
    }
}
