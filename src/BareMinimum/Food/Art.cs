using System;
using System.Collections.Generic;
using System.IO;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// WHICH PICTURE AN ITEM GETS.
    ///
    /// It used to be one white shape per icon GROUP -- p_burger.png for all eleven burgers --
    /// and the file name was built in five different places. Now there is drawn art, a picture
    /// per ITEM, and the rule is: the item's own file if it exists, the group's if not.
    ///
    ///     i_&lt;id&gt;.png      the item's own -- pixel art, in colour, see UI.Icon.Coloured
    ///     p_&lt;icon&gt;.png    the group's -- the white shape, tinted, as it always was
    ///
    /// DECIDED FROM THE FOLDER, NOT FROM A FIELD. The alternative is an "art" key on every
    /// item in foods.json that has to be kept in step with a folder of PNGs by hand, and the
    /// day the two disagree the item shows the wrong picture with nothing to say why. A file
    /// that is there is used; a file that is not is not. Adding a picture is dropping a file
    /// in, and tools/pixelart.py does exactly that.
    ///
    /// EXISTENCE IS ASKED ONCE PER ITEM AND REMEMBERED. Five screens draw up to twenty tiles a
    /// frame, and File.Exists is a call into the operating system each time; the answer cannot
    /// change while the game runs, so it is not asked twice. Forget clears it for a data
    /// reload.
    /// </summary>
    internal static class Art
    {
        private static readonly Dictionary<string, string> Chosen =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The file name to draw for that item, relative to the icon folder. Null when it has neither.</summary>
        public static string For(Item item)
        {
            if (item == null) return null;

            var key = item.Id ?? "";

            string file;
            if (key.Length > 0 && Chosen.TryGetValue(key, out file)) return file;

            file = null;

            try
            {
                if (key.Length > 0)
                {
                    var own = "i_" + key + ".png";
                    if (File.Exists(Path.Combine(Paths.Icons, own))) file = own;
                }
            }
            catch
            {
                // A folder we cannot read is a folder with no pictures of their own in it.
            }

            if (file == null && !string.IsNullOrEmpty(item.Icon)) file = "p_" + item.Icon + ".png";

            if (key.Length > 0) Chosen[key] = file;

            return file;
        }

        /// <summary>After a data reload, so a picture added while the game runs is found.</summary>
        public static void Forget()
        {
            Chosen.Clear();
        }
    }
}
