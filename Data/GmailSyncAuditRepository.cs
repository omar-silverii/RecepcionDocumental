using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace RecepcionDocumental.Data
{
    public sealed class GmailSyncAuditException : InvalidOperationException
    {
        public string Code { get; private set; }
        internal GmailSyncAuditException(string code,Exception inner):base(code,inner){Code=code;}
    }
    public sealed class GmailSyncAuditInfo
    {
        public string Origen,Estado;
        public DateTime Inicio;
        public DateTime? Fin;
        public int Mensajes,Errores;
    }
    public static class GmailSyncAuditRepository
    {
        private static SqlConnection Connection(){return new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString);}
        public static long? Start(int? accountId,string origin)
        {
            try
            {
                using(var cn=Connection())using(var cmd=new SqlCommand("INSERT dbo.GmailSyncEjecucion(GmailCuentaId,Origen,FechaInicioUtc,Estado) OUTPUT inserted.Id VALUES(@Account,@Origin,SYSUTCDATETIME(),N'EJECUTANDO');",cn))
                {cmd.Parameters.Add("@Account",SqlDbType.Int).Value=(object)accountId??DBNull.Value;cmd.Parameters.Add("@Origin",SqlDbType.NVarChar,20).Value=origin;cn.Open();return Convert.ToInt64(cmd.ExecuteScalar());}
            }
            catch(Exception ex){ReportingFailure(ex,"AUDIT_START_FAILED");throw new GmailSyncAuditException("AUDIT_START_FAILED",ex);}
        }
        public static void Finish(long? id,string state,GmailSyncResult result,Exception error=null)
        {
            try
            {
                if(!id.HasValue)throw new InvalidOperationException("Missing audit id.");
                using(var cn=Connection())using(var cmd=new SqlCommand(@"UPDATE dbo.GmailSyncEjecucion SET FechaFinUtc=SYSUTCDATETIME(),Estado=@State,MensajesEncontrados=@Found,MensajesNuevos=@New,AdjuntosAnalizados=@Attachments,Facturas=@Invoices,Revisar=@Review,Descartados=@Discard,DocumentosExistentes=@Existing,Errores=@Errors,UsoFallbackInicial=@Fallback,DetalleError=@Detail WHERE Id=@Id;
SELECT COUNT(*) FROM dbo.GmailSyncEjecucion WHERE Id=@Id AND Estado=@State AND Estado<>N'EJECUTANDO' AND FechaFinUtc IS NOT NULL;",cn))
                {
                    cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=id.Value;cmd.Parameters.Add("@State",SqlDbType.NVarChar,40).Value=state;
                    var r=result??new GmailSyncResult{Errores=1};
                    cmd.Parameters.AddWithValue("@Found",r.MensajesEncontrados);cmd.Parameters.AddWithValue("@New",r.MensajesNuevos);cmd.Parameters.AddWithValue("@Attachments",r.AdjuntosAnalizados);cmd.Parameters.AddWithValue("@Invoices",r.FacturasDetectadas);cmd.Parameters.AddWithValue("@Review",r.ParaRevisar);cmd.Parameters.AddWithValue("@Discard",r.Descartados);cmd.Parameters.AddWithValue("@Existing",r.DocumentosExistentes);cmd.Parameters.AddWithValue("@Errors",r.Errores);cmd.Parameters.AddWithValue("@Fallback",r.UsoFallbackInicial);
                    // Only the exception type is persisted. Raw provider messages can contain credentials or message content.
                    cmd.Parameters.Add("@Detail",SqlDbType.NVarChar,500).Value=error==null?(object)DBNull.Value:error.GetType().Name;
                    cn.Open();if(Convert.ToInt32(cmd.ExecuteScalar())!=1)throw new InvalidOperationException("Audit finalization was not confirmed.");
                }
            }
            catch(Exception ex){ReportingFailure(ex,"AUDIT_FINALIZATION_FAILED");throw new GmailSyncAuditException("AUDIT_FINALIZATION_FAILED",ex);}
        }
        public static GmailSyncAuditInfo Latest()
        {
            try{using(var cn=Connection())using(var cmd=new SqlCommand("SELECT TOP(1) Origen,Estado,FechaInicioUtc,FechaFinUtc,MensajesEncontrados,Errores FROM dbo.GmailSyncEjecucion ORDER BY Id DESC;",cn)){cn.Open();using(var r=cmd.ExecuteReader())return r.Read()?new GmailSyncAuditInfo{Origen=r.GetString(0),Estado=r.GetString(1),Inicio=r.GetDateTime(2),Fin=r.IsDBNull(3)?(DateTime?)null:r.GetDateTime(3),Mensajes=r.GetInt32(4),Errores=r.GetInt32(5)}:null;}}
            catch(Exception ex){ReportingFailure(ex);return null;}
        }
        private static void ReportingFailure(Exception ex,string code="ReportingFailure"){try{Logs.LogError("GmailSyncAudit | "+code+" | Error="+ex.GetType().Name);}catch{} }
    }
}
