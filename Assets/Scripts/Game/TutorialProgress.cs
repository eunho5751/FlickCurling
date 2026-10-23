using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlickCurling.Game.Account;
using Unity.Services.CloudSave;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>
    /// Whether the tutorial is done (finished or skipped): in the player's Cloud Save data (written by the device: nobody
    /// gains by faking it), and on this device too, so a later start knows at once without asking the server. A player
    /// who did it on another device is found out once signed in here.
    /// </summary>
    public static class TutorialProgress
    {
        const string CloudKey = "tutorialDone";
        const string DoneHere = "tutorial.done";        // this device: done
        const string Unsaved = "tutorial.unsaved";      // ... but the server has not been told yet
        const string ForgetCloud = "tutorial.forgetCloud";   // testing: the player's note goes at the next check

        /// <summary>Whether to show the tutorial now: not if this device has it done; otherwise asked of the player's
        /// data once signed in (the network gate's popup shows while that cannot be).</summary>
        public static async Task<bool> NeededAsync()
        {
            bool forget = PlayerPrefs.GetInt(ForgetCloud, 0) == 1;
            if (!forget && PlayerPrefs.GetInt(DoneHere, 0) == 1)
            {
                if (PlayerPrefs.GetInt(Unsaved, 0) == 1) SaveSoon();   // told now
                return false;
            }
            while (AccountService.Status != AccountService.State.Ready)
            {
                if (AccountService.Status == AccountService.State.Failed || AccountService.Status == AccountService.State.Offline)
                    _ = AccountService.StartAsync();
                await Task.Delay(300);
            }
            if (forget) { await ForgetAsync(); return true; }
            try
            {
                var items = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { CloudKey });
                if (items.ContainsKey(CloudKey)) { Remember(false); return false; }
            }
            catch (Exception e) { Debug.LogWarning("FlickCurling: reading the tutorial's progress failed: " + e.Message); }
            return true;   // (unreadable: shown; it can be skipped)
        }

        /// <summary>Finished or skipped: not shown again.</summary>
        public static void MarkDone()
        {
            Remember(true);
            SaveSoon();
        }

        /// <summary>Testing (the editor's FlickCurling > Tutorial menu): not done any more, here and in the player's data
        /// (now if signed in, else at the next check).</summary>
        public static void ResetForTesting()
        {
            PlayerPrefs.DeleteKey(DoneHere);
            PlayerPrefs.DeleteKey(Unsaved);
            PlayerPrefs.SetInt(ForgetCloud, 1);
            PlayerPrefs.Save();
            if (Application.isPlaying && AccountService.Status == AccountService.State.Ready) _ = ForgetAsync();
        }

        static async Task ForgetAsync()
        {
            try
            {
                await CloudSaveService.Instance.Data.Player.DeleteAsync(CloudKey, new Unity.Services.CloudSave.Models.Data.Player.DeleteOptions());
                PlayerPrefs.DeleteKey(ForgetCloud);
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning("FlickCurling: forgetting the tutorial's progress failed: " + e.Message); }
        }

        static void Remember(bool unsaved)
        {
            PlayerPrefs.SetInt(DoneHere, 1);
            PlayerPrefs.SetInt(Unsaved, unsaved ? 1 : 0);
            PlayerPrefs.Save();
        }

        static async void SaveSoon()
        {
            try
            {
                if (AccountService.Status != AccountService.State.Ready) await AccountService.StartAsync();
                await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { [CloudKey] = true });
                PlayerPrefs.SetInt(Unsaved, 0);
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning("FlickCurling: saving the tutorial's progress failed (told next start): " + e.Message); }
        }
    }
}
