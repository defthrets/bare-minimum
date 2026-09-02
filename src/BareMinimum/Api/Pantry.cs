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

        /// <summary>
        /// Called by Main once the real objects exist. Not part of the public contract -- the
        /// other side never calls this, it only ever reads.
        /// </summary>
        internal static void Wire(Food.Pantry bag, Food.Catalogue menu, Food.Eating eating)
        {
            _bag = bag;
            _menu = menu;
            _eating = eating;
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

                _bag.Add(id);
                return false;
            }
            catch { return false; }
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
    }
}
