using System;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// The sprint meter: the third bar, and the special ability's tank.
    ///
    /// THE GAME'S OWN STAMINA IS INVISIBLE AND NEARLY INFINITE. There is a stat, it takes
    /// minutes to run down, and when it does the game hurts you rather than slowing you. This
    /// is the version a HUD can show: a bar that drains while you sprint, at a rate you can
    /// see, and when it is empty you JOG -- the sprint button does nothing -- until it has come
    /// back to where you have your breath again. It refills on its own, fairly quickly, faster
    /// stood still than jogging, and always in a car.
    ///
    /// AND THE SPECIAL ABILITY RUNS ON THE SAME TANK. Rage, focus, the slow motion -- whichever
    /// of the three he is -- burns this bar while it is on, at its own rate, and when the bar
    /// is out the ability stops with it. The game's own meter is not consulted: it is kept
    /// topped up so its clock never ends the ability and never refuses to start one, and THIS
    /// bar is the limit. Which is what makes the ability cost something you can see, and what
    /// makes burning it leave you winded. See Ability. [Vitals] EnergyPowersSpecial turns it off
    /// and hands the ability back to the game.
    ///
    /// LOCKED AT EMPTY, FREED PART WAY UP. Freeing it the moment it left zero would let a
    /// player tap-sprint along the bottom of the bar forever, a stutter rather than a limit.
    /// So the sprint stays off until the level is back to EnergySprintAgainAt, and the bar's
    /// colour says which state it is in -- dimmed and beating slowly while it is locked.
    ///
    /// WINDED IS A JOG, LIKE THE GAME'S OWN. The first cut disabled the sprint button, and
    /// that read as the wrong thing -- a button that does nothing -- and on some layouts left
    /// him walking. Now the ped's top movement speed is CAPPED at a run while he is winded,
    /// through SET_PED_MAX_MOVE_BLEND_RATIO, the native missions use to hold you at a walk or
    /// a jog: press sprint and you jog, tired, the way the game itself does when its own
    /// stamina runs out. The cap is lifted the moment he has his breath back, and on the way
    /// out of the script, because unlike a disabled control it would otherwise stay. The same
    /// goes for the ability being switched off while winded -- see Release, which lifts both.
    /// </summary>
    internal sealed class Energy
    {
        /// <summary>Move blend ratios: 2 is a run, 3 a sprint.</summary>
        private const float RunCap = 2f;
        private const float SprintCap = 3f;

        /// <summary>The ped the cap was last put on, so it comes off the right one.</summary>
        private int _cappedPed;

        /// <summary>Whether the special ability is switched off at the game's end right now.</summary>
        private bool _abilityOff;

        /// <summary>0 to 1. Starts full.</summary>
        public float Level = 1f;

        /// <summary>Whether the sprint is locked off until the level has come back.</summary>
        public bool Tired;

        /// <summary>Whether the special ability is running and drinking from this bar this frame.</summary>
        public bool Spending;

        /// <summary>This frame's events, for the bar to slosh on.</summary>
        public bool JustEmptied;
        public bool JustRecovered;

        /// <summary>Game time, in ms, until which something is holding the meter full. See Hold.</summary>
        private int _holdUntil;

        /// <summary>Game time, in ms, until which the thing holding it was a stimulant.</summary>
        private int _wiredUntil;

        /// <summary>Whether something is holding the meter full right now.</summary>
        public bool Held => Game.GameTime < _holdUntil;

        /// <summary>
        /// Whether what is holding it is a stimulant. The bar shimmers while this is true --
        /// see Paint.Third -- because a coffee and a gram of meth should not look the same.
        /// </summary>
        public bool Wired => Game.GameTime < _wiredUntil;

        /// <summary>
        /// Holds the meter full for a while. A later one extends, never shortens.
        ///
        /// Game time rather than the wall clock, so a pause does not eat it.
        /// </summary>
        public void Hold(float seconds, bool wired = false)
        {
            if (seconds <= 0f) return;

            var until = Game.GameTime + (int)(seconds * 1000f);

            if (until > _holdUntil) _holdUntil = until;
            if (wired && until > _wiredUntil) _wiredUntil = until;
        }

        public void Update(Settings cfg, float dt)
        {
            JustEmptied = false;
            JustRecovered = false;
            Spending = false;

            if (!cfg.VitalsEnergy)
            {
                Level = 1f;
                Tired = false;
                Release();
                return;
            }

            if (dt < 0f) dt = 0f;
            if (dt > 0.1f) dt = 0.1f;

            // HELD FULL. Anything he drank that was not alcohol keeps the meter at the top for
            // a while, sprinting or not, and lifts the winded cap if it was on -- with the same
            // kick a recovery gets, so the bar says so. So does a stimulant, for longer and
            // with a shimmer on it: see Wired, and Dope's table for how long each one rides.
            // Which drinks and which drugs count is Main's decision, where the item and the
            // meter both exist. The ability still runs off the bar while this holds; the bar
            // simply does not go down, so a man who is up is a man who cannot run out.
            if (Game.GameTime < _holdUntil)
            {
                Level = 1f;

                if (Tired)
                {
                    Tired = false;
                    JustRecovered = true;
                }

                ReleaseCap();
                Ability(cfg);
                return;
            }

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var onFoot = !me.IsInVehicle();
                var sprinting = onFoot && me.IsSprinting && !Tired;
                var jogging = onFoot && !sprinting && me.IsRunning;

                Spending = cfg.EnergyPowersSpecial && Running();

                // BOTH DRAIN, AND THEY ADD UP. Sprinting through a rage costs what the sprint
                // costs and what the rage costs, which is the honest answer and the one the bar
                // can show. Nothing rebuilds while either is going.
                var drain = 0f;

                if (Spending) drain += dt / Math.Max(1f, cfg.EnergySpecialSeconds);
                if (sprinting) drain += dt / Math.Max(1f, cfg.EnergySprintSeconds);

                if (drain > 0f)
                {
                    Level -= drain;
                }
                else
                {
                    // Half rate at a jog: he is still working. Full rate at a walk, stood still,
                    // or in a seat.
                    Level += dt / Math.Max(1f, cfg.EnergyRebuildSeconds) * (jogging ? 0.5f : 1f);
                }

                if (Level <= 0f)
                {
                    Level = 0f;

                    if (!Tired)
                    {
                        Tired = true;
                        JustEmptied = true;
                    }
                }

                if (Level > 1f) Level = 1f;

                if (Tired && Level >= Ink.Clamp(cfg.EnergySprintAgainAt, 0.05f, 1f))
                {
                    Tired = false;
                    JustRecovered = true;
                }

                // Every frame while winded, because a respawn or a switch hands us a new ped
                // with the game's own cap on it; once, and on the right ped, when it lifts.
                if (Tired)
                {
                    // THE OLD BODY FIRST. This used to overwrite the handle, so going winded as
                    // one character and switching to another left the first one capped with
                    // nothing that knew about him -- he could not sprint again until he happened
                    // to go winded and recover on that body a second time.
                    if (_cappedPed != 0 && _cappedPed != me.Handle) ReleaseCap();

                    Function.Call(Hash.SET_PED_MAX_MOVE_BLEND_RATIO, me.Handle, RunCap);
                    _cappedPed = me.Handle;
                }
                else if (_cappedPed != 0)
                {
                    ReleaseCap();
                }

                Ability(cfg);
            }
            catch (Exception ex)
            {
                Log.Once("vitals-energy", "The energy meter failed: " + ex.Message);
            }
        }

        /// <summary>Whether the game says the special ability is running.</summary>
        private static bool Running()
        {
            try { return Function.Call<bool>(Hash.IS_SPECIAL_ABILITY_ACTIVE, Game.Player.Handle, 0); }
            catch { return false; }
        }

        /// <summary>
        /// The special ability, run off this bar.
        ///
        /// THE GAME'S METER IS KEPT FULL, not read. There is no native that says how full it is
        /// -- see Readings.SpecialMeter, which had to guess -- and its own clock would end the
        /// ability on a schedule this bar knows nothing about. So it is topped up whenever it is
        /// not full, which does two things at once: the ability never ends on the game's terms,
        /// and it can always be started while this bar has something in it.
        ///
        /// WINDED, THERE IS NONE. The ability is switched off at the game's end while the bar is
        /// locked, so the button does nothing and a running ability is cut off where the bar ran
        /// out. Switched back on the moment he has his breath back -- and by Release, because a
        /// disabled ability would otherwise outlive the script.
        /// </summary>
        private void Ability(Settings cfg)
        {
            if (!cfg.EnergyPowersSpecial)
            {
                ReleaseAbility();
                return;
            }

            try
            {
                var player = Game.Player.Handle;

                if (Tired)
                {
                    if (!_abilityOff)
                    {
                        Function.Call(Hash.ENABLE_SPECIAL_ABILITY, player, false, 0);
                        _abilityOff = true;
                    }

                    Function.Call(Hash.SPECIAL_ABILITY_DEACTIVATE, player, 0);
                    return;
                }

                if (_abilityOff)
                {
                    Function.Call(Hash.ENABLE_SPECIAL_ABILITY, player, true, 0);
                    _abilityOff = false;
                }

                if (!Function.Call<bool>(Hash.IS_SPECIAL_ABILITY_METER_FULL, player, 0))
                {
                    Function.Call(Hash.SPECIAL_ABILITY_FILL_METER, player, true, 0);
                }
            }
            catch (Exception ex)
            {
                Log.Once("vitals-special", "The special ability could not be run off the energy bar: " + ex.Message);
            }
        }

        /// <summary>Everything this class does to the player, undone. Safe to call with nothing on; called on the way out too.</summary>
        public void Release()
        {
            ReleaseCap();
            ReleaseAbility();
        }

        /// <summary>Lifts the movement cap, if one is on.</summary>
        private void ReleaseCap()
        {
            if (_cappedPed == 0) return;

            try
            {
                var ped = Entity.FromHandle(_cappedPed) as Ped;
                if (ped != null && ped.Exists()) Function.Call(Hash.SET_PED_MAX_MOVE_BLEND_RATIO, ped.Handle, SprintCap);
            }
            catch
            {
                // A ped that is gone has no cap to lift.
            }

            _cappedPed = 0;
        }

        /// <summary>Switches the special ability back on, if this class switched it off.</summary>
        private void ReleaseAbility()
        {
            if (!_abilityOff) return;

            try { Function.Call(Hash.ENABLE_SPECIAL_ABILITY, Game.Player.Handle, true, 0); }
            catch
            {
                // Nothing more to try.
            }

            _abilityOff = false;
        }
    }
}
