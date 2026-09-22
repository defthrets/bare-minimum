using System;

namespace BareMinimum.Api
{
    /// <summary>
    /// The body in his arms, asked about by another mod.
    ///
    /// THIS SURFACE MOVED HERE WITH THE CARRY, on 2026-09-22. It was Five0Patrol.Api.Bodies
    /// and it had one caller: Posted Up's loot card, which put "DRAG HIM" on its footer so the
    /// last thing you read over a corpse could also be the thing that moved it. Both halves
    /// live in this mod now and talk to each other by method call, so the surface is kept for
    /// the mods that are NOT in this repository -- and kept with the same member names, so
    /// anything written against the old one only has to change a type name.
    ///
    /// AND FIVE0 IS A CALLER NOW RATHER THAN THE OWNER. Its own killing rules need to know
    /// that a body is in somebody's arms -- a carried body must not be "found" by a passing
    /// witness, and a body brought back to life to be carried must not be swept off its books
    /// as no longer a corpse. That is what Carrying answers.
    ///
    /// ONLY BCL TYPES CROSS, and the caller reaches this by REFLECTION -- it holds no
    /// reference to this assembly and cannot name a type declared in it. A ped is an int
    /// handle here, the way it is in the natives underneath. Read the note at the top of
    /// Api.Pantry once; this follows the same rules and does not repeat them.
    ///
    /// NOTHING THROWN LEAVES THIS FILE. An exception across a reflection call surfaces at the
    /// other end wrapped in a type the caller has no way to catch by name.
    ///
    /// AND IT IS SAFE BEFORE THIS MOD HAS FINISHED STARTING. SHVDN builds scripts in whatever
    /// order it finds them, so the other side can reach this before Main has made a Drag.
    /// Everything answers "no" until it has.
    /// </summary>
    public static class Bodies
    {
        /// <summary>Bumped when the shape of this class changes in a way a caller would notice.</summary>
        /// <summary>
        /// A PROPERTY, LIKE EVERY OTHER SURFACE IN THIS FOLDER. Api.Pantry publishes this as
        /// "public static int ApiVersion => 1" and so does the drugs mod next door; this one
        /// was written as a const, which is a FIELD, and a caller reading the surface with
        /// GetProperty got null and concluded the version was 0. It then refused to use a
        /// surface that was working perfectly. Consistency here is not tidiness, it is the
        /// difference between an integration existing and not.
        /// </summary>
        public static int ApiVersion => 1;

        /// <summary>Set by Main once the carry exists. Null until then, and that is ordinary.</summary>
        internal static BareMinimum.Bodies.Drag Hands;

        /// <summary>
        /// Whoever knows who these people were, or null. Set by WireIdentity.
        ///
        /// Func and int and string[] are all BCL types, so a mod that holds no reference to
        /// this assembly can still name the delegate it is handing over.
        /// </summary>
        internal static Func<int, string[]> Identity;

        /// <summary>Whether there is anything here to ask yet.</summary>
        public static bool Ready
        {
            get
            {
                try { return Hands != null; }
                catch { return false; }
            }
        }

        /// <summary>Whether carrying is switched on at all, so a caller can hide the option.</summary>
        public static bool Enabled
        {
            get
            {
                try { return Hands != null && Hands.Allowed; }
                catch { return false; }
            }
        }

        /// <summary>Whether a body is in hand right now.</summary>
        public static bool Holding
        {
            get
            {
                try { return Hands != null && Hands.Holding; }
                catch { return false; }
            }
        }

        /// <summary>
        /// The handle of whoever is in his arms, or 0.
        ///
        /// FOR THE MOD THAT DECIDES WHEN A BODY IS FOUND. Five0 Patrol keeps a list of the
        /// people you killed that nobody saw, and it does two things with a carried one: it
        /// does not let a witness discover a body that is off the ground, and it does not
        /// forget a body that is momentarily alive. A corpse has to be brought back to life to
        /// take a clip at all -- see Bodies.Drag.Hold -- so for as long as he is held he
        /// answers IsAlive, and a sweep that dropped him for it would hand the same man a
        /// fresh set of pockets the moment he was set down.
        ///
        /// A HANDLE RATHER THAN Holding, because the caller has a list and needs to know WHICH
        /// one. Holding stays for the callers that only want the yes or no.
        /// </summary>
        public static int Carrying
        {
            get
            {
                try
                {
                    var who = Hands == null ? null : Hands.Carrying;
                    return who == null ? 0 : who.Handle;
                }
                catch
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Takes hold of that body, and says whether it took.
        ///
        /// FALSE MEANS THE OPTION SHOULD NOT HAVE BEEN OFFERED -- too far away, already holding
        /// somebody, the setting off, this mod not started. The caller is expected to check
        /// Enabled before drawing a key rather than to find out by pressing it.
        /// </summary>
        public static bool Take(int pedHandle)
        {
            try { return Hands != null && Hands.TakeByHandle(pedHandle); }
            catch { return false; }
        }

        /// <summary>Lets go of whoever is in hand. Safe when nobody is.</summary>
        public static void Drop()
        {
            try { if (Hands != null) Hands.Put(); }
            catch { /* it is let go of at teardown either way */ }
        }

        /// <summary>
        /// Lets another mod name the dead.
        /// </summary>
        ///
        /// <remarks>
        /// THIS MOD MAKES UP AN IDENTITY AND ANOTHER MOD REMEMBERS ONE. Everything on the
        /// loot card except the pockets is invented here from the handle and the model - the
        /// name, the date of birth, the height - and it is invented well enough that the same
        /// man is the same man every time you open him. What it cannot be is RIGHT, because
        /// this mod has never spoken to him.
        ///
        /// NPC Mind has. It gives every pedestrian in the city a persona and a memory, so by
        /// the time one of them is lying on the pavement it knows his name because the player
        /// was told it to his face, along with what he did for a living, who he ran with and
        /// how many times the two of them had spoken. A card that calls that man something
        /// else makes a liar of one of the two mods, and the player has no way to tell which.
        /// So the invented identity gives way to a remembered one.
        ///
        /// THE PROVIDER IS HANDED A PED HANDLE AND RETURNS FLAT PAIRS: name, born, height,
        /// ethnicity, occupation, affiliation, note, female. Anything it leaves out keeps the
        /// value this mod invented, so a provider that knows only a name is perfectly
        /// welcome. Keys are matched without case and unknown keys are ignored, so this
        /// surface can grow without breaking anybody already written against it.
        ///
        /// IT IS CALLED ONCE PER BODY, when the body is first built, and never again for that
        /// body - see Corpses.For. So the provider may be slow, and must be consistent: a
        /// name that changes between two calls is a name the player will never see change,
        /// because the second call does not happen.
        ///
        /// Passing null unwires it. Nothing thrown by the provider escapes Corpses.
        /// </remarks>
        public static void WireIdentity(Func<int, string[]> provider)
        {
            try { Identity = provider; }
            catch { }
        }

        /// <summary>Whether somebody is naming the dead for us.</summary>
        public static bool IdentityWired
        {
            get
            {
                try { return Identity != null; }
                catch { return false; }
            }
        }
    }
}
