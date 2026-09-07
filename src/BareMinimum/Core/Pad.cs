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
        /// What to print on a keycap: the pad button while he is on a pad, the bound key
        /// otherwise.
        /// </summary>
        ///
        /// <remarks>
        /// NOT A GLYPH EITHER WAY. The game turns ~INPUT_~ into a button picture inside its
        /// own help box and nowhere else; through DISPLAY_TEXT, which every panel in this mod
        /// uses, the raw token comes out instead -- "t_E" on a keyboard, "b__7" on a pad. So
        /// the cap is a letter this mod draws itself.
        ///
        /// IT SAYS THE BUTTON ON A PAD, and that is the second correction to this method. It
        /// began by returning null whenever the game reported a controller, which left a
        /// prompt with a hole where its button had been. The fix for that was to show the key
        /// always, defended on the grounds that E is not a lie -- every interact in here reads
        /// the configured key AND Control.Context, so E really does still work. Which is true,
        /// and beside the point: a man holding a controller is not asking whether E works, he
        /// is asking which button to press, and E is not one of them.
        ///
        /// The button itself lives in UI.Kit beside the frontend letters, because that is the
        /// only thing in this mod that knows what the buttons are called and one such place is
        /// enough.
        /// </remarks>
        public static string Cap(System.Windows.Forms.Keys key)
        {
            // Only while the pad is the thing he is actually using. A controller plugged in
            // and untouched still reports the keyboard, which is right -- the cap follows the
            // hands, not the hardware.
            var button = UI.Kit.Interact;
            if (button != null) return button;

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
