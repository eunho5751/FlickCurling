using FlickCurling.Core;

namespace FlickCurling.Game
{
    /// <summary>
    /// A match picked in the main menu, handed to the Game scene: on loading, the game starts it at once instead of
    /// showing its own setup card. Taken once.
    /// </summary>
    public static class MatchLaunch
    {
        public const string GameScene = "Game";

        static bool pending;
        static bool vsAi;
        static AiLevel level;
        static string map = "base";

        /// <summary>Queues a match: against the AI at <paramref name="aiLevel"/>, or two players on one device.</summary>
        public static void Set(bool againstAi, AiLevel aiLevel, string mapKey)
        {
            pending = true; vsAi = againstAi; level = aiLevel; map = mapKey;
        }

        public static bool Take(out bool againstAi, out AiLevel aiLevel, out string mapKey)
        {
            againstAi = vsAi; aiLevel = level; mapKey = map;
            if (!pending) return false;
            pending = false;
            return true;
        }
    }
}
