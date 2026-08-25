using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;

namespace RecepcionDocumental.Services
{
    public static class GmailOAuthService
    {
        public static GoogleAuthorizationCodeFlow CreateFlow(GoogleOAuthSettings settings)
        {
            return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = settings.ClientSecrets,
                Scopes = new[] { GoogleOAuthSettings.GmailReadonlyScope }
            });
        }

        public static string CreateAuthorizationUrl(GoogleOAuthSettings settings, string state)
        {
            using (var flow = CreateFlow(settings))
            {
                var request = (GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(settings.RedirectUri);
                request.AccessType = "offline";
                request.State = state;
                return request.Build().AbsoluteUri;
            }
        }

        public static async Task<OAuthResult> CompleteAuthorizationAsync(GoogleOAuthSettings settings, string code)
        {
            using (var flow = CreateFlow(settings))
            {
                var token = await flow.ExchangeCodeForTokenAsync("oauth-callback", code, settings.RedirectUri, CancellationToken.None);
                var credential = new UserCredential(flow, "oauth-callback", token);
                using (var gmail = new GmailService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "RecepcionDocumental"
                }))
                {
                    var profile = await gmail.Users.GetProfile("me").ExecuteAsync();
                    if (profile == null || string.IsNullOrWhiteSpace(profile.EmailAddress))
                        throw new InvalidOperationException("Google no devolvió una dirección de cuenta válida.");

                    return new OAuthResult { Email = profile.EmailAddress.Trim(), RefreshToken = token.RefreshToken };
                }
            }
        }

        public static string GenerateState()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static GmailAuthorizedClient CreateAuthorizedClient(GoogleOAuthSettings settings, string email, string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) throw new InvalidOperationException("La cuenta no tiene un refresh token disponible.");
            var flow = CreateFlow(settings);
            var credential = new UserCredential(flow, email, new TokenResponse { RefreshToken = refreshToken });
            var service = new GmailService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "RecepcionDocumental" });
            return new GmailAuthorizedClient(flow, service);
        }
    }

    public sealed class GmailAuthorizedClient : IDisposable
    {
        private readonly GoogleAuthorizationCodeFlow _flow;
        public GmailService Service { get; private set; }
        public GmailAuthorizedClient(GoogleAuthorizationCodeFlow flow, GmailService service) { _flow = flow; Service = service; }
        public void Dispose() { Service.Dispose(); _flow.Dispose(); }
    }

    public sealed class OAuthResult
    {
        public string Email { get; set; }
        public string RefreshToken { get; set; }
    }
}
