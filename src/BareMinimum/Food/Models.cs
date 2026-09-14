using System;
using System.Collections.Generic;
using System.IO;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// Every model in this game you could put in a man's hand, read from data/props.txt.
    ///
    /// THE SIXTY THE SHOPS ALREADY USE ARE NOT ENOUGH. Swapping a burger for a taco is fine
    /// off that list; finding the right model for a slice of pizza is not, because the pizza
    /// models this game has are not on it -- nothing in the shops holds one. The bench could
    /// only ever offer what was already in use, which is why the Pizza Slice sat there
    /// holding a closed box with nothing to swap it for.
    ///
    /// SO THE LIST IS THE GAME'S OWN. tools/props.py reads menyooStuff/PropList.txt -- every
    /// object name in the install, the same dump the animations and the props are checked
    /// against everywhere else in this mod -- and keeps the fifteen hundred that read as
    /// food, drink or packaging. Nothing in it is invented, and every one is checked against
    /// the build again before it is spawned.
    ///
    /// READ ONCE AND HELD. It is a plain text file of names and the fitting bench is the only
    /// thing that ever asks for it, so it is loaded the first time that screen opens and not
    /// at startup: a mod that reads a fifteen hundred line file on every load for a screen
    /// most people never open is a mod being rude about somebody's loading time.
    /// </summary>
    internal static class Models
    {
        private static string[] _all;

        public static string[] All()
        {
            if (_all != null) return _all;

            var found = new List<string>();

            try
            {
                var path = Path.Combine(Path.GetDirectoryName(Paths.FoodsFile) ?? "", "props.txt");

                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var name = line.Trim();

                        // Comments are the file's own header, which explains where it came
                        // from -- see tools/props.py.
                        if (name.Length == 0 || name.StartsWith(";")) continue;

                        found.Add(name);
                    }

                    Log.Info("Models: " + found.Count + " holdable model(s) read from props.txt.");
                }
                else
                {
                    Log.Info("Models: no props.txt, so the fitting bench can only offer the " +
                             "models the shops already use.");
                }
            }
            catch (Exception ex)
            {
                Log.Once("models", "Could not read props.txt: " + ex.Message);
            }

            _all = found.ToArray();
            return _all;
        }
    }
}
