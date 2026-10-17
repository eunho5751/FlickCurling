using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// Asks a new player for their nickname (the prefab Resources/NicknamePopup, built by MainMenuBuilder on the shared
    /// Popup and the InputField prefab). It has no close button and the backdrop does not close it: it stays until a
    /// nickname is saved. The guide under the field gives the rules (Nickname) and turns red when the text breaks one;
    /// OK is only pressable while the text is valid.
    /// </summary>
    public sealed class NicknamePopup : MonoBehaviour
    {
        public const string ResourcePath = "NicknamePopup";

        [SerializeField] Popup popup;
        [SerializeField] TMP_InputField input;
        [SerializeField] TextMeshProUGUI guide, counter;
        [SerializeField] Button ok;
        [SerializeField] Color guideColor = new Color32(0x86, 0x61, 0x3d, 0xff), errorColor = new Color32(0xc0, 0x3a, 0x22, 0xff);

        static NicknamePopup open;

        /// <summary>Opens the popup over <paramref name="near"/>'s canvas, unless it is already up.</summary>
        public static void Open(Component near)
        {
            if (open) return;
            var prefab = Resources.Load<NicknamePopup>(ResourcePath);
            var canvas = near != null ? near.GetComponentInParent<Canvas>() : null;
            if (prefab == null || canvas == null) return;
            open = Instantiate(prefab, canvas.rootCanvas.transform, false);
            open.Run();
        }

        void Run()
        {
            input.characterLimit = Nickname.MaxLength;
            input.onValueChanged.AddListener(_ => Refresh());
            input.onSubmit.AddListener(_ => Submit());
            ok.onClick.AddListener(Submit);
            popup.CloseOnBackdrop = false;
            popup.Closed += () => Destroy(gameObject);
            Refresh();
            popup.Show();
        }

        void Refresh()
        {
            string text = input.text;
            var check = Nickname.Validate(text);
            counter.text = text.Length + "/" + Nickname.MaxLength;
            bool wrong = check != Nickname.Check.Ok && check != Nickname.Check.Empty;
            guide.text = Loc.T("nickname.guide");
            guide.color = counter.color = wrong ? errorColor : guideColor;
            ok.interactable = check == Nickname.Check.Ok && !AccountService.Busy;
        }

        async void Submit()
        {
            if (!ok.interactable) return;
            ok.interactable = false;
            bool saved = await AccountService.SetNicknameAsync(input.text);
            if (!this) return;
            if (saved) { popup.Hide(); return; }
            guide.text = Loc.T("nickname.failed");
            guide.color = errorColor;
            ok.interactable = Nickname.IsValid(input.text);
        }

        void OnDestroy() { if (open == this) open = null; }
    }
}
