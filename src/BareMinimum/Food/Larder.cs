using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// What is in the fridge.
    ///
    /// THE SAME STORE AS THE POCKET, with two numbers changed -- see Store, which exists
    /// because this class and Pantry were otherwise the same file twice.
    ///
    /// WHY A FRIDGE AT ALL. The pocket holds three things by default and that is the right
    /// number for a pocket: it is what makes a shop a place you go back to rather than a
    /// warehouse you clear out once. But it also meant there was nowhere to put a week's
    /// shopping, so buying the big things -- a rotisserie chicken, a bag of groceries -- was
    /// pointless. A fridge is the other half of that: somewhere with room, that you have to
    /// travel to.
    ///
    /// IT IS PER CHARACTER, which is Store's rule and the right one here for a reason of its
    /// own: each of the three has his own kitchen, and a fridge shared between them would be
    /// one magic box reachable from three houses. What it is NOT is per fridge -- see Fridges
    /// for why every fridge a character owns opens the same one.
    /// </summary>
    internal sealed class Larder : Store
    {
        private readonly Core.Settings _cfg;

        public Larder(Core.Settings cfg, Catalogue menu) : base(menu)
        {
            _cfg = cfg;

            Load();
        }

        public override int Slots => _cfg.FridgeSlots < 1 ? 1 : _cfg.FridgeSlots;

        protected override string File => Paths.FridgeFile;

        protected override string What => "Fridge";
    }
}
