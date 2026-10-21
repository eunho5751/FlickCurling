using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlickCurling.Game.Account;
using TMPro;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Exceptions;
using Unity.Services.Leaderboards.Models;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game.Online
{
    /// <summary>
    /// The popup the main menu's "Leaderboard" button opens (built by MainMenuBuilder on the shared Popup): the top 50 of
    /// the "rating" leaderboard by rating points, one row each (rank, nickname, points, win rate) in a scroll view, the
    /// first three with a gold, silver and bronze medal and this player's own row highlighted. Above the list, always,
    /// this player's rank in the whole leaderboard with their points and win rate; "no rank yet" when they are not on it,
    /// and for a guest (never on it) "Sign in with Google" instead, after which they are. Only the Cloud Code module
    /// writes the leaderboard; this only reads it.
    /// </summary>
    public sealed class LeaderboardPopup : MonoBehaviour
    {
        const string LeaderboardId = "rating";   // (as the module keeps it)
        const int Top = 50;

        [SerializeField] Popup popup;
        [SerializeField] RectTransform list;
        [SerializeField] LeaderboardRow rowTemplate;
        [SerializeField] TextMeshProUGUI listStatus;
        // this player: rank, points and win rate (the "mine" figures), or a line saying why there are none, or (a guest)
        // the Google button
        [SerializeField] GameObject mine;
        [SerializeField] TextMeshProUGUI myRank, myPoints, myRate, myStatus;
        [SerializeField] Button googleButton;

        readonly List<LeaderboardRow> rows = new List<LeaderboardRow>();
        int loading;

        void Awake()
        {
            rowTemplate.gameObject.SetActive(false);
            googleButton.onClick.AddListener(LinkGoogle);
        }

        void OnEnable() => AccountService.Changed += OnAccount;
        void OnDisable() => AccountService.Changed -= OnAccount;

        public void Open()
        {
            popup.Show();
            Load();
        }

        // signed in again, linked: the player may be ranked now
        void OnAccount() { if (gameObject.activeInHierarchy) Load(); }

        async void Load()
        {
            int ticket = ++loading;
            ShowMine(MineState.Loading);
            listStatus.text = Loc.T("leaderboard.loading");
            listStatus.gameObject.SetActive(rows.Count == 0);
            if (AccountService.Status != AccountService.State.Ready) await AccountService.StartAsync();
            if (ticket != loading || !this) return;

            // the top of the leaderboard and this player's place, both read straight from it
            var top = ReadTop();
            var me = ReadMine();
            List<LeaderboardEntry> entries = null;
            try { entries = await top; } catch (Exception e) { Debug.LogWarning("FlickCurling: reading the leaderboard failed: " + e.Message); }
            var (state, entry) = await me;
            if (ticket != loading || !this) return;

            Fill(entries);
            ShowMine(state, entry);
        }

        static async Task<List<LeaderboardEntry>> ReadTop()
        {
            var page = await LeaderboardsService.Instance.GetScoresAsync(LeaderboardId, new GetScoresOptions { Limit = Top, IncludeMetadata = true });
            return page.Results;
        }

        enum MineState { Loading, Ranked, Unranked, Guest, Failed }

        static async Task<(MineState, LeaderboardEntry)> ReadMine()
        {
            if (AccountService.Status != AccountService.State.Ready) return (MineState.Failed, null);
            // whether the server has put this player on the leaderboard (it checks the linked account itself)
            await AccountService.RankSync;
            if (AccountService.Ranked != true)
                return (IsGuest() ? MineState.Guest : MineState.Unranked, null);
            try { return (MineState.Ranked, await LeaderboardsService.Instance.GetPlayerScoreAsync(LeaderboardId, new GetPlayerScoreOptions { IncludeMetadata = true })); }
            catch (LeaderboardsException e) when (e.Reason == LeaderboardsExceptionReason.EntryNotFound) { return (MineState.Unranked, null); }
            catch (Exception e) { Debug.LogWarning("FlickCurling: reading this player's rank failed: " + e.Message); return (MineState.Failed, null); }
        }

        static bool IsGuest() => !AccountService.IsLinked(AccountService.Google) && !AccountService.IsLinked(AccountService.Apple);

        void Fill(List<LeaderboardEntry> entries)
        {
            int n = entries?.Count ?? 0;
            while (rows.Count < n)
            {
                var row = Instantiate(rowTemplate, list, false);
                row.name = "Row" + rows.Count;
                rows.Add(row);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < n;
                rows[i].gameObject.SetActive(used);
                if (!used) continue;
                var e = entries[i];
                var (w, l, d) = Record(e);
                rows[i].Set(e.Rank + 1, Nickname.Display(e.PlayerName), (int)e.Score, WinRate(w, l, d), e.PlayerId == AccountService.PlayerId);
            }
            listStatus.text = entries == null ? Loc.T("leaderboard.failed") : Loc.T("leaderboard.empty");
            listStatus.gameObject.SetActive(n == 0);
        }

        void ShowMine(MineState state, LeaderboardEntry entry = null)
        {
            bool ranked = state == MineState.Ranked && entry != null;
            mine.SetActive(ranked);
            googleButton.gameObject.SetActive(state == MineState.Guest);
            myStatus.gameObject.SetActive(!ranked);
            myStatus.text = state switch
            {
                MineState.Loading => Loc.T("leaderboard.loading"),
                MineState.Guest => Loc.T("leaderboard.guest"),
                MineState.Failed => Loc.T("leaderboard.failed"),
                _ => Loc.T("leaderboard.unranked"),
            };
            googleButton.interactable = !AccountService.Busy;
            if (!ranked) return;
            var (w, l, d) = Record(entry);
            myRank.text = (entry.Rank + 1).ToString("N0");
            myPoints.text = ((int)entry.Score).ToString("N0");
            myRate.text = WinRate(w, l, d);
        }

        // won/lost/drawn, which the module writes with each score
        [Serializable] sealed class Meta { public int w, l, d; }

        static (int, int, int) Record(LeaderboardEntry e)
        {
            if (e?.Metadata == null) return (0, 0, 0);
            try
            {
                var m = JsonUtility.FromJson<Meta>(e.Metadata);
                return m == null ? (0, 0, 0) : (m.w, m.l, m.d);
            }
            catch (Exception) { return (0, 0, 0); }
        }

        // wins out of every match played (draws included), whole per cent; "-" before any
        static string WinRate(int w, int l, int d)
        {
            int n = w + l + d;
            return n == 0 ? "-" : Mathf.RoundToInt(100f * w / n) + "%";
        }

        // a guest signs in with Google here as in the settings: linked, the server ranks them (AccountService), and this
        // reloads on the account's change
        async void LinkGoogle()
        {
            if (!AccountService.Google.IsSupported)
            {
#if UNITY_EDITOR
                Debug.Log("FlickCurling: to test Google sign-in in the editor, set the Web client ID and secret in FlickCurling > Google Sign-In (Editor) Setup");
#endif
                myStatus.text = Loc.T("account.googleUnavailable"); return;
            }
            bool ok = await AccountService.LinkAsync(AccountService.Google, conflict => LinkConflictPopup.ChooseAsync(this, conflict));
            if (this && !ok && !string.IsNullOrEmpty(AccountService.LastError)) myStatus.text = Loc.T("account.linkFailed");
        }
    }
}
