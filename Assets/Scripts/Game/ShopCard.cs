using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// One product in the shop (the prefab Assets/Prefabs/UI/ShopCard.prefab, built by UiPrefabBuilder): a parchment card
    /// pinned to the wall by a brass nail at its top, the name across the top, the picture in the middle and the price on
    /// the wooden band at the bottom. Each card hangs a little crooked, tilted at random, a few degrees either way.
    /// </summary>
    public sealed class ShopCard : MonoBehaviour
    {
        [SerializeField] RectTransform tilt;   // the card under the nail; it swings about the nail
        [SerializeField] TextMeshProUGUI price;
        [SerializeField] Image picture;
        [SerializeField] float maxTilt = 3;

        public Button Button => GetComponent<Button>();

        void Awake() => tilt.localEulerAngles = new Vector3(0, 0, Random.Range(-maxTilt, maxTilt));

        public void SetPrice(int amount) => price.text = amount.ToString("N0");
        public void SetPicture(Sprite sprite) => picture.sprite = sprite;
    }
}
