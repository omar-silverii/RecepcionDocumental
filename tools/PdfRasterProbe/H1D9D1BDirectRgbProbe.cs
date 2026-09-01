using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace PdfRasterProbe
{
    internal static class H1D9D1BDirectRgbProbe
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        internal static int Run(string[] args)
        {
            if (args.Length != 5) { Console.Error.WriteLine("Uso: --h1d9d1b-direct-rgb <python-reference.csv> <development-assets.csv> <holdout-assets.csv> <output>"); return 2; }
            try { return Execute(args); }
            catch (Exception ex) { Console.Error.WriteLine("H1D9D1B | Resultado=H1D9D1B NO APROBADO\n" + ex); return 1; }
        }

        private static int Execute(string[] args)
        {
            var references = Read(args[1]).ToDictionary(r => r["Sha256"], StringComparer.OrdinalIgnoreCase);
            var assets = LoadAssets(args[2], "DEVELOPMENT").Concat(LoadAssets(args[3], "HOLDOUT")).ToDictionary(x => x.Sha, StringComparer.OrdinalIgnoreCase);
            var output = Path.GetFullPath(args[4]); var temporary = Path.Combine(output, "python-source-rgb.tmp");
            if (references.Count != 80 || assets.Count != 80 || !Directory.Exists(temporary)) throw new InvalidDataException("Universo o referencia temporal incompletos.");
            var results = new List<Result>();
            foreach (var pair in references)
            {
                var reference = pair.Value; var asset = assets[pair.Key];
                var watch = Stopwatch.StartNew(); Audit audit; var rgb = DecodeRgbDirect(asset.Path, out audit); watch.Stop();
                var expectedHash = reference["SourceRgbSha256"]; var actualHash = Hash(rgb);
                var widthEqual = audit.Width == I(reference["Width"]); var heightEqual = audit.Height == I(reference["Height"]); var hashEqual = actualHash == expectedHash;
                var result = new Result { Sha = pair.Key, Cohort = asset.Cohort, PillowMode = reference["PillowMode"], HasAlpha = reference["HasAlpha"], HasIcc = reference["HasIcc"], HasGamma = reference["HasGamma"],
                    ExpectedHash = expectedHash, ActualHash = actualHash, WidthEqual = widthEqual, HeightEqual = heightEqual, HashEqual = hashEqual, Audit = audit, Milliseconds = watch.Elapsed.TotalMilliseconds };
                if (!hashEqual)
                {
                    var expected = ReadGzip(Path.Combine(temporary, pair.Key + ".rgb.gz")); result.Diff = Compare(expected, rgb);
                }
                results.Add(result);
            }
            Directory.Delete(temporary, true);
            WriteArtifacts(output, references, results);
            var equal = results.Count(x => x.HashEqual && x.WidthEqual && x.HeightEqual); var approved = equal == 80;
            Console.WriteLine("H1D9D1B | Resultado=" + (approved ? "H1D9D1B APROBADO" : "H1D9D1B NO APROBADO") + " | Source=" + equal + "/80 | Width=" + results.Count(x => x.WidthEqual) + "/80 | Height=" + results.Count(x => x.HeightEqual) + "/80");
            return approved ? 0 : 1;
        }

        private static byte[] DecodeRgbDirect(string path, out Audit audit)
        {
            using (var bitmap = new Bitmap(path, false))
            {
                var format = bitmap.PixelFormat; var bpp = Image.GetPixelFormatSize(format);
                audit = new Audit { RawFormat = bitmap.RawFormat.Guid == ImageFormat.Png.Guid ? "PNG" : bitmap.RawFormat.ToString(), PixelFormat = format.ToString(), BitsPerPixel = bpp,
                    HasAlpha = Image.IsAlphaPixelFormat(format), IsExtended = Image.IsExtendedPixelFormat(format), IsCanonical = Image.IsCanonicalPixelFormat(format), Width = bitmap.Width, Height = bitmap.Height };
                if (format == PixelFormat.Format32bppPArgb) throw new InvalidDataException("Formato premultiplicado no recuperable sin aproximación: " + path);
                if (format != PixelFormat.Format24bppRgb && format != PixelFormat.Format32bppArgb && format != PixelFormat.Format32bppRgb &&
                    format != PixelFormat.Format1bppIndexed && format != PixelFormat.Format4bppIndexed && format != PixelFormat.Format8bppIndexed)
                    throw new InvalidDataException("PixelFormat no contemplado: " + format + " en " + path);
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, format);
                try
                {
                    var stride = Math.Abs(data.Stride); var raw = new byte[stride * bitmap.Height]; Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                    var rgb = new byte[bitmap.Width * bitmap.Height * 3]; var palette = (format & PixelFormat.Indexed) != 0 ? bitmap.Palette.Entries : null;
                    for (var y = 0; y < bitmap.Height; y++)
                    {
                        var row = data.Stride >= 0 ? y * stride : (bitmap.Height - 1 - y) * stride;
                        for (var x = 0; x < bitmap.Width; x++)
                        {
                            var d = (y * bitmap.Width + x) * 3;
                            if (format == PixelFormat.Format24bppRgb) { var s = row + x * 3; rgb[d] = raw[s + 2]; rgb[d + 1] = raw[s + 1]; rgb[d + 2] = raw[s]; }
                            else if (format == PixelFormat.Format32bppArgb || format == PixelFormat.Format32bppRgb) { var s = row + x * 4; rgb[d] = raw[s + 2]; rgb[d + 1] = raw[s + 1]; rgb[d + 2] = raw[s]; }
                            else { var index = format == PixelFormat.Format8bppIndexed ? raw[row + x] : format == PixelFormat.Format4bppIndexed ? ((x & 1) == 0 ? raw[row + x / 2] >> 4 : raw[row + x / 2] & 15) : (raw[row + x / 8] >> (7 - (x & 7))) & 1; var color = palette[index]; rgb[d] = color.R; rgb[d + 1] = color.G; rgb[d + 2] = color.B; }
                        }
                    }
                    return rgb;
                }
                finally { bitmap.UnlockBits(data); }
            }
        }

        private static void WriteArtifacts(string output, Dictionary<string, Dictionary<string, string>> references, List<Result> results)
        {
            var formats = results.GroupBy(x => x.Audit.PixelFormat).OrderBy(x => x.Key).ToList(); var mismatches = results.Where(x => !x.HashEqual || !x.WidthEqual || !x.HeightEqual).ToList();
            Write(Path.Combine(output, "csharp-pixel-formats.csv"), new[] { "Sha256,Cohort,RawFormat,PixelFormat,BitsPerPixel,IsAlphaPixelFormat,IsExtendedPixelFormat,IsCanonicalPixelFormat,Width,Height" }.Concat(results.Select(x => string.Join(",", x.Sha,x.Cohort,x.Audit.RawFormat,x.Audit.PixelFormat,x.Audit.BitsPerPixel,B(x.Audit.HasAlpha),B(x.Audit.IsExtended),B(x.Audit.IsCanonical),x.Audit.Width,x.Audit.Height))));
            Write(Path.Combine(output, "source-rgb-parity.csv"), new[] { "Sha256,Cohort,PillowMode,CSharpPixelFormat,WidthEqual,HeightEqual,PythonSourceRgbSha256,CSharpSourceRgbSha256,Equal,DifferentBytes,MaeRgb,MaxChannelDelta,FirstDifferentByte,PixelIndex,Channel,PythonValue,CSharpValue,DecodeMs" }.Concat(results.Select(x => string.Join(",",x.Sha,x.Cohort,x.PillowMode,x.Audit.PixelFormat,B(x.WidthEqual),B(x.HeightEqual),x.ExpectedHash,x.ActualHash,B(x.HashEqual),x.Diff==null?0:x.Diff.Count,F(x.Diff==null?0:x.Diff.Mae),x.Diff==null?0:x.Diff.Max,x.Diff==null?-1:x.Diff.First,x.Diff==null?-1:x.Diff.First/3,x.Diff==null?"":new[]{"R","G","B"}[x.Diff.First%3],x.Diff==null?"":x.Diff.Expected.ToString(),x.Diff==null?"":x.Diff.Actual.ToString(),F(x.Milliseconds)))));
            Write(Path.Combine(output, "source-rgb-mismatch-summary.csv"), new[] { "PillowMode,CSharpPixelFormat,HasAlpha,HasIcc,HasGamma,Count" }.Concat(mismatches.GroupBy(x => string.Join("|",x.PillowMode,x.Audit.PixelFormat,x.HasAlpha,x.HasIcc,x.HasGamma)).Select(g => { var x=g.First(); return string.Join(",",x.PillowMode,x.Audit.PixelFormat,x.HasAlpha,x.HasIcc,x.HasGamma,g.Count()); })));
            var times=results.Select(x=>x.Milliseconds).OrderBy(x=>x).ToList(); var equal=results.Count(x=>x.HashEqual&&x.WidthEqual&&x.HeightEqual); var approved=equal==80;
            var formatText=string.Join(", ",formats.Select(g=>g.Key+"="+g.Count())); var modeText=string.Join(", ",results.GroupBy(x=>x.PillowMode).Select(g=>g.Key+"="+g.Count()));
            var metrics="# H1D9D1B — Métricas\n\n- PillowMode: "+modeText+".\n- C# PixelFormat: "+formatText+".\n- Source RGB iguales: "+equal+"/80.\n- Width iguales: "+results.Count(x=>x.WidthEqual)+"/80; Height iguales: "+results.Count(x=>x.HeightEqual)+"/80.\n- Decode directo ms: media "+F(times.Average())+", P50 "+F(Pct(times,.5))+", P95 "+F(Pct(times,.95))+", máximo "+F(times.Last())+".\n- Graphics usado: false. Resize/tensor/ONNX final ejecutados: false.\n";
            Write(Path.Combine(output,"parity-metrics.md"),metrics); Write(Path.Combine(output,"resumen.md"),"# H1D9D1B — Resultado\n\n`H1D9D1B "+(approved?"APROBADO":"NO APROBADO")+"`\n\n"+metrics.Substring(metrics.IndexOf("- Pillow"))+"\nNo hubo entrenamiento, tuning ni cambios de producto.\n");
            Write(Path.Combine(output,"parity-manifest.json"),"{\n  \"milestone\": \"H1D9D1B\",\n  \"status\": \""+(approved?"APROBADO":"NO_APROBADO")+"\",\n  \"source_total\": 80,\n  \"source_equal\": "+equal+",\n  \"format_counts\": {"+string.Join(", ",formats.Select(g=>"\""+g.Key+"\": "+g.Count()))+"},\n  \"graphics_used\": false,\n  \"resize_executed\": false,\n  \"tensor_executed\": false,\n  \"final_onnx_executed\": false,\n  \"temporary_rgb_references_deleted\": true,\n  \"training_performed\": false,\n  \"threshold_tuning_performed\": false,\n  \"product_modified\": false\n}\n");
        }

        private static Diff Compare(byte[] expected, byte[] actual) { long count=0,sum=0;var max=0;var first=-1;byte ev=0,av=0;for(var i=0;i<expected.Length;i++){var d=Math.Abs(expected[i]-actual[i]);if(d>0){count++;if(first<0){first=i;ev=expected[i];av=actual[i];}}sum+=d;max=Math.Max(max,d);}return new Diff{Count=count,Mae=(double)sum/expected.Length,Max=max,First=first,Expected=ev,Actual=av}; }
        private static byte[] ReadGzip(string path){using(var f=File.OpenRead(path))using(var z=new GZipStream(f,CompressionMode.Decompress))using(var m=new MemoryStream()){z.CopyTo(m);return m.ToArray();}}
        private static IEnumerable<Asset> LoadAssets(string p,string c){return Read(p).Select(r=>new Asset{Sha=r["Sha256"],Cohort=c,Path=r["VisualAssetPath"]});}
        private static List<Dictionary<string,string>> Read(string p){var rows=new List<Dictionary<string,string>>();using(var q=new TextFieldParser(p,Encoding.UTF8)){q.SetDelimiters(",");q.HasFieldsEnclosedInQuotes=true;var h=q.ReadFields();while(!q.EndOfData){var f=q.ReadFields();var r=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);for(var i=0;i<h.Length;i++)r[h[i]]=f[i];rows.Add(r);}}return rows;}
        private static string Hash(byte[] x){using(var a=SHA256.Create())return BitConverter.ToString(a.ComputeHash(x)).Replace("-","");}private static void Write(string p,IEnumerable<string>x){File.WriteAllText(p,string.Join("\n",x)+"\n",Utf8);}private static void Write(string p,string x){File.WriteAllText(p,x.Replace("\r\n","\n"),Utf8);}private static int I(string x){return int.Parse(x,CultureInfo.InvariantCulture);}private static string B(bool x){return x?"true":"false";}private static string F(double x){return x.ToString("0.######",CultureInfo.InvariantCulture);}private static double Pct(IReadOnlyList<double>x,double p){var r=(x.Count-1)*p;var l=(int)Math.Floor(r);var u=(int)Math.Ceiling(r);return x[l]+(x[u]-x[l])*(r-l);}
        private sealed class Asset{public string Sha,Cohort,Path;}private sealed class Audit{public string RawFormat,PixelFormat;public int BitsPerPixel,Width,Height;public bool HasAlpha,IsExtended,IsCanonical;}private sealed class Diff{public long Count;public double Mae;public int Max,First;public byte Expected,Actual;}private sealed class Result{public string Sha,Cohort,PillowMode,HasAlpha,HasIcc,HasGamma,ExpectedHash,ActualHash;public bool WidthEqual,HeightEqual,HashEqual;public Audit Audit;public Diff Diff;public double Milliseconds;}
    }
}
