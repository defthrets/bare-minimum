using System;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Bodies
{
    /// <summary>
    /// WHERE THE BODY HANGS AND WHAT SHAPE IT IS IN -- SET IN THE GAME, WITH THE BODY IN FRONT
    /// OF YOU, RATHER THAN GUESSED AT IN A SOURCE FILE.
    ///
    /// THE NUMBERS WERE NEVER GOING TO BE FOUND BY THINKING ABOUT THEM. An attach offset is
    /// three distances from a bone in the ped's own axes, and nobody can picture that: the way
    /// it has been done until now is to write 0.12, 0.28, -0.10, build, load, carry somebody,
    /// decide it is wrong, and start again -- four minutes a guess. This is the same loop with
    /// the build taken out of it, so a guess costs a keypress. It is the same argument the
    /// fitting bench makes about food in a hand; see UI.FitScreen.
    ///
    /// AND A POSE, BECAUSE THE RAGDOLL IS NOT HOLDING. A soft-pinned ragdoll is the honest way
    /// to drag a man and it is at the mercy of the solver: he folds, he catches on a kerb, he
    /// goes through a wall and the constraint hauls him back through it. So the body is PUT IN
    /// A SHAPE instead -- one of the game's own dead poses, or the animation it plays on
    /// somebody being dragged in its own missions -- and attached rigidly, which cannot fold
    /// and cannot argue. Which of those looks right is a thing to be looked at, not reasoned
    /// about, so it is on the same tool.
    ///
    /// ONLY WHILE A BODY IS IN HAND, and only when the ini says so. It reads the numpad, which
    /// nothing else in this mod touches, and it reads it exactly nowhere else -- no body, no
    /// keys. A player who never turns it on has a mod with no extra bindings in it at all.
    ///
    /// WHAT IT IS FOR IS PRINTING THE ANSWER. Numpad 0 writes the current settings into the
    /// log in the exact shape the source wants them, so a good position found at two in the
    /// morning is a line to paste rather than a number to remember.
    ///
    /// THIS FILE CAME OVER FROM FIVE0 PATROL ON 2026-09-22, when the body carry and the body
    /// loot were both moved here so that one mod owns the corpse. The numbers below are the
    /// ones Michael set with a body in his arms on 2026-09-19 and they have been carried over
    /// to the digit -- the whole point of moving the code was not to make him dial them again.
    /// </summary>
    internal static class DragPose
    {
        /// <summary>
        /// THE SETTINGS ARE THE NUMBERS. These are the [Bodies] keys, so the numpad and the ini
        /// move the same thing. Bound once by Drag; before that they read as the defaults and
        /// writes go nowhere.
        /// </summary>
        private static Core.Settings _cfg;

        public static void Bind(Core.Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>
        /// Set by every write below and cleared by Drag.Hauling, which re-welds the body on
        /// the next pass. A slider is not a slider if the thing it moves stays put.
        /// </summary>
        public static bool Dirty;

        public static float X
        {
            get { return _cfg == null ? Home[0] : _cfg.DragAcross; }
            set { if (_cfg != null) _cfg.DragAcross = value; Dirty = true; }
        }

        public static float Y
        {
            get { return _cfg == null ? Home[1] : _cfg.DragForward; }
            set { if (_cfg != null) _cfg.DragForward = value; Dirty = true; }
        }

        public static float Z
        {
            get { return _cfg == null ? Home[2] : _cfg.DragUp; }
            set { if (_cfg != null) _cfg.DragUp = value; Dirty = true; }
        }

        /// <summary>
        /// Which way round he is, in degrees about the bone.
        ///
        /// Nought was the only option before, because a soft-pinned ragdoll ignores rotation
        /// anyway -- the solver decides which way up he ends. It matters the moment a POSE is
        /// chosen, because a posed body is welded and faces exactly where it is told to.
        /// </summary>
        public static float Pitch
        {
            get { return _cfg == null ? Home[3] : _cfg.DragPitch; }
            set { if (_cfg != null) _cfg.DragPitch = value; Dirty = true; }
        }

        public static float Roll
        {
            get { return _cfg == null ? Home[4] : _cfg.DragRoll; }
            set { if (_cfg != null) _cfg.DragRoll = value; Dirty = true; }
        }

        public static float Yaw
        {
            get { return _cfg == null ? Home[5] : _cfg.DragYaw; }
            set { if (_cfg != null) _cfg.DragYaw = value; Dirty = true; }
        }

        /// <summary>
        /// Which shape he is in, and it starts on dead E. See Home.
        ///
        /// THIS NUMBER IS THE WHOLE OF HOW HE LOOKS IN YOUR ARMS. The body is welded to a bone
        /// of the player's and a clip is played on it and held on its last frame -- see Shaped
        /// -- so the pose is not a curiosity beside the mechanism, it is the mechanism. The
        /// list is the game's own laying and death poses; dead E is the one chosen with a
        /// body in hand, and the rest stay on the list to compare against.
        ///
        /// (It started on 1, the curled idle, as a first guess, and before that on the
        /// ragdoll, back when the carry was a constraint the solver could pull on. Neither
        /// held on this install. See Drag.Hold.)
        /// </summary>
        public static int Shape
        {
            get { return _cfg == null ? HomeShape : _cfg.DragShape; }
            set { if (_cfg != null) _cfg.DragShape = value; Dirty = true; }
        }

        /// <summary>
        /// WHAT HE IS PINNED TO, AND IT IS NOT THE HAND.
        ///
        /// THE WRIST IS THE WRONG THING TO MEASURE FROM. An attach offset is in the BONE's own
        /// space, so pinning to SKEL_R_Hand meant the body's position was relative to a hand
        /// that swings through a full arc every stride and rotates with every gesture. A
        /// position lined up perfectly while stood still came apart the moment he walked: the
        /// body swung round his legs, because the thing it was measured from was swinging.
        ///
        /// The chest is the bone that was settled on -- it turns when he turns and it does not
        /// swing, which is exactly the frame this wants. "A metre and a bit in front of him,
        /// the way he is facing" is a sentence about the man, not about his arm.
        ///
        /// THE HANDS ARE KEPT AS OPTIONS rather than deleted, because a wrist is the right
        /// answer for a body held UP by one and the wrong one for a body carried against a
        /// chest, and which of those this is has changed once already. End walks them.
        /// </summary>
        public static int Bone
        {
            get { return _cfg == null ? HomeBone : _cfg.DragBone; }
            set { if (_cfg != null) _cfg.DragBone = value; Dirty = true; }
        }

        /// <summary>The choices, for a settings page's two pickers. Same order as the ini numbers.</summary>
        public static string[] PoseNames => Named;
        public static string[] BoneLabels => BoneNames;

        /// <summary>
        /// The left hand, the right hand, the root, then the chest.
        ///
        /// 18905 is SKEL_L_Hand and 57005 is SKEL_R_Hand -- the wrists, which this was pinned
        /// to for as long as it was a thing held in a hand. 0 is SKEL_ROOT, the ped's own
        /// origin. 24818 is SKEL_Spine3, between the shoulder blades -- the same bone this mod
        /// hangs the rucksack off, and the one in use.
        /// </summary>
        private static readonly int[] Bones = { 18905, 57005, 0, 24818 };

        private static readonly string[] BoneNames =
        {
            "his left hand",
            "his right hand",
            "his root",
            "HIS CHEST",
        };

        /// <summary>The bone id the attach wants.</summary>
        public static int BoneId => Bones[Bone % Bones.Length];

        /// <summary>The same bone by name, for the log.</summary>
        public static string BoneName => GameNames[Bone % Bones.Length];

        private static readonly string[] GameNames =
        {
            "SKEL_L_Hand", "SKEL_R_Hand", "SKEL_ROOT", "SKEL_Spine3"
        };

        /// <summary>
        /// Where the numbers go back to on the reset key, and what they start at.
        ///
        /// NOT A GUESS. These are the numbers Michael set with a body in hand on 2026-09-19 --
        /// across, forward, up, tip, roll, turn, then the pose and the bone -- and they are the
        /// same eight Settings starts on and the ini ships with; change one and change all
        /// three. The first guess they replaced was half a metre out and a foot down, curled on
        /// his side, no turns.
        /// </summary>
        private static readonly float[] Home = { 0.78f, 0.88f, -0.04f, -75f, 40f, -15f };
        private const int HomeShape = 9;
        private const int HomeBone = 3;

        /// <summary>
        /// The shapes, in the order the tool walks them.
        ///
        /// EVERY NAME IS OFF THIS MACHINE'S OWN ANIMATION LIST rather than remembered. The
        /// "dead" dictionary is eight whole-body dead poses the game ships for exactly this --
        /// a man on the floor, in eight different arrangements of limbs -- and
        /// combat@drag_ped@ is the pair the game itself uses when a character drags a casualty:
        /// injured_drag_plyr is what the CARRIER does and injured_drag_ped is what the body
        /// does, so the second of those is a body already shaped to be pulled along.
        ///
        /// NOUGHT IS NO CLIP AT ALL -- welded as he died, which is the mannequin the very first
        /// version of this produced and is kept only to compare against.
        /// </summary>
        private static readonly string[][] Shapes =
        {
            null,                                                 // 0 -- no clip: welded as he died

            // CURLED UP ON HIS SIDE. The game's slumped-bum idle is a man lying on his left
            // side with his knees drawn up, and lifted off the ground and turned in against a
            // chest it is a man being carried. The sleeping one beside it is the same shape
            // lying flatter. Both names are in the machine's own list.
            new[] { "amb@world_human_bum_slumped@male@laying_on_left_side@base", "base" },
            new[] { "mp_sleep", "sleep_loop" },

            new[] { "combat@drag_ped@", "injured_drag_ped" },     // the game's own dragged body
            new[] { "misslamar1dead_body", "dead_idle" },
            new[] { "dead", "dead_a" },
            new[] { "dead", "dead_b" },
            new[] { "dead", "dead_c" },
            new[] { "dead", "dead_d" },
            new[] { "dead", "dead_e" },
            new[] { "dead", "dead_f" },
            new[] { "dead", "dead_g" },
            new[] { "dead", "dead_h" },
        };

        /// <summary>What each shape is called on the readout.</summary>
        private static readonly string[] Named =
        {
            "as he died  --  no clip",
            "CURLED UP on his side",
            "asleep on his side",
            "dragged  --  the game's own",
            "dead idle",
            "dead A", "dead B", "dead C", "dead D",
            "dead E", "dead F", "dead G", "dead H",
        };

        /// <summary>The shape's name, for the log.</summary>
        public static string ShapeName => Named[Shape % Named.Length];

        /// <summary>Whether the chosen clip is actually running on him right now.</summary>
        public static bool Playing(Ped body)
        {
            try
            {
                if (body == null || !body.Exists() || !Posed) return false;

                var pair = Shapes[Shape % Shapes.Length];

                return Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, body.Handle, pair[0], pair[1], 3);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Whether a pose has been chosen on the tuner. Nothing in Drag reads this any more --
        /// the weld holds him whatever this says -- it only gates Shaped.
        /// </summary>
        public static bool Posed => Shape > 0;

        /// <summary>The offset as the attach wants it.</summary>
        public static Vector3 Offset => new Vector3(X, Y, Z);

        // ---- the tool ----------------------------------------------------------

        /// <summary>
        /// One pass, from Drag while a body is in hand. Draws the readout and reads the pad.
        ///
        /// The caller says whether anything moved, because a changed number is only a changed
        /// number until the body is taken hold of again -- see Drag.Hauling, which re-welds on
        /// a true.
        /// </summary>
        public static bool Update(Ped body, bool on)
        {
            if (!on) return false;

            Readout();

            var moved = Keys_();

            if (moved && body != null && body.Exists()) Shaped(body);

            return moved;
        }

        /// <summary>
        /// The numpad, which nothing else in this mod binds and which is nowhere near a hand
        /// that is walking backwards carrying a corpse.
        ///
        /// A CENTIMETRE AND FIVE DEGREES A PRESS, which is small enough to creep up on a
        /// position and big enough that creeping does not take all night. Holding a key repeats
        /// on the game's own repeat, which is exactly what is wanted here.
        /// </summary>
        private static bool Keys_()
        {
            var moved = false;

            if (Tap(Keys.NumPad4)) { X -= Step; moved = true; }
            if (Tap(Keys.NumPad6)) { X += Step; moved = true; }
            if (Tap(Keys.NumPad8)) { Y += Step; moved = true; }
            if (Tap(Keys.NumPad2)) { Y -= Step; moved = true; }
            if (Tap(Keys.NumPad7)) { Z -= Step; moved = true; }
            if (Tap(Keys.NumPad9)) { Z += Step; moved = true; }

            if (Tap(Keys.NumPad1)) { Yaw -= Turn; moved = true; }
            if (Tap(Keys.NumPad3)) { Yaw += Turn; moved = true; }
            if (Tap(Keys.Divide)) { Pitch -= Turn; moved = true; }
            if (Tap(Keys.Multiply)) { Pitch += Turn; moved = true; }
            if (Tap(Keys.Subtract)) { Roll -= Turn; moved = true; }
            if (Tap(Keys.Add)) { Roll += Turn; moved = true; }

            if (Tap(Keys.NumPad5))
            {
                Shape = (Shape + 1) % Shapes.Length;
                moved = true;
            }

            // ON END RATHER THAN ON THE PAD, because all sixteen pad keys are spoken for and
            // this is the one that is pressed twice a session rather than forty times.
            if (Tap(Keys.End))
            {
                Bone = (Bone + 1) % Bones.Length;
                moved = true;
            }

            if (Tap(Keys.Decimal))
            {
                X = Home[0]; Y = Home[1]; Z = Home[2];
                Pitch = Home[3]; Roll = Home[4]; Yaw = Home[5];
                Shape = HomeShape;
                Bone = HomeBone;
                moved = true;
            }

            // NOT A MOVE. Writing it down changes nothing about where he is, and re-welding
            // the body to celebrate would jolt the thing you just spent five minutes lining up.
            if (Tap(Keys.NumPad0)) Write();

            return moved;
        }

        private const float Step = 0.01f;
        private const float Turn = 5f;

        /// <summary>
        /// A key going down, once per press.
        ///
        /// SHVDN's own key state is a level rather than an edge -- IsKeyPressed answers yes for
        /// every frame the key is held -- so an edge has to be kept here or one tap of Numpad 6
        /// walks the body half a metre before the finger is off it. The same trap
        /// Main.SurveyKey is written around, and the same fix.
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

        /// <summary>Which body is in which shape, and when the clip was last given. See Shaped.</summary>
        private static string _played = "";
        private static int _shapedAt;

        /// <summary>How long a clip is given to start before not running counts as thrown off.</summary>
        private const int RetryMs = 250;

        /// <summary>
        /// The clip will be played again on the next Shaped, whatever body it was.
        ///
        /// THE ONCE-PER-BODY GUARD IS RIGHT FOR A HOLD LOOP AND WRONG AFTER A CLEAR. Drag.Hold
        /// clears the body's tasks on every full weld -- a re-pick, a slider nudge, a bone
        /// change -- which stops the pose, and Shaped then saw the same body in the same shape
        /// and declined to start it again. The log said it plainly: posed=True on the first
        /// grip of a session and posed=False on every one after. Whoever clears the tasks
        /// calls this first.
        /// </summary>
        public static void Forget()
        {
            _played = "";
        }

        private static readonly System.Collections.Generic.HashSet<Keys> _down =
            new System.Collections.Generic.HashSet<Keys>();

        /// <summary>
        /// Puts the body in whatever shape is selected, or hands it back to the physics.
        ///
        /// A POSED BODY MUST NOT BE ALLOWED TO RAGDOLL. The animation and the solver are two
        /// things trying to own the same skeleton, and the solver wins -- the pose plays for a
        /// frame and then he folds. So ragdolling is switched off for as long as he is posed
        /// and switched back on the moment he is not.
        /// </summary>
        public static void Shaped(Ped body)
        {
            if (body == null || !body.Exists()) return;

            try
            {
                if (!Posed) return;   // the weld holds him; see Drag.Hold.

                var pair = Shapes[Shape % Shapes.Length];

                // RAGDOLLING OFF FOR A POSE. A ped that can ragdoll will, and a ragdolling ped
                // owns its own transform -- which is fatal for a weld.
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, body.Handle, false);

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, pair[0]))
                {
                    // Asked for and not waited on. The next pass plays it.
                    Function.Call(Hash.REQUEST_ANIM_DICT, pair[0]);
                    return;
                }

                // ONCE PER BODY PER SHAPE, NOT ONCE A FRAME. Hauling calls this every pass,
                // and TASK_PLAY_ANIM re-issued sixty times a second restarts the clip sixty
                // times a second -- which on a body welded to a moving chest is a man vibrating
                // in your arms. The task persists on its own; it only has to be given once.
                //
                // ONCE, WHILE IT IS RUNNING. "Once" was the whole guard, and it was wrong on
                // about every third body: the log said posed=False from the first line of a
                // carry to the last, on a clip this had given exactly once and never looked at
                // again. He is brought back from the dead to take the clip, and a ped brought
                // back lying on the ground has the engine's own get-up waiting for him -- so
                // on the frame after the pose went on, the get-up took the task off him, and
                // what was welded to your chest was a man in his standing idle, straight as a
                // plank and plainly alive. Which is what was reported, twice.
                //
                // So it is asked whether the clip is actually running, and put back when it is
                // not: tasks cleared, because the thing that threw it off is still on him, and
                // not more than four times a second, because a clip takes a frame or two to
                // register and a re-issue inside that window is the vibration again.
                var tag = body.Handle + ":" + Shape;
                var now = Game.GameTime;

                if (tag == _played)
                {
                    if (Playing(body)) return;

                    if (now - _shapedAt < RetryMs) return;

                    Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, body.Handle);

                    Log.Debug("The dead pose was thrown off the body; put back.");
                }

                _played = tag;
                _shapedAt = now;

                // FLAG 2, HOLD THE LAST FRAME -- NOT 15, WHICH LOOPED. The dead poses are
                // death animations: they play from standing to collapsed and, looped, snap
                // back to standing at the top of every cycle -- which is the "plank ped when
                // moved" that a dead pose reset to. Played once and frozen on its last frame,
                // the collapsed one, a death pose is a dead body and stays one. The curled
                // idle looked right under the loop only because its clip is already a static
                // laying frame; it holds the same frame just as well.
                Function.Call(Hash.TASK_PLAY_ANIM, body.Handle, pair[0], pair[1],
                              8f, -8f, -1, 2, 0f, false, false, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not shape the body: " + ex.Message);
            }
        }

        // ---- saying where it is -------------------------------------------------

        /// <summary>
        /// The readout, top left, out of the way of everything this mod already draws.
        ///
        /// Plain TEXT natives rather than this mod's own HUD kit. It is a workshop tool -- it
        /// wants to be legible and to cost nothing, and it must not go anywhere near the shared
        /// rectangle budget the five mods on this machine are already fighting over. See
        /// UI.Draw.Budget and the "Draw list:" line in the log.
        /// </summary>
        private static void Readout()
        {
            Line(0.012f, 0.060f, "~y~BODY POSITION~s~   numpad", 0.34f);
            Line(0.012f, 0.085f, "4/6 across    " + X.ToString("0.00"), 0.30f);
            Line(0.012f, 0.105f, "8/2 out       " + Y.ToString("0.00"), 0.30f);
            Line(0.012f, 0.125f, "7/9 up        " + Z.ToString("0.00"), 0.30f);
            Line(0.012f, 0.150f, "1/3 turn      " + Yaw.ToString("0") + " deg", 0.30f);
            Line(0.012f, 0.170f, "//* tip       " + Pitch.ToString("0") + " deg", 0.30f);
            Line(0.012f, 0.190f, "-/+ roll      " + Roll.ToString("0") + " deg", 0.30f);
            Line(0.012f, 0.215f, "~b~5~s~ shape    " + Named[Shape % Named.Length], 0.30f);
            Line(0.012f, 0.235f, "~b~END~s~ pinned to  " + BoneNames[Bone % Bones.Length], 0.30f);
            Line(0.012f, 0.260f, "~g~0~s~ write it to the log    ~r~.~s~ back to the start", 0.28f);
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
        /// The answer, in the shape the ini wants it.
        ///
        /// Written as the actual lines rather than as a list of numbers, because the point of
        /// the tool is to stop somebody transcribing three floats at two in the morning.
        /// </summary>
        private static void Write()
        {
            var pair = Posed ? Shapes[Shape % Shapes.Length] : null;

            Log.Info("BODY POSITION -- paste into [Bodies] in BareMinimum.ini:");
            Log.Info("    Across=" + X.ToString("0.000"));
            Log.Info("    Forward=" + Y.ToString("0.000"));
            Log.Info("    Up=" + Z.ToString("0.000"));
            Log.Info("    Pitch=" + Pitch.ToString("0.#") + "   Roll=" + Roll.ToString("0.#") +
                     "   Yaw=" + Yaw.ToString("0.#"));
            Log.Info("    Pose=" + Shape + "     ; " + Named[Shape % Named.Length] +
                     (pair == null ? "" : "  -- " + pair[0] + " / " + pair[1]));
            Log.Info("    Bone=" + Bone + "     ; " + BoneNames[Bone % Bones.Length] +
                     "  -- bone id " + BoneId);

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
