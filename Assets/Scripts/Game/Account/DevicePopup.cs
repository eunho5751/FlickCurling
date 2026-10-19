using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// "Signed in on another device" (the prefab Resources/DevicePopup, built by MainMenuBuilder on the shared Popup):
    /// no close button, the backdrop does not close it; its one button signs in on this device again.
    /// </summary>
    public sealed class DevicePopup : MonoBehaviour
    {
        public const string ResourcePath = "DevicePopup";

        [SerializeField] Popup popup;
        [SerializeField] Button reconnect;

        public void Run(Func<Task<bool>> reclaim, Action closed)
        {
            popup.CloseOnBackdrop = false;
            popup.Closed += () => closed?.Invoke();
            reconnect.onClick.AddListener(async () =>
            {
                reconnect.interactable = false;
                bool ok = await reclaim();
                if (!this) return;
                if (ok) popup.Hide();
                else reconnect.interactable = true;
            });
            popup.Show();
        }
    }
}
