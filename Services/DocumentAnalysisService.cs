using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace RecepcionDocumental.Services
{
    public sealed class DocumentCandidate
    {
        public string SourcePath { get; set; }
        public string OriginalName { get; set; }
        public string MimeType { get; set; }
        public string OriginType { get; set; }
        public string InternalContainerPath { get; set; }
        public string OriginHash { get; set; }
        public long SizeBytes { get; set; }
        public InvoiceSelection Selection { get; set; }
        public VisualShadowResult VisualShadow { get; set; }
        public bool QrDetected { get; set; }
        public int? TipoComprobanteArca { get; set; }
        public string QrSource { get; set; }
        public int RasterQrDurationMilliseconds { get; set; }
        public int PdfRasterizationCount { get; set; }
        public int PdfPagesRenderedForOcr { get; set; }
        public int PdfPagesRenderedForShadow { get; set; }
        public bool PdfFirstPageRenderedByOcr { get; set; }
        public bool PdfFirstPageReusedByShadow { get; set; }
        public bool EmbeddedQrDetected { get; set; }
        public bool RasterQrDetected { get; set; }
        public bool RasterQrArcaValid { get; set; }
        public int? RasterTipoComprobanteArca { get; set; }
    }

    public sealed class AttachmentAnalysis
    {
        public IList<DocumentCandidate> Candidates { get; set; } = new List<DocumentCandidate>();
        public int ContainersZip { get; set; }
        public int ZipFilesAnalyzed { get; set; }
        public int Discarded { get; set; }
    }

    internal sealed class ZipBudget { public int Entries; public long TotalBytes; }
    internal sealed class PdfOcrAnalysisResult { public InvoiceSelection Selection; public ArcaQrEvidence RasterQr=new ArcaQrEvidence(); public int RasterQrDurationMilliseconds; public int RasterizationCount; public int PagesRendered; public bool FirstPageRendered; public string FirstPageVisualFailureReason; public OcrImageData FirstRasterImage; }

    public static class DocumentAnalysisService
    {
        private static readonly string[] ContainerExtensions = { ".zip", ".rar", ".7z" };
        private static readonly string[] PackagedDocumentExtensions = {
            ".docx", ".docm", ".dotx", ".dotm",
            ".xlsx", ".xlsm", ".xltx", ".xltm",
            ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm",
            ".odt", ".ods", ".odp"
        };
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

        public static AttachmentAnalysis Analyze(byte[] bytes, string fileName, string mimeType, AttachmentWorkspace workspace)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            Logs.LogProc("DocumentAnalysis | Inicio análisis attachment | NombreOmitido=true");
            var extension = Path.GetExtension(fileName ?? string.Empty);
            var rootPath = workspace.CreatePath(extension); File.WriteAllBytes(rootPath, bytes);
            var result = new AttachmentAnalysis();
            if (IsContainer(fileName, mimeType, bytes))
            {
                Logs.LogProc("DocumentAnalysis | Contenedor detectado | Profundidad=1");
                AnalyzeContainer(rootPath, fileName, mimeType, fileName, 1, workspace, new ZipBudget(), result, true);
            }
            else AnalyzeDocument(rootPath, fileName, mimeType, "DIRECTO", null, HashFile(rootPath), workspace, result);
            return result;
        }

        private static void AnalyzeContainer(string containerPath, string containerName, string mimeType, string chain, int depth, AttachmentWorkspace workspace, ZipBudget budget, AttachmentAnalysis result, bool root)
        {
            result.ContainersZip++;
            var candidateStart = result.Candidates.Count;
            var discardedStart = result.Discarded;
            var filesStart = result.ZipFilesAnalyzed;
            var config = ConfiguracionSistema.Actual;
            if (depth > config.ZipMaxProfundidad) { AddUnanalyzableContainer(containerPath, containerName, mimeType, chain, root, "El contenedor anidado supera la profundidad permitida.", result); return; }
            try
            {
                using (var reader = ReaderFactory.OpenReader(containerPath))
                {
                    var extractionRoot = Path.Combine(workspace.RootPath, "container-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(extractionRoot);
                    var extractionPrefix = Path.GetFullPath(extractionRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    while (reader.MoveToNextEntry())
                    {
                        var entry = reader.Entry;
                        if (++budget.Entries > config.ZipMaxEntradas) throw new InvalidDataException("El contenedor supera MaxEntradas.");
                        if (entry.IsDirectory) continue;
                        if (entry.IsEncrypted || !string.IsNullOrEmpty(entry.LinkTarget)) throw new InvalidDataException("Entrada de contenedor no soportada, enlazada o protegida.");
                        var entryName = (entry.Key ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
                        if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName) || entryName.IndexOf(':') >= 0) throw new InvalidDataException("Ruta absoluta o inválida dentro del contenedor.");
                        var destination = Path.GetFullPath(Path.Combine(extractionRoot, entryName));
                        if (!destination.StartsWith(extractionPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Se bloqueó una entrada con path traversal.");
                        if (File.Exists(destination) || Directory.Exists(destination)) throw new InvalidDataException("El contenedor contiene rutas en colisión.");
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        long entryBytes = 0; var buffer = new byte[81920]; int read;
                        using (var entryStream = reader.OpenEntryStream())
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                entryBytes += read; budget.TotalBytes += read;
                                if (entryBytes > config.ZipMaxBytesPorArchivo) throw new InvalidDataException("Una entrada supera MaxBytesPorArchivo.");
                                if (budget.TotalBytes > config.ZipMaxBytesDescomprimidos) throw new InvalidDataException("La expansión supera MaxBytesDescomprimidos.");
                                output.Write(buffer, 0, read);
                            }
                            output.Flush(true);
                        }
                        result.ZipFilesAnalyzed++;
                        var nestedChain = chain + "!/" + entry.Key;
                        if (nestedChain.Length > 2000) throw new InvalidDataException("La cadena de origen supera la longitud permitida.");
                        if (IsContainerFile(destination, entry.Key)) AnalyzeContainer(destination, entry.Key, GuessMime(entry.Key), nestedChain, depth + 1, workspace, budget, result, false);
                        else AnalyzeDocument(destination, Path.GetFileName(entry.Key), GuessMime(entry.Key), "ZIP", nestedChain, StableZipOriginHash(nestedChain, destination), workspace, result);
                    }
                }
                Logs.LogProc("DocumentAnalysis | Contenedor entradas analizadas | Entradas=" + budget.Entries + " | Bytes=" + budget.TotalBytes);
            }
            catch (Exception ex) when (IsContainerException(ex))
            {
                while (result.Candidates.Count > candidateStart) result.Candidates.RemoveAt(result.Candidates.Count - 1);
                result.Discarded = discardedStart; result.ZipFilesAnalyzed = filesStart;
                AddUnanalyzableContainer(containerPath, containerName, mimeType, chain, root, ex.Message, result);
            }
        }

        private static void AnalyzeDocument(string path, string name, string mime, string originType, string internalPath, string originHash, AttachmentWorkspace workspace, AttachmentAnalysis result)
        {
            InvoiceSelection selection;
            var qr = new ArcaQrEvidence();
            var qrSource = "NINGUNO";var rasterQrDuration=0;var rasterizationCount=0;var pagesRenderedForOcr=0;var pagesRenderedForShadow=0;var firstPageRenderedByOcr=false;var firstPageReusedByShadow=false;string firstPageVisualFailureReason=null;var embeddedQrDetected=false;var rasterQrDetected=false;var rasterQrValid=false;int? rasterTipo=null;OcrImageData visualRaster=null;
            if (string.Equals(Path.GetExtension(name), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                qr = MdocPdfQrDetector.Detect(path);
                embeddedQrDetected=qr.QrDetected;
                Logs.LogProc("DocumentAnalysis | QR detectado=" + (qr.QrDetected ? "Sí" : "No") + " | QR ARCA válido=" + (qr.IsValid ? "Sí" : "No") + (qr.IsValid ? " | Versión=1 | TipoCmp=" + qr.TipoComprobante.Value : string.Empty));
                var pdf = MdocPdfTextExtractor.Extract(path);
                var mdocSelection = InvoiceSelector.SelectPdf(pdf.Text, pdf.HasUsefulText);
                Logs.LogProc("DocumentAnalysis | MdocTextoUtil=" + (pdf.HasUsefulText ? "Sí" : "No") + " | MdocClasificacion=" + mdocSelection.Classification);
                InvoiceSelection textSelection = mdocSelection;
                if (string.Equals(mdocSelection.Classification, "REVISAR", StringComparison.Ordinal))
                {
                    var ocrReason = pdf.HasUsefulText ? "MDOC_REVISAR" : "MDOC_SIN_TEXTO";
                    Logs.LogProc("DocumentAnalysis | OCR requerido=Sí | Motivo=" + ocrReason);
                    var ocrAnalysis = AnalyzePdfWithOcr(path, pdf, workspace);visualRaster=ocrAnalysis.FirstRasterImage;firstPageRenderedByOcr=ocrAnalysis.FirstPageRendered;firstPageVisualFailureReason=ocrAnalysis.FirstPageVisualFailureReason;var ocrSelection=ocrAnalysis.Selection;rasterQrDuration=ocrAnalysis.RasterQrDurationMilliseconds;rasterizationCount=ocrAnalysis.RasterizationCount;pagesRenderedForOcr=ocrAnalysis.PagesRendered;rasterQrDetected=ocrAnalysis.RasterQr.QrDetected;rasterQrValid=ocrAnalysis.RasterQr.IsValid;rasterTipo=ocrAnalysis.RasterQr.TipoComprobante;
                    textSelection = FusePdfSelections(mdocSelection, ocrSelection);
                    Logs.LogProc("DocumentAnalysis | OCR resultado=" + ocrSelection.Classification + " | FusionAccion=" + FusionAction(mdocSelection, ocrSelection));
                    if(qr.IsValid&&ocrAnalysis.RasterQr.IsValid&&qr.TipoComprobante!=ocrAnalysis.RasterQr.TipoComprobante)
                    {
                        selection=InvoiceSelector.Review("QR_FUENTES_CONFLICTO","Los QR ARCA embedded y raster informan tipos de comprobante distintos.",70);qr=new ArcaQrEvidence{QrDetected=true};qrSource="CONFLICTO";
                        Logs.LogProc("DocumentAnalysis | QR efectivo=CONFLICTO | Fuentes=EMBEDDED+RASTER | DuracionDecodeRasterMs="+rasterQrDuration);
                        goto SelectionReady;
                    }
                    if(!qr.IsValid&&ocrAnalysis.RasterQr.IsValid){qr=ocrAnalysis.RasterQr;qrSource="RASTER";}else if(qr.IsValid)qrSource="EMBEDDED";
                    Logs.LogProc("DocumentAnalysis | QR raster detectado="+(ocrAnalysis.RasterQr.QrDetected?"Sí":"No")+" | QR raster ARCA válido="+(ocrAnalysis.RasterQr.IsValid?"Sí":"No")+" | DuracionDecodeRasterMs="+rasterQrDuration);
                }
                else Logs.LogProc("DocumentAnalysis | OCR requerido=No | Motivo=MDOC_CONCLUYENTE | FusionAccion=MDOC_CONSERVADO");
                if(qr.IsValid&&qrSource=="NINGUNO")qrSource="EMBEDDED";
                selection = ArcaQrDecoder.Combine(qr, textSelection);
            }
            else if (IsImage(name)) selection = AnalyzeImageWithOcr(path, Path.GetExtension(name));
            else selection = InvoiceSelector.SelectNonPdf(name);
        SelectionReady:
            Logs.LogProc("DocumentAnalysis | Documento clasificado | Clasificacion=" + selection.Classification + " | Metodo=" + selection.DetectionMethod);
            if (selection.Classification == "DESCARTAR") { result.Discarded++; Logs.LogProc("DocumentAnalysis | Documento descartado | Metodo=" + selection.DetectionMethod); return; }
            VisualShadowResult visualShadow=null;
            if(ConfiguracionSistema.Actual.VisionShadowEnabled)
            {
                visualShadow=VisualInvoiceShadowService.CreateVersionErrorIfUnsupported("MODEL_VERSION_VALIDATION");
                if(visualShadow==null&&string.Equals(Path.GetExtension(name),".pdf",StringComparison.OrdinalIgnoreCase))
                {
                    if(visualRaster!=null){firstPageReusedByShadow=true;visualShadow=VisualInvoiceShadowService.EvaluateCanonicalPng(visualRaster.Bytes,"PDF_OCR_RASTER_REUSED",true);}
                    else if(firstPageRenderedByOcr) visualShadow=VisualInvoiceShadowService.CreateRasterError("PDF_OCR_RASTER_NOT_REUSABLE",firstPageVisualFailureReason);
                    else { var first=PdfPageRasterizer.RasterizeFirstPage(path,workspace);rasterizationCount++;pagesRenderedForShadow=first.PagesRendered;visualShadow=first.Images.Count==0?VisualInvoiceShadowService.CreateRasterError("PDF_SHADOW_FIRST_PAGE",first.FailureReason):VisualInvoiceShadowService.EvaluateCanonicalPng(first.Images[0].Bytes,"PDF_SHADOW_FIRST_PAGE",false); }
                }
                else if(visualShadow==null&&IsImage(name)) visualShadow=VisualInvoiceShadowService.EvaluateImageFile(path);
                else if(visualShadow==null) visualShadow=VisualInvoiceShadowService.CreateUnsupportedError();
                LogVisualShadow(visualShadow);
            }
            result.Candidates.Add(new DocumentCandidate { SourcePath = path, OriginalName = SafeOriginalName(name), MimeType = mime, OriginType = originType, InternalContainerPath = internalPath, OriginHash = originHash, SizeBytes = new FileInfo(path).Length, Selection = selection, VisualShadow=visualShadow, QrDetected = qr.QrDetected, TipoComprobanteArca = qr.IsValid ? qr.TipoComprobante : null,QrSource=qrSource,RasterQrDurationMilliseconds=rasterQrDuration,PdfRasterizationCount=rasterizationCount,PdfPagesRenderedForOcr=pagesRenderedForOcr,PdfPagesRenderedForShadow=pagesRenderedForShadow,PdfFirstPageRenderedByOcr=firstPageRenderedByOcr,PdfFirstPageReusedByShadow=firstPageReusedByShadow,EmbeddedQrDetected=embeddedQrDetected,RasterQrDetected=rasterQrDetected,RasterQrArcaValid=rasterQrValid,RasterTipoComprobanteArca=rasterTipo });
        }

        private static void LogVisualShadow(VisualShadowResult value)
        {
            if(value==null)return;
            if(value.Status=="OK")Logs.LogProc("VisualShadow | DocumentoConservado=true | Estado=OK | Modelo="+value.ModelVersion+" | Zona="+value.Zone+" | PFactura="+value.PFactura.Value.ToString("0.#########",System.Globalization.CultureInfo.InvariantCulture)+" | RasterReutilizado="+value.RasterReused+" | TotalMs="+value.TotalMilliseconds);
            else Logs.LogError("VisualShadow | Estado=ERROR | Codigo="+Logs.SanitizarMensaje(value.ErrorCode));
        }

        internal static InvoiceSelection FusePdfSelections(InvoiceSelection mdocSelection, InvoiceSelection ocrSelection)
        {
            if (mdocSelection == null) throw new ArgumentNullException("mdocSelection");
            if (!string.Equals(mdocSelection.Classification, "REVISAR", StringComparison.Ordinal)) return mdocSelection;
            if (ocrSelection == null) throw new ArgumentNullException("ocrSelection");
            if (string.Equals(ocrSelection.Classification, "FACTURA", StringComparison.Ordinal)) return ocrSelection;
            if (string.Equals(ocrSelection.Classification, "DESCARTAR", StringComparison.Ordinal))
                return InvoiceSelector.Review("MDOC_OCR_CONFLICTO", "Mdoc no produjo una clasificación concluyente y OCR detectó evidencia de otro tipo documental. Se conserva REVISAR por política de fusión conservadora.", null);
            return ocrSelection;
        }

        private static string FusionAction(InvoiceSelection mdocSelection, InvoiceSelection ocrSelection)
        {
            if (!string.Equals(mdocSelection.Classification, "REVISAR", StringComparison.Ordinal)) return "MDOC_CONSERVADO";
            if (string.Equals(ocrSelection.Classification, "FACTURA", StringComparison.Ordinal)) return "OCR_PROMUEVE_FACTURA";
            if (string.Equals(ocrSelection.Classification, "DESCARTAR", StringComparison.Ordinal)) return "OCR_DESCARTAR_BLOQUEADO";
            return "OCR_MANTIENE_REVISAR";
        }

        private static PdfOcrAnalysisResult AnalyzePdfWithOcr(string path, PdfTextResult pdf, AttachmentWorkspace workspace)
        {
            Logs.LogProc("DocumentAnalysis | FuenteOCR=RASTER_PAGINA");
            var output=new PdfOcrAnalysisResult();var raster = PdfPageRasterizer.Rasterize(path, workspace);output.RasterizationCount=1;output.PagesRendered=raster.PagesRendered;output.FirstPageRendered=raster.FirstPageRendered;output.FirstPageVisualFailureReason=raster.FirstPageVisualFailureReason;output.FirstRasterImage=raster.FirstPageForVisualReuse;
            Logs.LogProc("PDFRaster | Ejecutado=Sí | Paginas=" + raster.PageCount + " | DPI=" + OcrLimits.PdfRasterDpi + " | DuracionMs=" + raster.DurationMilliseconds);
            if (raster.LimitExceeded)
            { output.Selection=InvoiceSelector.Review("OCR_LIMITE", raster.FailureReason, null);return output; }
            if (raster.Images.Count == 0)
            {
                if (raster.StructuralFailure)
                    Logs.LogError("PDFRaster | Operación=Rasterizar | Estado=FalloEstructural | Motivo=" + Logs.SanitizarMensaje(raster.FailureReason));
                output.Selection=InvoiceSelector.Review("OCR_RENDER_ERROR", raster.FailureReason ?? pdf.FailureReason ?? "No se pudo obtener una imagen procesable para OCR.", null);return output;
            }
            var rasterQr=RasterQrDetector.Detect(raster.Images);output.RasterQr=rasterQr.Evidence;output.RasterQrDurationMilliseconds=rasterQr.DurationMilliseconds;output.Selection=SelectOcr(raster.Images,"PDF_RASTER");return output;
        }

        private static InvoiceSelection AnalyzeImageWithOcr(string path, string extension)
        {
            var ocr = DocumentOcrService.RecognizeImageFile(path);
            return SelectOcrWithHeaderFallback(ocr, () => DocumentOcrService.RecognizeImageHeader(path), string.IsNullOrWhiteSpace(extension) ? "IMAGEN" : extension.TrimStart('.').ToUpperInvariant());
        }

        private static InvoiceSelection SelectOcr(IEnumerable<OcrImageData> images, string type)
        {
            var candidates = images == null ? new List<OcrImageData>() : images.ToList();
            return SelectOcrWithHeaderFallback(DocumentOcrService.Recognize(candidates), () => DocumentOcrService.RecognizeHeader(candidates), type);
        }

        private static InvoiceSelection SelectOcrWithHeaderFallback(OcrResult ocr, Func<OcrResult> recognizeHeader, string type)
        {
            Logs.LogProc("DocumentAnalysis | OCR ejecutado=Sí | Tipo=" + type + " | Imagenes=" + ocr.ImagesProcessed + " | DuracionMs=" + ocr.DurationMilliseconds + " | TextoCaracteres=" + (ocr.Text ?? string.Empty).Length);
            if (!ocr.Success)
            {
                Logs.LogProc("DocumentAnalysis | SegundoPaseEncabezado=No | Tipo=" + type);
                if (ocr.SystemFailure) Logs.LogError("DocumentAnalysis | Operación=OCR | Error=" + (ocr.FailureReason ?? "Fallo estructural del motor OCR."));
                return InvoiceSelector.Review("OCR_ERROR", ocr.FailureReason ?? "El OCR no pudo procesar el documento.", null);
            }
            var selection = InvoiceSelector.SelectOcrText(ocr.Text, ocr.HasUsefulText);
            if (!string.Equals(selection.Classification, "REVISAR", StringComparison.Ordinal))
            {
                Logs.LogProc("DocumentAnalysis | SegundoPaseEncabezado=No | Tipo=" + type);
                return selection;
            }
            var header = recognizeHeader();
            Logs.LogProc("DocumentAnalysis | SegundoPaseEncabezado=Sí | Tipo=" + type + " | Imagenes=" + header.ImagesProcessed + " | DuracionMs=" + header.DurationMilliseconds + " | TextoCaracteres=" + (header.Text ?? string.Empty).Length);
            if (!header.Success)
            {
                if (header.SystemFailure) Logs.LogError("DocumentAnalysis | Operación=OCR_Encabezado | Error=" + (header.FailureReason ?? "Fallo estructural del motor OCR."));
                return selection;
            }
            var combined = DocumentOcrService.Combine(ocr, header);
            return InvoiceSelector.SelectOcrText(combined.Text, combined.HasUsefulText);
        }

        private static void AddUnanalyzableContainer(string path, string name, string mime, string chain, bool root, string reason, AttachmentAnalysis result)
        {
            result.Candidates.Add(new DocumentCandidate { SourcePath = path, OriginalName = SafeOriginalName(name), MimeType = mime ?? GuessMime(name) ?? "application/octet-stream", OriginType = root ? "DIRECTO" : "ZIP", InternalContainerPath = root ? null : chain, OriginHash = root ? HashFile(path) : StableZipOriginHash(chain, path), SizeBytes = new FileInfo(path).Length, Selection = InvoiceSelector.Review("ZIP_NO_ANALIZABLE", "Contenedor no analizable: " + SafeReason(reason), null) });
            Logs.LogProc("DocumentAnalysis | Documento clasificado | Clasificacion=REVISAR | Metodo=ZIP_NO_ANALIZABLE");
        }

        private static bool IsContainer(string name, string mime, byte[] bytes)
        {
            var extension = Path.GetExtension(name ?? string.Empty);
            if (ContainerExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return true;
            if (PackagedDocumentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return false;
            if (new[] { "application/zip", "application/x-zip-compressed", "application/vnd.rar", "application/x-rar-compressed", "application/x-7z-compressed" }.Contains(mime, StringComparer.OrdinalIgnoreCase)) return true;
            return HasZipSignature(bytes) || HasRarSignature(bytes) || HasSevenZipSignature(bytes);
        }
        private static bool IsContainerFile(string path, string name)
        {
            if (IsContainer(name, null, null)) return true;
            using (var stream = File.OpenRead(path)) { var header = new byte[8]; var count = stream.Read(header, 0, header.Length); return IsContainer(name, null, header.Take(count).ToArray()); }
        }
        private static bool HasZipSignature(byte[] value) { return value != null && value.Length >= 4 && value[0] == 0x50 && value[1] == 0x4b && (value[2] == 3 || value[2] == 5 || value[2] == 7) && (value[3] == 4 || value[3] == 6 || value[3] == 8); }
        private static bool HasRarSignature(byte[] value) { return value != null && value.Length >= 7 && value[0] == 0x52 && value[1] == 0x61 && value[2] == 0x72 && value[3] == 0x21 && value[4] == 0x1a && value[5] == 0x07 && (value[6] == 0x00 || (value[6] == 0x01 && value.Length >= 8 && value[7] == 0x00)); }
        private static bool HasSevenZipSignature(byte[] value) { return value != null && value.Length >= 6 && value[0] == 0x37 && value[1] == 0x7a && value[2] == 0xbc && value[3] == 0xaf && value[4] == 0x27 && value[5] == 0x1c; }
        private static bool IsContainerException(Exception ex) { return ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is NotSupportedException || (ex.GetType().Namespace ?? string.Empty).StartsWith("SharpCompress", StringComparison.Ordinal); }
        private static string GuessMime(string name)
        {
            var extension = Path.GetExtension(name ?? string.Empty);
            if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)) return "application/pdf";
            if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)) return "application/zip";
            if (string.Equals(extension, ".rar", StringComparison.OrdinalIgnoreCase)) return "application/vnd.rar";
            if (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)) return "image/jpeg";
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)) return "image/png";
            if (string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase)) return "image/bmp";
            if (string.Equals(extension, ".tif", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".tiff", StringComparison.OrdinalIgnoreCase)) return "image/tiff";
            return string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase) ? "application/x-7z-compressed" : null;
        }
        private static bool IsImage(string name) { return ImageExtensions.Contains(Path.GetExtension(name ?? string.Empty), StringComparer.OrdinalIgnoreCase); }
        private static string StableZipOriginHash(string chain, string path) { return HashBytes(Encoding.UTF8.GetBytes((chain ?? string.Empty) + "|" + HashFile(path))); }
        internal static string HashFile(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(stream)); }
        private static string HashBytes(byte[] bytes) { using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(bytes)); }
        private static string ToHex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant(); }
        private static string SafeReason(string value) { var safe = (value ?? "Error de contenedor").Replace('\r', ' ').Replace('\n', ' '); return safe.Length > 300 ? safe.Substring(0, 300) : safe; }
        private static string SafeOriginalName(string value) { var name = string.IsNullOrWhiteSpace(value) ? "documento" : value; return name.Length > 500 ? name.Substring(0, 500) : name; }
    }
}
