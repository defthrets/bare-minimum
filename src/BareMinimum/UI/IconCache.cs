using System;
using System.Collections.Generic;

namespace BareMinimum.UI
{
    /// <summary>
    /// One Icon per file, for the whole session.
    ///
    /// THIS EXISTS BECAUSE AN Icon OWNS A TEXTURE. CustomSprite holds a handle to a texture
    /// loaded off disk, and the shop menu rebuilds its rows every time the selection moves --
    /// so building an Icon per row per refill would allocate a fresh texture handle several
    /// times a second and never let go of any of them. A shop with eight rows would leak
    /// eight handles a keypress.
    ///
    /// Rows therefore carry a FILE NAME and look the Icon up here. The cache is tiny -- there
    /// are eighteen product shapes and ten HUD frames -- and it is never invalidated, because
    /// the files do not change while the game is running.
    /// </summary>
    internal static class IconCache
    {
        private static readonly Dictionary<string, Icon> Held =
            new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The Icon for a file, creating it once. Null for an empty name.</summary>
        public static Icon Get(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;

            Icon icon;
            if (Held.TryGetValue(file, out icon)) return icon;

            icon = new Icon(file);
            Held[file] = icon;
            return icon;
        }

        /// <summary>How many are held. For the log, so a leak would be visible.</summary>
        public static int Count => Held.Count;
    }
}
