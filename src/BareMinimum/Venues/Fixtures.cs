using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>
    /// Things this mod puts in the world because the game did not, out of data/fixtures.txt.
    ///
    /// IT EXISTS FOR ONE FRIDGE AND IT IS WRITTEN TO HOLD MORE. Franklin's aunt's kitchen on
    /// Forum Drive has a fridge in it -- v_res_tt_fridge, bolted into the interior -- and the
    /// mod finds it by hash the way it finds every other one. Michael put a SECOND fridge
    /// inside it, a spawnable prop_fridge_03 in the same space, because the map's own is a
    /// fixture and a fixture is not a thing you can rely on the way you can rely on an object
    /// you placed. He did it in Menyoo, which means loading a spooner file every session for
    /// one prop. This is that placement, shipped.
    ///
    /// A LINE IN A TEXT FILE RATHER THAN A COORDINATE IN THIS SOURCE. The same argument as
    /// foods.json and doors.txt: a position is a thing somebody stands in front of and reads
    /// off a screen, and the person who needs to change it is not necessarily the person who
    /// can rebuild the dll. It is also how the next one gets added without touching code.
    ///
    /// MADE WHEN YOU ARE NEAR AND TAKEN AWAY WHEN YOU ARE NOT. A handful of props left
    /// standing all over the map for a whole session is a handful of entities the game did
    /// not ask for; within a hundred metres it is one object and nobody can tell.
    ///
    /// AND IT WILL NOT DOUBLE ONE UP. If something of the same model is already standing
    /// within a metre and a half of the spot -- Menyoo's copy of the very same placement,
    /// most likely, or this mod's own from before a reload -- nothing is made. Two fridges
    /// inside each other is exactly the "duplicate houses and doubled rooms" that a mod
    /// rebuilding a superseded spooner draft causes, and it is not being repeated here.
    /// </summary>
    internal sealed class Fixtures
    {
        /// <summary>One thing, somewhere.</summary>
        private sealed class Thing
        {
            public string Model = "";
            public int Hash;
            public Vector3 At;
            public float Heading;
            public string Note = "";

            /// <summary>Ours, while it exists. Never the one somebody else put there.</summary>
            public Prop Made;
        }

        /// <summary>How near he has to be before the thing is a real object.</summary>
        private const float MakeWithin = 100f;

        /// <summary>And how far past that it is let go of, so an edge does not flicker.</summary>
        private const float DropBeyond = 130f;

        /// <summary>Close enough to count as already being there. See the note on the class.</summary>
        private const float Same = 1.5f;

        private const int EveryMs = 2000;

        private readonly Core.Settings _cfg;
        private readonly List<Thing> _things = new List<Thing>();

        private bool _read;
        private int _at;

        public Fixtures(Core.Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>How many are on the list, for the log and the settings page.</summary>
        public int Count => _things.Count;

        public void Update()
        {
            if (_cfg == null || !_cfg.Fixtures) { Clear(); return; }

            int now;

            try { now = Game.GameTime; }
            catch { return; }

            if (now - _at < EveryMs) return;
            _at = now;

            Read();

            if (_things.Count == 0) return;

            Ped me;

            try
            {
                me = Game.Player.Character;
                if (me == null || !me.Exists()) return;
            }
            catch
            {
                return;
            }

            var here = me.Position;

            foreach (var thing in _things)
            {
                var gap = here.DistanceTo(thing.At);

                if (gap > DropBeyond) { Unmake(thing); continue; }
                if (gap > MakeWithin) continue;

                Make(thing);
            }
        }

        // ======================================================================

        /// <summary>
        /// Reads the file, once.
        ///
        /// A MISSING FILE IS NOT A FAILURE. It means nobody has anything to add to the world,
        /// which is the ordinary case for anybody who has not gone looking for this.
        /// </summary>
        private void Read()
        {
            if (_read) return;
            _read = true;

            try
            {
                var path = Path.Combine(Paths.Data, "fixtures.txt");

                if (!File.Exists(path))
                {
                    Log.Debug("Fixtures: no fixtures.txt, so nothing is added to the world.");
                    return;
                }

                var bad = 0;

                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();

                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

                    // ANYTHING AFTER A SEMICOLON IS A NOTE, so a line can say what it is and
                    // where it came from. The whole point of the file being text.
                    var note = "";
                    var semi = line.IndexOf(';');

                    if (semi >= 0)
                    {
                        note = line.Substring(semi + 1).Trim();
                        line = line.Substring(0, semi).Trim();
                    }

                    var bits = line.Split(',');

                    if (bits.Length < 5) { bad++; continue; }

                    var name = bits[0].Trim();

                    float x, y, z, h;

                    if (name.Length == 0 ||
                        !Num(bits[1], out x) || !Num(bits[2], out y) ||
                        !Num(bits[3], out z) || !Num(bits[4], out h))
                    {
                        bad++;
                        continue;
                    }

                    int hash;

                    try { hash = new Model(name).Hash; }
                    catch { bad++; continue; }

                    _things.Add(new Thing
                    {
                        Model = name,
                        Hash = hash,
                        At = new Vector3(x, y, z),
                        Heading = h,
                        Note = note
                    });
                }

                Log.Info("Fixtures: " + _things.Count + " thing(s) to put in the world" +
                         (bad > 0 ? ", and " + bad + " line(s) that would not read" : "") + ".");
            }
            catch (Exception ex)
            {
                Log.Once("fixtures-read", "Could not read fixtures.txt: " + ex.Message);
            }
        }

        private static bool Num(string text, out float value)
        {
            return float.TryParse((text ?? "").Trim(), NumberStyles.Float,
                                  CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Puts it there, unless it is there already.
        ///
        /// THE MODEL IS ASKED FOR AND NOT WAITED ON, the same rule as everything else that
        /// streams in this mod: a spin inside a script tick stops the frame that would have
        /// loaded it, and the timer that frame drives never moves. The next pass makes it.
        /// </summary>
        private void Make(Thing thing)
        {
            if (thing.Made != null && thing.Made.Exists()) return;

            thing.Made = null;

            try
            {
                // SOMEBODY ELSE'S FIRST. Menyoo's copy of the same placement, or ours from
                // before a script reload -- either way there is already one there and a
                // second would be a fridge inside a fridge inside a fridge.
                var there = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,
                                               thing.At.X, thing.At.Y, thing.At.Z, Same,
                                               thing.Hash, false, false, false);

                if (there != 0) return;

                var model = new Model(thing.Hash);

                if (!model.IsValid || !model.IsInCdImage)
                {
                    Log.Once("fixture-model-" + thing.Model,
                             "Fixtures: this build has no " + thing.Model + ", so nothing is " +
                             "put at " + Said(thing) + ".");
                    return;
                }

                if (!model.IsLoaded) { model.Request(); return; }

                var made = World.CreateProp(model, thing.At, false, false);

                model.MarkAsNoLongerNeeded();

                if (made == null || !made.Exists()) return;

                made.Rotation = new Vector3(0f, 0f, thing.Heading);

                // FROZEN AND OURS. It is a fixture: it does not fall, it does not get nudged
                // across the kitchen by somebody walking into it, and the population manager
                // does not get to clean it up while the player is stood in front of it.
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, made.Handle, true, true);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, made.Handle, true);

                made.IsPersistent = true;

                thing.Made = made;

                Log.Info("Fixtures: put a " + thing.Model + " at " + Said(thing) +
                         (thing.Note.Length > 0 ? " -- " + thing.Note : "") + ".");
            }
            catch (Exception ex)
            {
                Log.Once("fixture-make-" + thing.Model,
                         "Could not put a " + thing.Model + " in the world: " + ex.Message);
            }
        }

        private static string Said(Thing thing)
        {
            return thing.At.X.ToString("0") + ", " + thing.At.Y.ToString("0");
        }

        /// <summary>Takes ours away. Never touches one somebody else put there.</summary>
        private static void Unmake(Thing thing)
        {
            if (thing.Made == null) return;

            try
            {
                if (thing.Made.Exists()) thing.Made.Delete();
            }
            catch
            {
                // The game tidies it eventually.
            }

            thing.Made = null;
        }

        /// <summary>
        /// Everything of ours, gone. For a shutdown and for the switch going off.
        ///
        /// NOT OPTIONAL. A prop marked as a mission entity is a prop the game will NOT clean
        /// up on its own, which is the whole reason it is marked -- so a reload that left them
        /// behind would stack another set on top on the way back in, and the duplicate guard
        /// only looks at the model, not at who made it.
        /// </summary>
        public void Clear()
        {
            foreach (var thing in _things) Unmake(thing);
        }
    }
}
