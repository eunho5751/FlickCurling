using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// The shared popup window of the main menu (the prefab Assets/Prefabs/Popup.prefab, built by PopupBuilder): a dim
    /// backdrop, a 9-sliced cream panel in a wooden frame with the title on its wide top bar and a small round close
    /// button at that bar's right end. Put what the popup shows under <see cref="Content"/> (or just set
    /// <see cref="Message"/>); the window can be any size (<see cref="SetSize"/>), the art keeps its rims.
    /// </summary>
    public sealed class Popup : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] RectTransform window;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] TextMeshProUGUI message;
        [SerializeField] RectTransform content;
        [SerializeField] Button closeButton;
        [SerializeField] Button backdrop;

        /// <summary>Called after the popup closes (by its close button, the backdrop, or <see cref="Hide"/>).</summary>
        public event Action Closed;

        public RectTransform Content => content;
        public string Title { get => title.text; set => title.text = value; }
        /// <summary>A plain text body inside the content area; empty hides it.</summary>
        public string Message { get => message.text; set { message.text = value; message.gameObject.SetActive(!string.IsNullOrEmpty(value)); } }
        /// <summary>Whether a tap on the dim backdrop closes the popup (on by default).</summary>
        public bool CloseOnBackdrop { get; set; } = true;

        void Awake()
        {
            closeButton.onClick.AddListener(Hide);
            backdrop.onClick.AddListener(() => { if (CloseOnBackdrop) Hide(); });
        }

        /// <summary>The window's size in reference pixels (1080 x 1920 layout).</summary>
        public void SetSize(float width, float height) => window.sizeDelta = new Vector2(width, height);

        public void Show(string titleText = null, string messageText = null)
        {
            if (titleText != null) Title = titleText;
            if (messageText != null) Message = messageText;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            DOTween.Kill(this);
            group.alpha = 0; group.interactable = group.blocksRaycasts = true;
            window.localScale = Vector3.one * 0.9f;
            DOTween.To(() => group.alpha, a => group.alpha = a, 1, 0.15f).SetUpdate(true).SetId(this);
            window.DOScale(1, 0.2f).SetEase(Ease.OutBack).SetUpdate(true).SetId(this);
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            DOTween.Kill(this);
            group.interactable = false;
            DOTween.To(() => group.alpha, a => group.alpha = a, 0, 0.12f).SetUpdate(true).SetId(this).OnComplete(() =>
            {
                gameObject.SetActive(false);
                Closed?.Invoke();
            });
        }

        void OnDestroy() => DOTween.Kill(this);
    }
}
