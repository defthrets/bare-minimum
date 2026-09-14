using System;

namespace BareMinimum.Api
{
    /// <summary>
    /// Storage for another mod's containers. See Food.Vault for what it is and is not.
    ///
    /// THE SAME RULES AS Api.Pantry NEXT DOOR, and for the same reasons: only BCL types cross,
    /// nothing thrown leaves this file, and every method answers "nothing" rather than blowing
    /// up when it is called before this mod has finished starting. Read the note at the top of
    /// Pantry once; it is the whole contract and it is not repeated here.
    ///
    /// WHAT THIS IS FOR. Hoodrich keeps a jacket pocket, a bag you can drop in the street, a
    /// car boot and a stash house. Each of those was its own store in its own save file, which
    /// is two mods on one machine each holding their own idea of what the player is carrying --
    /// and they disagree the first time either is reloaded. The containers live here now.
    ///
    /// AND THIS SIDE DOES NOT KNOW WHAT IS IN THEM. A container is a name and a string; the
    /// string is the owner's own format, carried and handed back byte for byte. Grams, purity,
    /// packaged or bulk are that mod's vocabulary and there is no reason for this one to learn
    /// it -- which is also what stops this rotting the day they add a field.
    /// </summary>
    public static class Vault
    {
        /// <summary>
        /// Bumped when the shape of THIS class changes in a way a caller would notice.
        ///
        /// Separate from Pantry's: they are two surfaces and a mod may want one and not the
        /// other. A caller that wants both asks both.
        /// </summary>
        public const int ApiVersion = 1;

        /// <summary>Set by Main once the vault exists. Null until then, and that is ordinary.</summary>
        internal static Food.Vault Held;

        /// <summary>Whether there is anywhere to put anything yet.</summary>
        public static bool Ready
        {
            get { return Held != null; }
        }

        /// <summary>What is in that container, or empty for one nobody has filled.</summary>
        public static string Get(string name)
        {
            try { return Held == null ? "" : Held.Get(name); }
            catch { return ""; }
        }

        /// <summary>Whether anything has ever been put in that container.</summary>
        public static bool Has(string name)
        {
            try { return Held != null && Held.Has(name); }
            catch { return false; }
        }

        /// <summary>
        /// Puts a container's contents in, and says whether it took.
        ///
        /// FALSE MEANS KEEP YOUR OWN COPY. It is false before this mod has started, and false
        /// for a caller that has gone past the size or the count -- both of which are the
        /// caller's cue to fall back to storing it themselves rather than to lose it. Empty
        /// removes the container, which is how a bag gets thrown away.
        /// </summary>
        public static bool Put(string name, string what)
        {
            try { return Held != null && Held.Put(name, what); }
            catch { return false; }
        }

        /// <summary>Every container there is. Never null.</summary>
        public static string[] Names()
        {
            try { return Held == null ? new string[0] : Held.Names(); }
            catch { return new string[0]; }
        }

        /// <summary>Writes it out now, for a caller that has just done something it cannot lose.</summary>
        public static void Flush()
        {
            try { if (Held != null) Held.Save(); }
            catch { /* it goes out on the timer, or at shutdown */ }
        }
    }
}
