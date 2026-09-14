using System;
using System.Security.Cryptography;

namespace RecepcionDocumental.Security
{
    public static class SingleUserLoginService
    {
        private const string UserEnvironmentVariable = "RECEPCIONDOCUMENTAL_LOGIN_USER";
        private const string PasswordHashEnvironmentVariable = "RECEPCIONDOCUMENTAL_LOGIN_PASSWORD_HASH";
        private const string AlgorithmName = "pbkdf2-sha1";

        public static bool TryValidate(string userName, string password, out string authenticatedUser, out string configurationError)
        {
            authenticatedUser = null;
            configurationError = null;

            var configuredUser = GetEnvironmentValue(UserEnvironmentVariable);
            var configuredHash = GetEnvironmentValue(PasswordHashEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(configuredUser) || string.IsNullOrWhiteSpace(configuredHash))
            {
                configurationError = "El acceso a la aplicación todavía no está configurado en el servidor.";
                return false;
            }

            configuredUser = configuredUser.Trim();
            configuredHash = configuredHash.Trim();

            if (!IsHashFormatValid(configuredHash))
            {
                configurationError = "La configuración de acceso del servidor no es válida.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(userName) || password == null)
            {
                return false;
            }

            if (!string.Equals(configuredUser, userName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // Ejecutar igualmente el PBKDF2 evita que el tiempo de respuesta revele
                // si el nombre de usuario existe.
                VerifyPassword(password, configuredHash);
                return false;
            }

            if (!VerifyPassword(password, configuredHash))
            {
                return false;
            }

            authenticatedUser = configuredUser;
            return true;
        }

        private static string GetEnvironmentValue(string variableName)
        {
            var value = Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(value)) return value;

            value = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrWhiteSpace(value)) return value;

            return Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User);
        }

        private static bool IsHashFormatValid(string encodedHash)
        {
            try
            {
                string[] parts = encodedHash.Split('$');
                if (parts.Length != 4 || !string.Equals(parts[0], AlgorithmName, StringComparison.Ordinal)) return false;

                int iterations;
                if (!int.TryParse(parts[1], out iterations) || iterations < 10000) return false;

                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                return salt.Length >= 16 && expected.Length >= 20;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool VerifyPassword(string password, string encodedHash)
        {
            try
            {
                string[] parts = encodedHash.Split('$');
                if (parts.Length != 4 || !string.Equals(parts[0], AlgorithmName, StringComparison.Ordinal)) return false;

                int iterations;
                if (!int.TryParse(parts[1], out iterations) || iterations < 10000) return false;

                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expected = Convert.FromBase64String(parts[3]);
                byte[] actual;

                using (var derive = new Rfc2898DeriveBytes(password, salt, iterations))
                {
                    actual = derive.GetBytes(expected.Length);
                }

                return FixedTimeEquals(expected, actual);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is CryptographicException)
            {
                return false;
            }
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;

            int difference = 0;
            for (int i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }

            return difference == 0;
        }
    }
}
