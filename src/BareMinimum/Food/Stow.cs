namespace BareMinimum.Food
{
    /// <summary>
    /// WHERE SOMETHING BOUGHT OR HANDED OVER GOES: the pocket, and the bag when the pocket
    /// is full and there is a bag on him. One rule, written once.
    ///
    /// THERE WERE TWO RULES AND THEY DISAGREED. Api.Pantry.Give -- what the mod next door
    /// calls when you loot a body -- has always tried the pocket and then the bag. The shop
    /// counter and the street vendors only ever tried the pocket: they asked Pantry.Full,
    /// got yes, and handed him the burger to eat on the spot with "no room in your pockets"
    /// while the bag on his back had thirteen empty slots in it. Reported as exactly that:
    /// the mod saying he had no room when he plainly did.
    ///
    /// So the decision lives here and everybody asks it. A bag is a second PLACE, not a
    /// bigger pocket -- see Pantry.Slots -- so the pocket is always tried first and the bag
    /// only takes the overflow, which is what the two-grid screen shows and what a person
    /// carrying a bag would do.
    ///
    /// KIT IS NOT ROUTED THROUGH HERE. A bong is carried in the POCKET or it does not count
    /// as carried -- Dope.Kit reads the pocket alone -- so the shop keeps its own pocket-only
    /// path for anything marked Keep.
    /// </summary>
    internal static class Stow
    {
        /// <summary>Where it ended up, so the message can say so.</summary>
        public enum Where { Nowhere, Pocket, Bag }

        /// <summary>
        /// Whether there is anywhere at all to put one more. The shop asks this BEFORE taking
        /// the money, so a full pocket and a full (or absent) bag become a meal on the spot
        /// rather than a refund.
        /// </summary>
        public static bool Room(Store pocket, Knapsack bag)
        {
            if (pocket != null && !pocket.Full) return true;

            return bag != null && Knapsack.Worn && !bag.Full;
        }

        /// <summary>Puts one in the pocket, else the worn bag, and says which.</summary>
        public static Where Put(Store pocket, Knapsack bag, string id, int howMany = 1)
        {
            if (pocket != null && pocket.Add(id, howMany)) return Where.Pocket;

            if (bag != null && Knapsack.Worn && bag.Add(id, howMany)) return Where.Bag;

            return Where.Nowhere;
        }

        /// <summary>The "in your pocket. 3 of 5." tail, for whichever store took it.</summary>
        public static string Said(Where where, Store pocket, Knapsack bag)
        {
            switch (where)
            {
                case Where.Pocket:
                    return " - in your pocket. " + pocket.Taken + " of " + pocket.Slots + ".";

                case Where.Bag:
                    return " - in your bag. " + bag.Taken + " of " + bag.Slots + ".";

                default:
                    return "";
            }
        }
    }
}
