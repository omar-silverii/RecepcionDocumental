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
        public string ResultadoRevision { get; set; }
        public string EstadoEfectivo { get { return ResultadoRevision ?? Clasificacion; } }
        public bool PendienteRevision { get { return Clasificacion == "REVISAR" && ResultadoRevision == null; } }
    }

    public sealed class MessageDocumentInfo
    {
        public string NombreOriginal { get; set; } public string Clasificacion { get; set; }
        public string MetodoDeteccion { get; set; } public byte? Confianza { get; set; }
        public string OrigenTipo { get; set; } public string RutaInternaContenedor { get; set; }
        public string ResultadoRevision { get; set; }
    }

    public sealed class ReviewDocumentRecord { public long Id { get; set; } public DateTime MessageDateUtc { get; set; } public string GmailMessageId { get; set; } public string NombreOriginal { get; set; } public string RutaLocal { get; set; } public string HashSha256 { get; set; } public long TamanioBytes { get; set; } public string Clasificacion { get; set; } public string ResultadoRevision { get; set; } }

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
            const string sql = @"SELECT d.Id,m.FechaMensajeUtc,m.Remitente,m.Asunto,d.NombreOriginal,d.Clasificacion,d.MetodoDeteccion,d.Confianza,d.MotivoClasificacion,d.OrigenTipo,d.ResultadoRevision FROM dbo.DocumentoRecepcion d INNER JOIN dbo.GmailMensaje m ON m.Id=d.GmailMensajeId WHERE (@Classification IS NULL AND ISNULL(d.ResultadoRevision,N'')<>N'DESCARTAR') OR (@Classification=N'FACTURA' AND (d.Clasificacion=N'FACTURA' OR d.ResultadoRevision=N'FACTURA')) OR (@Classification=N'REVISAR' AND d.Clasificacion=N'REVISAR' AND d.ResultadoRevision IS NULL) OR (@Classification=N'DESCARTAR' AND d.ResultadoRevision=N'DESCARTAR') ORDER BY d.FechaClasificacionUtc DESC,d.Id DESC;";
            using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Classification",SqlDbType.NVarChar,20).Value=Db(classification); cn.Open(); using(var r=cmd.ExecuteReader()) while(r.Read()) result.Add(new DocumentInfo { Id=r.GetInt64(0),Fecha=r.GetDateTime(1),Remitente=r.GetString(2),Asunto=r.IsDBNull(3)?"(Sin asunto)":r.GetString(3),NombreOriginal=r.GetString(4),Clasificacion=r.GetString(5),MetodoDeteccion=r.GetString(6),Confianza=r.IsDBNull(7)?(byte?)null:r.GetByte(7),Motivo=r.IsDBNull(8)?null:r.GetString(8),OrigenTipo=r.GetString(9),ResultadoRevision=r.IsDBNull(10)?null:r.GetString(10) });
            }
            return result;
        }

        public static IList<MessageDocumentInfo> ListByMessage(long messageId)
        {
            var result = new List<MessageDocumentInfo>();
            const string sql = @"SELECT NombreOriginal,Clasificacion,MetodoDeteccion,Confianza,OrigenTipo,RutaInternaContenedor,ResultadoRevision FROM dbo.DocumentoRecepcion WHERE GmailMensajeId=@MessageId ORDER BY Id;";
            using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@MessageId", SqlDbType.BigInt).Value = messageId; cn.Open();
                using (var r = cmd.ExecuteReader()) while (r.Read()) result.Add(new MessageDocumentInfo { NombreOriginal=r.GetString(0),Clasificacion=r.GetString(1),MetodoDeteccion=r.GetString(2),Confianza=r.IsDBNull(3)?(byte?)null:r.GetByte(3),OrigenTipo=r.GetString(4),RutaInternaContenedor=r.IsDBNull(5)?null:r.GetString(5),ResultadoRevision=r.IsDBNull(6)?null:r.GetString(6) });
            }
            return result;
        }

        public static void GetCounts(out int invoices, out int review)
        {
            const string sql="SELECT SUM(CASE WHEN Clasificacion=N'FACTURA' OR ResultadoRevision=N'FACTURA' THEN 1 ELSE 0 END),SUM(CASE WHEN Clasificacion=N'REVISAR' AND ResultadoRevision IS NULL THEN 1 ELSE 0 END) FROM dbo.DocumentoRecepcion;";
            using(var cn=new SqlConnection(ConnectionString)) using(var cmd=new SqlCommand(sql,cn)){cn.Open();using(var r=cmd.ExecuteReader()){r.Read();invoices=r.IsDBNull(0)?0:r.GetInt32(0);review=r.IsDBNull(1)?0:r.GetInt32(1);}}
        }
        public static ReviewDocumentRecord GetForReview(long id)
        {
            const string sql=@"SELECT d.Id,m.FechaMensajeUtc,m.GmailMessageId,d.NombreOriginal,d.RutaLocal,d.HashSha256,d.TamanioBytes,d.Clasificacion,d.ResultadoRevision FROM dbo.DocumentoRecepcion d INNER JOIN dbo.GmailMensaje m ON m.Id=d.GmailMensajeId WHERE d.Id=@Id;";
            using(var cn=new SqlConnection(ConnectionString))using(var cmd=new SqlCommand(sql,cn)){cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id;cn.Open();using(var r=cmd.ExecuteReader()){if(!r.Read())return null;return new ReviewDocumentRecord{Id=r.GetInt64(0),MessageDateUtc=r.GetDateTime(1),GmailMessageId=r.GetString(2),NombreOriginal=r.GetString(3),RutaLocal=r.GetString(4),HashSha256=r.GetString(5),TamanioBytes=r.GetInt64(6),Clasificacion=r.GetString(7),ResultadoRevision=r.IsDBNull(8)?null:r.GetString(8)};}}
        }
        public static bool TryResolve(long id,string result,string user,string observation,DocumentStoredFile stored)
        {
            const string sql=@"UPDATE dbo.DocumentoRecepcion SET ResultadoRevision=@Result,FechaRevisionUtc=SYSUTCDATETIME(),UsuarioRevision=@User,ObservacionRevision=@Observation,RutaLocal=CASE WHEN @Result=N'FACTURA' THEN @Path ELSE RutaLocal END,HashSha256=CASE WHEN @Result=N'FACTURA' THEN @Hash ELSE HashSha256 END,TamanioBytes=CASE WHEN @Result=N'FACTURA' THEN @Size ELSE TamanioBytes END WHERE Id=@Id AND Clasificacion=N'REVISAR' AND ResultadoRevision IS NULL;";
            using(var cn=new SqlConnection(ConnectionString))using(var cmd=new SqlCommand(sql,cn)){cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id;cmd.Parameters.Add("@Result",SqlDbType.NVarChar,20).Value=result;cmd.Parameters.Add("@User",SqlDbType.NVarChar,256).Value=Db(user);cmd.Parameters.Add("@Observation",SqlDbType.NVarChar,1000).Value=Db(observation);cmd.Parameters.Add("@Path",SqlDbType.NVarChar,2000).Value=stored==null?(object)DBNull.Value:stored.FullPath;cmd.Parameters.Add("@Hash",SqlDbType.Char,64).Value=stored==null?(object)DBNull.Value:stored.HashSha256;cmd.Parameters.Add("@Size",SqlDbType.BigInt).Value=stored==null?(object)DBNull.Value:stored.Size;cn.Open();return cmd.ExecuteNonQuery()==1;}
        }
        private static object Db(string value) { return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value; }
    }
}
