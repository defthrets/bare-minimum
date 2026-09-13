using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// What is in his pockets.
    ///
    /// ALL OF THE MECHANICS ARE IN Store, which was extracted from this class when the fridge
    /// turned out to want the same hundred and fifty lines. What is left here is the two facts
    /// that are actually about a pocket: how much fits in one, and where it is written down.
    ///
    /// THE SLOT COUNT IS A SETTING AND IS READ EVERY TIME rather than copied at construction,
    /// so changing it in the menu takes effect on the next frame rather than on the next
    /// reload. It can therefore be LOWERED below what is already being carried -- Add refuses
    /// past the cap but nothing takes anything off you, which is why Store.Room floors at zero
    /// and the pocket screen is happy to draw "4 of 3".
    /// </summary>
    internal sealed class Pantry : Store
    {
        private readonly Core.Settings _cfg;

        public Pantry(Core.Settings cfg, Catalogue menu) : base(menu)
        {
            _cfg = cfg;

            // AFTER the base is constructed and the catalogue is in place: the load drops
            // anything foods.json has stopped defining, and it needs the list to know.
            Load();
        }

        /// <summary>
        /// How much he can carry, plus whatever is lending him room.
        ///
        /// See Api.Pantry.ExtraSlots -- Hoodrich's bag, when it is on his back. Never below
        /// one, because a pocket with no slots in it is a mod that has stopped working rather
        /// than a player with full hands.
        /// </summary>
        public override int Slots
        {
            get
            {
                var mine = _cfg.PantrySlots < 1 ? 1 : _cfg.PantrySlots;

                var lent = 0;
                try { lent = Api.Pantry.ExtraSlots; } catch { /* nobody is lending */ }

                return lent <= 0 ? mine : mine + lent;
            }
        }

        protected override string File => Paths.PantryFile;

        protected override string What => "Pantry";
    }
}
