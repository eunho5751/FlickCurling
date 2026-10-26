using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

namespace FlickCurling.Game
{
    /// <summary>
    /// The store (Unity IAP): what the shop's products cost here, what this player owns, and buying. Started once at
    /// launch (Boot): it connects to the store, fetches the catalog's products (their prices in the player's own currency)
    /// and then the purchases the store account has. Nothing is kept on the device: what is owned is asked of the store at
    /// every launch. A purchase is granted once its receipt checks out (on Google Play, against the game's key: the
    /// GooglePlayTangle), then confirmed to the store. A purchase waiting on someone's approval (a parent's) is deferred:
    /// nothing is granted until it goes through, now or at a later launch.
    /// </summary>
    public static class Purchases
    {
        /// <summary>The "remove ads" product: owned, no ads are shown.</summary>
        public const string RemoveAdsId = "remove_ads";

        public enum State { Connecting, Ready, Unavailable }

        /// <summary>Connecting (and fetching), ready (prices and purchases known), or the store could not be reached.</summary>
        public static State Status { get; private set; } = State.Connecting;
        /// <summary>The player has bought "remove ads" (known once <see cref="Status"/> is Ready).</summary>
        public static bool NoAds => Owns(RemoveAdsId);

        /// <summary>Anything above has changed (status, prices, owned, deferred).</summary>
        public static event Action Changed;
        /// <summary>A purchase made since launch has gone through (bought now, or approved after being deferred).</summary>
        public static event Action<ShopProduct> Purchased;
        /// <summary>A purchase is waiting for approval.</summary>
        public static event Action<ShopProduct> Deferred;
        /// <summary>A purchase failed; true if the player cancelled it.</summary>
        public static event Action<ShopProduct, bool> Failed;

        static StoreController store;
        static List<ShopProduct> catalog = new List<ShopProduct>();
        static readonly HashSet<string> owned = new HashSet<string>(), deferred = new HashSet<string>(), asked = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            store = null; Status = State.Connecting;
            owned.Clear(); deferred.Clear(); asked.Clear();
            Changed = null; Purchased = null; Deferred = null; Failed = null;
        }

        public static IReadOnlyList<ShopProduct> Catalog => catalog;
        public static bool Owns(string id) => owned.Contains(id);
        public static bool IsDeferred(string id) => deferred.Contains(id);

        /// <summary>The price in the player's currency as the store gives it ("₩3,300"), or null while unknown.</summary>
        public static string Price(string id)
        {
            var p = store?.GetProductById(id);
            return p != null && p.availableToPurchase ? p.metadata.localizedPriceString : null;
        }

        /// <summary>Once, at launch; again (to try once more) after the store could not be reached.</summary>
        public static void Start()
        {
            if (store != null)
            {
                if (Status == State.Unavailable) Connect();
                return;
            }
            var c = ShopCatalog.Load();
            catalog = c ? c.products.Where(p => p && !string.IsNullOrEmpty(p.id)).ToList() : new List<ShopProduct>();
            store = UnityIAPServices.StoreController();
            // every event before connecting: purchases left from an earlier launch can come at once
            store.OnStoreConnected += OnConnected;
            store.OnStoreDisconnected += OnDisconnected;
            store.OnProductsFetched += OnProductsFetched;
            store.OnProductsFetchFailed += OnProductsFetchFailed;
            store.OnPurchasesFetched += OnPurchasesFetched;
            store.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
            store.OnPurchasePending += OnPending;
            store.OnPurchaseConfirmed += OnConfirmed;
            store.OnPurchaseFailed += OnFailed;
            store.OnPurchaseDeferred += OnDeferred;
            Connect();
        }

        /// <summary>Starts buying the product (the store's own dialog); false if the store is not ready for it.</summary>
        public static bool Buy(ShopProduct product)
        {
            if (Status != State.Ready || Price(product.id) == null) return false;
            asked.Add(product.id);
            store.PurchaseProduct(product.id);
            return true;
        }

        /// <summary>Asks the store for this account's earlier purchases again (the App Store's "Restore Purchases"; Apple
        /// requires the button). Each one found comes as a purchase and is granted; <paramref name="done"/> says whether
        /// the store answered. False if the store is not ready.</summary>
        public static bool Restore(Action<bool> done)
        {
            if (Status != State.Ready) return false;
            store.RestoreTransactions((ok, error) =>
            {
                if (!ok) Debug.LogWarning("FlickCurling: restoring purchases failed: " + error);
                done?.Invoke(ok);
            });
            return true;
        }

        static async void Connect()
        {
            Set(State.Connecting);
            try { await store.Connect(); }
            catch (Exception e) { Debug.LogWarning("FlickCurling: the store could not be reached: " + e.Message); Set(State.Unavailable); }
        }

        static void OnConnected() =>
            store.FetchProducts(catalog.Select(p => new ProductDefinition(p.id, p.type)).ToList());

        static void OnDisconnected(StoreConnectionFailureDescription f)
        {
            Debug.LogWarning("FlickCurling: the store disconnected: " + f.Message);
            Set(State.Unavailable);
        }

        // the prices first, then what is owned
        static void OnProductsFetched(List<Product> products) => store.FetchPurchases();

        static void OnProductsFetchFailed(ProductFetchFailed f)
        {
            Debug.LogWarning("FlickCurling: the store's products could not be fetched: " + f.FailureReason);
            store.FetchPurchases();   // (any fetched are listed; what is owned is still worth knowing)
        }

        // what the store account owns (the pending ones come through OnPending as well)
        static void OnPurchasesFetched(Orders orders)
        {
            owned.Clear();
            foreach (var o in orders.ConfirmedOrders)
                if (Valid(o)) owned.UnionWith(Ids(o));
            deferred.Clear();
            foreach (var o in orders.DeferredOrders) deferred.UnionWith(Ids(o));
            deferred.ExceptWith(owned);
            Set(State.Ready);
        }

        static void OnPurchasesFetchFailed(PurchasesFetchFailureDescription f)
        {
            Debug.LogWarning("FlickCurling: the purchases could not be fetched: " + f.Message);
            Set(State.Unavailable);
        }

        // paid (or approved): granted if the receipt checks out, then confirmed to the store; the same purchase can come
        // more than once (until confirmed), granting it again does no harm
        static void OnPending(PendingOrder order)
        {
            var ids = Ids(order).ToList();
            if (!Valid(order)) { Debug.LogWarning("FlickCurling: a purchase's receipt did not check out: " + string.Join(", ", ids)); return; }
            owned.UnionWith(ids);
            store.ConfirmPurchase(order);
            Changed?.Invoke();
            // bought since launch (or approved since being deferred): the player is told
            foreach (var id in ids)
                if (asked.Remove(id) | deferred.Remove(id)) Purchased?.Invoke(Product(id));
        }

        static void OnConfirmed(Order order)
        {
            if (order is FailedOrder f) Debug.LogWarning("FlickCurling: confirming a purchase failed: " + f.FailureReason + " " + f.Details);
        }

        static void OnFailed(FailedOrder order)
        {
            var ids = Ids(order).ToList();
            foreach (var id in ids) asked.Remove(id);
            bool cancelled = order.FailureReason == PurchaseFailureReason.UserCancelled;
            if (!cancelled) Debug.LogWarning("FlickCurling: a purchase failed: " + order.FailureReason + " " + order.Details);
            // already bought (on another device, say): what is owned is asked again
            if (order.FailureReason == PurchaseFailureReason.DuplicateTransaction) store.FetchPurchases();
            foreach (var id in ids) Failed?.Invoke(Product(id), cancelled);
            Changed?.Invoke();
        }

        // waiting for approval: nothing granted yet
        static void OnDeferred(DeferredOrder order)
        {
            foreach (var id in Ids(order))
            {
                asked.Remove(id);
                if (deferred.Add(id)) Deferred?.Invoke(Product(id));
            }
            Changed?.Invoke();
        }

        static IEnumerable<string> Ids(Order o) =>
            o.CartOrdered?.Items().Select(i => i.Product?.definition.id).Where(id => id != null) ?? Enumerable.Empty<string>();

        static ShopProduct Product(string id) => catalog.FirstOrDefault(p => p.id == id);

        // Google Play: the receipt's signature against the game's key. The App Store's StoreKit 2 checks its own; the
        // editor's fake store has nothing to check
        static bool Valid(Order o)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                new CrossPlatformValidator(GooglePlayTangle.Data(), Application.identifier).Validate(o.Info.Receipt);
                return true;
            }
            catch (Exception e) { Debug.LogWarning("FlickCurling: receipt check failed: " + e.Message); return false; }
#else
            return true;
#endif
        }

        static void Set(State s)
        {
            Status = s;
            Changed?.Invoke();
        }
    }
}
