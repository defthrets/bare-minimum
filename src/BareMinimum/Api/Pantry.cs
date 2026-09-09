using System;
using System.Collections.Generic;

namespace BareMinimum.Api
{
    /// <summary>
    /// The bridge. Everything another mod is allowed to touch, and nothing else.
    ///
    /// This exists so Hoodrich's phone can show what you are carrying and let you eat it from
    /// there, without either mod referencing the other.
    ///
    /// ONLY BCL TYPES CROSS THIS BOUNDARY -- string, int, bool, and arrays of those. That is
    /// not stylistic, it is the whole reason the bridge works. The other side calls in by
    /// REFLECTION, so it has no reference to this assembly and cannot name any type declared
    /// in it; hand back an Item and the caller gets an object it can only poke at with more
    /// reflection. mscorlib is the one assembly both mods are guaranteed to agree about, so
    /// mscorlib is the vocabulary.
    ///
    /// WHY REFLECTION AND NOT A SHARED INTEROP DLL. A GTA scripts\ folder is ONE assembly
    /// resolution namespace. Two mods that both reference a third assembly must agree about
    /// its exact version forever, and when they stop agreeing the failure is a TypeLoadException
    /// at load with no log -- because the thing that would have written the log is the thing
    /// that did not load. Precinct 88 and Hoodrich already talk this way for exactly this
    /// reason, and this is deliberately the same shape so there is one pattern on this machine
    /// rather than two.
    ///
    /// EVERY METHOD SWALLOWS ITS OWN EXCEPTIONS AND RETURNS SOMETHING SENSIBLE. An exception
    /// thrown across a reflection call surfaces at the other end as a TargetInvocationException
    /// wrapping something the caller has no type for -- a throw here is a crash in a mod whose
    /// author cannot read the stack trace. Nothing thrown leaves this file.
    ///
    /// IT IS ALSO SAFE BEFORE THE HOST HAS WIRED ITSELF IN. SHVDN builds scripts in whatever
    /// order it finds them, so the other mod can reach this class before Bare Minimum has
    /// constructed a pantry. Every method answers "nothing" rather than throwing, and the
    /// caller is expected to keep asking -- see Ready.
    /// </summary>
    public static class Pantry
    {
        /// <summary>
        /// The contract version. Bumped when a signature here changes in a way that breaks.
        ///
        /// Read by the caller BEFORE anything else, and this is the reason the whole bridge is
        /// safe to change later: an old Hoodrich talking to a new Bare Minimum checks this
        /// number, does not like it, and quietly shows no food rather than half-calling an API
        /// that has moved.
        /// </summary>
        public static int ApiVersion => 1;

        /// <summary>The mod's version string, for the other side's log.</summary>
        public static string Version
        {
            get { try { return Core.Build.Version; } catch { return "?"; } }
        }

        // ---- what the host wires in ------------------------------------------

        private static Food.Pantry _bag;
        private static Food.Catalogue _menu;
        private static Food.Eating _eating;

        /// <summary>The meters, for Drain. Nothing else out here reads them.</summary>
        private static Needs.Needs _needs;

        /// <summary>
        /// Called by Main once the real objects exist. Not part of the public contract -- the
        /// other side never calls this, it only ever reads.
        /// </summary>
        internal static void Wire(Food.Pantry bag, Food.Catalogue menu, Food.Eating eating,
                                  Needs.Needs needs)
        {
            _bag = bag;
            _menu = menu;
            _eating = eating;
            _needs = needs;
        }

        /// <summary>
        /// Whether there is anything behind this yet.
        ///
        /// The caller polls this rather than resolving once, because load order is not
        /// guaranteed and a bridge that gave up on the first frame would be permanently absent
        /// on about half of all launches -- which is the worst shape a bug can have.
        /// </summary>
        public static bool Ready => _bag != null && _menu != null;

        // ---- reading the pocket ----------------------------------------------

        /// <summary>Ids currently carried, oldest first. Never null.</summary>
        public static string[] Ids()
        {
            try
            {
                if (_bag == null) return new string[0];

                var ids = _bag.Ids();
                return ids == null ? new string[0] : ids.ToArray();
            }
            catch { return new string[0]; }
        }

        /// <summary>How many of one thing. 0 for anything not carried.</summary>
        public static int CountOf(string id)
        {
            try { return _bag == null ? 0 : _bag.CountOf(id); }
            catch { return 0; }
        }

        /// <summary>Items carried in total, all kinds together.</summary>
        public static int Total
        {
            get { try { return _bag == null ? 0 : _bag.Total; } catch { return 0; } }
        }

        /// <summary>How many fit.</summary>
        public static int Slots
        {
            get { try { return _bag == null ? 0 : _bag.Slots; } catch { return 0; } }
        }

        // ---- describing an item ----------------------------------------------

        public static string NameOf(string id)
        {
            try
            {
                var item = _menu == null ? null : _menu.Find(id);
                return item == null ? "" : item.Name;
            }
            catch { return ""; }
        }

        public static string DescOf(string id)
        {
            try
            {
                var item = _menu == null ? null : _menu.Find(id);
                return item == null ? "" : item.Desc;
            }
            catch { return ""; }
        }

        /// <summary>
        /// The FULL PATH of this item's picture, or "" when there is not one.
        ///
        /// A path rather than a bare name, because the caller's own icons live in its own
        /// folder and it has no reason to know where ours are. Both mods draw PNGs the same
        /// way, so a path is all the other side needs.
        /// </summary>
        public static string IconOf(string id)
        {
            try
            {
                var item = _menu == null ? null : _menu.Find(id);
                if (item == null || string.IsNullOrEmpty(item.Icon)) return "";

                var file = System.IO.Path.Combine(Core.Paths.Icons, "p_" + item.Icon + ".png");

                return System.IO.File.Exists(file) ? file : "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// The mod's own mark, as a full path: "food" for the drumstick, "moon" for the
        /// crescent. "" if it is not there.
        ///
        /// ADDED WITHOUT BUMPING ApiVersion, which is safe in this one direction: a caller
        /// written against v1 never asks for a method it does not know about, and a caller
        /// that does ask is by definition newer than the surface it is asking of. Removing or
        /// re-signing anything above is what the version number is for.
        /// </summary>
        public static string Mark(string which)
        {
            try
            {
                var file = string.Equals(which, "moon", StringComparison.OrdinalIgnoreCase)
                    ? "moon0.png"
                    : "food0.png";

                var path = System.IO.Path.Combine(Core.Paths.Icons, file);

                return System.IO.File.Exists(path) ? path : "";
            }
            catch { return ""; }
        }

        /// <summary>The item's tint, packed ARGB. Colour cannot cross, an int can.</summary>
        public static int TintOf(string id)
        {
            try
            {
                var item = _menu == null ? null : _menu.Find(id);
                if (item == null) return unchecked((int)0xFFEBEBF0);

                return item.Tint.ToArgb();
            }
            catch { return unchecked((int)0xFFEBEBF0); }
        }

        /// <summary>
        /// EVERYTHING A SHOP WOULD SELL, so another mod can offer the food rather than
        /// only hand it over.
        ///
        /// Ids alone. The name, the picture, the tint, what it does and what it costs are
        /// each their own call above, so a caller takes what it needs and nothing here has
        /// to agree with the other side about the shape of a struct -- which across an
        /// assembly boundary is the thing that breaks.
        ///
        /// InShop only. The catalogue carries things a counter does not sell -- what a
        /// mission hands you, what a fridge starts with -- and a delivery menu with those
        /// on it would be offering food that has no price.
        ///
        /// ADDED WITHOUT BUMPING ApiVersion, for the reason Mark gives: a caller written
        /// against the old surface never asks for a method it does not know about.
        /// </summary>
        public static string[] Menu()
        {
            try
            {
                if (_menu == null) return new string[0];

                var ids = new System.Collections.Generic.List<string>();

                foreach (var item in _menu.Items)
                {
                    if (item == null || !item.InShop || string.IsNullOrEmpty(item.Id)) continue;
                    ids.Add(item.Id);
                }

                return ids.ToArray();
            }
            catch { return new string[0]; }
        }

        /// <summary>What one costs over the counter. Nought for anything not in the catalogue.</summary>
        public static int PriceOf(string id)
        {
            try
            {
                var item = _menu == null ? null : _menu.Find(id);
                return item == null ? 0 : item.Price;
            }
            catch { return 0; }
        }

        /// <summary>What it is: "Food", "Drinks" or "Snacks".</summary>
        public static string CategoryOf(string id)
        {
            try
            {
                var item = _menu == null ? null : _menu.Find(id);
                return item == null ? "" : item.Category;
            }
            catch { return ""; }
        }

        // ---- doing something with it -----------------------------------------

        /// <summary>
        /// Eats, drinks or smokes one. True when it actually started.
        ///
        /// TAKEN FIRST AND PUT BACK ON REFUSAL, exactly as the mod's own pocket screen does.
        /// Eating a thing that stayed in your pocket is a duplication bug and losing one to a
        /// refused animation is a theft bug; this has neither, and the caller does not have to
        /// know that it needed thinking about.
        /// </summary>
        public static bool Consume(string id)
        {
            try
            {
                if (_bag == null || _menu == null || _eating == null) return false;
                if (_eating.Busy) return false;

                var item = _menu.Find(id);
                if (item == null) return false;

                if (!_bag.Take(id)) return false;

                if (_eating.Begin(item)) return true;

                // BACK, not added: the cap can have been lowered under what he is carrying
                // since the take, and a refused put-back destroys the item. See Store.Return.
                _bag.Return(id);
                return false;
            }
            catch { return false; }
        }

        // ======================================================================
        // Time that was not rest
        // ======================================================================

        /// <summary>
        /// The next big jump in the clock was NOT sleep.
        ///
        /// WHY ANOTHER MOD NEEDS TO BE ABLE TO SAY THIS. This one watches the game clock and
        /// treats a jump it did not cause as somebody having slept -- which is right nearly
        /// every time, because the things that move the clock in hours are beds. It is wrong
        /// exactly when the hours were not restful, and the mod that moved them is the only
        /// thing in the world that knows which it was.
        ///
        /// The case that found it: Posted Up knocks you out on an overdose and skips six
        /// hours. Waking up hungry from that is correct and waking up FULLY RESTED is not --
        /// passing out on four drugs was quietly the most efficient way to sleep in the game.
        ///
        /// A LATCH RATHER THAN AN ARGUMENT, because the caller does not know how many hours
        /// this mod will decide it saw, and should not have to. Set it, move the clock, and
        /// the jump that follows is counted as time spent awake -- which drains, rather than
        /// as time spent asleep, which fills.
        ///
        /// Consumed once. A latch that stayed set would make every later bed useless.
        /// </summary>
        public static void NotSleep()
        {
            Needs.Needs.NotSleepNext = true;
        }

        /// <summary>
        /// Take some hunger and some sleep off him. Fractions of the whole meter, 0 to 1.
        ///
        /// FOR THINGS THAT HAPPEN TO A BODY. NotSleep above says "those hours were not rest",
        /// which stops a jump crediting a night -- it does not say the hours were BAD for him,
        /// and some of them are. Six hours face down in a gutter after an overdose is not
        /// neutral time: you come round wrecked, and the ordinary drain for six hours on an
        /// eighty-hour meter is seven per cent, which is not wrecked, it is Tuesday afternoon.
        ///
        /// SUBTRACTED RATHER THAN SET, so two things can do it in the same minute and the
        /// second does not undo the first. Clamped at zero, and the caller is trusted to pick
        /// numbers that leave him standing -- an empty hunger meter in this mod takes health,
        /// so a caller that drains a full one to nothing has decided to hurt him.
        /// </summary>
        public static void Drain(float hunger, float sleep)
        {
            try
            {
                if (_needs == null) return;

                _needs.Drain(hunger, sleep);
            }
            catch
            {
            }
        }

        /// <summary>Puts one in, if there is room. For a mod that wants to GIVE you food.</summary>
        public static bool Give(string id, int howMany = 1)
        {
            try
            {
                if (_bag == null || _menu == null) return false;
                if (_menu.Find(id) == null) return false;

                return _bag.Add(id, howMany);
            }
            catch { return false; }
        }

        /// <summary>
        /// Takes some out WITHOUT eating them. For a mod that gives you somewhere to put food
        /// down -- Posted Up's car boot is the one that asked. False, and nothing moved, when
        /// he has not got that many.
        ///
        /// ADDED WITHOUT BUMPING ApiVersion, the same way Mark was: a caller that asks for
        /// this is newer than the surface, and an older caller never asks.
        /// </summary>
        public static bool Take(string id, int howMany = 1)
        {
            try
            {
                if (_bag == null || howMany < 1) return false;
                if (_bag.CountOf(id) < howMany) return false;

                var taken = 0;
                for (; taken < howMany; taken++)
                {
                    if (!_bag.Take(id)) break;
                }

                if (taken == howMany) return true;

                // Short. Back in, so the count is what it was before the ask.
                if (taken > 0) _bag.Add(id, taken);
                return false;
            }
            catch { return false; }
        }
    }
}
