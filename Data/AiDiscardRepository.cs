using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace RecepcionDocumental.Data
{
    public static class AiDiscardRepository
    {
        private static string ConnectionString { get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; } }

        public static void TrySave(GmailMessageRecord message, string gmailPartId, AiDiscardEvidence evidence)
        {
            if (message == null || evidence == null) return;

            const string sql = @"INSERT dbo.DocumentoDescarteIa
(GmailMessageId,GmailPartId,FechaMensajeUtc,Remitente,Asunto,NombreOriginal,OrigenTipo,RutaInternaContenedor,OrigenHash,MetodoDeteccion,Confianza,MotivoClasificacion,FechaClasificacionUtc)
SELECT @GmailMessageId,@GmailPartId,@FechaMensajeUtc,@Remitente,@Asunto,@NombreOriginal,@OrigenTipo,@RutaInternaContenedor,@OrigenHash,@MetodoDeteccion,@Confianza,@MotivoClasificacion,SYSUTCDATETIME()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.DocumentoDescarteIa WITH (UPDLOCK,SERIALIZABLE)
    WHERE GmailMessageId=@GmailMessageId AND GmailPartId=@GmailPartId AND OrigenHash=@OrigenHash
);";
            try
            {
                using (var cn = new SqlConnection(ConnectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.Add("@GmailMessageId", SqlDbType.NVarChar, 255).Value = message.GmailMessageId;
                    cmd.Parameters.Add("@GmailPartId", SqlDbType.NVarChar, 255).Value = Db(gmailPartId);
                    cmd.Parameters.Add("@FechaMensajeUtc", SqlDbType.DateTime2).Value = message.MessageDateUtc;
                    cmd.Parameters.Add("@Remitente", SqlDbType.NVarChar, 500).Value = message.From ?? "Remitente no disponible";
                    cmd.Parameters.Add("@Asunto", SqlDbType.NVarChar, 1000).Value = Db(message.Subject);
                    cmd.Parameters.Add("@NombreOriginal", SqlDbType.NVarChar, 500).Value = evidence.OriginalName;
                    cmd.Parameters.Add("@OrigenTipo", SqlDbType.NVarChar, 20).Value = evidence.OriginType;
                    cmd.Parameters.Add("@RutaInternaContenedor", SqlDbType.NVarChar, 2000).Value = Db(evidence.InternalContainerPath);
                    cmd.Parameters.Add("@OrigenHash", SqlDbType.Char, 64).Value = evidence.OriginHash;
                    cmd.Parameters.Add("@MetodoDeteccion", SqlDbType.NVarChar, 50).Value = evidence.Selection.DetectionMethod;
                    cmd.Parameters.Add("@Confianza", SqlDbType.TinyInt).Value = evidence.Selection.Confidence.HasValue ? (object)evidence.Selection.Confidence.Value : DBNull.Value;
                    cmd.Parameters.Add("@MotivoClasificacion", SqlDbType.NVarChar, 2000).Value = Db(evidence.Selection.Reason);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (SqlException ex)
            {
                Logs.LogError("AiDiscardAudit | Operación=Persistir | Estado=ERROR | " + Logs.DescribirExcepcion(ex));
            }
        }

        public static IList<DocumentInfo> List()
        {
            var result = new List<DocumentInfo>();
            const string sql = @"SELECT Id,FechaMensajeUtc,Remitente,Asunto,NombreOriginal,MetodoDeteccion,Confianza,MotivoClasificacion,OrigenTipo,FechaClasificacionUtc
FROM dbo.DocumentoDescarteIa
ORDER BY FechaClasificacionUtc DESC,Id DESC;";
            using (var cn = new SqlConnection(ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        result.Add(new DocumentInfo
                        {
                            Id = r.GetInt64(0),
                            Fecha = r.GetDateTime(1),
                            Remitente = r.GetString(2),
                            Asunto = r.IsDBNull(3) ? "(Sin asunto)" : r.GetString(3),
                            NombreOriginal = r.GetString(4),
                            Clasificacion = "DESCARTAR",
                            MetodoDeteccion = r.GetString(5),
                            Confianza = r.IsDBNull(6) ? (byte?)null : r.GetByte(6),
                            Motivo = r.IsDBNull(7) ? null : r.GetString(7),
                            OrigenTipo = r.GetString(8),
                            FechaOrden = r.GetDateTime(9),
                            EsDescarteIa = true
                        });
                    }
                }
            }
            return result;
        }

        private static object Db(string value) { return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value; }
    }
}
