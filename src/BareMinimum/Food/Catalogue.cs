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
        /// How drunk one of these makes you, 0 to 1.
        ///
        /// Cumulative: the meter it feeds is capped at 1, so a fourth beer does less than the
        /// first. That is the intended shape -- keep buying and you get there, but you cannot
        /// leap straight to hammered on one bottle.
        /// </summary>
        public float Booze;

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
        /// Which product shape represents it in a shop: "can", "burger", "pack" and so on.
        ///
        /// A SHAPE NAME, not a file per item. Eighteen shapes serve thirty-odd items because a
        /// can is a can whether it holds eCola or Sprunk; the tint and the name do the rest.
        /// See tools/make_products.py.
        /// </summary>
        public string Icon = "";

        /// <summary>What colour to draw that shape. The art is white; this is the product.</summary>
        public System.Drawing.Color Tint = System.Drawing.Color.FromArgb(255, 235, 235, 240);

        /// <summary>
        /// The line the shop shows about it. Where the character lives.
        ///
        /// Somebody reading a list of eight snacks wants a reason to pick one, and a hunger
        /// figure is not a reason. Written per item in foods.json.
        /// </summary>
        public string Desc = "";

        /// <summary>Something you smoke rather than eat or drink. Cigarettes.</summary>
        public bool Smoke;

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

        /// <summary>
        /// This item's own eating animation, or null to use the menu's.
        ///
        /// PER ITEM BECAUSE A BURGER AND A HOT DOG ARE NOT EATEN THE SAME WAY. Until now all
        /// hundred and fifty-odd foods played the one burger loop, which is right for a burger
        /// and merely tolerable for everything else -- and with a burger cart standing next to
        /// a hot dog cart the two carts would have sold visibly the same meal.
        ///
        /// NULL RATHER THAN A COPY OF THE DEFAULT, so that changing the global animation in
        /// foods.json still moves every item that has not asked for something specific. A
        /// per-item copy would quietly stop tracking it.
        /// </summary>
        public AnimRef Eat;

        /// <summary>False once the prop is known not to exist in this build, so it is tried once.</summary>
        public bool PropUsable = true;
    }

    /// <summary>A movement of animation dictionary and clip, as read from the file.</summary>
    internal sealed class AnimRef
    {
        public string Dict = "";
        public string Clip = "";

        /// <summary>
        /// Candidate dict/clip pairs, tried in order. The first whose DICT EXISTS wins.
        ///
        /// THE SAME TREATMENT THE MODEL LISTS GET, and for the same reason. An animation
        /// dictionary that is not in this build never loads, so HAS_ANIM_DICT_LOADED stays
        /// false forever and the mod silently plays nothing -- which is exactly how the
        /// smoking animation shipped broken: one guessed name, no alternative, and a single
        /// log line nobody was looking for.
        ///
        /// A clip inside a real dict still cannot be checked. But a wrong dict is most of the
        /// risk, and naming several means one wrong guess costs nothing.
        /// </summary>
        public string[][] Options = new string[0][];

        /// <summary>
        /// A scenario to run instead when no dictionary works at all.
        ///
        /// The floor under the whole thing. Scenarios are not asset names that might be
        /// missing -- WORLD_HUMAN_SMOKING is the same one the hot dog man uses on his break,
        /// and that has been visibly working since the day it went in. Something imperfect
        /// beats a man standing still holding a cigarette.
        /// </summary>
        public string Scenario = "";

        /// <summary>Set once the options have been checked against this build.</summary>
        public bool Resolved;

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

        /// <summary>Whether there is anything at all to play, anim or scenario.</summary>
        public bool Usable => Valid || !string.IsNullOrEmpty(Scenario);

        /// <summary>
        /// Picks the first candidate whose dictionary exists here, and names the misses.
        ///
        /// Runs once, on first use rather than at load: DOES_ANIM_DICT_EXIST is a native and
        /// the catalogue is built before the game is necessarily ready to answer.
        /// </summary>
        public void Resolve(string what)
        {
            if (Resolved) return;
            Resolved = true;

            if (Options.Length == 0) return;

            var missing = new System.Collections.Generic.List<string>();

            foreach (var pair in Options)
            {
                if (pair == null || pair.Length < 2) continue;

                try
                {
                    if (Function.Call<bool>(Hash.DOES_ANIM_DICT_EXIST, pair[0]))
                    {
                        Dict = pair[0];
                        Clip = pair[1];

                        if (missing.Count > 0)
                        {
                            Log.Info(what + ": not in this build, skipped - " +
                                     string.Join(", ", missing.ToArray()));
                        }

                        Log.Info(what + ": using " + Dict + " / " + Clip + ".");
                        return;
                    }
                }
                catch
                {
                    // Treated as missing, same as a name that is simply not there.
                }

                missing.Add(pair[0]);
            }

            Dict = "";
            Clip = "";

            Log.Warn(what + ": none of " + missing.Count + " animation dictionar(ies) exist - " +
                     string.Join(", ", missing.ToArray()) +
                     (string.IsNullOrEmpty(Scenario)
                          ? ". Nothing will play."
                          : ". Falling back to the " + Scenario + " scenario."));
        }
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

        /// <summary>
        /// Ambient speech names, by occasion. Read from foods.json, handed to Speech.
        ///
        /// HERE RATHER THAN IN Speech BECAUSE THIS IS THE FILE READER. Every other piece of
        /// content in the mod comes out of a json through the class that owns that json, and
        /// a second class opening the same file to pick two keys out of it would be two
        /// things to keep in step for no gain.
        /// </summary>
        public readonly System.Collections.Generic.Dictionary<string, string[]> Lines =
            new System.Collections.Generic.Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Smoking. The street smoker's ambient set, not an MP interaction.
        ///
        /// A separate entry rather than reusing the drink loop, because the two look nothing
        /// alike and a cigarette drunk like a bottle would be worse than no animation at all.
        ///
        /// THE DEFAULT USED TO NAME mp_player_intsmoke, WHICH DOES NOT EXIST. There is an
        /// inteat and an intdrink in that family and no intsmoke, so the one name written here
        /// was a name nobody could have checked without the game in front of them. It never
        /// mattered while foods.json listed alternatives -- but this is what runs when that
        /// file is missing, which is the one time nothing else can cover for it.
        ///
        /// THE idle_a CLIP, NOT base. In an ambient set base is the standing-still pose and
        /// the idle clips are the movement, so base is a man holding a cigarette and never
        /// raising it. Hoodrich paid for this lesson on its joint; the note is in Highs.cs.
        /// </summary>
        public readonly AnimRef Smoke = new AnimRef
        {
            Dict = "amb@world_human_smoking@male@male_a@idle_a",
            Clip = "idle_a",
            LeftHanded = false
        };

        public IList<Item> Items => _items;

        /// <summary>
        /// One item by id, or null.
        ///
        /// Linear, and that is fine: the catalogue is a hundred-odd entries and this is called
        /// when a menu opens or a pocket is drawn, not per frame per row. A dictionary here
        /// would be a second copy of the list to keep in step for no measurable gain.
        /// </summary>
        public Item Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (var i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return _items[i];
            }

            return null;
        }
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
                        Booze = node["booze"].AsFloat(0f),
                        InShop = node["shop"].AsBool(true),
                        Icon = node["icon"].AsString(""),
                        Tint = Colour(node["tint"].AsString("")),
                        Desc = node["desc"].AsString(""),
                        Smoke = node["smoke"].AsBool(false),
                        Props = PropNames(node["prop"]),
                        Seconds = node["seconds"].AsFloat(4f),
                        VehicleSeconds = node["vehicleSeconds"].AsFloat(0f),
                        Combo = node["combo"].AsBool(false),
                        DrinkProps = PropNames(node["drinkProp"]),
                        Eat = ItemAnim(node["anim"])
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

                Lines.Clear();

                var speech = doc["speech"];

                foreach (var key in speech.Keys)
                {
                    // _comment is documentation, not a set of things to say. Every json in
                    // this mod carries its own explanation inside itself, and a reader that
                    // does not skip those keys loads the manual as content.
                    if (key.StartsWith("_")) continue;

                    var node = speech[key];
                    var list = new System.Collections.Generic.List<string>();

                    for (var i = 0; i < node.Count; i++)
                    {
                        var name = node[i].AsString("");
                        if (name.Length > 0) list.Add(name);
                    }

                    if (list.Count > 0) Lines[key] = list.ToArray();
                }
                ReadAnim(doc["animations"]["smoke"], Smoke);

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
        /// <summary>
        /// Reads a "#rrggbb" tint, falling back to near-white.
        ///
        /// Hand-parsed rather than via ColorTranslator: that lives in System.Drawing's
        /// Windows-only half, and this assembly references System.Drawing for Color and
        /// nothing else. Six hex digits is not worth the risk of a type that may not resolve.
        /// </summary>
        private static System.Drawing.Color Colour(string hex)
        {
            var fallback = System.Drawing.Color.FromArgb(255, 235, 235, 240);

            if (string.IsNullOrEmpty(hex)) return fallback;

            hex = hex.TrimStart('#').Trim();
            if (hex.Length != 6) return fallback;

            try
            {
                var r = Convert.ToInt32(hex.Substring(0, 2), 16);
                var g = Convert.ToInt32(hex.Substring(2, 2), 16);
                var b = Convert.ToInt32(hex.Substring(4, 2), 16);

                return System.Drawing.Color.FromArgb(255, r, g, b);
            }
            catch
            {
                return fallback;
            }
        }

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

            var scenario = node["scenario"].AsString("");
            if (!string.IsNullOrEmpty(scenario)) into.Scenario = scenario;

            // A list of candidates, if the file gives one. The single dict/clip above stays
            // supported because two of the three animations have never needed alternatives.
            var options = node["options"];
            var list = new System.Collections.Generic.List<string[]>();

            for (var i = 0; i < options.Count; i++)
            {
                var d = options[i]["dict"].AsString("");
                var c = options[i]["clip"].AsString("");

                if (d.Length > 0 && c.Length > 0) list.Add(new[] { d, c });
            }

            if (list.Count > 0)
            {
                into.Options = list.ToArray();
                into.Resolved = false;
            }
            else if (!string.IsNullOrEmpty(into.Dict))
            {
                // One named pair is a list of one, so everything downstream has a single path.
                into.Options = new[] { new[] { into.Dict, into.Clip } };
                into.Resolved = false;
            }

            var hand = node["hand"].AsString("");
            if (!string.IsNullOrEmpty(hand))
            {
                into.LeftHanded = !hand.Trim().StartsWith("r", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// One item's own animation block, or null when it has not got one.
        ///
        /// NOT SEEDED FROM THE GLOBAL EAT. A half-written override that inherited the burger
        /// dictionary and replaced only the clip would name a clip that is not in that
        /// dictionary, and GTA plays a missing clip as nothing at all, in silence -- the same
        /// failure every other wrong name in this game has. So an override either says what it
        /// is in full or it is not an override, and ReadAnim's own "one pair is a list of one"
        /// rule is what decides that: no dict, no options, no override.
        /// </summary>
        private static AnimRef ItemAnim(Json node)
        {
            if (node == null || node.IsNull) return null;

            var anim = new AnimRef();
            ReadAnim(node, anim);

            return anim.Options.Length > 0 ? anim : null;
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
                                  Props = new[] { "p_amb_coffeecup_01" } });

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
