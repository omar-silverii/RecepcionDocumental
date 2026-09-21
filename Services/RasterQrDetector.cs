using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RecepcionDocumental.Infrastructure;
using ZXing;
using ZXing.Common;

namespace RecepcionDocumental.Services
{
    public sealed class RasterQrDetectionResult
    {
        public ArcaQrEvidence Evidence { get; set; } = new ArcaQrEvidence();
        public int DurationMilliseconds { get; set; }
    }

    public static class RasterQrDetector
    {
        public static RasterQrDetectionResult Detect(IEnumerable<OcrImageData> images)
        {
            var imageList=(images??Enumerable.Empty<OcrImageData>()).ToList();
            var watch=Stopwatch.StartNew();var output=new RasterQrDetectionResult();
            Log("RasterQR | Inicio | Imagenes="+imageList.Count);
            try
            {
                var reader=new BarcodeReaderGeneric{AutoRotate=true};
                reader.Options=new DecodingOptions{TryHarder=true,TryInverted=true,PossibleFormats=new List<BarcodeFormat>{BarcodeFormat.QR_CODE}};
                for(var index=0;index<imageList.Count;index++)
                {
                    var item=imageList[index];
                    if(item==null||item.Bytes==null||item.Bytes.Length==0)continue;
                    var pageWatch=Stopwatch.StartNew();var detected=false;var arcaValid=false;
                    Log("RasterQR | Pagina="+(index+1)+" | Inicio | Width="+item.Width+" | Height="+item.Height);
                    try
                    {
                        using(var stream=new MemoryStream(item.Bytes,false))using(var source=new Bitmap(stream))using(var bitmap=new Bitmap(source.Width,source.Height,PixelFormat.Format24bppRgb))
                        {
                            using(var graphics=Graphics.FromImage(bitmap))graphics.DrawImageUnscaled(source,0,0);
                            var data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
                            try
                            {
                                var bytes=new byte[Math.Abs(data.Stride)*data.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
                                var decoded=reader.Decode(bytes,bitmap.Width,bitmap.Height,RGBLuminanceSource.BitmapFormat.BGR24);
                                if(decoded!=null)
                                {
                                    detected=true;output.Evidence.QrDetected=true;var arca=ArcaQrDecoder.Decode(decoded.Text);
                                    arcaValid=arca.IsValid;if(arcaValid)output.Evidence=arca;
                                }
                            }
                            finally{bitmap.UnlockBits(data);}
                        }
                    }
                    catch(Exception ex)when(ex is ArgumentException||ex is IOException||ex is ExternalException){continue;}
                    finally
                    {
                        pageWatch.Stop();
                        Log("RasterQR | Pagina="+(index+1)+" | Fin | DuracionMs="+pageWatch.ElapsedMilliseconds+" | Detectado="+(detected?"Sí":"No")+" | ArcaValido="+(arcaValid?"Sí":"No"));
                    }
                    if(arcaValid)break;
                }
            }
            finally
            {
                watch.Stop();output.DurationMilliseconds=(int)Math.Min(int.MaxValue,watch.ElapsedMilliseconds);
                Log("RasterQR | Fin | DuracionMs="+watch.ElapsedMilliseconds);
            }
            return output;
        }

        private static void Log(string message)
        { if(Logs.EstaInicializado)Logs.LogProc(message); }
    }
}
