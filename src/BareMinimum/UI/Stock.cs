using System;
using System.Drawing;
using System.Globalization;
using BareMinimum.Food;

namespace BareMinimum.UI
{
    /// <summary>
    /// Turns an item into a shop row. The one place that decides what a shop row looks like.
    ///
    /// THIS IS THE "ONE UI THE SHOPS USE AS THEIR BASE". Menu draws rows and knows nothing
    /// about food; this knows about food and nothing about drawing. Between them, the counter
    /// at a 24/7 and the shelf at a corner shop are built by the same code and cannot drift
    /// apart -- which is the whole point, because there are a dozen shops and they were only
    /// ever going to diverge if each built its own rows.
    /// </summary>
    internal static class Stock
    {
        /// <summary>Fallback picture for an item whose icon is missing or unset.</summary>
        private const string Unknown = "p_box.png";

        /// <summary>
        /// One row for one item.
        ///
        /// THE PRICE IS PASSED IN rather than read off the item, because a shop can be having
        /// a sale and the item does not know that. See Vendor.Discount.
        ///
        /// <paramref name="closed"/> is a reason the item cannot be had right now -- a lunch
        /// menu at midnight. It is SHOWN AND GREYED rather than left out of the list, so the
        /// player learns the place does lunch instead of never finding out.
        /// </summary>
        public static Row RowFor(Item item, int money, int price, string closed)
        {
            var afford = money >= price;
            var open = string.IsNullOrEmpty(closed);
            var cheap = price < item.Price;

            var right = "$" + price.ToString(CultureInfo.InvariantCulture);
            if (cheap && open) right = "~g~" + right;

            return new Row
            {
                Left = item.Name,
                Right = right,
                Note = !open ? "~c~" + closed
                     : cheap ? Join(Describe(item, afford), "~g~Was $" +
                                    item.Price.ToString(CultureInfo.InvariantCulture) + ".")
                     : Describe(item, afford),
                Enabled = open && afford,
                IconFile = File(item),
                IconTint = item.Tint,
                Tag = item
            };
        }

        private static string File(Item item)
        {
            return string.IsNullOrEmpty(item.Icon) ? Unknown : "p_" + item.Icon + ".png";
        }

        /// <summary>
        /// What the row says about itself.
        ///
        /// THE ITEM'S OWN LINE FIRST, if it has one. The descriptions in foods.json are the
        /// character of the shop -- somebody reading a list of eight snacks wants a reason to
        /// pick one, and "0.14 hunger" is not a reason. The generated fallback below is only
        /// for an item nobody has written a line for yet.
        /// </summary>
        private static string Describe(Item item, bool afford)
        {
            if (!afford) return "~r~You cannot afford this.";

            if (!string.IsNullOrEmpty(item.Desc)) return item.Desc;

            string size;

            if (item.Hunger >= 0.40f) size = "A proper meal.";
            else if (item.Hunger >= 0.25f) size = "A decent feed.";
            else if (item.Hunger >= 0.12f) size = "Takes the edge off.";
            else if (item.Hunger > 0f) size = "Barely a mouthful.";
            else size = "";

            if (item.Smoke) return "One will not hurt. That is what everybody says.";

            if (item.Booze >= 0.40f) return Join(size, "This will do it.");
            if (item.Booze > 0f) return Join(size, "One of these will not hurt.");
            if (item.Wake >= 0.10f) return Join(size, "Will wake you up a bit.");
            if (item.Wake > 0f) return Join(size, "A slight lift.");

            return size;
        }

        private static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + " " + b;
        }

        /// <summary>
        /// The header line: what you have, and how you are doing.
        ///
        /// The money is there because a price list without a balance sends the player to the
        /// pause menu; the needs are there because "how hungry am I actually" is the question
        /// the whole shop exists to answer, and the HUD icon gives a band, not a number.
        /// </summary>
        public static string Header(int money, float fed, float rested, float drunk)
        {
            var line = "$" + money.ToString("N0", CultureInfo.InvariantCulture) +
                       "     Fed " + Pct(fed) + "     Rested " + Pct(rested);

            // Only once it is showing. A sober reading of 0% is a stat nobody asked for.
            if (drunk >= 0.22f) line += "     ~y~Drunk " + Pct(drunk);

            return line;
        }

        private static string Pct(float v)
        {
            return ((int)Math.Round(v * 100f)).ToString(CultureInfo.InvariantCulture) + "%";
        }
    }
}
