using System.Threading.Tasks;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// A platform account the player can link to their UGS player (Google now, Apple later): it knows whether it can run
    /// on this device, gets an ID token from the platform's own sign-in sheet, and hands that token to UGS Authentication
    /// to link the current player or to sign in as the player already linked to it.
    /// </summary>
    public interface IIdentityProvider
    {
        /// <summary>The UGS identity type id ("google.com", "apple.com"), as it shows in PlayerInfo.Identities.</summary>
        string TypeId { get; }

        /// <summary>Whether this provider can sign in on this device and build.</summary>
        bool IsSupported { get; }

        /// <summary>Shows the platform's sign-in (an account chooser) and returns the ID token. Throws when cancelled or failed.</summary>
        Task<string> GetIdTokenAsync();

        /// <summary>Links the token's account to the signed-in UGS player.</summary>
        Task LinkAsync(string idToken);

        /// <summary>Signs in as the UGS player already linked to the token's account.</summary>
        Task SignInAsync(string idToken);
    }
}
