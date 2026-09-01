using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Newtonsoft.Json;

namespace RecepcionDocumental.Services
{
    public sealed class VisualShadowResult
    {
        public bool Attempted { get; set; }
        public string Status { get; set; }
        public string ModelVersion { get; set; }
        public string ModelSha256 { get; set; }
        public string PreprocessingVersion { get; set; }
        public double? PNoFactura { get; set; }
        public double? PFactura { get; set; }
        public string Zone { get; set; }
        public string VisualSource { get; set; }
        public bool RasterReused { get; set; }
        public int? DecodeMilliseconds { get; set; }
        public int? HorizontalMilliseconds { get; set; }
        public int? VerticalMilliseconds { get; set; }
        public int? LetterboxMilliseconds { get; set; }
        public int? ResizeMilliseconds { get; set; }
        public int? NormalizeMilliseconds { get; set; }
        public int? OnnxMilliseconds { get; set; }
        public int? TotalMilliseconds { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorReason { get; set; }
    }

    public static class VisualInvoiceShadowService
    {
        public const string ExpectedModelVersion = "H1D9B-CANDIDATE-001";
        public const string ExpectedModelSha256 = "A1DC24FE90C3C14303C3319EC0BD6D9EF95E91CC82C9B38E2FBAAEB0EF826811";
        public const long ExpectedModelBytes = 16708744;
        public const string PreprocessingVersion = "H1D9D1C-PILLOW-12.3.0-BICUBIC-FIXED22";
        public const double TNoFactura = 0.1169927716255188;
        public const double TFactura = 0.7979831695556641;
        private const int Size = 224, Precision = 22;
        private static readonly float[] Mean = { .485f, .456f, .406f };
        private static readonly float[] Std = { .229f, .224f, .225f };
        private static readonly Lazy<RuntimeState> Runtime = new Lazy<RuntimeState>(LoadRuntime, true);
        private static int _sessionsCreated;

        public static int SessionsCreated { get { return _sessionsCreated; } }

        public static VisualShadowResult CreateRasterError(string source,string reason)
        { var result=Base(source,false);result.ErrorCode="PDF_RASTER_ERROR";result.ErrorReason=string.IsNullOrWhiteSpace(reason)?"No se obtuvo la primera página.":reason;return result; }

        public static VisualShadowResult CreateUnsupportedError()
        { var result=Base("UNSUPPORTED_RETAINED_DOCUMENT",false);result.ErrorCode="UNSUPPORTED_FORMAT";result.ErrorReason="El formato conservado no admite visión en H1D9E.";return result; }

        public static VisualShadowResult EvaluateCanonicalPng(byte[] png, string visualSource, bool rasterReused)
        {
            return EvaluateCanonicalPngCore(png, visualSource, rasterReused, null);
        }

        public static VisualShadowResult EvaluateCanonicalPngForValidation(byte[] png,string modelDirectory)
        { return EvaluateCanonicalPngCore(png,"VALIDATION",false,modelDirectory); }

        internal static VisualShadowResult EvaluateCanonicalPngCore(byte[] png, string visualSource, bool rasterReused, string modelDirectoryOverride)
        {
            var total = Stopwatch.StartNew();
            var result = Base(visualSource, rasterReused);
            try
            {
                int width, height;
                var decode = Stopwatch.StartNew();
                var source = DecodeRgbDirect(png, out width, out height);
                decode.Stop(); result.DecodeMilliseconds = Ms(decode);
                var scale = Math.Min((double)Size / width, (double)Size / height);
                var resizedWidth = Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.ToEven));
                var resizedHeight = Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.ToEven));
                var horizontalWatch = Stopwatch.StartNew();
                var horizontal = Horizontal(source, width, height, resizedWidth);
                horizontalWatch.Stop(); result.HorizontalMilliseconds = Ms(horizontalWatch);
                var verticalWatch = Stopwatch.StartNew();
                var resized = Vertical(horizontal, resizedWidth, height, resizedHeight);
                verticalWatch.Stop(); result.VerticalMilliseconds = Ms(verticalWatch);
                result.ResizeMilliseconds = result.HorizontalMilliseconds + result.VerticalMilliseconds;
                var letterboxWatch = Stopwatch.StartNew();
                var target = Enumerable.Repeat((byte)255, Size * Size * 3).ToArray();
                var left = (Size - resizedWidth) / 2; var top = (Size - resizedHeight) / 2;
                for (var y = 0; y < resizedHeight; y++) Buffer.BlockCopy(resized, y * resizedWidth * 3, target, ((top + y) * Size + left) * 3, resizedWidth * 3);
                letterboxWatch.Stop(); result.LetterboxMilliseconds = Ms(letterboxWatch);
                var normalizeWatch = Stopwatch.StartNew();
                var tensor = Normalize(target);
                normalizeWatch.Stop(); result.NormalizeMilliseconds = Ms(normalizeWatch);
                var state = modelDirectoryOverride == null ? Runtime.Value : LoadRuntime(modelDirectoryOverride);
                var onnxWatch = Stopwatch.StartNew();
                float pFactura;
                using (var input = OrtValue.CreateTensorValueFromMemory(tensor, new long[] { 1, 3, Size, Size }))
                using (var options = new RunOptions())
                using (var output = state.Session.Run(options, new[] { "image" }, new[] { input }, new[] { "probabilities" }))
                    pFactura = output[0].GetTensorDataAsSpan<float>()[1];
                onnxWatch.Stop(); result.OnnxMilliseconds = Ms(onnxWatch);
                result.PFactura = pFactura; result.PNoFactura = 1d - pFactura;
                result.Zone = pFactura <= TNoFactura ? "NO_FACTURA_FUERTE" : pFactura >= TFactura ? "FACTURA_FUERTE" : "INCIERTO_VISUAL";
                result.Status = "OK";
            }
            catch (Exception ex)
            {
                result.Status = "ERROR"; result.ErrorCode = ErrorCode(ex); result.ErrorReason = SafeReason(ex);
            }
            finally { total.Stop(); result.TotalMilliseconds = Ms(total); }
            return result;
        }

        public static VisualShadowResult EvaluateImageFile(string path)
        {
            var result = Base("IMAGE_CANONICAL_PNG", false);
            try { return EvaluateCanonicalPng(CanonicalizeImage(path), "IMAGE_CANONICAL_PNG", false); }
            catch (Exception ex) { result.Status = "ERROR"; result.ErrorCode = ErrorCode(ex); result.ErrorReason = SafeReason(ex); return result; }
        }

        private static byte[] CanonicalizeImage(string path)
        {
            using (var image = Image.FromFile(path))
            {
                const int orientationId = 0x0112;
                if (image.PropertyIdList.Contains(orientationId))
                {
                    var value = image.GetPropertyItem(orientationId).Value;
                    var orientation = value.Length >= 2 ? BitConverter.ToUInt16(value, 0) : (ushort)1;
                    var rotate = RotateFlipType.RotateNoneFlipNone;
                    switch (orientation) { case 2: rotate=RotateFlipType.RotateNoneFlipX; break; case 3: rotate=RotateFlipType.Rotate180FlipNone; break; case 4: rotate=RotateFlipType.Rotate180FlipX; break; case 5: rotate=RotateFlipType.Rotate90FlipX; break; case 6: rotate=RotateFlipType.Rotate90FlipNone; break; case 7: rotate=RotateFlipType.Rotate270FlipX; break; case 8: rotate=RotateFlipType.Rotate270FlipNone; break; }
                    if (rotate != RotateFlipType.RotateNoneFlipNone) image.RotateFlip(rotate);
                }
                using (var memory = new MemoryStream()) { image.Save(memory, ImageFormat.Png); return memory.ToArray(); }
            }
        }

        private static RuntimeState LoadRuntime() { return LoadRuntime(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "DocumentAi", "Models", ExpectedModelVersion)); }
        private static RuntimeState LoadRuntime(string directory)
        {
            if (!Environment.Is64BitProcess) throw new BadImageFormatException("Visual shadow requiere un proceso x64.");
            var manifestPath = Path.Combine(directory, "runtime-manifest.json"); var modelPath = Path.Combine(directory, "candidate.onnx");
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("No se encontró el manifest visual.");
            if (!File.Exists(modelPath)) throw new FileNotFoundException("No se encontró el modelo visual.");
            var manifest = JsonConvert.DeserializeObject<RuntimeManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.model_version != ExpectedModelVersion || manifest.onnx_sha256 != ExpectedModelSha256 || manifest.onnx_bytes != ExpectedModelBytes || manifest.ort_version != "1.29.0")
                throw new InvalidDataException("El manifest visual no coincide con el contrato congelado.");
            var info = new FileInfo(modelPath); if (info.Length != ExpectedModelBytes) throw new InvalidDataException("El tamaño del modelo visual es incorrecto.");
            using (var stream = File.OpenRead(modelPath)) using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") != ExpectedModelSha256) throw new InvalidDataException("El SHA-256 del modelo visual es incorrecto.");
            OrtEnv.Instance().DisableTelemetryEvents();
            var sessionOptions = new SessionOptions();
            sessionOptions.LogId = "RecepcionDocumental.VisualShadow";
            var session = new InferenceSession(modelPath, sessionOptions);
            ValidateMetadata(session); System.Threading.Interlocked.Increment(ref _sessionsCreated);
            return new RuntimeState { Session = session };
        }

        private static void ValidateMetadata(InferenceSession session)
        {
            NodeMetadata input, output;
            if (!session.InputMetadata.TryGetValue("image", out input) || !input.Dimensions.SequenceEqual(new[] { 1, 3, 224, 224 })) throw new InvalidDataException("Metadata ONNX de entrada inválida.");
            if (!session.OutputMetadata.TryGetValue("probabilities", out output) || !output.Dimensions.SequenceEqual(new[] { 1, 2 })) throw new InvalidDataException("Metadata ONNX de salida inválida.");
        }

        private static byte[] DecodeRgbDirect(byte[] bytes, out int width, out int height)
        {
            using (var stream = new MemoryStream(bytes, false)) using (var bitmap = new Bitmap(stream, false))
            {
                width=bitmap.Width;height=bitmap.Height;var format=bitmap.PixelFormat;
                if(format!=PixelFormat.Format24bppRgb&&format!=PixelFormat.Format32bppArgb&&format!=PixelFormat.Format8bppIndexed)throw new InvalidDataException("PixelFormat visual no soportado: "+format);
                var data=bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,format);
                try{var stride=Math.Abs(data.Stride);var raw=new byte[stride*height];Marshal.Copy(data.Scan0,raw,0,raw.Length);var rgb=new byte[width*height*3];var palette=format==PixelFormat.Format8bppIndexed?bitmap.Palette.Entries:null;
                    for(var y=0;y<height;y++){var row=data.Stride>=0?y*stride:(height-1-y)*stride;for(var x=0;x<width;x++){var d=(y*width+x)*3;if(format==PixelFormat.Format24bppRgb){var s=row+x*3;rgb[d]=raw[s+2];rgb[d+1]=raw[s+1];rgb[d+2]=raw[s];}else if(format==PixelFormat.Format32bppArgb){var s=row+x*4;rgb[d]=raw[s+2];rgb[d+1]=raw[s+1];rgb[d+2]=raw[s];}else{var c=palette[raw[row+x]];rgb[d]=c.R;rgb[d+1]=c.G;rgb[d+2]=c.B;}}}return rgb;
                }finally{bitmap.UnlockBits(data);}
            }
        }
        private static byte[] Horizontal(byte[] input,int iw,int ih,int ow){var table=Coefficients(iw,ow);var output=new byte[ow*ih*3];for(int y=0;y<ih;y++)for(int x=0;x<ow;x++)for(int c=0;c<3;c++){long value=1<<(Precision-1);for(int k=0;k<table[x].Values.Length;k++)value+=input[(y*iw+table[x].Start+k)*3+c]*(long)table[x].Values[k];output[(y*ow+x)*3+c]=Clip(value>>Precision);}return output;}
        private static byte[] Vertical(byte[] input,int iw,int ih,int oh){var table=Coefficients(ih,oh);var output=new byte[iw*oh*3];for(int y=0;y<oh;y++)for(int x=0;x<iw;x++)for(int c=0;c<3;c++){long value=1<<(Precision-1);for(int k=0;k<table[y].Values.Length;k++)value+=input[((table[y].Start+k)*iw+x)*3+c]*(long)table[y].Values[k];output[(y*iw+x)*3+c]=Clip(value>>Precision);}return output;}
        private static Coeff[] Coefficients(int input,int output){var scale=(double)input/output;var filterScale=Math.Max(1,scale);var support=2*filterScale;var result=new Coeff[output];for(int xx=0;xx<output;xx++){var center=(xx+.5)*scale;var min=Math.Max(0,(int)(center-support+.5));var max=Math.Min(input,(int)(center+support+.5));var values=new double[max-min];double sum=0;for(int k=0;k<values.Length;k++){values[k]=Cubic((k+min-center+.5)/filterScale);sum+=values[k];}for(int k=0;k<values.Length;k++)values[k]/=sum;result[xx]=new Coeff{Start=min,Values=values.Select(v=>v<0?(int)(-.5+v*(1<<Precision)):(int)(.5+v*(1<<Precision))).ToArray()};}return result;}
        private static double Cubic(double x){x=Math.Abs(x);return x<1?((1.5*x-2.5)*x*x+1):x<2?(((-.5*x+2.5)*x-4)*x+2):0;}
        private static byte Clip(long value){return value<0?(byte)0:value>255?(byte)255:(byte)value;}
        private static float[] Normalize(byte[] bytes){var result=new float[Size*Size*3];for(int i=0;i<Size*Size;i++)for(int c=0;c<3;c++)result[c*Size*Size+i]=((bytes[i*3+c]/255f)-Mean[c])/Std[c];return result;}
        private static VisualShadowResult Base(string source,bool reused){return new VisualShadowResult{Attempted=true,Status="ERROR",ModelVersion=ExpectedModelVersion,ModelSha256=ExpectedModelSha256,PreprocessingVersion=PreprocessingVersion,VisualSource=source,RasterReused=reused};}
        private static int Ms(Stopwatch watch){return (int)Math.Min(int.MaxValue,watch.ElapsedMilliseconds);}
        private static string ErrorCode(Exception ex){if(ex is FileNotFoundException)return "MODEL_MISSING";if(ex is BadImageFormatException)return "PROCESS_OR_RUNTIME_X64";if(ex is InvalidDataException&&ex.Message.IndexOf("SHA-256",StringComparison.OrdinalIgnoreCase)>=0)return "MODEL_HASH_INVALID";if(ex is InvalidDataException)return "CONTRACT_INVALID";return "VISUAL_INFERENCE_ERROR";}
        private static string SafeReason(Exception ex){var text=ex.GetType().Name+": "+ex.Message;return text.Length<=1000?text:text.Substring(0,1000);}
        private sealed class Coeff{public int Start;public int[] Values;}
        private sealed class RuntimeState{public InferenceSession Session;}
        private sealed class RuntimeManifest{public string model_version {get;set;} public string onnx_sha256 {get;set;} public long onnx_bytes {get;set;} public string ort_version {get;set;}}
    }
}
