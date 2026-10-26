using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Purchasing;

namespace FlickCurling.Game
{
    /// <summary>
    /// One product sold in the shop, bought through the store (Unity IAP): its store ID and type, its name (localized),
    /// the line shown once it has been bought, and its picture. The price comes from the store, in the player's own
    /// currency. The shop lists the products in the <see cref="ShopCatalog"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "FlickCurling/Shop Product", fileName = "ShopProduct")]
    public sealed class ShopProduct : ScriptableObject
    {
        [Tooltip("The product ID, the same in Google Play and the App Store")]
        public string id;
        public ProductType type = ProductType.NonConsumable;
        public LocalizedString displayName;
        [Tooltip("Shown when the purchase has gone through")]
        public LocalizedString purchasedMessage;
        public Sprite icon;
    }
}
