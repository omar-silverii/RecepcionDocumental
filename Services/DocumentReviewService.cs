using System;
using System.IO;
using System.Security.Cryptography;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Data;
using RecepcionDocumental.Infrastructure;

namespace RecepcionDocumental.Services
{
    public sealed class DocumentReviewResult { public bool Success { get; set; } public string Message { get; set; } }

    public static class DocumentReviewService
    {
        public static DocumentReviewResult ConfirmInvoice(long id,string user,string observation) { return Resolve(id,"FACTURA",user,observation); }
        public static DocumentReviewResult Discard(long id,string user,string observation) { return Resolve(id,"DESCARTAR",user,observation); }
        public static ReviewDocumentRecord GetSafeDocument(long id)
        {
            var doc=DocumentRepository.GetForReview(id);if(doc==null)return null;
            var full=Path.GetFullPath(doc.RutaLocal);if(!Under(full,ConfiguracionSistema.Actual.RutaFacturas)&&!Under(full,ConfiguracionSistema.Actual.RutaRevisar))throw new UnauthorizedAccessException("La ruta del documento no pertenece al almacenamiento autorizado.");
            if(!File.Exists(full))throw new FileNotFoundException("El archivo del documento no existe.",full);doc.RutaLocal=full;return doc;
        }
        private static DocumentReviewResult Resolve(long id,string result,string user,string observation)
        {
            var doc=DocumentRepository.GetForReview(id);if(doc==null)return Fail("Documento inexistente.");if(doc.Clasificacion!="REVISAR"||doc.ResultadoRevision!=null)return Fail("El documento ya no está pendiente de revisión.");doc=GetSafeDocument(id);
            Verify(doc.RutaLocal,doc.HashSha256,doc.TamanioBytes);DocumentStoredFile stored=null;
            if(result=="FACTURA"){stored=DocumentStorage.Save(doc.RutaLocal,"FACTURA",doc.MessageDateUtc,doc.GmailMessageId,doc.NombreOriginal,doc.HashSha256);if(!string.Equals(stored.HashSha256,doc.HashSha256,StringComparison.OrdinalIgnoreCase)||stored.Size!=doc.TamanioBytes)throw new IOException("La copia a Facturas no conserva hash y tamaño.");}
            if(!DocumentRepository.TryResolve(id,result,Human(user),Trim(observation,1000),stored))return Fail("El documento fue resuelto por otra solicitud.");
            try{if(result=="DESCARTAR"||!string.Equals(doc.RutaLocal,stored.FullPath,StringComparison.OrdinalIgnoreCase))File.Delete(doc.RutaLocal);}catch(Exception ex){Logs.LogError("DocumentReview | Limpieza posterior fallida | DocumentoId="+id+" | "+Logs.DescribirExcepcion(ex));}
            Logs.LogProc("DocumentReview | DocumentoId="+id+" | Resultado="+result+" | UsuarioDisponible="+(!string.IsNullOrWhiteSpace(Human(user))));return new DocumentReviewResult{Success=true,Message=result=="FACTURA"?"Documento confirmado como factura.":"Documento descartado manualmente."};
        }
        private static void Verify(string path,string expected,long size){var f=new FileInfo(path);if(f.Length!=size)throw new IOException("El tamaño físico no coincide con SQL.");using(var sha=SHA256.Create())using(var s=File.OpenRead(path)){var hash=BitConverter.ToString(sha.ComputeHash(s)).Replace("-","");if(!hash.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new IOException("El hash físico no coincide con SQL.");}}
        private static bool Under(string path,string root){var p=Path.GetFullPath(path);var r=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;return p.StartsWith(r,StringComparison.OrdinalIgnoreCase);}
        private static string Human(string value){return string.IsNullOrWhiteSpace(value)?null:Trim(value,256);}
        private static string Trim(string value,int max){if(string.IsNullOrWhiteSpace(value))return null;value=value.Trim();return value.Length>max?value.Substring(0,max):value;}
        private static DocumentReviewResult Fail(string message){return new DocumentReviewResult{Success=false,Message=message};}
    }
}
