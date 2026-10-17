using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// The account row at the top of the settings popup: the UGS player ID and nickname (a guest's are the ones UGS gives),
    /// and "Sign in with Google", which opens Google's account chooser and links the picked account, or, once linked,
    /// "Linked with Google" in its place. Under them a short status line while signing in or after a failure.
    /// </summary>
    public sealed class AccountPanel : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI pid, nickname, status;
        [SerializeField] Button googleButton;
        [SerializeField] GameObject googleLinked;

        /// <summary>Whether "Sign in with Google" can be pressed: in the main menu yes, in the game it shows grayed out.</summary>
        public bool LinkAllowed { get => linkAllowed; set { linkAllowed = value; Render(); } }
        bool linkAllowed = true;

        void Awake() => googleButton.onClick.AddListener(LinkGoogle);

        void OnEnable()
        {
            AccountService.Changed += Render;
            if (AccountService.Status == AccountService.State.Failed) _ = AccountService.StartAsync();   // try again
            Render();
        }

        void OnDisable() => AccountService.Changed -= Render;

        async void LinkGoogle()
        {
            if (!linkAllowed) return;
            if (!AccountService.Google.IsSupported)
            {
#if UNITY_EDITOR
                Debug.Log("FlickCurling: to test Google sign-in in the editor, set the Web client ID and secret in FlickCurling > Google Sign-In (Editor) Setup");
#endif
                ShowStatus("account.googleUnavailable"); return;
            }
            bool ok = await AccountService.LinkAsync(AccountService.Google, conflict => LinkConflictPopup.ChooseAsync(this, conflict));
            if (this && !ok && !string.IsNullOrEmpty(AccountService.LastError)) ShowStatus("account.linkFailed");
        }

        void Render()
        {
            if (!this) return;
            bool ready = AccountService.Status == AccountService.State.Ready;
            pid.text = ready ? AccountService.PlayerId : "-";   // the values; their labels are localized on their own
            nickname.text = ready ? Nickname.Display(AccountService.PlayerName) : "-";
            bool linked = AccountService.IsLinked(AccountService.Google);
            googleLinked.SetActive(linked);
            googleButton.gameObject.SetActive(!linked);
            googleButton.interactable = ready && !AccountService.Busy && linkAllowed;
            var fade = googleButton.GetComponent<CanvasGroup>();
            if (fade) fade.alpha = googleButton.interactable ? 1 : 0.55f;
            status.text = AccountService.Status == AccountService.State.SigningIn || AccountService.Busy ? Loc.T("account.signingIn")
                : AccountService.Status == AccountService.State.Failed ? Loc.T("account.offline") : "";
        }

        void ShowStatus(string key) => status.text = Loc.T(key);
    }
}
