using System;
using System.IO;
using System.Security.Cryptography;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Data;
using RecepcionDocumental.Infrastructure;

namespace RecepcionDocumental.Services
{
    public static class InvoiceSampleService
    {
        // Called only for a successful new INSERT. Failure never changes ingestion success or Gmail state.
        public static void AfterPersisted(long id)
        {
            try { InvoiceSampleRepository.SelectNew(id); }
            catch(Exception ex) { Logs.LogError("InvoiceSample | Selección fallida | DocumentoId="+id+" | "+Logs.DescribirExcepcion(ex)); }
        }
        public static ReviewDocumentRecord GetSafeDocument(long id)
        {
            if(InvoiceSampleRepository.Pending(id)==null)return null;
            return DocumentReviewService.GetSafeDocument(id);
        }
        public static DocumentReviewResult Resolve(long id,string label,string user,string observation)
        {
            ReviewDocumentRecord original;DocumentStoredFile destination;
            var success=InvoiceSampleRepository.Resolve(id,label,Trim(user,256),Trim(observation,1000),
                doc=>Prepare(doc,label),Compensate,out original,out destination);
            if(!success)return new DocumentReviewResult{Success=false,Message="Este documento ya no está pendiente de control."};
            if(!string.Equals(original.RutaLocal,destination.FullPath,StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // A physical file can be shared by persisted documents. Never delete another document's source.
                    if(!InvoiceSampleRepository.PathReferenced(original.RutaLocal))File.Delete(original.RutaLocal);
                }
                catch(Exception ex){Logs.LogError("InvoiceSample | Limpieza posterior fallida | DocumentoId="+id+" | "+Logs.DescribirExcepcion(ex));}
            }
            Logs.LogProc("InvoiceSample | DocumentoId="+id+" | Etiqueta="+label);
            return new DocumentReviewResult{Success=true,Message="Revisión de control registrada."};
        }
        private static DocumentStoredFile Prepare(ReviewDocumentRecord doc,string label)
        {
            var full=Path.GetFullPath(doc.RutaLocal);
            if(!Under(full,ConfiguracionSistema.Actual.RutaFacturas)&&!Under(full,ConfiguracionSistema.Actual.RutaRevisar))throw new UnauthorizedAccessException("Ruta no autorizada.");
            Verify(full,doc.HashSha256,doc.TamanioBytes);
            DocumentStoredFile stored=null;
            try
            {
                stored=DocumentStorage.Save(full,label=="FACTURA"?"FACTURA":"REVISAR",doc.MessageDateUtc,doc.GmailMessageId,doc.NombreOriginal,doc.HashSha256);
                if(stored.Size!=doc.TamanioBytes||!stored.HashSha256.Equals(doc.HashSha256,StringComparison.OrdinalIgnoreCase))throw new IOException("La copia no conserva hash/tamaño.");
                return stored;
            }
            catch { Compensate(doc,stored);throw; }
        }
        private static void Compensate(ReviewDocumentRecord doc,DocumentStoredFile stored)
        {
            if(doc==null||stored==null||!stored.CreatedByThisCall||string.Equals(doc.RutaLocal,stored.FullPath,StringComparison.OrdinalIgnoreCase))return;
            // Executed under the sample's SQL lock before rollback; pre-existing destinations are never deleted.
            if(!Under(stored.FullPath,ConfiguracionSistema.Actual.RutaRevisar)&&!Under(stored.FullPath,ConfiguracionSistema.Actual.RutaFacturas))throw new UnauthorizedAccessException("Compensación fuera de almacenamiento autorizado.");
            File.Delete(stored.FullPath);
        }
        private static void Verify(string path,string hash,long size)
        {
            using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())
            {if(stream.Length!=size||!BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").Equals(hash,StringComparison.OrdinalIgnoreCase))throw new IOException("El archivo no coincide con SQL.");}
        }
        private static bool Under(string path,string root){return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        private static string Trim(string value,int max){if(string.IsNullOrWhiteSpace(value))return null;value=value.Trim();return value.Length>max?value.Substring(0,max):value;}
    }
}
