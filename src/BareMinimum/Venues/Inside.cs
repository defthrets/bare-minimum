using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

using BareMinimum.Core;

using Hud = BareMinimum.UI.Draw;

namespace BareMinimum.Venues
{
    /// <summary>
    /// One of the game's own rooms that a bar's door can put you inside.
    /// </summary>
    ///
    /// <remarks>
    /// AN INTERIOR CANNOT BE PLACED. It is baked into the map at one coordinate, and the only
    /// thing a door can do is warp you across the city to wherever the room actually lives.
    /// From inside that is indistinguishable, because there are no windows -- which is how
    /// every mod that gives a facade an inside does it, including the ninety-three-door one
    /// that asked this question.
    ///
    /// THE COORDINATE IS A GUESS UNTIL SOMEBODY HAS STOOD IN IT, and the code is built around
    /// that. The anchor and its alternatives are asked of the game with GET_INTERIOR_AT_COORDS,
    /// which answers for free about any point on the loaded map, and the first one with a room
    /// in it wins. After the warp the game is asked again whether he is actually in a room;
    /// if he is not, he comes straight back out to the door and is told why.
    /// </remarks>
    internal sealed class Room
    {
        public string Key = "";
        public string Name = "";

        /// <summary>IPLs to request, semicolons between. Empty for a room already in the story map.</summary>
        public string Ipl = "";

        /// <summary>
        /// Whether the room only exists on the ONLINE map.
        ///
        /// Neither bar this ships with needs it, and the switch is here anyway because it is
        /// six lines and the next room somebody wants -- a nightclub, the casino bar -- will.
        /// Put on when he goes in and back when he comes out, never left on: the online map
        /// changes buildings across the whole city and takes story-mode blips with it.
        /// </summary>
        public bool Online;

        public Vector3 Anchor;
        public float Heading;

        /// <summary>Other places the room might be, tried in order when the anchor has nothing.</summary>
        public readonly List<Vector3> Also = new List<Vector3>();
    }

    /// <summary>
    /// The way into a bar, and the way back out.
    /// </summary>
    ///
    /// <remarks>
    /// THE MECHANISM IS POSTED UP'S, MOVED IN. Its InteriorDoor has had every one of these
    /// failures already -- the flat wait that was too short on the run that mattered, the ped
    /// that fell through the sky because he was unfrozen over a room that had not streamed,
    /// the leave prompt twenty metres away through a wall because it was measured from the
    /// ini's coordinate rather than from where he actually landed, the man left in the desert
    /// after a reload forgot he was inside -- and each is written into that file with its
    /// fix. Everything below is that fix, not a second attempt at the same lesson.
    ///
    /// NO MAP SWAP for the two rooms this ships with. Tequi-la-la and Bahama Mamas are story
    /// interiors that have been in the map the whole time with no way in; they need a door and
    /// nothing else. The linked mod's vanishing-blip reports are what a GLOBAL online-map
    /// switch does to a story game, and this never makes one.
    ///
    /// ONE MARK INSIDE, NOT THREE. Where he lands is where he orders and where he leaves from:
    /// press to open the bar's shelf, hold to go back out the door. Two more coordinates per
    /// room -- a counter and an exit -- would be two more numbers somebody has to stand on to
    /// get right, for rooms this mod cannot see. The hold is the same idiom the bed uses.
    /// </remarks>
    internal sealed class Inside
    {
        /// <summary>How close to the mark before it offers the bar and the way out.</summary>
        private const float MarkRange = 2.4f;

        /// <summary>How long the key is held to leave. A press is an order.</summary>
        private const int LeaveHoldMs = 1100;

        /// <summary>Long enough for the fade, short enough not to feel like a loading screen.</summary>
        private const int FadeMs = 650;

        /// <summary>A ceiling on how long a room gets to arrive, and how often it is asked.</summary>
        private const int StreamCeilingMs = 8000;
        private const int StreamStepMs = 100;

        /// <summary>How far above the guess the floor is looked for, and how far off it is believed.</summary>
        private const float FloorProbeUp = 3f;
        private const float FloorProbeBand = 4f;

        /// <summary>
        /// How far a room's own origin may be from the guess before it is a different room.
        ///
        /// GET_INTERIOR_AT_COORDS answers for an interior the point is merely NEAR, so an
        /// origin a hundred metres off means the guess is at the wrong building, not the wrong
        /// corner of the right one.
        /// </summary>
        private const float OriginTrust = 60f;

        /// <summary>A doorway reads as no interior for a step or two. Not acted on inside this.</summary>
        private const float DoorwaySlack = 8f;

        /// <summary>Past this, something else moved him -- hospital, a cell -- and he is let go.</summary>
        private const float GoneFar = 500f;

        /// <summary>Long enough after the warp for the room to have decided he is in it.</summary>
        private const int SettleGraceMs = 2500;

        /// <summary>How often the room is asked whether he is standing in it uninvited.</summary>
        private const int RecoverEveryMs = 2000;

        private const float RingRange = 30f;

        private readonly Settings _cfg;
        private readonly Vendors _vendors;

        private Vendor _in;
        private Room _room;

        /// <summary>Where he actually landed. Everything inside is measured from here.</summary>
        private Vector3 _mark;

        /// <summary>The pavement he stepped in from, and the way he was facing.</summary>
        private Vector3 _cameFrom;
        private float _cameFacing;

        private int _enteredAt;
        private bool _busy;

        private bool _wasDown;
        private int _heldSince;
        private int _nextRecover;

        /// <summary>The file that remembers which bar he is in across a reload.</summary>
        private static string Sidecar => Path.Combine(Paths.Writable, "inside.txt");

        public Inside(Settings cfg, Vendors vendors)
        {
            _cfg = cfg;
            _vendors = vendors;
        }

        /// <summary>True while he is in a bar's room.</summary>
        public bool IsInside => _in != null;

        /// <summary>True while a prompt of ours is on screen, so nothing else reads the key.</summary>
        public bool Offering { get; private set; }

        // ======================================================================

        public void Update(bool suspended)
        {
            Offering = false;

            if (_busy) return;

            Ped me;
            try
            {
                me = Game.Player.Character;
                if (me == null || !me.Exists()) return;
            }
            catch { return; }

            try
            {
                // Dead men are not in bars. The hospital he wakes up at is nowhere near the
                // room, and a state that says otherwise would try to put him back in it.
                if (!me.IsAlive) { if (_in != null) Forget("he died"); return; }

                if (_in == null)
                {
                    Recover(me);
                    return;
                }

                if (WanderedOut(me)) return;

                Ring(me);

                if (suspended) { _wasDown = false; _heldSince = 0; return; }

                AtTheBar(me);
            }
            catch (Exception ex)
            {
                Log.Once("inside", "The bar room failed: " + ex.Message);
            }
        }

        /// <summary>
        /// The one mark: order on a press, leave on a hold.
        ///
        /// ACTED ON AT RELEASE for the order, so a hold does not open the shelf on its first
        /// frame and then leave with it open. The chip fills while the key is down, the same
        /// way the bed's does, so a hold is visibly a hold.
        /// </summary>
        private void AtTheBar(Ped me)
        {
            var now = Game.GameTime;

            if (me.Position.DistanceTo(_mark) > MarkRange)
            {
                _wasDown = false;
                _heldSince = 0;
                return;
            }

            Offering = true;

            var down = Down();

            if (down && !_wasDown) _heldSince = now;

            if (down && _heldSince != 0)
            {
                var held = now - _heldSince;

                UI.Hint.Show("Leaving", Pad.Cap(_cfg.InteractKey), Math.Min(1f, held / (float)LeaveHoldMs));

                if (held >= LeaveHoldMs)
                {
                    _heldSince = 0;
                    _wasDown = false;
                    Leave(me);
                    return;
                }
            }
            else
            {
                Hud.Help("Press ~INPUT_CONTEXT~ to order at the bar.  Hold it to leave.");
            }

            // Let go before the hold matured: that is an order.
            if (!down && _wasDown && _heldSince != 0 && !UI.Menu.Quiet)
            {
                _heldSince = 0;
                Hud.ClearHelp();
                _vendors.OpenShelfInside(_in, _mark);
            }

            if (!down) _heldSince = 0;
            _wasDown = down;
        }

        /// <summary>
        /// Whether the interact is down, by key or by pad, enabled or not.
        ///
        /// BOTH VARIANTS OF THE CONTROL. The counter pass holds Context disabled near a till so
        /// the game's own shop never hears it, and a room with a till prop in it would
        /// otherwise go deaf. A level rather than an edge, because a hold is a level.
        /// </summary>
        private bool Down()
        {
            try
            {
                if (Game.IsKeyPressed(_cfg.InteractKey)) return true;

                return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context) ||
                       Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.Context);
            }
            catch { return false; }
        }

        // ======================================================================
        // In
        // ======================================================================

        /// <summary>
        /// In. Fade, find the room, warp, wait for it, check, fade back.
        ///
        /// Called by Vendors when the key is pressed at the door of a bar that has a room --
        /// so the door is the vendor's own coordinate, and the prompt outside is the vendor's
        /// own prompt with different words on it.
        /// </summary>
        public void Enter(Vendor v, Room room)
        {
            if (v == null || room == null || _busy) return;

            Ped me;
            try { me = Game.Player.Character; } catch { return; }
            if (me == null || !me.Exists() || !me.IsAlive) return;

            _busy = true;

            var frozen = false;

            try
            {
                // Read off him rather than off the file: he is within reach of the door or the
                // prompt would not have been up, so this IS the doorway.
                _cameFrom = me.Position;
                _cameFacing = me.Heading;

                Fade(false);

                if (room.Online) Mp(true);

                foreach (var raw in room.Ipl.Split(';'))
                {
                    var one = raw.Trim();
                    if (one.Length == 0) continue;

                    Function.Call(Hash.REQUEST_IPL, one);
                    Log.Info("Asked for ipl '" + one + "' for " + room.Name + ".");
                }

                var to = Somewhere(room);

                // Asked for BEFORE the warp, so the streamer has the whole fade to work in.
                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, to.X, to.Y, to.Z);
                Function.Call(Hash.NEW_LOAD_SCENE_START_SPHERE, to.X, to.Y, to.Z, 40f, 0);

                // FROZEN ACROSS THE WARP. An unfrozen ped over a room that has not streamed
                // starts falling on the next frame and is under the floor by the time it
                // arrives. That is the falling through the sky, and this is the whole fix.
                Function.Call(Hash.FREEZE_ENTITY_POSITION, me.Handle, true);
                frozen = true;

                me.Position = to;
                me.Heading = room.Heading;

                var interior = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, to.X, to.Y, to.Z);

                if (interior != 0)
                {
                    Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, interior);
                    Function.Call(Hash.SET_INTERIOR_ACTIVE, interior, true);
                }

                // WAITED ON, NOT WAITED OUT. Is there collision round him, is the interior
                // ready, is the scene loaded, and is HE in a room -- not "is there a room at
                // the coordinate", which stays true while he stands just outside its wall.
                var waited = 0;
                var inRoom = 0;

                while (waited < StreamCeilingMs)
                {
                    Script.Wait(StreamStepMs);
                    waited += StreamStepMs;

                    if (interior == 0)
                    {
                        interior = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, to.X, to.Y, to.Z);
                        if (interior != 0)
                        {
                            Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, interior);
                            Function.Call(Hash.SET_INTERIOR_ACTIVE, interior, true);
                        }
                    }

                    if (room.Ipl.Trim().Length > 0 && waited % 1000 == 0 &&
                        !Function.Call<bool>(Hash.IS_IPL_ACTIVE, room.Ipl.Split(';')[0].Trim()))
                    {
                        // A request is not a load; a dropped one looks like a slow one.
                        Function.Call(Hash.REQUEST_IPL, room.Ipl.Split(';')[0].Trim());
                    }

                    var solid = Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, me.Handle);
                    var ready = interior == 0 || Function.Call<bool>(Hash.IS_INTERIOR_READY, interior);
                    var scene = Function.Call<bool>(Hash.IS_NEW_LOAD_SCENE_LOADED);

                    inRoom = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me.Handle);

                    if (solid && ready && scene && inRoom != 0) break;
                }

                Function.Call(Hash.NEW_LOAD_SCENE_STOP);

                // THE ROOM'S OWN MIDDLE, only if the guess left him outside it. A coordinate
                // can find a room and still be on the wrong side of one of its walls; the
                // interior knows its origin, and offset zero from that is somewhere to stand.
                if (interior != 0 && inRoom == 0)
                {
                    var origin = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_INTERIOR_IN_WORLD_COORDS,
                                                        interior, 0f, 0f, 0f);

                    if (origin != Vector3.Zero && origin.DistanceTo(to) < OriginTrust)
                    {
                        Log.Info(room.Name + " is really at " + origin + ", not " + to +
                                 " -- standing him at the room's own origin.");
                        to = origin;
                        me.Position = to;
                        Script.Wait(300);
                        inRoom = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me.Handle);
                    }
                }

                // The floor the GAME reports, not the height somebody typed.
                var floorZ = 0f;
                var floorFound = Ground(to, out floorZ);

                if (floorFound && Math.Abs(floorZ - to.Z) <= FloorProbeBand)
                {
                    me.Position = new Vector3(to.X, to.Y, floorZ + 0.05f);
                }

                Log.Info("Into " + room.Name + " for " + v.Name + ": interior=" + interior +
                         ", he is in interior=" + inRoom + ", floor=" + (floorFound ? floorZ.ToString("0.00") : "none") +
                         ", waited " + waited + "ms.");

                // Nothing under him and not in a room is open air. Back out the door he came
                // in by, and say which of the two numbers is wrong.
                if (interior == 0 || (!floorFound && inRoom == 0))
                {
                    Log.Warn(room.Name + " is not at " + to + " (interior=" + interior + ", in=" +
                             inRoom + "). Check that room's anchor in vendors.json.");

                    me.Position = _cameFrom;
                    me.Heading = _cameFacing;

                    Function.Call(Hash.FREEZE_ENTITY_POSITION, me.Handle, false);
                    frozen = false;

                    if (room.Online) Mp(false);

                    Script.Wait(400);
                    Fade(true);

                    Compat.Ticker("~r~" + room.Name + " isn't loading.~s~ Its anchor in vendors.json is off.");
                    return;
                }

                _in = v;
                _room = room;
                _mark = me.Position;
                _enteredAt = Game.GameTime;
                _wasDown = true;               // the press that opened the door is still down
                _heldSince = 0;

                Remember(v.Id);

                Function.Call(Hash.FREEZE_ENTITY_POSITION, me.Handle, false);
                frozen = false;

                Script.Wait(200);
                Fade(true);
            }
            catch (Exception ex)
            {
                Log.Error("Could not go into " + room.Name, ex);

                // UNFROZEN FIRST. Anything thrown after the freeze would otherwise leave him
                // unable to move for the rest of the save.
                try { if (frozen) Function.Call(Hash.FREEZE_ENTITY_POSITION, me.Handle, false); }
                catch { /* nothing else to try */ }

                try { me.Position = _cameFrom; } catch { /* nothing else to try */ }
                if (room.Online) Mp(false);
                Fade(true);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// Which of the room's coordinates it is actually at.
        ///
        /// Asked of the game, for free, before anybody is moved. The anchor goes first so a
        /// coordinate somebody has stood on always wins over the fallbacks; when none has
        /// anything, the anchor is handed back and the checks after the warp say so.
        /// </summary>
        private static Vector3 Somewhere(Room room)
        {
            try
            {
                if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS,
                                       room.Anchor.X, room.Anchor.Y, room.Anchor.Z) != 0)
                {
                    return room.Anchor;
                }

                foreach (var maybe in room.Also)
                {
                    if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, maybe.X, maybe.Y, maybe.Z) == 0) continue;

                    Log.Info(room.Name + " is not at its anchor " + room.Anchor + " -- using " + maybe +
                             ", which has a room. Make that the anchor to keep it.");
                    return maybe;
                }
            }
            catch
            {
                // The anchor, and the checks after the warp will judge it.
            }

            return room.Anchor;
        }

        // ======================================================================
        // Out
        // ======================================================================

        /// <summary>
        /// Out, to the doorway he came in by.
        ///
        /// Not to the coordinate in the file. They are the same place in the ordinary case,
        /// and the file is what a reload falls back on -- but putting him back exactly where he
        /// was standing is the only version of this that cannot come out somewhere else.
        /// </summary>
        private void Leave(Ped me)
        {
            if (_in == null) return;

            _busy = true;

            var v = _in;
            var room = _room;

            try
            {
                _vendors.CloseShelf();
                Hud.ClearHelp();

                Fade(false);

                var back = Back(v);

                me.Position = back;
                me.Heading = _cameFrom != Vector3.Zero && _cameFrom.DistanceTo(v.Position) <= DoorwaySlack
                    ? _cameFacing
                    : v.Heading;

                if (room != null && room.Online) Mp(false);

                Log.Info("Out of " + (room == null ? "the room" : room.Name) + " to " + back +
                         (_cameFrom == Vector3.Zero ? " (the door -- nothing remembered the way in)."
                                                    : " (the way he came in)."));

                Script.Wait(400);
                Fade(true);
            }
            catch (Exception ex)
            {
                Log.Error("Could not leave " + (room == null ? "the room" : room.Name), ex);
                Fade(true);
            }
            finally
            {
                v.Counter = Vector3.Zero;
                _in = null;
                _room = null;
                _mark = Vector3.Zero;
                Forget(null);
                _busy = false;
            }
        }

        /// <summary>
        /// Where leaving puts him: the doorway he used, or the vendor's door after a reload.
        ///
        /// THE DOOR WINS WHEN THE TWO DISAGREE. What is remembered was read off him as he
        /// stepped in, so a reload or a mission can leave a coordinate from somewhere else
        /// entirely -- and coming out into the middle of Vinewood is worse than coming out a
        /// stride from the door.
        /// </summary>
        private Vector3 Back(Vendor v)
        {
            if (_cameFrom == Vector3.Zero) return v.Position;
            return _cameFrom.DistanceTo(v.Position) > DoorwaySlack ? v.Position : _cameFrom;
        }

        /// <summary>
        /// Whether he got out of the room some way other than by holding the key.
        ///
        /// A story interior has real doors and they open onto a real street -- Tequi-la-la's
        /// onto Vinewood Boulevard, not onto Mirror Park. Walking out of them is leaving, so he
        /// goes back to the bar he came in through. Far enough away that something else must
        /// have moved him -- Pillbox, a cell -- he is let go instead of dragged back.
        /// </summary>
        private bool WanderedOut(Ped me)
        {
            if (_enteredAt != 0 && Game.GameTime - _enteredAt < SettleGraceMs) return false;

            int room;
            try { room = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me.Handle); }
            catch { return false; }

            if (room != 0) return false;

            var away = me.Position.DistanceTo(_mark);

            if (away < DoorwaySlack) return false;

            if (away > GoneFar)
            {
                Forget((int)away + "m from the room; something else moved him");
                return true;
            }

            Log.Info("Walked out of " + _room.Name + " by its own door; back to " + _in.Name + ".");
            Leave(me);
            return true;
        }

        // ======================================================================
        // Across a reload
        // ======================================================================

        /// <summary>
        /// Realises he is standing in one of our rooms even though nothing here put him there.
        ///
        /// Being inside is a bool in memory, and a script reload and a loaded save both throw
        /// memory away. The room is asked instead: if the game says he is in an interior that
        /// one of our rooms is at, he is inside, and the way out works again. Which BAR he came
        /// through is what the sidecar file is for -- two bars can share a room, and putting
        /// him out at the wrong one is a free trip across the map.
        /// </summary>
        private void Recover(Ped me)
        {
            var now = Game.GameTime;
            if (now < _nextRecover) return;
            _nextRecover = now + RecoverEveryMs;

            int room;
            try { room = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me.Handle); }
            catch { return; }

            if (room == 0) return;

            foreach (var pair in _vendors.Rooms)
            {
                var r = pair.Value;
                if (me.Position.DistanceTo(r.Anchor) > OriginTrust) continue;

                int mine;
                try { mine = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, r.Anchor.X, r.Anchor.Y, r.Anchor.Z); }
                catch { continue; }

                if (mine == 0 || mine != room) continue;

                var v = Remembered(r.Key);
                if (v == null) return;

                _in = v;
                _room = r;
                _mark = me.Position;
                _enteredAt = now;
                _cameFrom = Vector3.Zero;      // the doorway went with the memory; the door is the way out

                Log.Info("Found him already in " + r.Name + "; the way out is " + v.Name + "'s door.");
                return;
            }
        }

        /// <summary>The bar he went in through, from the sidecar; else the first bar using that room.</summary>
        private Vendor Remembered(string roomKey)
        {
            string id = null;

            try
            {
                if (File.Exists(Sidecar)) id = File.ReadAllText(Sidecar).Trim();
            }
            catch { /* then the first bar with this room */ }

            Vendor first = null;

            foreach (var v in _vendors.All)
            {
                if (v.RoomKey != roomKey) continue;
                if (first == null) first = v;
                if (id != null && v.Id == id) return v;
            }

            return first;
        }

        private static void Remember(string vendorId)
        {
            try { File.WriteAllText(Sidecar, vendorId ?? ""); }
            catch { /* a reload will fall back to the first bar with that room */ }
        }

        private void Forget(string why)
        {
            if (why != null && _in != null)
            {
                Log.Info("No longer in " + (_room == null ? "the room" : _room.Name) + ": " + why + ".");
            }

            if (_in != null) _in.Counter = Vector3.Zero;

            _in = null;
            _room = null;
            _mark = Vector3.Zero;

            try { if (File.Exists(Sidecar)) File.Delete(Sidecar); }
            catch { /* stale, and harmless: Recover checks the room before trusting it */ }
        }

        /// <summary>On a reload, nothing to undo -- the sidecar is what carries him across it.</summary>
        public void Shutdown()
        {
            try
            {
                var me = Game.Player.Character;
                if (me != null && me.Exists()) Function.Call(Hash.FREEZE_ENTITY_POSITION, me.Handle, false);
            }
            catch { /* teardown */ }

            if (_room != null && _room.Online) Mp(false);
        }

        // ======================================================================

        /// <summary>A ring on the floor at the mark, so the way out is findable from across the room.</summary>
        private void Ring(Ped me)
        {
            if (me.Position.DistanceTo(_mark) > RingRange) return;

            try
            {
                var c = UI.Palette.Brand;

                Function.Call(Hash.DRAW_MARKER, 1, _mark.X, _mark.Y, _mark.Z - 1.0f,
                              0f, 0f, 0f, 0f, 0f, 0f,
                              1.1f, 1.1f, 0.30f,
                              (int)c.R, (int)c.G, (int)c.B, 105,
                              false, false, 2, false, 0, 0, false);
            }
            catch
            {
                // A room without its ring is still a room.
            }
        }

        /// <summary>The ground under a point, through the native -- World.GetGroundHeight's mode enum is not in 3.6.0.</summary>
        private static bool Ground(Vector3 at, out float z)
        {
            z = at.Z;

            try
            {
                var arg = new OutputArgument();
                var ok = Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD,
                                             at.X, at.Y, at.Z + FloorProbeUp, arg, false);
                if (ok) z = arg.GetResult<float>();
                return ok;
            }
            catch { return false; }
        }

        private static void Mp(bool on)
        {
            try
            {
                Function.Call(on ? Hash.ON_ENTER_MP : Hash.ON_ENTER_SP);
                Log.Info("Map switched to the " + (on ? "online" : "story") + " one.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not switch the map: " + ex.Message);
            }
        }

        private static void Fade(bool inwards)
        {
            try
            {
                if (inwards)
                {
                    Function.Call(Hash.DO_SCREEN_FADE_IN, FadeMs);
                }
                else
                {
                    Function.Call(Hash.DO_SCREEN_FADE_OUT, FadeMs);
                    Script.Wait(FadeMs);
                }
            }
            catch
            {
                // A cut instead of a fade.
            }
        }
    }
}
