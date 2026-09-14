using GTA;
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
        /// How much he can carry, and that is all it is.
        ///
        /// IT USED TO GROW BY WHATEVER THE BAG LENT and that was the wrong shape. A bag is a
        /// second PLACE -- things go in it and come out of it, and the difference between
        /// what is on you and what is in your bag is the whole point of carrying one -- so
        /// lending its twenty slots to the pocket made one flat list of twenty-five and threw
        /// that difference away. The room the bag is worth is the bag's own store now, with
        /// two grids and a transfer between them. See Food.Knapsack.
        ///
        /// Never below one, because a pocket with no slots in it is a mod that has stopped
        /// working rather than a player with full hands.
        /// </summary>
        public override int Slots => _cfg.PantrySlots < 1 ? 1 : _cfg.PantrySlots;

        /// <summary>
        /// ONE PLACE PER KIND OF DRUG ON YOU. See Store.Reserved.
        ///
        /// PER KIND, NOT PER GRAM, because that is what the pocket screen draws: one tile for
        /// the weed however much of it there is, the same as one tile for the crisps however
        /// many packets. The grams have a capacity of their own over in the other mod and are
        /// reported beside this rather than folded into it.
        ///
        /// CACHED FOR A FIFTH OF A SECOND. Ids() reaches into the other mod by reflection and
        /// Full is asked on every frame a shop is open. The answer only changes when somebody
        /// picks something up, so a stale one is at most three frames behind.
        /// </summary>
        protected override int Reserved
        {
            get
            {
                var now = Game.GameTime;

                if (now >= _dopeAt)
                {
                    _dopeAt = now + 200;

                    try
                    {
                        var ids = Dope.Ids();
                        _dope = ids == null ? 0 : ids.Length;
                    }
                    catch
                    {
                        // The other mod is not there, or is mid-reload. Nothing is in the way.
                        _dope = 0;
                    }
                }

                return _dope;
            }
        }

        private int _dope;
        private int _dopeAt;

        protected override string File => Paths.PantryFile;

        protected override string What => "Pantry";
    }
}
