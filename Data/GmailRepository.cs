using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace RecepcionDocumental.Data
{
    public sealed class DashboardResumen { public int CuentasActivas { get; set; } public int Mensajes { get; set; } public int Adjuntos { get; set; } }
    public sealed class GmailCuentaInfo { public int Id { get; set; } public string Email { get; set; } public bool Activo { get; set; } public DateTime? UltimaConsultaUtc { get; set; } public bool TieneRefreshToken { get; set; } }
    public sealed class GmailMensajeInfo { public long Id { get; set; } public string GmailMessageId { get; set; } public DateTime FechaMensajeUtc { get; set; } public string Remitente { get; set; } public string Asunto { get; set; } public string Snippet { get; set; } public string CuentaEmail { get; set; } }

    public static class GmailRepository
    {
        private static string ConnectionString { get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; } }

        public static bool TryGetDashboardResumen(out DashboardResumen resumen)
        {
            resumen = new DashboardResumen();
            const string sql = @"SELECT (SELECT COUNT(*) FROM dbo.GmailCuenta WHERE Activo = 1), (SELECT COUNT(*) FROM dbo.GmailMensaje), (SELECT COUNT(*) FROM dbo.GmailAdjunto WHERE FechaDescargaUtc IS NOT NULL);";
            try { using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn)) { cn.Open(); using (var r = cmd.ExecuteReader()) { r.Read(); resumen.CuentasActivas = r.GetInt32(0); resumen.Mensajes = r.GetInt32(1); resumen.Adjuntos = r.GetInt32(2); } } return true; }
            catch (SqlException) { return false; }
        }

        public static bool TryGetCuenta(out GmailCuentaInfo cuenta)
        {
            cuenta = null;
            const string sql = @"SELECT TOP (1) Id, Email, Activo, UltimaConsultaUtc, CASE WHEN RefreshTokenProtegido IS NULL THEN 0 ELSE 1 END FROM dbo.GmailCuenta ORDER BY Activo DESC, Id;";
            try { using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn)) { cn.Open(); using (var r = cmd.ExecuteReader()) if (r.Read()) cuenta = MapCuenta(r); } return true; }
            catch (SqlException) { return false; }
        }

        public static bool TryGetCuentaPorEmail(string email, out GmailCuentaInfo cuenta)
        {
            cuenta = null;
            const string sql = @"SELECT Id, Email, Activo, UltimaConsultaUtc, CASE WHEN RefreshTokenProtegido IS NULL THEN 0 ELSE 1 END FROM dbo.GmailCuenta WHERE Email = @Email;";
            try
            {
                using (var cn = new SqlConnection(ConnectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 320).Value = email;
                    cn.Open();
                    using (var r = cmd.ExecuteReader()) if (r.Read()) cuenta = MapCuenta(r);
                }
                return true;
            }
            catch (SqlException) { return false; }
        }

        public static bool GuardarCuentaAutorizada(string email, byte[] nuevoRefreshTokenProtegido)
        {
            const string sql = @"
UPDATE dbo.GmailCuenta
SET Activo = 1,
    RefreshTokenProtegido = COALESCE(@RefreshTokenProtegido, RefreshTokenProtegido),
    FechaModificacion = SYSUTCDATETIME()
WHERE Email = @Email;
IF @@ROWCOUNT = 0
BEGIN
    INSERT dbo.GmailCuenta (Email, Activo, RefreshTokenProtegido)
    VALUES (@Email, 1, @RefreshTokenProtegido);
END;";
            try
            {
                using (var cn = new SqlConnection(ConnectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 320).Value = email;
                    cmd.Parameters.Add("@RefreshTokenProtegido", SqlDbType.VarBinary, -1).Value = (object)nuevoRefreshTokenProtegido ?? DBNull.Value;
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch (SqlException) { return false; }
        }

        public static bool TryGetMensajes(out IList<GmailMensajeInfo> mensajes)
        {
            mensajes = new List<GmailMensajeInfo>();
            const string sql = @"SELECT m.Id, m.GmailMessageId, m.FechaMensajeUtc, m.Remitente, m.Asunto, m.Snippet, c.Email FROM dbo.GmailMensaje m INNER JOIN dbo.GmailCuenta c ON c.Id = m.GmailCuentaId ORDER BY m.FechaMensajeUtc DESC, m.Id DESC;";
            try { using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn)) { cn.Open(); using (var r = cmd.ExecuteReader()) while (r.Read()) mensajes.Add(MapMensaje(r)); } return true; }
            catch (SqlException) { return false; }
        }

        public static bool TryGetMensaje(long id, out GmailMensajeInfo mensaje)
        {
            mensaje = null;
            const string sql = @"SELECT m.Id, m.GmailMessageId, m.FechaMensajeUtc, m.Remitente, m.Asunto, m.Snippet, c.Email FROM dbo.GmailMensaje m INNER JOIN dbo.GmailCuenta c ON c.Id = m.GmailCuentaId WHERE m.Id = @Id;";
            try { using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn)) { cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = id; cn.Open(); using (var r = cmd.ExecuteReader()) if (r.Read()) mensaje = MapMensaje(r); } return true; }
            catch (SqlException) { return false; }
        }

        private static GmailMensajeInfo MapMensaje(SqlDataReader r)
        {
            return new GmailMensajeInfo { Id = r.GetInt64(0), GmailMessageId = r.GetString(1), FechaMensajeUtc = r.GetDateTime(2), Remitente = r.GetString(3), Asunto = r.IsDBNull(4) ? "(Sin asunto)" : r.GetString(4), Snippet = r.IsDBNull(5) ? null : r.GetString(5), CuentaEmail = r.GetString(6) };
        }

        private static GmailCuentaInfo MapCuenta(SqlDataReader r)
        {
            return new GmailCuentaInfo { Id = r.GetInt32(0), Email = r.GetString(1), Activo = r.GetBoolean(2), UltimaConsultaUtc = r.IsDBNull(3) ? (DateTime?)null : r.GetDateTime(3), TieneRefreshToken = r.GetInt32(4) == 1 };
        }
    }
}
