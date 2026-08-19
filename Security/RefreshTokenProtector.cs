using System;
using System.Text;
using System.Web.Security;

namespace RecepcionDocumental.Security
{
    public static class RefreshTokenProtector
    {
        private const string Purpose = "RecepcionDocumental.Gmail.RefreshToken.v1";

        public static byte[] Protect(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) throw new ArgumentException("El refresh token es obligatorio.", "refreshToken");
            return MachineKey.Protect(Encoding.UTF8.GetBytes(refreshToken), Purpose);
        }

        public static string Unprotect(byte[] protectedToken)
        {
            if (protectedToken == null || protectedToken.Length == 0) return null;
            var clearBytes = MachineKey.Unprotect(protectedToken, Purpose);
            if (clearBytes == null) throw new InvalidOperationException("No fue posible desproteger el refresh token.");
            return Encoding.UTF8.GetString(clearBytes);
        }
    }
}
