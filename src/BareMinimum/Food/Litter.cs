using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// What is left on the pavement when he has finished.
    ///
    /// An empty can that vanishes out of a man's hand the instant he stops drinking is the
    /// one moment this mod stops modelling anything. He drops it, and it stays where it fell.
    ///
    /// IT IS CAPPED AND IT IS A QUEUE. Props are a limited resource in this game and a mod
    /// that adds one to the world every time you have a drink, with nothing ever taking one
    /// away, is a mod that eventually costs somebody their traffic. So the oldest goes when
    /// the newest arrives and there are never more than the cap. Twelve is a long walk's worth.
    ///
    /// THE CAP DELETES; SHUTTING DOWN DOES NOT. Deleting the lot on a script reload would
    /// sweep a street somebody had spent an afternoon littering, so on the way out they are
    /// handed to the game instead -- marked no longer needed, which lets the streamer clear
    /// them up on its own terms the way it does every other bit of world rubbish. The one
    /// thing never done is leaving an owned prop with nobody owning it.
    ///
    /// WHAT IT DROPS IS THE ITEM'S BUSINESS. A drink leaves a can, a beer leaves a bottle,
    /// food leaves the bag it came in -- the three are named in foods.json beside everything
    /// else about the food, and any item can name its own or name nothing at all. Nothing here
    /// knows what a taco is.
    /// </summary>
    internal sealed class Litter
    {
        private readonly Settings _cfg;

        /// <summary>What has been dropped, oldest first. See the class note.</summary>
        private readonly Queue<Prop> _dropped = new Queue<Prop>();

        private bool _saidModel;

        public Litter(Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>
        /// Drops the finished thing where he is standing.
        ///
        /// <paramref name="what"/> is the prop's model name, already decided by the catalogue
        /// -- empty means this one leaves nothing behind, which is a real answer and not a
        /// missing one: nobody throws a pipe away when they have finished with it.
        /// </summary>
        public void Drop(Ped me, string what)
        {
            if (!_cfg.Litter || string.IsNullOrEmpty(what)) return;
            if (me == null || !me.Exists()) return;

            try
            {
                var model = new Model(what);

                if (!model.IsLoaded)
                {
                    // ASKED FOR, NEVER WAITED ON, and never dropped late either. The prop he
                    // was holding has already gone by now, so a litter prop that turned up two
                    // seconds after the meal would appear out of the air beside him. Requested
                    // so the NEXT one is ready, and this one is simply not dropped.
                    model.Request();

                    if (!_saidModel)
                    {
                        _saidModel = true;
                        Log.Info("Litter: " + what + " was not loaded yet, so nothing was dropped " +
                                 "this time. It is being asked for now.");
                    }

                    return;
                }

                // A LITTLE IN FRONT AND A LITTLE ABOVE, so it falls rather than appearing on
                // the floor, and falls away from his feet rather than through them.
                var at = me.Position + me.ForwardVector * 0.45f + Vector3.WorldUp * 0.55f;

                // Dynamic, because the whole point is that it lands and rolls. The fourth
                // argument places it on the ground, which is exactly what must NOT happen --
                // it would snap to the pavement and never fall at all.
                var bit = World.CreateProp(model, at, true, false);

                if (bit == null || !bit.Exists()) return;

                // A flick of the wrist, not a throw. Somebody dropping a can does not put any
                // effort into it, and a can that sails six feet reads as a pitch.
                try
                {
                    bit.Velocity = me.ForwardVector * 1.6f + Vector3.WorldUp * 0.6f;
                    Function.Call(Hash.SET_ENTITY_ANGULAR_VELOCITY, bit.Handle, 2.5f, 1.5f, 3.5f);
                }
                catch
                {
                    // Then it drops straight down, which is still a dropped can.
                }

                Keep(bit);
            }
            catch (Exception ex)
            {
                Log.Once("litter-drop", "Could not drop " + what + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Adds one to the queue and takes the oldest away once the cap is passed.
        ///
        /// The cap is read every time rather than held, because it is live on the menu and a
        /// player winding it down from twelve to three expects three, not twelve until the
        /// next reload.
        /// </summary>
        private void Keep(Prop bit)
        {
            _dropped.Enqueue(bit);

            var cap = Math.Max(0, _cfg.LitterMax);

            while (_dropped.Count > cap)
            {
                var old = _dropped.Dequeue();

                try
                {
                    if (old != null && old.Exists()) old.Delete();
                }
                catch
                {
                    // A prop the game has already taken is a prop we no longer have to.
                }
            }
        }

        /// <summary>
        /// Hands everything dropped back to the game, without deleting it.
        ///
        /// See the class note: a reload should not tidy the street. Marked no longer needed,
        /// the streamer clears them on its own terms, the same as every other bit of rubbish
        /// in the world -- and what must never happen, an owned prop with nobody owning it,
        /// does not.
        /// </summary>
        public void Shutdown()
        {
            foreach (var bit in _dropped)
            {
                try
                {
                    if (bit != null && bit.Exists()) bit.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // Going either way.
                }
            }

            _dropped.Clear();
        }

        /// <summary>How many bits are on the ground. For the log.</summary>
        public int Count => _dropped.Count;
    }
}
