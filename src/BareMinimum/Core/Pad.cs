using System;
using GTA;

namespace BareMinimum.Core
{
    /// <summary>
    /// The controller, in the two shapes this mod needs it.
    ///
    /// SEPARATE FROM THE KEYBOARD ON PURPOSE. Every trigger in the mod reads its configured
    /// key and then asks here as well, so a pad is an ADDITION to the ini rather than a mode
    /// the player has to switch into -- and nothing has to be rebound to use one.
    /// </summary>
    internal static class Pad
    {
        /// <summary>The game's own interact button. What ~INPUT_CONTEXT~ draws in a prompt.</summary>
        public static bool Context()
        {
            try { return Game.IsControlJustPressed(Control.Context); }
            catch { return false; }
        }

        /// <summary>Held down right now, as a level rather than an edge.</summary>
        public static bool Down(Control c)
        {
            try { return Game.IsControlPressed(c); }
            catch { return false; }
        }

        /// <summary>
        /// Two buttons together, edge-detected against the caller's own memory.
        ///
        /// The caller keeps the flag because there is more than one chord in the mod and a
        /// shared one would let the menu eat the pocket's press.
        /// </summary>
        public static bool Chord(Control modifier, Control button, ref bool was)
        {
            var down = Down(modifier) && Down(button);
            var edge = down && !was;

            was = down;
            return edge;
        }
    }
}
