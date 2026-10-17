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
    /// belongs to another player, the player chooses (<see cref="LinkConflict"/>): load that player, or keep this one and
    /// move the link over to it.
    /// </summary>
    public static class AccountService
    {
        public enum State { Offline, SigningIn, Ready, Failed }

        /// <summary>A player as the conflict choice shows them.</summary>
        public sealed class PlayerSummary
        {
            public string Id = "", Name = "";
            public DateTime? CreatedAt;
        }

        /// <summary>Linking found the account already on another player: the two to choose between (Existing is null
        /// when it could not be looked up).</summary>
        public sealed class LinkConflict
        {
            public IIdentityProvider Provider;
            public PlayerSummary Current, Existing;
        }

        public enum ConflictChoice { Cancel, LoadExisting, KeepCurrent }

        /// <summary>Raised on the main thread whenever the state, the player or their links change.</summary>
        public static event Action Changed;

        public static State Status { get; private set; } = State.Offline;
        public static bool Busy { get; private set; }
        public static string PlayerId { get; private set; } = "";
        public static string PlayerName { get; private set; } = "";
        public static string LastError { get; private set; } = "";

        public static readonly IIdentityProvider Google = new GoogleIdentityProvider();
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

        // who the account's player is, without losing this one: sign in as them under a scratch profile (its own session),
        // then come back to this player's session in the original profile
        static async Task<PlayerSummary> PeekAsync(IIdentityProvider provider, string token)
        {
            var auth = AuthenticationService.Instance;
            string profile = auth.Profile;
            PlayerSummary other = null;
            auth.SignOut();   // keeps this player's session
            try
            {
                auth.SwitchProfile(PeekProfile);
                await provider.SignInAsync(token);
                var info = await auth.GetPlayerInfoAsync();
                other = new PlayerSummary { Id = auth.PlayerId ?? "", Name = await auth.GetPlayerNameAsync(), CreatedAt = info?.CreatedAt };
            }
            catch (Exception e) { Debug.LogWarning("FlickCurling: could not look up the linked player: " + e.Message); }
            finally
            {
                if (auth.IsSignedIn) auth.SignOut(true);   // and forget the scratch session
                auth.SwitchProfile(profile);
            }
            await auth.SignInAnonymouslyAsync();   // back to this player (their session, not a new guest)
            return other;
        }

        const string PeekProfile = "linkpeek";

        /// <summary>Whether the signed-in player is linked to <paramref name="provider"/>'s account.</summary>
        public static bool IsLinked(IIdentityProvider provider)
        {
            if (Status != State.Ready || UnityServices.State != ServicesInitializationState.Initialized) return false;
            var ids = AuthenticationService.Instance.PlayerInfo?.Identities;
            return ids != null && ids.Any(i => i.TypeId == provider.TypeId);
        }

        /// <summary>
        /// Shows the provider's account chooser and links the picked account to this player. If that account already has
        /// a player of its own, <paramref name="resolve"/> chooses: sign in as that player, or move the link to this one
        /// (that player can then no longer be reached through the account), or neither. Returns whether the link went
        /// through; false with an empty <see cref="LastError"/> when cancelled.
        /// </summary>
        public static async Task<bool> LinkAsync(IIdentityProvider provider, Func<LinkConflict, Task<ConflictChoice>> resolve)
        {
            if (Busy || Status != State.Ready || !provider.IsSupported) return false;
            Busy = true; LastError = ""; Notify();
            try
            {
                string token = await provider.GetIdTokenAsync();
                try { await provider.LinkAsync(token); }
                catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
                {
                    // the account is already someone's: let the player choose
                    var conflict = new LinkConflict
                    {
                        Provider = provider,
                        Current = new PlayerSummary { Id = PlayerId, Name = PlayerName, CreatedAt = AuthenticationService.Instance.PlayerInfo?.CreatedAt },
                        Existing = await PeekAsync(provider, token),
                    };
                    var choice = resolve != null ? await resolve(conflict) : ConflictChoice.Cancel;
                    if (choice == ConflictChoice.Cancel) return false;
                    if (choice == ConflictChoice.LoadExisting)
                    {
                        AuthenticationService.Instance.SignOut();
                        await provider.SignInAsync(token);
                    }
                    else await provider.LinkAsync(token, force: true);
                }
                await RefreshAsync();
                return true;
            }
            catch (TaskCanceledException) { return false; }   // closed the chooser
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: linking " + provider.TypeId + " failed: " + e.Message);
                LastError = e.Message;
                // lost the way back to this player (the conflict look-up signs out): offline until StartAsync retries
                if (!AuthenticationService.Instance.IsSignedIn) { Status = State.Failed; starting = null; }
                return false;
            }
            finally { Busy = false; Notify(); }
        }

        /// <summary>Removes <paramref name="provider"/>'s account from this player, who is then a guest again (kept on this
        /// device only). Returns false (with <see cref="LastError"/>) if it did not.</summary>
        public static async Task<bool> UnlinkAsync(IIdentityProvider provider)
        {
            if (Busy || !IsLinked(provider)) return false;
            Busy = true; LastError = ""; Notify();
            try
            {
                await provider.UnlinkAsync();
                await RefreshAsync();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: unlinking " + provider.TypeId + " failed: " + e.Message);
                LastError = e.Message;
                return false;
            }
            finally { Busy = false; Notify(); }
        }

#if UNITY_EDITOR
        /// <summary>Testing: forgets this device's player (the session token) and signs in as a brand-new guest. The old
        /// player stays on the server, still reachable through any account linked to it.</summary>
        public static async Task<bool> StartOverAsGuestAsync()
        {
            if (Busy || Status != State.Ready) return false;
            Busy = true; LastError = ""; Notify();
            try
            {
                var auth = AuthenticationService.Instance;
                auth.SignOut(true);   // and clear the credentials
                auth.ClearSessionToken();
                await auth.SignInAnonymouslyAsync();
                await RefreshAsync();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: starting over as a guest failed: " + e.Message);
                LastError = e.Message;
                Status = State.Failed; starting = null;
                return false;
            }
            finally { Busy = false; Notify(); }
        }
#endif

        static void Notify() => Changed?.Invoke();
    }
}
