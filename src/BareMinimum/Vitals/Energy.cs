using System;
using GTA;
using GTA.Native;
using BareMinimum.Core;

namespace BareMinimum.Vitals
{
    /// <summary>
    /// The sprint meter: the third bar, doubling for the special ability.
    ///
    /// THE GAME'S OWN STAMINA IS INVISIBLE AND NEARLY INFINITE. There is a stat, it takes
    /// minutes to run down, and when it does the game hurts you rather than slowing you. This
    /// is the version a HUD can show: a bar that drains while you sprint, at a rate you can
    /// see, and when it is empty you JOG -- the sprint button does nothing -- until it has come
    /// back to where you have your breath again. It refills on its own, fairly quickly, faster
    /// stood still than jogging, and always in a car.
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
    /// out of the script, because unlike a disabled control it would otherwise stay.
    /// </summary>
    internal sealed class Energy
    {
        /// <summary>Move blend ratios: 2 is a run, 3 a sprint.</summary>
        private const float RunCap = 2f;
        private const float SprintCap = 3f;

        /// <summary>The ped the cap was last put on, so it comes off the right one.</summary>
        private int _cappedPed;

        /// <summary>0 to 1. Starts full.</summary>
        public float Level = 1f;

        /// <summary>Whether the sprint is locked off until the level has come back.</summary>
        public bool Tired;

        /// <summary>This frame's events, for the bar to slosh on.</summary>
        public bool JustEmptied;
        public bool JustRecovered;

        public void Update(Settings cfg, float dt)
        {
            JustEmptied = false;
            JustRecovered = false;

            if (!cfg.VitalsEnergy)
            {
                Level = 1f;
                Tired = false;
                Release();
                return;
            }

            if (dt < 0f) dt = 0f;
            if (dt > 0.1f) dt = 0.1f;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var onFoot = !me.IsInVehicle();
                var sprinting = onFoot && me.IsSprinting && !Tired;
                var jogging = onFoot && !sprinting && me.IsRunning;

                if (sprinting)
                {
                    Level -= dt / Math.Max(1f, cfg.EnergySprintSeconds);
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
                    Function.Call(Hash.SET_PED_MAX_MOVE_BLEND_RATIO, me.Handle, RunCap);
                    _cappedPed = me.Handle;
                }
                else if (_cappedPed != 0)
                {
                    Release();
                }
            }
            catch (Exception ex)
            {
                Log.Once("vitals-energy", "The energy meter failed: " + ex.Message);
            }
        }

        /// <summary>Lifts the cap, if one is on. Safe to call with none; called on the way out too.</summary>
        public void Release()
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
    }
}
