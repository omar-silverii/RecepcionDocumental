using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using PDFtoImage;

namespace PdfRasterProbe
{
    internal static class H1D9BVisualAssetProbe
    {
        private const string DatasetHash = "AFECA7A2F995CE1B2DF6F9DAF3501A392CC7FB4DD1509900A19D1588E26794C2";
        private const string FrozenHash = "FADEA71A298125E8CE0EB65C31F6232EAAE72EB71F33141B912D23F4E59603E4";
        private const int PdfDpi = 300;

        internal static int Run(string[] args)
        {
            if (args.Length != 4)
            {
                Console.Error.WriteLine("Uso: --h1d9b-export-visual-assets <dataset.csv> <frozen-test-groups.txt> <output>");
                return 2;
            }
            try
            {
                var dataset = Path.GetFullPath(args[1]);
                var frozen = Path.GetFullPath(args[2]);
                var output = Path.GetFullPath(args[3]);
                ValidateHash(dataset, DatasetHash);
                ValidateHash(frozen, FrozenHash);
                if (Directory.Exists(output))
                    throw new IOException("La salida H1D9B ya existe: " + output);

                var frozenGroups = new HashSet<string>(File.ReadAllLines(frozen).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()), StringComparer.Ordinal);
                var all = Load(dataset);
                var development = all.Where(x => !frozenGroups.Contains(x.GroupId)).ToList();
                var test = all.Where(x => frozenGroups.Contains(x.GroupId)).ToList();
                ValidateUniverse(all, development, test, frozenGroups);

                var assets = Path.Combine(output, "assets");
                Directory.CreateDirectory(assets);
                var manifest = new List<string> { "Sha256,GroupId,LabelOriginal,LabelBinario,SourceType,CorpusPath,VisualAssetPath,Width,Height,Method,Dpi" };
                foreach (var row in development)
                {
                    var target = Path.Combine(assets, row.Sha256 + ".png");
                    int width;
                    int height;
                    string method;
                    int? dpi;
                    if (string.Equals(row.SourceType, "PDF", StringComparison.OrdinalIgnoreCase))
                    {
                        Conversion.SavePng(target, File.ReadAllBytes(row.Path), 0, options: new RenderOptions { Dpi = PdfDpi });
                        using (var image = Image.FromFile(target)) { width = image.Width; height = image.Height; }
                        method = "PDFTOIMAGE_FIRST_PAGE_300_DPI_NO_CROP";
                        dpi = PdfDpi;
                    }
                    else
                    {
                        using (var image = Image.FromFile(row.Path))
                        {
                            ApplyExifOrientation(image);
                            width = image.Width;
                            height = image.Height;
                            image.Save(target, ImageFormat.Png);
                        }
                        method = "SYSTEM_DRAWING_EXIF_ORIENTED_PNG_NO_CROP";
                        dpi = null;
                    }
                    ValidatePng(target, width, height);
                    manifest.Add(Join(row.Sha256, row.GroupId, row.Label, row.Label == "FACTURA" ? "FACTURA" : "NO_FACTURA", row.SourceType,
                        row.Path, target, width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture), method,
                        dpi.HasValue ? dpi.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
                    Console.WriteLine("H1D9B_ASSET | " + manifest.Count.ToString(CultureInfo.InvariantCulture) + "/71 | Sha256=" + row.Sha256 + " | " + width + "x" + height + " | " + method);
                }
                if (manifest.Count != 71 || Directory.GetFiles(assets, "*.png").Length != 70)
                    throw new InvalidDataException("Gate de assets H1D9B falló: se esperaban exactamente 70 assets.");
                File.WriteAllLines(Path.Combine(output, "asset-manifest.csv"), manifest, new UTF8Encoding(false));
                Console.WriteLine("H1D9B_ASSETS | Gate=PASS | Development=70 | TestExcluido=10 | Assets=70 | Output=" + output);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("H1D9B_ASSETS | Gate=FAIL");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static void ValidateUniverse(List<Row> all, List<Row> development, List<Row> test, HashSet<string> frozenGroups)
        {
            if (all.Count != 80 || all.Select(x => x.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 80 || all.Select(x => x.GroupId).Distinct().Count() != 54)
                throw new InvalidDataException("El universo autoritativo no coincide con 80 archivos/80 hashes/54 grupos.");
            if (frozenGroups.Count != 5 || test.Count != 10 || test.Select(x => x.GroupId).Distinct().Count() != 5)
                throw new InvalidDataException("El TEST congelado no coincide con 10 archivos/5 grupos.");
            if (development.Count != 70 || development.Select(x => x.GroupId).Distinct().Count() != 49)
                throw new InvalidDataException("Desarrollo no coincide con 70 archivos/49 grupos.");
            if (development.Count(x => x.Label == "FACTURA") != 20 || development.Count(x => x.Label != "FACTURA") != 50)
                throw new InvalidDataException("La composición binaria de desarrollo no coincide con 20/50.");
            if (development.Select(x => x.GroupId).Intersect(frozenGroups).Any())
                throw new InvalidDataException("Se detectó leakage de TEST congelado en desarrollo.");
        }

        private static void ApplyExifOrientation(Image image)
        {
            const int orientationId = 0x0112;
            if (!image.PropertyIdList.Contains(orientationId)) return;
            var value = image.GetPropertyItem(orientationId).Value[0];
            switch (value)
            {
                case 2: image.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                case 3: image.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                case 4: image.RotateFlip(RotateFlipType.Rotate180FlipX); break;
                case 5: image.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                case 6: image.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                case 7: image.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                case 8: image.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            }
        }

        private static void ValidatePng(string path, int expectedWidth, int expectedHeight)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0) throw new InvalidDataException("Asset vacío o ausente: " + path);
            using (var image = Image.FromFile(path))
                if (image.RawFormat.Guid != ImageFormat.Png.Guid || image.Width != expectedWidth || image.Height != expectedHeight || image.Width <= 0 || image.Height <= 0)
                    throw new InvalidDataException("Asset PNG inválido: " + path);
        }

        private static List<Row> Load(string path)
        {
            var rows = new List<Row>();
            var root = Path.GetDirectoryName(path);
            using (var parser = new TextFieldParser(path, Encoding.UTF8))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.HasFieldsEnclosedInQuotes = true;
                parser.SetDelimiters(",");
                var headers = parser.ReadFields();
                var columns = headers.Select((name, index) => new { name, index }).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
                while (!parser.EndOfData)
                {
                    var fields = parser.ReadFields();
                    if (fields == null) continue;
                    Func<string, string> get = name => columns.ContainsKey(name) && columns[name] < fields.Length ? fields[columns[name]] : string.Empty;
                    var storedPath = get("Path");
                    var resolved = Path.IsPathRooted(storedPath) ? storedPath : Path.GetFullPath(Path.Combine(root, storedPath));
                    if (!File.Exists(resolved)) throw new FileNotFoundException("Falta un archivo del corpus.", resolved);
                    rows.Add(new Row { Path = resolved, Label = get("Label"), GroupId = get("GroupId"), SourceType = get("SourceType"), Sha256 = get("Sha256") });
                }
            }
            return rows;
        }

        private static void ValidateHash(string path, string expected)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var actual = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    throw new InvalidDataException("SHA-256 inesperado para " + path + ": " + actual);
            }
        }

        private static string Join(params string[] values) { return string.Join(",", values.Select(Csv)); }
        private static string Csv(string value) { return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\""; }
        private sealed class Row { internal string Path, Label, GroupId, SourceType, Sha256; }
    }
}
