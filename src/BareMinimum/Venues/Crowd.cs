using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

using BareMinimum.Core;

namespace BareMinimum.Venues
{
    /// <summary>
    /// The people in a room, spawned when he walks in and gone when he walks out.
    ///
    /// A ROOM THE GAME NEVER LETS YOU INTO IS A ROOM THE GAME NEVER PUTS ANYBODY IN. Tequi-la-la
    /// and Bahama Mamas are furnished, lit and completely empty, because nothing in the story
    /// ever sends a crowd to them; walking into a nightclub and finding a bar with nobody behind
    /// it is worse than not being able to get in at all. So the room is populated on the way in.
    ///
    /// THE ONLINE CLUB'S OWN DANCERS, not a scenario. GTA Online's nightclubs animate their
    /// crowd out of anim@amb@nightclub@dancers@ -- a face-the-DJ set, a groups set and an
    /// ambient set, each at low, medium and high intensity, with a male and a female take of
    /// every clip. All of it is in this copy of the game whether or not it is ever played, and
    /// every dictionary and clip named in vendors.json was read out of the game's own animation
    /// list rather than remembered. A scenario would have been one man drinking on a loop.
    ///
    /// SPAWNED AS A RING, NOT A GRID. A room's floor plan is not knowable from a coordinate, so
    /// they are put on a circle around where he landed at a radius the room gives, each one
    /// dropped to the ground under it and skipped if there is no ground -- which is what keeps
    /// a dancer out of the wall of a room this mod has never seen.
    ///
    /// EVERY ONE OF THEM IS DELETED. They are this mod's peds, made on the way in and removed on
    /// the way out, on a reload, and on the way out of the script. A crowd left behind in a room
    /// nobody can reach is a permanent population the player pays for in peds forever.
    /// </summary>
    internal sealed class Crowd
    {
        /// <summary>What a room asks for. Read from vendors.json; empty means an empty room.</summary>
        internal sealed class Plan
        {
            public int Count;
            public float Radius = 5f;
            public string[] Peds = new string[0];
            public string Dict = "";
            public string[] Clips = new string[0];
            public string Scenario = "";
        }

        private readonly List<Ped> _made = new List<Ped>();
        private readonly Random _rng = new Random();

        /// <summary>Whether anybody is standing in the room on this mod's account.</summary>
        public bool Any => _made.Count > 0;

        /// <summary>
        /// Fills the room around where he landed.
        ///
        /// Quietly does nothing when the room asks for nobody, which is every shop that has not
        /// been given a crowd -- the common case, and not a failure.
        /// </summary>
        public void Fill(Plan plan, Vector3 centre)
        {
            Clear();

            if (plan == null || plan.Count <= 0 || plan.Peds.Length == 0) return;

            var wanted = Math.Min(plan.Count, 24);

            try
            {
                if (plan.Dict.Length > 0 && !Ask(plan.Dict))
                {
                    Log.Warn("Crowd: " + plan.Dict + " would not load; the room stays empty.");
                    return;
                }

                for (var i = 0; i < wanted; i++)
                {
                    // Round the circle, with the radius jittered so it is a crowd rather than
                    // a fairy ring.
                    var angle = (float)(i * 2.0 * Math.PI / wanted + _rng.NextDouble() * 0.4);
                    var reach = plan.Radius * (0.45f + (float)_rng.NextDouble() * 0.55f);

                    var at = new Vector3(centre.X + (float)Math.Cos(angle) * reach,
                                         centre.Y + (float)Math.Sin(angle) * reach,
                                         centre.Z);

                    float z;
                    if (Ground(at, out z)) at.Z = z;

                    var ped = Make(plan.Peds[_rng.Next(plan.Peds.Length)], at,
                                   (float)(_rng.NextDouble() * 360.0));
                    if (ped == null) continue;

                    _made.Add(ped);
                    Move(ped, plan);
                }

                Log.Info("Crowd: " + _made.Count + " in the room.");
            }
            catch (Exception ex)
            {
                Log.Once("crowd", "Could not fill the room: " + ex.Message);
            }
        }

        /// <summary>Everybody this made, gone. Safe to call with nobody there.</summary>
        public void Clear()
        {
            foreach (var ped in _made)
            {
                try
                {
                    if (ped != null && ped.Exists()) ped.Delete();
                }
                catch { /* a ped the engine already took is already gone */ }
            }

            _made.Clear();
        }

        // ======================================================================

        private Ped Make(string model, Vector3 at, float heading)
        {
            try
            {
                var m = new Model(model);
                if (!m.IsValid) return null;

                if (!m.IsLoaded)
                {
                    m.Request(1500);

                    // RELEASED EVEN WHEN IT DID NOT ARRIVE. Asking for a model locks it in
                    // memory until somebody says otherwise, and this used to return here with
                    // the request still standing -- so every dancer that missed its window was
                    // a ped model pinned for the session, and going in while the streamer was
                    // still catching up left several.
                    if (!m.IsLoaded) { m.MarkAsNoLongerNeeded(); return null; }
                }

                var ped = World.CreatePed(m, at, heading);
                m.MarkAsNoLongerNeeded();

                if (ped == null || !ped.Exists()) return null;

                // NOT PART OF THE WORLD'S POPULATION. They are scenery: they do not wander off,
                // do not flee, do not join a fight, and cannot be culled out from under the
                // animation while he is standing in the room looking at them.
                ped.BlockPermanentEvents = true;
                ped.IsPersistent = true;

                Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, false);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, false);

                return ped;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The dance, or the scenario when the room asked for one instead.</summary>
        private void Move(Ped ped, Plan plan)
        {
            try
            {
                if (plan.Dict.Length > 0 && plan.Clips.Length > 0)
                {
                    var clip = plan.Clips[_rng.Next(plan.Clips.Length)];

                    // Flag 1 is a loop; 8 lets the upper body play over whatever the legs are
                    // doing, which is what keeps a dancer from sliding when the floor moves
                    // under them. Blended in over a quarter second so the room does not snap.
                    Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, plan.Dict, clip,
                                  4f, -4f, -1, 1 | 8, 0f, false, false, false);

                    // Started a little way in, so twenty dancers are not one dancer twenty times.
                    Function.Call(Hash.SET_ENTITY_ANIM_CURRENT_TIME, ped.Handle, plan.Dict, clip,
                                  (float)_rng.NextDouble());
                    return;
                }

                if (plan.Scenario.Length > 0)
                {
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, plan.Scenario, 0, true);
                }
            }
            catch (Exception ex)
            {
                Log.Once("crowd-anim", "Could not start the crowd moving: " + ex.Message);
            }
        }

        private static bool Ask(string dict)
        {
            try
            {
                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)) return true;

                Function.Call(Hash.REQUEST_ANIM_DICT, dict);

                for (var i = 0; i < 40; i++)
                {
                    if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)) return true;
                    Script.Wait(50);
                }
            }
            catch { /* answered below */ }

            return false;
        }

        /// <summary>The floor under a point. Inside.Ground says why this is asked rather than assumed.</summary>
        private static bool Ground(Vector3 at, out float z)
        {
            z = at.Z;

            try
            {
                using (var found = new OutputArgument())
                {
                    if (!Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD,
                                             at.X, at.Y, at.Z + 1.5f, found, false)) return false;

                    var got = found.GetResult<float>();

                    // A floor more than a storey from where he is standing is another storey.
                    if (Math.Abs(got - at.Z) > 3f) return false;

                    z = got;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
