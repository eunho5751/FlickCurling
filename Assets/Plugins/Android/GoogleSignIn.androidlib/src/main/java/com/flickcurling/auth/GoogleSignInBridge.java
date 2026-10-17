package com.flickcurling.auth;

import android.app.Activity;
import android.os.CancellationSignal;

import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;

import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

import java.util.concurrent.Executors;

/**
 * Shows Google's "choose an account" sheet (Sign in with Google, through Credential Manager) and hands the picked
 * account's OpenID ID token to the game, which links it with Unity Gaming Services. Called from C#
 * (GoogleIdentityProvider) with a Listener implemented there, so this needs nothing from Unity's own classes.
 */
public final class GoogleSignInBridge {
    public interface Listener {
        void onToken(String idToken);
        void onError(String message);
        void onCancel();
    }

    private GoogleSignInBridge() { }

    public static void signIn(final Activity activity, final String webClientId, final Listener listener) {
        GetSignInWithGoogleOption option = new GetSignInWithGoogleOption.Builder(webClientId).build();
        GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(option).build();
        CredentialManager.create(activity).getCredentialAsync(activity, request, new CancellationSignal(),
            Executors.newSingleThreadExecutor(),
            new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                @Override public void onResult(GetCredentialResponse response) {
                    Credential c = response.getCredential();
                    if (c instanceof CustomCredential
                            && GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(c.getType())) {
                        try {
                            listener.onToken(GoogleIdTokenCredential.createFrom(((CustomCredential) c).getData()).getIdToken());
                        } catch (Exception e) {
                            listener.onError("bad Google credential: " + e.getMessage());
                        }
                    } else {
                        listener.onError("unexpected credential type " + c.getType());
                    }
                }

                @Override public void onError(GetCredentialException e) {
                    if (e instanceof GetCredentialCancellationException) listener.onCancel();
                    else listener.onError(e.getClass().getSimpleName() + ": " + e.getMessage());
                }
            });
    }
}
