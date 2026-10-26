using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// One product in the shop (the prefab Assets/Prefabs/UI/ShopCard.prefab, built by UiPrefabBuilder): a parchment card
    /// pinned to the wall by a brass nail at its top, the name across the top, the picture in the middle and the price on
    /// the wooden band at the bottom (a coin and an amount, or the store's price in the player's money, or a word such as
    /// "owned"). Each card hangs a little crooked, tilted at random, up to about three degrees either way.
    /// </summary>
    public sealed class ShopCard : MonoBehaviour
    {
        [SerializeField] RectTransform tilt;   // the card under the nail; it swings about the nail
        [SerializeField] TextMeshProUGUI price;
        [SerializeField] GameObject coin;
        [SerializeField] LocalizeStringEvent nameText;
        [SerializeField] Image picture;
        [SerializeField] float maxTilt = 2.8f;   // the shop's gap between cards is sized to this

        public Button Button => GetComponent<Button>();

        void Awake() => tilt.localEulerAngles = new Vector3(0, 0, Random.Range(-maxTilt, maxTilt));

        /// <summary>A price in coins: the coin and the amount.</summary>
        public void SetPrice(int amount) { coin.SetActive(true); price.text = amount.ToString("N0"); }
        /// <summary>Anything else on the band (a store price, "owned"), without the coin.</summary>
        public void SetPriceText(string text) { coin.SetActive(false); price.text = text; }
        /// <summary>The name, following the language.</summary>
        public void SetName(LocalizedString name)
        {
            // a copy: a LocalizedString keeps its listeners, so one asset's is not shared between texts
            nameText.StringReference = new LocalizedString(name.TableReference, name.TableEntryReference);
            nameText.RefreshString();
        }
        public void SetPicture(Sprite sprite) => picture.sprite = sprite;
    }
}
