using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// Apple, for the iOS release (not built yet): the shape is ready, the native part is not. To finish it, add a Sign in
    /// with Apple plugin (for example the Apple Unity plugin or "Sign in with Apple Unity Plugin" by lupidan), enable the
    /// Sign in with Apple capability in Xcode, set Apple up as an identity provider in the UGS dashboard, then have
    /// <see cref="GetIdTokenAsync"/> return the identity token from the plugin's login and turn <see cref="IsSupported"/>
    /// on for iOS. Linking and signing in already go through UGS.
    /// </summary>
    public sealed class AppleIdentityProvider : IIdentityProvider
    {
        public string TypeId => "apple.com";

        public bool IsSupported => false;   // iOS: true once the native sign-in below is in place

        public Task<string> GetIdTokenAsync() =>
            Task.FromException<string>(new NotSupportedException("Sign in with Apple is not set up yet"));

        public Task LinkAsync(string idToken) => AuthenticationService.Instance.LinkWithAppleAsync(idToken);
        public Task SignInAsync(string idToken) => AuthenticationService.Instance.SignInWithAppleAsync(idToken);
    }
}
