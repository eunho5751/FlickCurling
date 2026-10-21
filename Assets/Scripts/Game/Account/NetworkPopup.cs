using System;
using UnityEngine;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// "Check your network connection" (the prefab Resources/NetworkPopup, built by MainMenuBuilder on the shared Popup):
    /// no close button, the backdrop does not close it; <see cref="NetworkGuard"/> takes it away once the network is back.
    /// </summary>
    public sealed class NetworkPopup : MonoBehaviour
    {
        public const string ResourcePath = "NetworkPopup";

        [SerializeField] Popup popup;

        public void Show()
        {
            popup.CloseOnBackdrop = false;
            popup.Show();
        }

        public void Hide(Action gone)
        {
            popup.Closed += () => gone?.Invoke();
            popup.Hide();
        }
    }
}
