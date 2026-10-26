using System.Collections;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace FlickCurling.Game
{
    /// <summary>
    /// The first scene (Assets/Scenes/Boot.unity, built by BootSceneBuilder): sets the app up once (60 fps), waits for
    /// Unity Localization to finish starting (locales and the preloaded UI table), so no screen ever asks it for a string
    /// before it is ready, starts the store (Purchases), then goes on to the main menu through the screen fade, or first
    /// to the tutorial (in the game scene) for a player who has not done it.
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        public const string NextScene = "MainMenu";
        public const int TargetFrameRate = 60;

        IEnumerator Start()
        {
            Application.targetFrameRate = TargetFrameRate;
            yield return LocalizationSettings.InitializationOperation;
            Loc.Init();   // already done, so this only hooks the language change
            Purchases.Start();   // the store: prices and what this player owns, fetched in the background
            var needed = TutorialProgress.NeededAsync();
            while (!needed.IsCompleted) yield return null;
            if (needed.Result) { MatchLaunch.SetTutorial(); ScreenFader.LoadScene(MatchLaunch.GameScene); }
            else ScreenFader.LoadScene(NextScene);
        }
    }
}
