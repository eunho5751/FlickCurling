using System;
using Cysharp.Threading.Tasks;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>
    /// AdMob: a full-screen (interstitial) ad once every few matches (GameConfig.adEveryMatches), shown as the player
    /// leaves a match's result card (for the menu or to play again), never during a match, and never for a player who has
    /// bought "remove ads". Started once at launch (Boot): first Google's consent check (UMP; in the EEA and the UK the
    /// consent form comes up the first time), then the SDK, then the first ad is loaded so it is ready when it is due. The
    /// matches played are counted on the device (PlayerPrefs), so quitting does not reset the count. Android only: on other
    /// platforms (and in the editor without the SDK's stand-in) nothing is shown and the game goes straight on.
    /// </summary>
    public static class Ads
    {
        const string CountKey = "ads.matches", NoAdsKey = "ads.noads";

        static InterstitialAd ad;
        static bool started, sdkAsked, ready, loading, showing;
        static int mainThread;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ad = null; started = sdkAsked = ready = loading = showing = false; PrivacyChanged = null; }

        static GameConfig Config => GameConfig.Load();
        static bool Off => Application.platform != RuntimePlatform.Android && !Application.isEditor;

#if UNITY_EDITOR
        // play mode started from another scene than Boot (the main menu, the game): Boot's start-up never ran, so the
        // store and the ads are started here, or no ad could ever be shown while testing
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartWithoutBoot()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Boot") return;
            Purchases.Start();
            Start();
        }
#endif

        /// <summary>Consent, then the SDK, then the first ad (Boot, once).</summary>
        public static void Start()
        {
            if (started || Off) return;
            started = true;
            mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            Purchases.Changed += RememberNoAds;
            MobileAds.RaiseAdEventsOnUnityMainThread = true;
            // (in the editor the consent check is only a stand-in that never answers: straight to the SDK, which shows its
            // own stand-in ad)
            if (Application.isEditor) { StartSdk(); return; }
            // Google's order: the consent check, and the form only where it is still needed; the SDK as soon as ads may be
            // asked for (already now, with consent from an earlier launch)
            ConsentInformation.Update(new ConsentRequestParameters(), updateError => OnMain(() =>
            {
                if (updateError != null) Debug.LogWarning("Ads: consent check failed: " + updateError.Message);
                PrivacyChanged?.Invoke();
                if (ConsentInformation.CanRequestAds()) { StartSdk(); return; }
                ConsentForm.LoadAndShowConsentFormIfRequired(formError => OnMain(() =>
                {
                    if (formError != null) Debug.LogWarning("Ads: consent form failed: " + formError.Message);
                    PrivacyChanged?.Invoke();
                    // (without consent, or while the check could not be made, no ad is asked for; it is tried again at the next launch)
                    if (!ConsentInformation.CanRequestAds()) { Debug.LogWarning("Ads: no consent to ask for ads (yet): the SDK is not started"); return; }
                    StartSdk();
                }));
            }));
            if (ConsentInformation.CanRequestAds()) StartSdk();
        }

        // the consent callbacks can come on another thread: run there, a step goes on at once; elsewhere it is put back
        // on the main thread (by UniTask: the SDK's own way back only runs once the SDK has started)
        static void OnMain(Action a)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == mainThread) a();
            else UniTask.Post(a);
        }

        // whether this player bought "remove ads": the store's answer once it has one, kept on the device for a launch where
        // the store cannot be reached (a buyer then still sees none; a player the store has never answered for sees them)
        static bool NoAds => Purchases.Status == Purchases.State.Ready ? Purchases.NoAds : PlayerPrefs.GetInt(NoAdsKey) == 1;

        static void RememberNoAds()
        {
            if (Purchases.Status != Purchases.State.Ready) return;
            PlayerPrefs.SetInt(NoAdsKey, Purchases.NoAds ? 1 : 0); PlayerPrefs.Save();
        }

        static void StartSdk()
        {
            if (sdkAsked) return;
            sdkAsked = true;
            MobileAds.Initialize(_ => OnMain(() => { ready = true; Load(); }));
        }

        /// <summary>A match has come to its result (not the tutorial).</summary>
        public static void MatchPlayed()
        {
            if (Off) return;
            PlayerPrefs.SetInt(CountKey, PlayerPrefs.GetInt(CountKey) + 1);
            PlayerPrefs.Save();
            Load();   // (an earlier load that failed is tried again, in time for the result card's way out)
        }

        static bool Due => PlayerPrefs.GetInt(CountKey) >= Mathf.Max(1, Config.adEveryMatches);

        /// <summary>
        /// Leaving a result card: the ad first if one is due (and loaded, and this player sees ads), <paramref name="then"/>
        /// once it is closed; otherwise straight on. An ad due but not loaded yet waits for the next time.
        /// </summary>
        public static void ShowIfDue(Action then)
        {
            if (showing) return;
            if (Off || NoAds || !Due) { then(); return; }
            if (ad == null || !ad.CanShowAd())
            {
                Load();   // (due but not loaded: tried again, shown at the next way out)
                then();
                return;
            }
            showing = true;
            var shown = ad; ad = null;
            void Done()
            {
                if (!showing) return;
                showing = false;
                shown.Destroy();
                PlayerPrefs.SetInt(CountKey, 0); PlayerPrefs.Save();
                Load();
                then();
            }
            shown.OnAdFullScreenContentClosed += Done;
            shown.OnAdFullScreenContentFailed += e => { Debug.LogWarning("Ads: the ad could not be shown: " + e.GetMessage()); Done(); };
            shown.Show();
        }

        static void Load()
        {
            if (!ready || loading || ad != null || NoAds) return;
            string unit = Config.adUnitAndroid;
            if (string.IsNullOrWhiteSpace(unit)) { Debug.LogWarning("Ads: no ad unit set in GameConfig"); return; }
            loading = true;
            InterstitialAd.Load(unit.Trim(), new AdRequest(), (loaded, error) =>
            {
                loading = false;
                if (error != null || loaded == null) { Debug.LogWarning("Ads: loading failed: " + error?.GetMessage()); return; }
                ad = loaded;
            });
        }

        // ---------------- privacy options (UMP) ----------------

        /// <summary>The player must be able to change their ad consent from the game (in the EEA and the UK): the settings
        /// popup then shows a "privacy options" link.</summary>
        public static bool PrivacyOptionsRequired =>
            started && !Application.isEditor && ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;

        /// <summary>Whether that link is needed may have changed (the consent check came back).</summary>
        public static event Action PrivacyChanged;

        /// <summary>Google's form to change the ad consent.</summary>
        public static void ShowPrivacyOptions()
        {
            ConsentForm.ShowPrivacyOptionsForm(error => OnMain(() =>
            {
                if (error != null) Debug.LogWarning("Ads: privacy options failed: " + error.Message);
                PrivacyChanged?.Invoke();
            }));
        }
    }
}
