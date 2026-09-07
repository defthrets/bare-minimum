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
        /// What to print on a keycap for a bound key.
        ///
        /// THE KEY, NOT A GLYPH. The game turns ~INPUT_~ into a button picture inside its own
        /// help box and nowhere else; through DISPLAY_TEXT, which every panel in this mod
        /// uses, the raw token comes out instead -- "t_E" on a keyboard, "b__7" on a pad.
        /// So the cap says what the ini says, which is also the only thing that stays correct
        /// when somebody rebinds it.
        ///
        /// IT IS SHOWN ON A PAD TOO, and that is a correction. This used to return null the
        /// moment the game reported a controller as the last input, on the reasoning that a
        /// keyboard key is a lie to somebody holding a pad. It is not a lie in THIS mod: every
        /// interact reads the configured key AND Control.Context, so E keeps working with a
        /// controller plugged in and a pad in your hands. The cap was simply vanishing --
        /// pick up a pad once, and the prompt lost its button for the rest of the session.
        ///
        /// A pad glyph would be better again and is not available: the game only turns
        /// ~INPUT_~ into a picture inside its own help box, which is the whole reason this
        /// method exists. A key that works beats a blank space.
        /// </summary>
        public static string Cap(System.Windows.Forms.Keys key)
        {
            var name = key.ToString();

            // Keys.D1 through D9 are the number row, and "D1" on a cap means nothing.
            if (name.Length == 2 && name[0] == 'D' && name[1] >= '0' && name[1] <= '9')
                return name.Substring(1);

            return name;
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
