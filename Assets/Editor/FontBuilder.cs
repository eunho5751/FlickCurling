using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// Builds dynamic TMP font assets from the Noto Sans files in Assets/Fonts (SIL OFL, notofonts/noto-cjk):
    /// body = Noto Sans KR Regular, display = Noto Sans KR Bold (Korean + Latin), each falling back to the matching
    /// Noto Sans JP weight for kana and kanji. Also makes the body font the TMP default.
    /// The main menu buttons use a rounder casual pair (both SIL OFL, google/fonts): Jua for Korean and Latin, falling
    /// back to Mochiy Pop One for Japanese, then to the Noto display fonts for anything either lacks.
    /// </summary>
    public static class FontBuilder
    {
        public const string Dir = "Assets/Fonts";
        public const string BodyPath = Dir + "/NotoSansKR-Regular SDF.asset";
        public const string DisplayPath = Dir + "/NotoSansKR-Bold SDF.asset";
        public const string JpPath = Dir + "/NotoSansJP-Regular SDF.asset";
        public const string JpBoldPath = Dir + "/NotoSansJP-Bold SDF.asset";
        public const string ButtonPath = Dir + "/Jua-Regular SDF.asset";
        public const string ButtonJpPath = Dir + "/MochiyPopOne-Regular SDF.asset";

        [MenuItem("FlickCurling/Build Font Assets")]
        public static void Build()
        {
            var jp = Make(Dir + "/NotoSansJP-Regular.otf", JpPath, null);
            var jpBold = Make(Dir + "/NotoSansJP-Bold.otf", JpBoldPath, null);
            var body = Make(Dir + "/NotoSansKR-Regular.otf", BodyPath, jp);
            var display = Make(Dir + "/NotoSansKR-Bold.otf", DisplayPath, jpBold);
            var buttonJp = Make(Dir + "/MochiyPopOne-Regular.ttf", ButtonJpPath, jpBold);
            Make(Dir + "/Jua-Regular.ttf", ButtonPath, buttonJp, display);

            // TMP Settings: default font + global fallback, so any TMP text in the project can show ko / en / ja
            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                so.FindProperty("m_defaultFontAsset").objectReferenceValue = body;
                // Korean breaks lines at spaces (word by word), not between any two syllables
                var hangul = so.FindProperty("m_UseModernHangulLineBreakingRules");
                if (hangul != null) hangul.boolValue = true;
                var fb = so.FindProperty("m_fallbackFontAssets");
                fb.ClearArray();
                fb.InsertArrayElementAtIndex(0);
                fb.GetArrayElementAtIndex(0).objectReferenceValue = jp;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("FlickCurling: built TMP font assets (body, display, Japanese fallback, menu buttons)");
        }

        static TMP_FontAsset Make(string fontPath, string assetPath, params TMP_FontAsset[] fallbacks)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null) throw new FileNotFoundException("FlickCurling: font file missing", fontPath);
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (fa == null)
            {
                // dynamic: glyphs are rendered into the atlas on demand, so the whole CJK set need not be baked
                fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                fa.name = Path.GetFileNameWithoutExtension(assetPath);
                AssetDatabase.CreateAsset(fa, assetPath);
                fa.atlasTextures[0].name = fa.name + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = fa.name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            fa.isMultiAtlasTexturesEnabled = true;
            fa.ClearFontAssetData(true);   // keep the committed atlas empty; it fills at run time
            fa.fallbackFontAssetTable = new List<TMP_FontAsset>(System.Array.FindAll(fallbacks ?? new TMP_FontAsset[0], f => f != null));
            EditorUtility.SetDirty(fa);
            return fa;
        }
    }
}
