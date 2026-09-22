using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.Food;
using BareMinimum.UI;

namespace BareMinimum.Bodies
{
    /// <summary>What one thing on a body is.</summary>
    internal enum LootKind
    {
        Gun = 0,
        Money,
        Food,
        Drug
    }

    /// <summary>One thing you can take off somebody.</summary>
    internal sealed class LootItem
    {
        public LootKind Kind;

        /// <summary>The drug id or the food id. Empty for money. For a gun, the weapon's own name.</summary>
        public string Id = "";

        /// <summary>The weapon hash, for a gun. Nought for everything else.</summary>
        public uint Hash;

        public string Name = "";

        /// <summary>A path or a file name for the picture, or "" for anything drawn without one.</summary>
        public string Icon = "";

        /// <summary>Rounds in the gun, notes in the roll, or how many of a thing.</summary>
        public int Count;

        /// <summary>Drugs only.</summary>
        public float Grams;
        public float Purity = 1f;

        /// <summary>What the chip in the corner of the tile says.</summary>
        public string Tag = "";

        public Color Tint = Palette.Text;
    }

    /// <summary>
    /// Who somebody was, and what was in their pockets.
    ///
    /// EVERYTHING HERE IS INVENTED EXCEPT THE GUNS. A ped in this game has a model, a
    /// relationship group and whatever weapons the game gave it, and nothing else -- no name,
    /// no age, no address. So the guns and the rounds in them are read off the man himself
    /// and the rest is made up: a name, a date of birth, a height, where the game says he is
    /// from, and a roll of notes.
    ///
    /// MADE UP ONCE, AND THE SAME EVERY TIME. Seeded off the ped's own handle and model, so
    /// the card you get the second time you open a body is the card you got the first time.
    /// A man whose name changes while you are looking at him is not a man, he is a dice roll,
    /// and the whole point of the card is that he was somebody.
    /// </summary>
    internal sealed class Body
    {
        public int Handle;

        public string Name = "";
        public string Born = "";
        public string Height = "";
        public string Ethnicity = "";
        public string Affiliation = "";

        /// <summary>
        /// What he did, when another mod knows. Empty when nobody told us, and the card
        /// leaves the row out rather than inventing a job.
        /// </summary>
        public string Occupation = "";

        /// <summary>
        /// One line about your history with him, when another mod has one: "You spoke three
        /// times." Empty otherwise.
        ///
        /// THE ONLY THING ON THE CARD THIS MOD COULD NEVER KNOW. Everything else here is a
        /// fact about a body; this is a fact about the pair of you, and it is what turns the
        /// card from a receipt into an accusation.
        /// </summary>
        public string Note = "";

        /// <summary>Another mod supplied the identity, so ours was not invented.</summary>
        public bool Named;

        /// <summary>
        /// The colour of whoever he ran with, or the mod's own amber where he ran with
        /// nobody.
        ///
        /// WORKED OUT WHEN THE BODY IS, NOT WHEN THE CARD IS DRAWN. The affiliation is a NAME
        /// by the time anything sees it, so a screen asked to colour itself by it would have
        /// to match that name back against a table every frame, and match it by text.
        /// </summary>
        public Color Colour = Palette.Brand;

        /// <summary>
        /// Whether the body is a woman, which the screen needs and the pronouns need.
        ///
        /// In the mod this came from it was worked out and thrown away: the sex picked the
        /// first name and the height and then went out of scope, so every line of copy on the
        /// screen said "him" over a woman lying on the pavement.
        /// </summary>
        public bool Female;

        /// <summary>"him" or "her", and the rest of it, so no screen has to work it out twice.</summary>
        public string Him { get { return Female ? "her" : "him"; } }
        public string He { get { return Female ? "she" : "he"; } }
        public string His { get { return Female ? "her" : "his"; } }

        public readonly List<LootItem> Items = new List<LootItem>();

        public bool Empty => Items.Count == 0;
    }

    /// <summary>
    /// The bodies you have been through, and what was on them.
    ///
    /// THIS CAME FROM POSTED UP ON 2026-09-22, with the carry that came from Five0 Patrol,
    /// because the corpse now belongs to one mod. It is rebuilt on this mod's own screens and
    /// its own pockets: food goes where food goes here, and product is handed over the bridge
    /// to the mod that owns product. What is NOT here is that mod's gun locker and gang
    /// registry, so the guns are read straight off the man and a set's colour comes from a
    /// short table rather than from a json file this mod does not ship.
    ///
    /// SEARCHING IS THE ONLY WAY ANY OF IT MOVES. The game's own answer to a dead man with a
    /// gun is a pickup on the pavement you walk over without looking; this turns that off (see
    /// Bodies.Search.Nobody) and puts the gun in his pocket instead, where you have to kneel
    /// down and take it. That is the whole feature: the difference between loot arriving and
    /// loot being taken.
    ///
    /// ONE SEARCH PER MAN. What is on him is worked out the first time you open him and kept
    /// against his handle, so closing the screen and opening it again shows the same pockets
    /// with the same things missing. Handles are reused by the game once a body is cleaned
    /// up, so the table is swept of anything that is no longer a corpse.
    /// </summary>
    internal sealed class Corpses
    {
        /// <summary>How much cash somebody might be carrying, before who they are is considered.</summary>
        private const int NotesMin = 8;
        private const int NotesMax = 140;

        /// <summary>A gang member carries the day's money rather than bus fare.</summary>
        private const int GangNotesMin = 40;
        private const int GangNotesMax = 520;

        /// <summary>In a hundred: how often there is anything to eat, and anything to take.</summary>
        private const int FoodChance = 34;
        private const int DrugChance = 18;
        private const int GangDrugChance = 55;

        /// <summary>How often the table is swept of bodies that are no longer there.</summary>
        private const int SweepEveryMs = 20000;

        private readonly Dictionary<int, Body> _known = new Dictionary<int, Body>();

        /// <summary>
        /// Everybody whose pockets have actually been opened, by handle.
        ///
        /// NOT THE SAME AS BEING ON THE BOOKS. _known holds anybody who has been LOOKED at --
        /// it is built on demand by the scan, before you have knelt down -- and it holds them
        /// whether or not you ever searched them. This is the narrower fact, and it is the one
        /// the CARRY waits on: you go through him and then you move him, so the carry prompt
        /// does not appear until this says yes. See Drag.Waiting.
        ///
        /// SWEPT WITH THE REST. The game reuses ped handles, so a set that is never cleared
        /// eventually tells the carry that a fresh corpse has already been searched.
        /// </summary>
        private readonly HashSet<int> _opened = new HashSet<int>();

        private int _sweptAt;

        private readonly Core.Settings _cfg;
        private readonly Catalogue _menu;
        private readonly Pantry _pantry;
        private readonly Knapsack _knapsack;

        /// <summary>
        /// The handle of whoever is in his arms, or 0. Wired by Main to the carry.
        ///
        /// A BODY BEING CARRIED IS ALIVE. The carry brings a corpse back to life to put a
        /// clip on it -- only a living ped takes one -- and kills him again when he is set
        /// down. For the minute in between he answers IsAlive, and a sweep that forgot him
        /// for it would hand the same man a fresh set of pockets the moment he was put down:
        /// searchable again, everything back in them. While his hands are full, nobody alive
        /// is forgotten.
        /// </summary>
        public Func<int> Carried;

        /// <summary>How many bodies are on the books, for the log.</summary>
        public int Count => _known.Count;

        public Corpses(Core.Settings cfg, Catalogue menu, Pantry pantry, Knapsack knapsack)
        {
            _cfg = cfg;
            _menu = menu;
            _pantry = pantry;
            _knapsack = knapsack;
        }

        /// <summary>
        /// Everything known about somebody, made the first time and kept after that.
        ///
        /// Null when there is nothing to make it from.
        /// </summary>
        public Body For(Ped who)
        {
            if (who == null || !who.Exists()) return null;

            Body body;
            if (_known.TryGetValue(who.Handle, out body)) return body;

            body = Build(who);
            _known[who.Handle] = body;

            return body;
        }

        /// <summary>Noted the moment his pockets are actually opened. See _opened.</summary>
        public void Open(Ped who)
        {
            if (who == null || !who.Exists()) return;

            _opened.Add(who.Handle);
        }

        /// <summary>Whether his pockets have been opened. By handle, for the carry.</summary>
        public bool Opened(int handle)
        {
            return handle != 0 && _opened.Contains(handle);
        }

        /// <summary>
        /// Forgets that he was opened, WITHOUT forgetting what is on him.
        ///
        /// CALLED WHEN A CARRIED BODY IS PUT BACK DOWN. The two halves take it in turns over
        /// one key: an unopened body is the search's and an opened one is the carry's, which
        /// is the only arrangement where exactly one prompt is ever on screen. The cost of
        /// that is a body you left a gun on, because once opened he would be the carry's
        /// forever.
        ///
        /// Setting him down is the moment that gives him back. It is also the honest
        /// reading: you have carried him somewhere and are now having another look.
        ///
        /// WHAT IS ON HIM IS UNTOUCHED. _known keeps his card and his pockets exactly as he
        /// was left -- the same man, the same name, the same gun still on him -- so this is
        /// only ever about whose prompt he is.
        /// </summary>
        public void Shut(int handle)
        {
            if (handle == 0) return;

            _opened.Remove(handle);
        }

        /// <summary>Whether this one has already been gone through and emptied.</summary>
        public bool Done(Ped who)
        {
            if (who == null || !who.Exists()) return false;

            Body body;
            return _known.TryGetValue(who.Handle, out body) && body.Empty;
        }

        /// <summary>
        /// Drops anything whose body is gone.
        ///
        /// THE GAME REUSES HANDLES. A table keyed by handle and never cleared eventually hands
        /// a fresh corpse the pockets of a man who died an hour ago -- emptied ones at that,
        /// so the new body would be unsearchable for no reason anybody could see.
        /// </summary>
        public void Sweep(int now)
        {
            if (now - _sweptAt < SweepEveryMs) return;
            _sweptAt = now;

            if (_known.Count == 0) return;

            var gone = new List<int>();

            var held = 0;
            try { held = Carried == null ? 0 : Carried(); }
            catch { held = 0; }

            foreach (var pair in _known)
            {
                if (pair.Key == held) continue;   // see Carried

                var ped = Entity.FromHandle(pair.Key) as Ped;

                if (ped == null || !ped.Exists()) { gone.Add(pair.Key); continue; }
                if (ped.IsAlive) gone.Add(pair.Key);
            }

            // AND THE OPENED SET GOES WITH IT, for exactly the same reason the table does: a
            // recycled handle would otherwise tell the carry that a fresh corpse had already
            // been searched, and it would quietly skip the search on every new body.
            foreach (var handle in gone) { _known.Remove(handle); _opened.Remove(handle); }

            if (gone.Count > 0) Log.Debug("Bodies: forgot " + gone.Count + " that are no longer there.");
        }

        public void Forget()
        {
            _known.Clear();
            _opened.Clear();
        }

        // ---- who he was ---------------------------------------------------------

        private Body Build(Ped who)
        {
            var body = new Body { Handle = who.Handle };

            uint seed;

            try { seed = Seed(who); }
            catch { seed = (uint)who.Handle; }

            var model = Model(who);

            var male = Male(who, model);

            body.Female = !male;

            body.Affiliation = Set(who, model);
            body.Colour = Tint(body.Affiliation);
            body.Ethnicity = Race(model, ref seed);
            body.Name = Called(male, body.Ethnicity, ref seed);
            body.Born = Birthday(ref seed);
            body.Height = Tall(male, ref seed);

            // ---- somebody else may already know who this was -------------------------
            // OURS IS INVENTED AND THEIRS IS REMEMBERED, so theirs wins. A mod that holds
            // conversations knows this man's name because the player was told it to his face,
            // and a card that then calls him something else makes a liar of one of us. The
            // invention above still runs first, so every field has a sensible value if the
            // other mod knows only some of them.
            //
            // It is asked AFTER the seed work and BEFORE the pockets, because the pockets do
            // not depend on the name and this way a provider that throws cannot cost the
            // player the loot.
            Identify(body);

            Pockets(who, body, model, ref seed);

            return body;
        }

        /// <summary>
        /// Ask whoever is wired in whether they know this man, and take their word for it.
        ///
        /// FLAT KEY-VALUE PAIRS, because the provider reaches us by reflection and can name no
        /// type declared in this assembly. A string array is the widest thing that crosses
        /// safely, and pairs mean a provider written today still works when a field is added
        /// tomorrow - it simply does not send the key it has never heard of.
        ///
        /// Unknown keys are ignored rather than being an error, for the same reason.
        /// </summary>
        private static void Identify(Body body)
        {
            var provider = Api.Bodies.Identity;
            if (provider == null || body == null) return;

            string[] pairs;
            try { pairs = provider(body.Handle); }
            catch { return; }          // their bug must not cost us the card
            if (pairs == null || pairs.Length < 2) return;

            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                var key = pairs[i];
                var value = pairs[i + 1];
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;

                switch (key.ToLowerInvariant())
                {
                    case "name": body.Name = value; body.Named = true; break;
                    case "born": body.Born = value; break;
                    case "height": body.Height = value; break;
                    case "ethnicity": body.Ethnicity = value; break;
                    case "occupation": body.Occupation = value; break;
                    case "note": body.Note = value; break;
                    case "affiliation":
                        body.Affiliation = value;
                        body.Colour = Tint(value);
                        break;
                    case "female":
                        body.Female = value == "1" || value.ToLowerInvariant() == "true";
                        break;
                }
            }
        }

        /// <summary>
        /// The one number everything else comes out of.
        ///
        /// The handle and the model together, so two men of the same model standing next to
        /// each other are two different people, and so the same man is the same man.
        /// </summary>
        private static uint Seed(Ped who)
        {
            var h = 2166136261u;

            h ^= (uint)who.Handle;
            h *= 16777619u;

            h ^= unchecked((uint)who.Model.Hash);
            h *= 16777619u;

            return h;
        }

        /// <summary>One number off the seed, and the seed moves on. Deterministic, and not Random.</summary>
        private static int Roll(ref uint seed, int lessThan)
        {
            if (lessThan <= 1) return 0;

            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;

            return (int)(seed % (uint)lessThan);
        }

        private static string Model(Ped who)
        {
            try { return Names.Of(who.Model.Hash) ?? ""; }
            catch { return ""; }
        }

        /// <summary>
        /// Whose he was.
        ///
        /// ASKED OF THE RELATIONSHIP GROUP FIRST, because that is what actually decides gang
        /// membership in this game. The game's own ambient groups are checked by name, and
        /// only then does it fall back to reading the model, which is a guess and is treated
        /// as one.
        /// </summary>
        private static string Set(Ped who, string model)
        {
            try
            {
                var group = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, who.Handle);

                if (group != 0)
                {
                    foreach (var pair in Ambient)
                    {
                        if (Function.Call<int>(Hash.GET_HASH_KEY, pair[0]) != group) continue;

                        return pair[1];
                    }
                }
            }
            catch
            {
                // The model has a guess in it.
            }

            foreach (var pair in ByModel)
            {
                if (model.IndexOf(pair[0], StringComparison.OrdinalIgnoreCase) < 0) continue;

                return pair[1];
            }

            if (model.StartsWith("s_m_y_cop", StringComparison.OrdinalIgnoreCase) ||
                model.StartsWith("s_f_y_cop", StringComparison.OrdinalIgnoreCase) ||
                model.IndexOf("sheriff", StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("swat", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "LSPD";
            }

            return "None";
        }

        /// <summary>The game's own gang groups, by the names it hashes them from.</summary>
        private static readonly string[][] Ambient =
        {
            new[] { "AMBIENT_GANG_BALLAS", "Ballas" },
            new[] { "AMBIENT_GANG_FAMILY", "Families" },
            new[] { "AMBIENT_GANG_MEXICAN", "Vagos" },
            new[] { "AMBIENT_GANG_MARABUNTE", "Marabunta Grande" },
            new[] { "AMBIENT_GANG_SALVA", "Marabunta Grande" },
            new[] { "AMBIENT_GANG_LOST", "The Lost" },
            new[] { "AMBIENT_GANG_HILLBILLY", "Rednecks" },
            new[] { "AMBIENT_GANG_WEICHENG", "Wei Cheng Triads" },
            new[] { "COP", "LSPD" },
            new[] { "SECURITY_GUARD", "Security" },
            new[] { "MEDIC", "LSFD" },
            new[] { "FIREMAN", "LSFD" }
        };

        /// <summary>The last resort: what the model is called. A guess, and only used as one.</summary>
        private static readonly string[][] ByModel =
        {
            new[] { "balla", "Ballas" },
            new[] { "famca", "Families" },
            new[] { "famdnf", "Families" },
            new[] { "famfor", "Families" },
            new[] { "mexgoon", "Vagos" },
            new[] { "vagos", "Vagos" },
            new[] { "mexgang", "Vagos" },
            new[] { "salvaboss", "Marabunta Grande" },
            new[] { "salvagoon", "Marabunta Grande" },
            new[] { "lost", "The Lost" },
            new[] { "korean", "Kkangpae" },
            new[] { "chigoon", "Wei Cheng Triads" },
            new[] { "armgoon", "Armenian Mob" },
            new[] { "armboss", "Armenian Mob" },
            new[] { "armlieut", "Armenian Mob" }
        };

        /// <summary>
        /// A set's own colour, by the name Set just handed back.
        ///
        /// A SHORT TABLE RATHER THAN A DATA FILE, and that is the one thing that had to change
        /// when this came across. The mod it came from reads a set's colour out of its
        /// gangs.json, so a set recoloured in the data recoloured on the card. This mod does
        /// not ship a gangs.json and has no business growing one for a stripe on a panel, so
        /// the game's own six are written here -- green for the Families, purple for the
        /// Ballas, yellow for the Vagos, and the rest as the game draws them on its own
        /// territory map.
        ///
        /// Nobody, or a set not on the list, gets this mod's amber. That is not a fallback so
        /// much as the honest answer: an unaffiliated body has no colours.
        /// </summary>
        private static Color Tint(string affiliation)
        {
            if (string.IsNullOrEmpty(affiliation)) return Palette.Brand;

            switch (affiliation)
            {
                case "Ballas": return Color.FromArgb(255, 150, 96, 182);
                case "Families": return Color.FromArgb(255, 86, 168, 90);
                case "Vagos": return Color.FromArgb(255, 226, 196, 60);
                case "Marabunta Grande": return Color.FromArgb(255, 84, 176, 176);
                case "The Lost": return Color.FromArgb(255, 176, 176, 176);
                case "Wei Cheng Triads": return Color.FromArgb(255, 200, 72, 72);
                case "Kkangpae": return Color.FromArgb(255, 188, 120, 60);
                case "Armenian Mob": return Color.FromArgb(255, 132, 132, 200);
                case "Rednecks": return Color.FromArgb(255, 170, 140, 90);
                case "LSPD": return Color.FromArgb(255, 96, 150, 214);
                case "LSFD": return Color.FromArgb(255, 214, 110, 60);
                case "Security": return Color.FromArgb(255, 140, 150, 160);
            }

            return Palette.Brand;
        }

        /// <summary>
        /// Where the game says he is from.
        ///
        /// Taken from the MODEL where it says so, because the game's gangs are drawn along
        /// those lines and pretending otherwise would put a name on the card that does not
        /// match the man stood in front of you. Everybody else is a straight deterministic
        /// pick, weighted no way at all.
        /// </summary>
        private static string Race(string model, ref uint seed)
        {
            foreach (var pair in Looks)
            {
                if (model.IndexOf(pair[0], StringComparison.OrdinalIgnoreCase) < 0) continue;

                return pair[1];
            }

            return Anybody[Roll(ref seed, Anybody.Length)];
        }

        private static readonly string[][] Looks =
        {
            new[] { "balla", Black },
            new[] { "famca", Black },
            new[] { "famdnf", Black },
            new[] { "famfor", Black },
            new[] { "afri", Black },
            new[] { "mexgoon", "Hispanic" },
            new[] { "mexgang", "Hispanic" },
            new[] { "mexlabor", "Hispanic" },
            new[] { "salva", "Hispanic" },
            new[] { "vagos", "Hispanic" },
            new[] { "korean", "Korean" },
            new[] { "ktown", "Korean" },
            new[] { "chigoon", "Chinese" },
            new[] { "chin", "Chinese" },
            new[] { "arm", "Armenian" },
            new[] { "indian", "South Asian" }
        };

        /// <summary>
        /// What the card calls it, in one place.
        ///
        /// IT SAID "BLACK", WHICH IS NOT WHAT A CARD SAYS. Every other line on that card is
        /// written the way an official one would write it -- a date in full, a height in feet
        /// and inches, an affiliation or None -- and the ethnicity was the one field written
        /// the way a witness statement would. The rest of the list is already in that register:
        /// Hispanic, Middle Eastern, South Asian.
        /// </summary>
        private const string Black = "African American";

        private static readonly string[] Anybody =
        {
            "White", Black, "Hispanic", "Asian", "Mixed", "Middle Eastern"
        };

        /// <summary>
        /// Whether this one is a man, by the model's own name first and the flag second.
        ///
        /// IS_PED_MALE IS NOT RELIABLE AND THE MODEL NAME IS. Every ped model Rockstar ship
        /// carries its sex in the middle of its name -- a_f_y_beach_01, s_m_y_cop_01,
        /// mp_f_freemode_01 -- and that convention has no exceptions in the list this machine
        /// has. The flag does: a woman on the pavement came up as Errol Hobbs, six foot five,
        /// because the flag said male and nothing else was asked.
        ///
        /// So the name decides where it says anything, and the flag is the fallback -- for an
        /// add-on ped, and for a machine with no name lists on it, where the name comes off
        /// the game's own enum and has no underscores in it to read. See Core.Names.
        /// </summary>
        private static bool Male(Ped who, string model)
        {
            if (!string.IsNullOrEmpty(model))
            {
                var name = model.ToLowerInvariant();

                if (name.Contains("_f_") || name.StartsWith("f_", StringComparison.Ordinal)) return false;
                if (name.Contains("_m_") || name.StartsWith("m_", StringComparison.Ordinal)) return true;
            }

            try { return Function.Call<bool>(Hash.IS_PED_MALE, who.Handle); }
            catch { return true; }
        }

        private static string Called(bool male, string race, ref uint seed)
        {
            var first = male ? MaleNames : FemaleNames;

            var last = string.Equals(race, "Hispanic", StringComparison.OrdinalIgnoreCase) ? Hispanic
                     : string.Equals(race, "Korean", StringComparison.OrdinalIgnoreCase) ? Korean
                     : string.Equals(race, "Chinese", StringComparison.OrdinalIgnoreCase) ? Chinese
                     : string.Equals(race, "Armenian", StringComparison.OrdinalIgnoreCase) ? Armenian
                     : Surnames;

            return first[Roll(ref seed, first.Length)] + " " + last[Roll(ref seed, last.Length)];
        }

        private static readonly string[] MaleNames =
        {
            "Darnell", "Marcus", "Terrell", "Andre", "Jamal", "Devon", "Keon", "Tyrone",
            "Lamar", "Curtis", "Ronnie", "Deshawn", "Malik", "Trevon", "Otis", "Wesley",
            "Hector", "Miguel", "Ramon", "Carlos", "Javier", "Ernesto", "Rafael", "Ruben",
            "Danny", "Wayne", "Craig", "Vernon", "Leon", "Errol", "Delroy", "Winston",
            "Kyle", "Brett", "Todd", "Shane", "Dale", "Gary", "Neil", "Duane"
        };

        private static readonly string[] FemaleNames =
        {
            "Tanisha", "Denise", "Latoya", "Keisha", "Yvette", "Simone", "Rochelle", "Andrea",
            "Marisol", "Carmen", "Yolanda", "Esperanza", "Lourdes", "Rosa", "Alma", "Consuelo",
            "Sharon", "Michelle", "Dawn", "Tracey", "Paula", "Bernice", "Loretta", "Faye"
        };

        private static readonly string[] Surnames =
        {
            "Wilkes", "Barnes", "Colley", "Mifflin", "Dupree", "Rand", "Hollis", "Beckett",
            "Vance", "Kearns", "Ostrander", "Pell", "Rutledge", "Sable", "Thurgood", "Wren",
            "Ashby", "Cutler", "Doyle", "Fenner", "Garrity", "Hobbs", "Ingram", "Judd",
            "Keane", "Lattimore", "Mabry", "Nash", "Orr", "Purvis", "Quill", "Reeves"
        };

        private static readonly string[] Hispanic =
        {
            "Delgado", "Ibarra", "Carrillo", "Mejia", "Salcedo", "Peralta", "Cuevas",
            "Herrera", "Robles", "Valdez", "Zamora", "Nava", "Orozco", "Padilla"
        };

        private static readonly string[] Korean =
        {
            "Park", "Choi", "Kwon", "Han", "Baek", "Jung", "Seo", "Yoon", "Shin", "Oh"
        };

        private static readonly string[] Chinese =
        {
            "Cheng", "Lau", "Ng", "Fung", "Tsang", "Yau", "Ho", "Kwan", "Sit", "Mak"
        };

        private static readonly string[] Armenian =
        {
            "Petrosyan", "Sarkisian", "Avakian", "Manukyan", "Hovsepian", "Zakarian"
        };

        /// <summary>
        /// A date of birth that puts him between seventeen and sixty-four.
        ///
        /// AGAINST THE GAME'S OWN YEAR, not against a number in a comment, so a card written
        /// on the same day the game is set does not say somebody was born after they died.
        /// </summary>
        private static string Birthday(ref uint seed)
        {
            var year = 2013;

            try { year = Function.Call<int>(Hash.GET_CLOCK_YEAR); }
            catch { /* the fallback is the year the game shipped set in */ }

            if (year < 1900 || year > 3000) year = 2013;

            var age = 17 + Roll(ref seed, 48);
            var month = 1 + Roll(ref seed, 12);
            var day = 1 + Roll(ref seed, 28);

            return day.ToString("00") + "/" + month.ToString("00") + "/" + (year - age);
        }

        /// <summary>Feet and inches, which is the unit an identity card in this city would use.</summary>
        private static string Tall(bool male, ref uint seed)
        {
            var inches = male ? 64 + Roll(ref seed, 14) : 60 + Roll(ref seed, 12);

            return (inches / 12) + "'" + (inches % 12) + "\"";
        }

        // ---- what was on him ----------------------------------------------------

        private void Pockets(Ped who, Body body, string model, ref uint seed)
        {
            var gang = !string.Equals(body.Affiliation, "None", StringComparison.OrdinalIgnoreCase);

            // HIS GUNS, ONLY IF THE SETTING SAYS SO -- and it says no by default, because
            // the game's own answer to a dead man with a rifle is a rifle on the pavement.
            // See Settings.LootGuns, and Search.Nobody, which is the other half of it: there
            // is no point taking a gun off a body that has already dropped it at your feet.
            if (_cfg != null && _cfg.LootGuns) Iron(who, body);
            Notes(body, gang, model, ref seed);
            Bite(body, ref seed);
            Powder(body, gang, ref seed);
        }

        /// <summary>
        /// Every weapon the game knows about, asked once and kept.
        ///
        /// THE GAME HAS NO CALL THAT LISTS WHAT SOMEBODY IS CARRYING. HAS_PED_GOT_WEAPON is
        /// the whole of the interface, so the only way to find out is to ask about each one
        /// in turn -- which means having a list to ask from. The mod this came from walked its
        /// own gun locker; this mod has no locker, so it walks the game's own enum, which
        /// SHVDN ships complete.
        ///
        /// BUILT ONCE FOR THE SESSION rather than per body. It is a hundred-odd entries and
        /// Enum.GetValues allocates; a corpse is searched often enough for that to matter and
        /// the list never changes.
        /// </summary>
        private static WeaponHash[] _guns;

        private static WeaponHash[] Guns()
        {
            if (_guns != null) return _guns;

            try
            {
                var all = (WeaponHash[])Enum.GetValues(typeof(WeaponHash));

                var keep = new List<WeaponHash>(all.Length);

                foreach (var gun in all)
                {
                    if (gun == WeaponHash.Unarmed) continue;

                    // NOTHING ELSE IS FILTERED OUT HERE, and the animal bites in this enum
                    // are the reason that is worth a line. A dog's bite is a weapon as far as
                    // the game is concerned, so the list this walks has several of them on it
                    // -- and asking a dead man whether he has one is a native call that
                    // answers no. The scan that finds the body is human-only anyway (see
                    // Search.Scan and the dead cat that came up as Keon Peralta), so the
                    // question never reaches an animal in the first place and a list trimmed
                    // by name would be a list of names to keep right forever.
                    keep.Add(gun);
                }

                _guns = keep.ToArray();
            }
            catch (Exception ex)
            {
                Log.Debug("Bodies: could not list the weapons: " + ex.Message);
                _guns = new WeaponHash[0];
            }

            return _guns;
        }

        /// <summary>
        /// The guns, and these are not invented.
        ///
        /// Asked of the man himself, one name at a time -- see Guns. The rounds come off him
        /// too, so a man who emptied a clip at you leaves a gun with what he had left in it.
        /// </summary>
        private static void Iron(Ped who, Body body)
        {
            try
            {
                foreach (var gun in Guns())
                {
                    var hash = unchecked((uint)gun);

                    if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, who.Handle, hash, false)) continue;

                    var rounds = 0;

                    try { rounds = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, who.Handle, hash); }
                    catch { /* it comes with nothing in it, then */ }

                    body.Items.Add(new LootItem
                    {
                        Kind = LootKind.Gun,
                        Id = gun.ToString(),
                        Hash = hash,
                        Name = GunName(gun),
                        Count = Math.Max(0, rounds),
                        Tag = rounds > 0 ? rounds + " RDS" : "EMPTY",
                        Tint = Color.FromArgb(255, 196, 200, 208)
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Bodies: could not read the guns: " + ex.Message);
            }
        }

        /// <summary>
        /// What the game calls that gun, in the player's own language.
        ///
        /// TWO STEPS AND BOTH CAN FAIL. SHVDN turns a weapon hash into the game's LABEL for it
        /// -- "WT_PIST" -- and the game turns that label into the words on screen, which are
        /// translated. Either can hand back nothing on a weapon the game has no entry for, and
        /// what comes out then is the enum's own name, which is at least English and at least
        /// the right gun.
        /// </summary>
        private static string GunName(WeaponHash gun)
        {
            try
            {
                var label = Weapon.GetDisplayNameFromHash(gun);

                if (!string.IsNullOrEmpty(label))
                {
                    var said = Game.GetLocalizedString(label);

                    if (!string.IsNullOrEmpty(said) && said != "NULL") return said;
                }
            }
            catch
            {
                // The enum's own name below.
            }

            return Spaced(gun.ToString());
        }

        /// <summary>"AssaultRifle" as "Assault Rifle". The enum names run the words together.</summary>
        private static string Spaced(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var sb = new System.Text.StringBuilder(name.Length + 6);

            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');

                sb.Append(name[i]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// A roll of notes.
        ///
        /// A GANG MEMBER IS CARRYING THE DAY'S MONEY and a man on his way home from work is
        /// carrying bus fare, which is the difference between a body worth going through and
        /// a body you learn not to bother with. Both are the same one deterministic number
        /// scaled differently.
        /// </summary>
        private static void Notes(Body body, bool gang, string model, ref uint seed)
        {
            var low = gang ? GangNotesMin : NotesMin;
            var high = gang ? GangNotesMax : NotesMax;

            // The two ends of the city, and the game names them plainly enough to use.
            if (model.IndexOf("business", StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("vinewood", StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("golfer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                low = 90;
                high = 700;
            }
            else if (model.IndexOf("hobo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     model.IndexOf("tramp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     model.IndexOf("downtown", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                low = 0;
                high = 18;
            }

            var notes = low + Roll(ref seed, Math.Max(1, high - low));
            if (notes <= 0) return;

            body.Items.Add(new LootItem
            {
                Kind = LootKind.Money,
                Name = "Cash",
                Count = notes,
                Tag = "$" + notes,
                Tint = Palette.Cash
            });
        }

        /// <summary>
        /// Something to eat or drink, now and then.
        ///
        /// OFF THIS MOD'S OWN SHELF, which is the one part of this that got simpler coming
        /// across: the loot used to have to ask over a bridge what food existed and what it
        /// was called. Anything a shop sells is something somebody could be carrying.
        /// </summary>
        private void Bite(Body body, ref uint seed)
        {
            if (_menu == null) return;
            if (Roll(ref seed, 100) >= FoodChance) return;

            var shelf = new List<Item>();

            foreach (var item in _menu.Items)
            {
                if (item == null || string.IsNullOrEmpty(item.Id)) continue;
                if (!item.InShop) continue;

                // KIT IS NOT FOOD. A bong is the one thing in the catalogue marked Keep, and
                // a random pedestrian is not carrying one.
                if (item.Keep) continue;

                shelf.Add(item);
            }

            if (shelf.Count == 0) return;

            var chosen = shelf[Roll(ref seed, shelf.Count)];

            body.Items.Add(new LootItem
            {
                Kind = LootKind.Food,
                Id = chosen.Id,
                Name = chosen.Name,
                Icon = Art.For(chosen),
                Count = 1,
                Tag = "1",
                Tint = chosen.Tint
            });
        }

        /// <summary>
        /// A bit of product, more often on somebody who was in the life.
        ///
        /// BAGGED AND STEPPED ON. What comes off a body is street weight at street purity --
        /// a personal amount somebody was carrying, not a lot off a plug -- so it goes into
        /// the packaged side of the other mod's pockets with a purity to match.
        ///
        /// AND ONLY WHEN THAT MOD IS HERE. Dope.Catalogue is empty without it, so a body on a
        /// machine with no drug mod on it simply has no product in his jacket.
        /// </summary>
        private static void Powder(Body body, bool gang, ref uint seed)
        {
            var all = Dope.Catalogue();
            if (all == null || all.Length == 0) return;

            if (Roll(ref seed, 100) >= (gang ? GangDrugChance : DrugChance)) return;

            var id = all[Roll(ref seed, all.Length)];
            if (string.IsNullOrEmpty(id)) return;

            var name = Dope.NameOf(id);
            if (string.IsNullOrEmpty(name)) return;

            var grams = 1f + Roll(ref seed, 14);
            var purity = 0.45f + Roll(ref seed, 40) / 100f;

            body.Items.Add(new LootItem
            {
                Kind = LootKind.Drug,
                Id = id,
                Name = name,
                Icon = Dope.IconOf(id),
                Grams = grams,
                Purity = purity,
                Tag = Dope.Label(id, grams),
                Tint = Dope.Effect(id).Tint
            });
        }

        // ---- taking it ----------------------------------------------------------

        /// <summary>
        /// Moves one thing off the body and onto you. False, with a reason, when it will not
        /// go -- and nothing is taken off the body when it does not.
        /// </summary>
        public bool Take(Body body, LootItem item, out string why)
        {
            why = "";

            if (body == null || item == null) { why = "nothing there"; return false; }

            var me = Game.Player.Character;

            if (me == null || !me.Exists()) { why = "not right now"; return false; }

            try
            {
                switch (item.Kind)
                {
                    case LootKind.Gun:
                        if (item.Hash == 0) { why = "no such gun"; return false; }

                        Function.Call(Hash.GIVE_WEAPON_TO_PED, me.Handle, item.Hash,
                                      Math.Max(0, item.Count), false, false);
                        break;

                    case LootKind.Money:
                        try { Game.Player.Money += item.Count; }
                        catch { why = "could not count it"; return false; }
                        break;

                    case LootKind.Food:
                        // THE ONE RULE FOR WHERE A THING GOES: the pocket, then the bag on his
                        // back. See Food.Stow, which the counter and the stalls also ask.
                        if (Stow.Put(_pantry, _knapsack, item.Id, Math.Max(1, item.Count)) ==
                            Stow.Where.Nowhere)
                        {
                            why = Knapsack.Worn ? "no room in your pockets or the bag"
                                                : "no room in your pockets";
                            return false;
                        }
                        break;

                    case LootKind.Drug:
                        // PART OF IT IS STILL A TAKE. A pocket with room for four grams of a
                        // six-gram bag takes four and leaves two, which is what a pocket does.
                        var took = Dope.Loot(item.Id, item.Grams, item.Purity);

                        if (took <= 0.005f)
                        {
                            why = "no room on you";

                            Log.Info("Loot: would not take " + item.Grams.ToString("0.#") + "g of " +
                                     item.Name + " off " + body.Name + " -- " + why + " (" +
                                     Dope.Carried.ToString("0.#") + "g of " +
                                     Dope.Capacity.ToString("0") + "g on him).");

                            return false;
                        }

                        Log.Info("Loot: " + took.ToString("0.#") + "g of " + item.Name + " off " +
                                 body.Name + " -- now carrying " + Dope.Carried.ToString("0.#") +
                                 "g of " + Dope.Capacity.ToString("0") + "g.");

                        if (took < item.Grams - 0.005f)
                        {
                            item.Grams -= took;
                            item.Tag = Dope.Label(item.Id, item.Grams);
                            return true;
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Bodies: could not take " + item.Name + ": " + ex.Message);
                why = "it would not come";
                return false;
            }

            body.Items.Remove(item);

            if (item.Kind != LootKind.Drug)
            {
                Log.Info("Loot: took the " + item.Name +
                         (string.IsNullOrEmpty(item.Tag) ? "" : " (" + item.Tag + ")") +
                         " off " + body.Name + ".");
            }

            return true;
        }
    }
}
