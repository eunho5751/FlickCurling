using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// Asked when linking finds the account already on another player (the prefab Resources/LinkConflictPopup, built by
    /// MainMenuBuilder on the shared Popup): the account's record and this one (nickname, PID, the day each started),
    /// then "load that record" or "keep this record". Closing it cancels.
    /// </summary>
    public sealed class LinkConflictPopup : MonoBehaviour
    {
        public const string ResourcePath = "LinkConflictPopup";

        [SerializeField] Popup popup;
        [SerializeField] TextMeshProUGUI existingName, existingPid, existingDate, currentName, currentPid, currentDate;
        [SerializeField] Button load, keep;

        readonly TaskCompletionSource<AccountService.ConflictChoice> done = new TaskCompletionSource<AccountService.ConflictChoice>();
        AccountService.ConflictChoice result = AccountService.ConflictChoice.Cancel;

        /// <summary>Opens the popup over everything on <paramref name="near"/>'s canvas and waits for the choice.</summary>
        public static Task<AccountService.ConflictChoice> ChooseAsync(Component near, AccountService.LinkConflict conflict)
        {
            var prefab = Resources.Load<LinkConflictPopup>(ResourcePath);
            var canvas = near != null ? near.GetComponentInParent<Canvas>() : null;
            if (prefab == null || canvas == null) return Task.FromResult(AccountService.ConflictChoice.Cancel);
            var p = Instantiate(prefab, canvas.rootCanvas.transform, false);
            return p.Run(conflict);
        }

        Task<AccountService.ConflictChoice> Run(AccountService.LinkConflict c)
        {
            Fill(existingName, existingPid, existingDate, c.Existing);
            Fill(currentName, currentPid, currentDate, c.Current);
            load.onClick.AddListener(() => Choose(AccountService.ConflictChoice.LoadExisting));
            keep.onClick.AddListener(() => Choose(AccountService.ConflictChoice.KeepCurrent));
            popup.CloseOnBackdrop = false;   // a choice this big takes a deliberate press
            popup.Closed += () => { done.TrySetResult(result); Destroy(gameObject); };
            popup.Show();
            return done.Task;
        }

        void Choose(AccountService.ConflictChoice choice)
        {
            result = choice;
            popup.Hide();
        }

        static void Fill(TextMeshProUGUI name, TextMeshProUGUI pid, TextMeshProUGUI date, AccountService.PlayerSummary p)
        {
            name.text = p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : "-";
            pid.text = Loc.T("account.pid") + "  " + (p != null && !string.IsNullOrEmpty(p.Id) ? p.Id : "-");
            date.text = p?.CreatedAt is DateTime d ? Loc.T("link.created", ("date", d.ToLocalTime().ToString("yyyy-MM-dd"))) : "";
        }
    }
}
