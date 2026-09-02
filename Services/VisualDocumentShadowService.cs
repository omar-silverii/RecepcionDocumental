using System;
using System.IO;
using System.Linq;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;

namespace RecepcionDocumental.Services
{
    public sealed class VisualDocumentShadowEvaluation
    {
        public VisualShadowResult Result { get; set; }
        public int RasterizerCalls { get; set; }
        public int PagesRendered { get; set; }
        public bool FirstPageReused { get; set; }
    }

    // One visual path for newly retained Gmail documents and existing-document backfill.
    // Eligibility/classification is owned by the caller; this service never changes it.
    public static class VisualDocumentShadowService
    {
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

        public static bool IsImage(string name)
        {
            return ImageExtensions.Contains(Path.GetExtension(name ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        }

        public static VisualDocumentShadowEvaluation Evaluate(string path, string name, AttachmentWorkspace workspace,
            OcrImageData visualRaster = null, bool firstPageRenderedByOcr = false, string firstPageVisualFailureReason = null)
        {
            var evaluation = new VisualDocumentShadowEvaluation();
            if (!ConfiguracionSistema.Actual.VisionShadowEnabled) return evaluation;
            var shadow = VisualInvoiceShadowService.CreateVersionErrorIfUnsupported("MODEL_VERSION_VALIDATION");
            if (shadow == null && string.Equals(Path.GetExtension(name), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                if (visualRaster != null)
                {
                    evaluation.FirstPageReused = true;
                    shadow = VisualInvoiceShadowService.EvaluateCanonicalPng(visualRaster.Bytes, "PDF_OCR_RASTER_REUSED", true);
                }
                else if (firstPageRenderedByOcr)
                    shadow = VisualInvoiceShadowService.CreateRasterError("PDF_OCR_RASTER_NOT_REUSABLE", firstPageVisualFailureReason);
                else
                {
                    var first = PdfPageRasterizer.RasterizeFirstPage(path, workspace);
                    evaluation.RasterizerCalls = 1;
                    evaluation.PagesRendered = first.PagesRendered;
                    shadow = first.Images.Count == 0
                        ? VisualInvoiceShadowService.CreateRasterError("PDF_SHADOW_FIRST_PAGE", first.FailureReason)
                        : VisualInvoiceShadowService.EvaluateCanonicalPng(first.Images[0].Bytes, "PDF_SHADOW_FIRST_PAGE", false);
                }
            }
            else if (shadow == null && IsImage(name)) shadow = VisualInvoiceShadowService.EvaluateImageFile(path);
            else if (shadow == null) shadow = VisualInvoiceShadowService.CreateUnsupportedError();
            evaluation.Result = shadow;
            if (shadow.Status == "OK")
                Logs.LogProc("VisualShadow | DocumentoConservado=true | Estado=OK | Modelo=" + shadow.ModelVersion + " | Zona=" + shadow.Zone + " | PFactura=" + shadow.PFactura.Value.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture) + " | RasterReutilizado=" + shadow.RasterReused + " | TotalMs=" + shadow.TotalMilliseconds);
            else Logs.LogError("VisualShadow | Estado=ERROR | Codigo=" + Logs.SanitizarMensaje(shadow.ErrorCode));
            return evaluation;
        }
    }
}
