using System.Collections.Generic;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>The products the shop sells, in the order it shows them (Resources/ShopCatalog).</summary>
    [CreateAssetMenu(menuName = "FlickCurling/Shop Catalog", fileName = "ShopCatalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        public const string ResourcePath = "ShopCatalog";

        public List<ShopProduct> products = new List<ShopProduct>();

        public static ShopCatalog Load() => Resources.Load<ShopCatalog>(ResourcePath);
    }
}
