using System;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// The bag as a thing you can SEE on his back.
    ///
    /// WHAT IS IN IT IS THE KNAPSACK'S BUSINESS AND WHAT IT LOOKS LIKE IS THIS ONE'S. Two
    /// files because they answer to two different things: the contents are saved state that
    /// has to survive a restart, and a prop welded to a spine bone survives nothing at all.
    ///
    /// IT REPLACES A VEST DRAWABLE, AND THAT IS THE WHOLE POINT OF IT. Posted Up has carried
    /// this bag since long before this file, and it wore it as component nine, drawable seven
    /// -- the strap across Franklin's chest. That has three faults you cannot fix from inside
    /// it: there is exactly ONE of it, so every bag in the game looks the same; it cannot be
    /// moved a millimetre, because a drawable is worn where the artist put it; and the number
    /// of drawables in a slot CHANGES WITH THE TORSO, so on half the outfits in the game
    /// asking for seven asks for nothing and he wears no bag at all while every other part of
    /// the mod insists he is carrying one.
    ///
    /// A REAL PROP HAS NONE OF THOSE. It is a model, so there can be a dozen; it is attached,
    /// so it can be moved; and it is the same on every outfit and every character. What it
    /// costs is that somebody has to say WHERE, for each model, which is the one thing that
    /// cannot be worked out on paper -- see Where and the tuner below, and the same argument
    /// at the top of UI.FitScreen about food in a hand and Bodies.DragPose about a body in
    /// his arms. This is the third time this mod has had to solve it and it is solved the
    /// same way each time: put the thing on, look at it, and move it a centimetre at a time.
    ///
    /// WHOSE BAG IT IS HAS NOT MOVED. Whether there is a bag at all is still decided next
    /// door -- it holds that mod's product, it lives in that mod's save, and it is that mod's
    /// bag lying on the pavement when you put it down. This reads the same "worn" that the
    /// shelf reads (see Knapsack.Worn) and draws it. What it publishes back is one fact: that
    /// a real bag is being drawn, so the strap on the chest can stand down rather than the
    /// player wearing both.
    /// </summary>
    internal sealed class Strap
    {
        /// <summary>
        /// Where the fact that a real bag is drawn is left for the mod that owns the bag.
        ///
        /// THE SAME APPDOMAIN CHANNEL EVERYTHING ELSE ON THIS MACHINE USES, and plain types
        /// for the same reason: SHVDN loads every script into one AppDomain, so this needs no
        /// reference and no version agreement between two assemblies. One int: 1 while a bag
        /// model is actually on his back.
        ///
        /// IT ONLY EVER SAYS YES WHILE THE PROP EXISTS. Saying it from the setting instead
        /// would take the strap off the chest on a build where the model would not load, and
        /// he would be carrying a bag nobody can see at all -- which is worse than the strap.
        /// </summary>
        private const string Shown = "spitmux.bag.shown";

        /// <summary>
        /// Which bag he chose, and where it lies on the ground, for the mod that puts it there.
        ///
        /// THE BAG ON THE PAVEMENT IS THAT MOD'S PROP, and it made its own duffel whatever was
        /// on his back, because nothing told it otherwise -- Michael, 2026-09-26. A model name
        /// and six numbers, plain types for the reasons on Shown. Empty and null while this is
        /// not drawing bags at all, so the other side goes back to its own.
        /// </summary>
        private const string ModelOut = "spitmux.bag.model";
        private const string GroundOut = "spitmux.bag.ground";

        /// <summary>And whether a bag is actually lying on the ground, written by the other side.</summary>
        private const string FloorIn = "spitmux.bag.floor";

        /// <summary>
        /// The bags, best first, every name checked against this machine's own prop list.
        ///
        /// NOT GUESSED. Menyoo ships all twenty-two thousand object names as plain text and
        /// every one of these was read out of it -- see the note in CLAUDE.md about names
        /// being checkable and positions not. A name that is not in THIS build still costs
        /// nothing: Make walks the list and the first one that loads wins, so a player on an
        /// install without the newer packs gets the older backpack instead of no bag.
        ///
        /// THE _s ONES ARE PED PROPS. p_ld_heist_bag_s is the duffel the game itself hangs on
        /// a man's back in the heists, which is why it is near the top: it was made to be worn
        /// rather than made to sit on a shelf, and it shows.
        /// </summary>
        private static readonly string[][] Bags =
        {
            new[] { "prop_michael_backpack",      "BACKPACK" },
            new[] { "p_michael_backpack_s",       "BACKPACK, WORN" },
            new[] { "p_ld_heist_bag_s",           "HEIST DUFFEL" },
            new[] { "hei_p_m_bag_var22_arm_s",    "ARMOURED BAG" },
            new[] { "sf_prop_sf_backpack_01a",    "DAY PACK" },
            new[] { "sf_prop_sf_backpack_02a",    "DAY PACK 2" },
            new[] { "sf_prop_sf_backpack_03a",    "DAY PACK 3" },
            new[] { "vw_prop_vw_backpack_01a",    "CASINO PACK" },
            new[] { "xm3_prop_xm3_backpack_01a",  "TACTICAL PACK" },
            new[] { "h4_prop_h4_michael_backpack","ISLAND PACK" },
            new[] { "bkr_prop_duffel_bag_01a",    "DUFFEL" },
            new[] { "ch_prop_ch_duffelbag_01x",   "DUFFEL 2" },
            new[] { "prop_cs_duffel_01",          "HOLDALL" },
            new[] { "p_tennis_bag_01_s",          "SPORTS BAG" },
            new[] { "prop_ld_case_01",            "CASE" },
        };

        /// <summary>What each one is called on a picker. Same order as Bags.</summary>
        public static string[] Names
        {
            get
            {
                var made = new string[Bags.Length];
                for (var i = 0; i < Bags.Length; i++) made[i] = Bags[i][1];

                return made;
            }
        }

        /// <summary>The model names, in the same order, for the ini's own list.</summary>
        public static string[] Models
        {
            get
            {
                var made = new string[Bags.Length];
                for (var i = 0; i < Bags.Length; i++) made[i] = Bags[i][0];

                return made;
            }
        }

        public static int Count => Bags.Length;

        /// <summary>
        /// The bag a fresh install starts on: the tactical pack, at Michael's word.
        ///
        /// BY NAME RATHER THAN BY NUMBER. Its index is 8 today and would be 9 the moment
        /// anybody puts another bag above it in the list, and a default that silently becomes
        /// a different object because a list grew is the kind of thing nobody thinks to check.
        /// The name cannot drift.
        /// </summary>
        private const string Favourite = "xm3_prop_xm3_backpack_01a";

        public static int Default
        {
            get
            {
                for (var i = 0; i < Bags.Length; i++)
                {
                    if (string.Equals(Bags[i][0], Favourite, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }

                return 0;
            }
        }

        /// <summary>
        /// WHERE EACH BAG SITS, AND THESE ARE DIALLED RATHER THAN GUESSED.
        ///
        /// SEVEN OF THEM ARE MICHAEL'S, off the numpad on 2026-09-22, in the order he did
        /// them. They were nearly lost: the tuner's write key printed them to the log and
        /// saved nothing, and the settings page had overwritten the ini's table with a bare
        /// "-50" -- see the note on Save below and SettingsPanel's axis rows. They were read
        /// back out of the log line at 19:11:07 and they are the defaults now, so a fresh
        /// install has them and no ini is needed to get them.
        ///
        /// THE OTHER EIGHT ARE THE MEDIAN OF THOSE SEVEN, at his word: any he had not set,
        /// put in the same place as the ones he had. Nought across, seventeen centimetres
        /// behind, level, rolled ninety. It is a far better start than the one it replaced
        /// and it is still only a start -- a duffel is not a backpack, and the tuner is how
        /// each one stops being a median and becomes its own line.
        /// </summary>
        private static readonly float[] Start = { 0f, -0.17f, 0f, 0f, 90f, 0f };

        /// <summary>
        /// The ones that have actually been looked at. Model name, then the six.
        ///
        /// READ BEFORE THE INI, NOT INSTEAD OF IT. A line in [Bag] Fit still wins -- these
        /// are what a model falls back to when nobody has moved it, which for seven of them
        /// is somebody having already moved it here. See Where.
        /// </summary>
        private static readonly object[][] Dialled =
        {
            new object[] { "p_michael_backpack_s",      new[] {  0.05f, -0.15f,  0.12f,  -10f, 100f,  -5f } },
            new object[] { "p_ld_heist_bag_s",          new[] {  0.00f, -0.18f,  0.00f,    0f,   0f,   0f } },
            new object[] { "sf_prop_sf_backpack_01a",   new[] { -0.19f, -0.20f,  0.00f,    0f,  90f,   5f } },
            new object[] { "sf_prop_sf_backpack_02a",   new[] { -0.02f, -0.13f,  0.01f, -170f,  90f,   5f } },
            new object[] { "sf_prop_sf_backpack_03a",   new[] {  0.00f, -0.17f,  0.00f,  175f, 105f,   0f } },
            new object[] { "vw_prop_vw_backpack_01a",   new[] { -0.26f, -0.08f,  0.00f, -175f,  90f,  10f } },
            new object[] { "xm3_prop_xm3_backpack_01a", new[] {  0.01f, -0.19f,  0.02f,   -5f,  95f, -80f } },
        };

        /// <summary>
        /// Where each bag lies on the ground, once somebody has looked. Empty until Michael
        /// dials them -- he said he would -- and then his numbers go here the way the seven
        /// on his back went into Dialled. A model with no line lies exactly as it always has.
        /// </summary>
        private static readonly object[][] DialledGround =
        {
        };

        /// <summary>The ground six for the model now picked: the ini's, then the dialled, then nought.</summary>
        public static float[] GroundWhere(Core.Settings cfg)
        {
            var name = Picked(cfg);

            float[] six;
            if (cfg != null && cfg.BagGround.TryGetValue(name, out six) && six != null && six.Length >= 6)
            {
                return six;
            }

            foreach (var row in DialledGround)
            {
                if (!string.Equals((string)row[0], name, StringComparison.OrdinalIgnoreCase)) continue;

                var d = (float[])row[1];
                return new[] { d[0], d[1], d[2], d[3], d[4], d[5] };
            }

            return new float[6];
        }

        /// <summary>Moves one of the ground six for the model now picked. See Move.</summary>
        public static void MoveGround(Core.Settings cfg, int which, float to)
        {
            if (cfg == null || which < 0 || which > 5) return;

            var name = Picked(cfg);

            float[] six;
            if (!cfg.BagGround.TryGetValue(name, out six) || six == null || six.Length < 6)
            {
                six = GroundWhere(cfg);
                cfg.BagGround[name] = six;
            }

            six[which] = to;

            Dirty = true;
        }

        /// <summary>The dialled six for a model, or null when nobody has done that one.</summary>
        private static float[] Known(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            foreach (var row in Dialled)
            {
                if (!string.Equals((string)row[0], name, StringComparison.OrdinalIgnoreCase)) continue;

                var six = (float[])row[1];

                // A COPY, NEVER THE ARRAY ITSELF, or the first nudge on the settings page
                // edits the baked default and there is nothing left to go back to.
                return new[] { six[0], six[1], six[2], six[3], six[4], six[5] };
            }

            return null;
        }

        /// <summary>
        /// SKEL_Spine3, between the shoulder blades, which is where a bag hangs from.
        ///
        /// THE SAME BONE THE BODY CARRY SETTLED ON, and for the same reason: it turns when he
        /// turns and it does not swing. A hand bone swings through a full arc every stride,
        /// so anything measured from one is lined up perfectly while stood still and somewhere
        /// else entirely the moment he walks. See Bodies.DragPose.Bone, which learnt this the
        /// expensive way.
        /// </summary>
        private static readonly int[] Bones = { 24818, 0, 40269, 45509 };

        private static readonly string[] BoneNames =
        {
            "BETWEEN THE SHOULDER BLADES", "his root", "his left shoulder", "his right shoulder"
        };

        public static string[] BoneLabels => BoneNames;

        private readonly Core.Settings _cfg;

        private Prop _thing;

        /// <summary>Which model is on his back, so a change of mind is noticed.</summary>
        private string _wearing = "";

        /// <summary>Said once per model that will not load, rather than once a frame.</summary>
        private bool _moaned;

        public Strap(Core.Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>The model name the settings have picked, whatever this build makes of it.</summary>
        public static string Picked(Core.Settings cfg)
        {
            if (cfg == null) return Bags[0][0];

            var at = cfg.BagModel;
            if (at < 0 || at >= Bags.Length) at = 0;

            return Bags[at][0];
        }

        /// <summary>The six numbers for the model now picked: its own, or the starting point.</summary>
        public static float[] Where(Core.Settings cfg)
        {
            var name = Picked(cfg);

            float[] six;
            if (cfg != null && cfg.BagFit.TryGetValue(name, out six) && six != null && six.Length >= 6)
            {
                return six;
            }

            // THEN THE ONES THAT HAVE BEEN LOOKED AT, and only then the median. See Dialled.
            var known = Known(name);
            if (known != null) return known;

            // A COPY, NEVER THE ARRAY ITSELF. Handing back Start would let the first nudge on
            // the settings page edit the default for every model that has not been dialled.
            return new[] { Start[0], Start[1], Start[2], Start[3], Start[4], Start[5] };
        }

        /// <summary>
        /// Moves one of the six for the model now picked, making its line if it has none.
        ///
        /// PER MODEL, WHICH IS THE WHOLE REASON THIS IS A TABLE AND NOT SIX SETTINGS. A number
        /// that stands a backpack up lays a duffel on its side, the same way one number could
        /// never serve the twelve different drink models -- see Settings.Fit, which is the
        /// same table for the same reason about food in a hand.
        /// </summary>
        public static void Move(Core.Settings cfg, int which, float to)
        {
            if (cfg == null || which < 0 || which > 5) return;

            var name = Picked(cfg);

            float[] six;
            if (!cfg.BagFit.TryGetValue(name, out six) || six == null || six.Length < 6)
            {
                six = Where(cfg);
                cfg.BagFit[name] = six;
            }

            six[which] = to;

            Dirty = true;
        }

        /// <summary>Set by every write, cleared by the pass that re-seats the bag on it.</summary>
        public static bool Dirty;

        /// <summary>A change is owed to the ini, and when the last one was made. See Owed.</summary>
        private static bool _owed;
        private static int _owedAt;

        /// <summary>
        /// Notices a change and writes it down once it has settled.
        ///
        /// A SETTLE TIMER RATHER THAN A WRITE PER KEYPRESS. A nudge is a centimetre and a
        /// session is hundreds of them; rewriting the ini on each would be hundreds of file
        /// writes to record one decision. Two seconds after the LAST one, which is well
        /// inside the time it takes to reach for Insert -- and Off forces it early anyway,
        /// so taking the bag off, dying or shutting down all land it immediately.
        /// </summary>
        private void Owed()
        {
            int now;

            try { now = Game.GameTime; }
            catch { return; }

            if (Dirty)
            {
                Dirty = false;
                _owed = true;
                _owedAt = now;
            }

            if (_owed && now - _owedAt >= SettleMs) Save();
        }

        /// <summary>How long after the last nudge the table is written down.</summary>
        private const int SettleMs = 2000;

        /// <summary>
        /// Writes the table, the model and the bone to the ini.
        ///
        /// ALL THREE, because the tuner moves all three: 5 walks the models and End walks the
        /// bones, and a session that ends with a different bag picked and only the positions
        /// saved comes back wearing the old one.
        /// </summary>
        private void Save()
        {
            _owed = false;

            if (_cfg == null) return;

            try
            {
                IniFile.SetValue(Paths.Ini, "Bag", "Fit", _cfg.BagFitLine);
                IniFile.SetValue(Paths.Ini, "Bag", "Ground", _cfg.BagGroundLine);
                IniFile.SetValue(Paths.Ini, "Bag", "Model", _cfg.BagModel.ToString());
                IniFile.SetValue(Paths.Ini, "Bag", "Bone", _cfg.BagBone.ToString());
            }
            catch (Exception ex)
            {
                Log.Once("bag-save", "Could not write the bag position: " + ex.Message);
            }
        }

        // ======================================================================

        public void Update()
        {
            try
            {
                var me = Game.Player.Character;

                // ANY OWED WRITE FIRST, BEFORE A SINGLE GATE BELOW IT.
                //
                // THIS USED TO SIT AFTER Seat, WHICH IS INSIDE ALL OF THEM. A nudge followed
                // within the settle window by taking the bag off, dying, unticking the
                // switch or reloading left the write owed and never made -- which is the
                // same "it did not save" as the tuner that only printed to the log, one
                // layer further in and just as quiet.
                Owed();

                // AND WHICH BAG IT IS, BEFORE THE GATE, because the case that matters is the
                // bag NOT being on him -- lying on a pavement, drawn next door. See ModelOut.
                Publish();

                var want = _cfg != null && _cfg.BagShow && Knapsack.Worn &&
                           me != null && me.Exists() && !me.IsDead;

                if (!want) { Off(); return; }

                var name = Picked(_cfg);

                // A DIFFERENT BAG IS A DIFFERENT OBJECT. Nothing about an attached prop can
                // be turned into another model, so the old one goes and the new one is made.
                if (_thing != null && _thing.Exists() && name != _wearing) Off();

                if (_thing == null || !_thing.Exists()) Make(me, name);

                if (_thing == null || !_thing.Exists()) { Say(false); return; }

                // SEATED EVERY PASS, and that is not belt and braces. A prop attached once
                // comes off on a respawn, on a cutscene, on anything that clears his entities
                // -- and the tuner is only a tuner if turning a row moves the bag that is
                // already on him. Re-attaching something already attached MOVES it rather
                // than refusing, which is the same fact the food in his hand leans on.
                Seat(me);

                Say(true);
            }
            catch (Exception ex)
            {
                Log.Once("strap", "The bag on his back failed: " + ex.Message);
                Off();
            }
        }

        /// <summary>
        /// Makes the bag, walking the list until this build has one.
        ///
        /// ASKED FOR AND NOT WAITED ON, the same rule as everything else that streams in this
        /// mod: a spin here stops the frame that would have loaded it. A bag that turns up two
        /// frames late is a bag nobody noticed was late.
        /// </summary>
        private void Make(Ped me, string wanted)
        {
            var tried = 0;

            foreach (var name in Order(wanted))
            {
                tried++;

                try
                {
                    var model = new Model(name);

                    if (!model.IsValid || !model.IsInCdImage) continue;

                    if (!model.IsLoaded)
                    {
                        model.Request();
                        return;   // next pass, with it in memory
                    }

                    _thing = World.CreateProp(model, me.Position, false, false);

                    model.MarkAsNoLongerNeeded();

                    if (_thing == null || !_thing.Exists()) { _thing = null; continue; }

                    // OURS, AND NOT COLLIDING WITH ANYTHING. A bag welded to his back with its
                    // collision on is a bag shoving him through walls -- the same thing that
                    // launched him off a roof when the body carry left it on. See Bodies.Drag.
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _thing.Handle, true, true);
                    Function.Call(Hash.SET_ENTITY_COLLISION, _thing.Handle, false, false);
                    Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, _thing.Handle, me.Handle, true);

                    _wearing = name;

                    if (name != wanted)
                    {
                        Log.Info("Bag: " + wanted + " is not in this build, so it is " + name + ".");
                    }
                    else
                    {
                        Log.Once("bag-model-" + name, "Bag: wearing " + name + ".");
                    }

                    return;
                }
                catch
                {
                    // The next name on the list.
                }
            }

            if (_moaned || tried == 0) return;

            _moaned = true;

            Log.Warn("Bag: none of the " + tried + " bag model(s) would load on this build, so " +
                     "there is nothing on his back. The strap next door is still there.");
        }

        /// <summary>The picked model first, then the rest of the list as fallbacks.</summary>
        private static System.Collections.Generic.IEnumerable<string> Order(string wanted)
        {
            if (!string.IsNullOrEmpty(wanted)) yield return wanted;

            foreach (var pair in Bags)
            {
                if (pair[0] == wanted) continue;

                yield return pair[0];
            }
        }

        /// <summary>
        /// Puts it where the numbers say, in his own axes: across, forward, up, then turned.
        ///
        /// The last six arguments are the set this mod has proven twice over for a thing held
        /// on a person: no soft pinning, no collision, not a ped, vertex 2, fixed rotation.
        /// </summary>
        private void Seat(Ped me)
        {
            var six = Where(_cfg);

            var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, BoneId(_cfg));

            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _thing.Handle, me.Handle, bone,
                          six[0], six[1], six[2],
                          six[3], six[4], six[5],
                          false, false, false, false, 2, true);
        }

        public static int BoneId(Core.Settings cfg)
        {
            var at = cfg == null ? 0 : cfg.BagBone;
            if (at < 0 || at >= Bones.Length) at = 0;

            return Bones[at];
        }

        /// <summary>
        /// Takes it off and deletes it. Safe when there is nothing on him.
        ///
        /// AND LANDS ANYTHING OWED, rather than leaving it to a timer that is about to stop
        /// being called. Main calls this on the way out, so a reload with a nudge two seconds
        /// old writes it down instead of losing it -- and the same goes for dying, for the
        /// bag coming off, and for the switch being unticked. Save does nothing when nothing
        /// is owed, which is every frame he is not carrying one.
        /// </summary>
        public void Off()
        {
            if (_owed) Save();

            Say(false);

            if (_thing == null) { _wearing = ""; return; }

            try
            {
                if (_thing.Exists())
                {
                    Function.Call(Hash.DETACH_ENTITY, _thing.Handle, true, true);
                    _thing.Delete();
                }
            }
            catch (Exception ex)
            {
                Log.Once("strap-off", "Could not take the bag off his back: " + ex.Message);
            }

            _thing = null;
            _wearing = "";
        }

        private string _saidModel;
        private float[] _saidGround;

        /// <summary>
        /// Writes the chosen model and its ground six for the mod that lays the bag down. See
        /// ModelOut. Only when something changed: a string and six floats, but every frame.
        /// </summary>
        private void Publish()
        {
            try
            {
                var on = _cfg != null && _cfg.BagShow;
                var name = on ? Picked(_cfg) : "";
                var six = on ? GroundWhere(_cfg) : null;

                if (name != _saidModel)
                {
                    AppDomain.CurrentDomain.SetData(ModelOut, name);
                    _saidModel = name;
                }

                if (!Same(six, _saidGround))
                {
                    AppDomain.CurrentDomain.SetData(GroundOut, six == null ? null : (float[])six.Clone());
                    _saidGround = six == null ? null : (float[])six.Clone();
                }
            }
            catch
            {
                // Then the pavement gets the other mod's own bag, which is what it had.
            }
        }

        private static bool Same(float[] a, float[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;

            for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;

            return true;
        }

        /// <summary>Whether the mod next door says a bag is lying on the ground right now.</summary>
        private static bool FloorMade()
        {
            try
            {
                var row = AppDomain.CurrentDomain.GetData(FloorIn) as int[];
                return row != null && row.Length > 0 && row[0] == 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tells the mod that owns the bag whether a real one is being drawn. See Shown.
        ///
        /// Written every pass rather than on a change, because it is one int and a reader that
        /// starts up halfway through a session should find an answer immediately rather than
        /// waiting for somebody to move something.
        /// </summary>
        private static void Say(bool drawn)
        {
            try
            {
                var row = AppDomain.CurrentDomain.GetData(Shown) as int[];

                if (row == null || row.Length < 1)
                {
                    row = new int[1];
                    AppDomain.CurrentDomain.SetData(Shown, row);
                }

                row[0] = drawn ? 1 : 0;
            }
            catch
            {
                // Then the strap stays on his chest as well, which is ugly and not broken.
            }
        }

        // ======================================================================
        // The tuner
        // ======================================================================

        /// <summary>
        /// The numpad, while the tuner is on and a bag is actually on his back.
        ///
        /// THE SAME KEYS THE BODY CARRY USES, on purpose: the two tools do the same job on two
        /// different objects and there is no reason to learn a second keyboard. They cannot
        /// both be on at once in any way that matters -- one wants a corpse in his arms and
        /// this one wants a bag on his back -- and if they ever are, the body wins the pad
        /// because Main ticks it last.
        ///
        /// IT READS NOTHING UNLESS BOTH ARE TRUE, so a player who never turns it on has a mod
        /// with no extra bindings in it at all.
        /// </summary>
        public void Tune()
        {
            if (_cfg == null || !_cfg.BagTuner) return;

            // ON HIS BACK, OR ON THE GROUND. The same keys dial whichever the bag is doing:
            // hanging off his spine, or lying on the pavement next door's mod put it on. See
            // GroundWhere. On the ground only once that mod says it has made the thing, so the
            // keys never move numbers for a bag nobody can see.
            var onBack = _thing != null && _thing.Exists();
            var onFloor = !onBack && _cfg.BagShow && !Knapsack.Worn && FloorMade();

            if (!onBack && !onFloor) return;

            Readout(onFloor);

            var six = onFloor ? GroundWhere(_cfg) : Where(_cfg);

            Action<int, float> move = (i, v) =>
            {
                if (onFloor) MoveGround(_cfg, i, v);
                else Move(_cfg, i, v);
            };

            if (Tap(Keys.NumPad4)) move(0, six[0] - Step);
            if (Tap(Keys.NumPad6)) move(0, six[0] + Step);
            if (Tap(Keys.NumPad8)) move(1, six[1] + Step);
            if (Tap(Keys.NumPad2)) move(1, six[1] - Step);
            if (Tap(Keys.NumPad7)) move(2, six[2] - Step);
            if (Tap(Keys.NumPad9)) move(2, six[2] + Step);

            if (Tap(Keys.Divide)) move(3, six[3] - Turn);
            if (Tap(Keys.Multiply)) move(3, six[3] + Turn);
            if (Tap(Keys.Subtract)) move(4, six[4] - Turn);
            if (Tap(Keys.Add)) move(4, six[4] + Turn);
            if (Tap(Keys.NumPad1)) move(5, six[5] - Turn);
            if (Tap(Keys.NumPad3)) move(5, six[5] + Turn);

            // NEXT BAG, so a whole wardrobe can be dialled without opening the menu between
            // each one. The model change is noticed by Update on the next pass.
            if (Tap(Keys.NumPad5))
            {
                _cfg.BagModel = (_cfg.BagModel + 1) % Bags.Length;
                Dirty = true;
            }

            if (!onFloor && Tap(Keys.End))
            {
                _cfg.BagBone = (_cfg.BagBone + 1) % Bones.Length;
                Dirty = true;
            }

            if (Tap(Keys.Decimal))
            {
                // On the ground, back to exactly how it always lay; on his back, the start.
                for (var i = 0; i < 6; i++) move(i, onFloor ? 0f : Start[i]);
            }

            // NOT A MOVE. Writing it down changes nothing about where it is, and re-seating
            // the bag to celebrate would jolt the thing just lined up.
            if (Tap(Keys.NumPad0)) Write();
        }

        private const float Step = 0.01f;
        private const float Turn = 5f;

        private static readonly System.Collections.Generic.HashSet<Keys> _down =
            new System.Collections.Generic.HashSet<Keys>();

        /// <summary>
        /// A key going down, once per press. SHVDN's own key state is a LEVEL, not an edge, so
        /// one tap of Numpad 6 walks the bag half a metre before the finger is off it.
        /// </summary>
        private static bool Tap(Keys key)
        {
            var down = false;

            try { down = Game.IsKeyPressed(key); }
            catch { return false; }

            var was = _down.Contains(key);

            if (down && !was) { _down.Add(key); return true; }
            if (!down && was) _down.Remove(key);

            return false;
        }

        private void Readout(bool floor)
        {
            var six = floor ? GroundWhere(_cfg) : Where(_cfg);

            Line(0.012f, 0.060f, "~y~" + (floor ? "BAG ON THE GROUND" : "BAG ON HIS BACK") + "~s~   " +
                                 Bags[_cfg.BagModel % Bags.Length][1], 0.34f);
            Line(0.012f, 0.085f, Picked(_cfg), 0.26f);
            Line(0.012f, 0.110f, "4/6 across    " + six[0].ToString("0.00"), 0.30f);
            Line(0.012f, 0.130f, (floor ? "8/2 along     " : "8/2 out       ") + six[1].ToString("0.00"), 0.30f);
            Line(0.012f, 0.150f, "7/9 up        " + six[2].ToString("0.00"), 0.30f);
            Line(0.012f, 0.175f, "//* tip       " + six[3].ToString("0") + " deg", 0.30f);
            Line(0.012f, 0.195f, "-/+ roll      " + six[4].ToString("0") + " deg", 0.30f);
            Line(0.012f, 0.215f, "1/3 turn      " + six[5].ToString("0") + " deg", 0.30f);
            Line(0.012f, 0.240f, floor
                                     ? "~b~5~s~ next bag -- it changes on the ground too"
                                     : "~b~5~s~ next bag   ~b~END~s~ " + BoneNames[_cfg.BagBone % Bones.Length], 0.28f);
            Line(0.012f, 0.262f, "~g~0~s~ save it now    ~r~.~s~ " +
                                 (floor ? "back to how it always lay" : "back to the start"), 0.28f);
        }

        private static void Line(float x, float y, string text, float scale)
        {
            try
            {
                Function.Call(Hash.SET_TEXT_FONT, 4);
                Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
                Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, 225);
                Function.Call(Hash.SET_TEXT_DROP_SHADOW);
                Function.Call(Hash.SET_TEXT_OUTLINE);
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
            }
            catch
            {
                // A tool with no readout is still a tool with keys.
            }
        }

        /// <summary>
        /// The answer, as the line the ini wants, so a position found at two in the morning is
        /// a paste rather than six numbers to remember.
        /// </summary>
        private void Write()
        {
            // SAVED, NOT JUST SAID. This key printed the line and wrote nothing, which read
            // as "written down" and was not -- see the note on Save. It still prints, because
            // a line in the log is how these were recovered the one time it mattered.
            Save();

            Log.Info("BAG POSITION -- saved to [Bag] Fit and Ground in BareMinimum.ini:");
            Log.Info("    Fit = " + _cfg.BagFitLine);
            Log.Info("    Ground = " + _cfg.BagGroundLine);
            Log.Info("    Bone = " + _cfg.BagBone + "     ; " +
                     BoneNames[_cfg.BagBone % Bones.Length]);

            try
            {
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "SELECT",
                              "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
            }
            catch
            {
                // It is in the log either way, which is the part that matters.
            }
        }
    }
}
