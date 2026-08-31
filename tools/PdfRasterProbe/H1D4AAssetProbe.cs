using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using PDFtoImage;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    public static class H1D4AAssetProbe
    {
        public static int Run(string[] args)
        {
            if (args.Length != 3) { Console.Error.WriteLine("Uso: --h1d4a-assets <dataset.csv> <output>"); return 2; }
            try
            {
                var dataset = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
                if (Directory.Exists(output)) throw new IOException("La salida de assets ya existe.");
                Directory.CreateDirectory(output); var visual = Path.Combine(output, "visual"); Directory.CreateDirectory(visual);
                var rows = Load(dataset); var lines = new List<string> { "Path,Label,GroupId,SourceType,Sha256,OriginalPath,Diversity,VisualPath,VisualFeatures,TextBase64,TextOrigin,TextLen,PhysicalFormat" };
                var direct = 0; var ocr = 0; var noText = 0;
                foreach (var r in rows)
                {
                    var bytes = File.ReadAllBytes(r.Path); var format = Format(bytes); var target = Path.Combine(visual, r.Sha256 + ".png");
                    string rendered = null; var temp = Path.Combine(Path.GetTempPath(), "H1D4A-" + Guid.NewGuid().ToString("N") + ".png");
                    try
                    {
                        if (format == "PDF") { Conversion.SavePng(temp, bytes, 0, options: new RenderOptions { Dpi = 150 }); rendered = temp; }
                        else rendered = r.Path;
                        var features = Normalize(rendered, target);
                        var text = ""; var origin = "NONE";
                        if (format == "PDF") { var m = MdocPdfTextExtractor.Extract(r.Path); if (m.HasUsefulText) { text = m.Text ?? ""; origin = "MDOC"; direct++; } }
                        if (text.Length == 0) { var result = DocumentOcrService.RecognizeImageFile(rendered); if (result.Success && !string.IsNullOrWhiteSpace(result.Text)) { text = result.Text; origin = "OCR"; ocr++; } else noText++; }
                        lines.Add(string.Join(",", new[] { r.Path, r.Label, r.GroupId, r.SourceType, r.Sha256, r.OriginalPath, r.Diversity, target, string.Join(";", features.Select(x => x.ToString("R", CultureInfo.InvariantCulture))), Convert.ToBase64String(Encoding.UTF8.GetBytes(text)), origin, text.Length.ToString(CultureInfo.InvariantCulture), format }.Select(Csv)));
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                File.WriteAllLines(Path.Combine(output, "assets.csv"), lines, new UTF8Encoding(false));
                Console.WriteLine("H1D4A_ASSETS | Filas=" + rows.Count + " | TextoMdoc=" + direct + " | TextoOCR=" + ocr + " | SinTexto=" + noText + " | Output=" + output);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("ERROR | " + ex.GetType().Name + ": " + ex.Message); return 1; }
        }
        private static float[] Normalize(string source, string target) { using (var input = Image.FromFile(source)) using (var bitmap = new Bitmap(96, 96, PixelFormat.Format24bppRgb)) using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.White); g.InterpolationMode = InterpolationMode.HighQualityBicubic; var scale = Math.Min(96d / input.Width, 96d / input.Height); var w = Math.Max(1, (int)(input.Width * scale)); var h = Math.Max(1, (int)(input.Height * scale)); g.DrawImage(input, (96 - w) / 2, (96 - h) / 2, w, h); bitmap.Save(target, ImageFormat.Png); var f = new float[64]; for (var y = 0; y < 96; y++) for (var x = 0; x < 96; x++) { var c = bitmap.GetPixel(x, y); f[c.R / 16]++; f[16 + c.G / 16]++; f[32 + c.B / 16]++; f[48 + ((c.R + c.G + c.B) / 3) / 16]++; } for (var n = 0; n < f.Length; n++) f[n] /= 9216f; return f; } }
        private static string Format(byte[] b) { if (b.Length >= 5 && Encoding.ASCII.GetString(b, 0, 5) == "%PDF-") return "PDF"; if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4e && b[3] == 0x47) return "PNG"; if (b.Length >= 3 && b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff) return "JPEG"; throw new InvalidDataException("Formato no soportado: " + Path.GetFileName(b.ToString())); }
        private static List<Row> Load(string path) { var result = new List<Row>(); var baseDir = Path.GetDirectoryName(path); using (var p = new TextFieldParser(path)) { p.TextFieldType = FieldType.Delimited; p.HasFieldsEnclosedInQuotes = true; p.SetDelimiters(","); var h = p.ReadFields(); var c = h.Select((n, i) => new { n, i }).ToDictionary(x => x.n, x => x.i, StringComparer.OrdinalIgnoreCase); while (!p.EndOfData) { var f = p.ReadFields(); if (f == null) continue; Func<string, string> v = n => c.ContainsKey(n) && c[n] < f.Length ? f[c[n]] : ""; var stored = v("Path"); result.Add(new Row(Path.IsPathRooted(stored) ? stored : Path.GetFullPath(Path.Combine(baseDir, stored)), v("Label"), v("GroupId"), v("SourceType"), v("Sha256"), v("OriginalPath"), v("Diversity"))); } } return result; }
        private static string Csv(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }
        private sealed class Row { internal Row(string p, string l, string g, string t, string h, string o, string d) { Path = p; Label = l; GroupId = g; SourceType = t; Sha256 = h; OriginalPath = o; Diversity = d; } internal string Path, Label, GroupId, SourceType, Sha256, OriginalPath, Diversity; }
    }
}
