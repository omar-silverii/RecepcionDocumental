using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Google;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using RecepcionDocumental.Data;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Security;

namespace RecepcionDocumental.Services
{
    public sealed class GmailSyncResult
    {
        public int MensajesEncontrados { get; set; }
        public int MensajesNuevos { get; set; }
        public int AdjuntosDescargados { get; set; }
        public int AdjuntosExistentes { get; set; }
        public int Errores { get; set; }
        public int MensajesOmitidosNotFound { get; set; }
        public bool UsoFallbackInicial { get; set; }
    }

    internal sealed class AttachmentPart
    {
        public string AttachmentId { get; set; }
        public string PartId { get; set; }
        public string FileName { get; set; }
        public string MimeType { get; set; }
        public string InlineData { get; set; }
        public int? DeclaredSize { get; set; }
    }

    internal sealed class GmailSyncBatch
    {
        public IList<string> MessageIds { get; set; }
        public string CompletionHistoryId { get; set; }
        public bool IsInitial { get; set; }
        public int HistoryPages { get; set; }
    }

    internal sealed class MessageProcessResult
    {
        public string HistoryId { get; set; }
        public int AttachmentsFound { get; set; }
    }

    public static class GmailSyncService
    {
        private const int MaxMessages = 100;
        private const int HistoryPageSize = 100;

        public static async Task<GmailSyncResult> SynchronizeAsync()
        {
            var total = Stopwatch.StartNew();
            try { return await SynchronizeCoreAsync(total); }
            catch (Exception ex)
            {
                Logs.LogError("GmailSyncService | Operación=Sincronización | " + Logs.DescribirExcepcion(ex));
                throw;
            }
        }

        private static async Task<GmailSyncResult> SynchronizeCoreAsync(Stopwatch total)
        {
            var account = GmailSyncRepository.GetActiveAccount();
            if (account == null) throw new InvalidOperationException("No hay una cuenta Gmail activa.");
            if (account.ProtectedRefreshToken == null || account.ProtectedRefreshToken.Length == 0) throw new InvalidOperationException("La cuenta Gmail activa no tiene autorización persistida.");

            GoogleOAuthSettings settings;
            string configurationError;
            if (!GoogleOAuthSettings.TryLoad(out settings, out configurationError)) throw new InvalidOperationException(configurationError);
            var refreshToken = RefreshTokenProtector.Unprotect(account.ProtectedRefreshToken);
            var result = new GmailSyncResult();
            Logs.LogProc("GmailSyncService | Inicio sincronización | CuentaId=" + account.Id + " | Modo=" + (string.IsNullOrWhiteSpace(account.LastHistoryId) ? "Inicial" : "Incremental"));

            using (var client = GmailOAuthService.CreateAuthorizedClient(settings, account.Email, refreshToken))
            {
                GmailSyncBatch batch;
                if (string.IsNullOrWhiteSpace(account.LastHistoryId)) batch = await GetInitialMessageIdsAsync(client.Service);
                else
                {
                    try { batch = await GetHistoryMessageIdsAsync(client.Service, account.LastHistoryId); }
                    catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
                    {
                        result.UsoFallbackInicial = true;
                        Logs.LogProc("GmailSyncService | HistoryId vencido (404) | CuentaId=" + account.Id + " | Fallback=Inicial");
                        batch = await GetInitialMessageIdsAsync(client.Service);
                    }
                }

                result.MensajesEncontrados = batch.MessageIds.Count;
                Logs.LogProc("GmailSyncService | Consulta completada | Modo=" + (batch.IsInitial ? "Inicial" : "Incremental") + " | Ids=" + batch.MessageIds.Count + " | PaginasHistory=" + batch.HistoryPages);
                for (var index = 0; index < batch.MessageIds.Count; index++)
                {
                    var messageStopwatch = Stopwatch.StartNew();
                    var downloadedBefore = result.AdjuntosDescargados;
                    var existingBefore = result.AdjuntosExistentes;
                    var errorsBefore = result.Errores;
                    var attachmentsFound = 0;
                    try
                    {
                        var processed = await ProcessMessageAsync(client.Service, account.Id, batch.MessageIds[index], result);
                        attachmentsFound = processed.AttachmentsFound;
                        if (batch.IsInitial && string.IsNullOrWhiteSpace(batch.CompletionHistoryId) && !string.IsNullOrWhiteSpace(processed.HistoryId))
                            batch.CompletionHistoryId = processed.HistoryId;
                    }
                    catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
                    {
                        result.MensajesOmitidosNotFound++;
                        Logs.LogProc("GmailSyncService | Mensaje omitido | GmailMessageId=" + Logs.SanitizarMensaje(batch.MessageIds[index]) + " | Motivo=NotFound");
                    }
                    catch (Exception ex) when (ex is GoogleApiException || ex is IOException || ex is UnauthorizedAccessException || ex is System.Data.SqlClient.SqlException || ex is FormatException)
                    {
                        result.Errores++;
                        Logs.LogError("GmailSyncService | Operación=ProcesarMensaje | GmailMessageId=" + Logs.SanitizarMensaje(batch.MessageIds[index]) + " | " + Logs.DescribirExcepcion(ex));
                    }
                    finally
                    {
                        messageStopwatch.Stop();
                        Logs.LogProc("GmailSyncService | Mensaje " + (index + 1) + "/" + batch.MessageIds.Count + " | GmailMessageId=" + Logs.SanitizarMensaje(batch.MessageIds[index]) + " | DuracionMs=" + messageStopwatch.ElapsedMilliseconds + " | Adjuntos=" + attachmentsFound + " | Descargados=" + (result.AdjuntosDescargados - downloadedBefore) + " | Existentes=" + (result.AdjuntosExistentes - existingBefore) + " | Errores=" + (result.Errores - errorsBefore));
                    }
                }

                var cursorEstado = "conservado";
                if (result.Errores == 0)
                {
                    GmailSyncRepository.CompleteSync(account.Id, batch.CompletionHistoryId);
                    cursorEstado = string.IsNullOrWhiteSpace(batch.CompletionHistoryId) ? "actualizado a NULL" : "avanzado";
                }
                total.Stop();
                Logs.LogProc("GmailSyncService | Fin sincronización | MensajesEncontrados=" + result.MensajesEncontrados + " | MensajesOmitidosNotFound=" + result.MensajesOmitidosNotFound + " | MensajesNuevos=" + result.MensajesNuevos + " | AdjuntosDescargados=" + result.AdjuntosDescargados + " | AdjuntosExistentes=" + result.AdjuntosExistentes + " | Errores=" + result.Errores + " | DuracionMs=" + total.ElapsedMilliseconds + " | Cursor=" + cursorEstado);
            }
            return result;
        }

        private static async Task<GmailSyncBatch> GetInitialMessageIdsAsync(GmailService service)
        {
            var request = service.Users.Messages.List("me");
            request.Q = "has:attachment newer_than:30d";
            request.MaxResults = MaxMessages;
            Logs.LogProc("GmailSyncService | Consulta inicial | UltimosDias=30 | Limite=" + MaxMessages);
            var response = await request.ExecuteAsync();
            return new GmailSyncBatch
            {
                IsInitial = true,
                MessageIds = (response.Messages ?? new List<Message>()).Where(x => !string.IsNullOrWhiteSpace(x.Id)).Select(x => x.Id).Distinct().Take(MaxMessages).ToList()
            };
        }

        private static async Task<GmailSyncBatch> GetHistoryMessageIdsAsync(GmailService service, string historyId)
        {
            ulong startHistoryId;
            if (!ulong.TryParse(historyId, out startHistoryId)) throw new InvalidOperationException("El historyId almacenado no es válido.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string pageToken = null;
            string completionHistoryId = null;
            var pages = 0;
            do
            {
                var request = service.Users.History.List("me");
                request.StartHistoryId = startHistoryId;
                request.HistoryTypes = UsersResource.HistoryResource.ListRequest.HistoryTypesEnum.MessageAdded;
                request.MaxResults = HistoryPageSize;
                request.PageToken = pageToken;
                var response = await request.ExecuteAsync();
                pages++;
                foreach (var history in response.History ?? new List<History>())
                    foreach (var added in history.MessagesAdded ?? new List<HistoryMessageAdded>())
                        if (added.Message != null && !string.IsNullOrWhiteSpace(added.Message.Id)) ids.Add(added.Message.Id);
                pageToken = response.NextPageToken;
                if (string.IsNullOrEmpty(pageToken) && response.HistoryId.HasValue) completionHistoryId = response.HistoryId.Value.ToString();
            } while (!string.IsNullOrEmpty(pageToken));
            if (string.IsNullOrWhiteSpace(completionHistoryId)) throw new InvalidOperationException("Gmail no devolvió el cursor final de la sincronización incremental.");
            return new GmailSyncBatch { IsInitial = false, MessageIds = ids.ToList(), CompletionHistoryId = completionHistoryId, HistoryPages = pages };
        }

        private static async Task<MessageProcessResult> ProcessMessageAsync(GmailService service, int accountId, string messageId, GmailSyncResult result)
        {
            var get = service.Users.Messages.Get("me", messageId);
            get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
            var message = await get.ExecuteAsync();
            var parts = new List<AttachmentPart>();
            CollectAttachmentParts(message.Payload, parts, "0");
            var messageHistoryId = message.HistoryId.HasValue ? message.HistoryId.Value.ToString() : null;
            if (parts.Count == 0) return new MessageProcessResult { HistoryId = messageHistoryId, AttachmentsFound = 0 };

            var record = MapMessage(message);
            bool created;
            var databaseMessageId = GmailSyncRepository.EnsureMessage(accountId, record, out created);
            if (created) result.MensajesNuevos++;

            foreach (var part in parts)
            {
                var downloadedPath = GmailSyncRepository.GetDownloadedAttachmentPath(databaseMessageId, part.PartId);
                if (!string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath))
                {
                    result.AdjuntosExistentes++;
                    Logs.LogProc("GmailSyncService | GmailMessageId=" + Logs.SanitizarMensaje(message.Id) + " | PartId=" + Logs.SanitizarMensaje(part.PartId) + " | Estado=Existente");
                    continue;
                }
                try
                {
                    var data = part.InlineData;
                    if (!string.IsNullOrWhiteSpace(part.AttachmentId))
                    {
                        var attachment = await service.Users.Messages.Attachments.Get("me", message.Id, part.AttachmentId).ExecuteAsync();
                        data = attachment.Data;
                    }
                    var bytes = DecodeBase64Url(data);
                    var identity = "part:" + part.PartId;
                    var stored = AttachmentStorage.Save(bytes, record.MessageDateUtc, message.Id, part.FileName, identity);
                    GmailSyncRepository.SaveAttachment(databaseMessageId, new GmailAttachmentRecord { GmailAttachmentId = part.AttachmentId, GmailPartId = part.PartId, OriginalName = part.FileName, MimeType = part.MimeType, SizeBytes = stored.Size, LocalPath = stored.FullPath, HashSha256 = stored.HashSha256, DownloadedUtc = DateTime.UtcNow, Status = "Descargado" });
                    result.AdjuntosDescargados++;
                    Logs.LogProc("GmailSyncService | GmailMessageId=" + Logs.SanitizarMensaje(message.Id) + " | PartId=" + Logs.SanitizarMensaje(part.PartId) + " | Estado=Descargado");
                }
                catch (Exception ex) when (ex is GoogleApiException || ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is System.Data.SqlClient.SqlException)
                {
                    result.Errores++;
                    Logs.LogError("GmailSyncService | Operación=ProcesarAdjunto | GmailMessageId=" + Logs.SanitizarMensaje(message.Id) + " | PartId=" + Logs.SanitizarMensaje(part.PartId) + " | Estado=Error | " + Logs.DescribirExcepcion(ex));
                    try { GmailSyncRepository.SaveAttachment(databaseMessageId, new GmailAttachmentRecord { GmailAttachmentId = part.AttachmentId, GmailPartId = part.PartId, OriginalName = part.FileName, MimeType = part.MimeType, SizeBytes = part.DeclaredSize, Status = "Error" }); }
                    catch (System.Data.SqlClient.SqlException saveException) { Logs.LogError("GmailSyncService | Operación=RegistrarErrorAdjunto | GmailMessageId=" + Logs.SanitizarMensaje(message.Id) + " | PartId=" + Logs.SanitizarMensaje(part.PartId) + " | Estado=Error | " + Logs.DescribirExcepcion(saveException)); }
                }
            }
            return new MessageProcessResult { HistoryId = messageHistoryId, AttachmentsFound = parts.Count };
        }

        private static void CollectAttachmentParts(MessagePart part, IList<AttachmentPart> result, string traversalPath)
        {
            if (part == null) return;
            if (!string.IsNullOrWhiteSpace(part.Filename) && part.Body != null && (!string.IsNullOrWhiteSpace(part.Body.AttachmentId) || !string.IsNullOrWhiteSpace(part.Body.Data)))
                result.Add(new AttachmentPart { AttachmentId = part.Body.AttachmentId, PartId = string.IsNullOrWhiteSpace(part.PartId) ? traversalPath : part.PartId, FileName = part.Filename, MimeType = part.MimeType, InlineData = part.Body.Data, DeclaredSize = part.Body.Size });
            var children = part.Parts ?? new List<MessagePart>();
            for (var index = 0; index < children.Count; index++) CollectAttachmentParts(children[index], result, traversalPath + "." + index);
        }

        private static GmailMessageRecord MapMessage(Message message)
        {
            var headers = message.Payload == null ? new List<MessagePartHeader>() : message.Payload.Headers ?? new List<MessagePartHeader>();
            var milliseconds = message.InternalDate ?? 0;
            var date = milliseconds > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime : DateTime.UtcNow;
            var from = GetHeader(headers, "From");
            var subject = GetHeader(headers, "Subject");
            return new GmailMessageRecord { GmailMessageId = message.Id, GmailThreadId = message.ThreadId, MessageDateUtc = date, From = string.IsNullOrWhiteSpace(from) ? "Remitente no disponible" : from, Subject = subject, Snippet = message.Snippet };
        }

        private static string GetHeader(IEnumerable<MessagePartHeader> headers, string name)
        {
            var header = headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
            return header == null ? null : header.Value;
        }

        private static byte[] DecodeBase64Url(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) throw new FormatException("El contenido del adjunto está vacío.");
            var normalized = data.Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4) { case 2: normalized += "=="; break; case 3: normalized += "="; break; case 1: throw new FormatException("Contenido Base64URL inválido."); }
            return Convert.FromBase64String(normalized);
        }
    }
}
