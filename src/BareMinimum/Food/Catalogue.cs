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

        /// <summary>
        /// The model held while consuming it: the one that was found to exist.
        ///
        /// Chosen from Props by CheckProps once the game is running. Empty means this build
        /// has none of them and the item is eaten empty-handed.
        /// </summary>
        public string Prop = "";

        /// <summary>
        /// Candidate models, in order of preference.
        ///
        /// A LIST rather than one name, because a prop name is the single easiest thing in
        /// this mod to get slightly wrong and the failure is invisible -- the food works, it
        /// just never appears in your hand, and nothing on screen says why. Listing the
        /// plausible spellings costs nothing: a name this build lacks simply never matches.
        /// </summary>
        public string[] Props = new string[0];

        public float Seconds = 4f;

        /// <summary>
        /// How long it takes IN A CAR. Zero means use Seconds.
        ///
        /// A meal eaten at the wheel is a different thing from one eaten standing at a window:
        /// you pick at it between junctions. A drive-through combo is set to about a minute,
        /// which is a drive across a few blocks rather than a four-second gulp.
        /// </summary>
        public float VehicleSeconds;

        /// <summary>
        /// Alternate eating and drinking, for anything that comes with a drink.
        ///
        /// A combo is a sandwich AND a soda, so playing one four-second eat loop for it says
        /// the wrong thing. With this the hand swaps between the food and the cup.
        /// </summary>
        public bool Combo;

        /// <summary>Candidate models for the drink half of a combo. See Props.</summary>
        public string[] DrinkProps = new string[0];

        /// <summary>The drink model that was found to exist, or empty.</summary>
        public string DrinkProp = "";

        /// <summary>False once the prop is known not to exist in this build, so it is tried once.</summary>
        public bool PropUsable = true;
    }

    /// <summary>A movement of animation dictionary and clip, as read from the file.</summary>
    internal sealed class AnimRef
    {
        public string Dict = "";
        public string Clip = "";

        /// <summary>
        /// Which hand this animation brings to the mouth. Left for the MP eat and drink sets.
        ///
        /// In the file rather than in code because it is a thing you can only learn by
        /// watching, and getting it wrong is not an error -- it is food riding along at hip
        /// height while the other hand mimes eating. Changing an animation here should not
        /// need a rebuild to put the prop back in the right hand.
        /// </summary>
        public bool LeftHanded = true;

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
                        Props = PropNames(node["prop"]),
                        Seconds = node["seconds"].AsFloat(4f),
                        VehicleSeconds = node["vehicleSeconds"].AsFloat(0f),
                        Combo = node["combo"].AsBool(false),
                        DrinkProps = PropNames(node["drinkProp"])
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

        /// <summary>
        /// Reads "prop" as EITHER a single name or a list of them.
        ///
        /// Both spellings are allowed so the file stays readable: most items want one name and
        /// should not have to be written as a one-element array to say so.
        /// </summary>
        private static string[] PropNames(Json node)
        {
            if (node == null || node.IsNull) return new string[0];

            var single = node.AsString("");
            if (!string.IsNullOrEmpty(single)) return new[] { single };

            var list = new List<string>();

            foreach (var item in node.Items)
            {
                var s = item.AsString("");
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }

            return list.ToArray();
        }

        private static void ReadAnim(Json node, AnimRef into)
        {
            if (node == null || node.IsNull) return;

            var dict = node["dict"].AsString("");
            var clip = node["clip"].AsString("");

            if (!string.IsNullOrEmpty(dict)) into.Dict = dict;
            if (!string.IsNullOrEmpty(clip)) into.Clip = clip;

            var hand = node["hand"].AsString("");
            if (!string.IsNullOrEmpty(hand))
            {
                into.LeftHanded = !hand.Trim().StartsWith("r", StringComparison.OrdinalIgnoreCase);
            }
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
                                  Price = 8, Hunger = 0.42f, Props = new[] { "prop_sandwich_01" } });
            _items.Add(new Item { Id = "crisps", Name = "Crisps", Category = "Snacks",
                                  Price = 3, Hunger = 0.14f, Props = new[] { "prop_food_bs_chips" } });
            _items.Add(new Item { Id = "ecola", Name = "eCola", Category = "Drinks",
                                  Price = 2, Hunger = 0.06f, Wake = 0.04f, Drink = true,
                                  Props = new[] { "prop_ecola_can" } });
            _items.Add(new Item { Id = "coffee", Name = "Coffee", Category = "Drinks",
                                  Price = 4, Hunger = 0.05f, Wake = 0.10f, Drink = true,
                                  Props = new[] { "prop_amb_coffeecup_01" } });

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
            var blank = new List<string>();

            foreach (var item in _items)
            {
                item.Prop = "";

                foreach (var name in item.Props)
                {
                    try
                    {
                        var model = new Model(name);

                        if (Function.Call<bool>(Hash.IS_MODEL_VALID, model.Hash))
                        {
                            item.Prop = name;
                            break;
                        }

                        missing.Add(name);
                    }
                    catch
                    {
                        missing.Add(name);
                    }
                }

                item.PropUsable = !string.IsNullOrEmpty(item.Prop);

                item.DrinkProp = "";

                foreach (var name in item.DrinkProps)
                {
                    try
                    {
                        var model = new Model(name);

                        if (Function.Call<bool>(Hash.IS_MODEL_VALID, model.Hash))
                        {
                            item.DrinkProp = name;
                            break;
                        }

                        missing.Add(name);
                    }
                    catch
                    {
                        missing.Add(name);
                    }
                }

                if (!item.PropUsable) blank.Add(item.Id);
            }

            if (missing.Count > 0)
            {
                // NAMED, not counted. These are the guesses most likely to be wrong, and a
                // name in somebody's log is the whole repair path.
                Log.Info("Catalogue: prop name(s) not in this build, skipped - " +
                         string.Join(", ", missing.ToArray()));
            }

            if (blank.Count == 0)
            {
                Log.Info("Catalogue: every item has a prop that exists in this build.");
                return;
            }

            Log.Warn("Catalogue: NO usable prop for - " + string.Join(", ", blank.ToArray()) +
                     ". Those are eaten empty-handed; add a model name that exists to foods.json.");
        }
    }
}
