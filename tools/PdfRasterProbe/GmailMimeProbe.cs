using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Google.Apis.Gmail.v1.Data;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class GmailMimeProbe
    {
        internal static int Run(string[] args)
        {
            if (args.Length != 3 && args.Length != 6)
            {
                Console.Error.WriteLine("Uso: --mime <ini> <factura-jpg> [pdf zip rar]");
                return 2;
            }

            Initialize(args[1]);
            var ok = true;
            ok &= VerifyCollected("A_Attachment", Part("a", "attachment", null), 1);
            ok &= VerifyCollected("B_InlineContentId", Part("b", "inline; filename=\"factura.jpg\"", "<b>"), 1);
            ok &= VerifyCollected("C_InlineSinDisposition", Part("c", null, "<c>"), 1);
            ok &= VerifyCollected("D_AttachmentContentId", Part("d", "attachment; filename=\"factura.jpg\"", "<d>"), 1);

            var mixed = new MessagePart { Parts = new List<MessagePart>() };
            for (var index = 0; index < 4; index++) mixed.Parts.Add(Part("inline-" + index, "inline", "<i" + index + ">"));
            mixed.Parts.Add(Part("attachment", "attachment", null));
            ok &= VerifyCollected("E_Mixto", mixed, 5);

            var query = (string)typeof(GmailSyncService).GetField("InitialSearchQuery", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
            var discoveryOk = string.Equals(query, "newer_than:30d", StringComparison.Ordinal) && query.IndexOf("has:attachment", StringComparison.OrdinalIgnoreCase) < 0;
            Console.WriteLine("MIME | Caso=F_DescubrimientoInline | Query=" + query + " | OK=" + discoveryOk);
            ok &= discoveryOk;

            var bytes = File.ReadAllBytes(args[2]);
            var inline = Analyze(bytes, "G_FacturaInline");
            var attachment = Analyze(bytes, "H_FacturaAttachment");
            var same = inline == "FACTURA/OCR" && string.Equals(inline, attachment, StringComparison.Ordinal);
            Console.WriteLine("MIME | Caso=G_H_MismaClasificacion | Inline=" + inline + " | Attachment=" + attachment + " | OK=" + same);
            ok &= same;
            if (args.Length == 6)
            {
                ok &= AnalyzeRegression(args[3], "application/pdf", "PDF");
                ok &= AnalyzeRegression(args[4], "application/zip", "ZIP");
                ok &= AnalyzeRegression(args[5], "application/vnd.rar", "RAR");
            }
            return ok ? 0 : 1;
        }

        private static void Initialize(string iniPath)
        {
            var installed = ConfiguracionIni.Cargar(iniPath);
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MimeProbeData");
            var configuration = new ConfiguracionAplicacion(installed.NombreProyecto, Path.Combine(root, "Logs"), Path.Combine(root, "Trabajo"), Path.Combine(root, "Facturas"), Path.Combine(root, "Revisar"), installed.ZipMaxEntradas, installed.ZipMaxBytesPorArchivo, installed.ZipMaxBytesDescomprimidos, installed.ZipMaxProfundidad, installed.GmailRedirectUri);
            configuration.PrepararRutasOperativas();
            ConfiguracionSistema.Inicializar(configuration);
            Logs.Inicializar(configuration);
        }

        private static MessagePart Part(string id, string disposition, string contentId)
        {
            var headers = new List<MessagePartHeader>();
            if (disposition != null) headers.Add(new MessagePartHeader { Name = "Content-Disposition", Value = disposition });
            if (contentId != null) headers.Add(new MessagePartHeader { Name = "Content-ID", Value = contentId });
            return new MessagePart { PartId = id, Filename = "factura.jpg", MimeType = "image/jpeg", Body = new MessagePartBody { Data = "AA", Size = 1 }, Headers = headers };
        }

        private static bool VerifyCollected(string caseName, MessagePart root, int expected)
        {
            var attachmentType = typeof(GmailSyncService).Assembly.GetType("RecepcionDocumental.Services.AttachmentPart", true);
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(attachmentType));
            var collect = typeof(GmailSyncService).GetMethod("CollectAttachmentParts", BindingFlags.NonPublic | BindingFlags.Static);
            collect.Invoke(null, new object[] { root, list, "0" });
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list) unique.Add((string)attachmentType.GetProperty("PartId").GetValue(item, null));
            var valid = list.Count == expected && unique.Count == expected;
            Console.WriteLine("MIME | Caso=" + caseName + " | Recolectadas=" + list.Count + " | Unicas=" + unique.Count + " | OK=" + valid);
            return valid;
        }

        private static string Analyze(byte[] bytes, string caseName)
        {
            string root;
            AttachmentAnalysis analysis;
            using (var workspace = new AttachmentWorkspace())
            {
                root = workspace.RootPath;
                analysis = DocumentAnalysisService.Analyze(bytes, "factura.jpg", "image/jpeg", workspace);
            }
            var selection = analysis.Candidates[0].Selection;
            var value = selection.Classification + "/" + selection.DetectionMethod;
            Console.WriteLine("MIME | Caso=" + caseName + " | Resultado=" + value + " | WorkspaceEliminado=" + !Directory.Exists(root));
            return value;
        }

        private static bool AnalyzeRegression(string path, string mimeType, string caseName)
        {
            string root;
            AttachmentAnalysis analysis;
            using (var workspace = new AttachmentWorkspace())
            {
                root = workspace.RootPath;
                analysis = DocumentAnalysisService.Analyze(File.ReadAllBytes(path), Path.GetFileName(path), mimeType, workspace);
            }
            var clean = !Directory.Exists(root);
            Console.WriteLine("MIME | Caso=Regresion" + caseName + " | Candidatos=" + analysis.Candidates.Count + " | Descartados=" + analysis.Discarded + " | WorkspaceEliminado=" + clean + " | OK=" + clean);
            return clean;
        }
    }
}
