using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using RecepcionDocumental.Services;
using RecepcionDocumental.Infrastructure;

namespace RecepcionDocumental.Data
{
    public sealed class DocumentInfo
    {
        public long Id { get; set; } public DateTime Fecha { get; set; } public string Remitente { get; set; } public string Asunto { get; set; }
        public string NombreOriginal { get; set; } public string Clasificacion { get; set; } public string MetodoDeteccion { get; set; }
        public byte? Confianza { get; set; } public string Motivo { get; set; } public string OrigenTipo { get; set; }
        public string ResultadoRevision { get; set; } public string EtiquetaRevision { get; set; }
        public string EstadoEfectivo { get { return ResultadoRevision ?? Clasificacion; } }
        public bool PendienteRevision { get { return Clasificacion == "REVISAR" && ResultadoRevision == null; } }
    }

    public sealed class MessageDocumentInfo
    {
        public string NombreOriginal { get; set; } public string Clasificacion { get; set; }
        public string MetodoDeteccion { get; set; } public byte? Confianza { get; set; }
        public string OrigenTipo { get; set; } public string RutaInternaContenedor { get; set; }
        public string ResultadoRevision { get; set; } public string EtiquetaRevision { get; set; }
    }

    public sealed class ReviewDocumentRecord { public long Id { get; set; } public DateTime MessageDateUtc { get; set; } public string GmailMessageId { get; set; } public string NombreOriginal { get; set; } public string RutaLocal { get; set; } public string HashSha256 { get; set; } public long TamanioBytes { get; set; } public string Clasificacion { get; set; } public string ResultadoRevision { get; set; } }

    public sealed class PendingReviewInfo
    {
        public long Id { get; set; } public DateTime Fecha { get; set; } public string Remitente { get; set; } public string Asunto { get; set; }
        public string NombreOriginal { get; set; } public string Clasificacion { get; set; } public string MetodoDeteccion { get; set; }
        public byte? Confianza { get; set; } public string Motivo { get; set; } public string OrigenTipo { get; set; }
        public int Position { get; set; } public int Total { get; set; }
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

        public static long? GetId(long messageId,string partId,string originHash)
        {
            const string sql="SELECT Id FROM dbo.DocumentoRecepcion WHERE GmailMensajeId=@MessageId AND GmailPartId=@PartId AND OrigenHash=@Hash;";
            try{using(var cn=new SqlConnection(ConnectionString))using(var cmd=new SqlCommand(sql,cn)){cmd.Parameters.Add("@MessageId",SqlDbType.BigInt).Value=messageId;cmd.Parameters.Add("@PartId",SqlDbType.NVarChar,255).Value=partId;cmd.Parameters.Add("@Hash",SqlDbType.Char,64).Value=originHash;cn.Open();var value=cmd.ExecuteScalar();return value==null||value==DBNull.Value?(long?)null:Convert.ToInt64(value);}}
            catch(Exception ex){Logs.LogError("VisualShadow | Operación=ObtenerDocumentoId | Estado=ERROR | "+Logs.DescribirExcepcion(ex));return null;}
        }

        public static IList<DocumentInfo> List(string classification)
        {
            var result = new List<DocumentInfo>();
            const string sql = @"SELECT d.Id,m.FechaMensajeUtc,m.Remitente,m.Asunto,d.NombreOriginal,d.Clasificacion,d.MetodoDeteccion,d.Confianza,d.MotivoClasificacion,d.OrigenTipo,d.ResultadoRevision,d.EtiquetaRevision FROM dbo.DocumentoRecepcion d INNER JOIN dbo.GmailMensaje m ON m.Id=d.GmailMensajeId WHERE (@Classification IS NULL AND ISNULL(d.ResultadoRevision,N'')<>N'DESCARTAR') OR (@Classification=N'FACTURA' AND (d.Clasificacion=N'FACTURA' OR d.ResultadoRevision=N'FACTURA')) OR (@Classification=N'REVISAR' AND d.Clasificacion=N'REVISAR' AND d.ResultadoRevision IS NULL) OR (@Classification=N'DESCARTAR' AND d.ResultadoRevision=N'DESCARTAR') ORDER BY d.FechaClasificacionUtc DESC,d.Id DESC;";
            using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Classification",SqlDbType.NVarChar,20).Value=Db(classification); cn.Open(); using(var r=cmd.ExecuteReader()) while(r.Read()) result.Add(new DocumentInfo { Id=r.GetInt64(0),Fecha=r.GetDateTime(1),Remitente=r.GetString(2),Asunto=r.IsDBNull(3)?"(Sin asunto)":r.GetString(3),NombreOriginal=r.GetString(4),Clasificacion=r.GetString(5),MetodoDeteccion=r.GetString(6),Confianza=r.IsDBNull(7)?(byte?)null:r.GetByte(7),Motivo=r.IsDBNull(8)?null:r.GetString(8),OrigenTipo=r.GetString(9),ResultadoRevision=r.IsDBNull(10)?null:r.GetString(10),EtiquetaRevision=r.IsDBNull(11)?null:r.GetString(11) });
            }
            return result;
        }

        public static IList<MessageDocumentInfo> ListByMessage(long messageId)
        {
            var result = new List<MessageDocumentInfo>();
            const string sql = @"SELECT NombreOriginal,Clasificacion,MetodoDeteccion,Confianza,OrigenTipo,RutaInternaContenedor,ResultadoRevision,EtiquetaRevision FROM dbo.DocumentoRecepcion WHERE GmailMensajeId=@MessageId ORDER BY Id;";
            using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@MessageId", SqlDbType.BigInt).Value = messageId; cn.Open();
                using (var r = cmd.ExecuteReader()) while (r.Read()) result.Add(new MessageDocumentInfo { NombreOriginal=r.GetString(0),Clasificacion=r.GetString(1),MetodoDeteccion=r.GetString(2),Confianza=r.IsDBNull(3)?(byte?)null:r.GetByte(3),OrigenTipo=r.GetString(4),RutaInternaContenedor=r.IsDBNull(5)?null:r.GetString(5),ResultadoRevision=r.IsDBNull(6)?null:r.GetString(6),EtiquetaRevision=r.IsDBNull(7)?null:r.GetString(7) });
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
        public static PendingReviewInfo GetPendingForReview(long id)
        {
            const string sql=@"SELECT d.Id,m.FechaMensajeUtc,m.Remitente,m.Asunto,d.NombreOriginal,d.Clasificacion,d.MetodoDeteccion,d.Confianza,d.MotivoClasificacion,d.OrigenTipo,
(SELECT COUNT(*) FROM dbo.DocumentoRecepcion p WHERE p.Clasificacion=N'REVISAR' AND p.ResultadoRevision IS NULL AND (p.FechaClasificacionUtc<d.FechaClasificacionUtc OR (p.FechaClasificacionUtc=d.FechaClasificacionUtc AND p.Id<=d.Id))),
(SELECT COUNT(*) FROM dbo.DocumentoRecepcion p WHERE p.Clasificacion=N'REVISAR' AND p.ResultadoRevision IS NULL)
FROM dbo.DocumentoRecepcion d INNER JOIN dbo.GmailMensaje m ON m.Id=d.GmailMensajeId
WHERE d.Id=@Id AND d.Clasificacion=N'REVISAR' AND d.ResultadoRevision IS NULL;";
            using(var cn=new SqlConnection(ConnectionString))using(var cmd=new SqlCommand(sql,cn)){cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id;cn.Open();using(var r=cmd.ExecuteReader()){return r.Read()?MapPending(r):null;}}
        }
        public static long? GetFirstPending() { return PendingId(@"SELECT TOP (1) Id FROM dbo.DocumentoRecepcion WHERE Clasificacion=N'REVISAR' AND ResultadoRevision IS NULL ORDER BY FechaClasificacionUtc ASC,Id ASC;",null); }
        public static long? GetNextPending(long id) { return PendingId(@"SELECT TOP (1) p.Id FROM dbo.DocumentoRecepcion p CROSS JOIN (SELECT FechaClasificacionUtc,Id FROM dbo.DocumentoRecepcion WHERE Id=@Id) c WHERE p.Clasificacion=N'REVISAR' AND p.ResultadoRevision IS NULL AND (p.FechaClasificacionUtc>c.FechaClasificacionUtc OR (p.FechaClasificacionUtc=c.FechaClasificacionUtc AND p.Id>c.Id)) ORDER BY p.FechaClasificacionUtc ASC,p.Id ASC;",id); }
        public static long? GetPreviousPending(long id) { return PendingId(@"SELECT TOP (1) p.Id FROM dbo.DocumentoRecepcion p CROSS JOIN (SELECT FechaClasificacionUtc,Id FROM dbo.DocumentoRecepcion WHERE Id=@Id) c WHERE p.Clasificacion=N'REVISAR' AND p.ResultadoRevision IS NULL AND (p.FechaClasificacionUtc<c.FechaClasificacionUtc OR (p.FechaClasificacionUtc=c.FechaClasificacionUtc AND p.Id<c.Id)) ORDER BY p.FechaClasificacionUtc DESC,p.Id DESC;",id); }
        private static long? PendingId(string sql,long? id){using(var cn=new SqlConnection(ConnectionString))using(var cmd=new SqlCommand(sql,cn)){if(id.HasValue)cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id.Value;cn.Open();var value=cmd.ExecuteScalar();return value==null||value==DBNull.Value?(long?)null:Convert.ToInt64(value);}}
        private static PendingReviewInfo MapPending(SqlDataReader r){return new PendingReviewInfo{Id=r.GetInt64(0),Fecha=r.GetDateTime(1),Remitente=r.GetString(2),Asunto=r.IsDBNull(3)?"(Sin asunto)":r.GetString(3),NombreOriginal=r.GetString(4),Clasificacion=r.GetString(5),MetodoDeteccion=r.GetString(6),Confianza=r.IsDBNull(7)?(byte?)null:r.GetByte(7),Motivo=r.IsDBNull(8)?null:r.GetString(8),OrigenTipo=r.GetString(9),Position=r.GetInt32(10),Total=r.GetInt32(11)};}
        public static bool TryResolve(long id,string result,string label,string user,string observation,DocumentStoredFile stored)
        {
            var binary=label=="FACTURA"?"FACTURA":label=="OTRO_DOCUMENTO"||label=="NO_DOCUMENTO"?"NO_FACTURA":null;
            if(binary==null)throw new ArgumentException("Etiqueta de revisión no soportada.","label");
            const string update=@"DECLARE @Decision TABLE(HashSha256 CHAR(64),TamanioBytes BIGINT,FechaDecisionUtc DATETIME2(0));
UPDATE dbo.DocumentoRecepcion
SET ResultadoRevision=@Result,EtiquetaRevision=@Label,FechaRevisionUtc=SYSUTCDATETIME(),UsuarioRevision=@User,ObservacionRevision=@Observation,
    RutaLocal=CASE WHEN @Result=N'FACTURA' THEN @Path ELSE RutaLocal END,
    HashSha256=CASE WHEN @Result=N'FACTURA' THEN @Hash ELSE HashSha256 END,
    TamanioBytes=CASE WHEN @Result=N'FACTURA' THEN @Size ELSE TamanioBytes END
OUTPUT inserted.HashSha256,inserted.TamanioBytes,inserted.FechaRevisionUtc INTO @Decision
WHERE Id=@Id AND Clasificacion=N'REVISAR' AND ResultadoRevision IS NULL;
SELECT HashSha256,TamanioBytes,FechaDecisionUtc FROM @Decision;";
            const string insert=@"INSERT dbo.DocumentoGroundTruth
(DocumentoRecepcionId,Secuencia,EsVigente,EtiquetaBinaria,EtiquetaDetallada,Fuente,DocumentoSha256,TamanioBytes,UsuarioRevision,ObservacionRevision,FechaDecisionUtc,DocumentoVisionShadowId)
SELECT @Id,ISNULL(MAX(gt.Secuencia),0)+1,1,@Binary,@Label,N'REVISION_OPERATIVA',@DocumentHash,@DocumentSize,@User,@Observation,@DecisionUtc,
       (SELECT TOP (1) s.Id FROM dbo.DocumentoVisionShadow s WHERE s.DocumentoRecepcionId=@Id ORDER BY s.FechaEvaluacionUtc DESC,s.Id DESC)
FROM dbo.DocumentoGroundTruth gt WITH(UPDLOCK,HOLDLOCK)
WHERE gt.DocumentoRecepcionId=@Id;";
            using(var cn=new SqlConnection(ConnectionString))
            {
                cn.Open();PrepareGroundTruthSession(cn);using(var tx=cn.BeginTransaction())
                {
                    try
                    {
                        string hash;long size;DateTime decisionUtc;
                        using(var cmd=new SqlCommand(update,cn,tx))
                        {
                            AddReviewParameters(cmd,id,result,label,user,observation,stored);
                            bool updated;
                            using(var reader=cmd.ExecuteReader())
                            {
                                if(reader.Read()){hash=reader.GetString(0);size=reader.GetInt64(1);decisionUtc=reader.GetDateTime(2);updated=true;}
                                else{hash=null;size=0;decisionUtc=default(DateTime);updated=false;}
                            }
                            if(!updated){tx.Rollback();return false;}
                        }
                        using(var cmd=new SqlCommand(insert,cn,tx))
                        {
                            cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id;
                            cmd.Parameters.Add("@Binary",SqlDbType.NVarChar,20).Value=binary;
                            cmd.Parameters.Add("@Label",SqlDbType.NVarChar,30).Value=label;
                            cmd.Parameters.Add("@DocumentHash",SqlDbType.Char,64).Value=hash;
                            cmd.Parameters.Add("@DocumentSize",SqlDbType.BigInt).Value=size;
                            cmd.Parameters.Add("@User",SqlDbType.NVarChar,256).Value=Db(user);
                            cmd.Parameters.Add("@Observation",SqlDbType.NVarChar,1000).Value=Db(observation);
                            cmd.Parameters.Add("@DecisionUtc",SqlDbType.DateTime2).Value=decisionUtc;
                            if(cmd.ExecuteNonQuery()!=1)throw new DataException("No se pudo registrar DocumentoGroundTruth.");
                        }
                        tx.Commit();return true;
                    }
                    catch{try{tx.Rollback();}catch{}throw;}
                }
            }
        }
        private static void AddReviewParameters(SqlCommand cmd,long id,string result,string label,string user,string observation,DocumentStoredFile stored)
        {cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id;cmd.Parameters.Add("@Result",SqlDbType.NVarChar,20).Value=result;cmd.Parameters.Add("@Label",SqlDbType.NVarChar,30).Value=label;cmd.Parameters.Add("@User",SqlDbType.NVarChar,256).Value=Db(user);cmd.Parameters.Add("@Observation",SqlDbType.NVarChar,1000).Value=Db(observation);cmd.Parameters.Add("@Path",SqlDbType.NVarChar,2000).Value=stored==null?(object)DBNull.Value:stored.FullPath;cmd.Parameters.Add("@Hash",SqlDbType.Char,64).Value=stored==null?(object)DBNull.Value:stored.HashSha256;cmd.Parameters.Add("@Size",SqlDbType.BigInt).Value=stored==null?(object)DBNull.Value:stored.Size;}
        private static void PrepareGroundTruthSession(SqlConnection connection)
        {
            const string sql=@"SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;";
            using(var cmd=new SqlCommand(sql,connection))cmd.ExecuteNonQuery();
        }
        private static object Db(string value) { return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value; }
    }
}
