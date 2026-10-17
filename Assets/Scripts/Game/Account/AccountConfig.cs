using UnityEngine;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// Account settings (Assets/Resources/AccountConfig.asset): the OAuth client IDs the platform sign-ins use. Google's
    /// is the "Web application" client ID from Google Cloud Console (APIs and Services, Credentials) of the project whose
    /// Android client has this app's package name and signing SHA-1; the same ID goes into the UGS dashboard's Google
    /// identity provider.
    /// </summary>
    [CreateAssetMenu(fileName = "AccountConfig", menuName = "FlickCurling/Account Config")]
    public sealed class AccountConfig : ScriptableObject
    {
        public const string ResourcePath = "AccountConfig";

        [Tooltip("Google Cloud Console: the Web application OAuth client ID (…apps.googleusercontent.com)")]
        public string googleWebClientId = "";

        public static AccountConfig Load()
        {
            var c = Resources.Load<AccountConfig>(ResourcePath);
            return c != null ? c : CreateInstance<AccountConfig>();
        }
    }
}
