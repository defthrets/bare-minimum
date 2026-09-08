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
    /// THE LOCK IS THE CONTROL, DISABLED EACH FRAME, which is all a sprint lock needs to be:
    /// jogging is what the game does when sprint is not pressed, and nothing has to be put
    /// back when the script goes because a disabled control lasts one frame.
    /// </summary>
    internal sealed class Energy
    {
        /// <summary>INPUT_SPRINT, by the game's number.</summary>
        private const int SprintControl = 21;

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

                if (Tired) Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, SprintControl, true);
            }
            catch (Exception ex)
            {
                Log.Once("vitals-energy", "The energy meter failed: " + ex.Message);
            }
        }
    }
}
