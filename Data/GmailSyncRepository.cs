using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace RecepcionDocumental.Data
{
    public sealed class GmailSyncAccount
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public byte[] ProtectedRefreshToken { get; set; }
        public string LastHistoryId { get; set; }
    }

    public sealed class GmailMessageRecord
    {
        public string GmailMessageId { get; set; }
        public string GmailThreadId { get; set; }
        public DateTime MessageDateUtc { get; set; }
        public string From { get; set; }
        public string Subject { get; set; }
        public string Snippet { get; set; }
    }

    public sealed class GmailAttachmentRecord
    {
        public string GmailAttachmentId { get; set; }
        public string GmailPartId { get; set; }
        public string OriginalName { get; set; }
        public string MimeType { get; set; }
        public long? SizeBytes { get; set; }
        public string LocalPath { get; set; }
        public string HashSha256 { get; set; }
        public DateTime? DownloadedUtc { get; set; }
        public string Status { get; set; }
    }

    public sealed class GmailAttachmentInfo
    {
        public long Id { get; set; }
        public string NombreOriginal { get; set; }
        public string MimeType { get; set; }
        public long? TamanioBytes { get; set; }
        public string Estado { get; set; }
        public DateTime? FechaDescargaUtc { get; set; }
    }

    public static class GmailSyncRepository
    {
        private static string ConnectionString { get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; } }

        public static GmailSyncAccount GetActiveAccount()
        {
            const string sql = @"SELECT TOP (1) Id, Email, RefreshTokenProtegido, UltimoHistoryId FROM dbo.GmailCuenta WHERE Activo = 1 ORDER BY Id;";
            using (var cn = new SqlConnection(ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return new GmailSyncAccount { Id = r.GetInt32(0), Email = r.GetString(1), ProtectedRefreshToken = r.IsDBNull(2) ? null : (byte[])r[2], LastHistoryId = r.IsDBNull(3) ? null : r.GetString(3) };
                }
            }
        }

        public static long EnsureMessage(int accountId, GmailMessageRecord message, out bool created)
        {
            using (var cn = new SqlConnection(ConnectionString))
            {
                cn.Open();
                using (var tx = cn.BeginTransaction(IsolationLevel.Serializable))
                {
                    const string find = "SELECT Id FROM dbo.GmailMensaje WITH (UPDLOCK, HOLDLOCK) WHERE GmailCuentaId=@CuentaId AND GmailMessageId=@MessageId;";
                    using (var cmd = new SqlCommand(find, cn, tx))
                    {
                        cmd.Parameters.Add("@CuentaId", SqlDbType.Int).Value = accountId;
                        cmd.Parameters.Add("@MessageId", SqlDbType.NVarChar, 255).Value = message.GmailMessageId;
                        var existing = cmd.ExecuteScalar();
                        if (existing != null) { tx.Commit(); created = false; return Convert.ToInt64(existing); }
                    }

                    const string insert = @"INSERT dbo.GmailMensaje (GmailCuentaId,GmailMessageId,GmailThreadId,FechaMensajeUtc,Remitente,Asunto,Snippet) OUTPUT INSERTED.Id VALUES (@CuentaId,@MessageId,@ThreadId,@Fecha,@Remitente,@Asunto,@Snippet);";
                    using (var cmd = new SqlCommand(insert, cn, tx))
                    {
                        cmd.Parameters.Add("@CuentaId", SqlDbType.Int).Value = accountId;
                        cmd.Parameters.Add("@MessageId", SqlDbType.NVarChar, 255).Value = message.GmailMessageId;
                        cmd.Parameters.Add("@ThreadId", SqlDbType.NVarChar, 255).Value = DbValue(message.GmailThreadId);
                        cmd.Parameters.Add("@Fecha", SqlDbType.DateTime2).Value = message.MessageDateUtc;
                        cmd.Parameters.Add("@Remitente", SqlDbType.NVarChar, 500).Value = message.From;
                        cmd.Parameters.Add("@Asunto", SqlDbType.NVarChar, 1000).Value = DbValue(message.Subject);
                        cmd.Parameters.Add("@Snippet", SqlDbType.NVarChar, 2000).Value = DbValue(message.Snippet);
                        var id = Convert.ToInt64(cmd.ExecuteScalar());
                        tx.Commit(); created = true; return id;
                    }
                }
            }
        }

        public static string GetDownloadedAttachmentPath(long messageId, string attachmentId, string partId)
        {
            const string sql = @"SELECT TOP (1) RutaLocal FROM dbo.GmailAdjunto WHERE GmailMensajeId=@MensajeId AND ((@AttachmentId IS NOT NULL AND GmailAttachmentId=@AttachmentId) OR (@AttachmentId IS NULL AND GmailAttachmentId IS NULL AND GmailPartId=@PartId)) AND Estado=N'Descargado' AND RutaLocal IS NOT NULL;";
            using (var cn = new SqlConnection(ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@MensajeId", SqlDbType.BigInt).Value = messageId;
                cmd.Parameters.Add("@AttachmentId", SqlDbType.NVarChar, 255).Value = DbValue(attachmentId);
                cmd.Parameters.Add("@PartId", SqlDbType.NVarChar, 255).Value = DbValue(partId);
                cn.Open(); var value = cmd.ExecuteScalar(); return value == null || value == DBNull.Value ? null : Convert.ToString(value);
            }
        }

        public static void SaveAttachment(long messageId, GmailAttachmentRecord attachment)
        {
            const string sql = @"
UPDATE dbo.GmailAdjunto SET NombreOriginal=@Nombre, MimeType=@Mime, TamanioBytes=@Tamanio, RutaLocal=@Ruta, HashSha256=@Hash, FechaDescargaUtc=@Fecha, Estado=@Estado
WHERE GmailMensajeId=@MensajeId AND ((@AttachmentId IS NOT NULL AND GmailAttachmentId=@AttachmentId) OR (@AttachmentId IS NULL AND GmailAttachmentId IS NULL AND GmailPartId=@PartId));
IF @@ROWCOUNT=0 INSERT dbo.GmailAdjunto (GmailMensajeId,GmailAttachmentId,GmailPartId,NombreOriginal,MimeType,TamanioBytes,RutaLocal,HashSha256,FechaDescargaUtc,Estado)
VALUES (@MensajeId,@AttachmentId,@PartId,@Nombre,@Mime,@Tamanio,@Ruta,@Hash,@Fecha,@Estado);";
            using (var cn = new SqlConnection(ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@MensajeId", SqlDbType.BigInt).Value = messageId;
                cmd.Parameters.Add("@AttachmentId", SqlDbType.NVarChar, 255).Value = DbValue(attachment.GmailAttachmentId);
                cmd.Parameters.Add("@PartId", SqlDbType.NVarChar, 255).Value = DbValue(attachment.GmailPartId);
                cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 500).Value = attachment.OriginalName;
                cmd.Parameters.Add("@Mime", SqlDbType.NVarChar, 255).Value = DbValue(attachment.MimeType);
                cmd.Parameters.Add("@Tamanio", SqlDbType.BigInt).Value = attachment.SizeBytes.HasValue ? (object)attachment.SizeBytes.Value : DBNull.Value;
                cmd.Parameters.Add("@Ruta", SqlDbType.NVarChar, 2000).Value = DbValue(attachment.LocalPath);
                cmd.Parameters.Add("@Hash", SqlDbType.Char, 64).Value = DbValue(attachment.HashSha256);
                cmd.Parameters.Add("@Fecha", SqlDbType.DateTime2).Value = attachment.DownloadedUtc.HasValue ? (object)attachment.DownloadedUtc.Value : DBNull.Value;
                cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 50).Value = attachment.Status;
                cn.Open(); cmd.ExecuteNonQuery();
            }
        }

        public static void CompleteSync(int accountId, string historyId)
        {
            const string sql = @"UPDATE dbo.GmailCuenta SET UltimoHistoryId=@HistoryId, UltimaConsultaUtc=SYSUTCDATETIME(), FechaModificacion=SYSUTCDATETIME() WHERE Id=@Id;";
            using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn))
            { cmd.Parameters.Add("@Id", SqlDbType.Int).Value = accountId; cmd.Parameters.Add("@HistoryId", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(historyId) ? (object)DBNull.Value : historyId; cn.Open(); cmd.ExecuteNonQuery(); }
        }

        public static IList<GmailAttachmentInfo> GetAttachments(long messageId)
        {
            var result = new List<GmailAttachmentInfo>();
            const string sql = @"SELECT Id,NombreOriginal,MimeType,TamanioBytes,Estado,FechaDescargaUtc FROM dbo.GmailAdjunto WHERE GmailMensajeId=@Id ORDER BY Id;";
            using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn))
            { cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = messageId; cn.Open(); using (var r=cmd.ExecuteReader()) while(r.Read()) result.Add(new GmailAttachmentInfo { Id=r.GetInt64(0), NombreOriginal=r.GetString(1), MimeType=r.IsDBNull(2)?null:r.GetString(2), TamanioBytes=r.IsDBNull(3)?(long?)null:r.GetInt64(3), Estado=r.GetString(4), FechaDescargaUtc=r.IsDBNull(5)?(DateTime?)null:r.GetDateTime(5) }); }
            return result;
        }

        private static object DbValue(string value) { return string.IsNullOrEmpty(value) ? (object)DBNull.Value : value; }
    }
}
