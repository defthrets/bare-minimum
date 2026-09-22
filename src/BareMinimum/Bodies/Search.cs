using System;
using GTA;
using GTA.Native;
using BareMinimum.Core;
using BareMinimum.UI;

namespace BareMinimum.Bodies
{
    /// <summary>
    /// Going through somebody's pockets.
    ///
    /// THE GAME'S OWN ANSWER IS A PICKUP AND THIS TURNS IT OFF. A dead man with a rifle drops
    /// a rifle on the pavement and you collect it by walking over it without looking down --
    /// which is the whole transaction, and it is worth nothing. Every ped near you is told not
    /// to drop what it is carrying (see Nobody), so the only way a gun changes hands is you
    /// kneeling down and taking it.
    ///
    /// AND HE KNEELS DOWN. The screen does not open over a man stood upright with his hands by
    /// his sides: he goes down on one knee first, and stays there for as long as the pockets
    /// are open.
    ///
    /// ONE KEY, HELD, which is the second thing that changed coming across from Posted Up.
    /// That mod used two -- the context key to OFFER it and a second to actually do it --
    /// because standing over a body and pressing the button you press for everything else is
    /// how you loot a corpse on the way past. This mod has one interact key and every prompt
    /// in it is a hold: the fridge, the room door, the bed, the stall. A hold is already the
    /// thing that cannot be done by accident, so the second key would be a rule this mod does
    /// not have anywhere else.
    /// </summary>
    internal sealed class Search
    {
        /// <summary>How close you have to be to a body to be offered it.</summary>
        private const float Reach = 2.2f;

        /// <summary>And how far you can get before it shuts on its own.</summary>
        private const float Leave = 3.4f;

        /// <summary>How often the ground around you is looked at.</summary>
        private const int ScanMs = 400;

        /// <summary>How far the sweep reaches when it tells people not to drop things.</summary>
        private const float SweepRange = 90f;
        private const int SweepEveryMs = 2000;

        /// <summary>How long the key is held before he kneels. Long enough that a stray press is not a search.</summary>
        private const int HoldMs = 600;

        /// <summary>How long he is knelt over the body before the pockets come up.</summary>
        private const int KneelMs = 1100;

        private const string KneelDict = "amb@medic@standing@kneel@base";
        private const string KneelClip = "base";

        /// <summary>Looping, held until it is stopped. See TASK_PLAY_ANIM's flags.</summary>
        private const int LoopingAnim = 1;

        private readonly Core.Settings _cfg;
        private readonly Corpses _bodies;
        private readonly LootScreen _screen = new LootScreen();

        /// <summary>Set by Main: off while something louder owns the screen or the key.</summary>
        public Func<bool> Busy;

        /// <summary>
        /// Set by Main: the carry, so "and now move him" can be on the card's footer.
        ///
        /// THIS IS THE SEAM THAT USED TO BE A REFLECTION BRIDGE. The loot card is the last
        /// thing you look at before you want the body gone, and the footer of that card is the
        /// honest place to say so rather than closing it and hunting for a second prompt. It
        /// was two mods and three classes to arrange that; it is a field now.
        /// </summary>
        public Drag Carry
        {
            get { return _screen.Carry; }
            set { _screen.Carry = value; }
        }

        private Ped _near;
        private Ped _at;

        private int _nextScan;
        private int _sweptAt;

        /// <summary>When the hold started, and when he knelt.</summary>
        private int _holdSince;
        private int _kneltAt;

        private bool _kneeling;

        /// <summary>Whether this owns the screen and the buttons right now.</summary>
        public bool IsOpen => _screen.IsOpen || _kneltAt != 0;

        public Search(Core.Settings cfg, Corpses bodies)
        {
            _cfg = cfg;
            _bodies = bodies;

            _screen.Done = Stand;
        }

        public void Update()
        {
            var now = Game.GameTime;

            if (_cfg == null || !_cfg.LootBodies)
            {
                // SWITCHED OFF MID-SESSION IS STILL AN ENDING. He does not stay on one knee
                // over a body because a box was unticked while he was down there.
                if (IsOpen) RestoreWorld();
                return;
            }

            var player = Game.Player.Character;

            if (_bodies != null) _bodies.Sweep(now);

            Nobody(player, now);

            if (_screen.IsOpen)
            {
                // HE STAYS DOWN WHILE THE POCKETS ARE OPEN -- AND NOT A FRAME LONGER.
                //
                // THE GUARD IS THE SEARCH RATHER THAN THE PANEL, and that matters: in the mod
                // this came from, IsOpen was true while the panel was LEAVING as well as while
                // it was up, and Close had already run Done, which is Stand, which stopped the
                // animation. So the frame after standing him up, this knelt him again, with a
                // looping task and no duration, and nothing was ever going to stop it a second
                // time. He searched a body once and spent the rest of the session on one knee.
                if (_kneltAt != 0) Kneel(player);

                if (_at == null || !_at.Exists() ||
                    (player != null && player.Exists() && player.Position.DistanceTo(_at.Position) > Leave))
                {
                    _screen.Close();
                    return;
                }

                _screen.Update();
                return;
            }

            // Knelt down, and the pockets have not come up yet.
            if (_kneltAt != 0)
            {
                Kneel(player);

                if (now - _kneltAt < KneelMs) return;

                Open(player);
                return;
            }

            if (player == null || !player.Exists() || !player.IsAlive) { _near = null; return; }
            if (player.IsInVehicle()) { _near = null; _holdSince = 0; return; }

            if (Busy != null && Busy()) { _near = null; _holdSince = 0; return; }

            if (now >= _nextScan)
            {
                _nextScan = now + ScanMs;
                Scan(player);
            }

            if (_near == null || !_near.Exists())
            {
                _holdSince = 0;
                return;
            }

            Offer(now);
        }

        /// <summary>
        /// The offer, and then the hold.
        ///
        /// SHOWN WHENEVER YOU ARE STOOD OVER ONE, which is how every other prompt in this mod
        /// behaves -- Hint fades itself out the moment nothing is asking for it, so "call it
        /// every frame you mean it" is the whole contract. The mod this came from had to build
        /// a greeting that introduced itself once and then hid, because its prompt had no
        /// fade of its own and a permanent line would have become furniture.
        /// </summary>
        private void Offer(int now)
        {
            var cap = Pad.Cap(_cfg.InteractKey);

            if (!Down())
            {
                _holdSince = 0;
                Hint.Show("Search the body", cap, -1f);
                return;
            }

            if (_holdSince == 0) _holdSince = now;

            var held = (now - _holdSince) / (float)HoldMs;

            Hint.Show("Search the body", cap, held);

            if (held < 1f) return;

            _holdSince = 0;
            Begin(now);
        }

        /// <summary>Down on one knee, and the pockets a beat later.</summary>
        private void Begin(int now)
        {
            _at = _near;
            _kneltAt = now;
            _kneeling = false;

            Kneel(Game.Player.Character);

            Sound("SELECT");
        }

        /// <summary>
        /// Puts him on one knee and keeps him there.
        ///
        /// Asked for every tick rather than once, because a looping animation can be knocked
        /// off by anything the game decides is more important -- and a screen that says he is
        /// knelt over a body while he stands there is worse than no animation at all.
        /// </summary>
        private void Kneel(Ped player)
        {
            if (player == null || !player.Exists()) return;

            try
            {
                if (!_kneeling)
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, KneelDict);

                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, KneelDict)) return;

                    _kneeling = true;
                }

                if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, player.Handle,
                                        KneelDict, KneelClip, 3)) return;

                Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, KneelDict, KneelClip,
                              4f, -2f, -1, LoopingAnim, 0f, false, false, false);
            }
            catch
            {
                // He searches it stood up, then.
            }
        }

        /// <summary>Back on his feet.</summary>
        private void Stand()
        {
            var player = Game.Player.Character;

            try
            {
                if (player != null && player.Exists())
                {
                    Function.Call(Hash.STOP_ANIM_TASK, player.Handle, KneelDict, KneelClip, -4f);
                }
            }
            catch
            {
                // He gets up on his own.
            }

            // AND THE TASK ITSELF, not only the clip. STOP_ANIM_TASK asks the ped to stop
            // playing one named animation, which is right when that is all he is doing and not
            // enough when the engine has him in a task that will start it again -- a scripted
            // animation with no duration IS a task, and the ped keeps the task after the clip
            // stops. The same belt-and-braces the carry needs on the way out, and for the same
            // reason. See Drag.Put.
            try
            {
                if (player != null && player.Exists())
                {
                    // CLEAR_PED_SECONDARY_TASK by its own name rather than the wrapper: SHVDN's
                    // ClearAnimation is marked obsolete in 3.9 and the replacement it names is
                    // not in the 3.6 this builds against, so the native is the one thing that
                    // is true in both.
                    Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, player.Handle);
                }
            }
            catch
            {
                // Both attempts have now been made; there is nothing else to try.
            }

            _kneeling = false;
            _kneltAt = 0;
            _at = null;
            _near = null;
            _holdSince = 0;
            _nextScan = Game.GameTime + 600;
        }

        private void Open(Ped player)
        {
            if (_bodies == null || _at == null || !_at.Exists()) { Stand(); return; }

            var body = _bodies.For(_at);

            if (body == null) { Stand(); return; }

            Log.Info("Search: " + body.Name + ", " + body.Affiliation + ", " +
                     body.Items.Count + " thing(s) on " + body.Him + ".");

            // AND ON THE BOOKS AS OPENED, which is a narrower fact than being known about and
            // is the one the carry is waiting on. See Corpses.Opened and Drag.Waiting.
            _bodies.Open(_at);

            _screen.Open(_bodies, body, _at);
        }

        /// <summary>
        /// The nearest body worth kneeling over.
        ///
        /// EMPTIED ONES ARE SKIPPED, which is what stops a searched body offering itself for
        /// the rest of the night. One you left something on still asks.
        /// </summary>
        private void Scan(Ped player)
        {
            _near = null;

            try
            {
                var closest = Reach;

                foreach (var ped in World.GetNearbyPeds(player, Reach + 1.5f))
                {
                    if (ped == null || !ped.Exists() || ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;

                    // ---- PEOPLE ONLY ----
                    //
                    // A DEAD CAT WAS COMING UP AS KEON PERALTA, 5'5", HISPANIC, WITH NINETY
                    // EIGHT DOLLARS ON HIM. Every animal in Los Santos is a ped as far as the
                    // game is concerned -- cats, coyotes, the chickens -- so a corpse scan that
                    // asks for peds gets them, and then the card fills itself in from the
                    // generators, because the card has never had a reason to doubt it was
                    // looking at a person.
                    if (!ped.IsHuman) continue;

                    if (_bodies != null && _bodies.Done(ped)) continue;

                    var gap = player.Position.DistanceTo(ped.Position);
                    if (gap > closest) continue;

                    closest = gap;
                    _near = ped;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Search: could not look at the ground: " + ex.Message);
            }
        }

        /// <summary>
        /// Nobody drops anything any more.
        ///
        /// SET BEFORE THEY DIE, WHICH IS WHY IT IS A SWEEP. The flag decides what happens at
        /// the moment of death, so setting it on a corpse is too late -- the gun is already on
        /// the pavement. Everybody within ninety metres is told, every couple of seconds,
        /// which is one native call each on a few dozen people and is not worth optimising.
        /// </summary>
        private void Nobody(Ped player, int now)
        {
            if (player == null || !player.Exists()) return;
            if (now - _sweptAt < SweepEveryMs) return;

            _sweptAt = now;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, SweepRange))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;

                    Function.Call(Hash.SET_PED_DROPS_WEAPONS_WHEN_DEAD, ped.Handle, false);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Search: could not stop the drops: " + ex.Message);
            }
        }

        /// <summary>
        /// The interact key held down, as a level.
        ///
        /// THE BOUND KEY AND THE GAME'S OWN CONTEXT BUTTON, which is what every other world
        /// prompt in this mod reads. The disabled reading is there because a screen elsewhere
        /// in the mod may have suppressed the control this frame without meaning to suppress
        /// this.
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

        private static void Sound(string name)
        {
            try
            {
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, name,
                              "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
            }
            catch
            {
                // A menu without a click is still a menu.
            }
        }

        /// <summary>Teardown, and a reload. He is not left on one knee with a panel up.</summary>
        public void RestoreWorld()
        {
            try
            {
                if (_screen.IsOpen) _screen.Close();
                else Stand();
            }
            catch
            {
                // Teardown.
            }
        }
    }
}
