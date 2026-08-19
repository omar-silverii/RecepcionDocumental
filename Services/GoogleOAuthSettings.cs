using System;
using Google.Apis.Auth.OAuth2;

namespace RecepcionDocumental.Services
{
    public sealed class GoogleOAuthSettings
    {
        public const string RedirectUri = "https://localhost:44320/Gmail_OAuthCallback.aspx";
        public const string GmailReadonlyScope = "https://www.googleapis.com/auth/gmail.readonly";

        public ClientSecrets ClientSecrets { get; private set; }

        public static bool TryLoad(out GoogleOAuthSettings settings, out string errorMessage)
        {
            settings = null;
            var clientId = Environment.GetEnvironmentVariable("RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_SECRET");
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                errorMessage = "Falta configurar las credenciales OAuth de Google en las variables de entorno de Windows.";
                return false;
            }

            settings = new GoogleOAuthSettings
            {
                ClientSecrets = new ClientSecrets { ClientId = clientId.Trim(), ClientSecret = clientSecret.Trim() }
            };
            errorMessage = null;
            return true;
        }
    }
}
