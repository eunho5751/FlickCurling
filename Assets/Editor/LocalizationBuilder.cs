using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// Creates the Localization settings, the ko / en / ja locales and the "UI" string table collection, and writes
    /// every entry of <see cref="UiStrings"/> into it. Safe to run again: existing assets are reused, entries overwritten.
    /// </summary>
    public static class LocalizationBuilder
    {
        public const string Root = "Assets/Localization";
        public const string TableName = "UI";

        [MenuItem("FlickCurling/Build Localization Tables")]
        public static void Build()
        {
            Directory.CreateDirectory(Root + "/Locales");
            Directory.CreateDirectory(Root + "/Tables");

            var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (settings == null)
            {
                settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(Root + "/LocalizationSettings.asset");
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                    settings.name = "LocalizationSettings";
                    AssetDatabase.CreateAsset(settings, Root + "/LocalizationSettings.asset");
                }
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            }

            // locales: ko is the project (source) locale
            foreach (var code in UiStrings.Locales)
            {
                var id = new LocaleIdentifier(code);
                if (LocalizationEditorSettings.GetLocale(id) != null) continue;
                string path = $"{Root}/Locales/{code}.asset";
                var locale = AssetDatabase.LoadAssetAtPath<Locale>(path);
                if (locale == null)
                {
                    locale = Locale.CreateLocale(id);
                    AssetDatabase.CreateAsset(locale, path);
                }
                LocalizationEditorSettings.AddLocale(locale, false);
            }
            var ko = LocalizationEditorSettings.GetLocale(new LocaleIdentifier("ko"));
            LocalizationSettings.ProjectLocale = ko;

            // first launch: a choice the player made before, else the device language, else English
            var selectors = settings.GetStartupLocaleSelectors();
            selectors.Clear();
            selectors.Add(new PlayerPrefLocaleSelector { PlayerPreferenceKey = "flickcurling-locale" });
            selectors.Add(new CommandLineLocaleSelector());
            selectors.Add(new SystemLocaleSelector());
            selectors.Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier("en") });
            EditorUtility.SetDirty(settings);

            var collection = LocalizationEditorSettings.GetStringTableCollection(TableName)
                             ?? LocalizationEditorSettings.CreateStringTableCollection(TableName, Root + "/Tables");
            for (int li = 0; li < UiStrings.Locales.Length; li++)
            {
                var id = new LocaleIdentifier(UiStrings.Locales[li]);
                var table = collection.GetTable(id) as StringTable ?? collection.AddNewTable(id) as StringTable;
                foreach (var e in UiStrings.Entries)
                {
                    string value = e[li + 1];
                    var entry = table.AddEntry(e[0], value);
                    entry.IsSmart = value.Contains("{");
                }
                EditorUtility.SetDirty(table);
            }
            // drop keys that are no longer in the source
            var keys = UiStrings.Entries.Select(e => e[0]).ToHashSet();
            foreach (var k in collection.SharedData.Entries.Select(e => e.Key).Where(k => !keys.Contains(k)).ToList())
                collection.RemoveEntry(k);
            EditorUtility.SetDirty(collection.SharedData);
            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();
            Debug.Log($"FlickCurling: wrote {UiStrings.Entries.Length} entries x {UiStrings.Locales.Length} locales to the '{TableName}' table");
        }
    }
}
