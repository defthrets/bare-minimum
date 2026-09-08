using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using GTA;

using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// What is in your pockets that did not come from a shop.
    ///
    /// POSTED UP ALREADY SHOWS THIS MOD'S FOOD ON ITS PHONE, through its own Core/Larder,
    /// which reads BareMinimum.Api.Pantry by reflection. This is that arrangement turned
    /// round: the drugs you are carrying over there show up in the pocket over here, and
    /// taking one from this side does the same thing as taking it from that side.
    ///
    /// THE OTHER MOD OWNS THE DRUGS AND THIS ONE OWNS THE BODY, which is the whole division.
    /// How much you have, whether you are allowed another and what being high does to your
    /// walk are all questions for Posted Up, and Take asks it rather than answering. What a
    /// gram does to a stomach and to a night's sleep is this mod's only subject, so the table
    /// at the bottom of this file lives here and nowhere else.
    ///
    /// LATE-BOUND, and for the reason Larder spells out coming the other way: a GTA scripts\
    /// folder is one assembly resolution namespace, so two mods that both reference a third
    /// assembly must agree about its version forever, and the day they stop agreeing the
    /// failure is a TypeLoadException with no log -- because the thing that writes the log is
    /// the thing that did not load. Nothing here references Hoodrich.dll, nothing here fails
    /// to compile without it, and the answer to "not installed" is Present == false and an
    /// empty pocket, which is exactly what the pocket looked like before this file existed.
    ///
    /// IT RETRIES RATHER THAN RESOLVING ONCE. SHVDN builds scripts in whatever order it finds
    /// them, so on about half of all launches this mod is constructed before Posted Up exists
    /// in the AppDomain at all. A bridge that looked once at startup would be permanently
    /// absent on those launches only, which is the worst kind of bug to be sent: intermittent,
    /// and not reproducible by the person who wrote it. Learned twice already -- once on the
    /// Precinct 88 bridge and once on Larder -- and written down here so it is not learned a
    /// third time.
    /// </summary>
    internal static class Dope
    {
        private const string Assembly = "Hoodrich";
        private const string TypeName = "Hoodrich.Api.Drugs";

        /// <summary>The contract this code was written against.</summary>
        private const int WantApi = 1;

        private const int GiveUpAfterMs = 30000;
        private const int RetryEveryMs = 2000;

        private static Type _type;
        private static bool _gaveUp;
        private static int _nextTry;
        private static int _firstTry;

        private static PropertyInfo _ready, _unit, _carried, _capacity;
        private static MethodInfo _ids, _gramsOf, _nameOf, _iconOf, _countedOf, _amountOf, _use;

        // ======================================================================
        // The bridge
        // ======================================================================

        /// <summary>Whether Posted Up is here AND has finished starting up.</summary>
        public static bool Present
        {
            get
            {
                var type = Resolve();
                if (type == null) return false;

                try { return _ready != null && (bool)_ready.GetValue(null, null); }
                catch { return false; }
            }
        }

        private static Type Resolve()
        {
            if (_type != null) return _type;
            if (_gaveUp) return null;

            int now;
            try { now = Game.GameTime; }
            catch { return null; }

            if (_firstTry == 0) _firstTry = now;
            if (now < _nextTry) return null;

            _nextTry = now + RetryEveryMs;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != Assembly) continue;

                    var type = asm.GetType(TypeName);
                    if (type == null) continue;

                    // READ BEFORE ANYTHING ELSE, which is what makes the other side free to
                    // change later: an old Bare Minimum against a new Posted Up sees a number
                    // it does not recognise and quietly shows no drugs, rather than
                    // half-calling an API that has moved underneath it.
                    var api = type.GetProperty("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                    var have = api == null ? 0 : (int)api.GetValue(null, null);

                    if (have != WantApi)
                    {
                        Log.Info("Posted Up speaks API v" + have + " and this wants v" + WantApi +
                                 ". No drugs in the pocket.");
                        _gaveUp = true;
                        return null;
                    }

                    _ready = type.GetProperty("Ready", BindingFlags.Public | BindingFlags.Static);
                    _unit = type.GetProperty("Unit", BindingFlags.Public | BindingFlags.Static);
                    _carried = type.GetProperty("Carried", BindingFlags.Public | BindingFlags.Static);
                    _capacity = type.GetProperty("Capacity", BindingFlags.Public | BindingFlags.Static);

                    _ids = type.GetMethod("Ids", BindingFlags.Public | BindingFlags.Static);
                    _gramsOf = type.GetMethod("GramsOf", BindingFlags.Public | BindingFlags.Static);
                    _nameOf = type.GetMethod("NameOf", BindingFlags.Public | BindingFlags.Static);
                    _iconOf = type.GetMethod("IconOf", BindingFlags.Public | BindingFlags.Static);
                    _countedOf = type.GetMethod("CountedOf", BindingFlags.Public | BindingFlags.Static);
                    _amountOf = type.GetMethod("AmountOf", BindingFlags.Public | BindingFlags.Static);
                    _use = type.GetMethod("Use", BindingFlags.Public | BindingFlags.Static);

                    _type = type;

                    var version = type.GetProperty("Version", BindingFlags.Public | BindingFlags.Static);
                    Log.Info("Posted Up " +
                             (version == null ? "?" : version.GetValue(null, null) as string) +
                             " found. What you are holding will show in the pocket.");

                    return _type;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not look for Posted Up: " + ex.Message);
            }

            if (now - _firstTry > GiveUpAfterMs)
            {
                _gaveUp = true;
                Log.Info("Posted Up is not installed. Nothing but food in the pocket.");
            }

            return null;
        }

        // ======================================================================
        // What is on you
        // ======================================================================

        /// <summary>Everything street-ready in your pockets. Never null, empty without the other mod.</summary>
        public static string[] Ids()
        {
            try
            {
                if (!Present || _ids == null) return new string[0];
                return _ids.Invoke(null, null) as string[] ?? new string[0];
            }
            catch { return new string[0]; }
        }

        public static float GramsOf(string id)
        {
            try
            {
                if (!Present || _gramsOf == null) return 0f;
                return (float)_gramsOf.Invoke(null, new object[] { id });
            }
            catch { return 0f; }
        }

        public static string NameOf(string id)
        {
            try
            {
                if (!Present || _nameOf == null) return id ?? "";
                var name = _nameOf.Invoke(null, new object[] { id }) as string;
                return string.IsNullOrEmpty(name) ? (id ?? "") : name;
            }
            catch { return id ?? ""; }
        }

        /// <summary>
        /// The full path of that drug's picture, straight out of the other mod's icon folder.
        ///
        /// A PATH, AND IT NEEDS NOTHING ELSE. UI.Icon builds its own path with Path.Combine
        /// against this mod's icons folder, and Path.Combine hands a rooted path back
        /// untouched -- so a picture belonging to a different mod draws here with no change to
        /// Icon at all. The same trick Posted Up uses to draw our food on its phone.
        /// </summary>
        public static string IconOf(string id)
        {
            try
            {
                if (!Present || _iconOf == null) return "";
                return _iconOf.Invoke(null, new object[] { id }) as string ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// How much of it, written the way that drug counts itself -- "3 pills", "3.4g".
        ///
        /// ASKED RATHER THAN FORMATTED HERE. Which drugs come as pills and which come as
        /// powder is the other mod's business and changes when its drugs.json changes, and a
        /// pill labelled "1g" is the kind of small wrongness that makes a bridge look broken.
        /// </summary>
        public static string Label(string id)
        {
            var grams = GramsOf(id);

            try
            {
                if (Present && _amountOf != null)
                {
                    var said = _amountOf.Invoke(null, new object[] { id, grams }) as string;
                    if (!string.IsNullOrEmpty(said)) return said;
                }
            }
            catch { /* fall through to the plain number */ }

            return grams.ToString("0.#");
        }

        /// <summary>The number for the corner of a tile: whole pills, or grams to one place.</summary>
        public static string Chip(string id)
        {
            var grams = GramsOf(id);

            try
            {
                if (Present && _countedOf != null &&
                    (bool)_countedOf.Invoke(null, new object[] { id }))
                {
                    return ((int)Math.Round(grams)).ToString();
                }
            }
            catch { /* then it is weighed, like most of them */ }

            return grams.ToString("0.#");
        }

        /// <summary>Everything on you against what you can carry, for the panel head.</summary>
        public static float Carried
        {
            get
            {
                try { return !Present || _carried == null ? 0f : (float)_carried.GetValue(null, null); }
                catch { return 0f; }
            }
        }

        public static float Capacity
        {
            get
            {
                try { return !Present || _capacity == null ? 0f : (float)_capacity.GetValue(null, null); }
                catch { return 0f; }
            }
        }

        /// <summary>One of whatever it counts itself in. A gram, or a pill.</summary>
        public static float Unit
        {
            get
            {
                try { return !Present || _unit == null ? 1f : (float)_unit.GetValue(null, null); }
                catch { return 1f; }
            }
        }

        /// <summary>Whether there is enough of it left to take one.</summary>
        public static bool Enough(string id)
        {
            return GramsOf(id) >= Unit - 0.001f;
        }

        // ======================================================================
        // Taking one
        // ======================================================================

        /// <summary>
        /// Take one, and let it land on the body.
        /// </summary>
        ///
        /// <remarks>
        /// THE OTHER MOD DOES THE DRUG AND THIS ONE DOES THE BODY. Api.Drugs.Use is the same
        /// call its own pocket screen makes -- it refuses if he has had enough, takes the
        /// weight off him, plays the ritual and starts the high. None of that is reimplemented
        /// here, because a second implementation of one act is how you end up high twice, or
        /// high on a gram you still have.
        ///
        /// THE HUNGER AND THE SLEEP ARE OURS and land only after Use says it happened. A
        /// refusal has to leave the meters exactly where it found them, or a man who was told
        /// he had had enough would still be paying for it.
        ///
        /// Returns null when it landed, or the other mod's own sentence for why it did not --
        /// already written for a screen, so there is no wording to invent here.
        /// </remarks>
        public static string Take(Needs.Needs needs, string id)
        {
            if (!Present || _use == null) return "Not carrying that";
            if (needs == null) return "Not ready";

            var effect = Effect(id);

            var fedBefore = needs.Hunger.Value;
            var restedBefore = needs.Sleep.Value;

            string no;

            try { no = _use.Invoke(null, new object[] { id }) as string; }
            catch (Exception ex)
            {
                Log.Debug("Posted Up would not take " + id + ": " + ex.Message);
                return "Could not";
            }

            if (no != null) return no;

            // Restore and Drain are the two directions of the same thing, and each only
            // accepts its own sign -- so the sign picks the call rather than being passed in.
            if (effect.Hunger > 0f) needs.Hunger.Restore(effect.Hunger);
            if (effect.Wake > 0f) needs.Sleep.Restore(effect.Wake);

            if (effect.Hunger < 0f || effect.Wake < 0f)
            {
                needs.Drain(effect.Hunger < 0f ? -effect.Hunger : 0f,
                            effect.Wake < 0f ? -effect.Wake : 0f);
            }

            // UP, AND THE BAR SAYS SO. A stimulant pins the energy at the top and sets it
            // shimmering for as long as it rides: no drain from sprinting, and none from the
            // special ability either, because the bar it drinks from is not going down.
            if (effect.Wired > 0f && Rush != null)
            {
                try { Rush(effect.Wired); }
                catch (Exception ex) { Log.Debug("The rush did not reach the bar: " + ex.Message); }
            }

            Report(id, effect, fedBefore, restedBefore, needs);

            return null;
        }

        /// <summary>
        /// The same card a meal puts up, for the same reason: something went into him and the
        /// mod that keeps the meters should say what it did to them.
        ///
        /// THE BAR SHOWS WHICHEVER METER MOVED MOST. Almost everything here is a stimulant and
        /// moves the sleep meter hardest, but weed moves the stomach and nothing else worth
        /// drawing -- and a bar that did not visibly move is the mod saying nothing happened.
        /// </summary>
        private static void Report(string id, Dose effect, float fedBefore, float restedBefore,
                                   Needs.Needs needs)
        {
            try
            {
                var hunger = effect.Hunger < 0f ? -effect.Hunger : effect.Hunger;
                var wake = effect.Wake < 0f ? -effect.Wake : effect.Wake;

                var sleepy = wake >= hunger;

                var line = effect.Wake > 0f
                             ? "Rested " + Pct(needs.Sleep.Value) + "%."
                             : effect.Wake < 0f
                                 ? "Heavy. Rested " + Pct(needs.Sleep.Value) + "%."
                                 : "";

                if (effect.Hunger < 0f)
                {
                    line += (line.Length > 0 ? " " : "") + "Hungrier -- fed " +
                            Pct(needs.Hunger.Value) + "%.";
                }
                else if (effect.Hunger > 0f)
                {
                    line += (line.Length > 0 ? " " : "") + "Fed " + Pct(needs.Hunger.Value) + "%.";
                }

                if (line.Length == 0) line = "That is that.";

                UI.Toast.Show(IconOf(id), NameOf(id).ToUpperInvariant(), line, effect.Tint,
                              sleepy ? restedBefore : fedBefore,
                              sleepy ? needs.Sleep.Value : needs.Hunger.Value,
                              2500);
            }
            catch
            {
                // Not worth failing the dose over.
            }
        }

        private static int Pct(float v)
        {
            return (int)Math.Round(v * 100f);
        }

        // ======================================================================
        // What it does to a body
        // ======================================================================

        /// <summary>One drug's cost, as fractions of the whole meter, either way.</summary>
        internal struct Dose
        {
            public float Hunger;
            public float Wake;

            /// <summary>Real minutes it holds the energy bar at the top, shimmering. Nought for the downers.</summary>
            public float Wired;

            public Color Tint;
            public string Desc;
        }

        /// <summary>
        /// What a stimulant does to the energy bar. Wired by Main, because this file knows the
        /// drug and the vitals own the meter, and neither needs to know the other exists -- the
        /// same arrangement a drink already has through Eating.Drank.
        ///
        /// ONLY WHAT GOES THROUGH THIS POCKET. A dose taken on Posted Up's own phone screen
        /// never reaches this method, so the bar will not know about it. The other mod does not
        /// say how long its high runs, so there is nothing to ask; the minutes in the table
        /// below are this mod's own reckoning of how long each one rides.
        /// </summary>
        public static Action<float> Rush;

        /// <summary>
        /// What each one does, and it is not a table of medicine.
        /// </summary>
        ///
        /// <remarks>
        /// TWO CAMPS, AND EVERYTHING IN ONE RUNS THE SAME WAY. The downers put you under and
        /// leave you hungry; the uppers stand you up and put ten per cent back on the stomach.
        ///
        /// IT USED TO SAY THE OPPOSITE FOR HALF THE LIST, on a reading of the meters that was
        /// too clever by half: a stimulant does not literally feed anybody, so it took hunger
        /// away and handed sleep back, and a sedative was called restful because rest is what
        /// people take one for. Both of those are arguments. Neither is what happens in front
        /// of you -- take a xanax and you are tired, take meth and you are up, and you are not
        /// thinking about dinner because you feel fine.
        ///
        /// WAKE = 1 MEANS FULL, not "add one to it". Restore clamps at the top of the meter,
        /// so a whole number fills it from wherever it happened to be. That is deliberate and
        /// it is the biggest thing in this file: meth and the rest of the uppers ARE a night's
        /// sleep, not a top-up towards one, so anybody holding product need never go to bed.
        /// The counterweight is not on this side and does not need to be -- Posted Up refuses
        /// a fourth dose, and an overdose comes back through Api.Pantry.Drain taking far more
        /// off both meters than any of this puts on.
        ///
        /// IN A TABLE RATHER THAN A JSON, unlike everything else this mod reads. The ids are
        /// not ours: they come out of the other mod's drugs.json, and a file inviting somebody
        /// to add "fentanyl" here would be a file whose entries mostly do nothing, because
        /// nothing over there is carrying one. An id we have never heard of still works -- see
        /// Effect -- it simply does nothing to the meters.
        ///
        /// AN ENTRY MAY LEAD THE CATALOGUE. oxycodone is written down here and Posted Up does
        /// not stock it, so today it is a row that never matches anything. That costs nothing
        /// and means the day it is added over there it already behaves, rather than silently
        /// doing nothing to either meter until somebody notices.
        /// </remarks>
        private static readonly Dictionary<string, Dose> Doses =
            new Dictionary<string, Dose>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- down: under, and hungry with it ----

            { "xanax", new Dose {
                Hunger = -0.08f, Wake = -0.35f,
                Tint = Color.FromArgb(255, 190, 206, 232),
                Desc = "Puts you under. You will not stay up." } },

            { "heroin", new Dose {
                Hunger = -0.15f, Wake = -0.30f,
                Tint = Color.FromArgb(255, 168, 130, 96),
                Desc = "A nod, and nothing else matters." } },

            // Not in Posted Up's drugs.json today. Here so that it behaves the day it is.
            { "oxycodone", new Dose {
                Hunger = -0.12f, Wake = -0.30f,
                Tint = Color.FromArgb(255, 228, 212, 158),
                Desc = "A warm nod. Everything slows down." } },

            // The heaviest stomach on the list and the lightest head. Weed does not put you
            // out the way the other three do -- it makes you heavy, and it makes you eat.
            { "weed", new Dose {
                Hunger = -0.16f, Wake = -0.25f,
                Tint = Color.FromArgb(255, 126, 178, 96),
                Desc = "The munchies, and a heavy head." } },

            // ---- up: wide awake, a tenth back on the stomach, and the energy bar pinned ----
            //
            // WIRED IS IN REAL MINUTES, and the numbers are the shape of the drug rather than
            // its strength: meth rides for the best part of a quarter of an hour, ecstasy most
            // of that, coke is a short hard hit and crack shorter still. While it rides the
            // energy bar sits at the top and shimmers -- see Energy.Hold and Paint.Third -- so
            // there is no running out of breath and no running out of special ability. The
            // downers get none of it; a xanax is not a reason to be able to sprint all day.
            { "meth", new Dose {
                Hunger = 0.10f, Wake = 1f, Wired = 13f,
                Tint = Color.FromArgb(255, 150, 205, 230),
                Desc = "Days awake. Nothing gets you down." } },

            { "coke", new Dose {
                Hunger = 0.10f, Wake = 1f, Wired = 6f,
                Tint = Color.FromArgb(255, 238, 238, 244),
                Desc = "Wide awake, and suddenly fine." } },

            // CRACK AND ECSTASY WERE NOT NAMED and are here anyway, because they are the same
            // camp and half a rule is worse than either whole one -- a man who has just learnt
            // that uppers stand him up should not find that two of them do not.
            { "crack", new Dose {
                Hunger = 0.10f, Wake = 1f, Wired = 4f,
                Tint = Color.FromArgb(255, 226, 206, 168),
                Desc = "Sharp and short, and you are up." } },

            { "ecstasy", new Dose {
                Hunger = 0.10f, Wake = 1f, Wired = 10f,
                Tint = Color.FromArgb(255, 212, 122, 196),
                Desc = "Up all night, and glad about it." } },
        };

        /// <summary>
        /// What this one does. Something harmless for anything not on the list.
        ///
        /// A DRUG WE HAVE NEVER HEARD OF STILL APPEARS AND IS STILL TAKEABLE -- it just does
        /// nothing to the meters. The other mod can add one to its drugs.json without this mod
        /// being updated, and the failure of that is a bag that does not make you hungry
        /// rather than a bag that cannot be picked up.
        /// </summary>
        internal static Dose Effect(string id)
        {
            Dose dose;

            if (id != null && Doses.TryGetValue(id, out dose)) return dose;

            return new Dose
            {
                Hunger = 0f,
                Wake = 0f,
                Tint = Color.FromArgb(255, 235, 235, 240),
                Desc = "Whatever it is, it is on you."
            };
        }
    }
}
