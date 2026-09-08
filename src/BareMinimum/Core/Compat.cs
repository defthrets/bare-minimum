using System;
using GTA;
using GTA.Native;

namespace BareMinimum.Core
{
    /// <summary>
    /// The conveniences newer ScriptHookVDotNet grew, done through the natives underneath, so
    /// the mod still loads on 3.6.0.
    /// </summary>
    ///
    /// <remarks>
    /// THE COST OF A NEWER API IS NOT A MISSING FEATURE. SHVDN resolves a script's references
    /// in its own AppDomain and declines one newer than itself, so a dll compiled against 3.9
    /// does not load on 3.6 or on a 3.7 nightly AT ALL -- "Could not load file or assembly",
    /// and nothing in the mod runs. One convenient property costs the whole thing.
    ///
    /// So this file is small on purpose and everything in it is a native that has had the same
    /// name since 2013. Two members, and each replaces something that reads better and works
    /// on fewer machines:
    ///
    ///   Notification.PostTicker  -- arrived after 3.6.0. Eight call sites.
    ///   Player.Wanted            -- arrived after 3.6.0, replacing Player.WantedLevel, which
    ///                               is deprecated on 3.9. Both move; the native does not.
    ///
    /// The clock went the same way and was big enough to want its own file. See Core.Clock.
    /// </remarks>
    internal static class Compat
    {
        /// <summary>
        /// A line in the feed above the minimap. What Notification.PostTicker did.
        /// </summary>
        ///
        /// <remarks>
        /// Everything that called it passed the same two falses -- do not blink, no brief --
        /// so they are not parameters here. A flag nobody sets is a flag that goes wrong
        /// silently the first time somebody does.
        /// </remarks>
        public static void Ticker(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");

                // The same 99-character ceiling every text command has. A line longer than
                // that draws NOTHING at all rather than being cut, which is the worst way to
                // lose a message.
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,
                              message.Length > 99 ? message.Substring(0, 99) : message);

                Function.Call<int>(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, false);
            }
            catch (Exception ex)
            {
                Log.Once("ticker", "Could not post to the feed: " + ex.Message);
            }
        }

        /// <summary>
        /// His wanted level, 0-5.
        ///
        /// FIVE WHEN IT CANNOT BE READ, not zero. Everything asking is deciding whether he is
        /// allowed to lie down, and the honest failure is "not now" -- being unable to sleep is
        /// a nuisance, and going to sleep in front of a helicopter is the bug.
        /// </summary>
        public static int Wanted
        {
            get
            {
                try { return Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player); }
                catch { return 5; }
            }
        }

        /// <summary>
        /// Gives him a wanted level, and makes it stick this frame.
        /// </summary>
        ///
        /// <remarks>
        /// THE SECOND CALL IS NOT OPTIONAL. SET_PLAYER_WANTED_LEVEL only arms the level;
        /// without SET_PLAYER_WANTED_LEVEL_NOW it does not apply until the game next feels
        /// like it, which for a man being mugged in an alley is too late to matter. The SHVDN
        /// setter this replaces made both calls, and that is the whole reason it existed.
        /// </remarks>
        public static void SetWanted(int level)
        {
            try
            {
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, Game.Player, level, false);
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player, false);
            }
            catch (Exception ex)
            {
                Log.Once("wanted", "Could not set the wanted level: " + ex.Message);
            }
        }
    }
}
