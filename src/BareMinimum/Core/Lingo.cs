using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Native;

namespace BareMinimum.Core
{
    /// <summary>
    /// THE MOD IN ANOTHER LANGUAGE.
    ///
    /// THE ENGLISH STRING IS THE KEY. There are no "menu.bag.empty" identifiers anywhere in
    /// this mod and there are not going to be. A language file is a plain map from what the
    /// code says to what it should say instead:
    ///
    ///     { "Nothing on you." : "Vous n'avez rien sur vous." }
    ///
    /// Three things fall out of that and all three matter more than tidiness.
    ///
    /// ONE: NOTHING AT THE CALL SITES CHANGES. Every string in this mod already passes through
    /// Draw.Text, Draw.Width, Draw.Help or Kit.Fit on its way to the screen, so the lookup goes
    /// in those four and the other eleven thousand lines are untouched. A key-based system
    /// would have meant editing every one of them, and every edit is a chance to put the wrong
    /// key on the right line -- which nothing would ever catch, because a wrong key shows the
    /// wrong sentence in a language the person who made the mistake does not read.
    ///
    /// TWO: THE FALLBACK IS AUTOMATIC AND CANNOT FAIL. A miss returns what it was given, and
    /// what it was given is the English. There is no such thing as a missing string, a null
    /// label or an identifier showing through on screen. A half-finished translation is a
    /// working mod in two languages rather than a broken mod in one.
    ///
    /// THREE: THE DATA FILES COME FOR FREE. A hot dog's name and the joke under it live in
    /// foods.json and are drawn through the same four functions, so translating one is the
    /// same act as translating a button -- put the English in the file and the other language
    /// beside it. No code knows the difference and no code has to.
    ///
    /// ENGLISH (UK) IS THE CODE ITSELF, which is why en-GB.json exists but is allowed to be
    /// empty: the mod is written in English and this mod's English is British -- per cent,
    /// colour, whisky, petrol station. en-US.json is therefore not a translation but a DIFF,
    /// and a short one.
    ///
    /// ON WHETHER THE LETTERS WILL ACTUALLY DRAW. This is the part no amount of good
    /// translation gets round, and it is not ours to fix. Text goes to the screen through the
    /// game's own fonts, and the game's own fonts hold what Rockstar shipped:
    ///
    ///   Latin, and Latin with accents   Always there. English, Spanish, French, German,
    ///                                   Portuguese and Polish all draw on any install.
    ///   Cyrillic                        There. Russian is one of the game's own languages.
    ///   Han                             There, but loaded with the CJK font, which the game
    ///                                   swaps in according to ITS OWN language setting. Run
    ///                                   the game in English and pick Chinese here and you are
    ///                                   likely to get empty boxes -- not because the
    ///                                   translation is missing but because the glyphs are.
    ///   Devanagari                      NOT THERE AT ALL. GTA V has never shipped a Hindi
    ///                                   localisation, so there is no font in the game with
    ///                                   these letters in it. Hindi is offered because it was
    ///                                   asked for and the text is real, but expect boxes
    ///                                   until somebody puts a font in the game that has it.
    ///
    /// So the language is checked against the game's own at load and says so in the log
    /// instead of leaving somebody to wonder why their menu is full of squares.
    /// </summary>
    internal static class Lingo
    {
        /// <summary>One offered language.</summary>
        public sealed class Tongue
        {
            /// <summary>The file's name, without the extension: lang\fr.json.</summary>
            public string Code;

            /// <summary>What the settings row shows. Its OWN name, which is what a speaker looks for.</summary>
            public string Name;

            /// <summary>
            /// The game's own language ids this one's letters ride in on, or null for the ones
            /// that draw on any install. See the class note.
            /// </summary>
            public int[] Needs;

            /// <summary>Said in the log when Needs is not met. Null when there is nothing to warn about.</summary>
            public string Warning;
        }

        // The game's eLanguage, for the check below only: 0 English, 1 French, 2 German,
        // 3 Italian, 4 Spanish, 5 Brazilian, 6 Polish, 7 Russian, 8 Korean, 9 Chinese
        // Traditional, 10 Japanese, 11 Mexican, 12 Chinese Simplified.
        private static readonly int[] Chinese = { 9, 12 };
        private static readonly int[] Never = new int[0];

        /// <summary>
        /// THE ORDER IS THE ORDER THEY WERE ASKED FOR, with English first because it is the
        /// default and a default belongs at the top of a list you scroll.
        /// </summary>
        public static readonly Tongue[] Tongues =
        {
            new Tongue { Code = "en-GB", Name = "English (UK)" },
            new Tongue { Code = "en-US", Name = "English (US)" },
            new Tongue { Code = "pt-BR", Name = "Portugu\u00eas (BR)" },
            new Tongue { Code = "es",    Name = "Espa\u00f1ol" },
            new Tongue { Code = "fr",    Name = "Fran\u00e7ais" },
            new Tongue { Code = "de",    Name = "Deutsch" },
            new Tongue { Code = "ru",    Name = "\u0420\u0443\u0441\u0441\u043a\u0438\u0439" },
            new Tongue { Code = "pl",    Name = "Polski" },
            new Tongue { Code = "zh",    Name = "\u4e2d\u6587",
                         Needs = Chinese,
                         Warning = "the game is not running in Chinese, and the Han glyphs are in " +
                                   "the font the game loads for its OWN language. Expect empty " +
                                   "boxes until GTA itself is set to Chinese." },
            new Tongue { Code = "hi",    Name = "\u0939\u093f\u0928\u094d\u0926\u0940",
                         Needs = Never,
                         Warning = "GTA V has never shipped a Hindi localisation, so no font in " +
                                   "this game has Devanagari in it. The translation is real; the " +
                                   "letters will almost certainly draw as boxes." }
        };

        /// <summary>What is loaded. NULL means English, which is the code, and makes Say a single test.</summary>
        private static Dictionary<string, string> _table;

        /// <summary>The code in force. Never null.</summary>
        public static string Code = "en-GB";

        /// <summary>Every English phrase that has reached the screen, when the ini asks for it. See Dump.</summary>
        private static HashSet<string> _seen;
        private static int _seenCount;
        private static int _dumpAt;

        /// <summary>How many phrases the loaded file carries. For the log and the menu's note.</summary>
        public static int Phrases;

        /// <summary>The row in the settings menu: which of Tongues is in force.</summary>
        public static int Index
        {
            get
            {
                for (var i = 0; i < Tongues.Length; i++)
                    if (string.Equals(Tongues[i].Code, Code, StringComparison.OrdinalIgnoreCase)) return i;

                return 0;
            }
        }

        public static string[] Names
        {
            get
            {
                var n = new string[Tongues.Length];
                for (var i = 0; i < n.Length; i++) n[i] = Tongues[i].Name;
                return n;
            }
        }

        /// <summary>
        /// WHAT TO SAY INSTEAD. A miss hands back exactly what it was given, which is the
        /// English, which is why nothing can ever be missing.
        ///
        /// It is called on every string drawn in every frame, so the English case is one null
        /// test and a return -- no dictionary, no allocation, nothing to profile.
        ///
        /// IT IS SAFE TO CALL TWICE. Kit.Fit measures a string and then Draw.Text draws it, and
        /// both translate; the second call looks up a phrase that is already French, misses,
        /// and hands the French straight back. That is deliberate -- the alternative is a rule
        /// about which of the two owns the lookup, and a rule like that is broken by the next
        /// person to add a screen.
        /// </summary>
        public static string Say(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;

            if (_seen != null) _seen.Add(english);

            var t = _table;
            if (t == null) return english;

            string said;
            return t.TryGetValue(english, out said) && said.Length > 0 ? said : english;
        }

        /// <summary>
        /// A sentence with something dropped into it -- a shop's name, a price -- translated
        /// WHOLE and filled in afterwards.
        ///
        /// A prompt built by gluing fragments together can never be translated, because what
        /// arrives at the table is a sentence with a shop's name already welded into the middle
        /// of it and no two of those are ever the same string. Worse, gluing assumes English
        /// word order: "Hold " + "to " + "sleep in the car" puts the verb after the button and
        /// the place after the verb, and plenty of languages want them somewhere else. A
        /// template moves the hole to wherever the translation needs it.
        ///
        /// A TRANSLATION WITH A BROKEN PLACEHOLDER FALLS BACK TO THE ENGLISH rather than taking
        /// the prompt down. Somebody typing {O} for {0} in a language nobody here reads is a
        /// certainty, not a risk, and the cost of it should be one English prompt.
        /// </summary>
        public static string Fill(string english, params object[] bits)
        {
            if (bits == null || bits.Length == 0) return Say(english);

            var said = Say(english);

            try
            {
                return string.Format(said, bits);
            }
            catch (FormatException)
            {
                Log.Once("lingo-format-" + Code,
                         "A phrase in lang\\" + Code + ".json has a bad placeholder in it and was " +
                         "skipped: \"" + english + "\". Use {0} exactly as the English does.");

                try { return string.Format(english, bits); }
                catch (FormatException) { return english; }
            }
        }

        /// <summary>
        /// Reads the chosen language in. Called at load and again whenever the row is nudged,
        /// so the menu changes language under the cursor.
        /// </summary>
        public static void Load(Settings cfg)
        {
            Code = Resolve(cfg == null ? null : cfg.Language);

            _seen = cfg != null && cfg.LanguageDump ? new HashSet<string>(StringComparer.Ordinal) : null;
            _seenCount = 0;

            if (string.Equals(Code, "en-GB", StringComparison.OrdinalIgnoreCase))
            {
                _table = null;
                Phrases = 0;
                Log.Info("Language: English (UK) -- the mod's own words, no file read.");
                return;
            }

            var file = Path.Combine(Folder, Code + ".json");

            if (!File.Exists(file))
            {
                _table = null;
                Phrases = 0;
                Log.Warn("Language: " + Code + " was asked for and lang\\" + Code + ".json is not there, " +
                         "so everything stays in English.");
                return;
            }

            try
            {
                Json root;
                if (!Json.TryParse(File.ReadAllText(file, System.Text.Encoding.UTF8), out root) || root == null)
                {
                    _table = null;
                    Phrases = 0;
                    Log.Warn("Language: lang\\" + Code + ".json will not parse, so everything stays in English.");
                    return;
                }

                var table = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var key in root.Keys)
                {
                    // A LEADING UNDERSCORE IS A NOTE, NOT A PHRASE. The files carry their own
                    // instructions at the top and nothing on screen begins with one.
                    if (key.Length == 0 || key[0] == '_') continue;

                    var said = root[key].AsString("");
                    if (said.Length > 0 && said != key) table[key] = said;
                }

                _table = table.Count > 0 ? table : null;
                Phrases = table.Count;

                Log.Info("Language: " + Name(Code) + " (" + Code + "), " + table.Count +
                         " phrase(s). Anything not in the file stays in English.");

                Glyphs();
            }
            catch (Exception ex)
            {
                _table = null;
                Phrases = 0;
                Log.Error("Language: could not read lang\\" + Code + ".json; everything stays in English", ex);
            }
        }

        /// <summary>
        /// Says in the log when the letters are not going to draw, which is not the same
        /// problem as the words being missing and must not be left looking like it.
        /// </summary>
        private static void Glyphs()
        {
            var t = Tongues[Index];
            if (t.Needs == null || t.Warning == null) return;

            var game = -1;
            try { game = Function.Call<int>(Hash.GET_CURRENT_LANGUAGE); }
            catch { /* then we simply do not know, and a guess is worse than the warning */ }

            foreach (var id in t.Needs) if (id == game) return;

            Log.Warn("Language: " + t.Name + " is loaded, but " + t.Warning);
        }

        /// <summary>An ini value to a code. Takes the code, the name, or a near miss at either.</summary>
        private static string Resolve(string asked)
        {
            if (string.IsNullOrEmpty(asked)) return "en-GB";

            asked = asked.Trim();

            foreach (var t in Tongues)
                if (string.Equals(t.Code, asked, StringComparison.OrdinalIgnoreCase)) return t.Code;

            foreach (var t in Tongues)
                if (string.Equals(t.Name, asked, StringComparison.OrdinalIgnoreCase)) return t.Code;

            // "english", "portuguese", "br", "zh-CN", "russian" -- anybody hand-editing an ini
            // writes what comes to mind, and refusing it silently is how a setting gets a
            // reputation for not working.
            var low = asked.ToLowerInvariant();

            if (low.StartsWith("en")) return low.Contains("us") || low.Contains("amer") ? "en-US" : "en-GB";
            if (low.StartsWith("pt") || low.Contains("portug") || low.Contains("brasil") || low.Contains("brazil")) return "pt-BR";
            if (low.StartsWith("es") || low.Contains("span") || low.Contains("castell")) return "es";
            if (low.StartsWith("fr") || low.Contains("french")) return "fr";
            if (low.StartsWith("de") || low.Contains("german")) return "de";
            if (low.StartsWith("ru") || low.Contains("russ")) return "ru";
            if (low.StartsWith("pl") || low.Contains("pol")) return "pl";
            if (low.StartsWith("zh") || low.Contains("chin") || low.Contains("mandarin")) return "zh";
            if (low.StartsWith("hi") || low.Contains("hind")) return "hi";

            Log.Warn("Language: \"" + asked + "\" is not one of the ten, so English it is. " +
                     "The codes are en-GB, en-US, pt-BR, es, fr, de, ru, pl, zh, hi.");

            return "en-GB";
        }

        public static string Name(string code)
        {
            foreach (var t in Tongues)
                if (string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase)) return t.Name;

            return code;
        }

        /// <summary>scripts\BareMinimum\lang\ -- shipped, and read-only as far as this is concerned.</summary>
        public static string Folder
        {
            get { return Path.Combine(Paths.Data, "lang"); }
        }

        /// <summary>
        /// EVERY PHRASE THE MOD HAS ACTUALLY PUT ON SCREEN, written out for whoever is doing
        /// the translating.
        ///
        /// A translator's real problem is not the translating, it is finding out what there is
        /// to translate: grepping the source turns up log lines, ini keys and native names
        /// mixed in with the words, and misses every sentence that came out of foods.json. This
        /// misses nothing and invents nothing, because it is a list of what was drawn.
        ///
        /// Play with it on, open every screen, then take lang\_seen.json and fill it in.
        /// </summary>
        public static void Dump(Settings cfg)
        {
            if (_seen == null || cfg == null || !cfg.LanguageDump) return;

            var now = Game.GameTime;
            if (_seen.Count == _seenCount || now < _dumpAt) return;

            _seenCount = _seen.Count;
            _dumpAt = now + 10000;

            try
            {
                var lines = new List<string>(_seen);
                lines.Sort(StringComparer.OrdinalIgnoreCase);

                var out_ = Json.Object();
                out_.Set("_note", Json.Str("Every phrase this mod has drawn on screen this session. " +
                                           "Put the translation on the right of each. Delete a line to " +
                                           "leave it in English -- an empty value does the same."));

                foreach (var line in lines) out_.Set(line, Json.Str(""));

                Directory.CreateDirectory(Paths.Writable);
                File.WriteAllText(Path.Combine(Paths.Writable, "lang-seen.json"),
                                  out_.ToJsonString(true), new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Once("lingo-dump", "Could not write the phrase dump: " + ex.Message);
            }
        }
    }
}
