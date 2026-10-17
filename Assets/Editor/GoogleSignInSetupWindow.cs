using FlickCurling.Game.Account;
using UnityEditor;
using UnityEngine;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// FlickCurling > Google Sign-In (Editor) Setup: the Web client ID (into Resources/AccountConfig, which the game uses
    /// everywhere) and that client's secret (into UserSettings/GoogleOAuth.json on this PC only, for the editor's browser
    /// sign-in, EditorGoogleSignIn), and the redirect URI to register on the Web client.
    /// </summary>
    public sealed class GoogleSignInSetupWindow : EditorWindow
    {
        const string ConfigPath = "Assets/Resources/" + AccountConfig.ResourcePath + ".asset";

        string clientId = "", secret = "";
        bool hadSecret;

        [MenuItem("FlickCurling/Google Sign-In (Editor) Setup")]
        static void Open()
        {
            var w = GetWindow<GoogleSignInSetupWindow>(true, "Google Sign-In Setup");
            w.minSize = new Vector2(520, 300);
        }

        void OnEnable()
        {
            clientId = AccountConfig.Load().googleWebClientId ?? "";
            hadSecret = EditorGoogleSignIn.HasSecret;
            secret = "";
        }

        void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Web 애플리케이션 클라이언트 (Google Cloud Console)", EditorStyles.boldLabel);
            clientId = EditorGUILayout.TextField("Client ID", clientId);
            EditorGUILayout.HelpBox("게임 전체(Android 포함)가 쓰는 값이에요. Assets/Resources/AccountConfig.asset에 저장돼요. "
                + "UGS 대시보드의 Google 공급자에도 같은 ID를 넣어 주세요.", MessageType.None);

            EditorGUILayout.Space(6);
            secret = EditorGUILayout.PasswordField("Client Secret", secret);
            EditorGUILayout.HelpBox((hadSecret ? "저장된 비밀번호가 있어요. 바꿀 때만 입력하세요. " : "")
                + "에디터 테스트에만 써요. " + EditorGoogleSignIn.SecretPath + "에 저장되고 Git과 빌드에는 들어가지 않아요.",
                MessageType.None);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("승인된 리디렉션 URI (웹 클라이언트에 등록)", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(EditorGoogleSignIn.RedirectUri, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            EditorGUILayout.Space(10);
            if (GUILayout.Button("저장", GUILayout.Height(28))) Save();
        }

        void Save()
        {
            var config = AssetDatabase.LoadAssetAtPath<AccountConfig>(ConfigPath);
            if (config == null) { config = CreateInstance<AccountConfig>(); AssetDatabase.CreateAsset(config, ConfigPath); }
            config.googleWebClientId = clientId.Trim();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            if (!string.IsNullOrEmpty(secret.Trim())) { EditorGoogleSignIn.WriteSecret(secret); hadSecret = true; secret = ""; }
            ShowNotification(new GUIContent("저장했어요"));
        }
    }
}
