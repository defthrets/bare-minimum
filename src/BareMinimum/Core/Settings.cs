using System;
using System.Windows.Forms;

namespace BareMinimum.Core
{
    /// <summary>
    /// Everything the player can tune, loaded from BareMinimum.ini.
    ///
    /// Every value has a working default in this file, so a missing or half-written ini is
    /// never fatal -- the mod runs on the defaults and says which settings it could not find.
    /// That is not defensive padding: the ini is deliberately never overwritten by a deploy
    /// (it is the file players hand-edit), so an install that has been through an update is
    /// the NORMAL case for a file that lacks the newest keys.
    /// </summary>
    internal sealed class Settings
    {
        // ---- General ---------------------------------------------------------

        public bool Enabled = true;
        public bool AnnounceOnLoad = true;
        public LogLevel LogLevel = LogLevel.Info;

        // ---- Hunger ----------------------------------------------------------

        public bool HungerEnabled = true;

        /// <summary>
        /// GAME hours from completely full to completely empty.
        ///
        /// Game hours, not real ones, because that is the clock the player is living on: they
        /// sleep by it, shops open by it, and a need measured in real minutes drifts out of
        /// step with all of it the moment anybody changes the timescale. At GTA's default rate
        /// a game hour is two real minutes, so 28 is a bit under an hour of real play.
        /// </summary>
        public float HungerHoursToEmpty = 28f;

        /// <summary>Multiplier on the drain while sprinting, swimming or otherwise working.</summary>
        public float HungerExertionMultiplier = 2.2f;

        /// <summary>Multiplier on the drain while asleep. Low, but not nothing.</summary>
        public float HungerSleepMultiplier = 0.45f;

        // ---- Sleep -----------------------------------------------------------

        public bool SleepEnabled = true;

        /// <summary>
        /// GAME hours awake before the meter is empty.
        ///
        /// Forty-four is deliberately just under two game days, so "a day or two without
        /// sleep" lands where the effects do: the first slowdown arrives around twenty-eight
        /// hours awake, which is a bit over one day.
        /// </summary>
        public float SleepHoursToEmpty = 44f;

        // ---- Effects ---------------------------------------------------------

        /// <summary>Below this, hunger starts to slow the player down.</summary>
        public float HungerSlowAt = 0.34f;

        /// <summary>Below this, they walk like their stomach hurts.</summary>
        public float HungerHurtAt = 0.15f;

        /// <summary>The slowest hunger alone will make them walk, as a fraction of normal.</summary>
        public float HungerMinMoveRate = 0.72f;

        /// <summary>
        /// The clipset for the starving walk.
        ///
        /// move_m@injured is the game's own hunched, side-clutching limp -- it is what a
        /// wounded ped uses, and it reads exactly as "something is wrong with my middle",
        /// which is the note asked for. A clipset must be STREAMED before it is applied; see
        /// Effects, where a set that has not loaded yet is simply not used this frame rather
        /// than applied and silently ignored.
        /// </summary>
        public string HungerClipset = "move_m@injured";

        /// <summary>Below this, missed sleep starts to show.</summary>
        public float SleepTiredAt = 0.35f;

        /// <summary>Below this, they are properly out of it: sway, blur and a drunk walk.</summary>
        public float SleepDrunkAt = 0.18f;

        public float SleepMinMoveRate = 0.85f;

        /// <summary>
        /// The clipset for the exhausted walk.
        ///
        /// The MODERATE drunk set rather than verydrunk: the brief was a SLIGHT drunk effect,
        /// and verydrunk is a stagger that makes doorways impossible. Moderate is a loose,
        /// unsteady walk that still gets you where you are going.
        /// </summary>
        public string SleepClipset = "move_m@drunk@moderatedrunk";

        /// <summary>How hard the camera sways when exhausted. 0 turns it off entirely.</summary>
        public float SleepCameraShake = 0.14f;

        /// <summary>
        /// How much faster you move on foot when BOTH needs are full. 1.0 turns it off.
        ///
        /// Deliberately tiny. The move-rate override scales the animations as well as the
        /// speed, so a big number does not read as "well fed", it reads as the game running
        /// fast -- the legs visibly cycle quicker than the ground moves. Five per cent is felt
        /// rather than seen, which is the right register for a reward you get for housekeeping.
        ///
        /// It also has to stay small because it stacks with nothing: there is no penalty in
        /// force when this applies, so whatever is here is the whole effect.
        /// </summary>
        public float WellFedBonus = 1.05f;

        /// <summary>
        /// Both needs must be above this before the bonus starts coming in.
        ///
        /// It RAMPS from here to full rather than switching on, for the same reason the
        /// penalties ramp: a step change at a number the player cannot see reads as the game
        /// stuttering, not as a state they have earned.
        /// </summary>
        public float WellFedAbove = 0.85f;

        /// <summary>Whether an empty stomach slowly costs health.</summary>
        public bool StarvingCostsHealth = true;

        /// <summary>Health points per game hour, once hunger is at zero.</summary>
        public float StarvingHealthPerHour = 4f;

        // ---- Sleeping --------------------------------------------------------

        public bool SleepInBeds = true;
        public bool SleepInCars = true;

        /// <summary>Game hours a proper bed advances the clock by.</summary>
        public float BedHours = 8f;

        /// <summary>Game hours dozing in a car advances the clock by.</summary>
        public float CarHours = 4f;

        /// <summary>
        /// How much of the sleep meter a car gives back, against a bed's whole.
        ///
        /// A car is a worse night's sleep, and this is the only place that says so -- the
        /// hours alone would make it merely a shorter one.
        /// </summary>
        public float CarRestoreFraction = 0.55f;

        /// <summary>How close to a bed you have to stand before it offers.</summary>
        public float BedReach = 1.6f;

        // ---- Money -----------------------------------------------------------

        /// <summary>Scales every price in foods.json at once, for anybody who finds them wrong.</summary>
        public float PriceMultiplier = 1f;

        // ---- HUD -------------------------------------------------------------

        public bool ShowHud = true;

        /// <summary>
        /// Place the icons beside the minimap automatically, whatever the screen shape is.
        ///
        /// THIS CANNOT BE A FIXED FRACTION, which is why the setting exists. GTA anchors the
        /// minimap to screen HEIGHT, so it is a constant number of PIXELS wide on any monitor
        /// of the same height -- and therefore a SHRINKING fraction of the width as the screen
        /// gets wider. Its right edge is at 0.157 of the width on 16:9 and about 0.117 on a
        /// 21:9 ultrawide. A default written for one is visibly wrong on the other: either
        /// floating out in the middle of the screen, or sitting on top of the map.
        /// </summary>
        public bool HudAutoPosition = true;

        /// <summary>Used only when HudAutoPosition is false. Fractions of the screen.</summary>
        public float HudX = 0.163f;
        public float HudY = 0.836f;

        /// <summary>Icon height as a fraction of screen height. Width follows; the PNGs are square.</summary>
        public float HudSize = 0.032f;

        /// <summary>Clear air between the two icons, as a fraction of the icon's own height.</summary>
        public float HudGap = 0.22f;

        public float HudOpacity = 0.92f;

        /// <summary>
        /// Hide an icon entirely while that need is comfortable.
        ///
        /// Off by default. A gauge that appears only once there is a problem cannot be learnt
        /// -- the first time it shows up is the first time it is ever seen, which is the worst
        /// possible moment to be working out what it means.
        /// </summary>
        public bool HudHideWhenFine = false;

        /// <summary>Above this, an icon counts as comfortable for the setting above.</summary>
        public float HudFineAbove = 0.60f;

        /// <summary>Pulse an icon once its need is critical.</summary>
        public bool HudFlashWhenCritical = true;

        // ---- Keys ------------------------------------------------------------

        /// <summary>
        /// The one key this mod uses, for the counter, the bed and the car alike.
        ///
        /// E, and PROXIMITY-GATED. That overlaps Fumes' nozzle key and the HKH suite's
        /// interact, which the hotkey audit lists as an acceptable class of overlap because
        /// each only fires within reach of its own thing. The one place it could genuinely
        /// collide is a petrol station, where Fumes wants the pump and this wants the till --
        /// and those are metres apart with much smaller reaches than the gap between them.
        /// </summary>
        public Keys InteractKey = Keys.E;

        /// <summary>
        /// Opens the settings menu. F7.
        ///
        /// Verified free on this machine's Enhanced install before it was chosen: the only hit
        /// for "F7" anywhere under scripts\ was a KEY-CODE REFERENCE TABLE inside
        /// SafeCracker.ini (its actual binding is StartKey=85, U), and no dll carries "F7" as
        /// a string in either ASCII or UTF-16. In use nearby: F2 Hoodrich, F3 Overspray,
        /// F8 Dealien Roleplay Menu, F10 and F11 PullMeOverRemade.
        /// </summary>
        public Keys MenuKey = Keys.F7;

        // ======================================================================

        /// <summary>
        /// Reads the ini, or returns defaults if it is not there.
        ///
        /// Never throws. A settings loader that can throw takes the script down before the
        /// log exists, which is the one failure nobody can diagnose.
        /// </summary>
        public static Settings Load()
        {
            var cfg = new Settings();

            try
            {
                var ini = IniFile.Load(Paths.Ini);

                if (ini == null)
                {
                    Log.Warn("No " + Paths.Stem + ".ini beside the dll - running on defaults.");
                    return cfg;
                }

                cfg.Enabled = ini.GetBool("General", "Enabled", cfg.Enabled);
                cfg.AnnounceOnLoad = ini.GetBool("General", "AnnounceOnLoad", cfg.AnnounceOnLoad);
                cfg.LogLevel = ParseLevel(ini.GetString("General", "LogLevel", "Info"), cfg.LogLevel);

                cfg.HungerEnabled = ini.GetBool("Hunger", "Enabled", cfg.HungerEnabled);
                cfg.HungerHoursToEmpty = ini.GetFloat("Hunger", "HoursToEmpty",
                                                      cfg.HungerHoursToEmpty, 0.5f, 500f);
                cfg.HungerExertionMultiplier = ini.GetFloat("Hunger", "ExertionMultiplier",
                                                            cfg.HungerExertionMultiplier, 1f, 10f);
                cfg.HungerSleepMultiplier = ini.GetFloat("Hunger", "SleepMultiplier",
                                                         cfg.HungerSleepMultiplier, 0f, 4f);

                cfg.SleepEnabled = ini.GetBool("Sleep", "Enabled", cfg.SleepEnabled);
                cfg.SleepHoursToEmpty = ini.GetFloat("Sleep", "HoursToEmpty",
                                                     cfg.SleepHoursToEmpty, 0.5f, 500f);

                cfg.HungerSlowAt = ini.GetFloat("Effects", "HungerSlowAt", cfg.HungerSlowAt, 0f, 1f);
                cfg.HungerHurtAt = ini.GetFloat("Effects", "HungerHurtAt", cfg.HungerHurtAt, 0f, 1f);
                cfg.HungerMinMoveRate = ini.GetFloat("Effects", "HungerMinMoveRate",
                                                     cfg.HungerMinMoveRate, 0.3f, 1f);
                cfg.HungerClipset = ini.GetString("Effects", "HungerClipset", cfg.HungerClipset);

                cfg.SleepTiredAt = ini.GetFloat("Effects", "SleepTiredAt", cfg.SleepTiredAt, 0f, 1f);
                cfg.SleepDrunkAt = ini.GetFloat("Effects", "SleepDrunkAt", cfg.SleepDrunkAt, 0f, 1f);
                cfg.SleepMinMoveRate = ini.GetFloat("Effects", "SleepMinMoveRate",
                                                    cfg.SleepMinMoveRate, 0.3f, 1f);
                cfg.SleepClipset = ini.GetString("Effects", "SleepClipset", cfg.SleepClipset);
                cfg.SleepCameraShake = ini.GetFloat("Effects", "SleepCameraShake",
                                                    cfg.SleepCameraShake, 0f, 1f);

                cfg.WellFedBonus = ini.GetFloat("Effects", "WellFedBonus",
                                                cfg.WellFedBonus, 1f, 1.5f);
                cfg.WellFedAbove = ini.GetFloat("Effects", "WellFedAbove",
                                                cfg.WellFedAbove, 0.1f, 1f);

                cfg.StarvingCostsHealth = ini.GetBool("Effects", "StarvingCostsHealth",
                                                      cfg.StarvingCostsHealth);
                cfg.StarvingHealthPerHour = ini.GetFloat("Effects", "StarvingHealthPerHour",
                                                         cfg.StarvingHealthPerHour, 0f, 100f);

                cfg.SleepInBeds = ini.GetBool("Sleeping", "InBeds", cfg.SleepInBeds);
                cfg.SleepInCars = ini.GetBool("Sleeping", "InCars", cfg.SleepInCars);
                cfg.BedHours = ini.GetFloat("Sleeping", "BedHours", cfg.BedHours, 1f, 24f);
                cfg.CarHours = ini.GetFloat("Sleeping", "CarHours", cfg.CarHours, 1f, 24f);
                cfg.CarRestoreFraction = ini.GetFloat("Sleeping", "CarRestoreFraction",
                                                      cfg.CarRestoreFraction, 0.05f, 1f);
                cfg.BedReach = ini.GetFloat("Sleeping", "BedReach", cfg.BedReach, 0.5f, 6f);

                cfg.PriceMultiplier = ini.GetFloat("Money", "PriceMultiplier",
                                                   cfg.PriceMultiplier, 0f, 50f);

                cfg.ShowHud = ini.GetBool("HUD", "Show", cfg.ShowHud);
                cfg.HudAutoPosition = ini.GetBool("HUD", "AutoPosition", cfg.HudAutoPosition);
                cfg.HudX = ini.GetFloat("HUD", "X", cfg.HudX, -0.2f, 1.2f);
                cfg.HudY = ini.GetFloat("HUD", "Y", cfg.HudY, -0.2f, 1.2f);
                cfg.HudSize = ini.GetFloat("HUD", "Size", cfg.HudSize, 0.005f, 0.30f);
                cfg.HudGap = ini.GetFloat("HUD", "Gap", cfg.HudGap, 0f, 3f);
                cfg.HudOpacity = ini.GetFloat("HUD", "Opacity", cfg.HudOpacity, 0.05f, 1f);
                cfg.HudHideWhenFine = ini.GetBool("HUD", "HideWhenFine", cfg.HudHideWhenFine);
                cfg.HudFineAbove = ini.GetFloat("HUD", "FineAbove", cfg.HudFineAbove, 0f, 1f);
                cfg.HudFlashWhenCritical = ini.GetBool("HUD", "FlashWhenCritical",
                                                       cfg.HudFlashWhenCritical);

                cfg.InteractKey = ini.GetKey("Keys", "Interact", cfg.InteractKey);
                cfg.MenuKey = ini.GetKey("Keys", "Menu", cfg.MenuKey);

                Log.Level = cfg.LogLevel;
            }
            catch (Exception ex)
            {
                Log.Error("Could not read " + Paths.Ini + " - running on defaults.", ex);
            }

            cfg.Validate();
            return cfg;
        }

        /// <summary>
        /// Catches the settings that are individually legal and jointly nonsense.
        ///
        /// Each range check above can only see one value. A player who sets HungerHurtAt above
        /// HungerSlowAt has written two numbers that are both inside 0..1 and which together
        /// mean the crippled walk starts BEFORE the slowdown -- so the slowdown never happens
        /// and the mod looks broken rather than misconfigured.
        /// </summary>
        private void Validate()
        {
            if (HungerHurtAt > HungerSlowAt)
            {
                Log.Warn("[Effects] HungerHurtAt (" + HungerHurtAt.ToString("0.00") +
                         ") is above HungerSlowAt (" + HungerSlowAt.ToString("0.00") +
                         "). Swapping them - the crippled walk has to start lower than the slowdown.");

                var t = HungerHurtAt;
                HungerHurtAt = HungerSlowAt;
                HungerSlowAt = t;
            }

            if (SleepDrunkAt > SleepTiredAt)
            {
                Log.Warn("[Effects] SleepDrunkAt (" + SleepDrunkAt.ToString("0.00") +
                         ") is above SleepTiredAt (" + SleepTiredAt.ToString("0.00") +
                         "). Swapping them.");

                var t = SleepDrunkAt;
                SleepDrunkAt = SleepTiredAt;
                SleepTiredAt = t;
            }
        }

        private static LogLevel ParseLevel(string text, LogLevel fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;

            switch (text.Trim().ToLowerInvariant())
            {
                case "error": return Core.LogLevel.Error;
                case "warn":
                case "warning": return Core.LogLevel.Warn;
                case "info": return Core.LogLevel.Info;
                case "debug": return Core.LogLevel.Debug;
                default: return fallback;
            }
        }
    }
}
