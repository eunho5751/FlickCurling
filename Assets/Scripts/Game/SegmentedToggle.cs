using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// A group toggle on the popup art: a groove split into equal options, with a raised wooden segment that slides to the
    /// chosen one. Exactly one option is always selected. Built by MainMenuBuilder (each option a Button with a label).
    /// </summary>
    public sealed class SegmentedToggle : MonoBehaviour
    {
        [SerializeField] RectTransform knob;
        [SerializeField] Button[] options;
        [SerializeField] TextMeshProUGUI[] labels;
        [SerializeField] Color onText = new Color32(0xf6, 0xea, 0xd2, 0xff), offText = new Color32(0x4a, 0x2a, 0x12, 0xff);
        [SerializeField] int selected;

        public event Action<int> Changed;
        public int Selected => selected;

        void Awake()
        {
            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                options[i].onClick.AddListener(() => Select(k));
            }
        }

        void OnEnable() => Select(selected, false, false);

        public void Select(int i, bool animate = true, bool notify = true)
        {
            i = Mathf.Clamp(i, 0, options.Length - 1);
            bool changed = i != selected;
            selected = i;
            // the knob covers one option's share of the groove
            float share = 1f / options.Length;
            DOTween.Kill(knob);
            var min = new Vector2(share * i, 0); var max = new Vector2(share * (i + 1), 1);
            if (animate && isActiveAndEnabled)
            {
                DOTween.To(() => knob.anchorMin, v => knob.anchorMin = v, min, 0.16f).SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(knob);
                DOTween.To(() => knob.anchorMax, v => knob.anchorMax = v, max, 0.16f).SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(knob);
            }
            else { knob.anchorMin = min; knob.anchorMax = max; }
            for (int k = 0; k < labels.Length; k++) labels[k].color = k == i ? onText : offText;
            if (changed && notify) Changed?.Invoke(i);
        }

        void OnDestroy() => DOTween.Kill(knob);
    }
}
