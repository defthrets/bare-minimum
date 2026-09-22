using System;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.UI;

namespace BareMinimum.Bodies
{
    /// <summary>
    /// Taking a body under the arms and walking backwards with it.
    ///
    /// THIS CAME OVER FROM FIVE0 PATROL ON 2026-09-22, and it came over because the corpse now
    /// belongs to one mod. Five0 had the carry and Posted Up had the loot, so two mods offered
    /// two things on one body over one key and had to talk to each other by reflection to work
    /// out whose turn it was. Both halves are here now. The bridge is a method call.
    ///
    /// HIS BODY IS CARRIED, NOT DRAGGED BY PHYSICS, which took three goes to arrive at.
    /// ATTACH_ENTITY_TO_ENTITY welds an entity to a bone and takes it out of the physics, so
    /// the first version was a mannequin held at the angle it died in. The version after that
    /// gave up on attaching and towed him by nudging his velocity, which slides but cannot be
    /// steered and throws him about on a kerb. The one after THAT used a physical constraint,
    /// which on this install sat inert: "body 0.0 m/s" for as long as it was held, with the
    /// hands drifting four metres apart.
    ///
    /// So he is brought back so that a clip will play on him -- a corpse takes no clip -- put
    /// in a death pose, and welded to the player's chest at an offset. A man in somebody's
    /// arms. Put undoes all of it and kills him again, so what is set down is a corpse.
    ///
    /// AND YOU WALK BACKWARDS, FACING HIM, by pushing back on the stick like anybody walks
    /// backwards in this game. Nothing turns him: forcing his heading every frame is a fight
    /// the player cannot win and reads as a man who will not steer.
    ///
    /// IT WAITS FOR THE POCKETS. You search him and then you move him, in that order, because
    /// moving him first means walking back to wherever you put him -- so the prompt does not
    /// appear until it is the only prompt. That used to be a reflection call into another mod;
    /// it is Searched now, wired by Main to this mod's own body store.
    /// </summary>
    internal sealed class Drag
    {
        /// <summary>When the grip was taken, so losing him says how long he lasted.</summary>
        private int _tookAt;

        /// <summary>How close you have to be to take hold of somebody.</summary>
        private const float Reach = 2.4f;

        /// <summary>And how long the key has to be down before it counts.</summary>
        private const int HoldMs = 260;

        /// <summary>
        /// How far he may get before he counts as left behind.
        ///
        /// MEASURED ORIGIN TO ORIGIN, WHICH IS FEET TO FEET, and a man held against a chest
        /// with his legs trailing has his feet a long way from yours. 4.5 was tripping on a
        /// body that was correctly held and merely lying down.
        /// </summary>
        private const float Snap = 7.5f;

        /// <summary>
        /// The game's own drag, and the half of it the player gets.
        ///
        /// THE PAIRED CLIPS ARE _plyr AND _ped AND ONLY THE FIRST GOES ON HIM. The dictionary
        /// ships both halves of a two-man drag; the ped half is on the body, and it is on the
        /// body through DragPose rather than from here, because the body has to be alive to
        /// take a clip at all.
        /// </summary>
        private const string DragDict = "combat@drag_ped@";
        private const string DragClip = "injured_drag_plyr";

        /// <summary>Loop, upper body, and let him walk. The same flag the street chats gesture with.</summary>
        private const int Over = 49;

        /// <summary>WEAPON_UNARMED, as SET_CURRENT_PED_WEAPON wants it.</summary>
        private const int Unarmed = unchecked((int)0xA2719263);

        private readonly Core.Settings _cfg;

        /// <summary>Who is being carried, and whether anybody is.</summary>
        private Ped _body;

        private int _downSince;

        /// <summary>Whether the mod may take hold of anybody just now. Wired by Main.</summary>
        public Func<bool> Busy;

        /// <summary>
        /// Whether that body has already been gone through. Wired by Main to the body store.
        ///
        /// NULL MEANS NOTHING IS SEARCHING BODIES, which is the honest answer when looting is
        /// switched off -- there is then nothing to wait for and the carry is offered straight
        /// away. A missing half must not be a feature that silently never appears.
        /// </summary>
        public Func<int, bool> Searched;

        /// <summary>Whether the loot half is running at all. Wired by Main. See Searched.</summary>
        public Func<bool> Looting;

        public Drag(Core.Settings cfg)
        {
            _cfg = cfg;

            // The tuner's numbers are the ini's. See DragPose.Bind.
            DragPose.Bind(cfg);
        }

        /// <summary>Whoever is in hand, or null.</summary>
        public Ped Carrying
        {
            get { return _body != null && _body.Exists() ? _body : null; }
        }

        /// <summary>Whether the setting allows it at all. For Api.Bodies.</summary>
        public bool Allowed => _cfg != null && _cfg.CarryBodies;

        /// <summary>Whether somebody is in hand. For Api.Bodies.</summary>
        public bool Holding => _body != null && _body.Exists();

        /// <summary>
        /// Takes hold of a body somebody else picked, by handle.
        ///
        /// FOR THE LOOT SCREEN. The card goes up over a corpse and the last thing you want
        /// after reading it is the body gone -- so the footer of that screen is the honest
        /// place for the option, and this is what it calls. It is also what the other mods on
        /// this machine reach through Api.Bodies.
        ///
        /// THE SAME RULES AS TAKING HOLD BY HAND, checked here rather than trusted from over
        /// there: close enough, dead, not already holding somebody. A caller cannot be expected
        /// to know what Reach is, and it is not its business.
        /// </summary>
        public bool TakeByHandle(int handle)
        {
            try
            {
                if (!Allowed || Holding || handle == 0) return false;

                var me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsDead || me.IsInVehicle()) return false;

                var body = Entity.FromHandle(handle) as Ped;
                if (body == null || !body.Exists() || body.IsAlive) return false;

                if (body.Position.DistanceTo(me.Position) > Reach + 1.5f) return false;

                Take(me, body);

                return Holding;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not take that body by handle: " + ex.Message);
                return false;
            }
        }

        public void Update()
        {
            if (_cfg == null || !_cfg.CarryBodies)
            {
                if (_body != null) Put();
                return;
            }

            try
            {
                var me = Game.Player.Character;

                if (me == null || !me.Exists() || me.IsDead) { Put(); return; }

                if (_body != null) { Hauling(me); return; }

                // NOTHING IN HAND, SO NOTHING ON HIS ARMS EITHER.
                //
                // Put clears the clip and the task it sits in, and Put runs on every ending
                // this file knows about -- dropping him, walking away from him, dying, the
                // setting going off. What it does not cover is an ending this file never saw:
                // another mod clearing the player's tasks and re-tasking him, a reload part way
                // through, a scripted scene. Any of those leave the loop running with nobody to
                // stop it, and he walks around holding an invisible body for the rest of the
                // session.
                //
                // So it is checked rather than assumed. One IS_ENTITY_PLAYING_ANIM on a frame
                // where there is nothing to carry, which is nearly all of them.
                if (Core.Anim.IsPlaying(me, DragDict, DragClip))
                {
                    Core.Anim.Stop(me, DragDict, DragClip);
                    Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, me.Handle);
                }

                // NOT WHILE SOMETHING ELSE OF OURS OWNS THE KEY. A screen is up, he is asleep,
                // he is mid-meal: none of those is a moment to start picking bodies up.
                if (Busy != null && Busy()) { _downSince = 0; return; }

                Waiting(me);
            }
            catch (Exception ex)
            {
                Log.Debug("The body carry went wrong: " + ex.Message);
                Put();
            }
        }

        // ---- nothing in hand ------------------------------------------------------

        private void Waiting(Ped me)
        {
            var body = Nearest(me);

            if (body == null) { _downSince = 0; return; }

            // HIS POCKETS FIRST. While the loot half still has something to offer on this body
            // there is one prompt on screen and it is not this one. See Searched.
            if (Looting != null && Looting() && Searched != null && !Searched(body.Handle))
            {
                _downSince = 0;
                return;
            }

            if (!Down())
            {
                _downSince = 0;
                Hint.Show("Carry the body", Pad.Cap(_cfg.InteractKey), -1f);
                return;
            }

            var now = Game.GameTime;

            if (_downSince == 0) _downSince = now;

            var held = (now - _downSince) / (float)HoldMs;

            Hint.Show("Carry the body", Pad.Cap(_cfg.InteractKey), held);

            if (held < 1f) return;

            _downSince = 0;
            Take(me, body);
        }

        /// <summary>
        /// The nearest corpse worth taking hold of.
        ///
        /// A DEAD PED, NOT A DOWNED ONE. IsDead is the only test that matters here: an injured
        /// man on the floor gets up again, and taking hold of one would be a scene this mod has
        /// no ending for.
        ///
        /// AND A PERSON. Every animal in Los Santos is a ped as far as the game is concerned,
        /// so a scan that asks for peds gets the cats and the coyotes with them -- see the same
        /// note in Bodies.Search, where a dead cat came up as a man with ninety-eight dollars
        /// on him.
        /// </summary>
        private static Ped Nearest(Ped me)
        {
            Ped best = null;
            var bestGap = Reach;

            try
            {
                foreach (var ped in World.GetNearbyPeds(me, Reach))
                {
                    if (ped == null || !ped.Exists()) continue;
                    if (ped.Handle == me.Handle) continue;
                    if (!ped.IsDead) continue;
                    if (!ped.IsHuman) continue;

                    var gap = ped.Position.DistanceTo(me.Position);
                    if (gap >= bestGap) continue;

                    bestGap = gap;
                    best = ped;
                }
            }
            catch
            {
                return null;
            }

            return best;
        }

        // ---- taking hold ----------------------------------------------------------

        private void Take(Ped me, Ped body)
        {
            try
            {
                // OURS FOR THE DURATION. A body the population manager still owns can be culled
                // out of your hands mid-street, and it will be, because a corpse is exactly what
                // it looks for something to clean up.
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, body.Handle, true, true);

                // NOT FROZEN, NOT ATTACHED, AND ABLE TO BE PUSHED ABOUT. The last two are the
                // defaults for a corpse and are asserted anyway, because another mod may well
                // have had hold of this body before we did.
                Function.Call(Hash.DETACH_ENTITY, body.Handle, true, true);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, body.Handle, false);
                Function.Call(Hash.SET_ENTITY_COLLISION, body.Handle, true, true);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, body.Handle, true);

                // AND NOTHING ELSE IS DRIVING HIM. A ped with any task left on it is a ped the
                // animation system is still posing, and a posed body on the end of a weld came
                // out upside down in the air: the pose says stand up, the weld says your chest
                // is over there, and what you get is a man pivoting about his own hand.
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, body.Handle);

                // HANDS FREE. The carry clip is an upper-body secondary, and a weapon in hand
                // owns the upper body: with a rifle out he stood there holding the rifle and
                // the clip never showed. Unarmed for the duration; Hauling keeps the weapon
                // keys off so he cannot draw one under it.
                Function.Call(Hash.SET_CURRENT_PED_WEAPON, me.Handle, Unarmed, true);

                if (Core.Anim.Ready(DragDict)) Core.Anim.Play(me, DragDict, DragClip, Over);

                Hold(me, body);

                _body = body;
                _tookAt = Game.GameTime;

                // THE KEY IS STILL DOWN, AND IT MUST NOT READ AS A DROP.
                //
                // Taking hold is a HOLD of the interact key and putting him down is a TAP of
                // it, so the very frame the grip lands the finger is still on the key. The
                // control side is safe -- IS_CONTROL_JUST_PRESSED will not fire again until it
                // is released -- but the raw key is read as an edge against this flag, and an
                // edge against false is a press. He would have been picked up and dropped on
                // consecutive frames.
                _dropWas = true;

                Log.Info("A body is being carried.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not take hold of a body: " + ex.Message);
                _body = null;
            }
        }

        /// <summary>
        /// HE IS PUT IN THE PLAYER'S ARMS, POSED, AND WELDED THERE. No physics on him at all.
        ///
        /// THREE PHYSICS TETHERS IN A ROW SAT INERT ON THIS INSTALL, and the log of the last
        /// of them is the whole argument: a rope tied hand to hand, textures loaded, the ped
        /// alive and IS_PED_RAGDOLL saying yes -- and "body 0.0 m/s" for as long as it was
        /// held, with the hands drifting four metres apart. Whatever simulates a corpse here,
        /// nothing a script pins to it moves it. So nothing is pinned to it.
        ///
        /// A WELD IS THE ONE ATTACHMENT THAT HAS ALWAYS HELD. The very first version of this
        /// file was one, and its fault was the pose: a mannequin held at the angle he died in.
        /// So the pose is chosen. He is brought back so that a clip will play on him -- a
        /// corpse takes no clip -- put in a death pose, and welded to the player's chest at an
        /// offset, which is a man carried in somebody's arms.
        ///
        /// THE OFFSET, THE TURN AND THE POSE ARE DragPose's, and they are set with the body
        /// in front of you rather than guessed here. The defaults are the numbers that were
        /// set that way and baked in; the ini is how they change, and the tuner is the other
        /// way -- Tuner in the ini, the numpad while a body is in hand, and 0 writes the
        /// answer into the log as the lines to paste.
        /// </summary>
        private static void Hold(Ped me, Ped body)
        {
            try
            {
                var now = Game.GameTime;

                // ---- ALREADY IN HIS ARMS? THE POSE IS KEPT UP, AND THAT IS ALL ----
                //
                // Shaped plays the clip when it is not running and asks for the dictionary
                // when it is not in yet, so it is safe to call every pass and it is the only
                // way the pose arrives on a body whose clip was not resident on the grip --
                // or comes back on one the engine threw it off. See DragPose.Shaped.
                //
                // ABOVE THE RATE LIMIT, NOT UNDER IT. This sat below the once-a-second guard,
                // so for the first second after a weld nothing could put a pose on him at all
                // -- which is precisely the second the engine's get-up is stripping it off.
                if (body.Handle == _limp &&
                    Function.Call<bool>(Hash.IS_ENTITY_ATTACHED_TO_ENTITY, body.Handle, me.Handle))
                {
                    DragPose.Shaped(body);
                    return;
                }

                // ---- AND NOT MORE THAN ONCE A SECOND, WHATEVER HAPPENS ----
                if (now - _madeAt < RemakeGapMs) return;

                // ---- 1. ALIVE, BECAUSE A CORPSE TAKES NO CLIP ----
                if (body.IsDead)
                {
                    var where = body.Position;

                    Function.Call(Hash.RESURRECT_PED, body.Handle);
                    Function.Call(Hash.SET_ENTITY_HEALTH, body.Handle, 200);

                    // RESURRECT CAN MOVE HIM. Put back where he was lying.
                    body.Position = where;
                }

                // ---- 2. AND NOTHING ABOUT BEING ALIVE SHOWS ----
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, body.Handle, true);
                Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, body.Handle, false);
                Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, body.Handle, false);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, body.Handle, false);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, body.Handle, true);
                Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, body.Handle, true);
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, body.Handle);
                Function.Call(Hash.SET_PED_CAN_PLAY_AMBIENT_ANIMS, body.Handle, false);

                // AND HE SHOVES NOBODY. Off between the two of them by native, and off
                // altogether below: a body welded a foot from a chest with its collision on is
                // a body pushing him down the street.
                Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, body.Handle, me.Handle, true);
                Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, me.Handle, body.Handle, true);

                // ---- 3. NO PHYSICS ON HIM. He is a picture in the player's arms, and the ----
                // ---- picture is the pose. A ped that can ragdoll will, the moment a weld  ----
                // ---- moves him, and a ragdolling ped owns its own transform.              ----
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, body.Handle, false);
                Function.Call(Hash.SET_ENTITY_COLLISION, body.Handle, false, false);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, body.Handle, false);
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, body.Handle);

                // THE CLEAR ABOVE STOPPED THE POSE. Shaped plays a clip once per body per
                // shape, so without this a re-pick or a slider nudge came back unposed --
                // see DragPose.Forget.
                DragPose.Forget();
                DragPose.Shaped(body);

                // ---- 4. THE WELD ----
                //
                // OFF ANYTHING FIRST. Re-attaching an attached entity moves it rather than
                // re-catching it, which is fine, but a body the last mod left attached to
                // something else is not.
                Function.Call(Hash.DETACH_ENTITY, body.Handle, true, true);

                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, DragPose.BoneId);
                var at = DragPose.Offset;

                // ATTACH_ENTITY_TO_ENTITY(entity1, entity2, boneIndex, x, y, z, xRot, yRot,
                //   zRot, p9, useSoftPinning, collision, isPed, vertexIndex, fixedRot). The
                // offset is from the bone in the player's own axes -- across, forward, up --
                // and the rotation is the body's relative to that bone. Fixed rotation, or he
                // turns with whatever the pose's root does; no soft pinning, or it is a spring
                // again; no collision, see above; isPed, because he is one.
                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, body.Handle, me.Handle, bone,
                              at.X, at.Y, at.Z,
                              DragPose.Pitch, DragPose.Roll, DragPose.Yaw,
                              false, false, false, true, 2, true);

                _limp = body.Handle;
                _madeAt = now;
                _saidAt = now;

                Log.Info("Carry: held  attached=" +
                         Function.Call<bool>(Hash.IS_ENTITY_ATTACHED_TO_ENTITY, body.Handle, me.Handle) +
                         "  pose=" + DragPose.ShapeName +
                         "  on " + DragPose.BoneName +
                         " at " + at.X.ToString("0.00") + ", " + at.Y.ToString("0.00") + ", " + at.Z.ToString("0.00") +
                         "  turned " + DragPose.Pitch.ToString("0") + "/" + DragPose.Roll.ToString("0") + "/" +
                         DragPose.Yaw.ToString("0") +
                         "  dead=" + body.IsDead);
            }
            catch (Exception ex)
            {
                Log.Info("Carry hold FAILED: " + ex.Message);

                // STAMPED EVEN ON A FAILURE, or the rate limit above stops applying and a
                // throwing hold is retried sixty times a second.
                _madeAt = Game.GameTime;
                _limp = 0;
            }
        }

        /// <summary>Who is in his arms. See Hold.</summary>
        private static int _limp;

        /// <summary>When the weld was last made, so it cannot be made every frame.</summary>
        private static int _madeAt;

        private const int RemakeGapMs = 1000;

        /// <summary>When the state line was last written. See Hauling.</summary>
        private static int _saidAt;
        private const int SayEveryMs = 2000;

        // ---- walking backwards with him -------------------------------------------

        private void Hauling(Ped me)
        {
            if (_body == null || !_body.Exists()) { _body = null; return; }

            // A CAR ENDS IT, and so does dying. Both would otherwise leave a corpse being towed
            // through the world by somebody who is no longer walking.
            if (me.IsInVehicle()) { Put(); return; }

            var gap = _body.Position.DistanceTo(me.Position);

            // LEFT BEHIND. A fence, a car door, a wall he could not come round -- at some point
            // the honest answer is that you are not holding him any more.
            if (gap > Snap)
            {
                Log.Info("The body was left behind at " + gap.ToString("0.0") + "m after " +
                         (Game.GameTime - _tookAt) + "ms.");

                Put();
                return;
            }

            Hint.Show("Put the body down", Pad.Cap(_cfg.InteractKey), -1f);

            // WRITTEN EVERY PASS. The clip is upper-body over ordinary movement, and anything
            // that takes the player's tasks -- a stumble, a ragdoll, a scripted event -- clears
            // it and leaves him strolling along with a corpse sliding after him.
            if (!Core.Anim.IsPlaying(me, DragDict, DragClip))
            {
                Core.Anim.Play(me, DragDict, DragClip, Over);
            }

            // NO RUNNING WITH A BODY, and no jumping over things with one. And no drawing a
            // gun with one: the carry clip is on his arms, and a weapon takes them back.
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)GTA.Control.Sprint, true);
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)GTA.Control.Jump, true);
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)GTA.Control.SelectWeapon, true);
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)GTA.Control.NextWeapon, true);
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)GTA.Control.PrevWeapon, true);
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)GTA.Control.Aim, true);
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)GTA.Control.Attack, true);

            // WHAT IS ACTUALLY HAPPENING, EVERY TWO SECONDS, because "not working" arrived
            // three times with a log that said only that the grip was made. Where he is
            // against you, whether he is still attached, whether the pose is still on him:
            // the next report comes with its evidence.
            if (Game.GameTime - _saidAt >= SayEveryMs)
            {
                _saidAt = Game.GameTime;

                try
                {
                    Log.Info("Carry: body " + gap.ToString("0.0") + "m from you" +
                             ", attached=" + Function.Call<bool>(Hash.IS_ENTITY_ATTACHED_TO_ENTITY, _body.Handle, me.Handle) +
                             ", posed=" + DragPose.Playing(_body) +
                             ", dead=" + _body.IsDead +
                             ", clip=" + Core.Anim.IsPlaying(me, DragDict, DragClip));
                }
                catch
                {
                    // The line can miss one.
                }
            }

            // HIS HEADING IS HIS OWN, AND THAT IS THE FIX FOR NOT BEING ABLE TO STEER.
            //
            // This used to force SET_ENTITY_HEADING every frame to the reverse of whichever way
            // he was travelling, so that pushing forward walked him backwards. It is a lovely
            // idea and it is a fight the player cannot win: he pushes the stick, the game turns
            // the ped toward it, this turns him back, and the next frame does it again. What
            // that feels like from the outside is a man who will not steer and a camera that
            // judders -- reported as not being able to control him, which is exactly right.
            //
            // Nothing turns him now. He walks backwards the way anybody walks backwards in this
            // game, by pushing back on the stick, and the body hangs off his chest in front of
            // him. The carry clip is authored for a man walking backwards and reads correctly
            // the moment he actually is.

            // A NUMBER MOVED ON THE TUNER IS A NEW GRIP. The weld is made once and kept, so a
            // changed offset or bone would otherwise wait for the next top-up to be seen.
            if (DragPose.Update(_body, _cfg != null && _cfg.DragTuner) || DragPose.Dirty)
            {
                DragPose.Dirty = false;
                _limp = 0;
                _madeAt = 0;
            }

            // HE CANNOT SHOVE YOU, ASKED EVERY FRAME. This is the one that launched the player
            // off a roof, so it is not left to a flag set once: a next-frame suppression
            // renewed for as long as he is in hand, which lapses on its own when he is not.
            try
            {
                Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, _body.Handle, me.Handle, true);
                Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, me.Handle, _body.Handle, true);
            }
            catch
            {
                // One frame of it is not worth a line in the log.
            }

            // THE WELD, EVERY PASS, AND NOTHING ELSE. This used to dispatch on DragPose.Posed
            // -- a synchronised scene when a pose was chosen, a constraint otherwise -- and the
            // pose defaulted to 1, so every carry anybody ever tried went down the scene path.
            // The log said what that did, five times in one evening:
            //
            //     Drag: held  ragdoll=True  attached=True  dead=False    (Take: the constraint)
            //     Drag: scene 14 running=True  dead=False               (next frame: the scene)
            //     The body was left behind at 1388.1m after 47ms.
            //
            // 1388 metres is how far Chamberlain Hills is from the world origin. The scene put
            // the body at nought, the leash saw him a kilometre away and let go, and what the
            // player saw was a corpse that vanished the moment he took hold of it. Hold is the
            // whole of it now; the scene and the constraint are gone from this file rather
            // than left to be dispatched to again.
            Hold(me, _body);

            if (Tapped()) Put();
        }

        // ---- the key --------------------------------------------------------------

        /// <summary>Whether the raw interact key was down last frame. See Tapped.</summary>
        private bool _dropWas;

        /// <summary>
        /// The interact key held down, as a level.
        ///
        /// THE BOUND KEY AND THE GAME'S OWN CONTEXT BUTTON, which is what every other world
        /// prompt in this mod reads -- see Venues.Inside and Venues.Vendors. A pad is an
        /// addition to the ini rather than a mode to switch into, and the disabled reading is
        /// there because a screen elsewhere in the mod may have suppressed the control this
        /// frame without meaning to suppress this.
        /// </summary>
        private bool Down()
        {
            try
            {
                if (_cfg != null && Game.IsKeyPressed(_cfg.InteractKey)) return true;
            }
            catch
            {
                // The control below is the other half of the answer.
            }

            try
            {
                return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)GTA.Control.Context) ||
                       Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)GTA.Control.Context);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The interact key going down, once per press. What puts him back on the floor.
        ///
        /// AN EDGE ON THE RAW KEY, kept here rather than asked of the game, because
        /// Game.IsKeyPressed is a level: held for a fifth of a second it is true across a dozen
        /// frames. The control side already answers as an edge, so it is asked as one.
        ///
        /// Take sets the flag true on the frame the grip lands, because the finger is still on
        /// the key that took hold -- see the note there.
        /// </summary>
        private bool Tapped()
        {
            var key = false;

            try { if (_cfg != null) key = Game.IsKeyPressed(_cfg.InteractKey); }
            catch { key = false; }

            var edge = key && !_dropWas;
            _dropWas = key;

            if (edge) return true;

            try { return Game.IsControlJustPressed(GTA.Control.Context); }
            catch { return false; }
        }

        /// <summary>
        /// Lets go. Everything Hold did to him is undone and he is killed again.
        /// </summary>
        public void Put()
        {
            var body = _body;
            _body = null;

            // OR THE NEXT BODY INHERITS THIS ONE'S GUARD and Hold returns at its first
            // line without ever taking hold of him.
            _limp = 0;
            _madeAt = 0;

            _downSince = 0;

            try
            {
                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    Core.Anim.Stop(me, DragDict, DragClip);

                    // AND THE TASK ITSELF, WHICH IS WHY HE GOT STUCK IN IT.
                    //
                    // The clip is played as a SECONDARY task -- which is what lets him walk
                    // while his arms do something else -- and STOP_ANIM_TASK addresses the
                    // clip, not the task holding it. Where the two disagree, and they do when
                    // the stop lands on the same frame the loop was re-issued, the task is
                    // still there and puts the clip straight back. He then walks around holding
                    // an invisible body until something else in the game clears his tasks.
                    //
                    // CLEAR_PED_SECONDARY_TASK is the one that ends it. It is also safe to call
                    // on somebody who is not doing anything, which is why it is unconditional.
                    Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, me.Handle);
                }
            }
            catch
            {
                // The clip is upper body and will fall off on its own.
            }

            if (body == null || !body.Exists()) return;

            try
            {
                // LET GO OF HIM FIRST. An attachment outlives the mod that made it: a body left
                // attached is a body that follows the player around for the rest of the
                // session, through walls and into cars.
                Function.Call(Hash.DETACH_ENTITY, body.Handle, true, true);

                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, body.Handle);

                // ---- AND EVERYTHING HOLD DID TO HIM IS UNDONE ----
                //
                // FOUND BY READING PUT AGAINST HOLD RATHER THAN BY PLAYING. Hold brings him
                // back to life so that he can take a clip, and Put did not kill him again -- so
                // a body set down was left ALIVE, invincible, mute, untargetable, deaf to every
                // event, and free to stand up and walk off the moment his ragdoll expired.
                // Every corpse ever carried would have got up a minute later.
                //
                // Killed last, after the flags are off, or an invincible ped ignores it.
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, body.Handle, false);
                Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, body.Handle, true);
                Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, body.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, body.Handle, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, body.Handle, false);
                Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, body.Handle, false);
                Function.Call(Hash.SET_PED_CAN_PLAY_AMBIENT_ANIMS, body.Handle, true);

                Function.Call(Hash.FREEZE_ENTITY_POSITION, body.Handle, false);
                Function.Call(Hash.SET_ENTITY_COLLISION, body.Handle, true, true);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, body.Handle, true);

                // AWAKE, OR HE HANGS WHERE HE WAS LET GO OF. A prop or a ped set down by a
                // script spawns asleep as far as the solver is concerned and does not fall
                // until something touches it -- the same thing that left a dropped crisp packet
                // hanging in the air. See Food.Eating and the litter.
                Function.Call(Hash.ACTIVATE_PHYSICS, body.Handle);

                Function.Call(Hash.SET_ENTITY_HEALTH, body.Handle, 0);
            }
            catch
            {
                // He was not held, or he is already gone.
            }

            try
            {
                // AND HE STOPS DEAD RATHER THAN CARRYING ON. Whatever the last frame gave him
                // is still on him, and a corpse released at a walking pace slides off up the
                // pavement on his own.
                body.Velocity = Vector3.Zero;

                body.MarkAsNoLongerNeeded();
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put a body down: " + ex.Message);
            }
        }

        /// <summary>Teardown. Whoever is in hand is let go of rather than left owned.</summary>
        public void Release()
        {
            Put();
            Hint.Clear();
        }
    }
}
