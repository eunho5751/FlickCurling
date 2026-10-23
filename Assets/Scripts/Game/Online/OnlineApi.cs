using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models.Data.Player;
using UnityEngine;

namespace FlickCurling.Game.Online
{
    /// <summary>
    /// The calls to the Cloud Code module FlickCurlingModule (CloudCode/FlickCurlingModule, Docs/OnlineMatch.md), which
    /// keeps the match records and everyone's won/lost/drawn. Failures are logged and come back as null / false: online
    /// play goes on without the bookkeeping (until the module is deployed, for one).
    /// </summary>
    public static class OnlineApi
    {
        public const string Module = "FlickCurlingModule";
        const string ActiveKey = "activeMatch";

        public sealed class MatchInfo
        {
            public bool Exists;
            public string Status = "";        // ongoing / finished
            public string[] Players = new string[0];
            public string FirstThrower = "";
            public long Seed;
            public string Map = "";
            public string Winner = "";        // a player id or "draw"
            public string Ending = "";        // reported / abandoned / expired
        }

        public sealed class PlayerStats
        {
            public string PlayerId = "";
            public int W, L, D;
            public int P;                       // rating points
            public string LastMatch = "";       // the last match counted, and what it did to P
            public int LastDelta;
        }

        static async Task<T> Call<T>(string function, Dictionary<string, object> args) where T : class
        {
            try { return await CloudCodeService.Instance.CallModuleEndpointAsync<T>(Module, function, args); }
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: " + Module + "." + function + " failed: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// The current match's StartMatch call (it makes the match's record). The game does not wait for it, but the calls
        /// that need the record (MarkStarted, ReportResult, CancelMatch, Acknowledge) go after it.
        /// </summary>
        public static Task RecordReady = Task.CompletedTask;

        static async Task AfterRecord()
        {
            try { await RecordReady; } catch (Exception) { }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => RecordReady = Task.CompletedTask;

        /// <summary>Makes the match's record if it is not there yet (both players call it) and notes it as the caller's
        /// current match. Its settings come back too; they are the same as <see cref="LocalInfo"/>.</summary>
        public static Task<MatchInfo> StartMatch(string matchId, string opponentId, string map) =>
            Call<MatchInfo>("StartMatch", new Dictionary<string, object> { ["matchId"] = matchId, ["opponentId"] = opponentId, ["map"] = map });

        /// <param name="winner">the winner's player id, or "draw"</param>
        /// <param name="score">the two scores in the order of <see cref="MatchInfo.Players"/></param>
        public static async Task<MatchInfo> ReportResult(string matchId, string winner, int[] score)
        {
            await AfterRecord();
            return await Call<MatchInfo>("ReportResult", new Dictionary<string, object> { ["matchId"] = matchId, ["winner"] = winner, ["score"] = score });
        }

        /// <summary>This player was dropped from the match's network (or left the app in it): their loss, counted at once.
        /// The opponent's result is their own report's (both dropped: both lose).</summary>
        public static async Task<MatchInfo> ReportDropped(string matchId)
        {
            await AfterRecord();
            return await Call<MatchInfo>("ReportDropped", new Dictionary<string, object> { ["matchId"] = matchId });
        }

        public static async Task<bool> Acknowledge(string matchId)
        {
            await AfterRecord();
            try { await CloudCodeService.Instance.CallModuleEndpointAsync(Module, "Acknowledge", new Dictionary<string, object> { ["matchId"] = matchId }); return true; }
            catch (Exception e) { Debug.LogWarning("FlickCurling: " + Module + ".Acknowledge failed: " + e.Message); return false; }
        }

        /// <summary>Both players have opened the match: from now on, abandoning it counts (a match that never started is
        /// simply deleted).</summary>
        public static async Task<bool> MarkStarted(string matchId)
        {
            await AfterRecord();
            try { return await CloudCodeService.Instance.CallModuleEndpointAsync<bool>(Module, "MarkStarted", new Dictionary<string, object> { ["matchId"] = matchId }); }
            catch (Exception e) { Debug.LogWarning("FlickCurling: " + Module + ".MarkStarted failed: " + e.Message); return false; }
        }

        /// <summary>Calls off a match the opponent never arrived in (only just after it was made); true when it is off.</summary>
        public static async Task<bool> CancelMatch(string matchId)
        {
            await AfterRecord();
            try { return await CloudCodeService.Instance.CallModuleEndpointAsync<bool>(Module, "CancelMatch", new Dictionary<string, object> { ["matchId"] = matchId }); }
            catch (Exception e) { Debug.LogWarning("FlickCurling: " + Module + ".CancelMatch failed: " + e.Message); return true; }
        }

        public static Task<MatchInfo> ResolveAbandoned(string matchId) =>
            Call<MatchInfo>("ResolveAbandoned", new Dictionary<string, object> { ["matchId"] = matchId });

        public static Task<MatchInfo> GetMatch(string matchId) =>
            Call<MatchInfo>("GetMatch", new Dictionary<string, object> { ["matchId"] = matchId });

        /// <summary>Puts the signed-in player on the leaderboard if they are linked to an account (the server checks), or
        /// takes them off; whether they are on it, or null if the server could not be reached.</summary>
        public static async Task<bool?> SyncLeaderboard()
        {
            try { return await CloudCodeService.Instance.CallModuleEndpointAsync<bool>(Module, "SyncLeaderboard", new Dictionary<string, object>()); }
            catch (Exception e) { Debug.LogWarning("FlickCurling: " + Module + ".SyncLeaderboard failed: " + e.Message); return null; }
        }

        public static Task<List<PlayerStats>> GetStats(params string[] playerIds) =>
            Call<List<PlayerStats>>("GetStats", new Dictionary<string, object> { ["playerIds"] = playerIds });

        /// <summary>The signed-in player's own won / lost / drawn, read straight from their protected player data (the module
        /// writes it; nothing yet: all zero), or null if it could not be read.</summary>
        public static async Task<PlayerStats> MyStats(string playerId)
        {
            try
            {
                var items = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { StatsKey },
                    new LoadOptions(new ProtectedReadAccessClassOptions()));
                var s = items.TryGetValue(StatsKey, out var item) ? ReadStats(item.Value) : null;
                return Stats(playerId, s);
            }
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: reading the records failed: " + e.Message);
                return null;
            }
        }
        const string StatsKey = "stats";   // (as the module keeps it)

        /// <summary>
        /// On starting the game: a match the signed-in player was still in (the app closed or crashed in it) counts as left,
        /// so lost, as one dropped from it (the opponent's result is their own report's); one that never started is called
        /// off. Then it is no longer their match.
        /// </summary>
        public static async Task SettleLeftMatch()
        {
            string me = Unity.Services.Authentication.AuthenticationService.Instance.PlayerId;
            var (id, stats) = await StartupData(me);
            if (stats != null) OnlineLobby.TakeRecord(stats);   // the record for this run's tickets, read with it
            if (string.IsNullOrEmpty(id)) return;
            var info = await GetMatch(id);
            if (info == null) return;   // the server cannot be reached: left for next time
            if (info.Exists && info.Status == "ongoing")
            {
                // called off (nobody's match any more), or left: lost (no longer the player's match either)
                if (!await CancelMatch(id)) await ReportDropped(id);
                return;
            }
            await Acknowledge(id);   // over (or gone): seen
        }

        /// <summary>The match the signed-in player is in, as the module last noted it, and their record (both protected
        /// player data, read together); (null, null) if they could not be read.</summary>
        public static async Task<(string active, PlayerStats stats)> StartupData(string playerId)
        {
            try
            {
                var items = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { ActiveKey, StatsKey },
                    new LoadOptions(new ProtectedReadAccessClassOptions()));
                string active = items.TryGetValue(ActiveKey, out var a) ? a.Value.GetAs<string>() : null;
                var s = items.TryGetValue(StatsKey, out var st) ? ReadStats(st.Value) : null;
                return (active, Stats(playerId, s));
            }
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: reading the player's data failed: " + e.Message);
                return (null, null);
            }
        }

        // the record as the module stores it: it keeps more fields than the device reads (the leaderboard standing), which
        // fail the default strict reading, so those are ignored
        static PlayerStats ReadStats(Unity.Services.CloudSave.Internal.Http.IDeserializable value) =>
            value.GetAs<PlayerStats>(new Unity.Services.CloudSave.Internal.Http.DeserializationSettings
                { MissingMemberHandling = Unity.Services.CloudSave.Internal.Http.MissingMemberHandling.Ignore });

        static PlayerStats Stats(string playerId, PlayerStats s) =>
            new PlayerStats { PlayerId = playerId, W = s?.W ?? 0, L = s?.L ?? 0, D = s?.D ?? 0, P = s?.P ?? 0, LastMatch = s?.LastMatch ?? "", LastDelta = s?.LastDelta ?? 0 };

        /// <summary>The module's push as it counts this player's result: the match, the rating points and their change,
        /// and the whole record after it.</summary>
        [Serializable] public sealed class Settled
        {
            public string m = ""; public int p, d;
            public int w = -1, l = -1, dr = -1;   // (-1: a push without the record, from a module before it carried it)
            public bool HasRecord => w >= 0 && l >= 0 && dr >= 0;
        }
        public const string SettledType = "matchSettled";

        public static Settled ParseSettled(string type, string message)
        {
            if (type != SettledType || string.IsNullOrEmpty(message)) return null;
            try { var s = JsonUtility.FromJson<Settled>(message); return s == null || string.IsNullOrEmpty(s.m) ? null : s; }
            catch (Exception) { return null; }
        }

        /// <summary>What the module decides for a match (the turn order shot's first thrower, the seed), worked out here
        /// the same way, so the game starts at once. Keep it the same as OnlineMatch.StartMatch in the Cloud Code module.</summary>
        public static MatchInfo LocalInfo(string matchId, string a, string b)
        {
            var players = string.CompareOrdinal(a, b) <= 0 ? new[] { a, b } : new[] { b, a };
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(matchId));
                return new MatchInfo
                {
                    Exists = false, Status = "ongoing", Players = players, Map = "base",
                    FirstThrower = players[h[0] & 1], Seed = BitConverter.ToInt32(h, 1) & 0x7fffffff,
                };
            }
        }
    }
}
