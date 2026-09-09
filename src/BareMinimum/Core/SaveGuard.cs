using System;

using BareMinimum.Core;

namespace BareMinimum.Core
{
    /// <summary>
    /// Whether a save file is safe to write over, worked out once from how it read.
    ///
    /// THE ENUM EXISTED AND NOBODY USED IT. JsonFile returns Missing and Unreadable separately
    /// precisely because they are opposites -- one is a new game and the other is a save that is
    /// sitting right there behind a lock, a half-written file or a permissions change -- and its
    /// own comment says that treating the second as the first "is how a playthrough gets
    /// overwritten by a blank one on the next autosave". Every caller then wrote
    /// `how != ReadResult.Ok`, which is exactly that mistake, four times over.
    ///
    /// It is not hypothetical. A backup tool or a virus scanner holding needs.json open for the
    /// moment SHVDN builds the script is enough: the mod starts everybody fed and rested and,
    /// ten seconds later, writes a file containing only the character you happen to be. The
    /// other two protagonists are gone, and the same goes for the pockets and the fridge.
    ///
    /// So: read, and if it could not be read, try the backup the writer already leaves. If that
    /// cannot be read either, the save is FROZEN for the session -- nothing is written, the
    /// player is told once, and the file they still have on disk is the file they keep.
    /// </summary>
    internal sealed class SaveGuard
    {
        private readonly string _what;

        public SaveGuard(string what)
        {
            _what = what;
        }

        /// <summary>Whether writing is allowed. False after an unreadable load, for the session.</summary>
        public bool MayWrite { get; private set; } = true;

        /// <summary>
        /// Reads the file, falling back to its backup, and decides whether writing stays safe.
        ///
        /// Hands back null for a new game AND for a frozen one; the caller starts at its
        /// defaults either way, and MayWrite is what stops the frozen one being saved.
        /// </summary>
        public Json Read(string path)
        {
            ReadResult how;
            var doc = JsonFile.Read(path, out how);

            if (how == ReadResult.Ok && doc != null && !doc.IsNull) return doc;

            if (how == ReadResult.Missing)
            {
                // A new game. Nothing to protect.
                return null;
            }

            var backup = JsonFile.BackupOf(path);

            ReadResult second;
            var spare = JsonFile.Read(backup, out second);

            if (second == ReadResult.Ok && spare != null && !spare.IsNull)
            {
                Log.Warn(_what + ": " + path + " could not be read, so the backup beside it was " +
                         "used instead. Saving carries on from there.");
                return spare;
            }

            MayWrite = false;

            Log.Error(_what + ": " + path + " is there and cannot be read, and neither can its " +
                      "backup. NOTHING WILL BE SAVED this session, so the file you have is kept " +
                      "rather than overwritten with a blank one. Close whatever is holding it -- " +
                      "a backup tool or a virus scanner is the usual answer -- and restart the " +
                      "scripts.");

            return null;
        }
    }
}
