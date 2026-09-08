using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>
    /// The room's own doors: locked while he is in it, put back as they were when he leaves.
    /// </summary>
    ///
    /// <remarks>
    /// THE ROOM IS SOMEWHERE ELSE. A door on Ginger Street warps him into the real 24/7 at
    /// Vinewood Plaza, and that shop has a front door of its own, which opens onto Vinewood.
    /// Walk out of it and you are two suburbs from the shop you went into -- the one thing
    /// about a warp that must never show. So the doors of the room he is stood in are locked
    /// the moment he is in it, and the hold is the only way out.
    ///
    /// WHICH OBJECTS ARE DOORS comes from data/doors.txt: every door model name in the game,
    /// hashed at load. The room is never asked to name its own doors -- it is a real building
    /// this mod has not seen the inside of -- so every door-model object within reach of where
    /// he landed is a door of his room. A shelf or a till is not on the list and is left alone;
    /// a neighbour's door across the street is on it and is locked for the visit and put back
    /// after, which nobody out there will notice.
    ///
    /// FOUND TWO WAYS. GET_CLOSEST_OBJECT_OF_TYPE sees placed map objects, which is the lesson
    /// Beds and Fridges learnt, but answers with one object per model; the pool walk sees every
    /// door that has physics, however many share a model, but not one that has not woken yet.
    /// Both are asked, and kept being asked for a while after the warp, because a door that had
    /// not streamed when he arrived is exactly the one he would walk out of.
    ///
    /// PUT BACK, NOT UNLOCKED. Tequi-la-la's street doors are locked by the game itself; that
    /// is why the bar has no way in. Unlocking them on the way out would open a story interior
    /// onto Vinewood Boulevard, so each door's state in the door system before is what it is
    /// told again after -- and a door this mod registered is taken out of the system rather
    /// than left in it. A door found already registered under one of OUR names is ours from
    /// before a reload and is treated as ours, or a reload with him inside would leave the
    /// shop locked for the rest of the session.
    /// </remarks>
    internal sealed class Bolt
    {
        /// <summary>How far from where he landed a door still belongs to his room.</summary>
        private const float Reach = 35f;

        /// <summary>How long after the warp the room keeps being swept for doors, and how often the pool is.</summary>
        private const int SweepForMs = 6000;
        private const int PoolEveryMs = 1000;

        /// <summary>
        /// How many models GET_CLOSEST_OBJECT_OF_TYPE is asked about per tick. Nine hundred in
        /// one frame is a hitch; sixty-four a frame is the whole list every quarter second.
        /// </summary>
        private const int PerTick = 64;

        /// <summary>The door system's states, the two this uses.</summary>
        private const int Unlocked = 0;
        private const int Locked = 1;

        /// <summary>One door held shut, and what to tell it afterwards.</summary>
        private sealed class Held
        {
            public int Door;
            public int Model;
            public Vector3 At;
            public bool Ours;
            public int WasState;
        }

        private readonly HashSet<int> _models = new HashSet<int>();
        private readonly List<int> _modelList = new List<int>();
        private readonly List<Held> _held = new List<Held>();

        private Vector3 _around;
        private int _sweepUntil;
        private int _nextPool;
        private int _cursor;

        public Bolt()
        {
            Load();
        }

        /// <summary>How many doors are held shut right now.</summary>
        public int Count => _held.Count;

        // ======================================================================

        /// <summary>
        /// Locks every door of the room round this point, now, and keeps looking for more for
        /// a while. Called with the screen still black, so the first full pass is free.
        /// </summary>
        public void Lock(Vector3 around)
        {
            _around = around;
            _sweepUntil = Game.GameTime + SweepForMs;
            _nextPool = Game.GameTime + PoolEveryMs;
            _cursor = 0;

            if (_models.Count == 0)
            {
                Log.Once("bolt-empty", "No door models known -- the room's own doors stay as the game has them.");
                return;
            }

            try
            {
                Pool();
                foreach (var model in _modelList) Nearest(model);
            }
            catch (Exception ex)
            {
                Log.Once("bolt", "Could not lock the room's doors: " + ex.Message);
            }

            Log.Info(_held.Count + " door(s) of the room locked on arrival.");
        }

        /// <summary>The sweeps after the warp. Nothing once they are done.</summary>
        public void Update()
        {
            if (_sweepUntil == 0 || _models.Count == 0) return;

            var now = Game.GameTime;

            try
            {
                if (now >= _nextPool)
                {
                    _nextPool = now + PoolEveryMs;
                    Pool();
                }

                Slice();
            }
            catch (Exception ex)
            {
                Log.Once("bolt", "Could not lock the room's doors: " + ex.Message);
            }

            if (now >= _sweepUntil) _sweepUntil = 0;
        }

        /// <summary>Every door put back the way it was, and the sweeps stopped.</summary>
        public void Release()
        {
            _sweepUntil = 0;

            if (_held.Count == 0) return;

            var put = 0;

            foreach (var h in _held)
            {
                try
                {
                    if (h.Ours)
                    {
                        Function.Call(Hash.DOOR_SYSTEM_SET_DOOR_STATE, h.Door, Unlocked, false, true);
                        Function.Call(Hash.REMOVE_DOOR_FROM_SYSTEM, h.Door, 0);
                    }
                    else
                    {
                        Function.Call(Hash.DOOR_SYSTEM_SET_DOOR_STATE, h.Door, h.WasState, false, true);
                    }

                    put++;
                }
                catch
                {
                    // The next one still gets put back.
                }
            }

            Log.Info(put + " of " + _held.Count + " door(s) put back as they were.");
            _held.Clear();
        }

        // ======================================================================

        /// <summary>Every door in the object pool round him, whatever its model.</summary>
        private void Pool()
        {
            foreach (var prop in World.GetNearbyProps(_around, Reach))
            {
                if (prop == null || !prop.Exists()) continue;

                var model = prop.Model.Hash;
                if (!_models.Contains(model)) continue;

                Hold(model, prop.Position);
            }
        }

        /// <summary>The next few models on the list, asked of the map.</summary>
        private void Slice()
        {
            var count = Math.Min(PerTick, _modelList.Count);

            for (var i = 0; i < count; i++)
            {
                if (_cursor >= _modelList.Count) _cursor = 0;
                Nearest(_modelList[_cursor++]);
            }
        }

        /// <summary>The nearest placed object of one model, which the pool may not have.</summary>
        private void Nearest(int model)
        {
            var handle = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,
                                            _around.X, _around.Y, _around.Z, Reach, model,
                                            false, false, false);
            if (handle == 0) return;

            // SHVDN 3.9 has no public pool-object constructors; Entity.FromHandle and a cast.
            var prop = Entity.FromHandle(handle) as Prop;
            if (prop == null || !prop.Exists()) return;

            Hold(model, prop.Position);
        }

        /// <summary>One door: remembered as it was, then locked. A door already held is left held.</summary>
        private void Hold(int model, Vector3 at)
        {
            foreach (var h in _held)
            {
                if (h.Model == model && h.At.DistanceTo(at) < 0.5f) return;
            }

            // A name for this door that is the same on every visit and after every reload:
            // the model and where it stands, to the decimetre.
            var ours = Game.GenerateHash("bareminimum_bolt_" + model + "_" +
                                         (int)Math.Round(at.X * 10.0) + "_" +
                                         (int)Math.Round(at.Y * 10.0) + "_" +
                                         (int)Math.Round(at.Z * 10.0));

            var held = new Held { Model = model, At = at };
            string how;

            if (Function.Call<bool>(Hash.IS_DOOR_REGISTERED_WITH_SYSTEM, ours))
            {
                // Ours from before a reload: nothing to remember, and ours to remove.
                held.Door = ours;
                held.Ours = true;
                how = "ours from before";
            }
            else
            {
                var arg = new OutputArgument();
                Function.Call(Hash.DOOR_SYSTEM_FIND_EXISTING_DOOR, at.X, at.Y, at.Z, model, arg);
                var existing = arg.GetResult<int>();

                if (existing != 0)
                {
                    held.Door = existing;
                    held.Ours = false;
                    held.WasState = Function.Call<int>(Hash.DOOR_SYSTEM_GET_DOOR_STATE, existing);
                    how = "the game's, was state " + held.WasState;
                }
                else
                {
                    Function.Call(Hash.ADD_DOOR_TO_SYSTEM, ours, model, at.X, at.Y, at.Z, false, false, false);
                    held.Door = ours;
                    held.Ours = true;
                    how = "registered";
                }
            }

            // Shut, then locked; a locked door that was standing open is a way out.
            Function.Call(Hash.DOOR_SYSTEM_SET_OPEN_RATIO, held.Door, 0f, false, true);
            Function.Call(Hash.DOOR_SYSTEM_SET_DOOR_STATE, held.Door, Locked, false, true);

            _held.Add(held);

            Log.Info("Locked a door of the room: model " + model + " at " + at + " (" + how + ").");
        }

        // ======================================================================

        private void Load()
        {
            try
            {
                var file = Paths.DoorsFile;

                if (!File.Exists(file))
                {
                    Log.Warn("No doors.txt beside vendors.json -- the rooms' own doors stay as the game has them.");
                    return;
                }

                foreach (var raw in File.ReadAllLines(file))
                {
                    var name = raw.Trim().ToLowerInvariant();
                    if (name.Length == 0 || name[0] == '#' || name[0] == ';') continue;

                    var hash = Game.GenerateHash(name);
                    if (_models.Add(hash)) _modelList.Add(hash);
                }

                Log.Info(_models.Count + " door models known from doors.txt.");
            }
            catch (Exception ex)
            {
                Log.Error("Could not read doors.txt", ex);
            }
        }
    }
}
