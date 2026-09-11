using System;
using Google.Apis.Auth.OAuth2;
using RecepcionDocumental.Configuration;

namespace RecepcionDocumental.Services
{
    public sealed class GoogleOAuthSettings
    {
        public const string GmailReadonlyScope = "https://www.googleapis.com/auth/gmail.readonly";

        private const string ClientIdEnvironmentVariable = "RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_ID";
        private const string ClientSecretEnvironmentVariable = "RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_SECRET";

        public ClientSecrets ClientSecrets { get; private set; }
        public string RedirectUri { get; private set; }

        public static bool TryLoad(out GoogleOAuthSettings settings, out string errorMessage)
        {
            settings = null;

            var clientId = GetEnvironmentValue(ClientIdEnvironmentVariable);
            var clientSecret = GetEnvironmentValue(ClientSecretEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                errorMessage = "Falta configurar las credenciales OAuth de Google en las variables de entorno de Windows.";
                return false;
            }

            settings = new GoogleOAuthSettings
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId.Trim(),
                    ClientSecret = clientSecret.Trim()
                },
                RedirectUri = ConfiguracionSistema.Actual.GmailRedirectUri
            };

            errorMessage = null;
            return true;
        }

        private static string GetEnvironmentValue(string variableName)
        {
            // En servidor las credenciales se configuran a nivel Machine.
            // Leer Machine explícitamente evita depender del bloque de entorno
            // heredado por WAS/w3wp antes de que la variable haya sido creada.
            var value = Environment.GetEnvironmentVariable(
                variableName,
                EnvironmentVariableTarget.Machine);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            // Compatibilidad con el entorno de desarrollo existente.
            value = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return Environment.GetEnvironmentVariable(
                variableName,
                EnvironmentVariableTarget.User);
        }
    }
}
