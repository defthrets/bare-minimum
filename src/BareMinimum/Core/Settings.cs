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

        /// <summary>
        /// GAME hours of sleep that take you from empty to fully rested.
        ///
        /// SEPARATE FROM HoursToEmpty, and much smaller, because recovery is not the same rate
        /// as decline. You drain over forty-four waking hours; you do not need forty-four hours
        /// in bed to undo it. Sharing one number meant a six-hour night gave back six
        /// forty-fourths -- about a seventh -- and a full night barely moved the meter, which
        /// read as sleeping being broken rather than as sleeping being slow.
        ///
        /// Twelve, so two normal nights take somebody from nothing to full and one six-hour
        /// night is worth half a meter.
        /// </summary>
        public float SleepHoursToFull = 12f;

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

        // ---- Drink -----------------------------------------------------------

        public bool BoozeEnabled = true;

        /// <summary>
        /// GAME hours to sober up from completely hammered.
        ///
        /// Eight, so a heavy night is still with you in the morning unless you sleep it off
        /// -- and sleeping advances the clock, which is exactly how it should clear.
        /// </summary>
        public float BoozeHoursToSober = 8f;

        /// <summary>Above this you start to show it.</summary>
        public float BoozeDrunkAt = 0.22f;

        /// <summary>Above this it stops being merry and starts being a stagger.</summary>
        public float BoozeHeavyAt = 0.65f;

        /// <summary>
        /// How much faster tiredness comes on when completely drunk.
        ///
        /// THE WHOLE POINT OF THE MECHANIC. Drink does not simply look like being tired; it
        /// MAKES you tired, and quickly. Scaled by how drunk you are, so one beer barely
        /// registers and a night of them costs you most of a day's rest.
        /// </summary>
        public float BoozeSleepMultiplier = 2.4f;

        public float BoozeCameraShake = 0.28f;

        /// <summary>Merry, and properly gone. Two clipsets so it escalates.</summary>
        public string BoozeClipset = "move_m@drunk@moderatedrunk";
        public string BoozeClipsetHeavy = "move_m@drunk@verydrunk";

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

        /// <summary>
        /// Credit sleep when SOMETHING ELSE puts the player to bed.
        ///
        /// Posted Up and Hoodrich both have their own sleep at Franklin's, and there is no
        /// way for this mod to hook either of them -- they share no API and this one is not
        /// going to grow a dependency on somebody else's dll.
        ///
        /// What it CAN see is the game clock. A jump of several hours that this mod did not
        /// cause is, near enough always, somebody sleeping: another mod's bed, or a mission
        /// that skips a night. Reading the clock instead of the mod means it works for all of
        /// them, including ones that do not exist yet.
        /// </summary>
        public bool CreditOutsideSleep = true;

        /// <summary>
        /// The window a jump has to fall in to count as sleep, in game hours.
        ///
        /// Bounded at BOTH ends on purpose. Below the floor it is a mission cutscene nudging
        /// the clock along, not a night; above the ceiling it is a fast travel or a trainer,
        /// and crediting a full meter for those would make the whole sleep half of the mod
        /// free to anybody with a time-of-day slider.
        /// </summary>
        public float OutsideSleepMinHours = 2f;
        public float OutsideSleepMaxHours = 14f;

        /// <summary>
        /// How good a night somebody else's bed counts as, against our own.
        ///
        /// Full marks. We have no idea whether they put you in a bed or a chair, and being
        /// stingy about a night this mod did not stage would just read as the rest not working.
        /// </summary>
        public float OutsideSleepQuality = 1f;

        /// <summary>
        /// Game scripts to terminate while stood at one of our counters.
        ///
        /// ob_cashregister IS THE VANILLA STORE MENU -- the "P's & Q's $1 / EgoChaser $2"
        /// list with Shoplift and Select on it. Confirmed by name off a live counter rather
        /// than guessed: Shop.NameTheScripts logs every running script the first time a
        /// counter is used, and that list had shop_controller and ShopRobberies on it too.
        ///
        /// WHICH IS EXACTLY WHY GUESSING WAS NOT ON. shop_controller is the obvious suspect
        /// and it also runs Ammu-Nation, the clothing shops and the barbers -- terminating it
        /// would have cost three shops to fix one menu. ShopRobberies is how you rob a store,
        /// which is a whole game mechanic. ob_ is the prefix for a script attached to ONE
        /// object, so this ends the register's own interaction and nothing else.
        ///
        /// Anything else that turns up can go here without a rebuild.
        /// </summary>
        public string[] SuppressScripts = { "ob_cashregister" };

        // ---- Social ----------------------------------------------------------

        /// <summary>
        /// Whether the shops post on Hoodrich's timeline.
        ///
        /// ON, but it costs nothing when Hoodrich is not installed: without its data folder
        /// there is nowhere to post, the socials file is never even read, and one line in the
        /// log says so. Here so that somebody who has both mods and wants the feed to stay
        /// about the block can have that.
        /// </summary>
        public bool SocialEnabled = true;

        /// <summary>Percent chance a given occasion actually produces a post.</summary>
        public int SocialChance = 22;

        /// <summary>
        /// Shortest gap between two posts from this mod, in real seconds.
        ///
        /// Long on purpose. Hoodrich's feed is somebody else's system and it is already busy;
        /// a hot dog stand chattering over the top of a gang war is this mod being a bad guest.
        /// </summary>
        public int SocialGapSeconds = 240;

        // ---- Map -------------------------------------------------------------

        /// <summary>Whether shops get map markers at all.</summary>
        public bool ShowShopBlips = true;

        /// <summary>
        /// How close before a shop's marker exists, in metres. 0 means always.
        ///
        /// A marker is CREATED and DESTROYED by distance rather than merely hidden, because
        /// twenty-five permanent blips is twenty-five icons competing with the ones the game
        /// put there -- and the point of a food marker is "there is one near you", not a
        /// permanent directory of every taco in the county.
        /// </summary>
        public float ShopBlipRange = 220f;

        /// <summary>
        /// Whether they also appear on the PAUSE map. Off.
        ///
        /// The pause map is where somebody goes to plan a journey, and a screen full of
        /// identical shop icons is exactly the clutter that makes people turn a mod off. On
        /// the minimap, near you, it is useful; on the big map it is noise.
        /// </summary>
        public bool ShopBlipsOnMainMap = false;

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

        /// <summary>
        /// Whether the two HUD icons move.
        ///
        /// TWO THINGS, AND THEY ARE NOT THE SAME SORT OF THING. A shimmer runs the whole
        /// time and is meant to sit under the threshold of notice -- it exists so the icons
        /// look lit rather than printed, not to tell anybody anything. A sway is added on the
        /// LAST stage only, and that one IS meant to be caught: by then the silhouette has
        /// run out of room to get worse, so movement is the only channel left.
        ///
        /// A switch because motion near the minimap is exactly the sort of thing that some
        /// people cannot stand and some people cannot see past.
        /// </summary>
        public bool HudAnimate = true;

        /// <summary>
        /// How strong the constant shimmer is. 0 switches it off, 1 is very obvious.
        ///
        /// A DIAL RATHER THAN A CONSTANT because "subtle" is not a number anybody can guess
        /// right first time. The first attempt at this was five per cent of brightness over
        /// four seconds and was, correctly, reported as invisible; whatever is chosen here
        /// will be wrong for somebody's monitor too, so it is in the ini.
        /// </summary>
        public float HudShimmer = 0.55f;

        /// <summary>
        /// How strong the low-meter sway is. 0 switches it off, 1 is a wobble.
        ///
        /// Only ever applied on the bottom stages, where the picture has run out of room to
        /// get any worse and movement is the only channel left.
        /// </summary>
        public float HudSway = 0.6f;

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
                cfg.SleepHoursToFull = ini.GetFloat("Sleep", "HoursToFull",
                                                    cfg.SleepHoursToFull, 0.5f, 200f);

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

                cfg.BoozeEnabled = ini.GetBool("Drink", "Enabled", cfg.BoozeEnabled);
                cfg.BoozeHoursToSober = ini.GetFloat("Drink", "HoursToSober",
                                                     cfg.BoozeHoursToSober, 0.5f, 100f);
                cfg.BoozeDrunkAt = ini.GetFloat("Drink", "DrunkAt", cfg.BoozeDrunkAt, 0f, 1f);
                cfg.BoozeHeavyAt = ini.GetFloat("Drink", "HeavyAt", cfg.BoozeHeavyAt, 0f, 1f);
                cfg.BoozeSleepMultiplier = ini.GetFloat("Drink", "SleepMultiplier",
                                                        cfg.BoozeSleepMultiplier, 1f, 10f);
                cfg.BoozeCameraShake = ini.GetFloat("Drink", "CameraShake",
                                                    cfg.BoozeCameraShake, 0f, 1f);
                cfg.BoozeClipset = ini.GetString("Drink", "Clipset", cfg.BoozeClipset);
                cfg.BoozeClipsetHeavy = ini.GetString("Drink", "ClipsetHeavy", cfg.BoozeClipsetHeavy);

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

                cfg.CreditOutsideSleep = ini.GetBool("Sleeping", "CreditOutsideSleep",
                                                     cfg.CreditOutsideSleep);
                cfg.OutsideSleepMinHours = ini.GetFloat("Sleeping", "OutsideSleepMinHours",
                                                        cfg.OutsideSleepMinHours, 0.5f, 24f);
                cfg.OutsideSleepMaxHours = ini.GetFloat("Sleeping", "OutsideSleepMaxHours",
                                                        cfg.OutsideSleepMaxHours, 1f, 48f);
                cfg.OutsideSleepQuality = ini.GetFloat("Sleeping", "OutsideSleepQuality",
                                                       cfg.OutsideSleepQuality, 0f, 1f);

                cfg.PriceMultiplier = ini.GetFloat("Money", "PriceMultiplier",
                                                   cfg.PriceMultiplier, 0f, 50f);

                // THE FALLBACK IS THE CURRENT VALUE, not an empty string. With "" there, a
                // missing key silently wiped the built-in default instead of leaving it
                // alone -- so the one script we actually know the name of would never have
                // been terminated on any install whose ini predates it. An ini that names
                // the key and leaves it blank still means "suppress nothing", which is the
                // behaviour somebody typing that would expect.
                cfg.SuppressScripts = Split(ini.GetString("Counters", "SuppressScripts",
                                                          string.Join(",", cfg.SuppressScripts)));

                cfg.SocialEnabled = ini.GetBool("Social", "Enabled", cfg.SocialEnabled);
                cfg.SocialChance = ini.GetInt("Social", "ChancePercent", cfg.SocialChance);
                cfg.SocialGapSeconds = ini.GetInt("Social", "GapSeconds", cfg.SocialGapSeconds);

                cfg.ShowShopBlips = ini.GetBool("Map", "ShowShopBlips", cfg.ShowShopBlips);
                cfg.ShopBlipRange = ini.GetFloat("Map", "ShopBlipRange",
                                                 cfg.ShopBlipRange, 0f, 5000f);
                cfg.ShopBlipsOnMainMap = ini.GetBool("Map", "ShopBlipsOnMainMap",
                                                     cfg.ShopBlipsOnMainMap);

                cfg.ShowHud = ini.GetBool("HUD", "Show", cfg.ShowHud);
                cfg.HudAutoPosition = ini.GetBool("HUD", "AutoPosition", cfg.HudAutoPosition);
                cfg.HudX = ini.GetFloat("HUD", "X", cfg.HudX, -0.2f, 1.2f);
                cfg.HudY = ini.GetFloat("HUD", "Y", cfg.HudY, -0.2f, 1.2f);
                cfg.HudSize = ini.GetFloat("HUD", "Size", cfg.HudSize, 0.005f, 0.30f);
                cfg.HudGap = ini.GetFloat("HUD", "Gap", cfg.HudGap, 0f, 3f);
                cfg.HudOpacity = ini.GetFloat("HUD", "Opacity", cfg.HudOpacity, 0.05f, 1f);
                cfg.HudAnimate = ini.GetBool("HUD", "Animate", cfg.HudAnimate);
                cfg.HudShimmer = ini.GetFloat("HUD", "Shimmer", cfg.HudShimmer, 0f, 1f);
                cfg.HudSway = ini.GetFloat("HUD", "Sway", cfg.HudSway, 0f, 1f);
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

            if (BoozeHeavyAt < BoozeDrunkAt)
            {
                Log.Warn("[Drink] HeavyAt is below DrunkAt. Swapping them - the stagger has to " +
                         "start higher than the sway.");

                var b = BoozeHeavyAt;
                BoozeHeavyAt = BoozeDrunkAt;
                BoozeDrunkAt = b;
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

        /// <summary>A comma-separated ini value as a trimmed list, empties dropped.</summary>
        private static string[] Split(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];

            var parts = text.Split(',');
            var list = new System.Collections.Generic.List<string>();

            foreach (var part in parts)
            {
                var s = part.Trim();
                if (s.Length > 0) list.Add(s);
            }

            return list.ToArray();
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
