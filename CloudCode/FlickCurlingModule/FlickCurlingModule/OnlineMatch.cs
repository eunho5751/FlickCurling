using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace FlickCurlingModule;

/// <summary>
/// Online match bookkeeping (Docs/OnlineMatch.md): a match's record in the private Game Data item "matches" (one key
/// per match id), each player's current match id and won/lost/drawn in their protected data (they can read it, only
/// this module writes it), and integrity counters in their private data. The first result report settles a match; a
/// second one is only compared. A player dropped from the match's network loses at once (ReportDropped); the other's
/// result waits for their own report (both dropped: both lose). Records go once both players have acknowledged, or in
/// the daily clean-up. Every counted result also moves the player's rating points (a win +10, a draw 0, a loss -5, never
/// below 0), which a player linked to an account (not a guest) has on the "rating" leaderboard: only this module writes
/// it (players are denied writing to leaderboards by the project's access policy).
/// </summary>
public class OnlineMatch
{
    const string MatchesId = "matches";          // private custom item: key = match id
    const string ActiveKey = "activeMatch";      // protected player data
    const string StatsKey = "stats";             // protected player data
    const string IntegrityKey = "integrity";     // private player data
    static readonly TimeSpan FinishedKeep = TimeSpan.FromDays(7);
    static readonly TimeSpan OngoingLimit = TimeSpan.FromHours(2);

    readonly IGameApiClient api;
    readonly ILogger<OnlineMatch> log;

    public OnlineMatch(IGameApiClient api, ILogger<OnlineMatch> log)
    {
        this.api = api;
        this.log = log;
    }

    // ---------------- records ----------------

    public class Report
    {
        public string Winner { get; set; } = "";     // a player id, or "draw"
        public int[] Score { get; set; } = new int[2];  // in the order of Players
        public long At { get; set; }
    }

    public class MatchRecord
    {
        public string[] Players { get; set; } = Array.Empty<string>();
        public string FirstThrower { get; set; } = "";
        public long Seed { get; set; }
        public string Map { get; set; } = "base";
        public long StartedAt { get; set; }
        public string Status { get; set; } = "ongoing";   // ongoing / finished
        public bool Started { get; set; }                  // both players opened the match (MarkStarted)
        public string Ending { get; set; } = "";           // reported / dropped / abandoned / expired
        public Report? Result { get; set; }
        public string SettledBy { get; set; } = "";
        public long FinishedAt { get; set; }
        public Dictionary<string, Report> Reports { get; set; } = new();
        public bool Mismatch { get; set; }
        public List<string> Acks { get; set; } = new();
        public List<string> Dropped { get; set; } = new();   // players who reported being dropped from the match
        public List<string> Counted { get; set; } = new();   // players whose won/lost/drawn has this match in it
    }

    public class Stats
    {
        public int W { get; set; }
        public int L { get; set; }
        public int D { get; set; }
        public int P { get; set; }                    // rating points
        public bool Ranked { get; set; }              // linked to an account: on the leaderboard (SyncLeaderboard)
        public string LastMatch { get; set; } = "";   // the last match counted, and what it did to P
        public int LastDelta { get; set; }
    }

    enum Outcome { Win, Draw, Loss }
    const int WinPoints = 10, LossPoints = 5;
    const string LeaderboardId = "rating";
    const string SettledMessage = "matchSettled";   // push to a player whose result was just counted

    public class Integrity
    {
        public int Mismatch { get; set; }
        public int FirstReports { get; set; }
    }

    public class MatchInfo
    {
        public bool Exists { get; set; }
        public string Status { get; set; } = "";
        public string[] Players { get; set; } = Array.Empty<string>();
        public string FirstThrower { get; set; } = "";
        public long Seed { get; set; }
        public string Map { get; set; } = "";
        public string Winner { get; set; } = "";
        public string Ending { get; set; } = "";
    }

    public class PlayerStats
    {
        public string PlayerId { get; set; } = "";
        public int W { get; set; }
        public int L { get; set; }
        public int D { get; set; }
        public int P { get; set; }
    }

    // ---------------- functions ----------------

    /// <summary>
    /// Both players call this once matched. Makes the match's record if it is not there yet (the turn order shot's
    /// first thrower and the random seed come from the match id, so both calls agree) and notes the match as the
    /// caller's current one.
    /// </summary>
    [CloudCodeFunction("StartMatch")]
    public async Task<MatchInfo> StartMatch(IExecutionContext ctx, string matchId, string opponentId, string map)
    {
        string me = Require(ctx.PlayerId, "player");
        Require(matchId, "matchId"); Require(opponentId, "opponentId");
        if (opponentId == me) throw new Exception("opponent is the caller");

        var (rec, _) = await LoadMatch(ctx, matchId);
        if (rec == null)
        {
            var players = new[] { me, opponentId }.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(matchId));
            rec = new MatchRecord
            {
                Players = players,
                FirstThrower = players[h[0] & 1],
                Seed = BitConverter.ToInt32(h, 1) & 0x7fffffff,
                Map = string.IsNullOrEmpty(map) ? "base" : map,
                StartedAt = Now(),
            };
            await SaveMatch(ctx, matchId, rec, null);
        }
        else if (!rec.Players.Contains(me)) throw new Exception("not a player of this match");

        await SetProtected(ctx, me, ActiveKey, matchId);
        return Info(rec);
    }

    /// <summary>
    /// A player still in the match reports how it ended, the moment it ends. The first report settles it and counts
    /// it for both players; a later one is compared and only noted when it differs.
    /// </summary>
    [CloudCodeFunction("ReportResult")]
    public async Task<MatchInfo> ReportResult(IExecutionContext ctx, IPushClient push, string matchId, string winner, int[] score)
    {
        string me = Require(ctx.PlayerId, "player");
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var (rec, lockId) = await LoadMatch(ctx, matchId);
            if (rec == null) throw new Exception("no such match");
            if (!rec.Players.Contains(me)) throw new Exception("not a player of this match");
            if (winner != "draw" && !rec.Players.Contains(winner)) throw new Exception("bad winner");
            if (score == null || score.Length != 2 || score.Any(s => s < 0 || s > 99)) throw new Exception("bad score");
            if (rec.Reports.ContainsKey(me)) return Info(rec);   // the same player again (a retry)

            var report = new Report { Winner = winner, Score = score, At = Now() };
            rec.Reports[me] = report;
            bool settles = rec.Status == "ongoing";
            bool differs = false;
            var uncounted = new List<string>();
            if (settles)
            {
                rec.Status = "finished"; rec.Ending = "reported"; rec.Result = report; rec.SettledBy = me; rec.FinishedAt = Now();
                uncounted = MarkCounted(rec, rec.Players);   // (one who reported being dropped has their loss already)
            }
            else if (rec.Result != null && (rec.Result.Winner != winner || !rec.Result.Score.SequenceEqual(score)))
            {
                rec.Mismatch = differs = true;
            }
            if (!await TrySaveMatch(ctx, matchId, rec, lockId)) continue;   // someone else wrote meanwhile: read again

            if (settles)
            {
                await Count(ctx, push, matchId, uncounted, winner);
                await BumpIntegrity(ctx, me, i => i.FirstReports++);
            }
            if (differs)
            {
                log.LogWarning("match {MatchId}: {Player} reported {Winner} {Score}, settled as {Settled}", matchId, me, winner, string.Join(":", score), rec.Result?.Winner);
                await BumpIntegrity(ctx, me, i => i.Mismatch++);
            }
            return Info(rec);
        }
        throw new Exception("could not save the report");
    }

    /// <summary>
    /// The caller was dropped from the match's network (or closed the app in it): they have lost, counted at once. The
    /// opponent's result is their own report's (still connected, they report a win) or, if they were dropped as well,
    /// a loss: when both have reported being dropped the match is over with no winner (nobody reporting: the daily
    /// clean-up). A match already settled stays as it is.
    /// </summary>
    [CloudCodeFunction("ReportDropped")]
    public async Task<MatchInfo> ReportDropped(IExecutionContext ctx, IPushClient push, string matchId)
    {
        string me = Require(ctx.PlayerId, "player");
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var (rec, lockId) = await LoadMatch(ctx, matchId);
            if (rec == null) throw new Exception("no such match");
            if (!rec.Players.Contains(me)) throw new Exception("not a player of this match");
            if (rec.Status != "ongoing" || rec.Dropped.Contains(me)) return Info(rec);   // settled already, or a retry

            rec.Dropped.Add(me);
            var uncounted = MarkCounted(rec, new[] { me });
            if (rec.Players.All(rec.Dropped.Contains))
            {
                rec.Status = "finished"; rec.Ending = "dropped"; rec.SettledBy = me; rec.FinishedAt = Now();
            }
            if (!await TrySaveMatch(ctx, matchId, rec, lockId)) continue;   // someone else wrote meanwhile: read again
            foreach (var p in uncounted) await UpdateStats(ctx, push, p, Outcome.Loss, matchId);
            return Info(rec);
        }
        throw new Exception("could not save the report");
    }

    /// <summary>The caller has seen how the match ended (or found it gone): it is no longer their current match. The
    /// record goes once both players have acknowledged.</summary>
    [CloudCodeFunction("Acknowledge")]
    public async Task Acknowledge(IExecutionContext ctx, string matchId)
    {
        string me = Require(ctx.PlayerId, "player");
        await ClearActive(ctx, me, matchId);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var (rec, lockId) = await LoadMatch(ctx, matchId);
            if (rec == null || !rec.Players.Contains(me) || rec.Status != "finished") return;
            if (!rec.Acks.Contains(me)) rec.Acks.Add(me);
            if (rec.Players.All(rec.Acks.Contains))
            {
                await DeleteMatch(ctx, matchId, lockId);
                return;
            }
            if (await TrySaveMatch(ctx, matchId, rec, lockId)) return;
        }
    }

    /// <summary>
    /// Both players have opened the match (each device calls this once it hears the other has arrived). A match that
    /// never started is deleted rather than counted when it is found abandoned.
    /// </summary>
    [CloudCodeFunction("MarkStarted")]
    public async Task<bool> MarkStarted(IExecutionContext ctx, string matchId)
    {
        string me = Require(ctx.PlayerId, "player");
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var (rec, lockId) = await LoadMatch(ctx, matchId);
            if (rec == null) return false;
            if (!rec.Players.Contains(me)) throw new Exception("not a player of this match");
            if (rec.Started || rec.Status != "ongoing") return true;
            rec.Started = true;
            if (await TrySaveMatch(ctx, matchId, rec, lockId)) return true;
        }
        return false;
    }

    // a match that never started goes without a trace: no record, nobody's current match
    async Task Drop(IExecutionContext ctx, string matchId, MatchRecord rec, string? lockId)
    {
        await DeleteMatch(ctx, matchId, lockId);
        foreach (var p in rec.Players) await ClearActive(ctx, p, matchId);
    }

    /// <summary>
    /// The opponent never arrived (Docs/OnlineMatch.md 6, "매칭 직후, 시작 전"): the match is called off with no
    /// record for anyone. Only before it started and shortly after it was made, so a match under way cannot be
    /// dropped this way.
    /// </summary>
    [CloudCodeFunction("CancelMatch")]
    public async Task<bool> CancelMatch(IExecutionContext ctx, string matchId)
    {
        string me = Require(ctx.PlayerId, "player");
        var (rec, lockId) = await LoadMatch(ctx, matchId);
        if (rec == null) { await ClearActive(ctx, me, matchId); return true; }
        if (!rec.Players.Contains(me)) throw new Exception("not a player of this match");
        if (rec.Status != "ongoing" || rec.Started || Now() - rec.StartedAt > (long)CancelWindow.TotalMilliseconds) return false;
        await Drop(ctx, matchId, rec, lockId);
        return true;
    }
    static readonly TimeSpan CancelWindow = TimeSpan.FromSeconds(90);

    /// <summary>The caller came back and the match's session is gone with the match still open: both players left, so
    /// both lose. A match that never started is deleted instead (nobody loses).</summary>
    [CloudCodeFunction("ResolveAbandoned")]
    public async Task<MatchInfo> ResolveAbandoned(IExecutionContext ctx, IPushClient push, string matchId)
    {
        string me = Require(ctx.PlayerId, "player");
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var (rec, lockId) = await LoadMatch(ctx, matchId);
            if (rec == null) { await ClearActive(ctx, me, matchId); return new MatchInfo(); }
            if (!rec.Players.Contains(me)) throw new Exception("not a player of this match");
            if (rec.Status != "ongoing") return Info(rec);
            if (!rec.Started) { await Drop(ctx, matchId, rec, lockId); return new MatchInfo(); }
            rec.Status = "finished"; rec.Ending = "abandoned"; rec.SettledBy = me; rec.FinishedAt = Now();
            var uncounted = MarkCounted(rec, rec.Players);
            if (!await TrySaveMatch(ctx, matchId, rec, lockId)) continue;
            foreach (var p in uncounted) await UpdateStats(ctx, push, p, Outcome.Loss, matchId);
            return Info(rec);
        }
        throw new Exception("could not resolve the match");
    }

    /// <summary>
    /// Puts the caller on the leaderboard once their player is linked to an account (Google, Apple), checked here with the
    /// player's own record, not taken from the device: their points go up at once if they have played, else after their
    /// first match. A guest (no linked account) is not ranked. The device calls it after signing in and after linking or
    /// unlinking. Returns whether the caller is ranked.
    /// </summary>
    [CloudCodeFunction("SyncLeaderboard")]
    public async Task<bool> SyncLeaderboard(IExecutionContext ctx)
    {
        string me = Require(ctx.PlayerId, "player");
        bool linked = await IsLinked(ctx, me);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var (s, lockId) = await GetProtected<Stats>(ctx, me, StatsKey);
            s ??= new Stats();
            if (s.Ranked == linked) return linked;
            s.Ranked = linked;
            try { await api.CloudSaveData.SetProtectedItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, me, new SetItemBody(StatsKey, s, lockId)); }
            catch (ApiException e) when (Conflict(e)) { continue; }
            // a player who unlinked stops being updated; their old entry stays until it is taken off in the dashboard
            if (linked && s.W + s.L + s.D > 0) await PostScore(ctx, me, s);
            return linked;
        }
        throw new Exception("could not update the player");
    }

    // whether the player has an account linked (any external id: Google, Apple), from Player Authentication
    async Task<bool> IsLinked(IExecutionContext ctx, string playerId)
    {
        var res = await api.PlayerAuth.GetPlayerAsync(ctx, ctx.ServiceToken, playerId, ctx.ProjectId);
        return res.Data?.ExternalIds?.Count > 0;
    }

    /// <summary>A match as far as its players need to know (whether it is there, still going, how it ended).</summary>
    [CloudCodeFunction("GetMatch")]
    public async Task<MatchInfo> GetMatch(IExecutionContext ctx, string matchId)
    {
        string me = Require(ctx.PlayerId, "player");
        var (rec, _) = await LoadMatch(ctx, matchId);
        if (rec == null) return new MatchInfo();
        if (!rec.Players.Contains(me)) throw new Exception("not a player of this match");
        return Info(rec);
    }

    /// <summary>Won/lost/drawn of the given players (the caller's opponent's included; only this module writes them).</summary>
    [CloudCodeFunction("GetStats")]
    public async Task<List<PlayerStats>> GetStats(IExecutionContext ctx, string[] playerIds)
    {
        Require(ctx.PlayerId, "player");
        var list = new List<PlayerStats>();
        foreach (var id in (playerIds ?? Array.Empty<string>()).Distinct().Take(8))
        {
            var (s, _) = await GetProtected<Stats>(ctx, id, StatsKey);
            s ??= new Stats();
            list.Add(new PlayerStats { PlayerId = id, W = s.W, L = s.L, D = s.D, P = s.P });
        }
        return list;
    }

    /// <summary>
    /// Daily clean-up (Scheduler + Trigger): removes matches finished over a week ago, and settles matches still open
    /// two hours after they started (both players left and neither came back) as a loss for both; one that never
    /// started is deleted instead.
    /// </summary>
    [CloudCodeFunction("DailyCleanup")]
    public async Task<string> DailyCleanup(IExecutionContext ctx)
    {
        // only the scheduled trigger runs this, never a player
        if (!string.IsNullOrEmpty(ctx.PlayerId)) throw new Exception("not for players");
        int removed = 0, expired = 0;
        string? after = null;
        var keys = new List<string>();
        do
        {
            var page = (await api.CloudSaveData.GetPrivateCustomKeysAsync(ctx, ctx.ServiceToken, ctx.ProjectId, MatchesId, after)).Data;
            keys.AddRange(page.Results.Select(k => k.Key));
            after = page.Results.Count > 0 && page.Links?.Next != null ? page.Results.Last().Key : null;
        } while (after != null);

        long now = Now();
        foreach (var key in keys)
        {
            var (rec, lockId) = await LoadMatch(ctx, key);
            if (rec == null) continue;
            if (rec.Status == "finished" && now - rec.FinishedAt > (long)FinishedKeep.TotalMilliseconds)
            {
                await DeleteMatch(ctx, key, lockId); removed++;
            }
            else if (rec.Status == "ongoing" && !rec.Started && now - rec.StartedAt > (long)CancelWindow.TotalMilliseconds)
            {
                await Drop(ctx, key, rec, lockId); removed++;
            }
            else if (rec.Status == "ongoing" && now - rec.StartedAt > (long)OngoingLimit.TotalMilliseconds)
            {
                rec.Status = "finished"; rec.Ending = "expired"; rec.FinishedAt = now;
                var uncounted = MarkCounted(rec, rec.Players);
                if (!await TrySaveMatch(ctx, key, rec, lockId)) continue;
                foreach (var p in uncounted) await UpdateStats(ctx, null, p, Outcome.Loss, key);
                expired++;
            }
        }
        return $"checked {keys.Count}, removed {removed}, expired {expired}";
    }

    // ---------------- helpers ----------------

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    static string Require(string? value, string what) =>
        string.IsNullOrEmpty(value) ? throw new Exception("missing " + what) : value;

    static MatchInfo Info(MatchRecord r) => new MatchInfo
    {
        Exists = true, Status = r.Status, Players = r.Players, FirstThrower = r.FirstThrower, Seed = r.Seed, Map = r.Map,
        Winner = r.Result?.Winner ?? "", Ending = r.Ending,
    };

    // the players among these whose count does not have this match yet, noted as having it (saved with the record,
    // before their counts change, so no one is counted twice)
    static List<string> MarkCounted(MatchRecord rec, IEnumerable<string> players)
    {
        var fresh = players.Where(p => !rec.Counted.Contains(p)).ToList();
        rec.Counted.AddRange(fresh);
        return fresh;
    }

    // the players' counts for a settled result: the winner a win, the other a loss, or a draw each
    async Task Count(IExecutionContext ctx, IPushClient? push, string matchId, IEnumerable<string> players, string winner)
    {
        foreach (var p in players)
            await UpdateStats(ctx, push, p, winner == "draw" ? Outcome.Draw : winner == p ? Outcome.Win : Outcome.Loss, matchId);
    }

    // one counted result: won/lost/drawn, the rating points (and how they moved, for the result screen), then the
    // leaderboard if the player is on it, and the player's device (no push from the daily clean-up: nobody waits there)
    async Task UpdateStats(IExecutionContext ctx, IPushClient? push, string playerId, Outcome outcome, string matchId)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var (s, lockId) = await GetProtected<Stats>(ctx, playerId, StatsKey);
            s ??= new Stats();
            int before = s.P;
            switch (outcome)
            {
                case Outcome.Win: s.W++; s.P += WinPoints; break;
                case Outcome.Draw: s.D++; break;
                default: s.L++; s.P = Math.Max(0, s.P - LossPoints); break;
            }
            s.LastMatch = matchId; s.LastDelta = s.P - before;
            try
            {
                await api.CloudSaveData.SetProtectedItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, playerId, new SetItemBody(StatsKey, s, lockId));
            }
            catch (ApiException e) when (Conflict(e)) { continue; }
            if (s.Ranked) await PostScore(ctx, playerId, s);
            // the player's device is told at once (its final result card waits for this): the match, the points, the change
            if (push != null)
            {
                try { await push.SendPlayerMessageAsync(ctx, JsonConvert.SerializeObject(new { m = matchId, p = s.P, d = s.LastDelta }), SettledMessage, playerId); }
                catch (Exception e) { log.LogWarning("settled push to {Player} failed: {Error}", playerId, e.Message); }   // the device reads its stats too
            }
            return;
        }
        log.LogError("could not update the stats of {Player}", playerId);
    }

    // the player's leaderboard entry: the rating points, with won/lost/drawn for the win rate
    async Task PostScore(IExecutionContext ctx, string playerId, Stats s)
    {
        try
        {
            await api.Leaderboards.AddLeaderboardPlayerScoreAsync(ctx, ctx.ServiceToken, Guid.Parse(ctx.ProjectId), LeaderboardId, playerId,
                new Unity.Services.Leaderboards.Model.AddLeaderboardScore(s.P, new Dictionary<string, int> { ["w"] = s.W, ["l"] = s.L, ["d"] = s.D }));
        }
        catch (Exception e) { log.LogError("could not post the score of {Player}: {Error}", playerId, e.Message); }
    }

    async Task BumpIntegrity(IExecutionContext ctx, string playerId, Action<Integrity> change)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var res = (await api.CloudSaveData.GetPrivateItemsAsync(ctx, ctx.ServiceToken, ctx.ProjectId, playerId, new List<string> { IntegrityKey }, null)).Data;
            var item = res.Results.FirstOrDefault();
            var i = item != null ? Read<Integrity>(item.Value) ?? new Integrity() : new Integrity();
            change(i);
            try
            {
                await api.CloudSaveData.SetPrivateItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, playerId, new SetItemBody(IntegrityKey, i, item?.WriteLock));
                return;
            }
            catch (ApiException e) when (Conflict(e)) { }
        }
    }

    async Task ClearActive(IExecutionContext ctx, string playerId, string matchId)
    {
        var (current, lockId) = await GetProtected<string>(ctx, playerId, ActiveKey);
        if (current == matchId)
            try { await api.CloudSaveData.DeleteProtectedItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, playerId, ActiveKey, lockId); }
            catch (ApiException e) when (Conflict(e) || NotFound(e)) { }
    }

    async Task SetProtected(IExecutionContext ctx, string playerId, string key, object value) =>
        await api.CloudSaveData.SetProtectedItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, playerId, new SetItemBody(key, value, null));

    async Task<(T?, string?)> GetProtected<T>(IExecutionContext ctx, string playerId, string key) where T : class
    {
        var res = (await api.CloudSaveData.GetProtectedItemsAsync(ctx, ctx.ServiceToken, ctx.ProjectId, playerId, new List<string> { key }, null)).Data;
        var item = res.Results.FirstOrDefault();
        return item == null ? (null, null) : (Read<T>(item.Value), item.WriteLock);
    }

    async Task<(MatchRecord?, string?)> LoadMatch(IExecutionContext ctx, string matchId)
    {
        Require(matchId, "matchId");
        var res = (await api.CloudSaveData.GetPrivateCustomItemsAsync(ctx, ctx.ServiceToken, ctx.ProjectId, MatchesId, new List<string> { matchId }, null)).Data;
        var item = res.Results.FirstOrDefault();
        return item == null ? (null, null) : (Read<MatchRecord>(item.Value), item.WriteLock);
    }

    async Task SaveMatch(IExecutionContext ctx, string matchId, MatchRecord rec, string? lockId) =>
        await api.CloudSaveData.SetPrivateCustomItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, MatchesId, new SetItemBody(matchId, rec, lockId));

    async Task<bool> TrySaveMatch(IExecutionContext ctx, string matchId, MatchRecord rec, string? lockId)
    {
        try { await SaveMatch(ctx, matchId, rec, lockId); return true; }
        catch (ApiException e) when (Conflict(e)) { return false; }
    }

    async Task DeleteMatch(IExecutionContext ctx, string matchId, string? lockId)
    {
        try { await api.CloudSaveData.DeletePrivateCustomItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, MatchesId, matchId, lockId); }
        catch (ApiException e) when (Conflict(e) || NotFound(e)) { }
    }

    static bool Conflict(ApiException e) => e.Response?.StatusCode == HttpStatusCode.Conflict;
    static bool NotFound(ApiException e) => e.Response?.StatusCode == HttpStatusCode.NotFound;

    // a stored value comes back as a JSON token (or a plain string)
    static T? Read<T>(object? value) where T : class
    {
        if (value == null) return null;
        if (typeof(T) == typeof(string)) return (value as string ?? value.ToString()) as T;
        string json = value as string ?? JsonConvert.SerializeObject(value);
        return JsonConvert.DeserializeObject<T>(json);
    }
}
