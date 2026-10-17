using FlickCurling.Game.Account;
using UnityEditor;
using UnityEngine;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// FlickCurling > Account Test, for trying the account flows in Play mode: unlink Google from the current player, start
    /// over as a brand-new guest, and log who is signed in. The settings popup's account box follows along.
    /// </summary>
    public static class AccountTestMenu
    {
        const string Root = "FlickCurling/Account Test/";

        static bool Ready => Application.isPlaying && AccountService.Status == AccountService.State.Ready && !AccountService.Busy;

        [MenuItem(Root + "Unlink Google")]
        static async void UnlinkGoogle()
        {
            bool ok = await AccountService.UnlinkAsync(AccountService.Google);
            Debug.Log(ok ? "FlickCurling: unlinked Google; " + Who() : "FlickCurling: could not unlink Google " + AccountService.LastError);
        }

        [MenuItem(Root + "Unlink Google", true)]
        static bool CanUnlinkGoogle() => Ready && AccountService.IsLinked(AccountService.Google);

        [MenuItem(Root + "Start Over As New Guest")]
        static async void StartOver()
        {
            if (!EditorUtility.DisplayDialog("Start over as a new guest",
                    "이 기기의 세션을 지우고 새 게스트로 로그인해요. 지금 플레이어가 게스트라면(연동 없음) 다시 돌아갈 수 없어요.", "새 게스트로", "취소"))
                return;
            bool ok = await AccountService.StartOverAsGuestAsync();
            Debug.Log(ok ? "FlickCurling: new guest; " + Who() : "FlickCurling: could not start over " + AccountService.LastError);
        }

        [MenuItem(Root + "Start Over As New Guest", true)]
        static bool CanStartOver() => Ready;

        [MenuItem(Root + "Log Current Player")]
        static void LogPlayer() => Debug.Log("FlickCurling: " + Who());

        [MenuItem(Root + "Log Current Player", true)]
        static bool CanLogPlayer() => Application.isPlaying;

        static string Who() => "status " + AccountService.Status + ", PID " + AccountService.PlayerId + ", name " + AccountService.PlayerName
            + ", Google " + (AccountService.IsLinked(AccountService.Google) ? "linked" : "not linked");
    }
}
