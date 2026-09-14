using System;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// What is in the bag on his back.
    ///
    /// THE BAG IS THE OTHER MOD'S AND WHAT IS IN IT IS THIS ONE'S. Hoodrich sells a satchel,
    /// puts it on him, takes it off when he drops it, and says how much room it is worth --
    /// see Api.Pantry.ExtraSlots, which is the one number that crosses. The food that goes in
    /// it is this mod's business, so it is kept here, in a store of its own, saved beside the
    /// pocket and the fridge.
    ///
    /// A STORE OF ITS OWN RATHER THAN A BIGGER POCKET, which is what this was first. Lending
    /// twenty slots to the pocket made one flat list of twenty-five, and that is not what a
    /// bag is: a bag is a second place, with things you put IN it and take OUT of it, and the
    /// difference between what is on you and what is in your bag is the whole point of having
    /// one. Two grids and a transfer, the way the fridge already works.
    ///
    /// IT KEEPS WHAT IS IN IT WHEN THE BAG COMES OFF. Dropping a bag in this game is a thing
    /// you do on purpose and get back from; emptying somebody's shopping onto the floor
    /// because they took their bag off at a safehouse would be a mod losing a player's
    /// property. The food waits, and the screen simply cannot be opened until the bag is on
    /// again -- which is also exactly what happens to a bag you leave in a car.
    /// </summary>
    internal sealed class Knapsack : Store
    {
        public Knapsack(Catalogue menu) : base(menu)
        {
            Load();
        }

        /// <summary>
        /// The channel the other mod writes the bag down on: worn, used, slots.
        ///
        /// NOT ExtraSlots, WHICH WAS ALREADY DEAD WHEN THIS WAS WRITTEN. That number was how
        /// the bag used to lend room to the pocket, and Hoodrich stopped writing it the day
        /// before this class landed -- it pins it to nought now, because the bag has a shelf
        /// of its own and lending as well would be the same twenty slots counted twice. So
        /// this was gated on a number that is always nought: the screen could never open.
        ///
        /// spitmux.bag is what it publishes today, every pass, as three ints. The same
        /// AppDomain channel the draw ledger uses, and plain types for the same reason.
        /// </summary>
        private const string Channel = "spitmux.bag";

        private const int SWorn = 0;
        private const int SSlots = 2;

        private static int[] Row
        {
            get
            {
                try { return AppDomain.CurrentDomain.GetData(Channel) as int[]; }
                catch { return null; }
            }
        }

        /// <summary>Whether there is a bag on his back at all.</summary>
        public static bool Worn
        {
            get
            {
                var row = Row;
                return row != null && row.Length > SWorn && row[SWorn] == 1;
            }
        }

        /// <summary>
        /// As big as the bag says it is, and never nought.
        ///
        /// A store with no slots refuses everything, including a Return -- which is how a mod
        /// loses the sandwich somebody was moving when they dropped their bag. One is the
        /// floor everywhere else in this file's family and it is the floor here.
        /// </summary>
        public override int Slots
        {
            get
            {
                var row = Row;
                var slots = row != null && row.Length > SSlots ? row[SSlots] : 0;

                return slots < 1 ? 1 : slots;
            }
        }

        protected override string File => Paths.BagFile;

        protected override string What => "Bag";
    }
}
