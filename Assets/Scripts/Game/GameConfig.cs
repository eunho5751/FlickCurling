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

        [Header("Online")]
        [Tooltip("How much faster a device plays the opponent's shot while it is behind theirs (the shot came late), until level")]
        [Range(1.05f, 2f)] public float catchUpSpeed = 1.15f;
        [Tooltip("Seconds from the moment a match is found for the two devices to be connected (both give up together past it; the service can take half a minute and more to start the match's network)")]
        [Range(15f, 120f)] public float connectSeconds = 45;
        [Tooltip("Seconds to wait, once a match is found, for the opponent to show up in the match's session (normally 1 to 3); past it the match is taken as called off on their side and the search goes on")]
        [Range(2f, 30f)] public float opponentJoinSeconds = 10;
        [Tooltip("Minutes a friend's room stays open waiting for the friend to come in with its code; then it closes")]
        [Range(1f, 30f)] public float friendRoomMinutes = 10;

        [Header("Stones")]
        [Tooltip("Stone radius in sheet units (the sheet is 100 wide; the house radius is 28)")]
        [Range(2f, 8f)] public float stoneRadius = 4;
        [Tooltip("How bouncy stones are on each other: the share of the speed they meet with that comes back (1: a head-on hit stops the thrown stone dead and the hit one takes it all; 0: they go on together)")]
        [Range(0f, 1f)] public float stoneBounce = 0.9f;
        [Tooltip("How bouncy the side walls are: the share of the speed into the wall that comes back (1: a mirror bounce, nothing lost; lower: flatter and slower off the wall). The dead rails beside the out line are not this")]
        [Range(0f, 1f)] public float wallBounce = 0.75f;
        [Tooltip("Show the gold ring round the stones that would score now (those closest to the centre), during play")]
        public bool showScoringGlow = true;

        [Header("Shot gauge")]
        [Tooltip("Show the power percentage beside the aim arrow while pulling")]
        public bool showPowerPercent = true;
        [Tooltip("On: the aim line stops where it first meets a stone, post or wall. Off: it runs through stones and posts to a wall or the far end. Its ring gauge sits at the end either way")]
        public bool showHitMarker = true;
        [Tooltip("Radius, in sheet units, of the power gauge ring round the launch stone (the stone's radius is 4 by default)")]
        [Range(2f, 20f)] public float launchRingRadius = 8;
        [Tooltip("Width, in screen points, of that ring's fill (its dark backing and track scale with it)")]
        [Range(1f, 12f)] public float launchRingWidth = 4.5f;
        [Tooltip("Radius, in sheet units, of the power gauge ring where the aim line ends")]
        [Range(2f, 20f)] public float hitRingRadius = 4;
        [Tooltip("Width, in screen points, of that ring's fill")]
        [Range(1f, 12f)] public float hitRingWidth = 3;

        [Header("Turn order shot")]
        [Tooltip("The first end's order by a coin toss (the side the coin shows goes second) instead of the turn order shot thrown at a target. Online, both players' builds must agree")]
        public bool coinToss = true;
        [Tooltip("Where the target's centre may be put, across the sheet: from (x) .. to (y), 0 the left rail, 1 the right")]
        public Vector2 lsdTargetX = new Vector2(0.15f, 0.85f);
        [Tooltip("Where the target's centre may be put, up the sheet: from (x) .. to (y), 0 the min line, 1 the out line")]
        public Vector2 lsdTargetY = new Vector2(0.15f, 0.93f);

        [Header("Item mode")]
        [Tooltip("Each side's item boxes on the sheet as an end starts (the ones left from the end before count)")]
        [Range(0, 6)] public int itemBoxes = 2;
        [Tooltip("An item box's half size, in sheet units (a stone's radius is 4)")]
        [Range(1.5f, 6f)] public float itemBoxSize = 3.2f;
        [Tooltip("How far apart the boxes are put, centre to centre, in sheet units, where there is room (otherwise as far apart as they can be); a box goes anywhere else in its area at random")]
        [Range(0f, 60f)] public float itemBoxSpacing = 25;
        [Tooltip("Where a box's centre may be put, across the sheet: from (x) .. to (y), 0 the left rail, 1 the right")]
        public Vector2 itemBoxX = new Vector2(0f, 1f);
        [Tooltip("Where a box's centre may be put, up the sheet: from (x) .. to (y), 0 the min line, 1 the house's centre")]
        public Vector2 itemBoxY = new Vector2(0f, 1f);
        [Tooltip("A giant stone's radius against a stone's")]
        [Range(1f, 2.5f)] public float giantSize = 1.5f;
        [Tooltip("A mini stone's radius against a stone's")]
        [Range(0.3f, 1f)] public float miniSize = 0.6f;
        [Tooltip("How far a bomb reaches from its centre, in sheet units (the house's radius is 28); it goes off when it hits another stone")]
        [Range(5f, 60f)] public float bombRadius = 20;
        [Tooltip("The speed a bomb gives a stone right beside it (less further off; a full throw is 197)")]
        [Range(10f, 200f)] public float bombPush = 80;
        [Tooltip("How far a magnet pulls from its centre, in sheet units, while it moves")]
        [Range(5f, 60f)] public float magnetRadius = 20;
        [Tooltip("A magnet's grip on a stone right beside it: how quickly that stone takes the magnet's way, per second (less further off)")]
        [Range(1f, 40f)] public float magnetPull = 8;
        [Tooltip("A box's chance of giving an item its taker already holds, against any other item's (1: the same as any other, 0.5: half, 0: never)")]
        [Range(0f, 1f)] public float heldItemChance = 0.5f;

        [Header("Ads")]
        [Tooltip("A full-screen ad once every this many matches played (against the AI, two on one device or online; not the tutorial), as the result is left. Never for a player who bought \"remove ads\"")]
        [Range(1, 10)] public int adEveryMatches = 2;
        [Tooltip("The AdMob interstitial ad unit ID for Android (ca-app-pub-…/…). The default is Google's test unit: put the game's own in before release")]
        public string adUnitAndroid = "ca-app-pub-3940256099942544/1033173712";

        [Header("Links")]
        [Tooltip("The web page the settings popup's \"Terms of Service\" opens")]
        public string termsUrl = "";
        [Tooltip("The web page the settings popup's \"Privacy Policy\" opens")]
        public string privacyUrl = "";

        public void Apply()
        {
            Sheet.Configure(ends, shotsPerTeam, stoneRadius, cushionTickets, unlimitedTickets);
            PhysicsParams.Configure(stoneBounce, wallBounce);
            // (0..1 across the sheet, and from the min line to the out line, in sheet units)
            double X(float v) => v * Sheet.W;
            double Y(float v) => Sheet.Hog + v * (Sheet.Back - Sheet.Hog);
            Sheet.ConfigureLsdTarget(X(lsdTargetX.x), X(lsdTargetX.y), Y(lsdTargetY.x), Y(lsdTargetY.y));
            // (0..1 from the min line to the house's centre)
            double BoxY(float v) => Sheet.Hog + v * (Sheet.TeeY - Sheet.Hog);
            Sheet.ConfigureItems(itemBoxes, itemBoxSize, X(itemBoxX.x), X(itemBoxX.y), BoxY(itemBoxY.x), BoxY(itemBoxY.y), giantSize, miniSize, bombRadius, bombPush, magnetRadius, magnetPull, itemBoxSpacing, heldItemChance);
        }

        /// <summary>The project's config (Resources/GameConfig), or the defaults if it is missing.</summary>
        public static GameConfig Load()
        {
            var c = Resources.Load<GameConfig>(ResourcePath);
            return c != null ? c : CreateInstance<GameConfig>();
        }
    }
}
