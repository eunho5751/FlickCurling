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

        public static Task<List<PlayerStats>> GetStats(params string[] playerIds) =>
            Call<List<PlayerStats>>("GetStats", new Dictionary<string, object> { ["playerIds"] = playerIds });

        /// <summary>The match the signed-in player is in, as the module last noted it (protected player data), or null.</summary>
        public static async Task<string> ActiveMatchId()
        {
            try
            {
                var items = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { ActiveKey },
                    new LoadOptions(new ProtectedReadAccessClassOptions()));
                return items.TryGetValue(ActiveKey, out var item) ? item.Value.GetAs<string>() : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: reading the active match failed: " + e.Message);
                return null;
            }
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
