using System;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Food
{
    /// <summary>
    /// The smoke off the lit end of the cigarette, the whole time it is in his hand.
    ///
    /// THE OTHER HALF OF A MAN SMOKING. Exhale is the lungful out of his mouth on the beat
    /// the clip lowers his hand; this is the thin wisp that comes off the tip between drags,
    /// which is most of the time and most of what you see. The game's own smoking scenario
    /// has both -- watch the hot dog man on his break -- and a script-driven clip has neither
    /// unless something puts them there. Michael asked for the smoke to come off the
    /// cigarette rather than off him on 2026-09-25, and this is that.
    ///
    /// THE GAME'S OWN EFFECT, OUT OF THE GAME'S OWN ASSET. ent_anim_cig_smoke is what the
    /// ambient smokers run on their cigarette, and it lives in scr_mp_cig beside the exhale
    /// that Exhale already proved plays on this install. Both names are in Menyoo's list.
    ///
    /// WHERE THE TIP IS IS MEASURED, NOT GUESSED. A cigarette model is a thin thing along one
    /// axis and its origin is wherever the artist left it, so the lit end is worked out from
    /// the model's own bounding box: the two ends of its longest axis, and the one farther
    /// from his palm is the lit one, because the filter is the end between his fingers. That
    /// holds for the cigars as well, and for any model somebody puts in foods.json later.
    ///
    /// LOOPED ON THE PROP, so it rides the cigarette to his mouth and back with no work here,
    /// and stopped by whoever deletes the prop. A looped effect left running on a deleted
    /// entity is a wisp hanging in the air where his hand was.
    /// </summary>
    internal static class Smoulder
    {
        private const string Asset = "scr_mp_cig";
        private const string Fx = "ent_anim_cig_smoke";

        /// <summary>How long the asset is waited on before the tip is given up for the session.</summary>
        private const int WaitMs = 8000;

        /// <summary>
        /// How long a new prop is left alone before its ends are measured against his palm.
        ///
        /// A PROP IS MADE AT HIS FEET AND ATTACHED A LINE LATER, and the attach takes a frame
        /// to move it. Measured on that first frame both ends are the same distance from his
        /// hand and the lit end is a coin toss, for the rest of the smoke.
        /// </summary>
        private const int SettleMs = 120;

        /// <summary>The looped effect, or nought; and the prop it is on.</summary>
        private static int _fx;
        private static int _on;

        /// <summary>The prop last seen, and when, so a new one settles before it is measured.</summary>
        private static int _seen;
        private static int _seenAt;

        private static bool _gaveUp;
        private static int _askedAt;
        private static bool _said;

        /// <summary>
        /// Keeps the tip smoking. Called every frame a smoke is in his hand; costs one native
        /// call once it is running.
        /// </summary>
        public static void Update(Ped me, Prop prop, bool lefty, string model)
        {
            if (_gaveUp) return;

            if (me == null || prop == null || !prop.Exists()) { Stop(); return; }

            try
            {
                if (_fx != 0 && _on == prop.Handle &&
                    Function.Call<bool>(Hash.DOES_PARTICLE_FX_LOOPED_EXIST, _fx))
                {
                    return;
                }

                Stop();

                var now = Game.GameTime;

                if (_seen != prop.Handle) { _seen = prop.Handle; _seenAt = now; }
                if (now - _seenAt < SettleMs) return;

                Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, Asset);

                if (_askedAt == 0) _askedAt = now;

                if (!Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, Asset))
                {
                    // STREAMING IS NOT FAILING, for a while. Exhale has the same asset and
                    // usually has it resident already; a session where it never comes is one
                    // where the tip does not smoke, said once.
                    if (now - _askedAt > WaitMs)
                    {
                        _gaveUp = true;
                        Log.Info("Smoulder: " + Asset + " never arrived, so the cigarette does not " +
                                 "smoulder this session.");
                    }

                    return;
                }

                Vector3 mid;
                var tip = Tip(me, prop, lefty, out mid);

                Function.Call(Hash.USE_PARTICLE_FX_ASSET, Asset);

                _fx = Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_ON_ENTITY, Fx, prop.Handle,
                                         tip.X, tip.Y, tip.Z, 0f, 0f, 0f,
                                         1.0f, false, false, false);

                if (_fx == 0)
                {
                    // THE GAME HAS THE ASSET AND WILL NOT PLAY THE EFFECT, which is the only
                    // answer that means the name is wrong. Nothing else is tried: the
                    // alternatives are steam and gas jets with a hiss on them.
                    _gaveUp = true;
                    Log.Info("Smoulder: " + Asset + " is loaded and " + Fx + " will not play out of " +
                             "it, so the cigarette does not smoulder this session.");
                    return;
                }

                _on = prop.Handle;

                if (!_said)
                {
                    _said = true;
                    Log.Info("Smoulder: " + Fx + " on the lit end of " + (model ?? "the cigarette") +
                             ", " + ((tip - mid).Length() * 100f).ToString("0.#") +
                             " cm from the middle of the model, at the end away from his palm.");
                }
            }
            catch (Exception ex)
            {
                _gaveUp = true;
                Log.Debug("The tip smoke failed and is off for the session: " + ex.Message);
            }
        }

        /// <summary>Puts it out. Safe when nothing is lit.</summary>
        public static void Stop()
        {
            if (_fx != 0)
            {
                try
                {
                    Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, _fx, false);
                    Function.Call(Hash.REMOVE_PARTICLE_FX, _fx, false);
                }
                catch
                {
                    // It dies with the prop.
                }
            }

            _fx = 0;
            _on = 0;
        }

        /// <summary>
        /// The lit end, in the model's own axes. See the note on the class.
        /// </summary>
        private static Vector3 Tip(Ped me, Prop prop, bool lefty, out Vector3 mid)
        {
            var min = new OutputArgument();
            var max = new OutputArgument();

            Function.Call(Hash.GET_MODEL_DIMENSIONS, prop.Model.Hash, min, max);

            var lo = min.GetResult<Vector3>();
            var hi = max.GetResult<Vector3>();

            var size = hi - lo;
            mid = (lo + hi) * 0.5f;

            // The two ends of the longest axis, on the centre line of the other two.
            var a = mid;
            var b = mid;

            if (size.X >= size.Y && size.X >= size.Z) { a.X = lo.X; b.X = hi.X; }
            else if (size.Y >= size.Z) { a.Y = lo.Y; b.Y = hi.Y; }
            else { a.Z = lo.Z; b.Z = hi.Z; }

            // The end away from his palm is the lit one: the filter is the end he holds.
            Vector3 palm;

            try { palm = me.Bones[lefty ? Bone.SkelLeftHand : Bone.SkelRightHand].Position; }
            catch { palm = me.Position; }

            var da = prop.GetOffsetPosition(a).DistanceTo(palm);
            var db = prop.GetOffsetPosition(b).DistanceTo(palm);

            return da >= db ? a : b;
        }
    }
}
