using FlickCurling.Core;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>
    /// Tunable match rules and presentation, edited in the Inspector (Assets/Resources/GameConfig.asset). The game applies
    /// it once at start-up (before the board and stones are made), so changes take effect on the next play.
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "FlickCurling/Game Config")]
    public sealed class GameConfig : ScriptableObject
    {
        public const string ResourcePath = "GameConfig";

        [Header("Match")]
        [Tooltip("Ends in a match (an extra end follows a tie)")]
        [Range(1, 10)] public int ends = 4;
        [Tooltip("Shots per team in each end")]
        [Range(1, 10)] public int shotsPerTeam = 5;
        [Tooltip("Cushion tickets per team in each end")]
        [Range(0, 10)] public int cushionTickets = 2;
        [Tooltip("Cushions are always free: no ticket is spent and no foul, and \"쿠션권 -1\" never shows")]
        public bool unlimitedTickets = false;
        [Tooltip("Seconds a player has to throw on their turn (the shot clock)")]
        [Range(5f, 60f)] public float turnSeconds = 15;

        [Header("Stones")]
        [Tooltip("Stone radius in sheet units (the sheet is 100 wide; the house radius is 28)")]
        [Range(2f, 8f)] public float stoneRadius = 4;

        [Header("Shot gauge")]
        [Tooltip("Show the power percentage beside the aim arrow while pulling")]
        public bool showPowerPercent = true;
        [Tooltip("Show a ring where the aim line first meets a stone, post or wall (the no-ticket warning on a wall always shows)")]
        public bool showHitMarker = true;

        [Header("Turn order shot")]
        [Tooltip("Radius of every bumper, the one in front of the house included")]
        [Range(2f, 8f)] public float lsdBumperRadius = 4;
        [Tooltip("Bumpers in each half (left and right), besides the one in front of the house")]
        [Range(0, 4)] public int lsdBumpersPerHalf = 2;

        public void Apply() => Sheet.Configure(ends, shotsPerTeam, stoneRadius, lsdBumperRadius, lsdBumpersPerHalf, cushionTickets, unlimitedTickets);

        /// <summary>The project's config (Resources/GameConfig), or the defaults if it is missing.</summary>
        public static GameConfig Load()
        {
            var c = Resources.Load<GameConfig>(ResourcePath);
            return c != null ? c : CreateInstance<GameConfig>();
        }
    }
}
