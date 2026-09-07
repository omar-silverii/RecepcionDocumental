using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Newtonsoft.Json;
using RecepcionDocumental.Services;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;
// Offline adapter only: existing product extraction/OCR/QR and normalization.
// No classifier, training, database, Gmail, or label input.
public class H1D10Entry {
 public static int Main(string[] a) {
  try {
   Assembly.LoadFrom(Path.Combine(a[0],"bin","RecepcionDocumental.dll"));
   var d=AppDomain.CreateDomain("H1D10A",null,new AppDomainSetup{ApplicationBase=a[0],PrivateBinPath="bin",ConfigurationFile=Path.Combine(a[0],"tools","PdfRasterProbe","bin","PdfRasterProbe.exe.config")});
   try { return ((H1D10Worker)d.CreateInstanceFromAndUnwrap(typeof(H1D10Entry).Assembly.Location,typeof(H1D10Worker).FullName)).Boot(a); } finally{AppDomain.Unload(d);}
  }catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);return 1;}
 }
}
public class H1D10Worker:MarshalByRefObject {
 Type visual;
 public override object InitializeLifetimeService(){return null;}
 public int Boot(string[] a){Assembly.LoadFrom(Path.Combine(a[0],"bin","RecepcionDocumental.dll"));visual=Assembly.LoadFrom(Path.Combine(a[1],"bin","VisualNormalization.dll")).GetType("RecepcionDocumental.Services.VisualInvoiceShadowService");return Run(a);}
 object Call(string name,params object[] args){return visual.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
 byte[] Decode(byte[] bytes,out int w,out int h){object[] a={bytes,0,0};var result=(byte[])Call("DecodeRgbDirect",a);w=(int)a[1];h=(int)a[2];return result;}
 static string Hash(byte[] b){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(b)).Replace("-","");}
 static string FileHash(string p){using(var s=SHA256.Create())using(var f=File.OpenRead(p))return BitConverter.ToString(s.ComputeHash(f)).Replace("-","");}
 static void SaveAsset(Bitmap image,string path){using(var stream=new MemoryStream()){image.Save(stream,ImageFormat.Png);var bytes=stream.ToArray();if(File.Exists(path)){if(FileHash(path)!=Hash(bytes))throw new InvalidDataException("Existing asset differs from deterministic reconstruction.");}else{using(var file=new FileStream(path,FileMode.CreateNew))file.Write(bytes,0,bytes.Length);}}}
 static Bitmap Rgb(byte[] rgb,int w,int h){var b=new Bitmap(w,h,PixelFormat.Format24bppRgb);var d=b.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,b.PixelFormat);try{var line=new byte[Math.Abs(d.Stride)];for(int y=0;y<h;y++){Array.Clear(line,0,line.Length);for(int x=0;x<w;x++){var i=(y*w+x)*3;line[x*3]=rgb[i+2];line[x*3+1]=rgb[i+1];line[x*3+2]=rgb[i];}Marshal.Copy(line,0,IntPtr.Add(d.Scan0,y*d.Stride),line.Length);}}finally{b.UnlockBits(d);}return b;}
 static object Evidence(ArcaQrEvidence q){return new{q.QrDetected,q.IsValid,q.TipoComprobante,CanonicalLabel=q.IsValid && q.TipoComprobante.HasValue ? (ArcaQrDecoder.IsInvoiceType(q.TipoComprobante.Value)?"FACTURA":ArcaQrDecoder.IsKnownNonInvoiceType(q.TipoComprobante.Value)?"OTRO_DOCUMENTO":"") : ""};}
 static string DHash(Bitmap b){using(var small=new Bitmap(9,8,PixelFormat.Format24bppRgb)){using(var g=Graphics.FromImage(small)){g.Clear(Color.White);g.DrawImage(b,0,0,9,8);}ulong bits=0;for(int y=0;y<8;y++)for(int x=0;x<8;x++){var c=small.GetPixel(x,y);var n=small.GetPixel(x+1,y);bits=(bits<<1)|((c.R+c.G+c.B>n.R+n.G+n.B)?1UL:0UL);}return bits.ToString("X16");}}
 static void Json(string p,object o){File.WriteAllText(p,JsonConvert.SerializeObject(o,Formatting.Indented),new System.Text.UTF8Encoding(false));}
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 int Run(string[] a){
  var output=a[1];var work=Path.Combine(output,"work",Guid.NewGuid().ToString("N"));
  var c=new ConfiguracionAplicacion("RecepcionDocumental",Path.Combine(work,"Logs"),Path.Combine(work,"Temp"),Path.Combine(work,"UnusedInvoices"),Path.Combine(work,"UnusedReview"),200,52428800,262144000,3,"https://localhost/h1d10a",false);
  c.PrepararRutasOperativas();ConfiguracionSistema.Inicializar(c);Logs.Inicializar(c);
  if(a[2]=="regression")return Regression(a[3],output);
  var assetRoot=a.Length>3?Path.GetFullPath(a[3]):Path.Combine(output,"assets");
  Directory.CreateDirectory(assetRoot);
  var timeoutPath=Path.Combine(output,"ocr-timeouts.json");
  var timeouts=File.Exists(timeoutPath)?JsonConvert.DeserializeObject<Dictionary<string,int>>(File.ReadAllText(timeoutPath)):new Dictionary<string,int>();
  int done=0;
  foreach(var line in File.ReadAllLines(a[2])){
   var f=line.Split('\t');var sha=f[0];var path=f[1];var record=Path.Combine(output,"raw",sha+".json");
   if(File.Exists(record)){done++;continue;}
   var data=new Dictionary<string,object>{{"Sha256",sha},{"FilePath",path},{"Errors",new List<string>()}};var errors=(List<string>)data["Errors"];
   try {
    if(FileHash(path)!=sha)throw new Exception("SOURCE_HASH_MISMATCH");
    byte[] bytes=null;string native="";bool useful=false;var embedded=new ArcaQrEvidence();
    using(var workspace=new AttachmentWorkspace()){
     if(Path.GetExtension(path).Equals(".pdf",StringComparison.OrdinalIgnoreCase)){
      var text=MdocPdfTextExtractor.Extract(path);native=text.Text??"";useful=text.HasUsefulText;data["NativeUseful"]=useful;data["NativeFailure"]=text.FailureReason;
      embedded=MdocPdfQrDetector.Detect(path);
      var raster=PdfPageRasterizer.RasterizeFirstPage(path,workspace);data["PageCount"]=raster.PageCount;
      if(raster.Images.Count>0)bytes=raster.Images[0].Bytes;else errors.Add("RASTER: "+raster.FailureReason);
     }else{bytes=(byte[])Call("CanonicalizeImage",path);data["NativeUseful"]=false;using(var im=Image.FromFile(path)){data["PageCount"]=im.GetFrameCount(new FrameDimension(im.FrameDimensionsList[0]));}}
     var baseName=Path.Combine(output,"text",sha);File.WriteAllText(baseName+".native.txt",native);data["NativeTextAsset"]=baseName+".native.txt";
     data["EmbeddedQr"]=Evidence(embedded);
     if(bytes!=null){
      int w,h;var rgb=Decode(bytes,out w,out h);data["Width"]=w;data["Height"]=h;data["RgbSha256"]=Hash(rgb);
      if(new DriveInfo(Path.GetPathRoot(assetRoot)).AvailableFreeSpace<256L*1024*1024)throw new IOException("ASSET_DISK_RESERVE_REACHED");
      var full=Path.Combine(assetRoot,sha+".full.png");var header=Path.Combine(assetRoot,sha+".header.png");
      using(var image=Rgb(rgb,w,h)){
       SaveAsset(image,full);data["FullDHash"]=DHash(image);
       int hh=Math.Max(1,(int)Math.Ceiling(h*.35));
       using(var crop=image.Clone(new Rectangle(0,0,w,hh),PixelFormat.Format24bppRgb)){SaveAsset(crop,header);data["HeaderDHash"]=DHash(crop);data["HeaderHeight"]=hh;}
      }
      data["FullPageAsset"]=full;data["HeaderAsset"]=header;data["FullPageSha256"]=FileHash(full);data["HeaderSha256"]=FileHash(header);
      var imageData=new OcrImageData{Bytes=File.ReadAllBytes(full),Width=w,Height=h};
      // Also inspect first-page raster so contradictory embedded/raster QR evidence is retained.
      data["RasterQr"]=Evidence(RasterQrDetector.Detect(new[]{imageData}).Evidence);
      var head=DocumentOcrService.Recognize(new[]{new OcrImageData{Bytes=File.ReadAllBytes(header),Width=w,Height=(int)data["HeaderHeight"]}});
      File.WriteAllText(baseName+".header.txt",head.Text??"");data["HeaderTextAsset"]=baseName+".header.txt";data["HeaderOcrConfidence"]=head.MeanConfidence;data["HeaderOcrSuccess"]=head.Success;if(!head.Success)errors.Add("HEADER_OCR: "+head.FailureReason);
      if(!useful&&timeouts.ContainsKey(sha)){data["TextAsset"]=baseName+".header.txt";data["TextMethod"]="OCR_HEADER_ONLY_FULL_PAGE_TIMEOUT";errors.Add("PRODUCT_FULL_PAGE_OCR_TIMEOUT_AFTER_"+timeouts[sha]+"_SECONDS; header retained, manual review required.");}
      else if(!useful){var ocr=DocumentOcrService.Recognize(new[]{imageData});File.WriteAllText(baseName+".ocr.txt",ocr.Text??"");data["TextAsset"]=baseName+".ocr.txt";data["TextMethod"]="OCR_PRODUCT_FIRST_PAGE";data["OcrConfidence"]=ocr.MeanConfidence;if(!ocr.Success)errors.Add("OCR: "+ocr.FailureReason);}
      else{data["TextAsset"]=baseName+".native.txt";data["TextMethod"]="MDOC_DOCUMENT+OCR_HEADER_POSITION";}
     }
    }
    if(FileHash(path)!=sha)throw new Exception("SOURCE_HASH_CHANGED");
   }catch(Exception e){errors.Add(e.GetType().Name+": "+e.Message);}
   data["Status"]=errors.Count==0?"OK":"REVIEW_QUALITY";Json(record,data);done++;Console.WriteLine("Prepared "+done+"/"+File.ReadAllLines(a[2]).Length);
  }
  if(VisualInvoiceShadowService.SessionsCreated!=0||(int)visual.GetProperty("SessionsCreated").GetValue(null)!=0)throw new Exception("FORBIDDEN_MODEL_SESSION");
  Json(Path.Combine(output,Path.GetFileNameWithoutExtension(a[2])+"-completion.json"),new{Completed=done,SessionsCreated=0,ProductAssemblySha256=FileHash(typeof(PdfPageRasterizer).Assembly.Location),NormalizationAssemblySha256=FileHash(visual.Assembly.Location),AssetRoot=assetRoot});
  return 0;
 }
 int Regression(string tsv,string output){
  var results=new List<object>();bool passed=true;
  foreach(var line in File.ReadAllLines(tsv)){
   var f=line.Split('\t');int w,h;var source=Decode(File.ReadAllBytes(f[1]),out w,out h);
   int rw=Math.Max(1,(int)Math.Round(w*Math.Min(224d/w,224d/h),MidpointRounding.ToEven)),rh=Math.Max(1,(int)Math.Round(h*Math.Min(224d/w,224d/h),MidpointRounding.ToEven));
   var horizontal=(byte[])Call("Horizontal",source,w,h,rw);var resized=(byte[])Call("Vertical",horizontal,rw,h,rh);var target=Enumerable.Repeat((byte)255,224*224*3).ToArray();for(int y=0;y<rh;y++)Buffer.BlockCopy(resized,y*rw*3,target,(((224-rh)/2+y)*224+(224-rw)/2)*3,rw*3);
   var tensor=(float[])Call("Normalize",target);var tb=new byte[tensor.Length*4];Buffer.BlockCopy(tensor,0,tb,0,tb.Length);
   bool ok=Hash(source)==f[2]&&Hash(target)==f[3]&&Hash(tb)==f[4];passed&=ok;results.Add(new{Sha256=f[0],SourceEqual=Hash(source)==f[2],TargetEqual=Hash(target)==f[3],TensorEqual=Hash(tb)==f[4]});
  }
  var tif=File.ReadAllText(Path.Combine(output,"regression-tiff.txt"));var png=(byte[])Call("CanonicalizeImage",tif);int tw,th;var actual=Decode(png,out tw,out th);bool equal=true;
  using(var ps=new MemoryStream(png))using(var b=new Bitmap(ps)){for(int y=0;y<th;y++)for(int x=0;x<tw;x++){var c=b.GetPixel(x,y);int i=(y*tw+x)*3;if(actual[i]!=c.R||actual[i+1]!=c.G||actual[i+2]!=c.B)equal=false;}}
  passed&=equal;Json(Path.Combine(output,"normalization-regression.json"),new{Passed=passed,HistoricalCases=results.Count,Cases=results,TiffAllPixelsEqual=equal,TiffWidth=tw,TiffHeight=th,SessionsCreated=(int)visual.GetProperty("SessionsCreated").GetValue(null)});Console.WriteLine("Normalization regression="+passed);return passed?0:1;
 }
}
