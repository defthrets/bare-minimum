using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GTA;
using BareMinimum.Core;

namespace BareMinimum.Social
{
    /// <summary>Somebody with an account. A stall, a shop, or the man behind the cart.</summary>
    internal sealed class Poster
    {
        public string Handle = "";
        public string Name = "";

        /// <summary>Which vendor in vendors.json this account belongs to.</summary>
        public string Vendor = "";

        public string Gender = "none";
        public bool Verified;
    }

    /// <summary>Why somebody posted.</summary>
    internal enum Chirp
    {
        Ambient,
        Bought,
        Closing,
        Opening,
        Away
    }

    /// <summary>
    /// Lets the food trade talk on Hoodrich's timeline.
    ///
    /// BARE MINIMUM HAS NO FEED AND IS NOT GROWING ONE. Building a second social timeline so
    /// that a hot dog stand could complain about seagulls would be a whole UI, a whole render
    /// path and a second thing on screen fighting the first -- to say fifteen sentences. So
    /// this writes a finished post into Hoodrich's inbox and stops there.
    ///
    /// IT DOES NOTHING AT ALL WITHOUT HOODRICH, which is the requirement: no folder, no
    /// feature, no error, no log spam beyond one line saying why. Nothing here is referenced
    /// at compile time either -- Bare Minimum still has zero external dependencies and would
    /// build and run on a machine that has never heard of Hoodrich.
    ///
    /// FINISHED TEXT, NOT TEMPLATES, is the seam. Hoodrich owns how a post looks; this owns
    /// how a chip shop talks. Handing over the rendered sentence means neither has to learn
    /// the other's template language, and a change to the jokes in socials.json can never
    /// break the receiver.
    ///
    /// ONE FILE PER POST rather than appending to a shared log. Two scripts on two threads,
    /// one appending while the other truncates, is a race for the sake of saving a few
    /// handles -- and the failure mode is a half-written line rendering as a post. A file
    /// that is written, then read, then deleted has no such middle state.
    /// </summary>
    internal sealed class Socials
    {
        private readonly Core.Settings _cfg;
        private readonly Random _rng = new Random();

        private readonly List<Poster> _posters = new List<Poster>();
        private readonly Dictionary<string, string[]> _lines =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string[]> _slots =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The last thing each account said, so nobody repeats themselves back to back.</summary>
        private readonly Dictionary<string, string> _lastSaid =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private string _drop;
        private bool _ready;
        private int _nextAllowed;
        private int _written;

        public Socials(Core.Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>True once there is somewhere to post and something to say.</summary>
        public bool Live => _ready;

        // ======================================================================

        public void Load()
        {
            _ready = false;

            if (!_cfg.SocialEnabled)
            {
                Log.Info("Socials: switched off in the ini.");
                return;
            }

            if (!Hoodrich())
            {
                // Not a warning. Most people will not have Hoodrich, and a mod that logs a
                // warning about a mod you never installed is a mod that looks broken.
                Log.Info("Socials: Hoodrich is not installed, so the shops keep quiet.");
                return;
            }

            if (!ReadContent()) return;

            _ready = _posters.Count > 0 && _lines.Count > 0;

            if (_ready)
            {
                Log.Info("Socials: " + _posters.Count + " account(s) posting to Hoodrich's feed.");
            }
        }

        /// <summary>
        /// Whether Hoodrich is installed.
        ///
        /// BY ITS DATA FOLDER, not by its dll. The dll can be renamed, moved out to disable
        /// it, or left in the folder as a .dll.bak from a bad update -- and none of those mean
        /// the feed is running. The data folder ships with the mod and is where its content
        /// lives, so its presence is the honest answer.
        ///
        /// THE DROP FOLDER IS NOT MADE HERE. It is made the first time there is actually
        /// something to say, so a player who has both mods but never walks past a stall never
        /// gets an empty directory in somebody else's install.
        /// </summary>
        private bool Hoodrich()
        {
            try
            {
                var scripts = Paths.Scripts;
                if (string.IsNullOrEmpty(scripts)) return false;

                _drop = Path.Combine(scripts, "Hoodrich", "guests");

                return Directory.Exists(Path.Combine(scripts, "Hoodrich"));
            }
            catch (Exception ex)
            {
                Log.Once("socials-find", "Could not look for Hoodrich: " + ex.Message);
                return false;
            }
        }

        private bool ReadContent()
        {
            try
            {
                var doc = JsonFile.Read(Paths.SocialsFile, out var how);

                if (how != ReadResult.Ok || doc == null || doc.IsNull)
                {
                    Log.Warn("Socials: no usable " + Paths.SocialsFile + ", so nobody posts.");
                    return false;
                }

                var authors = doc["authors"];

                for (var i = 0; i < authors.Count; i++)
                {
                    var node = authors[i];

                    var poster = new Poster
                    {
                        Handle = node["handle"].AsString(""),
                        Name = node["name"].AsString(""),
                        Vendor = node["vendor"].AsString(""),
                        Gender = node["gender"].AsString("none"),
                        Verified = node["verified"].AsBool(false)
                    };

                    if (string.IsNullOrEmpty(poster.Handle) || string.IsNullOrEmpty(poster.Name))
                    {
                        continue;
                    }

                    _posters.Add(poster);
                }

                ReadSets(doc["posts"], _lines);
                ReadSets(doc["slots"], _slots);

                Log.Info("Socials: read " + how + ".");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not read " + Paths.SocialsFile, ex);
                return false;
            }
        }

        private static void ReadSets(Json parent, Dictionary<string, string[]> into)
        {
            if (parent == null) return;

            foreach (var key in parent.Keys)
            {
                var node = parent[key];
                var list = new List<string>();

                for (var i = 0; i < node.Count; i++)
                {
                    var line = node[i].AsString("");
                    if (line.Length > 0) list.Add(line);
                }

                if (list.Count > 0) into[key] = list.ToArray();
            }
        }

        // ======================================================================
        // Saying something
        // ======================================================================

        /// <summary>
        /// Posts about a vendor, if the dice and the clock allow it.
        ///
        /// RATE LIMITED HARD, and deliberately so. Hoodrich's feed is the block talking about
        /// itself and it is already busy; a shop posting every time you buy a hot dog would
        /// bury the posts somebody actually built a mod around. One every few minutes at most,
        /// and only some of the time even then.
        /// </summary>
        public void About(string vendorId, Chirp why, string itemName, string shopName)
        {
            if (!_ready) return;

            try
            {
                var now = Game.GameTime;
                if (now < _nextAllowed) return;

                if (_rng.Next(100) >= Math.Max(0, _cfg.SocialChance)) return;

                var poster = For(vendorId);
                if (poster == null) return;

                var text = Compose(poster, why, itemName, shopName);
                if (text == null) return;

                if (!Drop(poster, text)) return;

                _nextAllowed = now + Math.Max(10, _cfg.SocialGapSeconds) * 1000;
                _lastSaid[poster.Handle] = text;

                Log.Debug("Socials: " + poster.Handle + " posted about " + vendorId + ".");
            }
            catch (Exception ex)
            {
                Log.Once("socials-about", "Could not post: " + ex.Message);
            }
        }

        private Poster For(string vendorId)
        {
            if (string.IsNullOrEmpty(vendorId)) return null;

            foreach (var p in _posters)
            {
                if (string.Equals(p.Vendor, vendorId, StringComparison.OrdinalIgnoreCase)) return p;
            }

            // No account for this stall. Silence is right: inventing a handle on the spot
            // would put a shop on the timeline that nobody wrote a voice for.
            return null;
        }

        /// <summary>
        /// Builds the sentence: pick a line for the occasion, then fill its slots.
        ///
        /// Tries a handful of times to avoid the line this account used last. Not a loop until
        /// success -- a set with one line in it would spin forever, and saying the same thing
        /// twice is a smaller problem than a hung frame.
        /// </summary>
        private string Compose(Poster poster, Chirp why, string itemName, string shopName)
        {
            string[] set;
            if (!_lines.TryGetValue(why.ToString(), out set) || set.Length == 0) return null;

            string last;
            _lastSaid.TryGetValue(poster.Handle, out last);

            var text = "";

            for (var attempt = 0; attempt < 4; attempt++)
            {
                text = Fill(set[_rng.Next(set.Length)], itemName, shopName, poster);
                if (text != last) break;
            }

            return string.IsNullOrEmpty(text) ? null : text;
        }

        private string Fill(string line, string itemName, string shopName, Poster poster)
        {
            var sb = new StringBuilder(line);

            sb.Replace("{shop}", string.IsNullOrEmpty(shopName) ? poster.Name : shopName);
            sb.Replace("{item}", string.IsNullOrEmpty(itemName) ? "special" : itemName.ToLowerInvariant());

            foreach (var slot in _slots)
            {
                var token = "{" + slot.Key + "}";
                if (line.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;

                sb.Replace(token, slot.Value[_rng.Next(slot.Value.Length)]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Writes one post for Hoodrich to find.
        ///
        /// Hand-rolled JSON rather than a serialiser, because the object has five fields and
        /// this mod does not carry a serialiser. Everything that reaches a value goes through
        /// Escape first: a shop name with a quote in it would otherwise write a broken file
        /// that the receiver drops on the floor.
        /// </summary>
        private bool Drop(Poster poster, string text)
        {
            try
            {
                // Counter as well as the clock, because two posts inside the same millisecond
                // would otherwise be one file written twice.
                var name = Game.GameTime.ToString(CultureInfo.InvariantCulture) + "-" +
                           (_written++).ToString(CultureInfo.InvariantCulture) + ".json";

                Directory.CreateDirectory(_drop);

                var body = "{\"from\":\"BareMinimum\"," +
                           "\"handle\":\"" + Escape(poster.Handle) + "\"," +
                           "\"name\":\"" + Escape(poster.Name) + "\"," +
                           "\"gender\":\"" + Escape(poster.Gender) + "\"," +
                           "\"verified\":" + (poster.Verified ? "true" : "false") + "," +
                           "\"text\":\"" + Escape(text) + "\"}";

                File.WriteAllText(Path.Combine(_drop, name), body, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                Log.Once("socials-drop", "Could not hand the post over: " + ex.Message);
                return false;
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder(s.Length + 8);

            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append(' '); break;
                    default:
                        if (c < 0x20) break;
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
