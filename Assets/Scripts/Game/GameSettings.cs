using System;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>
    /// The player's settings, kept in PlayerPrefs and shared by every scene: music and sound effect volumes (0..1) and the
    /// aim sensitivity level (0..4, x0.5 to x2). The language is Unity Localization's own selected locale (saved by its
    /// PlayerPrefLocaleSelector).
    /// </summary>
    public static class GameSettings
    {
        const string MusicPref = "flickcurling-music", SfxPref = "flickcurling-sfx";
        public const string SensPref = "flickcurling-aimsens";
        public const int SensLevels = 5, DefaultSens = 2;

        /// <summary>Raised when a setting changes.</summary>
        public static event Action Changed;

        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicPref, 0.8f);
            set => Set(() => PlayerPrefs.SetFloat(MusicPref, Mathf.Clamp01(value)));
        }

        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(SfxPref, 1f);
            set => Set(() => PlayerPrefs.SetFloat(SfxPref, Mathf.Clamp01(value)));
        }

        public static int SensLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(SensPref, DefaultSens), 0, SensLevels - 1);
            set => Set(() => PlayerPrefs.SetInt(SensPref, Mathf.Clamp(value, 0, SensLevels - 1)));
        }

        static void Set(Action write)
        {
            write();
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
