using System;

namespace BareMinimum.Api
{
    /// <summary>
    /// Where the row of bars stands, so another mod's gauge can stand in line with it.
    ///
    /// FUMES' FUEL GAUGE IS THE SIXTH BAR IN A ROW OF FIVE, and it has been kept in line by
    /// hand: its Width, Height and Y copied across whenever this mod's changed. That worked
    /// until the minimap frame started deciding where the row ends, and then it broke three
    /// times in a day -- every change to the frame moved the bars, and the fuel gauge stayed
    /// where it was and looked wrong. So the numbers are published instead of copied. There is
    /// exactly one set of them and one mod that owns them.
    ///
    /// ONLY BCL TYPES CROSS THIS BOUNDARY, and for the reasons Pantry sets out at length: the
    /// other side calls in by REFLECTION, with no reference to this assembly, because a GTA
    /// scripts\ folder is one assembly resolution namespace and two mods that share a third
    /// assembly must agree about its version forever. Floats and bools, and nothing else.
    ///
    /// NOTHING HERE THROWS AND NOTHING HERE IS EVER STALE-BY-A-FRAME IN A WAY THAT MATTERS.
    /// The row is republished every frame it is laid out; a reader that gets last frame's
    /// numbers is a gauge one frame behind a bar it is standing beside, which is invisible.
    /// Ready is false until the HUD has actually laid a row out, so a caller that asks early
    /// gets a straight no rather than a corner of the screen.
    /// </summary>
    public static class Rack
    {
        /// <summary>
        /// The contract version, read by the caller BEFORE anything else. An old reader against
        /// a new host sees a number it does not know and leaves its own settings alone.
        /// </summary>
        public static int ApiVersion => 1;

        /// <summary>The mod's version string, for the other side's log.</summary>
        public static string Version
        {
            get { try { return Core.Build.Version; } catch { return "?"; } }
        }

        /// <summary>Whether the numbers below mean anything yet: a row has been laid out and the bars are the HUD's style.</summary>
        public static bool Ready { get; private set; }

        /// <summary>The line the row's plates end on, group offset included. A fraction of screen height.</summary>
        public static float Bottom { get; private set; }

        /// <summary>How wide one bar is, as a fraction of screen width.</summary>
        public static float BarWidth { get; private set; }

        /// <summary>
        /// How tall the WHOLE instrument is -- bar, breath and plate together -- as a fraction
        /// of screen height. The same figure Fumes calls Height, which is why the two are
        /// directly comparable and why this is the one to publish rather than the bar alone.
        /// </summary>
        public static float BarLength { get; private set; }

        /// <summary>The distance from one bar's left edge to the next's. Nought when there is only one.</summary>
        public static float Pitch { get; private set; }

        /// <summary>How many bars are standing in the row.</summary>
        public static int Slots { get; private set; }

        /// <summary>The row's opacity, 0 to 1, so a neighbour can be as solid as it is.</summary>
        public static float Opacity { get; private set; }

        /// <summary>Where the row starts, as a fraction of screen width. For anything that wants to sit beside it.</summary>
        public static float Left { get; private set; }

        /// <summary>
        /// How deep the black plate under a bar is, as a fraction of screen height.
        ///
        /// PUBLISHED BECAUSE IT IS NOT WHAT IT LOOKS LIKE. It used to be the plate's width made
        /// square on screen, which anybody matching this row would work out for themselves and
        /// get right; it is a line of writing deep now, so that it ends level with the plate
        /// under the minimap, and a neighbour squaring its own would sit a dozen pixels low.
        /// </summary>
        public static float PlateHeight { get; private set; }

        /// <summary>Published by the gauge every time it lays the row out. Nothing else calls this.</summary>
        internal static void Publish(bool ready, float left, float bottom, float barWidth,
                                     float barLength, float pitch, int slots, float opacity,
                                     float plateHeight)
        {
            try
            {
                PlateHeight = plateHeight;
                Ready = ready;
                Left = left;
                Bottom = bottom;
                BarWidth = barWidth;
                BarLength = barLength;
                Pitch = pitch;
                Slots = slots;
                Opacity = opacity;
            }
            catch
            {
                // A property setter cannot throw, but the rule in this folder is that nothing
                // leaves it, and a rule with an exception is not a rule.
            }
        }
    }
}
