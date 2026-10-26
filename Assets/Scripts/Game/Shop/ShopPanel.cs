using System.Collections.Generic;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>
    /// The shop popup of the main menu (built by MainMenuBuilder on the shared Popup): one card per product of the
    /// <see cref="ShopCatalog"/> (made from the card template), its name and picture from the product, and on its band the
    /// store's price in the player's money ("…" until known), "owned" once bought, or "awaiting approval" while a purchase
    /// waits on someone's approval. Pressing a card buys it through the store (<see cref="Purchases"/>); how it went is
    /// told in the menu's message popup (nothing when the player cancelled). On iOS a "Restore Purchases" button sits
    /// under the cards (Apple requires it; elsewhere the purchases are fetched at every launch anyway).
    /// </summary>
    public sealed class ShopPanel : MonoBehaviour
    {
        [SerializeField] Popup popup;
        [SerializeField] ShopCard cardTemplate;
        [SerializeField] Popup message;   // the menu's shared popup, over this one
        [SerializeField] Vector2 messageSize = new Vector2(860, 560);
        [SerializeField] GameObject restoreRow;
        [SerializeField] UnityEngine.UI.Button restore;
        [SerializeField] RectTransform products;   // the cards' scroll view, made shorter by the restore row
        [SerializeField] Vector2 size = new Vector2(880, 900);
        [SerializeField] float restoreRoom = 140;   // the row and its gap

        readonly List<(ShopProduct product, ShopCard card)> cards = new List<(ShopProduct, ShopCard)>();
        bool ready;

        void Init()
        {
            if (ready) return;
            ready = true;
            Purchases.Start();   // (already started at launch: Boot; again if the store could not be reached)
            cardTemplate.gameObject.SetActive(false);
            foreach (var p in Purchases.Catalog)
            {
                var card = Instantiate(cardTemplate, cardTemplate.transform.parent);
                card.name = "Card_" + p.id;
                card.gameObject.SetActive(true);
                card.SetName(p.displayName);
                card.SetPicture(p.icon);
                var product = p;
                card.Button.onClick.AddListener(() => Buy(product));
                cards.Add((p, card));
            }
            restore.onClick.AddListener(Restore);
            bool restorable = Application.platform == RuntimePlatform.IPhonePlayer;
            restoreRow.SetActive(restorable);
            if (restorable)
            {
                popup.SetSize(size.x, size.y + restoreRoom);
                products.offsetMin = new Vector2(products.offsetMin.x, restoreRoom);
            }
            Purchases.Changed += Refresh;
            Purchases.Purchased += OnPurchased;
            Purchases.Deferred += OnDeferred;
            Purchases.Failed += OnFailed;
            Loc.Changed += Refresh;
        }

        void OnDestroy()
        {
            Purchases.Changed -= Refresh;
            Purchases.Purchased -= OnPurchased;
            Purchases.Deferred -= OnDeferred;
            Purchases.Failed -= OnFailed;
            Loc.Changed -= Refresh;
        }

        public void Open()
        {
            Init();
            if (Purchases.Status == Purchases.State.Unavailable) Purchases.Start();   // tried once more
            Refresh();
            popup.Show();
            Center();
        }

        // the cards in the middle of the window while they all fit; more than that, they start at the top and scroll
        void Center()
        {
            var grid = cardTemplate.transform.parent.GetComponent<UnityEngine.UI.GridLayoutGroup>();
            var viewport = (RectTransform)grid.transform.parent;
            Canvas.ForceUpdateCanvases();
            int columns = Mathf.Max(1, grid.constraintCount), rows = (cards.Count + columns - 1) / columns;
            float height = rows * grid.cellSize.y + Mathf.Max(0, rows - 1) * grid.spacing.y;
            int top = Mathf.Max(MinTopPad, Mathf.FloorToInt((viewport.rect.height - height) / 2));
            var pad = grid.padding;
            grid.padding = new RectOffset(pad.left, pad.right, top, pad.bottom);
        }
        const int MinTopPad = 8;

        void Refresh()
        {
            foreach (var (p, card) in cards)
            {
                string price = Purchases.Price(p.id);
                bool owned = Purchases.Owns(p.id) && p.type != UnityEngine.Purchasing.ProductType.Consumable;
                card.SetPriceText(owned ? Loc.T("shop.owned") : Purchases.IsDeferred(p.id) ? Loc.T("shop.pending") : price ?? "…");
                card.Button.interactable = !owned && !Purchases.IsDeferred(p.id);
            }
        }

        void Buy(ShopProduct p)
        {
            if (!Purchases.Buy(p)) Tell(Loc.T("shop.unavailable"));
        }

        void Restore()
        {
            if (!Purchases.Restore(ok => Tell(Loc.T(ok ? "shop.restored" : "shop.restoreFailed")))) Tell(Loc.T("shop.unavailable"));
        }

        void OnPurchased(ShopProduct p)
        {
            if (p != null && !p.purchasedMessage.IsEmpty) Tell(p.purchasedMessage.GetLocalizedString());
        }

        void OnDeferred(ShopProduct p) => Tell(Loc.T("shop.deferred"));

        void OnFailed(ShopProduct p, bool cancelled)
        {
            if (!cancelled) Tell(Loc.T("shop.failed"));
        }

        void Tell(string text)
        {
            if (!this || !message) return;
            message.SetIcon(null);
            message.SetSize(messageSize.x, messageSize.y);
            message.Show(Loc.T("mainmenu.shop"), text);
        }
    }
}
