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
    /// UGS dashboard with the same client ID). Not available in the editor or on other platforms.
    /// </summary>
    public sealed class GoogleIdentityProvider : IIdentityProvider
    {
        public string TypeId => "google.com";

        readonly string webClientId;

        public GoogleIdentityProvider(string webClientId) => this.webClientId = webClientId;

        public bool IsSupported =>
#if UNITY_ANDROID && !UNITY_EDITOR
            !string.IsNullOrEmpty(webClientId);
#else
            false;
#endif

        public Task LinkAsync(string idToken) => AuthenticationService.Instance.LinkWithGoogleAsync(idToken);
        public Task SignInAsync(string idToken) => AuthenticationService.Instance.SignInWithGoogleAsync(idToken);

        public Task<string> GetIdTokenAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var done = new TaskCompletionSource<string>();
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var bridge = new AndroidJavaClass("com.flickcurling.auth.GoogleSignInBridge"))
            {
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                bridge.CallStatic("signIn", activity, webClientId, new Listener(done));
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
