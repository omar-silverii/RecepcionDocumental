using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using RecepcionDocumental.Services;

namespace RecepcionDocumental.Data
{
    public static class InvoiceSampleRepository
    {
        private static string ConnectionString { get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; } }
        private const string Session = "SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;";
        public static int? Bucket(string sha)
        {
            if (sha == null || sha.Length != 64) throw new ArgumentException("SHA-256 inválida.", "sha");
            using (var cn = new SqlConnection(ConnectionString)) using (var cmd = new SqlCommand("SELECT dbo.H1D7C2Bucket(@sha);", cn))
            { cmd.Parameters.Add("@sha", SqlDbType.VarChar,64).Value=sha; cn.Open(); var v=cmd.ExecuteScalar(); return v==DBNull.Value?(int?)null:Convert.ToInt32(v); }
        }
        public static bool SelectNew(long id)
        {
            const string sql=@"INSERT dbo.DocumentoRevisionMuestra(DocumentoRecepcionId,TipoMuestra,ReglaVersion,Modulo,Bucket,FechaSeleccionUtc)
SELECT d.Id,N'FACTURA_AUTOMATICA',N'H1D7C2-V1',10,dbo.H1D7C2Bucket(d.HashSha256),SYSUTCDATETIME()
FROM dbo.DocumentoRecepcion d WHERE d.Id=@Id AND d.Clasificacion=N'FACTURA' AND d.ResultadoRevision IS NULL AND dbo.H1D7C2Bucket(d.HashSha256)=0
AND NOT EXISTS(SELECT 1 FROM dbo.DocumentoRevisionMuestra s WITH(UPDLOCK,HOLDLOCK) WHERE s.DocumentoRecepcionId=d.Id);";
            using(var cn=new SqlConnection(ConnectionString)) using(var cmd=new SqlCommand(sql,cn))
            { cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id; cn.Open(); return cmd.ExecuteNonQuery()==1; }
        }
        private static long? Scalar(string sql,long? id=null)
        {
            using(var cn=new SqlConnection(ConnectionString)) using(var cmd=new SqlCommand(sql,cn))
            { if(id.HasValue)cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id.Value; cn.Open();var v=cmd.ExecuteScalar();return v==null||v==DBNull.Value?(long?)null:Convert.ToInt64(v); }
        }
        public static int CountPending() { return (int)Scalar("SELECT COUNT(*) FROM dbo.DocumentoRevisionMuestra WHERE DocumentoGroundTruthId IS NULL;").Value; }
        public static long? First() { return Scalar("SELECT TOP(1) DocumentoRecepcionId FROM dbo.DocumentoRevisionMuestra WHERE DocumentoGroundTruthId IS NULL ORDER BY Id;"); }
        public static long? Next(long id) { return Scalar("SELECT TOP(1) DocumentoRecepcionId FROM dbo.DocumentoRevisionMuestra WHERE DocumentoGroundTruthId IS NULL AND Id>(SELECT Id FROM dbo.DocumentoRevisionMuestra WHERE DocumentoRecepcionId=@Id) ORDER BY Id;",id); }
        public static long? Previous(long id) { return Scalar("SELECT TOP(1) DocumentoRecepcionId FROM dbo.DocumentoRevisionMuestra WHERE DocumentoGroundTruthId IS NULL AND Id<(SELECT Id FROM dbo.DocumentoRevisionMuestra WHERE DocumentoRecepcionId=@Id) ORDER BY Id DESC;",id); }
        // Deliberately no automatic classification, method, confidence or shadow columns in this projection.
        public static PendingReviewInfo Pending(long id)
        {
            const string sql=@"SELECT d.Id,m.FechaMensajeUtc,m.Remitente,m.Asunto,d.NombreOriginal,
(SELECT COUNT(*) FROM dbo.DocumentoRevisionMuestra p WHERE p.DocumentoGroundTruthId IS NULL AND p.Id<=s.Id),
(SELECT COUNT(*) FROM dbo.DocumentoRevisionMuestra p WHERE p.DocumentoGroundTruthId IS NULL)
FROM dbo.DocumentoRevisionMuestra s JOIN dbo.DocumentoRecepcion d ON d.Id=s.DocumentoRecepcionId JOIN dbo.GmailMensaje m ON m.Id=d.GmailMensajeId
WHERE d.Id=@Id AND s.DocumentoGroundTruthId IS NULL;";
            using(var cn=new SqlConnection(ConnectionString)) using(var cmd=new SqlCommand(sql,cn))
            {cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id;cn.Open();using(var r=cmd.ExecuteReader())return r.Read()?new PendingReviewInfo{Id=r.GetInt64(0),Fecha=r.GetDateTime(1),Remitente=r.GetString(2),Asunto=r.IsDBNull(3)?"(Sin asunto)":r.GetString(3),NombreOriginal=r.GetString(4),Position=r.GetInt32(5),Total=r.GetInt32(6)}:null;}
        }
        public static bool PathReferenced(string path)
        {
            using(var cn=new SqlConnection(ConnectionString))using(var cmd=new SqlCommand("SELECT COUNT(*) FROM dbo.DocumentoRecepcion WHERE RutaLocal=@Path;",cn))
            {cmd.Parameters.Add("@Path",SqlDbType.NVarChar,2000).Value=path;cn.Open();return Convert.ToInt32(cmd.ExecuteScalar())!=0;}
        }
        // The row lock is acquired before any physical work, including by the losing request.
        public static bool Resolve(long id,string label,string user,string observation,
            Func<ReviewDocumentRecord,DocumentStoredFile> prepare,Action<ReviewDocumentRecord,DocumentStoredFile> compensate,
            out ReviewDocumentRecord original,out DocumentStoredFile destination)
        {
            if(label!="FACTURA"&&label!="OTRO_DOCUMENTO"&&label!="NO_DOCUMENTO")throw new ArgumentException("Etiqueta inválida.");
            original=null;destination=null;
            using(var cn=new SqlConnection(ConnectionString))
            {
                cn.Open();using(var cmd=new SqlCommand(Session,cn))cmd.ExecuteNonQuery();
                using(var tx=cn.BeginTransaction())
                {
                    bool committing=false;
                    try
                    {
                        const string read=@"SELECT d.Id,m.FechaMensajeUtc,m.GmailMessageId,d.NombreOriginal,d.RutaLocal,d.HashSha256,d.TamanioBytes,d.Clasificacion,d.ResultadoRevision
FROM dbo.DocumentoRevisionMuestra s WITH(UPDLOCK,HOLDLOCK) JOIN dbo.DocumentoRecepcion d WITH(UPDLOCK,HOLDLOCK) ON d.Id=s.DocumentoRecepcionId
JOIN dbo.GmailMensaje m ON m.Id=d.GmailMensajeId WHERE s.DocumentoRecepcionId=@Id AND s.DocumentoGroundTruthId IS NULL AND d.Clasificacion=N'FACTURA' AND d.ResultadoRevision IS NULL;";
                        using(var cmd=new SqlCommand(read,cn,tx))
                        {cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id;using(var r=cmd.ExecuteReader())if(r.Read())original=new ReviewDocumentRecord{Id=r.GetInt64(0),MessageDateUtc=r.GetDateTime(1),GmailMessageId=r.GetString(2),NombreOriginal=r.GetString(3),RutaLocal=r.GetString(4),HashSha256=r.GetString(5),TamanioBytes=r.GetInt64(6),Clasificacion=r.GetString(7),ResultadoRevision=r.IsDBNull(8)?null:r.GetString(8)};}
                        if(original==null){tx.Rollback();return false;}
                        destination=prepare(original);
                        const string write=@"DECLARE @Now datetime2(0)=SYSUTCDATETIME();
INSERT dbo.DocumentoGroundTruth(DocumentoRecepcionId,Secuencia,EsVigente,EtiquetaBinaria,EtiquetaDetallada,Fuente,DocumentoSha256,TamanioBytes,UsuarioRevision,ObservacionRevision,FechaDecisionUtc,DocumentoVisionShadowId)
SELECT @Id,ISNULL(MAX(Secuencia),0)+1,1,CASE WHEN @Label=N'FACTURA' THEN N'FACTURA' ELSE N'NO_FACTURA' END,@Label,N'MUESTREO_FACTURA_CIEGO',@Hash,@Size,@User,@Observation,@Now,
(SELECT TOP(1) Id FROM dbo.DocumentoVisionShadow WHERE DocumentoRecepcionId=@Id ORDER BY FechaEvaluacionUtc DESC,Id DESC)
FROM dbo.DocumentoGroundTruth WITH(UPDLOCK,HOLDLOCK) WHERE DocumentoRecepcionId=@Id;
DECLARE @Gt bigint=SCOPE_IDENTITY();
UPDATE dbo.DocumentoRevisionMuestra SET DocumentoGroundTruthId=@Gt,FechaResolucionUtc=@Now WHERE DocumentoRecepcionId=@Id AND DocumentoGroundTruthId IS NULL;
IF @@ROWCOUNT<>1 THROW 51000,'Muestra no pendiente.',1;
UPDATE dbo.DocumentoRecepcion SET ResultadoRevision=CASE WHEN @Label=N'FACTURA' THEN N'FACTURA' ELSE N'DESCARTAR' END,
EtiquetaRevision=@Label,FechaRevisionUtc=@Now,UsuarioRevision=@User,ObservacionRevision=@Observation,RutaLocal=@Path
WHERE Id=@Id AND Clasificacion=N'FACTURA' AND ResultadoRevision IS NULL;
IF @@ROWCOUNT<>1 THROW 51000,'Documento no elegible.',1;";
                        using(var cmd=new SqlCommand(write,cn,tx))
                        {
                            cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id;cmd.Parameters.Add("@Label",SqlDbType.NVarChar,30).Value=label;
                            cmd.Parameters.Add("@Hash",SqlDbType.Char,64).Value=original.HashSha256;cmd.Parameters.Add("@Size",SqlDbType.BigInt).Value=original.TamanioBytes;
                            cmd.Parameters.Add("@User",SqlDbType.NVarChar,256).Value=(object)user??DBNull.Value;cmd.Parameters.Add("@Observation",SqlDbType.NVarChar,1000).Value=(object)observation??DBNull.Value;
                            cmd.Parameters.Add("@Path",SqlDbType.NVarChar,2000).Value=destination.FullPath;cmd.ExecuteNonQuery();
                        }
                        committing=true;tx.Commit();return true;
                    }
                    catch
                    {
                        // Compensation before releasing the lock: no other operator can adopt this new copy.
                        // A failed COMMIT acknowledgement is ambiguous: preserve the file for reconciliation.
                        try{if(!committing)compensate(original,destination);}finally{try{tx.Rollback();}catch{}}
                        throw;
                    }
                }
            }
        }
    }
}
