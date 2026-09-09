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
        /// <summary>The same bone the eating code uses, and for the same reason.</summary>
        private const int RightHandBone = 28422;

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

                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, RightHandBone);
                var spin = Spin == null ? Vector3.Zero : Spin();

                // The same six trailing arguments the eating code uses: no soft pinning, no
                // collision, not treated as a ped, vertex 2, fixed rotation. They are the set
                // that was arrived at for a prop in a hand and there is no reason to differ.
                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _held.Handle, me.Handle, bone,
                              0f, 0f, 0f, spin.X, spin.Y, spin.Z, false, false, false, false, 2, true);

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
        public void Tick()
        {
            Build();
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
