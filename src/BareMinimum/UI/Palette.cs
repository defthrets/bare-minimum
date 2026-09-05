using System;
using System.Drawing;

namespace BareMinimum.UI
{
    /// <summary>
    /// Bare Minimum's colours: near-black panels lit in AMBER and EMBER.
    ///
    /// THE SAME SHAPE AS HOODRICH'S PALETTE, IN A DIFFERENT COLOUR. That mod's screens are all
    /// built on one brand pair -- a light end and a dark end of a single hue -- and everything
    /// else on them is white, dim white, or a colour that MEANS something: money, warning,
    /// danger. This mod's screens are built the same way now, so a player who has both sees one
    /// family of panels rather than two mods that happened to install together.
    ///
    /// The pair here is the amber that has been on these panels since the first shop. Brand is
    /// that amber to the digit, and BrandDeep is the ember under it: same family, a long way
    /// down in value, so anything drawn from one to the other has somewhere to travel.
    /// </summary>
    internal static class Palette
    {
        public static readonly Color Text = Color.FromArgb(245, 240, 240, 246);
        public static readonly Color TextDim = Color.FromArgb(200, 190, 190, 198);
        public static readonly Color TextDisabled = Color.FromArgb(160, 150, 150, 158);

        /// <summary>The amber every panel already wore. Unchanged, so nothing the player knows moves.</summary>
        public static readonly Color Brand = Color.FromArgb(255, 240, 170, 56);

        /// <summary>The ember under it. The wash at the top of a panel, the stroke on a rule, the far end of a fill.</summary>
        public static readonly Color BrandDeep = Color.FromArgb(255, 206, 96, 24);

        /// <summary>GTA HUD money green.</summary>
        public static readonly Color Cash = Color.FromArgb(255, 126, 190, 79);

        public static readonly Color Warn = Color.FromArgb(255, 232, 177, 44);
        public static readonly Color Danger = Color.FromArgb(255, 214, 69, 58);

        /// <summary>The fridge's own colour, so its half of that screen is told apart at a glance.</summary>
        public static readonly Color Cold = Color.FromArgb(255, 120, 198, 226);

        /// <summary>The same colour at a different alpha. Clamped, because arrive*255 can run past.</summary>
        public static Color Alpha(Color c, int alpha)
        {
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;

            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }
    }
}
