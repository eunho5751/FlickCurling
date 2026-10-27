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
        const string CountKey = "ads.matches";

        static InterstitialAd ad;
        static bool started, ready, loading, showing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ad = null; started = ready = loading = showing = false; PrivacyChanged = null; }

        static GameConfig Config => GameConfig.Load();
        static bool Off => Application.platform != RuntimePlatform.Android && !Application.isEditor;

        /// <summary>Consent, then the SDK, then the first ad (Boot, once).</summary>
        public static void Start()
        {
            if (started || Off) return;
            started = true;
            MobileAds.RaiseAdEventsOnUnityMainThread = true;
            // (in the editor the consent check is only a stand-in that never answers: straight to the SDK, which shows its
            // own stand-in ad)
            if (Application.isEditor) { StartSdk(); return; }
            // (the consent callbacks can come off the main thread: everything they do is put back on it, by UniTask, as
            // the SDK's own way back only runs once the SDK has started)
            ConsentInformation.Update(new ConsentRequestParameters(), updateError => OnMain(() =>
            {
                if (updateError != null) Debug.LogWarning("Ads: consent check failed: " + updateError.Message);
                ConsentForm.LoadAndShowConsentFormIfRequired(formError => OnMain(() =>
                {
                    if (formError != null) Debug.LogWarning("Ads: consent form failed: " + formError.Message);
                    PrivacyChanged?.Invoke();
                    // (without consent, or while the check could not be made, no ad is asked for; it is tried again at the next launch)
                    if (!ConsentInformation.CanRequestAds()) return;
                    StartSdk();
                }));
            }));
        }

        static void OnMain(Action a) => UniTask.Post(a);

        static void StartSdk() => MobileAds.Initialize(_ => OnMain(() => { ready = true; Load(); }));

        /// <summary>A match has come to its result (not the tutorial).</summary>
        public static void MatchPlayed()
        {
            if (Off) return;
            PlayerPrefs.SetInt(CountKey, PlayerPrefs.GetInt(CountKey) + 1);
            PlayerPrefs.Save();
        }

        static bool Due => PlayerPrefs.GetInt(CountKey) >= Mathf.Max(1, Config.adEveryMatches);

        /// <summary>
        /// Leaving a result card: the ad first if one is due (and loaded, and this player sees ads), <paramref name="then"/>
        /// once it is closed; otherwise straight on. An ad due but not loaded yet waits for the next time.
        /// </summary>
        public static void ShowIfDue(Action then)
        {
            if (showing) return;
            // (only once the store has said what this player owns: a buyer is never shown one while it is unknown)
            if (Off || Purchases.Status != Purchases.State.Ready || Purchases.NoAds || !Due || ad == null || !ad.CanShowAd()) { then(); return; }
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
            if (!ready || loading || ad != null || Purchases.NoAds) return;
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
