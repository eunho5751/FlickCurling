using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// The player's UGS account. At start-up the game signs in: the first time as a new guest (anonymous), afterwards as the
    /// same player again (UGS keeps the session token on the device), which may by then be a linked one. From there the
    /// player can link a platform account (<see cref="Google"/>, later <see cref="Apple"/>); if that account already
    /// belongs to another player, the game switches to that player instead.
    /// </summary>
    public static class AccountService
    {
        public enum State { Offline, SigningIn, Ready, Failed }

        /// <summary>Raised on the main thread whenever the state, the player or their links change.</summary>
        public static event Action Changed;

        public static State Status { get; private set; } = State.Offline;
        public static bool Busy { get; private set; }
        public static string PlayerId { get; private set; } = "";
        public static string PlayerName { get; private set; } = "";
        public static string LastError { get; private set; } = "";

        public static readonly IIdentityProvider Google = new GoogleIdentityProvider(AccountConfig.Load().googleWebClientId);
        public static readonly IIdentityProvider Apple = new AppleIdentityProvider();

        static Task starting;

        // a fresh start each play: with domain reload off in the editor, statics would otherwise carry the last play's
        // "signed in" over a services instance that is no longer there
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Status = State.Offline; Busy = false; PlayerId = PlayerName = LastError = ""; starting = null; Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => _ = StartAsync();

        /// <summary>Signs in (once; again after a failure).</summary>
        public static Task StartAsync() => starting ??= SignInAsync();

        static async Task SignInAsync()
        {
            Status = State.SigningIn; Notify();
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
                var auth = AuthenticationService.Instance;
                // a guest the first time; the same player (guest or linked) after that
                if (!auth.IsSignedIn) await auth.SignInAnonymouslyAsync();
                await RefreshAsync();
                Status = State.Ready;
            }
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: sign-in failed: " + e.Message);
                LastError = e.Message;
                Status = State.Failed;
                starting = null;   // the next StartAsync tries again
            }
            Notify();
        }

        static async Task RefreshAsync()
        {
            var auth = AuthenticationService.Instance;
            await auth.GetPlayerInfoAsync();
            PlayerId = auth.PlayerId ?? "";
            PlayerName = await auth.GetPlayerNameAsync();   // UGS makes one up the first time (Name#1234)
        }

        /// <summary>Whether the signed-in player is linked to <paramref name="provider"/>'s account.</summary>
        public static bool IsLinked(IIdentityProvider provider)
        {
            if (Status != State.Ready || UnityServices.State != ServicesInitializationState.Initialized) return false;
            var ids = AuthenticationService.Instance.PlayerInfo?.Identities;
            return ids != null && ids.Any(i => i.TypeId == provider.TypeId);
        }

        /// <summary>
        /// Shows the provider's account chooser and links the picked account to this player, or, if that account already
        /// has a player of its own, signs in as that player. Returns false (with <see cref="LastError"/>) if it did not.
        /// </summary>
        public static async Task<bool> LinkAsync(IIdentityProvider provider)
        {
            if (Busy || Status != State.Ready || !provider.IsSupported) return false;
            Busy = true; LastError = ""; Notify();
            try
            {
                string token = await provider.GetIdTokenAsync();
                try { await provider.LinkAsync(token); }
                catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
                {
                    // the account is already someone's: carry on as that player
                    AuthenticationService.Instance.SignOut();
                    await provider.SignInAsync(token);
                }
                await RefreshAsync();
                return true;
            }
            catch (TaskCanceledException) { return false; }   // closed the chooser
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: linking " + provider.TypeId + " failed: " + e.Message);
                LastError = e.Message;
                return false;
            }
            finally { Busy = false; Notify(); }
        }

        static void Notify() => Changed?.Invoke();
    }
}
