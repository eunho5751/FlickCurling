using System.Collections;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace FlickCurling.Game
{
    /// <summary>
    /// The first scene (Assets/Scenes/Boot.unity, built by BootSceneBuilder): sets the app up once (60 fps), waits for
    /// Unity Localization to finish starting (locales and the preloaded UI table), so no screen ever asks it for a string
    /// before it is ready, then goes on to the main menu through the screen fade.
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
            ScreenFader.LoadScene(NextScene);
        }
    }
}
