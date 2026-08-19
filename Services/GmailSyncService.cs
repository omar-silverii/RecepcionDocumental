using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Google;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using RecepcionDocumental.Data;
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

    public static class GmailSyncService
    {
        private const int MaxMessages = 100;

        public static async Task<GmailSyncResult> SynchronizeAsync()
        {
            var account = GmailSyncRepository.GetActiveAccount();
            if (account == null) throw new InvalidOperationException("No hay una cuenta Gmail activa.");
            if (account.ProtectedRefreshToken == null || account.ProtectedRefreshToken.Length == 0) throw new InvalidOperationException("La cuenta Gmail activa no tiene autorización persistida.");

            GoogleOAuthSettings settings;
            string configurationError;
            if (!GoogleOAuthSettings.TryLoad(out settings, out configurationError)) throw new InvalidOperationException(configurationError);
            var refreshToken = RefreshTokenProtector.Unprotect(account.ProtectedRefreshToken);
            var result = new GmailSyncResult();

            using (var client = GmailOAuthService.CreateAuthorizedClient(settings, account.Email, refreshToken))
            {
                IList<string> messageIds;
                if (string.IsNullOrWhiteSpace(account.LastHistoryId)) messageIds = await GetInitialMessageIdsAsync(client.Service);
                else
                {
                    try { messageIds = await GetHistoryMessageIdsAsync(client.Service, account.LastHistoryId); }
                    catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
                    {
                        result.UsoFallbackInicial = true;
                        messageIds = await GetInitialMessageIdsAsync(client.Service);
                    }
                }

                result.MensajesEncontrados = messageIds.Count;
                foreach (var messageId in messageIds.Take(MaxMessages))
                {
                    try { await ProcessMessageAsync(client.Service, account.Id, messageId, result); }
                    catch (GoogleApiException) { result.Errores++; }
                    catch (IOException) { result.Errores++; }
                    catch (UnauthorizedAccessException) { result.Errores++; }
                    catch (System.Data.SqlClient.SqlException) { result.Errores++; }
                    catch (FormatException) { result.Errores++; }
                }

                var profile = await client.Service.Users.GetProfile("me").ExecuteAsync();
                if (!profile.HistoryId.HasValue) throw new InvalidOperationException("Gmail no devolvió un historyId válido.");
                if (result.Errores == 0) GmailSyncRepository.CompleteSync(account.Id, profile.HistoryId.Value.ToString());
            }
            return result;
        }

        private static async Task<IList<string>> GetInitialMessageIdsAsync(GmailService service)
        {
            var request = service.Users.Messages.List("me");
            request.Q = "has:attachment newer_than:30d";
            request.MaxResults = MaxMessages;
            var response = await request.ExecuteAsync();
            return (response.Messages ?? new List<Message>()).Where(x => !string.IsNullOrWhiteSpace(x.Id)).Select(x => x.Id).Distinct().Take(MaxMessages).ToList();
        }

        private static async Task<IList<string>> GetHistoryMessageIdsAsync(GmailService service, string historyId)
        {
            ulong startHistoryId;
            if (!ulong.TryParse(historyId, out startHistoryId)) throw new InvalidOperationException("El historyId almacenado no es válido.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string pageToken = null;
            do
            {
                var request = service.Users.History.List("me");
                request.StartHistoryId = startHistoryId;
                request.HistoryTypes = UsersResource.HistoryResource.ListRequest.HistoryTypesEnum.MessageAdded;
                request.MaxResults = MaxMessages;
                request.PageToken = pageToken;
                var response = await request.ExecuteAsync();
                foreach (var history in response.History ?? new List<History>())
                    foreach (var added in history.MessagesAdded ?? new List<HistoryMessageAdded>())
                        if (added.Message != null && !string.IsNullOrWhiteSpace(added.Message.Id)) ids.Add(added.Message.Id);
                pageToken = response.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken) && ids.Count < MaxMessages);
            return ids.Take(MaxMessages).ToList();
        }

        private static async Task ProcessMessageAsync(GmailService service, int accountId, string messageId, GmailSyncResult result)
        {
            var get = service.Users.Messages.Get("me", messageId);
            get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
            var message = await get.ExecuteAsync();
            var parts = new List<AttachmentPart>();
            CollectAttachmentParts(message.Payload, parts, "0");
            if (parts.Count == 0) return;

            var record = MapMessage(message);
            bool created;
            var databaseMessageId = GmailSyncRepository.EnsureMessage(accountId, record, out created);
            if (created) result.MensajesNuevos++;

            foreach (var part in parts)
            {
                var downloadedPath = GmailSyncRepository.GetDownloadedAttachmentPath(databaseMessageId, part.AttachmentId, part.PartId);
                if (!string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath)) { result.AdjuntosExistentes++; continue; }
                try
                {
                    var data = part.InlineData;
                    if (!string.IsNullOrWhiteSpace(part.AttachmentId))
                    {
                        var attachment = await service.Users.Messages.Attachments.Get("me", message.Id, part.AttachmentId).ExecuteAsync();
                        data = attachment.Data;
                    }
                    var bytes = DecodeBase64Url(data);
                    var identity = !string.IsNullOrWhiteSpace(part.AttachmentId) ? "attachment:" + part.AttachmentId : "part:" + part.PartId;
                    var stored = AttachmentStorage.Save(bytes, record.MessageDateUtc, message.Id, part.FileName, identity);
                    GmailSyncRepository.SaveAttachment(databaseMessageId, new GmailAttachmentRecord { GmailAttachmentId = part.AttachmentId, GmailPartId = part.PartId, OriginalName = part.FileName, MimeType = part.MimeType, SizeBytes = stored.Size, LocalPath = stored.FullPath, HashSha256 = stored.HashSha256, DownloadedUtc = DateTime.UtcNow, Status = "Descargado" });
                    result.AdjuntosDescargados++;
                }
                catch (Exception ex) when (ex is GoogleApiException || ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is System.Data.SqlClient.SqlException)
                {
                    result.Errores++;
                    try { GmailSyncRepository.SaveAttachment(databaseMessageId, new GmailAttachmentRecord { GmailAttachmentId = part.AttachmentId, GmailPartId = part.PartId, OriginalName = part.FileName, MimeType = part.MimeType, SizeBytes = part.DeclaredSize, Status = "Error" }); }
                    catch (System.Data.SqlClient.SqlException) { }
                }
            }
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
