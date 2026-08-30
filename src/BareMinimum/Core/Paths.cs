using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BareMinimum.Core
{
    /// <summary>
    /// Resolves where Bare Minimum reads and writes.
    ///
    /// The lesson behind this cost days in Hoodrich and was paid for again in Fumes: SHVDN
    /// SHADOW-COPIES script assemblies into the .NET download cache, so
    /// Assembly.Location points at AppData\Local\assembly\dl3\... and not at scripts\.
    /// Anything hung off it silently "does not exist" -- the mod runs on built-in defaults
    /// forever, with no exception and no log line saying so.
    ///
    /// So no single path API is trusted. Several candidates are tested against files we know
    /// we shipped, and the first that actually holds them wins.
    /// </summary>
    internal static class Paths
    {
        /// <summary>
        /// The folder name under scripts\, and the stem of the dll, ini and log.
        ///
        /// NOT Build.Name -- that is "Bare Minimum", with a space, and a space in a path is a
        /// bug waiting for the first person who types it into a batch file. The two are
        /// deliberately separate; see Build.
        /// </summary>
        public const string Stem = "BareMinimum";

        private static string _scripts;

        /// <summary>The game's scripts\ folder.</summary>
        public static string Scripts
        {
            get
            {
                if (_scripts != null) return _scripts;

                var candidates = new List<string>();

                // SHVDN builds its script AppDomain with the scripts folder as the base.
                TryAdd(candidates, SafeGet(() => AppDomain.CurrentDomain.BaseDirectory));

                var cwd = SafeGet(Directory.GetCurrentDirectory);
                if (!string.IsNullOrEmpty(cwd))
                {
                    TryAdd(candidates, Path.Combine(cwd, "scripts"));
                    TryAdd(candidates, cwd);
                }

                // Last resort, and only because an unshadowed load would still be correct.
                TryAdd(candidates, SafeGet(() =>
                {
                    var loc = Assembly.GetExecutingAssembly().Location;
                    return string.IsNullOrEmpty(loc) ? null : Path.GetDirectoryName(loc);
                }));

                foreach (var dir in candidates)
                {
                    if (LooksLikeOurFolder(dir)) { _scripts = dir; return _scripts; }
                }

                _scripts = candidates.Count > 0 ? candidates[0] : cwd ?? ".";
                return _scripts;
            }
        }

        /// <summary>
        /// True when this folder holds the files the deploy puts down.
        ///
        /// Tested against foods.json rather than against the icons: the icon folder is
        /// created on demand by the Icons property below, so an empty scripts\BareMinimum\
        /// left behind by a half-finished install would answer yes and win the vote.
        /// </summary>
        private static bool LooksLikeOurFolder(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                if (File.Exists(Path.Combine(dir, Stem + ".ini"))) return true;

                var data = Path.Combine(dir, Stem);
                return Directory.Exists(data) && File.Exists(Path.Combine(data, "foods.json"));
            }
            catch
            {
                return false;
            }
        }

        private static void TryAdd(List<string> list, string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;

            try
            {
                dir = Path.GetFullPath(dir.TrimEnd(Path.DirectorySeparatorChar));
                if (Directory.Exists(dir) && !list.Contains(dir)) list.Add(dir);
            }
            catch
            {
                // Unusable path; skip it.
            }
        }

        private static string SafeGet(Func<string> get)
        {
            try { return get(); }
            catch { return null; }
        }

        /// <summary>scripts\BareMinimum\ -- the shipped data files.</summary>
        public static string Data
        {
            get
            {
                var d = Path.Combine(Scripts, Stem);
                EnsureDir(d);
                return d;
            }
        }

        private static string _writable;

        /// <summary>
        /// Where the log and the saved needs go.
        ///
        /// The game normally lives under Program Files, which an unelevated process cannot
        /// write to -- and GTA5.exe is unelevated. Reads work, so the shipped data loads
        /// fine, while every write fails silently: no log AND no saved state. Fall back to
        /// Documents the moment the game folder proves unwritable, rather than asking anybody
        /// to run their game as administrator.
        /// </summary>
        public static string Writable
        {
            get
            {
                if (_writable != null) return _writable;

                var preferred = Path.Combine(Scripts, Stem);
                if (IsWritable(preferred))
                {
                    _writable = preferred;
                    return _writable;
                }

                var fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Stem);

                try
                {
                    if (!Directory.Exists(fallback)) Directory.CreateDirectory(fallback);
                }
                catch
                {
                    fallback = Path.Combine(Path.GetTempPath(), Stem);
                    try { if (!Directory.Exists(fallback)) Directory.CreateDirectory(fallback); }
                    catch { /* nothing left to try */ }
                }

                _writable = fallback;
                return _writable;
            }
        }

        /// <summary>True when a real file can actually be created here.</summary>
        private static bool IsWritable(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var probe = Path.Combine(dir, "." + Stem + "_write_test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string Ini => Path.Combine(Scripts, Stem + ".ini");

        // ---- shipped content -------------------------------------------------

        /// <summary>Everything edible: what it costs, and what it does to hunger.</summary>
        public static string FoodsFile => Path.Combine(Data, "foods.json");

        /// <summary>
        /// Shipped artwork, beside the data rather than in the writable folder.
        ///
        /// It is CONTENT: it ships with the mod and nothing writes here. A missing folder
        /// just means the HUD has no pictures in it, which the icons report once and then
        /// carry on without.
        /// </summary>
        public static string Icons
        {
            get
            {
                var d = Path.Combine(Data, "icons");
                EnsureDir(d);
                return d;
            }
        }

        // ---- written ---------------------------------------------------------
        // These follow the writability fallback rather than sitting next to the dll. Anything
        // added here that the mod WRITES must also go on the deploy keep-list in build.ps1,
        // in the same change, or the next deploy destroys it and nothing says why.

        public static string LogFile => Path.Combine(Writable, Stem + ".log");

        /// <summary>How hungry and how tired, across sessions. The player's save.</summary>
        public static string StateFile => Path.Combine(Writable, "needs.json");

        private static void EnsureDir(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                Log.Error("Could not create directory " + path, ex);
            }
        }
    }
}
