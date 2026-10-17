using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// Asked when linking finds the account already on another player (the prefab Resources/LinkConflictPopup, built by
    /// MainMenuBuilder on the shared Popup): the account's record and this one side by side (nickname, PID, the day each
    /// started), then "load that record" or "keep this record", each confirmed once more since neither can be undone.
    /// Closing it cancels.
    /// </summary>
    public sealed class LinkConflictPopup : MonoBehaviour
    {
        public const string ResourcePath = "LinkConflictPopup";

        [SerializeField] Popup popup;
        [SerializeField] GameObject choicePage, confirmPage;
        [SerializeField] TextMeshProUGUI existingName, existingPid, existingDate, currentName, currentPid, currentDate;
        [SerializeField] TextMeshProUGUI confirmMessage;
        [SerializeField] Button load, keep, confirm, back;

        readonly TaskCompletionSource<AccountService.ConflictChoice> done = new TaskCompletionSource<AccountService.ConflictChoice>();
        AccountService.LinkConflict conflict;
        AccountService.ConflictChoice pending, result = AccountService.ConflictChoice.Cancel;

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
            conflict = c;
            Fill(existingName, existingPid, existingDate, c.Existing);
            Fill(currentName, currentPid, currentDate, c.Current);
            load.onClick.AddListener(() => Ask(AccountService.ConflictChoice.LoadExisting));
            keep.onClick.AddListener(() => Ask(AccountService.ConflictChoice.KeepCurrent));
            back.onClick.AddListener(() => Page(false));
            confirm.onClick.AddListener(() => { result = pending; popup.Hide(); });
            popup.CloseOnBackdrop = false;   // a choice this big takes a deliberate press
            popup.Closed += () => { done.TrySetResult(result); Destroy(gameObject); };
            Page(false);
            popup.Show();
            return done.Task;
        }

        static void Fill(TextMeshProUGUI name, TextMeshProUGUI pid, TextMeshProUGUI date, AccountService.PlayerSummary p)
        {
            name.text = p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : "-";
            pid.text = Loc.T("account.pid") + "  " + (p != null && !string.IsNullOrEmpty(p.Id) ? p.Id : "-");
            date.text = p?.CreatedAt is DateTime d ? Loc.T("link.created", ("date", d.ToLocalTime().ToString("yyyy-MM-dd"))) : "";
        }

        void Ask(AccountService.ConflictChoice choice)
        {
            pending = choice;
            string existing = conflict.Existing?.Name ?? "-", current = conflict.Current?.Name ?? "-";
            confirmMessage.text = choice == AccountService.ConflictChoice.LoadExisting
                ? Loc.T("link.confirmLoad", ("existing", existing), ("current", current))
                : Loc.T("link.confirmKeep", ("existing", existing), ("current", current));
            Page(true);
        }

        void Page(bool confirming)
        {
            choicePage.SetActive(!confirming);
            confirmPage.SetActive(confirming);
        }
    }
}
