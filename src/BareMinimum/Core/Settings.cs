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
        /// <summary>An apple and an eye that change shape. The default.</summary>
        Icons,

        /// <summary>Two filled bars, in the manner of the fuel gauge in Fumes.</summary>
        Bars
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
        public float HungerHoursToEmpty = 52f;

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
        public float SleepHoursToEmpty = 80f;

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
        /// NOT A REPLACEMENT, A CHOICE. The icons were the original requirement and they are
        /// still the default: a shape that changes says more at a glance than a length does.
        /// But a bar reads a number off at once, which the icons deliberately do not, and
        /// which some people would rather have.
        /// </summary>
        public HudStyle Style = HudStyle.Icons;

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
        public float HudBarLength = 0.184f;

        /// <summary>How WIDE a bar is, as a fraction of screen width. Fumes' figure.</summary>
        public float HudBarWidth = 0.0046f;

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
        public float HudBarWave = 0.6f;

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
        public float HudBarDrift = 0.35f;

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
        public float HudBarIconScale = 0.78f;

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
        public int PantrySlots = 3;

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

                cfg.BuyToPantry = ini.GetBool("Money", "BuyToPantry", cfg.BuyToPantry);
                cfg.PantrySlots = (int)ini.GetFloat("Money", "PantrySlots", cfg.PantrySlots, 1f, 200f);

                cfg.FridgeEnabled = ini.GetBool("Fridge", "Enabled", cfg.FridgeEnabled);
                cfg.FridgeSlots = (int)ini.GetFloat("Fridge", "Slots", cfg.FridgeSlots, 1f, 500f);
                cfg.FridgeReach = ini.GetFloat("Fridge", "Reach", cfg.FridgeReach, 0.5f, 6f);

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
