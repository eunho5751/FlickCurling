using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// Google: on Android, Credential Manager's "Sign in with Google" sheet (pick an account) through the Java bridge in
    /// Assets/Plugins/Android/GoogleSignIn.androidlib, which returns an OpenID ID token for the Web client ID in
    /// <see cref="AccountConfig"/>; UGS links or signs in with it (Google must be set up as an identity provider in the
    /// UGS dashboard with the same client ID). In the editor, the browser's Google sign-in instead
    /// (<see cref="EditorGoogleSignIn"/>, once its client secret is set up). Not available on other platforms.
    /// </summary>
    public sealed class GoogleIdentityProvider : IIdentityProvider
    {
        public string TypeId => "google.com";

        // read each time, so a client ID set while the editor runs counts at the next press
        static string WebClientId => AccountConfig.Load().googleWebClientId?.Trim() ?? "";

        public bool IsSupported =>
#if UNITY_EDITOR
            !string.IsNullOrEmpty(WebClientId) && EditorGoogleSignIn.HasSecret;
#elif UNITY_ANDROID
            !string.IsNullOrEmpty(WebClientId);
#else
            false;
#endif

        public Task LinkAsync(string idToken, bool force = false) =>
            AuthenticationService.Instance.LinkWithGoogleAsync(idToken, new LinkOptions { ForceLink = force });
        public Task SignInAsync(string idToken) => AuthenticationService.Instance.SignInWithGoogleAsync(idToken);
        public Task UnlinkAsync() => AuthenticationService.Instance.UnlinkGoogleAsync();

        public Task<string> GetIdTokenAsync()
        {
#if UNITY_EDITOR
            return EditorGoogleSignIn.GetIdTokenAsync(WebClientId);
#elif UNITY_ANDROID
            var done = new TaskCompletionSource<string>();
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var bridge = new AndroidJavaClass("com.flickcurling.auth.GoogleSignInBridge"))
            {
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                bridge.CallStatic("signIn", activity, WebClientId, new Listener(done));
            }
            return done.Task;
#else
            return Task.FromException<string>(new NotSupportedException("Google sign-in runs on Android devices only"));
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // the Java bridge calls back on one of its threads; the task carries the result back to Unity's
        sealed class Listener : AndroidJavaProxy
        {
            readonly TaskCompletionSource<string> done;
            public Listener(TaskCompletionSource<string> done) : base("com.flickcurling.auth.GoogleSignInBridge$Listener") => this.done = done;
            void onToken(string idToken) => done.TrySetResult(idToken);
            void onError(string message) => done.TrySetException(new Exception(message));
            void onCancel() => done.TrySetCanceled();
        }
#endif
    }
}
