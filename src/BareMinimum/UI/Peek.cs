using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

using BareMinimum.Core;

namespace BareMinimum.UI
{
    /// <summary>
    /// The thing under the cursor, in his hand, while the pocket is open.
    ///
    /// AN ICON SAYS WHICH ONE. A BURGER SAYS WHAT IT IS. The grid has a picture on every tile
    /// and the pictures are good, but they are all the same size and drawn in the same line
    /// weight -- so a hot dog and a club sandwich and a burrito are three warm-coloured
    /// rectangles until you read the words under them. Holding the actual thing up settles it
    /// before you have read anything.
    ///
    /// IN HIS HAND RATHER THAN IN FRONT OF THE CAMERA, which is the whole design and was not
    /// the obvious choice. The other mod shows a gun by putting it on a table and moving the
    /// camera to it -- that works because a gun counter is a place you stand at. A pocket is
    /// opened anywhere, in the middle of anything, and taking somebody's camera off them every
    /// time they check what they are carrying is not a viewer, it is an interruption.
    ///
    /// It also sidesteps the two problems that arrangement has. A prop cannot be scaled the way
    /// a weapon object can, so a burger floated in front of the lens at a distance the game
    /// will focus at is the size of a pea; and anything close enough to be worth looking at is
    /// inside the near depth of field and comes out soft. A thing in his hand is at arm's
    /// length from a camera that is already behind him: real size, in focus, no camera taken.
    ///
    /// The panel sits in the top middle of the screen and his hands do not, which is the other
    /// reason this works without moving anything.
    ///
    /// NOTHING IS EATEN AND NOTHING IS DROPPED. It is attached, not given -- taken off him the
    /// moment the pocket shuts.
    /// </summary>
    internal sealed class Peek
    {
        /// <summary>The same bones the eating code uses, and for the same reason.</summary>
        private const int RightHandBone = 28422;
        private const int LeftHandBone = 60309;

        /// <summary>
        /// WHICH HAND. Set by whoever is showing something; right unless told otherwise.
        ///
        /// THIS WAS HARDCODED TO THE RIGHT AND IT COST A DAY OF SOMEBODY'S WORK. The fitting
        /// bench shows a model through this class, and the eating code was putting the same
        /// model in the LEFT hand -- so every number dialled on the bench was a number for
        /// the wrong hand, and there was nothing on screen to say so. A hand is a property of
        /// the ANIMATION, and the only place that knows which animation an item uses is the
        /// catalogue, so it is asked rather than assumed.
        /// </summary>
        public bool Lefty;

        /// <summary>
        /// How long a model is waited on before that item is shown empty-handed.
        ///
        /// CREATE_OBJECT on a model that is not in memory returns nothing, so the request has
        /// to be made and then waited on -- and waited on WITHOUT blocking, because this is
        /// called from a draw. Scrolling quickly through a pocket would otherwise stutter once
        /// per tile.
        /// </summary>
        private const int WaitMs = 3000;

        private Prop _held;
        private string _showing = "";

        private string _want = "";
        private int _asked;

        /// <summary>
        /// Everything ever made, so losing track of one is survivable.
        ///
        /// A held prop that the mod forgets about is a burger welded to somebody's hand for the
        /// rest of the session, and the field alone cannot promise that never happens.
        /// </summary>
        private readonly List<int> _made = new List<int>();

        /// <summary>Set by the pocket: the spin an item takes in the hand. See Eating.SpinFor.</summary>
        public Func<Vector3> Spin;

        /// <summary>
        /// And where it sits, the same as Eating.SitsFor. Null is the middle of the hand,
        /// which is where everything used to be and is what it looked like.
        /// </summary>
        public Func<Vector3> Sits;

        /// <summary>
        /// The whole answer at once, for a hand with no clip on it: the bone tag and the six,
        /// or null to use Lefty, Sits and Spin on the prop bone as before.
        ///
        /// FOR THE POCKET, which shows a thing with his arm at his side. The fits are made
        /// against the prop bone with the eating clip posing it, and with the arm down that
        /// bone is somewhere else -- so Eating works out the same place relative to his palm
        /// off the WRIST instead, and hands the numbers over here. See Eating.Palm. The
        /// fitting bench and the settings page leave this null: they fit with the clip
        /// playing, where the prop bone is the right one.
        ///
        /// GIVEN THE PROP because the maths uses it as a converter for a frame while it is
        /// detached; it is attached again on the line after.
        /// </summary>
        public Func<Ped, Prop, float[]> Palm;

        /// <summary>
        /// Show this model, or nothing.
        ///
        /// The name is the one the catalogue already settled on -- Item.Prop is chosen from the
        /// candidate list once the game is running, so a build without a given model has an
        /// empty string here and this quietly shows nothing rather than asking for a model that
        /// does not exist.
        /// </summary>
        public void Show(string prop)
        {
            prop = prop ?? "";

            if (prop == _showing && _held != null && _held.Exists()) return;
            if (prop == _want && _held == null && prop.Length > 0) { Build(); return; }

            Clear();

            if (prop.Length == 0) return;

            _want = prop;
            _asked = 0;

            Build();
        }

        /// <summary>Makes it once the game has the model, and gives up quietly if it never comes.</summary>
        private void Build()
        {
            if (_held != null || _want.Length == 0) return;

            var now = Game.GameTime;
            if (_asked == 0) _asked = now;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || !me.IsAlive) return;

                var model = new Model(_want);

                if (!model.IsValid || !model.IsInCdImage)
                {
                    _want = "";
                    return;
                }

                if (!model.IsLoaded)
                {
                    model.Request();

                    if (now - _asked > WaitMs)
                    {
                        Log.Once("peek-" + _want, _want + " would not load; nothing in hand for that one.");
                        _want = "";
                    }

                    return;
                }

                _held = World.CreateProp(model, me.Position, false, false);
                model.MarkAsNoLongerNeeded();

                if (_held == null || !_held.Exists()) { _held = null; _want = ""; return; }

                _made.Add(_held.Handle);

                Seat(me);

                _showing = _want;
                _want = "";
                _asked = 0;
            }
            catch (Exception ex)
            {
                Log.Once("peek-fail-" + _want, "Could not hold that up: " + ex.Message);
                _want = "";
                _held = null;
            }
        }

        /// <summary>Called every frame the pocket is open, so a model still arriving still lands.</summary>
        /// <summary>
        /// Puts it where it belongs, again, every tick.
        ///
        /// AGAIN, SO THE NUMBERS CAN BE DIALLED WITH IT IN HIS HAND. Attaching once means a
        /// change to the offset does nothing until the next thing is picked up, and the whole
        /// reason this class holds a real prop is that somebody is looking at it.
        ///
        /// ATTACH_ENTITY_TO_ENTITY on something already attached moves it rather than
        /// refusing. The six trailing arguments are the eating code's: no soft pinning, no
        /// collision, not treated as a ped, vertex 2, fixed rotation.
        /// </summary>
        private void Seat(Ped me)
        {
            if (me == null || !me.Exists() || _held == null || !_held.Exists()) return;

            // OFF THE WRIST WHEN THERE IS AN ANSWER FOR IT. See Palm.
            if (Palm != null)
            {
                float[] palm = null;

                try { palm = Palm(me, _held); }
                catch { palm = null; }

                if (palm != null && palm.Length >= 7)
                {
                    var wrist = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, (int)palm[0]);

                    Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _held.Handle, me.Handle, wrist,
                                  palm[1], palm[2], palm[3],
                                  palm[4], palm[5], palm[6], false, false, false, false, 2, true);
                    return;
                }
            }

            var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle,
                                          Lefty ? LeftHandBone : RightHandBone);

            var spin = Spin == null ? Vector3.Zero : Spin();
            var sits = Sits == null ? Vector3.Zero : Sits();

            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _held.Handle, me.Handle, bone,
                          sits.X, sits.Y, sits.Z,
                          spin.X, spin.Y, spin.Z, false, false, false, false, 2, true);
        }

        public void Tick()
        {
            Build();

            // AND AGAIN, so a number turned on the menu moves the thing he is holding while
            // you watch it. See Seat.
            try { Seat(Game.Player.Character); }
            catch { /* it stays where it was, which is in his hand */ }
        }

        public void Clear()
        {
            Sweep();

            _showing = "";
            _want = "";
            _asked = 0;

            if (_held == null) return;

            try
            {
                if (_held.Exists())
                {
                    // DETACHED FIRST. Deleting a thing that is still pinned to a bone has been
                    // known to leave the attachment behind it.
                    Function.Call(Hash.DETACH_ENTITY, _held.Handle, true, true);

                    // NOT HANDED BACK FIRST. Marking it no longer needed gives it to the game,
                    // and a thing the game owns is not ours to delete any more -- the delete
                    // that follows does nothing and the prop stays in his hand.
                    _held.Delete();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not take it out of his hand: " + ex.Message);
            }

            _made.Remove(_held.Handle);
            _held = null;

            Sweep();
        }

        /// <summary>Anything ever made that is still about, taken away.</summary>
        private void Sweep()
        {
            for (var i = _made.Count - 1; i >= 0; i--)
            {
                var handle = _made[i];

                if (_held != null && _held.Exists() && handle == _held.Handle) continue;

                _made.RemoveAt(i);

                try
                {
                    var thing = Entity.FromHandle(handle);
                    if (thing == null || !thing.Exists()) continue;

                    Function.Call(Hash.DETACH_ENTITY, handle, true, true);
                    thing.Delete();
                }
                catch
                {
                    // Next sweep, or the game's own clean-up.
                }
            }
        }
    }
}
