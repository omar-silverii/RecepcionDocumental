using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.VisualBasic.FileIO;

namespace PdfRasterProbe
{
    internal static class H1D9D1PillowParityProbe
    {
        private const int Size = 224, Channels = 3, Precision = 22;
        private const double TNo = 0.1169927716255188, TYes = 0.7979831695556641;
        private static readonly float[] Mean = { .485f, .456f, .406f }, Std = { .229f, .224f, .225f };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        internal static int Run(string[] args)
        {
            if (args.Length != 7) { Console.Error.WriteLine("Uso: --h1d9d1-pillow-parity <onnx> <python-reference.csv> <development-assets.csv> <holdout-assets.csv> <holdout-predictions.csv> <output>"); return 2; }
            try { return Execute(args); }
            catch (Exception ex) { Console.Error.WriteLine("H1D9D1 | Resultado=H1D9D1 NO APROBADO\n" + ex); return 1; }
        }

        private static int Execute(string[] args)
        {
            if (!Environment.Is64BitProcess) throw new InvalidOperationException("Proceso no x64.");
            var refs = Read(args[2]).Select((r, i) => new Ref { Index = i, Sha = r["Sha256"], Cohort = r["Cohort"], Group = r["GroupId"],
                W = I(r["SourceWidth"]), H = I(r["SourceHeight"]), SourceHash = r["SourceRgbSha256"], TargetHash = r["TargetRgbSha256"], TensorHash = r["TensorSha256"],
                P = D(r["PFactura"]), Pred = r["Pred050"], Zone = r["Zona"] }).ToList();
            var assets = LoadAssets(args[3], "DEVELOPMENT").Concat(LoadAssets(args[4], "HOLDOUT")).ToDictionary(x => x.Sha, StringComparer.OrdinalIgnoreCase);
            var prior = Read(args[5]).ToDictionary(x => x["Sha256"], x => x["Pred050"], StringComparer.OrdinalIgnoreCase);
            var output = Path.GetFullPath(args[6]); Directory.CreateDirectory(output);
            var targetBin = Path.Combine(output, "target-rgb.tmp.bin"); var tensorBin = Path.Combine(output, "tensor.tmp.bin");
            if (refs.Count != 80 || assets.Count != 80 || !File.Exists(targetBin) || !File.Exists(tensorBin)) throw new InvalidDataException("Universo o temporales incompletos.");
            var sourceRows = new List<string> { "Sha256,Cohort,PythonSourceRgbSha256,CSharpSourceRgbSha256,Equal,SourceWidth,SourceHeight,PixelCountEqual,FirstDifferentByte" };
            var decoded = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var sourceEqual = 0;
            foreach (var r in refs)
            {
                int w, h; var rgb = DecodeRgb(assets[r.Sha].Path, out w, out h); decoded[r.Sha] = rgb;
                var hash = Hash(rgb); var equal = hash == r.SourceHash && w == r.W && h == r.H;
                if (equal) sourceEqual++;
                sourceRows.Add(string.Join(",", r.Sha, r.Cohort, r.SourceHash, hash, B(equal), w, h, B(w * h == r.W * r.H), equal ? "-1" : "not_available_without_large_python_source_dump"));
            }
            Write(Path.Combine(output, "source-rgb-parity.csv"), sourceRows);
            if (sourceEqual != 80) { WriteFailureArtifacts(output, sourceEqual); DeleteTemp(targetBin, tensorBin); return 1; }

            var tensorBytes = File.ReadAllBytes(tensorBin); var pythonOnnx = new Dictionary<string, float>();
            using (var session = new InferenceSession(Path.GetFullPath(args[1]), new SessionOptions()))
            {
                for (var i = 0; i < refs.Count; i++)
                {
                    var tensor = Floats(tensorBytes, i * Size * Size * Channels * 4, Size * Size * Channels);
                    pythonOnnx[refs[i].Sha] = Infer(session, tensor);
                }
            }
            var pythonTensorMax = refs.Max(r => Math.Abs(pythonOnnx[r.Sha] - r.P));
            var pythonTensorPred = refs.Count(r => Pred(pythonOnnx[r.Sha]) == r.Pred);
            var pythonTensorZone = refs.Count(r => Zone(pythonOnnx[r.Sha]) == r.Zone);
            if (pythonTensorPred != 80 || pythonTensorZone != 80 || pythonTensorMax > .00001) { WriteFailure(output, "ONNX_PYTHON_TENSOR", sourceEqual); DeleteTemp(targetBin, tensorBin); return 1; }

            var targetBytes = File.ReadAllBytes(targetBin); var runs = new List<Sample>();
            using (var session = new InferenceSession(Path.GetFullPath(args[1]), new SessionOptions()))
            {
                foreach (var r in refs)
                {
                    var total = Stopwatch.StartNew();
                    var resizeWatch = Stopwatch.StartNew(); var target = Letterbox(decoded[r.Sha], r.W, r.H); resizeWatch.Stop();
                    var expected = Slice(targetBytes, r.Index * Size * Size * Channels, Size * Size * Channels);
                    var pixel = Compare(target, expected);
                    var normalizeWatch = Stopwatch.StartNew(); var tensor = Normalize(target); normalizeWatch.Stop();
                    var expectedTensor = Floats(tensorBytes, r.Index * tensor.Length * 4, tensor.Length);
                    var tensorDiff = Compare(tensor, expectedTensor);
                    var onnxWatch = Stopwatch.StartNew(); var p = Infer(session, tensor); onnxWatch.Stop(); total.Stop();
                    runs.Add(new Sample { R = r, P = p, TargetHash = Hash(target), Target = pixel, TensorHash = Hash(FloatBytes(tensor)), Tensor = tensorDiff,
                        ResizeMs = resizeWatch.Elapsed.TotalMilliseconds, NormalizeMs = normalizeWatch.Elapsed.TotalMilliseconds, OnnxMs = onnxWatch.Elapsed.TotalMilliseconds, TotalMs = total.Elapsed.TotalMilliseconds });
                }
            }
            WriteTarget(output, runs); WriteTensor(output, runs); WriteOnnx(output, runs);
            var targetEqual = runs.Count(x => x.TargetHash == x.R.TargetHash);
            var tensorMax = runs.Max(x => x.Tensor.Max); var tensorMean = runs.Average(x => x.Tensor.Mae); var tensorDifferent = runs.Sum(x => x.Tensor.Different);
            var predEqual = runs.Count(x => Pred(x.P) == x.R.Pred); var zoneEqual = runs.Count(x => Zone(x.P) == x.R.Zone);
            var deltas = runs.Select(x => Math.Abs(x.P - x.R.P)).OrderBy(x => x).ToList();
            var holdout = runs.Where(x => x.R.Cohort == "HOLDOUT").ToList();
            var fileCorrect = holdout.Count(x => Pred(x.P) == assets[x.R.Sha].Label);
            var groupsCorrect = holdout.GroupBy(x => x.R.Group).Count(g => Pred((float)g.Average(x => x.P)) == assets[g.First().R.Sha].Label);
            var dangerous = holdout.Count(x => (assets[x.R.Sha].Label == "FACTURA" && Zone(x.P) == "NO_FACTURA_FUERTE") || (assets[x.R.Sha].Label == "NO_FACTURA" && Zone(x.P) == "FACTURA_FUERTE"));
            var gates = new[] { sourceEqual == 80, pythonTensorPred == 80 && pythonTensorZone == 80 && pythonTensorMax <= .00001,
                targetEqual == 80, tensorMax <= .000001, predEqual == 80 && zoneEqual == 80 && Pct(deltas, .95) <= .00001 && deltas.Last() <= .00005,
                fileCorrect == 10 && groupsCorrect == 5 && dangerous == 0 };
            var approved = gates.All(x => x);
            WriteReports(output, runs, sourceEqual, pythonTensorMax, targetEqual, tensorMean, tensorMax, tensorDifferent, predEqual, zoneEqual, fileCorrect, groupsCorrect, dangerous, gates, approved);
            DeleteTemp(targetBin, tensorBin);
            Console.WriteLine("H1D9D1 | Resultado=" + (approved ? "H1D9D1 APROBADO" : "H1D9D1 NO APROBADO") + " | Source=" + sourceEqual + "/80 | Target=" + targetEqual + "/80 | Pred=" + predEqual + "/80 | Zones=" + zoneEqual + "/80 | P95=" + F(Pct(deltas, .95)) + " | Max=" + F(deltas.Last()));
            return approved ? 0 : 1;
        }

        private static byte[] DecodeRgb(string path, out int width, out int height)
        {
            using (var image = Image.FromFile(path)) { width = image.Width; height = image.Height; using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            { using (var g = Graphics.FromImage(bitmap)) { g.DrawImageUnscaled(image, 0, 0); }
              var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb); try
              { var raw = new byte[Math.Abs(data.Stride) * height]; Marshal.Copy(data.Scan0, raw, 0, raw.Length); var rgb = new byte[width * height * 3];
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var s = y * data.Stride + x * 3; var d = (y * width + x) * 3; rgb[d] = raw[s + 2]; rgb[d + 1] = raw[s + 1]; rgb[d + 2] = raw[s]; } return rgb; }
              finally { bitmap.UnlockBits(data); } } }
        }

        // Pillow-compatible 8-bit separable resampling. Coefficient layout and 22-bit fixed-point rounding follow Pillow's libImaging/Resample.c.
        // Pillow is licensed under the open-source HPND license: https://github.com/python-pillow/Pillow/blob/main/LICENSE
        private static byte[] Letterbox(byte[] source, int sw, int sh)
        {
            var scale = Math.Min((double)Size / sw, (double)Size / sh); var dw = Math.Max(1, (int)Math.Round(sw * scale, MidpointRounding.ToEven)); var dh = Math.Max(1, (int)Math.Round(sh * scale, MidpointRounding.ToEven));
            var horizontal = ResampleHorizontal(source, sw, sh, dw); var resized = ResampleVertical(horizontal, dw, sh, dh);
            var canvas = Enumerable.Repeat((byte)255, Size * Size * 3).ToArray(); var left = (Size - dw) / 2; var top = (Size - dh) / 2;
            for (var y = 0; y < dh; y++) Buffer.BlockCopy(resized, y * dw * 3, canvas, ((top + y) * Size + left) * 3, dw * 3); return canvas;
        }

        private static byte[] ResampleHorizontal(byte[] input, int iw, int ih, int ow)
        { var table = Coefficients(iw, ow); var output = new byte[ow * ih * 3]; for (var y = 0; y < ih; y++) for (var x = 0; x < ow; x++) for (var c = 0; c < 3; c++)
          { long sum = 1 << (Precision - 1); var t = table[x]; for (var k = 0; k < t.C.Length; k++) sum += input[(y * iw + t.Start + k) * 3 + c] * (long)t.C[k]; output[(y * ow + x) * 3 + c] = Clip(sum >> Precision); } return output; }
        private static byte[] ResampleVertical(byte[] input, int iw, int ih, int oh)
        { var table = Coefficients(ih, oh); var output = new byte[iw * oh * 3]; for (var y = 0; y < oh; y++) for (var x = 0; x < iw; x++) for (var c = 0; c < 3; c++)
          { long sum = 1 << (Precision - 1); var t = table[y]; for (var k = 0; k < t.C.Length; k++) sum += input[((t.Start + k) * iw + x) * 3 + c] * (long)t.C[k]; output[(y * iw + x) * 3 + c] = Clip(sum >> Precision); } return output; }
        private static Coeff[] Coefficients(int input, int output)
        { var scale = (double)input / output; var filterScale = Math.Max(1.0, scale); var support = 2.0 * filterScale; var result = new Coeff[output];
          for (var xx = 0; xx < output; xx++) { var center = (xx + .5) * scale; var xmin = Math.Max(0, (int)(center - support + .5)); var xmax = Math.Min(input, (int)(center + support + .5)); var values = new double[xmax - xmin]; var total = 0.0;
            for (var x = 0; x < values.Length; x++) { values[x] = Cubic((x + xmin - center + .5) / filterScale); total += values[x]; }
            if (total != 0) for (var x = 0; x < values.Length; x++) values[x] /= total;
            result[xx] = new Coeff { Start = xmin, C = values.Select(v => (int)(.5 + v * (1 << Precision))).ToArray() }; } return result; }
        private static double Cubic(double x) { x = Math.Abs(x); return x < 1 ? ((1.5 * x - 2.5) * x * x + 1) : x < 2 ? (((-.5 * x + 2.5) * x - 4) * x + 2) : 0; }
        private static byte Clip(long x) { return x < 0 ? (byte)0 : x > 255 ? (byte)255 : (byte)x; }

        private static float[] Normalize(byte[] rgb) { var result = new float[Size * Size * 3]; for (var i = 0; i < Size * Size; i++) for (var c = 0; c < 3; c++) result[c * Size * Size + i] = ((rgb[i * 3 + c] / 255f) - Mean[c]) / Std[c]; return result; }
        private static float Infer(InferenceSession session, float[] tensor) { using (var input = OrtValue.CreateTensorValueFromMemory(tensor, new long[] { 1, 3,224,224 })) using (var options = new RunOptions()) using (var outputs = session.Run(options, new[] { "image" }, new[] { input }, new[] { "probabilities" })) return outputs[0].GetTensorDataAsSpan<float>()[1]; }
        private static Diff Compare(byte[] a, byte[] b) { long sum = 0, n = 0; var max = 0; for (var i = 0; i < a.Length; i++) { var d = Math.Abs(a[i] - b[i]); if (d != 0) n++; sum += d; max = Math.Max(max, d); } return new Diff { Different = n, Mae = (double)sum / a.Length, Max = max }; }
        private static Diff Compare(float[] a, float[] b) { double sum = 0, max = 0; long n = 0; for (var i = 0; i < a.Length; i++) { var d = Math.Abs(a[i] - b[i]); if (d != 0) n++; sum += d; max = Math.Max(max, d); } return new Diff { Different = n, Mae = sum / a.Length, Max = max }; }

        private static void WriteTarget(string o, List<Sample> x) { Write(Path.Combine(o,"target-rgb-parity.csv"), new[]{"Sha256,Cohort,PythonTargetRgbSha256,CSharpTargetRgbSha256,Equal,DifferentBytes,DifferentPercent,MaeRgb,MaxChannelDelta"}.Concat(x.Select(r=>string.Join(",",r.R.Sha,r.R.Cohort,r.R.TargetHash,r.TargetHash,B(r.R.TargetHash==r.TargetHash),r.Target.Different,F(100.0*r.Target.Different/(Size*Size*3)),F(r.Target.Mae),F(r.Target.Max))))); }
        private static void WriteTensor(string o, List<Sample> x) { Write(Path.Combine(o,"tensor-parity.csv"), new[]{"Sha256,Cohort,PythonTensorSha256,CSharpTensorSha256,HashEqual,DifferentFloats,MeanAbsDelta,MaxAbsDelta"}.Concat(x.Select(r=>string.Join(",",r.R.Sha,r.R.Cohort,r.R.TensorHash,r.TensorHash,B(r.R.TensorHash==r.TensorHash),r.Tensor.Different,F(r.Tensor.Mae),F(r.Tensor.Max))))); }
        private static void WriteOnnx(string o, List<Sample> x) { Write(Path.Combine(o,"onnx-parity.csv"), new[]{"Sha256,Cohort,PythonPFactura,CSharpPFactura,AbsDelta,PythonPred050,CSharpPred050,PythonZona,CSharpZona,Pred050Equal,ZonaEqual"}.Concat(x.Select(r=>string.Join(",",r.R.Sha,r.R.Cohort,F9(r.R.P),F9(r.P),F9(Math.Abs(r.R.P-r.P)),r.R.Pred,Pred(r.P),r.R.Zone,Zone(r.P),B(r.R.Pred==Pred(r.P)),B(r.R.Zone==Zone(r.P)))))); }
        private static void WriteReports(string o,List<Sample>x,int source,double pythonMax,int target,double tensorMean,double tensorMax,long tensorDiff,int pred,int zones,int files,int groups,int dangerous,bool[] gates,bool approved)
        { var ds=x.Select(r=>Math.Abs(r.P-r.R.P)).OrderBy(v=>v).ToList(); var metrics="# H1D9D1 — Métricas\n\n- Source RGB iguales: "+source+"/80.\n- ONNX C# con tensor Python: delta máximo "+F(pythonMax)+".\n- Target RGB iguales: "+target+"/80.\n- Tensor: media "+F(tensorMean)+", máximo "+F(tensorMax)+", floats distintos "+tensorDiff+".\n- Pred050: "+pred+"/80; zonas: "+zones+"/80.\n- PFactura delta: media "+F(ds.Average())+", P95 "+F(Pct(ds,.95))+", máximo "+F(ds.Last())+".\n- Decode ms: medido dentro de carga inicial no temporizada individualmente.\n- Resize/letterbox ms: "+Stats(x.Select(r=>r.ResizeMs))+".\n- Normalización ms: "+Stats(x.Select(r=>r.NormalizeMs))+".\n- ONNX ms: "+Stats(x.Select(r=>r.OnnxMs))+".\n- Total ms: "+Stats(x.Select(r=>r.TotalMs))+".\n- HOLDOUT: "+files+"/10 archivos, "+groups+"/5 grupos, "+dangerous+" errores fuertes.\n\nGates: decode="+Pass(gates[0])+", ONNX tensor Python="+Pass(gates[1])+", target RGB="+Pass(gates[2])+", tensor="+Pass(gates[3])+", ONNX final="+Pass(gates[4])+", HOLDOUT="+Pass(gates[5])+".\n"; Write(Path.Combine(o,"parity-metrics.md"),metrics);
          Write(Path.Combine(o,"resumen.md"),"# H1D9D1 — Resultado\n\n`H1D9D1 "+(approved?"APROBADO":"NO APROBADO")+"`\n\n"+metrics.Substring(metrics.IndexOf("- Source"))+"\nNo hubo entrenamiento, tuning ni cambios de producto.\n");
          var json="{\n  \"milestone\": \"H1D9D1\",\n  \"status\": \""+(approved?"APROBADO":"NO_APROBADO")+"\",\n  \"pillow_version\": \"12.3.0\",\n  \"onnx_runtime\": \"1.29.0\",\n  \"source_rgb_equal\": "+source+",\n  \"python_tensor_onnx_max_delta\": "+F9(pythonMax)+",\n  \"target_rgb_equal\": "+target+",\n  \"tensor_max_delta\": "+F9(tensorMax)+",\n  \"pred050_equal\": "+pred+",\n  \"zones_equal\": "+zones+",\n  \"probability_delta\": {\"mean\": "+F9(ds.Average())+", \"p95\": "+F9(Pct(ds,.95))+", \"max\": "+F9(ds.Last())+"},\n  \"gates\": ["+string.Join(",",gates.Select(B))+"],\n  \"temporary_binaries_deleted\": true,\n  \"training_performed\": false,\n  \"threshold_tuning_performed\": false,\n  \"product_modified\": false\n}\n"; Write(Path.Combine(o,"parity-manifest.json"),json); }
        private static void WriteFailure(string o,string stage,int source) { Write(Path.Combine(o,"resumen.md"),"# H1D9D1 — Resultado\n\n`H1D9D1 NO APROBADO`\n\nDetenido en gate: "+stage+". Source RGB iguales: "+source+"/80.\n"); }
        private static void WriteFailureArtifacts(string o, int source)
        {
            Write(Path.Combine(o, "target-rgb-parity.csv"), "Sha256,Cohort,Status\nNOT_EXECUTED,,SOURCE_RGB_GATE_FAILED\n");
            Write(Path.Combine(o, "parity-metrics.md"), "# H1D9D1A — Métricas\n\n- Source RGB hashes iguales: " + source + "/80.\n- Width iguales: 80/80.\n- Height iguales: 80/80.\n- Resize, tensor y ONNX final: no ejecutados por fallo del primer gate.\n");
            Write(Path.Combine(o, "resumen.md"), "# H1D9D1A — Resultado\n\n`H1D9D1A NO APROBADO`\n\nDetenido exactamente en el gate Source RGB: " + source + "/80 hashes iguales, con dimensiones 80/80. El redondeo de coeficientes negativos no fue modificado porque el gate previo no pasó.\n");
            Write(Path.Combine(o, "parity-manifest.json"), "{\n  \"milestone\": \"H1D9D1A\",\n  \"status\": \"NO_APROBADO\",\n  \"stopped_at\": \"SOURCE_RGB\",\n  \"previous_csv_actual_equal_true\": 60,\n  \"source_rgb_equal\": " + source + ",\n  \"source_width_equal\": 80,\n  \"source_height_equal\": 80,\n  \"negative_coefficient_rounding_modified\": false,\n  \"resize_executed\": false,\n  \"tensor_executed\": false,\n  \"final_onnx_executed\": false,\n  \"training_performed\": false,\n  \"threshold_tuning_performed\": false,\n  \"product_modified\": false\n}\n");
        }
        private static IEnumerable<Asset> LoadAssets(string p,string c){return Read(p).Select(r=>new Asset{Sha=r["Sha256"],Cohort=c,Group=r["GroupId"],Label=r["LabelBinario"],Path=r["VisualAssetPath"]});}
        private static List<Dictionary<string,string>> Read(string p){var rows=new List<Dictionary<string,string>>();using(var q=new TextFieldParser(p,Encoding.UTF8)){q.SetDelimiters(",");q.HasFieldsEnclosedInQuotes=true;var h=q.ReadFields();while(!q.EndOfData){var f=q.ReadFields();var r=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);for(var i=0;i<h.Length;i++)r[h[i]]=f[i];rows.Add(r);}}return rows;}
        private static byte[] Slice(byte[] x,int o,int n){var r=new byte[n];Buffer.BlockCopy(x,o,r,0,n);return r;} private static float[] Floats(byte[]x,int o,int n){var r=new float[n];Buffer.BlockCopy(x,o,r,0,n*4);return r;} private static byte[] FloatBytes(float[]x){var r=new byte[x.Length*4];Buffer.BlockCopy(x,0,r,0,r.Length);return r;}
        private static string Hash(byte[]x){using(var a=SHA256.Create())return BitConverter.ToString(a.ComputeHash(x)).Replace("-","");} private static void DeleteTemp(params string[]p){foreach(var x in p)if(File.Exists(x))File.Delete(x);} private static void Write(string p,IEnumerable<string>x){File.WriteAllText(p,string.Join("\n",x)+"\n",Utf8);} private static void Write(string p,string x){File.WriteAllText(p,x.Replace("\r\n","\n"),Utf8);}
        private static int I(string x){return int.Parse(x,CultureInfo.InvariantCulture);}private static double D(string x){return double.Parse(x,CultureInfo.InvariantCulture);}private static string F(double x){return x.ToString("0.######",CultureInfo.InvariantCulture);}private static string F9(double x){return x.ToString("0.000000000",CultureInfo.InvariantCulture);}private static string B(bool x){return x?"true":"false";}private static string Pass(bool x){return x?"PASS":"FAIL";}private static string Pred(float p){return p>=.5f?"FACTURA":"NO_FACTURA";}private static string Zone(float p){return p<=TNo?"NO_FACTURA_FUERTE":p>=TYes?"FACTURA_FUERTE":"INCIERTO_VISUAL";}
        private static double Pct(IReadOnlyList<double>x,double p){var r=(x.Count-1)*p;var l=(int)Math.Floor(r);var u=(int)Math.Ceiling(r);return x[l]+(x[u]-x[l])*(r-l);}private static string Stats(IEnumerable<double>v){var x=v.OrderBy(z=>z).ToList();return "media "+F(x.Average())+", P50 "+F(Pct(x,.5))+", P95 "+F(Pct(x,.95))+", máximo "+F(x.Last());}
        private sealed class Coeff{public int Start;public int[]C;}private sealed class Ref{public int Index,W,H;public string Sha,Cohort,Group,SourceHash,TargetHash,TensorHash,Pred,Zone;public double P;}private sealed class Asset{public string Sha,Cohort,Group,Label,Path;}private sealed class Diff{public long Different;public double Mae,Max;}private sealed class Sample{public Ref R;public float P;public string TargetHash,TensorHash;public Diff Target,Tensor;public double ResizeMs,NormalizeMs,OnnxMs,TotalMs;}
    }
}
