#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// Google sign-in for testing in the editor, where Android's account chooser is not there: opens Google's sign-in page
    /// in the browser (OAuth authorization code with PKCE), takes the code back on a local redirect
    /// (<see cref="RedirectUri"/>, registered on the Web client in Google Cloud Console) and trades it for an ID token
    /// for the same Web client ID the game uses, so UGS takes it as it takes the Android one. The trade needs the Web
    /// client's secret, which stays on this PC in <see cref="SecretPath"/> (UserSettings is not in git) and is never
    /// built into the game. FlickCurling > Google Sign-In (Editor) Setup writes it.
    /// </summary>
    public static class EditorGoogleSignIn
    {
        public const int Port = 53682;
        public const string RedirectUri = "http://127.0.0.1:53682/";
        public const string SecretPath = "UserSettings/GoogleOAuth.json";   // relative to the project folder

        const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth", TokenUrl = "https://oauth2.googleapis.com/token";
        static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

        [Serializable] public sealed class Secret { public string clientSecret = ""; }
        [Serializable] sealed class TokenResponse { public string id_token = "", error = "", error_description = ""; }

        public static string ReadSecret()
        {
            try { return File.Exists(SecretPath) ? JsonUtility.FromJson<Secret>(File.ReadAllText(SecretPath)).clientSecret ?? "" : ""; }
            catch (Exception) { return ""; }
        }

        public static void WriteSecret(string secret) =>
            File.WriteAllText(SecretPath, JsonUtility.ToJson(new Secret { clientSecret = secret.Trim() }, true));

        public static bool HasSecret => !string.IsNullOrEmpty(ReadSecret());

        /// <summary>The ID token of the account picked in the browser. Cancelled (closed, denied or timed out) throws
        /// TaskCanceledException.</summary>
        public static async Task<string> GetIdTokenAsync(string clientId)
        {
            string verifier = RandomUrlSafe(48), state = RandomUrlSafe(16);
            string challenge;
            using (var sha = SHA256.Create()) challenge = UrlSafe(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));

            var listener = new HttpListener();
            listener.Prefixes.Add(RedirectUri);
            listener.Start();
            try
            {
                Application.OpenURL(AuthUrl + "?client_id=" + Uri.EscapeDataString(clientId)
                    + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
                    + "&response_type=code&scope=" + Uri.EscapeDataString("openid email profile")
                    + "&code_challenge=" + challenge + "&code_challenge_method=S256"
                    + "&state=" + state + "&prompt=select_account");

                // wait for the browser to come back (or give up)
                var contextTask = listener.GetContextAsync();
                if (await Task.WhenAny(contextTask, Task.Delay(Timeout)) != contextTask) throw new TaskCanceledException("Google sign-in timed out");
                var context = await contextTask;
                string code = context.Request.QueryString["code"], error = context.Request.QueryString["error"];
                bool ok = !string.IsNullOrEmpty(code) && context.Request.QueryString["state"] == state;
                Reply(context.Response, ok ? "로그인이 끝났어요. 에디터로 돌아가세요." : "로그인하지 못했어요. 에디터로 돌아가세요.");
                if (error == "access_denied") throw new TaskCanceledException("Google sign-in cancelled");
                if (!ok) throw new Exception("Google sign-in failed: " + (error ?? "no code"));

                // the code for an ID token
                string secret = ReadSecret();
                using (var http = new HttpClient())
                {
                    var form = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["code"] = code, ["client_id"] = clientId, ["client_secret"] = secret, ["redirect_uri"] = RedirectUri,
                        ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
                    });
                    var response = await http.PostAsync(TokenUrl, form);
                    var token = JsonUtility.FromJson<TokenResponse>(await response.Content.ReadAsStringAsync());
                    if (string.IsNullOrEmpty(token.id_token))
                        throw new Exception("Google token exchange failed: " + token.error + " " + token.error_description);
                    return token.id_token;
                }
            }
            finally { listener.Close(); }
        }

        static void Reply(HttpListenerResponse response, string message)
        {
            var body = Encoding.UTF8.GetBytes("<!doctype html><meta charset=\"utf-8\"><title>FlickCurling</title>"
                + "<body style=\"font-family:sans-serif;text-align:center;margin-top:20vh\"><h2>" + message + "</h2></body>");
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = body.Length;
            response.OutputStream.Write(body, 0, body.Length);
            response.OutputStream.Close();
        }

        static string RandomUrlSafe(int bytes)
        {
            var b = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return UrlSafe(b);
        }

        static string UrlSafe(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
#endif
