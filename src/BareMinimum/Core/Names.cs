using System;
using System.Collections.Generic;
using System.IO;
using GTA;

namespace BareMinimum.Core
{
    /// <summary>
    /// A ped model hash turned back into the name somebody would type to spawn it.
    ///
    /// THE GAME ONLY GOES ONE WAY. A name is hashed into a number the moment it is used and
    /// the number is all any script can read back, so "what is this thing called" -- the first
    /// question you ask about anything you want to build round -- has no answer in the API at
    /// all.
    ///
    /// WHY THIS MOD NEEDS IT AT ALL: the body card. Whoever is lying on the pavement has a
    /// model and nothing else -- no name, no age, no set -- so the card is made up from what
    /// can be read off him, and the model's NAME is most of what can be read. a_f_y_beach_01
    /// says the game thinks of this one as a young woman at the beach; g_m_y_ballaorig_01 says
    /// Ballas. Both of those are in the string and neither is in the number.
    ///
    /// TWO SOURCES, IN ORDER. Menyoo and Rampage ship complete name lists as plain text in the
    /// game folder, and the hash is a nine-line function -- run it over the lists once and
    /// every hash that came from one of those names can be read out loud. Where they are not
    /// installed, SHVDN's own PedHash enum is walked instead: the names in it are derived from
    /// the model names, so "Ballas01GFY" still contains "balla" and the card still knows what
    /// it is looking at, even though the sex convention in the middle of a real model name is
    /// not there to be read.
    ///
    /// AND NEITHER IS REQUIRED. With no lists and no match this answers "", and everything
    /// that uses it falls back: the affiliation comes off the relationship group, which is
    /// what actually decides it anyway, and the rest of the card is a deterministic roll.
    ///
    /// Ported from Posted Up's Core.Names when the body loot came over on 2026-09-22, cut down
    /// to the ped lists -- this mod has no reason to know what a vehicle is called.
    /// </summary>
    internal static class Names
    {
        private static Dictionary<uint, string> _byHash;
        private static bool _tried;

        /// <summary>Where the lists live, relative to the game folder. Every one found is read.</summary>
        private static readonly string[] Lists =
        {
            @"RampageFiles\Lists\PedList.txt",
            @"menyooStuff\PedList.xml"
        };

        /// <summary>How many names are known. Zero means nothing on the bench had a list.</summary>
        public static int Count
        {
            get
            {
                Ready();
                return _byHash == null ? 0 : _byHash.Count;
            }
        }

        /// <summary>The name, lower-cased, or "" when nothing knows it.</summary>
        public static string Of(int hash)
        {
            return Of(unchecked((uint)hash));
        }

        public static string Of(uint hash)
        {
            Ready();

            string name;

            if (_byHash != null && _byHash.TryGetValue(hash, out name)) return name;

            // THE ENUM IS THE FALLBACK, not the first answer. Its names are SHVDN's own
            // spelling of the model names rather than the model names -- BallaEast01GMY for
            // g_m_y_ballaeast_01 -- so the words the card looks for are in there and the
            // underscored sex is not. Good enough to know a Balla from a golfer, which is
            // what the tables below this actually ask.
            try
            {
                var named = Enum.GetName(typeof(PedHash), (PedHash)unchecked((int)hash));

                if (!string.IsNullOrEmpty(named)) return named.ToLowerInvariant();
            }
            catch
            {
                // Not a vanilla ped, then. An add-on model has no name anybody can reach.
            }

            return "";
        }

        private static void Ready()
        {
            if (_tried) return;
            _tried = true;

            try
            {
                var root = GameRoot();
                if (string.IsNullOrEmpty(root)) return;

                _byHash = new Dictionary<uint, string>();

                var read = 0;

                foreach (var rel in Lists)
                {
                    var path = Path.Combine(root, rel);
                    if (!File.Exists(path)) continue;

                    read++;

                    foreach (var raw in File.ReadAllLines(path))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0) continue;

                        // The xml list carries the name in an attribute; pull anything that
                        // looks like a model name out of the line rather than parsing it as
                        // xml, which would mean caring whether the file is well formed.
                        if (line[0] == '<')
                        {
                            foreach (var word in Words(line)) Add(word);
                            continue;
                        }

                        // Some lists are "name,0x1234" or "name = 123".
                        var cut = line.IndexOfAny(new[] { ',', '=', '\t', ' ' });
                        if (cut > 0) line = line.Substring(0, cut).Trim();

                        Add(line);
                    }
                }

                if (read == 0)
                {
                    Log.Debug("No ped name lists on the bench; body cards will read models " +
                              "off the game's own enum instead.");
                    return;
                }

                Log.Info("Ped model names on the bench: " + _byHash.Count +
                         " from " + read + " list(s).");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read the ped name lists: " + ex.Message);
            }
        }

        private static void Add(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 3) return;

            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return;
            }

            var hash = Joaat(name);
            if (!_byHash.ContainsKey(hash)) _byHash[hash] = name.ToLowerInvariant();
        }

        /// <summary>The name-shaped runs in a line of xml, without parsing it as xml.</summary>
        private static IEnumerable<string> Words(string line)
        {
            var sb = new System.Text.StringBuilder();

            foreach (var c in line)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                    continue;
                }

                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Length = 0;
                }
            }

            if (sb.Length > 0) yield return sb.ToString();
        }

        /// <summary>
        /// Rockstar's string hash: the one the game itself uses, on the lower-cased name.
        /// Jenkins one-at-a-time, and every model, anim dict, scenario and audio name in the
        /// game is one of these.
        /// </summary>
        public static uint Joaat(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            uint hash = 0;

            foreach (var raw in text)
            {
                var c = raw;
                if (c >= 'A' && c <= 'Z') c = (char)(c + 32);

                hash += c;
                hash += hash << 10;
                hash ^= hash >> 6;
            }

            hash += hash << 3;
            hash ^= hash >> 11;
            hash += hash << 15;

            return hash;
        }

        /// <summary>The folder the game runs from: the one above scripts\.</summary>
        private static string GameRoot()
        {
            try
            {
                var scripts = Paths.Scripts;
                if (string.IsNullOrEmpty(scripts)) return "";

                var up = Directory.GetParent(scripts);
                return up == null ? "" : up.FullName;
            }
            catch
            {
                return "";
            }
        }
    }
}
