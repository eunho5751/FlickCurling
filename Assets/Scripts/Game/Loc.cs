using System;
using System.Collections.Generic;
using FlickCurling.Core;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace FlickCurling.Game
{
    /// <summary>
    /// Lookups into the "UI" string table. Most text is composed in code (names, scores), so this is the one place
    /// strings come from. Plain entries are cached per locale; Smart Strings take named arguments.
    /// </summary>
    public static class Loc
    {
        public const string Table = "UI";
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        public static event Action Changed;
        static bool hooked;

        public static readonly string[] Codes = { "ko", "en", "ja" };
        public static readonly string[] NativeNames = { "한국어", "English", "日本語" };

        public static void Init()
        {
            LocalizationSettings.InitializationOperation.WaitForCompletion();
            if (hooked) return;
            hooked = true;
            LocalizationSettings.SelectedLocaleChanged += _ => { cache.Clear(); Changed?.Invoke(); };
        }

        public static string Code => LocalizationSettings.SelectedLocale?.Identifier.Code ?? "ko";

        public static void Select(string code)
        {
            var locale = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(code));
            if (locale != null) LocalizationSettings.SelectedLocale = locale;
        }

        public static string T(string key)
        {
            if (cache.TryGetValue(key, out var s)) return s;
            s = LocalizationSettings.StringDatabase.GetLocalizedString(Table, key);
            cache[key] = s;
            return s;
        }

        /// <summary>A Smart String with named arguments: T("hud.turn", ("name", "흑")).</summary>
        public static string T(string key, params (string name, object value)[] args)
        {
            var d = new Dictionary<string, object>(args.Length);
            foreach (var (n, v) in args) d[n] = v;
            return LocalizationSettings.StringDatabase.GetLocalizedString(Table, key, new List<object> { d });
        }

        public static string Team(Team t) => T(t == Core.Team.B ? "team.B" : "team.W");
        public static string Level(AiLevel l) => T(l == AiLevel.Easy ? "level.easy" : l == AiLevel.Normal ? "level.normal" : "level.hard");
        public static string Map(string key) => T("map." + key);
    }
}
