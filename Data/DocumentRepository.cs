using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using RecepcionDocumental.Services;

namespace RecepcionDocumental.Data
{
    public sealed class DocumentInfo
    {
        public long Id { get; set; } public DateTime Fecha { get; set; } public string Remitente { get; set; } public string Asunto { get; set; }
        public string NombreOriginal { get; set; } public string Clasificacion { get; set; } public string MetodoDeteccion { get; set; }
        public byte? Confianza { get; set; } public string Motivo { get; set; } public string OrigenTipo { get; set; }
    }

    public sealed class MessageDocumentInfo
    {
        public string NombreOriginal { get; set; } public string Clasificacion { get; set; }
        public string MetodoDeteccion { get; set; } public byte? Confianza { get; set; }
        public string OrigenTipo { get; set; } public string RutaInternaContenedor { get; set; }
    }

    public static class DocumentRepository
    {
        private static string ConnectionString { get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; } }

        public static bool Exists(long messageId, string partId, string originHash)
        {
            const string sql = "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.DocumentoRecepcion WHERE GmailMensajeId=@MessageId AND GmailPartId=@PartId AND OrigenHash=@Hash) THEN 1 ELSE 0 END;";
            using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn))
            { cmd.Parameters.Add("@MessageId", SqlDbType.BigInt).Value = messageId; cmd.Parameters.Add("@PartId", SqlDbType.NVarChar, 255).Value = partId; cmd.Parameters.Add("@Hash", SqlDbType.Char, 64).Value = originHash; cn.Open(); return Convert.ToInt32(cmd.ExecuteScalar()) == 1; }
        }

        public static bool Save(long messageId, string partId, DocumentCandidate candidate, DocumentStoredFile stored)
        {
            const string sql = @"INSERT dbo.DocumentoRecepcion (GmailMensajeId,GmailPartId,OrigenTipo,RutaInternaContenedor,OrigenHash,NombreOriginal,MimeType,TamanioBytes,RutaLocal,HashSha256,Clasificacion,MetodoDeteccion,Confianza,MotivoClasificacion,QrDetectado,TipoComprobanteArca,FechaClasificacionUtc)
SELECT @MessageId,@PartId,@OriginType,@InternalPath,@OriginHash,@Name,@Mime,@Size,@LocalPath,@Hash,@Classification,@Method,@Confidence,@Reason,@QrDetected,@ArcaType,SYSUTCDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM dbo.DocumentoRecepcion WITH (UPDLOCK,SERIALIZABLE) WHERE GmailMensajeId=@MessageId AND GmailPartId=@PartId AND OrigenHash=@OriginHash);";
            try
            {
                using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.Add("@MessageId", SqlDbType.BigInt).Value=messageId; cmd.Parameters.Add("@PartId",SqlDbType.NVarChar,255).Value=partId; cmd.Parameters.Add("@OriginType",SqlDbType.NVarChar,20).Value=candidate.OriginType; cmd.Parameters.Add("@InternalPath",SqlDbType.NVarChar,2000).Value=Db(candidate.InternalContainerPath); cmd.Parameters.Add("@OriginHash",SqlDbType.Char,64).Value=candidate.OriginHash; cmd.Parameters.Add("@Name",SqlDbType.NVarChar,500).Value=candidate.OriginalName; cmd.Parameters.Add("@Mime",SqlDbType.NVarChar,255).Value=Db(candidate.MimeType); cmd.Parameters.Add("@Size",SqlDbType.BigInt).Value=stored.Size; cmd.Parameters.Add("@LocalPath",SqlDbType.NVarChar,2000).Value=stored.FullPath; cmd.Parameters.Add("@Hash",SqlDbType.Char,64).Value=stored.HashSha256; cmd.Parameters.Add("@Classification",SqlDbType.NVarChar,20).Value=candidate.Selection.Classification; cmd.Parameters.Add("@Method",SqlDbType.NVarChar,50).Value=candidate.Selection.DetectionMethod; cmd.Parameters.Add("@Confidence",SqlDbType.TinyInt).Value=candidate.Selection.Confidence.HasValue?(object)candidate.Selection.Confidence.Value:DBNull.Value; cmd.Parameters.Add("@Reason",SqlDbType.NVarChar,2000).Value=Db(candidate.Selection.Reason); cmd.Parameters.Add("@QrDetected",SqlDbType.Bit).Value=candidate.QrDetected; cmd.Parameters.Add("@ArcaType",SqlDbType.Int).Value=candidate.TipoComprobanteArca.HasValue?(object)candidate.TipoComprobanteArca.Value:DBNull.Value;
                    cn.Open(); return cmd.ExecuteNonQuery() == 1;
                }
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627) { return false; }
        }

        public static IList<DocumentInfo> List(string classification)
        {
            var result = new List<DocumentInfo>();
            const string sql = @"SELECT d.Id,m.FechaMensajeUtc,m.Remitente,m.Asunto,d.NombreOriginal,d.Clasificacion,d.MetodoDeteccion,d.Confianza,d.MotivoClasificacion,d.OrigenTipo FROM dbo.DocumentoRecepcion d INNER JOIN dbo.GmailMensaje m ON m.Id=d.GmailMensajeId WHERE @Classification IS NULL OR d.Clasificacion=@Classification ORDER BY d.FechaClasificacionUtc DESC,d.Id DESC;";
            using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Classification",SqlDbType.NVarChar,20).Value=Db(classification); cn.Open(); using(var r=cmd.ExecuteReader()) while(r.Read()) result.Add(new DocumentInfo { Id=r.GetInt64(0),Fecha=r.GetDateTime(1),Remitente=r.GetString(2),Asunto=r.IsDBNull(3)?"(Sin asunto)":r.GetString(3),NombreOriginal=r.GetString(4),Clasificacion=r.GetString(5),MetodoDeteccion=r.GetString(6),Confianza=r.IsDBNull(7)?(byte?)null:r.GetByte(7),Motivo=r.IsDBNull(8)?null:r.GetString(8),OrigenTipo=r.GetString(9) });
            }
            return result;
        }

        public static IList<MessageDocumentInfo> ListByMessage(long messageId)
        {
            var result = new List<MessageDocumentInfo>();
            const string sql = @"SELECT NombreOriginal,Clasificacion,MetodoDeteccion,Confianza,OrigenTipo,RutaInternaContenedor FROM dbo.DocumentoRecepcion WHERE GmailMensajeId=@MessageId ORDER BY Id;";
            using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@MessageId", SqlDbType.BigInt).Value = messageId; cn.Open();
                using (var r = cmd.ExecuteReader()) while (r.Read()) result.Add(new MessageDocumentInfo { NombreOriginal=r.GetString(0),Clasificacion=r.GetString(1),MetodoDeteccion=r.GetString(2),Confianza=r.IsDBNull(3)?(byte?)null:r.GetByte(3),OrigenTipo=r.GetString(4),RutaInternaContenedor=r.IsDBNull(5)?null:r.GetString(5) });
            }
            return result;
        }

        public static void GetCounts(out int invoices, out int review)
        {
            const string sql="SELECT SUM(CASE WHEN Clasificacion=N'FACTURA' THEN 1 ELSE 0 END),SUM(CASE WHEN Clasificacion=N'REVISAR' THEN 1 ELSE 0 END) FROM dbo.DocumentoRecepcion;";
            using(var cn=new SqlConnection(ConnectionString)) using(var cmd=new SqlCommand(sql,cn)){cn.Open();using(var r=cmd.ExecuteReader()){r.Read();invoices=r.IsDBNull(0)?0:r.GetInt32(0);review=r.IsDBNull(1)?0:r.GetInt32(1);}}
        }
        private static object Db(string value) { return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value; }
    }
}
