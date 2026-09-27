using System;
using System.Diagnostics;
using GTA;

namespace BareMinimum.Core
{
    /// <summary>
    /// How much memory the game is holding, and how much of it belongs to the script mods.
    ///
    /// WRITTEN FOR ONE REPORT AND KEPT FOR THE NEXT. On 2026-09-27 the minimap and the pause
    /// map lost their picture partway through a session -- the blips drew, the map under them
    /// did not -- and pressing Insert did not bring it back. A log of the machine taken that
    /// afternoon showed the game climbing from 6 GB to 18 GB of committed memory in two hours
    /// on a PC with 16 GB of RAM, and to 22 GB in the session that lost its map. The map is
    /// streamed in as you move; with the game paged out to disk, it is the first thing that
    /// fails to arrive.
    ///
    /// SO THIS SPLITS THE CLIMB IN TWO. Every SHVDN script on this machine shares one .NET
    /// heap, and GC.GetTotalMemory is the size of it; the process's private bytes are
    /// everything. If the heap climbs with the total, a script mod is holding on to things.
    /// If the heap stays small while the total climbs, the leak is native -- the game itself,
    /// an ASI, or something a script asked the game to make and never let go of.
    ///
    /// DIAGNOSTIC ONLY. It reads two numbers once a minute and writes a line only when one of
    /// them has moved by a quarter of a gigabyte, so a steady session writes nothing at all.
    /// </summary>
    internal static class Memory
    {
        private const int EveryMs = 60000;
        private const long Step = 256L * 1024 * 1024;

        private static int _at;
        private static long _saidHeap = -1, _saidPrivate = -1;
        private static int _startedAt;

        public static void Tick()
        {
            int now;
            try { now = Game.GameTime; }
            catch { return; }

            if (_startedAt == 0) _startedAt = now;
            if (now - _at < EveryMs) return;
            _at = now;

            try
            {
                var heap = GC.GetTotalMemory(false);

                long priv;
                using (var me = Process.GetCurrentProcess()) priv = me.PrivateMemorySize64;

                if (_saidHeap >= 0 && Math.Abs(heap - _saidHeap) < Step &&
                    Math.Abs(priv - _saidPrivate) < Step)
                {
                    return;
                }

                _saidHeap = heap;
                _saidPrivate = priv;

                Log.Info("Memory: the game holds " + Gb(priv) + " GB; the script mods' shared .NET heap is " +
                         Mb(heap) + " MB (" + ((now - _startedAt) / 60000) + " min into this load).");
            }
            catch (Exception ex)
            {
                Log.Once("memory-read", "Could not read the game's memory use: " + ex.Message);
            }
        }

        private static string Gb(long bytes) => (bytes / 1073741824.0).ToString("0.00");
        private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0");
    }
}
