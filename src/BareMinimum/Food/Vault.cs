using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// Containers that belong to another mod, kept and written down by this one.
    ///
    /// ONE INVENTORY ON THE MACHINE, AND THIS IS THE HALF THAT IS BARE MINIMUM'S. Hoodrich has
    /// a jacket pocket, a bag you can drop in the street, a car boot and a stash house, and
    /// every one of them was its own store saved into its own file. Two mods each keeping their
    /// own idea of what the player is carrying is two mods that disagree the first time one of
    /// them is reloaded -- so the containers live here now, and there is one answer.
    ///
    /// IT DOES NOT KNOW WHAT IS IN THEM AND MUST NOT. A pocket in that mod holds grams of a
    /// drug at a purity, packaged or bulk; a boot holds the same; and none of that is anything
    /// this mod has an opinion about. So a container is a NAME and a STRING, and the string is
    /// whatever the owner wants it to be -- its own JSON, its own version, its own rules. This
    /// side stores it, saves it, and hands it back exactly as it arrived.
    ///
    /// WHICH IS WHY IT CANNOT ROT. The day Hoodrich adds a field to a stash, nothing here
    /// changes and nothing here needs rebuilding. A store that understood the format would have
    /// to be updated in step with a mod it does not reference, which is the one arrangement
    /// guaranteed to break on somebody else's machine.
    ///
    /// WRITTEN WHEN IT CHANGES, not every frame -- see Tick. A save is a file write and the
    /// thing being saved is a man walking around with a bag on.
    /// </summary>
    internal sealed class Vault
    {
        /// <summary>How long after a change before it goes to disk.</summary>
        private const float SaveAfter = 4f;

        /// <summary>
        /// A cap, because this is a public surface and the caller is another mod.
        ///
        /// Not a limit anybody will meet: Hoodrich's largest container is a stash house with a
        /// few dozen lots in it, which is a couple of kilobytes. It is here so a caller with a
        /// loop in it cannot fill the disk through a bridge that was only ever meant to hold a
        /// pocket.
        /// </summary>
        private const int Most = 64;
        private const int Longest = 256 * 1024;

        private readonly Dictionary<string, string> _held =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private bool _dirty;
        private float _since;

        public Vault()
        {
            Load();
        }

        /// <summary>What is in that container, or empty for one nobody has filled.</summary>
        public string Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            string held;
            return _held.TryGetValue(name, out held) ? held ?? "" : "";
        }

        /// <summary>Whether anything has ever been put in that container.</summary>
        public bool Has(string name)
        {
            return !string.IsNullOrEmpty(name) && _held.ContainsKey(name);
        }

        /// <summary>
        /// Puts a container's contents in. Empty removes it, which is how a bag is thrown away.
        /// </summary>
        public bool Put(string name, string what)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (what != null && what.Length > Longest) return false;

            if (string.IsNullOrEmpty(what))
            {
                if (!_held.Remove(name)) return true;

                _dirty = true;
                _since = 0f;
                return true;
            }

            if (!_held.ContainsKey(name) && _held.Count >= Most) return false;

            string was;
            if (_held.TryGetValue(name, out was) && was == what) return true;

            _held[name] = what;

            _dirty = true;
            _since = 0f;

            return true;
        }

        /// <summary>Every container there is, for anybody taking stock.</summary>
        public string[] Names()
        {
            var names = new string[_held.Count];
            _held.Keys.CopyTo(names, 0);

            return names;
        }

        /// <summary>Ticked by Main. Writes a while after the last change and not before.</summary>
        public void Tick(float dt)
        {
            if (!_dirty) return;

            _since += dt;
            if (_since < SaveAfter) return;

            Save();
        }

        /// <summary>Now, for the shutdown hook.</summary>
        public void Save()
        {
            _dirty = false;
            _since = 0f;

            try
            {
                var sb = new StringBuilder();

                sb.Append("{");

                var first = true;

                foreach (var pair in _held)
                {
                    if (!first) sb.Append(",");
                    first = false;

                    sb.Append(Quote(pair.Key)).Append(":").Append(Quote(pair.Value));
                }

                sb.Append("}");

                var path = Paths.VaultFile;
                var dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Once("vault-save", "Could not write the vault: " + ex.Message);
            }
        }

        /// <summary>
        /// Reads it back.
        ///
        /// A HAND-ROLLED PAIR OF STRINGS RATHER THAN A JSON READER, because that is all this
        /// file ever is: one flat object of name to string, both written by Quote below. The
        /// values are other people's JSON and are never parsed here -- they are carried.
        /// </summary>
        private void Load()
        {
            try
            {
                var path = Paths.VaultFile;
                if (!File.Exists(path)) return;

                var text = File.ReadAllText(path, Encoding.UTF8);

                var at = 0;

                while (true)
                {
                    var name = Next(text, ref at);
                    if (name == null) break;

                    var what = Next(text, ref at);
                    if (what == null) break;

                    _held[name] = what;
                }

                Log.Info("Vault: " + _held.Count + " container(s) read back.");
            }
            catch (Exception ex)
            {
                Log.Once("vault-load", "Could not read the vault: " + ex.Message);
            }
        }

        /// <summary>The next quoted string from `at`, unescaped, or null at the end.</summary>
        private static string Next(string text, ref int at)
        {
            while (at < text.Length && text[at] != '"') at++;
            if (at >= text.Length) return null;

            at++;

            var sb = new StringBuilder();

            while (at < text.Length)
            {
                var c = text[at++];

                if (c == '"') return sb.ToString();

                if (c != '\\') { sb.Append(c); continue; }

                if (at >= text.Length) break;

                var e = text[at++];

                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (at + 4 <= text.Length)
                        {
                            int code;
                            if (int.TryParse(text.Substring(at, 4),
                                             System.Globalization.NumberStyles.HexNumber,
                                             System.Globalization.CultureInfo.InvariantCulture,
                                             out code))
                            {
                                sb.Append((char)code);
                            }

                            at += 4;
                        }

                        break;
                    default: sb.Append(e); break;
                }
            }

            return null;
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder(s == null ? 2 : s.Length + 2);

            sb.Append('"');

            foreach (var c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);

                        break;
                }
            }

            sb.Append('"');

            return sb.ToString();
        }
    }
}
