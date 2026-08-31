using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Data;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Security;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class H1D6AOperationalGmailProbe
    {
        private const string AuthorizedSubject = "RD-H1D6A-20260831-02";
        private const string ResidualMessageId = "1a05913151bf741b";
        private const string RequiredHistoryId = "6796229";
        private static readonly IDictionary<string, Expected> ExpectedByHash = new Dictionary<string, Expected>(StringComparer.OrdinalIgnoreCase)
        {
            { "E8066B5FA877947A47862B566C3A752FD1BA514973CD2B01A7AD31803BBDD28B", new Expected("FACTURA", "OCR") },
            { "B4C8FA3786E8BA119F3C5F40F1BF5868D7591D0E865FF2AF84F7235674F33C88", new Expected("REVISAR", "MDOC_OCR_CONFLICTO") },
            { "382662294B57C459DB9F1231FE0503A77176D130F8EA1BFB709132C3115F1A49", new Expected("DESCARTAR", null) }
        };

        internal static int Run(string[] args)
        {
            if (args.Length != 4) { Console.Error.WriteLine("Uso: --h1d6a-gmail-operational <RecepcionDocumental.ini> <asunto-exacto> <output>"); return 2; }
            var setup = new AppDomainSetup { ApplicationBase = AppDomain.CurrentDomain.BaseDirectory, ConfigurationFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])), "Web.config") };
            var domain = AppDomain.CreateDomain("H1D6A-WebConfig", null, setup);
            try { return domain.ExecuteAssembly(typeof(H1D6AOperationalGmailProbe).Assembly.Location, new[] { "--h1d6a-gmail-operational-inner", args[1], args[2], args[3] }); }
            finally { AppDomain.Unload(domain); }
        }

        internal static int RunInner(string[] args)
        {
            if (args.Length != 4) { Console.Error.WriteLine("Invocación interna H1D6A inválida."); return 2; }
            var output = Path.GetFullPath(args[3]); Directory.CreateDirectory(output);
            var result = new RunResult { Subject = args[2], StartedUtc = DateTime.UtcNow };
            try
            {
                if (!string.Equals(result.Subject, AuthorizedSubject, StringComparison.Ordinal)) throw new InvalidOperationException("El asunto no coincide con el caso H1D6A2 autorizado.");
                Initialize(args[1]);
                var account = GmailSyncRepository.GetActiveAccount();
                if (account == null) throw new InvalidOperationException("No hay una cuenta Gmail activa.");
                if (account.ProtectedRefreshToken == null || account.ProtectedRefreshToken.Length == 0) throw new InvalidOperationException("La cuenta activa no tiene autorización persistida.");
                result.AccountId = account.Id;
                GoogleOAuthSettings settings; string error;
                if (!GoogleOAuthSettings.TryLoad(out settings, out error)) throw new InvalidOperationException(error);
                var refreshToken = RefreshTokenProtector.Unprotect(account.ProtectedRefreshToken);
                using (var client = GmailOAuthService.CreateAuthorizedClient(settings, account.Email, refreshToken))
                {
                    var list = client.Service.Users.Messages.List("me"); list.Q = "subject:\"" + result.Subject + "\""; list.MaxResults = 20;
                    var ids = (list.Execute().Messages ?? new List<Message>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id)).Select(x => x.Id).Distinct().ToList();
                    var exact = new List<Message>();
                    foreach (var id in ids) { var get = client.Service.Users.Messages.Get("me", id); get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full; var message = get.Execute(); if (string.Equals(Header(message, "Subject"), result.Subject, StringComparison.Ordinal)) exact.Add(message); }
                    if (exact.Count != 1) throw new InvalidOperationException("La búsqueda no devolvió exactamente un mensaje con Subject exacto; cantidad=" + exact.Count + ".");
                    var target = exact[0]; result.GmailMessageId = target.Id;
                    if (string.Equals(target.Id, ResidualMessageId, StringComparison.Ordinal)) throw new InvalidOperationException("El mensaje -02 resolvió al GmailMessageId residual -01.");
                    var parts = new List<Part>(); Collect(target.Payload, parts, "0");
                    if (parts.Count != 3) throw new InvalidOperationException("El mensaje no contiene exactamente 3 adjuntos documentales; cantidad=" + parts.Count + ".");
                    foreach (var part in parts)
                    {
                        var data = part.InlineData;
                        if (!string.IsNullOrWhiteSpace(part.AttachmentId)) data = client.Service.Users.Messages.Attachments.Get("me", target.Id, part.AttachmentId).Execute().Data;
                        part.Bytes = Decode(data); part.TransportHash = Hash(part.Bytes);
                    }
                    if (parts.Select(x => x.TransportHash).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3 || parts.Any(x => !ExpectedByHash.ContainsKey(x.TransportHash)) || ExpectedByHash.Keys.Any(h => !parts.Any(x => h.Equals(x.TransportHash, StringComparison.OrdinalIgnoreCase))))
                        throw new InvalidOperationException("Los hashes de transporte no coinciden exactamente con el conjunto autorizado.");
                    result.Parts = parts;
                    foreach (var part in parts)
                    {
                        part.Workspace = new AttachmentWorkspace();
                        part.Analysis = DocumentAnalysisService.Analyze(part.Bytes, part.FileName, part.MimeType, part.Workspace);
                        part.Classification = part.Analysis.Candidates.Count == 0 ? "DESCARTAR" : SingleClassification(part.Analysis);
                        part.Method = part.Analysis.Candidates.Count == 0 ? string.Empty : string.Join("+", part.Analysis.Candidates.Select(x => x.Selection.DetectionMethod).Distinct());
                        var expected = ExpectedByHash[part.TransportHash];
                        if (!string.Equals(part.Classification, expected.Classification, StringComparison.Ordinal) || (expected.Method != null && (expected.Method == "OCR" ? part.Method.IndexOf("OCR", StringComparison.Ordinal) < 0 : !string.Equals(part.Method, expected.Method, StringComparison.Ordinal))))
                            throw new InvalidOperationException("La clasificación productiva no coincide con la expectativa para " + part.TransportHash.Substring(0, 12) + ".");
                    }
                    result.Before = Snapshot(account.Id, target.Id);
                    ValidateBefore(result.Before);
                    if (result.Before.MessageExists) throw new InvalidOperationException("El GmailMessageId -02 ya existe; se detuvo para evitar duplicación.");
                    var record = Map(target); bool created; var messageId = GmailSyncRepository.EnsureMessage(account.Id, record, out created);
                    if (!created) throw new InvalidOperationException("EnsureMessage detectó una carrera o duplicado; no se persistieron documentos.");
                    result.DatabaseMessageId = messageId;
                    foreach (var part in parts)
                    foreach (var candidate in part.Analysis.Candidates)
                    {
                        if (DocumentRepository.Exists(messageId, part.PartId, candidate.OriginHash)) throw new InvalidOperationException("Documento inesperadamente existente antes de Save.");
                        var stored = DocumentStorage.Save(candidate.SourcePath, candidate.Selection.Classification, record.MessageDateUtc, target.Id, candidate.OriginalName, candidate.OriginHash);
                        if (!DocumentRepository.Save(messageId, part.PartId, candidate, stored)) throw new InvalidOperationException("DocumentRepository.Save no insertó el candidato.");
                        result.Stored.Add(new Stored { PartId = part.PartId, TransportHash = part.TransportHash, Classification = candidate.Selection.Classification, Method = candidate.Selection.DetectionMethod, Path = stored.FullPath, Hash = stored.HashSha256, Size = stored.Size });
                    }
                }
                result.After = Snapshot(account.Id, result.GmailMessageId);
                ValidateAfter(result);
                result.Approved = true; result.Status = "APROBADO";
            }
            catch (Exception ex) { result.Status = "NO APROBADO"; result.Error = ex.GetType().Name + ": " + ex.Message; try { if (result.AccountId > 0 && result.Before != null) result.After = Snapshot(result.AccountId, result.GmailMessageId); } catch { } }
            finally { foreach(var part in result.Parts) if(part.Workspace!=null) part.Workspace.Dispose(); result.FinishedUtc = DateTime.UtcNow; WriteReports(output, result); }
            Console.WriteLine("H1D6A | Estado=" + result.Status + " | GmailMessageId=" + (result.GmailMessageId ?? "(no resuelto)") + " | Documentos=" + result.Stored.Count + " | Output=" + output);
            if (!string.IsNullOrWhiteSpace(result.Error)) Console.Error.WriteLine("ERROR H1D6A | " + result.Error);
            return result.Approved ? 0 : 1;
        }

        private static void Initialize(string iniPath) { var c = ConfiguracionIni.Cargar(Path.GetFullPath(iniPath)); c.PrepararRutasOperativas(); ConfiguracionSistema.Inicializar(c); Logs.Inicializar(c); }
        private static string SingleClassification(AttachmentAnalysis a) { var values = a.Candidates.Select(x => x.Selection.Classification).Distinct().ToList(); if (values.Count != 1) throw new InvalidOperationException("Un adjunto produjo clasificaciones múltiples."); return values[0]; }
        private static string Header(Message m, string name) { var h = (m.Payload == null ? null : m.Payload.Headers) ?? new List<MessagePartHeader>(); var x = h.FirstOrDefault(y => string.Equals(y.Name, name, StringComparison.OrdinalIgnoreCase)); return x == null ? null : x.Value; }
        private static GmailMessageRecord Map(Message m) { var ms = m.InternalDate ?? 0; return new GmailMessageRecord { GmailMessageId=m.Id, GmailThreadId=m.ThreadId, MessageDateUtc=ms>0?DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime:DateTime.UtcNow, From=Header(m,"From")??"Remitente no disponible", Subject=Header(m,"Subject"), Snippet=m.Snippet }; }
        private static void Collect(MessagePart p, IList<Part> result, string path) { if (p == null) return; var id=string.IsNullOrWhiteSpace(p.PartId)?path:p.PartId; if (!string.IsNullOrWhiteSpace(p.Filename) && p.Body != null && (!string.IsNullOrWhiteSpace(p.Body.AttachmentId)||!string.IsNullOrWhiteSpace(p.Body.Data))) result.Add(new Part { AttachmentId=p.Body.AttachmentId, PartId=id, FileName=p.Filename, MimeType=p.MimeType, InlineData=p.Body.Data }); var children=p.Parts??new List<MessagePart>(); for(var i=0;i<children.Count;i++) Collect(children[i],result,path+"."+i); }
        private static byte[] Decode(string value) { if(string.IsNullOrWhiteSpace(value))throw new FormatException("Adjunto vacío."); var s=value.Replace('-','+').Replace('_','/'); if(s.Length%4==2)s+="==";else if(s.Length%4==3)s+="=";else if(s.Length%4==1)throw new FormatException("Base64URL inválido.");return Convert.FromBase64String(s); }
        private static string Hash(byte[] bytes) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-",string.Empty); }
        private static string HashFile(string path) { using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",string.Empty); }

        private static DbSnapshot Snapshot(int accountId, string gmailMessageId)
        {
            const string sql=@"SELECT UltimoHistoryId,UltimaConsultaUtc FROM dbo.GmailCuenta WHERE Id=@AccountId;
SELECT COUNT_BIG(*) FROM dbo.GmailMensaje;
SELECT COUNT_BIG(*) FROM dbo.DocumentoRecepcion;
SELECT COUNT_BIG(*) FROM dbo.GmailAdjunto;
SELECT COUNT_BIG(*) FROM dbo.GmailMensaje WHERE GmailCuentaId=@AccountId AND GmailMessageId=@MessageId;
SELECT COUNT_BIG(*),COUNT_BIG(d.Id) FROM dbo.GmailMensaje m LEFT JOIN dbo.DocumentoRecepcion d ON d.GmailMensajeId=m.Id WHERE m.GmailCuentaId=@AccountId AND m.GmailMessageId=@ResidualId;
SELECT d.Id,d.Clasificacion,d.MetodoDeteccion,d.RutaLocal,d.HashSha256 FROM dbo.DocumentoRecepcion d INNER JOIN dbo.GmailMensaje m ON m.Id=d.GmailMensajeId WHERE m.GmailCuentaId=@AccountId AND m.GmailMessageId=@MessageId ORDER BY d.Id;";
            var s=new DbSnapshot();using(var cn=new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))using(var cmd=new SqlCommand(sql,cn)){cmd.Parameters.Add("@AccountId",SqlDbType.Int).Value=accountId;cmd.Parameters.Add("@MessageId",SqlDbType.NVarChar,255).Value=(object)gmailMessageId??DBNull.Value;cmd.Parameters.Add("@ResidualId",SqlDbType.NVarChar,255).Value=ResidualMessageId;cn.Open();using(var r=cmd.ExecuteReader()){if(!r.Read())throw new InvalidOperationException("Cuenta activa ausente durante snapshot.");s.HistoryId=r.IsDBNull(0)?null:r.GetString(0);s.LastQueryUtc=r.IsDBNull(1)?(DateTime?)null:r.GetDateTime(1);r.NextResult();r.Read();s.Messages=r.GetInt64(0);r.NextResult();r.Read();s.Documents=r.GetInt64(0);r.NextResult();r.Read();s.Attachments=r.GetInt64(0);r.NextResult();r.Read();s.MessageExists=r.GetInt64(0)!=0;r.NextResult();r.Read();s.ResidualExists=r.GetInt64(0)==1;s.ResidualDocuments=r.GetInt64(1);r.NextResult();while(r.Read())s.TargetDocuments.Add(new DocumentRow{Id=r.GetInt64(0),Classification=r.GetString(1),Method=r.GetString(2),Path=r.GetString(3),Hash=r.GetString(4)});}}return s;
        }
        private static void ValidateBefore(DbSnapshot s) { if(!string.Equals(s.HistoryId,RequiredHistoryId,StringComparison.Ordinal))throw new InvalidOperationException("UltimoHistoryId previo no es 6796229.");if(!s.ResidualExists||s.ResidualDocuments!=0)throw new InvalidOperationException("El residual -01 no existe o ya tiene documentos asociados."); }
        private static void ValidateAfter(RunResult r)
        {
            if(r.Before==null||r.After==null)throw new InvalidOperationException("Faltan snapshots SQL.");
            if(!string.Equals(r.Before.HistoryId,r.After.HistoryId,StringComparison.Ordinal)||r.Before.LastQueryUtc!=r.After.LastQueryUtc)throw new InvalidOperationException("El cursor o UltimaConsultaUtc cambió.");
            if(!string.Equals(r.After.HistoryId,RequiredHistoryId,StringComparison.Ordinal))throw new InvalidOperationException("UltimoHistoryId posterior no es 6796229.");
            if(r.Before.ResidualExists!=r.After.ResidualExists||r.Before.ResidualDocuments!=r.After.ResidualDocuments||!r.After.ResidualExists||r.After.ResidualDocuments!=0)throw new InvalidOperationException("El residual -01 fue alterado.");
            if(r.After.Messages-r.Before.Messages!=1||r.After.Documents-r.Before.Documents!=2||r.After.Attachments-r.Before.Attachments!=0||!r.After.MessageExists)throw new InvalidOperationException("Los deltas SQL no son los esperados (+1 mensaje, +2 documentos, +0 adjuntos).");
            if(r.After.TargetDocuments.Count!=2||r.After.TargetDocuments.Count(x=>x.Classification=="FACTURA")!=1||r.After.TargetDocuments.Count(x=>x.Classification=="REVISAR")!=1)throw new InvalidOperationException("Los documentos SQL del mensaje -02 no son exactamente FACTURA y REVISAR.");
            if(r.Stored.Count!=2||r.Stored.Any(x=>!File.Exists(x.Path)||!HashFile(x.Path).Equals(x.Hash,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("La verificación física del almacenamiento falló.");
            if(r.After.TargetDocuments.Any(x=>!File.Exists(x.Path)||!HashFile(x.Path).Equals(x.Hash,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("La ruta o hash registrado en SQL no coincide físicamente.");
            if(r.Stored.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=2)throw new InvalidOperationException("Se detectaron rutas físicas duplicadas.");
        }
        private static void WriteReports(string output, RunResult r)
        {
            Func<DateTime?,string> dt=x=>x.HasValue?x.Value.ToString("O"):"NULL";Func<string,string> csv=x=>"\""+(x??"").Replace("\"","\"\"")+"\"";
            var rows=new List<string>{"TransportSha256,FileName,Classification,DetectionMethod,Candidates,Stored,DocumentoRecepcionId,PhysicalPath,PhysicalSha256"};
            foreach(var p in r.Parts){var stored=r.Stored.FirstOrDefault(x=>x.TransportHash==p.TransportHash);var doc=stored==null||r.After==null?null:r.After.TargetDocuments.FirstOrDefault(x=>string.Equals(x.Path,stored.Path,StringComparison.OrdinalIgnoreCase));rows.Add(string.Join(",",new[]{p.TransportHash,p.FileName,p.Classification,p.Method,p.Analysis.Candidates.Count.ToString(),(stored!=null).ToString(),doc==null?"":doc.Id.ToString(),stored==null?"":stored.Path,stored==null?"":stored.Hash}.Select(csv)));}
            File.WriteAllLines(Path.Combine(output,"h1d6a2-result.csv"),rows,new UTF8Encoding(false));
            var before=r.Before??new DbSnapshot();var after=r.After??new DbSnapshot();
            File.WriteAllText(Path.Combine(output,"sql-before-after.md"),"# H1D6A2 — SQL antes/después\n\n| Dato | Antes | Después |\n|---|---:|---:|\n| GmailCuenta.Id | "+r.AccountId+" | "+r.AccountId+" |\n| UltimoHistoryId | `"+(before.HistoryId??"NULL")+"` | `"+(after.HistoryId??"NULL")+"` |\n| UltimaConsultaUtc | `"+dt(before.LastQueryUtc)+"` | `"+dt(after.LastQueryUtc)+"` |\n| GmailMensaje | "+before.Messages+" | "+after.Messages+" |\n| DocumentoRecepcion | "+before.Documents+" | "+after.Documents+" |\n| GmailAdjunto | "+before.Attachments+" | "+after.Attachments+" |\n| Mensaje -02 existe | "+before.MessageExists+" | "+after.MessageExists+" |\n| Residual -01 existe | "+before.ResidualExists+" | "+after.ResidualExists+" |\n| Documentos residual -01 | "+before.ResidualDocuments+" | "+after.ResidualDocuments+" |\n",new UTF8Encoding(false));
            var error=string.IsNullOrWhiteSpace(r.Error)?"Ninguno":r.Error;
            var docs=after.TargetDocuments.Count==0?"Ninguno":string.Join("; ",after.TargetDocuments.Select(x=>"Id="+x.Id+", "+x.Classification+", "+x.Method+", "+x.Path));
            File.WriteAllText(Path.Combine(output,"operational-result.md"),"# H1D6A2 — Resultado operativo\n\n- Estado: **"+r.Status+"**\n- Asunto exacto: `"+r.Subject+"`\n- GmailMessageId nuevo: `"+(r.GmailMessageId??"no resuelto")+"`\n- GmailMessageId residual: `"+ResidualMessageId+"`\n- Residual intacto: "+(after.ResidualExists&&after.ResidualDocuments==0)+"\n- Inicio UTC: `"+r.StartedUtc.ToString("O")+"`\n- Fin UTC: `"+r.FinishedUtc.ToString("O")+"`\n- Documentos persistidos: "+r.Stored.Count+"\n- DocumentoRecepcion: "+docs+"\n- Error: "+error+"\n- `SynchronizeAsync`: no invocado.\n- `CompleteSync`: no invocado.\n",new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output,"resumen.md"),"# H1D6A2 — Resumen\n\n**"+r.Status+"**\n\n"+(r.Approved?"El mensaje -02 superó identidad, hashes, análisis previo 3/3, persistencia, integridad física, conservación del residual -01 y conservación del cursor. Validación UI pendiente y manual: abrir Gmail_Bandeja, localizar el asunto sin pulsar Buscar, abrir el mensaje y comprobar los dos documentos; luego revisar FACTURA y REVISAR en Documentos.":"La ejecución se detuvo ante el primer incumplimiento: "+error)+"\n",new UTF8Encoding(false));
        }
        private sealed class Expected { internal Expected(string c,string m){Classification=c;Method=m;} internal string Classification,Method; }
        private sealed class Part { internal string AttachmentId,PartId,FileName,MimeType,InlineData,TransportHash,Classification,Method;internal byte[] Bytes;internal AttachmentAnalysis Analysis;internal AttachmentWorkspace Workspace; }
        private sealed class Stored { internal string PartId,TransportHash,Classification,Method,Path,Hash;internal long Size; }
        private sealed class DocumentRow { internal long Id;internal string Classification,Method,Path,Hash; }
        private sealed class DbSnapshot { internal string HistoryId;internal DateTime? LastQueryUtc;internal long Messages,Documents,Attachments,ResidualDocuments;internal bool MessageExists,ResidualExists;internal List<DocumentRow> TargetDocuments=new List<DocumentRow>(); }
        private sealed class RunResult { internal string Subject,GmailMessageId,Status,Error;internal int AccountId;internal long DatabaseMessageId;internal DateTime StartedUtc,FinishedUtc;internal bool Approved;internal DbSnapshot Before,After;internal List<Part> Parts=new List<Part>();internal List<Stored> Stored=new List<Stored>(); }
    }
}
