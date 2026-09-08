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
    /// <summary>Which HUD is drawn beside the minimap.</summary>
    internal enum HudStyle
    {
        /// <summary>An apple and an eye that change shape.</summary>
        Icons,

        /// <summary>
        /// Two filled bars, in the manner of the fuel gauge in Fumes. The default.
        /// </summary>
        Bars
    }

    /// <summary>Which shape the vitals -- health, armour, energy -- take.</summary>
    internal enum VitalsStyle
    {
        /// <summary>Three columns at the left of the row, framed as the sleep and food bars are. The default.</summary>
        Upright,

        /// <summary>Three bars lying where the game's own strip was, under the minimap.</summary>
        Strip
    }

    /// <summary>Whether a special ability bar is drawn when the energy meter is switched off.</summary>
    internal enum SpecialMode
    {
        /// <summary>Only for Michael, Franklin and Trevor -- which is when the game draws its own.</summary>
        Auto,
        Always,
        Never
    }

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
        /// step with all of it the moment anybody changes the timescale.
        ///
        /// SLOWED FROM 28. At GTA's default rate a game hour is two real minutes, so 28 was
        /// under an hour of play from full to starving -- which turns a background system into
        /// an errand, and turns fifty shops into somewhere you have to stop rather than
        /// somewhere you can. 52 is a bit over two game days, and about an hour and three
        /// quarters of real play: enough that eating is something you do while getting on
        /// with the game rather than instead of it.
        /// </summary>
        public float HungerHoursToEmpty = 54f;

        /// <summary>Multiplier on the drain while sprinting, swimming or otherwise working.</summary>
        public float HungerExertionMultiplier = 2.2f;

        /// <summary>
        /// How much faster SLEEP goes while he is working, at a full sprint.
        ///
        /// SMALLER THAN THE FOOD ONE ON PURPOSE. Running the length of the city makes you
        /// hungry long before it makes you sleepy, and a number as big as hunger's would have
        /// a jog across Vinewood costing most of a night's rest.
        /// </summary>
        public float SleepExertionMultiplier = 1.5f;

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
        public float SleepHoursToEmpty = 82f;

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
        public float SleepHoursToFull = 8f;

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

        /// <summary>
        /// Health points per game hour, once hunger is at zero.
        ///
        /// RAISED FROM 4, WHICH WAS INVISIBLE. Four an hour on a two-hundred point bar with
        /// the floor a sixth of the way up is forty-two game hours to go from full health to
        /// the floor -- an hour and a half of real play, which nobody was ever going to
        /// notice happening. Fifteen gets there in about twenty real minutes: slow enough
        /// that it is a warning rather than a punishment, fast enough to be a warning at all.
        /// </summary>
        public float StarvingHealthPerHour = 15f;

        // ---- running out of sleep entirely -----------------------------------

        /// <summary>
        /// Whether an empty sleep meter eventually puts him on the floor.
        ///
        /// The meter had no bottom before this: it emptied, the walk got loose, the camera
        /// swayed, and then nothing else ever happened however long you stayed up. A need
        /// with no consequence at the end of it is a dial, not a need.
        /// </summary>
        public bool SleepCollapse = true;

        /// <summary>How long he is out for, in game hours.</summary>
        public float SleepCollapseHours = 4f;

        /// <summary>
        /// How much of a rest that is, against a proper night. Sleeping rough on a pavement,
        /// so the car's figure rather than the bed's -- and four hours at that comes back at
        /// about a fifth of the meter, which is enough that he does not simply drop again.
        /// </summary>
        public float SleepCollapseQuality = 0.55f;

        /// <summary>
        /// Real seconds between the meter hitting zero and him going down.
        ///
        /// A window rather than an instant, because being taken off your feet with no warning
        /// reads as the mod crashing. He gets told, the screen starts to swim, and then he
        /// goes -- which is long enough to pull over, and short enough to mean something.
        /// </summary>
        public float SleepCollapseAfterSeconds = 30f;

        /// <summary>Whether an empty sleep meter makes the picture swim.</summary>
        public bool SleepWobble = true;

        // ---- Sleeping --------------------------------------------------------

        public bool SleepInBeds = true;
        public bool SleepInCars = true;

        /// <summary>
        /// Whether a car with its engine RUNNING is somewhere you can sleep. Off: the car has
        /// to be stopped and switched off, which is the difference between parking up and
        /// pausing at the lights. On is for anybody who would rather not have to turn the key.
        /// </summary>
        public bool SleepEngineOn = false;

        /// <summary>
        /// How long the interact has to be HELD to sleep in a car. Seconds; 0 is a tap.
        ///
        /// A CAR ONLY, and not a bed. In a car the interact is the same button that orders at
        /// a drive-through, so a tap meant for lunch put you to sleep instead -- which is what
        /// was reported. At a bed nothing else is competing for the press, and a hold there
        /// would be friction bought with nothing.
        ///
        /// 1.5 rather than the three seconds asked for: long enough that it cannot happen by
        /// accident, short enough that somebody who meant it does not wonder whether it is
        /// broken. The ini goes to five for anybody who wants more.
        /// </summary>
        public float CarSleepHoldSeconds = 1.5f;

        /// <summary>Game hours a proper bed advances the clock by.</summary>
        public float BedHours = 8f;

        /// <summary>Game hours a sleep in a car advances the clock by.</summary>
        public float CarHours = 4f;

        /// <summary>
        /// How much of the sleep meter a car gives back, against a bed's whole.
        ///
        /// A car is a worse night's sleep, and this is the only place that says so -- the
        /// hours alone would make it merely a shorter one.
        /// </summary>
        /// <summary>
        /// Waking up in the road with two officers at the windows.
        ///
        /// ONLY IN THE ROAD -- see Knock.Exposed. A car park, a driveway or the back of a
        /// building is nobody's business and nothing happens there.
        /// </summary>
        public bool PoliceWake = true;

        /// <summary>How often it happens when the spot does qualify. 0 never, 1 every time.</summary>
        public float PoliceWakeChance = 0.6f;

        /// <summary>
        /// How many people and cars have to be about for the street to count as public.
        ///
        /// Counted within seventy metres, pedestrians and vehicles together. Six is a normal
        /// city street and more than an empty country road will ever manage.
        /// </summary>
        public int PoliceWakeNeighbours = 6;

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

        /// <summary>
        /// How far from a till the vanilla counter menu gets shut up, in metres.
        ///
        /// WIDER THAN OUR OWN REACH ON PURPOSE, and that was the whole bug. The sweep used to
        /// run only once the player was inside Counters.TillReach -- 1.9m, close enough to be
        /// leaning on the counter -- but the game's own shop trigger is bigger than that, so
        /// the vanilla list had already been offered and taken before we ever looked. Five
        /// metres gets there first and is still small enough to be one shop's till.
        /// </summary>
        public float CounterHushReach = 5f;

        /// <summary>
        /// Also swallow the context press inside that radius.
        ///
        /// BELT AND BRACES, because terminating an object script is not permanent: the game
        /// starts ob_cashregister again the moment its register is near, so between one sweep
        /// and the next there is a window where it is alive and listening. Holding the control
        /// down closes that window -- our own reads go through the disabled variant, so the
        /// key and the pad still reach us while the game hears nothing.
        /// </summary>
        public bool BlockVanillaCounter = true;

        // ---- Speech ----------------------------------------------------------

        /// <summary>
        /// Whether the player says anything when he buys something.
        ///
        /// The game's own ambient speech bank, in whichever character's voice you happen to
        /// be -- so it is a Franklin line for Franklin and a Trevor line for Trevor, which
        /// is more in character than anything written here could be.
        /// </summary>
        public bool SpeechEnabled = true;

        /// <summary>Percent chance a purchase actually gets a line out of him.</summary>
        public int SpeechChance = 45;

        /// <summary>
        /// Shortest gap between two lines, in real seconds.
        ///
        /// A character who thanks the cashier every single time has a tic. The gap is what
        /// keeps it feeling like a person rather than a trigger being pulled.
        /// </summary>
        public int SpeechGapSeconds = 45;

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
        /// Whether they also appear on the PAUSE map. On.
        ///
        /// This used to be off, and the argument was clutter: a screen full of identical shop
        /// icons is what makes people turn a mod off. What changed is that the blips are
        /// GROUPED now -- the pause map's legend collects them under one name and steps
        /// through them with left and right -- so the big map is a directory you can page
        /// rather than a rash of pins. Planning a journey past somewhere to eat is the one
        /// job the pause map is actually for.
        /// </summary>
        public bool ShopBlipsOnMainMap = true;

        /// <summary>
        /// Whether every shop marker carries the SAME name, so the map legend lists them once.
        ///
        /// THE LEGEND GROUPS BY NAME, and that is the whole mechanism. GTA builds the list on
        /// the pause map from the distinct names of the blips on it, which is why six Bean
        /// Machines have always been one row and not six -- they share a name already.
        /// Everything else has its own, so a hundred shops is a hundred rows, and the legend
        /// went past a hundred and forty entries with the game's own markers in among them.
        ///
        /// One shared name collapses the lot into a single row. Better than that, the legend's
        /// left and right keys step through the blips WITHIN the selected row, so one row means
        /// one place to stand and slide through every shop in the city.
        ///
        /// WHAT IT COSTS is the name on the marker when you highlight one: it will say the
        /// group instead of the shop. That is why it is a switch and not a decision. The shop
        /// still names itself when you walk up to the door, which is where it matters.
        /// </summary>
        public bool GroupShopBlips = true;

        /// <summary>
        /// What that one row is called. Also what a highlighted marker says.
        ///
        /// Kept as a setting because it is the single line of text this mod puts in a list the
        /// player reads often, and one person's "Food" is another's "Bare Minimum".
        ///
        /// SHORT ON PURPOSE. Grouping is what puts a prev/next pager on the legend row -- the
        /// row reads name, then "&lt; 25/38 &gt;", then the sprite -- and the game clips the name
        /// to whatever is left, keeping the END of it. "Food &amp; Drink" came out as "&amp; Drink".
        /// Anything much past six characters will lose its head the same way.
        /// </summary>
        public string ShopBlipGroupName = "Food";

        /// <summary>
        /// Whether hovering a shop's marker on the pause map puts up a card naming it.
        /// </summary>
        ///
        /// <remarks>
        /// THE ONE POPUP THE PAUSE MAP ALLOWS. With the markers grouped the legend says
        /// "Food 34/160" and nothing about which shop. The game tells scripts which blip the
        /// cursor is over only for blips flagged the way Online's job blips are, so every shop
        /// marker is flagged and the card reads the hook. See Venues.MapCard. Off leaves the
        /// legend as the only name.
        /// </remarks>
        public bool ShopBlipHoverCard = true;

        // ---- Money -----------------------------------------------------------

        /// <summary>Scales every price in foods.json at once, for anybody who finds them wrong.</summary>
        public float PriceMultiplier = 1f;

        // ---- HUD -------------------------------------------------------------

        public bool ShowHud = true;

        /// <summary>
        /// Place the marks against the minimap automatically, whatever the screen shape is.
        ///
        /// OFF BY DEFAULT NOW, and the position below is a measured one rather than a computed
        /// one. Auto asks the GAME where the minimap ends and tucks the pair just past it,
        /// which is right for a vanilla radar and wrong the moment anything has resized one --
        /// and a great many people run something that has. On the screen this default was
        /// taken from, the game reports its map ending at 0.117 of the width while the map on
        /// screen runs most of the way to twice that, so auto put the pair on top of it.
        ///
        /// THE TRADE IS REAL AND IT IS WORTH KNOWING. GTA anchors the minimap to screen
        /// HEIGHT, so the map is a constant number of PIXELS wide on any monitor of the same
        /// height and a SHRINKING fraction of the width as the screen gets wider -- its right
        /// edge is 0.157 of the width on 16:9 and about 0.117 on a 21:9. A fixed X therefore
        /// cannot hug the map on both. This one is clear of the map on either, which costs the
        /// tucked-in look on 16:9 and buys a HUD that is never underneath anything.
        ///
        /// Turn this back on for the old behaviour; it is the first row of the F7 placement
        /// page and takes effect the moment it is pressed.
        /// </summary>
        public bool HudAutoPosition = false;

        /// <summary>
        /// Used only when HudAutoPosition is false. Fractions of the screen, and the TOP LEFT
        /// of the pair -- the foot of it is HudY + HudSize * 2 + the gap, which is the line
        /// the bars stand on when the style is switched.
        ///
        /// Down at the bottom edge and clear of the minimap's right-hand side. Taken off a
        /// 3440x1440 screen with the pair placed by hand: left edge 0.252, foot 0.983.
        /// </summary>
        public float HudX = 0.252f;
        public float HudY = 0.9873f;

        /// <summary>
        /// Icon height as a fraction of screen height. Width follows; the PNGs are square.
        ///
        /// IT ALSO SETS THE FOOT, which is worth knowing before reaching for it to move the
        /// bars. Bars() is handed the foot and never sees this number, so on the bar style
        /// size is a position control and nothing else -- winding it down to the floor is a
        /// way of dragging the pair towards the bottom of the screen, and it is not the way.
        /// HudY is.
        /// </summary>
        public float HudSize = 0.032f;

        /// <summary>Clear air between the two icons, as a fraction of the icon's own height.</summary>
        public float HudGap = 0.22f;

        public float HudOpacity = 0.72f;

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

        /// <summary>
        /// Which of the two HUDs is on screen.
        ///
        /// NOT A REPLACEMENT, A CHOICE, and the bars are the one that ships. The icons were
        /// the original requirement and they say more at a glance than a length does -- but a
        /// bar reads its number off immediately, which the icons deliberately will not, and
        /// that turns out to be what people expect when they install a hunger mod. The first
        /// report on the first release was somebody who thought the icons WERE the bug.
        ///
        /// The icons are one press away on the F7 placement page and nothing about them has
        /// been taken out.
        /// </summary>
        public HudStyle Style = HudStyle.Bars;

        /// <summary>
        /// How tall the WHOLE gauge is -- bar plus the plate under it -- as a fraction of
        /// screen height. Bars only.
        ///
        /// The plate is inside this figure so that the number means the same thing here as
        /// Height does in Fumes, where the pump sits inside the gauge. Set both to the SAME
        /// number and the two instruments are the same height on screen; when this measured
        /// the bar alone they were not, and no amount of care with either number would have
        /// made them agree.
        ///
        /// THE DEFAULT IS DELIBERATELY TALLER THAN THE FUEL GAUGE. It was set to Fumes' own
        /// 0.1678 so the two would match exactly, and then tuned upward from there in front
        /// of the actual minimap -- which is the only place this can honestly be judged. Two
        /// meters read at a glance want a bit more column than one meter read on purpose.
        ///
        /// So 0.1678 is not a magic number to restore; it is simply what Fumes ships, and
        /// what to set this to if an exact match is ever wanted again.
        ///
        /// Named Length rather than Width because the bars stand up: the number is the run of
        /// the fill, and calling that a width while it measures downward is the sort of thing
        /// somebody edits in the wrong direction once and never trusts again.
        ///
        /// LIFTED STRAIGHT FROM THE FUEL GAUGE IN FUMES, along with the width and the icon
        /// scale below. Two mods by the same hand putting instruments on the same screen
        /// should agree about how big an instrument is, and Fumes' numbers have been looked
        /// at on this monitor for months, which is worth more than a fresh guess.
        /// </summary>
        public float HudBarLength = 0.2213f;

        /// <summary>How WIDE a bar is, as a fraction of screen width. Fumes' figure.</summary>
        public float HudBarWidth = 0.0048f;

        /// <summary>
        /// How far EITHER bar's surface moves, 0 to 1.
        ///
        /// A DIAL BECAUSE THIS ONE IS PURE FEEL and has been wrong in both directions on both
        /// bars. Hunger started as a wave rolling across fifteen pixels, which jittered, and
        /// the fix took the travel down to under two pixels, which is smooth and invisible.
        /// Sleep went three pixels (nobody could see it), then seven (it pulled the eye), and
        /// now sits between.
        ///
        /// ONE NUMBER FOR BOTH, because "how much do the levels move" is one question however
        /// differently the two answer it -- hunger bows, sleep takes a drop and settles.
        /// </summary>
        public float HudBarWave = 1f;

        /// <summary>
        /// Overall speed of everything that moves inside a bar. 1 is as shipped.
        ///
        /// ONE DIAL OVER THE LOT, on top of the individual ones, because "too fast" has now
        /// been said four times about four different parts and every answer so far has been
        /// to slow one of them. This scales the swell, the tilt, the breath, the drifting
        /// bands and the specks together, so the whole instrument can be taken down a gear
        /// without any part of it coming out of step with the rest.
        ///
        /// Below 1 is slower. The base numbers have already been slowed nearly twice over
        /// underneath it, so 1 is not where it started.
        /// </summary>
        /// <summary>
        /// How fast EVERYTHING in both bars moves. 1 is the rate they were drawn at.
        ///
        /// One number rather than a dial per animation, because it multiplies the clock the
        /// whole gauge reads: the surface, the drift inside the fill, the sediment and the
        /// stars all come out of Clock(), so this is the only knob that means "faster" without
        /// meaning anything else as well.
        ///
        /// THAT IS THE POINT, and it was learned the hard way. Three separate attempts to make
        /// this livelier reached for the periods and the travel instead, and each one changed
        /// the SHAPE of the animation while chasing its speed -- a faster wave with a bigger
        /// swing is a different animation, not the same one hurried along. Turning the clock
        /// up cannot do that: every relationship inside the gauge is preserved exactly and only
        /// the rate changes.
        ///
        /// Defaults to 42. Not a typo, and not arrived at by taste either -- the periods
        /// underneath are 144 and 208 SECONDS, which were tuned across about six rounds of
        /// somebody saying it was too fast and are now agreed to be far too slow. Forty-two turns
        /// those into a bow of 3.4s and a drift of 5.0s, which is a surface visibly moving.
        ///
        /// The number looks absurd because it is compensating for periods that were never
        /// re-tuned after the verdict flipped. Turning the clock up is still the right lever --
        /// it is the only one that changes the rate without changing the shape, which three
        /// reverted attempts established the hard way -- but the honest reading of a 30x
        /// multiplier is that the constants under it belong somewhere else, and rewriting them
        /// would break the one thing that has been signed off.
        /// </summary>
        public float HudBarPace = 42f;

        /// <summary>
        /// How much faster the whole gauge runs at a full sprint.
        ///
        /// A MULTIPLIER ON BarPace RATHER THAN A SECOND PACE, so it rides on top of the one
        /// speed dial instead of arguing with it. The waterline, the contents and the stars
        /// keep their ratios exactly and the entire instrument simply winds up -- which is the
        /// same argument BarPace itself is built on and the reason it is the only lever here
        /// that changes the rate without changing the shape.
        ///
        /// It reads the SAME Effort the drains do, and that is the whole point of it: the bars
        /// quicken for precisely the reason they are emptying quicker, so the two of them say
        /// one thing rather than two. A jog is 0.45 of the way there; a sprint is all of it.
        /// </summary>
        public float HudBarEffort = 2f;

        /// <summary>
        /// How quickly the specks inside the bars move, 0 to 1. 0 stops them dead.
        ///
        /// A DIAL FOR THE SAME REASON BarWave IS ONE: pace is feel, and feel does not survive
        /// being guessed at. These went in at roughly three times this and read as busy --
        /// they are meant to be something you notice on the second look, not traffic.
        /// </summary>
        public float HudBarDrift = 1f;

        /// <summary>
        /// How hard a bar jolts when its level changes in a step, 0 to 1. 0 is never.
        ///
        /// VITALS' SLOSH. A meal, a pill or a night's sleep throws the level and lets it ring
        /// down like liquid; the slow drain moves too little a frame to register. See Gauge.Slosh.
        /// </summary>
        public float HudBarSlosh = 1f;

        /// <summary>
        /// How much the liquid moves with the player's own motion, 0 to 1. Brake hard and the
        /// levels lift and lean, then settle; land from a height and they drop. 0 turns it off.
        /// </summary>
        public float HudBarLean = 1f;

        /// <summary>
        /// The five bars, left to right, by name: armour, health, sleep, food, energy. Any
        /// order; anything left out stands at the end in this order; a name that is not one
        /// of the five is ignored. See Gauge.Standing.
        /// </summary>
        public string HudRowOrder = "health, sleep, food, energy";

        /// <summary>
        /// THE WHOLE LOT AS ONE: an offset, in fractions of the screen, added to the row of bars
        /// AND to the cash readout, so the two move together. Each keeps its own position under
        /// it -- X and Y for the row, CashX and CashY for the cash. The minimap and its frame
        /// stay where the game's safe-zone slider puts them; no native the game exposes will
        /// move the minimap.
        /// </summary>
        public float HudGroupX = 0f;
        public float HudGroupY = 0f;

        /// <summary>
        /// The cash readout, brought down beside the bars. The game's own cannot be moved there
        /// -- its Y is fixed by the stack it sits in at the top right -- so it is hidden and
        /// the same thing drawn off the end of the row: the total in the game's money face, the
        /// change under it, counting up, holding, fading. CashX and CashY nudge it from its
        /// place beside the last bar, in fractions of the screen; CashScale is the size of the
        /// text; CashSeconds how long it stays up after the money moves. Off leaves the game's --
        /// and it is off: the game's readout at the top right was preferred once this was seen.
        /// </summary>
        public bool MoveCash = false;
        public float CashX = 0f;
        public float CashY = 0f;
        public float CashScale = 0.55f;
        public float CashSeconds = 4f;

        // ---- Vitals ----------------------------------------------------------
        //
        // HEALTH, ARMOUR AND ENERGY, once a mod of their own. The game's strip under the
        // minimap is hidden and the three are drawn as liquid, upright in the row with the
        // sleep and food bars or lying where the strip was. See Vitals.VitalsHud.

        public bool VitalsEnabled = true;
        public VitalsStyle VitalsStyle = VitalsStyle.Upright;

        /// <summary>Shows the game's own bars again, with ours drawn half-strength over them, for lining up.</summary>
        public bool VitalsCompare = false;

        /// <summary>Hide the game's health and armour strip under the minimap, and its special ability bar.</summary>
        public bool VitalsHideHealthArmour = true;
        public bool VitalsHideSpecial = true;

        /// <summary>
        /// The layout the minimap is told to use. 3 is the golf layout, which has no bars in
        /// it; that is the whole trick. 0 is what the game itself uses in single player.
        /// </summary>
        public int VitalsHideType = 3;
        public int VitalsShowType = 0;

        /// <summary>
        /// Collapse the big map once at start-up. OFF: the call itself was suspected of leaving
        /// the radar blank until the pause menu, and this mod never opens the big map anyway.
        /// </summary>
        public bool VitalsShrinkMapOnStart = false;

        /// <summary>
        /// The alternative to the flick: the radar off and on for one frame after the layout
        /// call. It was tried first and did NOT bring the minimap back; kept for an ini that
        /// would rather have no big-map blink and live with pressing Start. Only used with
        /// MapFlick off.
        /// </summary>
        public bool VitalsRefreshRadar = false;

        /// <summary>
        /// Flick the big map open and shut after the layout call, so the game rebuilds the
        /// minimap -- which otherwise stays BLANK until the pause menu is opened once. ON: it
        /// is the FiveM crowd's answer to exactly this, and the shut is held back and repeated
        /// so it is not ignored the way a one-frame shut was. Expect the big map for a fraction
        /// of a second at load.
        /// </summary>
        public bool VitalsMapFlick = true;

        /// <summary>
        /// THE THIRD BAR IS ENERGY: a sprint meter, and the special ability's tank. It drains
        /// while you sprint and while the ability runs, and empty it locks both -- you jog, and
        /// there is no ability -- until it has come back to EnergySprintAgainAt. Off, the third
        /// bar is the special ability's charge, worked out, see SpecialMode.
        /// </summary>
        public bool VitalsEnergy = true;

        /// <summary>Seconds of flat-out sprinting from full to empty. 45: "heaps longer" than the 12 it shipped at.</summary>
        public float EnergySprintSeconds = 39f;

        /// <summary>Seconds from empty back to full, stood still or in a car. Half as fast at a jog.</summary>
        public float EnergyRebuildSeconds = 10f;

        /// <summary>How far back up it has to be before the sprint unlocks, 0 to 1.</summary>
        public float EnergySprintAgainAt = 0.35f;

        /// <summary>
        /// THE SPECIAL ABILITY RUNS ON THE ENERGY BAR. Rage, focus, the slow motion: while it
        /// is on it drains this bar, and when the bar is out it stops with it and he is winded.
        /// The game's own meter is kept topped up rather than read, so this bar is the only
        /// limit -- see Energy.Ability. Off hands the ability back to the game untouched.
        /// </summary>
        public bool EnergyPowersSpecial = true;

        /// <summary>Seconds of special ability from a full bar. 30 is about what the game gives at full skill.</summary>
        public float EnergySpecialSeconds = 30f;

        /// <summary>
        /// How long a drink holds the energy bar full, in REAL minutes. 0 turns it off.
        ///
        /// Anything he drinks that is not alcohol: a soft drink, a coffee, a water, a juice.
        /// Sprinting does not run it down while this holds, and being winded is lifted the
        /// moment it starts. Real minutes rather than game hours, because the energy bar is a
        /// real-time meter -- it drains in the seconds you hold sprint, not over a game day.
        /// </summary>
        public float EnergyDrinkHoldMinutes = 15f;

        /// <summary>With the energy meter off: when a special ability bar is drawn at all.</summary>
        public SpecialMode VitalsSpecial = SpecialMode.Auto;

        /// <summary>
        /// The stock bars' own colours. Health runs to red with the level; armour is the game's
        /// blue; the third is energy's TEAL or the special's gold. Energy was yellow and yellow
        /// is what the fuel gauge beside it shows at a full tank; teal is the one family none of
        /// the six bars on that edge of the screen uses, and it reads as electricity under a bolt.
        /// </summary>
        public System.Drawing.Color VitalsHealth = System.Drawing.Color.FromArgb(255, 114, 204, 114);
        public System.Drawing.Color VitalsArmour = System.Drawing.Color.FromArgb(255, 93, 182, 229);
        public System.Drawing.Color VitalsEnergyColour = System.Drawing.Color.FromArgb(255, 70, 225, 205);
        public System.Drawing.Color VitalsSpecialColour = System.Drawing.Color.FromArgb(255, 240, 200, 80);

        /// <summary>One clock for everything periodic in the vitals. 1 is normal.</summary>
        public float VitalsPace = 1f;

        /// <summary>How many specks live inside the vitals, 0 to 1. Bubbles, glints, sparks.</summary>
        public float VitalsParticles = 1f;

        /// <summary>A lighter band along one side of the fill, 0 to 1. 0 is flat.</summary>
        public float VitalsGloss = 0.18f;

        /// <summary>
        /// The bevel and the sweeping highlight on every bar, 0 to 1. 0 is flat.
        ///
        /// A lit edge down the left of the fill and a shadowed one down the right, with a soft
        /// slanted band crossing every six seconds or so. It was the armour's alone; it is the
        /// whole row's now, and this is the dial for it.
        /// </summary>
        public float VitalsRelief = 1f;

        public bool VitalsLowHealthPulse = true;
        public float VitalsLowHealthAt = 0.25f;

        /// <summary>The third bar brightens and beats while the special ability is running.</summary>
        public bool VitalsActivePulse = true;

        /// <summary>The special meter's rates, for when the energy meter is off. The game does not say how full it is.</summary>
        public float SpecialDurationSeconds = 30f;
        public float SpecialMinDurationFraction = 0.30f;
        public float SpecialRechargeSeconds = 150f;
        public float SpecialMinimumCharge = 0.10f;

        // ---- the strip, when the vitals lie under the minimap ----

        /// <summary>Put the strip where the game's own strip is, asked of the game.</summary>
        public bool VitalsStripAuto = true;

        /// <summary>Used only when Auto is off: the strip's LEFT edge, the BOTTOM of the stock strip, and the minimap's width as a fraction of screen HEIGHT.</summary>
        public float VitalsStripX = 0.0150f;
        public float VitalsStripY = 0.9860f;
        public float VitalsStripWidth = 0.25f;

        public float VitalsStripWidthScale = 1f;

        /// <summary>How thick the strip's bars are, as a fraction of screen WIDTH -- the same unit as BarWidth.</summary>
        public float VitalsStripThickness = 0.0048f;

        public float VitalsStripOffsetX = 0f;
        public float VitalsStripOffsetY = 0f;

        /// <summary>How the strip is shared out. The game gives health half and splits the rest.</summary>
        public float VitalsHealthShare = 0.50f;
        public float VitalsThirdShare = 0.25f;

        /// <summary>Daylight between two bars of the strip, as a fraction of its width.</summary>
        public float VitalsStripGap = 0.008f;

        /// <summary>The empty part of a strip bar, and how solid the strip is.</summary>
        public System.Drawing.Color VitalsChannel = System.Drawing.Color.FromArgb(110, 0, 0, 0);
        public float VitalsStripOpacity = 0.85f;

        // ---- Minimap ---------------------------------------------------------

        /// <summary>A frame round the minimap in the bars' own black, so the map and the row read as one instrument.</summary>
        public bool MinimapFrame = true;

        /// <summary>The street and the suburb, always, written into the frame's top band either side of the compass.</summary>
        public bool MinimapLabel = true;

        /// <summary>
        /// How far outside the map the frame sits, as a fraction of screen height, with a
        /// translucent mat filling the gap. The game clamps far-off blips to the edge of the
        /// map and half of each pokes over it; flush (0) puts that half under the frame line.
        /// </summary>
        public float MinimapFrameGap = 0.006f;

        /// <summary>
        /// How far DOWN INTO the map the frame's top band reaches, as a fraction of the map's
        /// height. The radar fades out at the top and lets the world through, and the band
        /// comes down over that so the map ends hard -- but every bit of it past the fade is
        /// map you have paid for and cannot see, so it is the smallest number that still buries
        /// the fade. 0 is a plain line. The room the writing needs is BandHeight, above the map,
        /// and has nothing to do with this.
        /// </summary>
        public float MinimapTopCover = 0.05f;

        /// <summary>
        /// How tall the top band is ABOVE the map, as a fraction of screen height -- the room
        /// the street, the suburb and the compass are written in.
        ///
        /// IT HAS TO CLEAR THE BLIPS. The game draws its blips over anything a script draws,
        /// and a far-off one is clamped to the map's edge with half of it hanging over the top.
        /// A thin band put the writing right under that row of blips and neither could be read.
        /// Nothing the frame draws will ever get on top of them, so the band rises clear of
        /// them instead. Grow the bars with it -- see HudBarLength -- or the row and the frame
        /// stop ending on the same line.
        /// </summary>
        public float MinimapBandHeight = 0.034f;

        /// <summary>
        /// How far BELOW the map the plate's top edge starts, as a fraction of screen height.
        ///
        /// The plate begins at the map's bottom edge and the frame's foot drops far enough to
        /// keep a line of text under it -- which already reaches within a few pixels of the
        /// bottom of the screen, because the map's bottom edge is barely above the safe line.
        /// So there is no room left to clear the half of a clamped blip that hangs under the
        /// map as well. This buys that clearance out of the plate's own height: raise it and
        /// the blips come clear, and the writing in the plate shrinks to fit what is left.
        /// </summary>
        public float MinimapPlateDrop = 0f;

        /// <summary>A compass tape at the left of the frame's top band, and a speed readout with a rev bar at the right. In a vehicle only, the speedo.</summary>
        public bool MinimapCompass = true;
        public bool MinimapSpeedo = true;

        /// <summary>The dashboard lights at the left of the plate under the map, in a vehicle: oil (for the engine), headlamp, handbrake.</summary>
        public bool MinimapDash = true;

        /// <summary>KPH or MPH.</summary>
        public string MinimapSpeedUnits = "KPH";

        /// <summary>"r,g,b" or "r,g,b,a", each 0 to 255. Anything else keeps the default and says so.</summary>
        private static System.Drawing.Color ParseColour(string section, string key, string s, System.Drawing.Color fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;

            var parts = s.Split(',');
            if (parts.Length < 3 || parts.Length > 4)
            {
                Log.Warn("[" + section + "] " + key + " = '" + s + "' is not r,g,b or r,g,b,a - using the default.");
                return fallback;
            }

            var n = new int[4];
            n[3] = fallback.A;

            for (var i = 0; i < parts.Length; i++)
            {
                int v;
                if (!int.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Integer,
                                  System.Globalization.CultureInfo.InvariantCulture, out v))
                {
                    Log.Warn("[" + section + "] " + key + " = '" + s + "' has a part that is not a number - using the default.");
                    return fallback;
                }

                n[i] = v < 0 ? 0 : v > 255 ? 255 : v;
            }

            return System.Drawing.Color.FromArgb(n[3], n[0], n[1], n[2]);
        }

        private static VitalsStyle ParseVitalsStyle(string s, VitalsStyle fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;

            switch (s.Trim().ToLowerInvariant())
            {
                case "upright": case "columns": case "vertical": case "bars": return VitalsStyle.Upright;
                case "strip": case "horizontal": case "stock": return VitalsStyle.Strip;
                default:
                    Log.Warn("[Vitals] Style = '" + s + "' is not Upright or Strip - using " + fallback + ".");
                    return fallback;
            }
        }

        private static SpecialMode ParseSpecial(string s, SpecialMode fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;

            switch (s.Trim().ToLowerInvariant())
            {
                case "auto": return SpecialMode.Auto;
                case "always": case "show": case "on": case "true": return SpecialMode.Always;
                case "never": case "hide": case "off": case "false": return SpecialMode.Never;
                default:
                    Log.Warn("[Vitals] Special = '" + s + "' is not Auto, Always or Never - using " + fallback + ".");
                    return fallback;
            }
        }

        /// <summary>
        /// The mark under the bar, as a share of its BLACK PLATE. 1 fills the plate edge to
        /// edge; the default leaves a margin, which is what makes it read as a badge rather
        /// than a cropped picture.
        ///
        /// It has meant three things now -- share of the channel it stood in, then share of
        /// the bar once it moved out from under it, and now share of the plate it sits on.
        /// Clamped to 1 either way, because a mark wider than its own ground is just a mark
        /// on the world again, which is the thing the plate exists to stop.
        /// </summary>
        public float HudBarIconScale = 0.85f;

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
        /// <summary>
        /// Buying puts it in your pocket instead of eating it on the spot.
        ///
        /// ON BY DEFAULT, because instant eating is the thing this replaces: it sells you
        /// exactly one item per visit -- the second is refused while you are still chewing the
        /// first -- and it makes stocking up before a long drive impossible. A shop with
        /// fifteen things on the shelf should let you leave with fifteen things.
        ///
        /// Off restores the old behaviour exactly, for anybody who wants the mod to stay a
        /// two-key affair with nothing to manage.
        /// </summary>
        public bool BuyToPantry = true;

        /// <summary>
        /// How many items fit in a pocket, all kinds counted together.
        ///
        /// A total rather than a number of squares. A pocket holds what it holds; it does not
        /// care whether that is twenty tacos or one of everything, and a player who runs out of
        /// room wants to know how many MORE THINGS they can carry, not how many kinds.
        /// </summary>
        public int PantrySlots = 5;

        // ---- the fridge ------------------------------------------------------

        /// <summary>Whether the fridges in the safehouse kitchens open at all.</summary>
        public bool FridgeEnabled = true;

        /// <summary>
        /// How much the fridge holds, all kinds counted together.
        ///
        /// DELIBERATELY MUCH LARGER THAN THE POCKET, because that is the whole point of it.
        /// Three items is the right size for a pocket -- it is what makes a shop somewhere you
        /// go back to rather than a warehouse you clear out once -- but it also left nowhere
        /// to put a week's shopping, so the big items were pointless to buy. Forty is a
        /// fridge: room enough that stocking up is worth the trip home.
        /// </summary>
        public int FridgeSlots = 40;

        /// <summary>How close you have to be standing, in metres.</summary>
        public float FridgeReach = 1.8f;

        /// <summary>
        /// Whether the SNACK machines sell to you. Not the soda ones -- see VendingSipHunger.
        ///
        /// The game sells nothing out of a candy machine, so this is not a mod taking one
        /// over; it is a mod using a prop the game left as scenery. Every one of them in the
        /// world works, because they are found by model rather than listed by coordinate.
        /// </summary>
        public bool VendingMachines = true;

        /// <summary>Whether the roadside produce stalls sell to you. Same argument.</summary>
        public bool FruitStalls = true;

        /// <summary>
        /// What a fruit stall has on the trestle, as catalogue ids.
        ///
        /// A LIST RATHER THAN A CATEGORY, because "fruit" is not a category in foods.json --
        /// an apple is a Snack and a fresh fruit is Food, and a stall that sold every Snack
        /// in the game would be selling jerky and chocolate off a fruit crate.
        /// </summary>
        public string StallItems = "fruit,apple,got_box";

        /// <summary>
        /// How much hunger a drink out of a VANILLA soda machine gives back. 0 turns it off.
        ///
        /// Small on purpose. The game's own machine restores a little health and plays its own
        /// animation, and this rides along with that rather than replacing it -- you drank
        /// something, so the meter should notice, and that is all it should do.
        /// </summary>
        public float VendingSipHunger = 0.05f;

        /// <summary>
        /// Whether the machines and the stalls get map markers of their own.
        ///
        /// SEPARATE FROM ShowShopBlips, which they also obey. A shop is a destination and a
        /// machine is something you notice walking past, so somebody may reasonably want the
        /// shops marked and not want thirty vending machines on the same map.
        /// </summary>
        public bool MachineBlips = true;

        /// <summary>
        /// Whether the carts the GAME placed get a man behind them and start selling.
        ///
        /// The three hot dog stands and three burger carts in vendors.json are built by this
        /// mod at a listed coordinate. The game puts carts of its own around the map that
        /// nobody could ever buy from, and this is what staffs those -- found by model, like
        /// the fridges and the tills, so every one of them works without a coordinate list.
        ///
        /// A discovered cart is never deleted when it despawns. It was standing in the street
        /// before this mod loaded and it is not ours to remove.
        /// </summary>
        public bool DiscoverCarts = true;

        /// <summary>
        /// Whether a shop with a room in vendors.json can be walked into: hold the key at its
        /// door to go in, hold it inside to come out.
        /// </summary>
        ///
        /// <remarks>
        /// THE ROOMS ARE THE GAME'S OWN and are not where the shop is: the door warps you across
        /// the city into one of the story map's real interiors -- a real 24/7, a real Rob's
        /// Liquor, a real bar -- and back again when you leave. The pavement shelf stays either
        /// way; a press is the shelf and a hold is the door. Off makes every door a shelf only.
        /// </remarks>
        public bool WalkIns = true;

        /// <summary>
        /// Extra fridge model names to look for, comma-separated, on top of the built-in list.
        ///
        /// BECAUSE THE BUILT-IN LIST CANNOT BE COMPLETE. A fridge is found by model name, and
        /// a kitchen this build has never heard of -- a house from an interior mod, a room
        /// Rockstar added after this was written -- has a fridge that simply does not answer.
        /// Before this there was nothing the player could do about that but wait for me.
        ///
        /// A name that does not exist in the running game costs one line in the log and
        /// nothing else, so guessing here is safe.
        /// </summary>
        public string FridgeExtraModels = "";

        /// <summary>
        /// How food is turned in his hand, in degrees about the prop's own three axes.
        ///
        /// A prop attached to the hand bone with no rotation takes the model's own idea of
        /// which way is forward, and the food models were not built for a hand: the hot dog
        /// lay ACROSS the mouth like a cob of corn, every bite taken from its side. A quarter
        /// turn about Z is what points it into the mouth -- found in play, with these three
        /// as live dials in the menu, and confirmed. The dials came out again once it was
        /// settled; the numbers stay in the ini for anybody with a model that wants otherwise.
        ///
        /// Food only. Drinks and the cigarette sit right as they are; turning those with the
        /// same numbers would break two things to fix one.
        /// </summary>
        public float FoodSpinX = 0f;
        public float FoodSpinY = 0f;
        public float FoodSpinZ = 90f;

        public Keys MenuKey = Keys.F7;

        /// <summary>Opens the pocket: what you have bought and not eaten yet.</summary>
        public Keys BagKey = Keys.F11;

        /// <summary>
        /// Whether the settings menu and the pocket open on a CONTROLLER as well.
        ///
        /// A CHORD, not a button, and for the reason Fumes gives for its own: there is no
        /// spare button on a pad -- every one of them is spoken for on foot -- so a single
        /// press is always one press away from something the game already does.
        ///
        /// LB + D-pad UP for the menu and RB + X for the pocket. Fumes already owns LB +
        /// D-pad DOWN for its menu, and somebody running both mods should not have one chord
        /// open two panels.
        ///
        /// The interact needs no setting of its own: everything that reads the interact key
        /// already reads Control.Context beside it, which is the button the on-screen prompt
        /// is showing you in the first place.
        /// </summary>
        public bool MenuPad = true;
        public bool BagPad = true;

        /// <summary>
        /// What the prompt's key cap says on a controller. Any text.
        /// </summary>
        ///
        /// <remarks>
        /// THE WORD, NOT THE BUTTON. Every interact reads the game's own context control on a
        /// pad, and that stays: it is the button the game's own prompts point at. What it is
        /// CALLED depends on the pad, the layout and the person holding it, and this mod
        /// shipped two wrong guesses for it in one afternoon -- "A", then a name that is right
        /// on an Xbox layout and means nothing on a PlayStation one. So the player says.
        ///
        /// Free text on purpose. The menu offers the common names, and anything else typed in
        /// here is shown as itself.
        /// </remarks>
        public string PadInteractLabel = "D-PAD RIGHT";

        /// <summary>
        /// Whether drugs carried in Posted Up show in this mod's pocket and can be taken from
        /// it.
        /// </summary>
        ///
        /// <remarks>
        /// DOES NOTHING WITHOUT THAT MOD, so there is no cost to leaving it on. Off is for
        /// somebody who has both and wants the drug system reached only the way its own author
        /// built it, with this one staying out of it.
        ///
        /// It never touches PantrySlots. The product has a capacity of its own over there --
        /// four hundred grams -- and each mod enforces the limit on the thing it owns, so
        /// turning this on does not cost you room for a sandwich.
        /// </remarks>
        public bool DrugsInPocket = true;

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
                cfg.SleepExertionMultiplier = ini.GetFloat("Sleep", "ExertionMultiplier",
                                                           cfg.SleepExertionMultiplier, 1f, 10f);
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

                cfg.SleepCollapse = ini.GetBool("Sleep", "Collapse", cfg.SleepCollapse);
                cfg.SleepCollapseHours = ini.GetFloat("Sleep", "CollapseHours",
                                                      cfg.SleepCollapseHours, 0.5f, 24f);
                cfg.SleepCollapseQuality = ini.GetFloat("Sleep", "CollapseQuality",
                                                        cfg.SleepCollapseQuality, 0f, 1f);
                cfg.SleepCollapseAfterSeconds = ini.GetFloat("Sleep", "CollapseAfterSeconds",
                                                             cfg.SleepCollapseAfterSeconds, 0f, 600f);
                cfg.SleepWobble = ini.GetBool("Sleep", "Wobble", cfg.SleepWobble);

                cfg.SleepInBeds = ini.GetBool("Sleeping", "InBeds", cfg.SleepInBeds);
                cfg.SleepInCars = ini.GetBool("Sleeping", "InCars", cfg.SleepInCars);
                cfg.SleepEngineOn = ini.GetBool("Sleeping", "EngineOn", cfg.SleepEngineOn);
                cfg.CarSleepHoldSeconds = ini.GetFloat("Sleeping", "CarHoldSeconds",
                                                       cfg.CarSleepHoldSeconds, 0f, 5f);
                cfg.BedHours = ini.GetFloat("Sleeping", "BedHours", cfg.BedHours, 1f, 24f);
                cfg.CarHours = ini.GetFloat("Sleeping", "CarHours", cfg.CarHours, 1f, 24f);
                cfg.CarRestoreFraction = ini.GetFloat("Sleeping", "CarRestoreFraction",
                                                      cfg.CarRestoreFraction, 0.05f, 1f);
                cfg.BedReach = ini.GetFloat("Sleeping", "BedReach", cfg.BedReach, 0.5f, 6f);

                cfg.PoliceWake = ini.GetBool("Sleeping", "PoliceWake", cfg.PoliceWake);
                cfg.PoliceWakeChance = ini.GetFloat("Sleeping", "PoliceWakeChance",
                                                    cfg.PoliceWakeChance, 0f, 1f);
                cfg.PoliceWakeNeighbours = (int)ini.GetFloat("Sleeping", "PoliceWakeNeighbours",
                                                             cfg.PoliceWakeNeighbours, 0f, 60f);

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
                cfg.CounterHushReach = ini.GetFloat("Counters", "HushReach",
                                                    cfg.CounterHushReach, 1f, 25f);
                cfg.BlockVanillaCounter = ini.GetBool("Counters", "BlockVanillaMenu",
                                                      cfg.BlockVanillaCounter);

                cfg.SpeechEnabled = ini.GetBool("Speech", "Enabled", cfg.SpeechEnabled);
                cfg.SpeechChance = ini.GetInt("Speech", "ChancePercent", cfg.SpeechChance);
                cfg.SpeechGapSeconds = ini.GetInt("Speech", "GapSeconds", cfg.SpeechGapSeconds);

                cfg.SocialEnabled = ini.GetBool("Social", "Enabled", cfg.SocialEnabled);
                cfg.SocialChance = ini.GetInt("Social", "ChancePercent", cfg.SocialChance);
                cfg.SocialGapSeconds = ini.GetInt("Social", "GapSeconds", cfg.SocialGapSeconds);

                cfg.ShowShopBlips = ini.GetBool("Map", "ShowShopBlips", cfg.ShowShopBlips);
                cfg.ShopBlipRange = ini.GetFloat("Map", "ShopBlipRange",
                                                 cfg.ShopBlipRange, 0f, 5000f);
                cfg.ShopBlipsOnMainMap = ini.GetBool("Map", "ShopBlipsOnMainMap",
                                                     cfg.ShopBlipsOnMainMap);
                cfg.GroupShopBlips = ini.GetBool("Map", "GroupShopBlips", cfg.GroupShopBlips);
                cfg.ShopBlipGroupName = ini.GetString("Map", "ShopBlipGroupName",
                                                      cfg.ShopBlipGroupName);
                cfg.ShopBlipHoverCard = ini.GetBool("Map", "ShopBlipHoverCard", cfg.ShopBlipHoverCard);

                cfg.ShowHud = ini.GetBool("HUD", "Show", cfg.ShowHud);
                cfg.HudAutoPosition = ini.GetBool("HUD", "AutoPosition", cfg.HudAutoPosition);
                cfg.HudX = ini.GetFloat("HUD", "X", cfg.HudX, -0.2f, 1.2f);
                cfg.HudY = ini.GetFloat("HUD", "Y", cfg.HudY, -0.2f, 1.2f);
                cfg.HudSize = ini.GetFloat("HUD", "Size", cfg.HudSize, 0.005f, 0.30f);
                cfg.HudGap = ini.GetFloat("HUD", "Gap", cfg.HudGap, 0f, 3f);
                cfg.HudOpacity = ini.GetFloat("HUD", "Opacity", cfg.HudOpacity, 0.05f, 1f);
                cfg.Style = ParseStyle(ini.GetString("HUD", "Style", ""), cfg.Style);
                cfg.HudBarLength = ini.GetFloat("HUD", "BarLength", cfg.HudBarLength, 0.004f, 0.6f);
                cfg.HudBarWidth = ini.GetFloat("HUD", "BarWidth", cfg.HudBarWidth, 0.001f, 0.2f);
                cfg.HudBarIconScale = ini.GetFloat("HUD", "BarIconScale",
                                                   cfg.HudBarIconScale, 0.2f, 1f);
                cfg.HudBarWave = ini.GetFloat("HUD", "BarWave", cfg.HudBarWave, 0f, 1f);
                cfg.HudBarDrift = ini.GetFloat("HUD", "BarDrift", cfg.HudBarDrift, 0f, 1f);
                cfg.HudBarPace = ini.GetFloat("HUD", "BarPace", cfg.HudBarPace, 0.15f, 120f);
                cfg.HudBarEffort = ini.GetFloat("HUD", "BarEffort", cfg.HudBarEffort, 1f, 6f);
                cfg.HudBarSlosh = ini.GetFloat("HUD", "BarSlosh", cfg.HudBarSlosh, 0f, 1f);
                cfg.HudBarLean = ini.GetFloat("HUD", "BarLean", cfg.HudBarLean, 0f, 1f);
                cfg.HudRowOrder = ini.GetString("HUD", "RowOrder", cfg.HudRowOrder);
                cfg.HudGroupX = ini.GetFloat("HUD", "GroupX", cfg.HudGroupX, -1f, 1f);
                cfg.HudGroupY = ini.GetFloat("HUD", "GroupY", cfg.HudGroupY, -1f, 1f);
                cfg.MoveCash = ini.GetBool("HUD", "MoveCash", cfg.MoveCash);
                cfg.CashX = ini.GetFloat("HUD", "CashX", cfg.CashX, -2f, 2f);
                cfg.CashY = ini.GetFloat("HUD", "CashY", cfg.CashY, -2f, 2f);
                cfg.CashScale = ini.GetFloat("HUD", "CashScale", cfg.CashScale, 0.15f, 1.5f);
                cfg.CashSeconds = ini.GetFloat("HUD", "CashSeconds", cfg.CashSeconds, 0.5f, 30f);

                cfg.VitalsEnabled = ini.GetBool("Vitals", "Enabled", cfg.VitalsEnabled);
                cfg.VitalsStyle = ParseVitalsStyle(ini.GetString("Vitals", "Style", "Upright"), cfg.VitalsStyle);
                cfg.VitalsCompare = ini.GetBool("Vitals", "Compare", cfg.VitalsCompare);
                cfg.VitalsHideHealthArmour = ini.GetBool("Vitals", "HideHealthArmour", cfg.VitalsHideHealthArmour);
                cfg.VitalsHideSpecial = ini.GetBool("Vitals", "HideSpecial", cfg.VitalsHideSpecial);
                cfg.VitalsHideType = ini.GetInt("Vitals", "HideType", cfg.VitalsHideType, 0, 10);
                cfg.VitalsShowType = ini.GetInt("Vitals", "ShowType", cfg.VitalsShowType, 0, 10);
                cfg.VitalsShrinkMapOnStart = ini.GetBool("Vitals", "ShrinkMapOnStart", cfg.VitalsShrinkMapOnStart);
                cfg.VitalsRefreshRadar = ini.GetBool("Vitals", "RefreshRadar", cfg.VitalsRefreshRadar);
                cfg.VitalsMapFlick = ini.GetBool("Vitals", "MapFlick", cfg.VitalsMapFlick);
                cfg.VitalsEnergy = ini.GetBool("Vitals", "Energy", cfg.VitalsEnergy);
                cfg.EnergySprintSeconds = ini.GetFloat("Vitals", "EnergySprintSeconds", cfg.EnergySprintSeconds, 1f, 600f);
                cfg.EnergyRebuildSeconds = ini.GetFloat("Vitals", "EnergyRebuildSeconds", cfg.EnergyRebuildSeconds, 1f, 600f);
                cfg.EnergySprintAgainAt = ini.GetFloat("Vitals", "EnergySprintAgainAt", cfg.EnergySprintAgainAt, 0.05f, 1f);
                cfg.EnergyPowersSpecial = ini.GetBool("Vitals", "EnergyPowersSpecial", cfg.EnergyPowersSpecial);
                cfg.EnergySpecialSeconds = ini.GetFloat("Vitals", "EnergySpecialSeconds", cfg.EnergySpecialSeconds, 1f, 600f);
                cfg.EnergyDrinkHoldMinutes = ini.GetFloat("Vitals", "EnergyDrinkHoldMinutes", cfg.EnergyDrinkHoldMinutes, 0f, 600f);
                cfg.VitalsSpecial = ParseSpecial(ini.GetString("Vitals", "Special", "Auto"), cfg.VitalsSpecial);
                cfg.VitalsHealth = ParseColour("Vitals", "HealthColour", ini.GetString("Vitals", "HealthColour", null), cfg.VitalsHealth);
                cfg.VitalsArmour = ParseColour("Vitals", "ArmourColour", ini.GetString("Vitals", "ArmourColour", null), cfg.VitalsArmour);
                cfg.VitalsEnergyColour = ParseColour("Vitals", "EnergyColour", ini.GetString("Vitals", "EnergyColour", null), cfg.VitalsEnergyColour);
                cfg.VitalsSpecialColour = ParseColour("Vitals", "SpecialColour", ini.GetString("Vitals", "SpecialColour", null), cfg.VitalsSpecialColour);
                cfg.VitalsPace = ini.GetFloat("Vitals", "Pace", cfg.VitalsPace, 0.05f, 10f);
                cfg.VitalsParticles = ini.GetFloat("Vitals", "Particles", cfg.VitalsParticles, 0f, 1f);
                cfg.VitalsGloss = ini.GetFloat("Vitals", "Gloss", cfg.VitalsGloss, 0f, 1f);
                cfg.VitalsRelief = ini.GetFloat("Vitals", "Relief", cfg.VitalsRelief, 0f, 1f);
                cfg.VitalsLowHealthPulse = ini.GetBool("Vitals", "LowHealthPulse", cfg.VitalsLowHealthPulse);
                cfg.VitalsLowHealthAt = ini.GetFloat("Vitals", "LowHealthAt", cfg.VitalsLowHealthAt, 0f, 1f);
                cfg.VitalsActivePulse = ini.GetBool("Vitals", "ActivePulse", cfg.VitalsActivePulse);
                cfg.SpecialDurationSeconds = ini.GetFloat("Vitals", "SpecialDurationSeconds", cfg.SpecialDurationSeconds, 1f, 600f);
                cfg.SpecialMinDurationFraction = ini.GetFloat("Vitals", "SpecialMinDurationFraction", cfg.SpecialMinDurationFraction, 0.05f, 1f);
                cfg.SpecialRechargeSeconds = ini.GetFloat("Vitals", "SpecialRechargeSeconds", cfg.SpecialRechargeSeconds, 1f, 3600f);
                cfg.SpecialMinimumCharge = ini.GetFloat("Vitals", "SpecialMinimumCharge", cfg.SpecialMinimumCharge, 0f, 1f);

                cfg.VitalsStripAuto = ini.GetBool("VitalsStrip", "Auto", cfg.VitalsStripAuto);
                cfg.VitalsStripX = ini.GetFloat("VitalsStrip", "X", cfg.VitalsStripX, 0f, 1f);
                cfg.VitalsStripY = ini.GetFloat("VitalsStrip", "Y", cfg.VitalsStripY, 0f, 1f);
                cfg.VitalsStripWidth = ini.GetFloat("VitalsStrip", "Width", cfg.VitalsStripWidth, 0.02f, 1f);
                cfg.VitalsStripWidthScale = ini.GetFloat("VitalsStrip", "WidthScale", cfg.VitalsStripWidthScale, 0.2f, 3f);
                cfg.VitalsStripThickness = ini.GetFloat("VitalsStrip", "Thickness", cfg.VitalsStripThickness, 0.001f, 0.03f);
                cfg.VitalsStripOffsetX = ini.GetFloat("VitalsStrip", "OffsetX", cfg.VitalsStripOffsetX, -0.5f, 0.5f);
                cfg.VitalsStripOffsetY = ini.GetFloat("VitalsStrip", "OffsetY", cfg.VitalsStripOffsetY, -0.5f, 0.5f);
                cfg.VitalsHealthShare = ini.GetFloat("VitalsStrip", "HealthShare", cfg.VitalsHealthShare, 0.05f, 1f);
                cfg.VitalsThirdShare = ini.GetFloat("VitalsStrip", "ThirdShare", cfg.VitalsThirdShare, 0.05f, 1f);
                cfg.VitalsStripGap = ini.GetFloat("VitalsStrip", "Gap", cfg.VitalsStripGap, 0f, 0.1f);
                cfg.VitalsChannel = ParseColour("VitalsStrip", "Channel", ini.GetString("VitalsStrip", "Channel", null), cfg.VitalsChannel);
                cfg.VitalsStripOpacity = ini.GetFloat("VitalsStrip", "Opacity", cfg.VitalsStripOpacity, 0.05f, 1f);

                cfg.MinimapFrame = ini.GetBool("Minimap", "Frame", cfg.MinimapFrame);
                cfg.MinimapLabel = ini.GetBool("Minimap", "Label", cfg.MinimapLabel);
                cfg.MinimapFrameGap = ini.GetFloat("Minimap", "FrameGap", cfg.MinimapFrameGap, 0f, 0.03f);
                cfg.MinimapTopCover = ini.GetFloat("Minimap", "TopCover", cfg.MinimapTopCover, 0f, 0.5f);
                cfg.MinimapBandHeight = ini.GetFloat("Minimap", "BandHeight", cfg.MinimapBandHeight, 0.004f, 0.15f);
                cfg.MinimapPlateDrop = ini.GetFloat("Minimap", "PlateDrop", cfg.MinimapPlateDrop, 0f, 0.05f);
                cfg.MinimapCompass = ini.GetBool("Minimap", "Compass", cfg.MinimapCompass);
                cfg.MinimapSpeedo = ini.GetBool("Minimap", "Speedo", cfg.MinimapSpeedo);
                cfg.MinimapDash = ini.GetBool("Minimap", "Dash", cfg.MinimapDash);
                cfg.MinimapSpeedUnits = ini.GetString("Minimap", "SpeedUnits", cfg.MinimapSpeedUnits);

                cfg.HudAnimate = ini.GetBool("HUD", "Animate", cfg.HudAnimate);
                cfg.HudShimmer = ini.GetFloat("HUD", "Shimmer", cfg.HudShimmer, 0f, 1f);
                cfg.HudSway = ini.GetFloat("HUD", "Sway", cfg.HudSway, 0f, 1f);
                cfg.HudHideWhenFine = ini.GetBool("HUD", "HideWhenFine", cfg.HudHideWhenFine);
                cfg.HudFineAbove = ini.GetFloat("HUD", "FineAbove", cfg.HudFineAbove, 0f, 1f);
                cfg.HudFlashWhenCritical = ini.GetBool("HUD", "FlashWhenCritical",
                                                       cfg.HudFlashWhenCritical);

                cfg.InteractKey = ini.GetKey("Keys", "Interact", cfg.InteractKey);
                cfg.MenuKey = ini.GetKey("Keys", "Menu", cfg.MenuKey);
                cfg.BagKey = ini.GetKey("Keys", "Bag", cfg.BagKey);
                cfg.MenuPad = ini.GetBool("Keys", "MenuPad", cfg.MenuPad);
                cfg.BagPad = ini.GetBool("Keys", "BagPad", cfg.BagPad);

                var padLabel = ini.GetString("Keys", "PadInteractLabel", cfg.PadInteractLabel);
                cfg.PadInteractLabel = string.IsNullOrEmpty(padLabel) || padLabel.Trim().Length == 0
                    ? "D-PAD RIGHT"
                    : padLabel.Trim();

                cfg.DrugsInPocket = ini.GetBool("General", "DrugsInPocket", cfg.DrugsInPocket);

                cfg.BuyToPantry = ini.GetBool("Money", "BuyToPantry", cfg.BuyToPantry);
                cfg.PantrySlots = (int)ini.GetFloat("Money", "PantrySlots", cfg.PantrySlots, 1f, 200f);

                cfg.FridgeEnabled = ini.GetBool("Fridge", "Enabled", cfg.FridgeEnabled);
                cfg.FridgeSlots = (int)ini.GetFloat("Fridge", "Slots", cfg.FridgeSlots, 1f, 500f);
                cfg.FridgeReach = ini.GetFloat("Fridge", "Reach", cfg.FridgeReach, 0.5f, 6f);
                cfg.FridgeExtraModels = ini.GetString("Fridge", "ExtraModels",
                                                      cfg.FridgeExtraModels);

                cfg.VendingMachines = ini.GetBool("Counters", "VendingMachines",
                                                  cfg.VendingMachines);
                cfg.FruitStalls = ini.GetBool("Counters", "FruitStalls", cfg.FruitStalls);
                cfg.StallItems = ini.GetString("Counters", "StallItems", cfg.StallItems);
                cfg.VendingSipHunger = ini.GetFloat("Counters", "VendingSipHunger",
                                                    cfg.VendingSipHunger, 0f, 0.5f);
                cfg.MachineBlips = ini.GetBool("Counters", "MachineBlips", cfg.MachineBlips);
                cfg.DiscoverCarts = ini.GetBool("Counters", "DiscoverCarts", cfg.DiscoverCarts);
                cfg.WalkIns = ini.GetBool("Counters", "WalkIns", cfg.WalkIns);

                cfg.FoodSpinX = ini.GetFloat("Eating", "FoodSpinX", cfg.FoodSpinX, -180f, 180f);
                cfg.FoodSpinY = ini.GetFloat("Eating", "FoodSpinY", cfg.FoodSpinY, -180f, 180f);
                cfg.FoodSpinZ = ini.GetFloat("Eating", "FoodSpinZ", cfg.FoodSpinZ, -180f, 180f);

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

        /// <summary>Reads the HUD style, forgivingly. Anything unrecognised keeps the default.</summary>
        private static HudStyle ParseStyle(string text, HudStyle fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;

            var s = text.Trim();

            if (s.StartsWith("b", StringComparison.OrdinalIgnoreCase)) return HudStyle.Bars;
            if (s.StartsWith("i", StringComparison.OrdinalIgnoreCase)) return HudStyle.Icons;

            return fallback;
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
