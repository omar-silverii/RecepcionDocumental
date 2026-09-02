using System;
using System.Threading.Tasks;
using RecepcionDocumental.Data;

namespace RecepcionDocumental.Services
{
    public static class GmailSyncExecution
    {
        public static async Task<GmailSyncResult> RunAsync(string origin,Func<GmailSyncAccount,GmailSyncLease,Task<GmailSyncResult>> process)
        {
            if(origin!="WEB"&&origin!="SCHEDULER")throw new ArgumentException("Origen inválido.");
            long? audit=null;
            using(var lease=GmailSyncLease.TryAcquire())
            {
                if(lease==null)
                {
                    var skipped=new GmailSyncResult{AlreadyRunning=true};
                    audit=GmailSyncAuditRepository.Start(null,origin);
                    GmailSyncAuditRepository.Finish(audit,"OMITIDA_YA_EN_EJECUCION",skipped);
                    return skipped;
                }
                try
                {
                    lease.AssertHeld();
                    var account=GmailSyncRepository.GetActiveAccount();
                    audit=GmailSyncAuditRepository.Start(account==null?(int?)null:account.Id,origin);
                    if(account==null)throw new InvalidOperationException("No hay una cuenta Gmail activa.");
                    var result=await process(account,lease).ConfigureAwait(false);
                    GmailSyncAuditRepository.Finish(audit,result.Errores==0?"COMPLETADA":"COMPLETADA_CON_ERRORES",result);
                    return result;
                }
                catch(Exception ex)
                {
                    try
                    {
                        if(!audit.HasValue)audit=GmailSyncAuditRepository.Start(null,origin);
                        GmailSyncAuditRepository.Finish(audit,"FALLIDA",null,ex);
                    }
                    catch(GmailSyncAuditException auditError)
                    {
                        // Preserve the primary error while making a secondary audit failure observable.
                        ex.Data["AuditFailureCode"]=auditError.Code;
                    }
                    throw;
                }
            }
        }
    }
}
