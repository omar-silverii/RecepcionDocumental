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
using Microsoft.VisualBasic.FileIO;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;
using ZXing;
using ZXing.Common;

namespace PdfRasterProbe
{
 internal static class H1D8AFiscalEvidenceProbe
 {
  const string DatasetHash="AF1FCC230B279D7DA4ACD77F5641BB1835D7D42EA0E8A41AD524D473FEC48158";
  const string B4C8="B4C8FA3786E8BA119F3C5F40F1BF5868D7591D0E865FF2AF84F7235674F33C88";
  static readonly string[] InvoiceTypes={"FACTURA A","FACTURA B","FACTURA C","FACTURA M","FACTURA E","FACTURA DE CREDITO ELECTRONICA"};
  static readonly string[] Fiscal={"CUIT","CAE","CAEA","PUNTO DE VENTA","PTO VTA","COMPROBANTE","IMPORTE TOTAL","TOTAL","IVA","FECHA DE EMISION"};
  static readonly string[] OtherTypes={"NOTA DE CREDITO","NOTA DE DEBITO","REMITO","ORDEN DE COMPRA","RECIBO","NOTA DE PEDIDO","PRESUPUESTO"};

  internal static int Run(string[] a)
  {
   if(a.Length!=4){Console.Error.WriteLine("Uso: --h1d8a-fiscal-evidence <dataset.csv> <H1D5C2-product-validation.csv> <output>");return 2;}
   try
   {
    var dataset=Path.GetFullPath(a[1]);var old=Path.GetFullPath(a[2]);var output=Path.GetFullPath(a[3]);Check(dataset,DatasetHash);Directory.CreateDirectory(output);
    var rows=LoadDataset(dataset);var previous=ReadCsv(old,f=>new Previous{Sha=f("Sha256"),Classification=f("ProductClassification")}).ToDictionary(x=>x.Sha,StringComparer.OrdinalIgnoreCase);
    if(rows.Count!=80||previous.Count!=80)throw new InvalidDataException("El corpus congelado debe contener 80 filas.");
    var runtime=Path.Combine(Path.GetTempPath(),"RecepcionDocumental-H1D8A-"+Guid.NewGuid().ToString("N"));
    try{Initialize(runtime);for(var i=0;i<rows.Count;i++){Console.WriteLine("H1D8A | "+(i+1)+"/"+rows.Count+" | "+rows[i].Sha);Analyze(rows[i],previous[rows[i].Sha]);}}finally{if(Directory.Exists(runtime))Directory.Delete(runtime,true);}
    Write(output,rows);var gate=Gate(rows);Console.WriteLine("H1D8A_FASE_A | Filas="+rows.Count+" | PDFs="+rows.Count(x=>x.Format=="PDF")+" | Gate="+gate+" | Output="+output);return gate?0:1;
   }
   catch(Exception ex){Console.Error.WriteLine("ERROR H1D8A | "+ex.GetType().Name+": "+ex.Message);return 1;}
  }

  static void Analyze(Row r,Previous previous)
  {
   r.FrozenCurrent=previous.Classification;
   if(r.Format=="PDF")
   {
    var mdoc=MdocPdfTextExtractor.Extract(r.Path);r.MdocText=mdoc.Text??"";var currentM=InvoiceSelector.SelectPdf(r.MdocText,mdoc.HasUsefulText);r.Embedded=MdocPdfQrDetector.Detect(r.Path);r.Raster=new ArcaQrEvidence();
    OcrResult ocr=null;using(var workspace=new AttachmentWorkspace()){var raster=PdfPageRasterizer.Rasterize(r.Path,workspace);r.RasterMs=raster.DurationMilliseconds;r.RasterPages=raster.PageCount;r.RasterFailure=raster.FailureReason;if(raster.Images.Count>0){r.Raster=DecodeRaster(raster.Images);ocr=DocumentOcrService.Recognize(raster.Images);if(ocr.Success){var header=DocumentOcrService.RecognizeHeader(raster.Images);if(header.Success)ocr=DocumentOcrService.Combine(ocr,header);}}}
    r.OcrText=ocr==null?"":ocr.Text??"";var currentO=InvoiceSelector.SelectOcrText(r.OcrText,ocr!=null&&ocr.HasUsefulText);var currentText=currentM.Classification=="REVISAR"?DocumentAnalysisService.FusePdfSelections(currentM,currentO):currentM;r.ComputedCurrent=ArcaQrDecoder.Combine(r.Embedded,currentText).Classification;
    var m=Evidence(r.MdocText,mdoc.HasUsefulText);var o=Evidence(r.OcrText,ocr!=null&&ocr.HasUsefulText);var candidate=Fuse(m,o);candidate=CombineQr(EffectiveQr(r),candidate);r.Candidate=candidate.Classification;r.Trace=candidate.Trace;
   }
   else
   {
    var ocr=DocumentOcrService.RecognizeImageFile(r.Path);if(ocr.Success){var header=DocumentOcrService.RecognizeImageHeader(r.Path);if(header.Success)ocr=DocumentOcrService.Combine(ocr,header);}r.OcrText=ocr.Text??"";r.ComputedCurrent=InvoiceSelector.SelectOcrText(r.OcrText,ocr.HasUsefulText).Classification;var candidate=Evidence(r.OcrText,ocr.HasUsefulText);r.Candidate=candidate.Classification;r.Trace=candidate.Trace;r.Embedded=new ArcaQrEvidence();r.Raster=new ArcaQrEvidence();
   }
  }

  static Candidate Evidence(string text,bool useful)
  {
   if(!useful)return new Candidate("REVISAR",false,false,"SIN_TEXTO_UTIL");var n=Normalize(text);var head=n.Substring(0,Math.Min(900,n.Length));var explicitInvoice=InvoiceTypes.FirstOrDefault(x=>Has(n,x));var fiscal=Fiscal.Count(x=>Has(n,x));var principalOther=OtherTypes.FirstOrDefault(x=>Has(head,x));
   var strongInvoice=explicitInvoice!=null&&fiscal>=1;var strongOther=principalOther!=null&&!strongInvoice;
   if(strongInvoice&&principalOther!=null)return new Candidate("FACTURA",true,false,"FACTURA_EXPLICITA+FISCAL;REFERENCIA_SECUNDARIA="+principalOther);
   if(strongInvoice)return new Candidate("FACTURA",true,false,"FACTURA_EXPLICITA+FISCAL="+fiscal);
   if(strongOther)return new Candidate("DESCARTAR",false,true,"TIPO_PRINCIPAL_OTRO="+principalOther);
   return new Candidate("REVISAR",false,false,explicitInvoice!=null?"FACTURA_EXPLICITA_SIN_FISCAL":"NO_CONCLUYENTE");
  }
  static Candidate Fuse(Candidate a,Candidate b)
  {
   if((a.StrongInvoice&&b.StrongOther)||(a.StrongOther&&b.StrongInvoice))return new Candidate("REVISAR",false,false,"CONFLICTO_FUERTE_MDoc_OCR;"+a.Trace+";"+b.Trace);
   if(a.StrongInvoice||b.StrongInvoice)return new Candidate("FACTURA",true,false,"PREVALECE_FACTURA_FUERTE;MDOC="+a.Trace+";OCR="+b.Trace);
   if(a.StrongOther&&b.StrongOther)return new Candidate("DESCARTAR",false,true,"OTRO_TIPO_FUERTE_COINCIDENTE;MDOC="+a.Trace+";OCR="+b.Trace);
   if(a.StrongOther||b.StrongOther)return new Candidate("REVISAR",false,false,"OTRO_TIPO_NO_CONFIRMADO_ENTRE_FUENTES;MDOC="+a.Trace+";OCR="+b.Trace);
   return new Candidate("REVISAR",false,false,"SIN_EVIDENCIA_FUERTE;MDOC="+a.Trace+";OCR="+b.Trace);
  }
  static Candidate CombineQr(ArcaQrEvidence qr,Candidate text)
  {
   if(qr==null||!qr.IsValid||!qr.TipoComprobante.HasValue)return text;var invoice=ArcaQrDecoder.IsInvoiceType(qr.TipoComprobante.Value);var other=ArcaQrDecoder.IsKnownNonInvoiceType(qr.TipoComprobante.Value);
   if(invoice&&text.StrongOther)return new Candidate("REVISAR",false,false,"CONFLICTO_QR_FACTURA_TEXTO_OTRO");if(other&&text.StrongInvoice)return new Candidate("REVISAR",false,false,"CONFLICTO_QR_OTRO_TEXTO_FACTURA");if(invoice)return new Candidate("FACTURA",true,false,"QR_ARCA_FACTURA");if(other)return new Candidate("DESCARTAR",false,true,"QR_ARCA_OTRO");return text;
  }
  static ArcaQrEvidence EffectiveQr(Row r){if(r.Embedded.IsValid&&r.Raster.IsValid&&r.Embedded.TipoComprobante!=r.Raster.TipoComprobante)return new ArcaQrEvidence{QrDetected=true};return r.Embedded.IsValid?r.Embedded:r.Raster;}

  static ArcaQrEvidence DecodeRaster(IEnumerable<OcrImageData> images)
  {
   var evidence=new ArcaQrEvidence();var reader=new BarcodeReaderGeneric{AutoRotate=true};reader.Options=new DecodingOptions{TryHarder=true,TryInverted=true,PossibleFormats=new List<BarcodeFormat>{BarcodeFormat.QR_CODE}};
   foreach(var item in images){using(var ms=new MemoryStream(item.Bytes,false))using(var source=new Bitmap(ms))using(var bitmap=new Bitmap(source.Width,source.Height,PixelFormat.Format24bppRgb)){using(var g=Graphics.FromImage(bitmap))g.DrawImageUnscaled(source,0,0);var data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);try{var bytes=new byte[Math.Abs(data.Stride)*data.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);var result=reader.Decode(bytes,bitmap.Width,bitmap.Height,RGBLuminanceSource.BitmapFormat.BGR24);if(result==null)continue;evidence.QrDetected=true;var arca=ArcaQrDecoder.Decode(result.Text);if(arca.IsValid)return arca;}finally{bitmap.UnlockBits(data);}}}return evidence;
  }

  static void Write(string o,List<Row> r)
  {
   var fiscal=new List<string>{"Sha256,Label,GroupId,PhysicalFormat,FrozenCurrentSelector,RecomputedCurrentSelector,CandidateSelector,Changed,CandidateTrace"};fiscal.AddRange(r.Select(x=>Join(x.Sha,x.Label,x.Group,x.Format,x.FrozenCurrent,x.ComputedCurrent,x.Candidate,B(x.FrozenCurrent!=x.Candidate),x.Trace)));File.WriteAllLines(Path.Combine(o,"fiscal-selector-comparison.csv"),fiscal,new UTF8Encoding(false));
   var qr=new List<string>{"Sha256,Label,EmbeddedQrDetected,EmbeddedArcaValid,EmbeddedTipoComprobante,RasterQrDetected,RasterArcaValid,RasterTipoComprobante,Difference,RasterPages,RasterQrDurationMs,RasterFailure"};qr.AddRange(r.Where(x=>x.Format=="PDF").Select(x=>Join(x.Sha,x.Label,B(x.Embedded.QrDetected),B(x.Embedded.IsValid),N(x.Embedded.TipoComprobante),B(x.Raster.QrDetected),B(x.Raster.IsValid),N(x.Raster.TipoComprobante),B(x.Embedded.QrDetected!=x.Raster.QrDetected||x.Embedded.IsValid!=x.Raster.IsValid||x.Embedded.TipoComprobante!=x.Raster.TipoComprobante),x.RasterPages.ToString(),x.RasterMs.ToString(),x.RasterFailure)));File.WriteAllLines(Path.Combine(o,"qr-source-comparison.csv"),qr,new UTF8Encoding(false));
   var b=r.Single(x=>x.Sha==B4C8);var historical=r.Where(x=>x.Trace.IndexOf("REMITO",StringComparison.OrdinalIgnoreCase)>=0||x.Trace.IndexOf("RECIBO",StringComparison.OrdinalIgnoreCase)>=0||x.Trace.IndexOf("ORDEN DE COMPRA",StringComparison.OrdinalIgnoreCase)>=0).ToList();File.WriteAllText(Path.Combine(o,"regression-cases.md"),"# Casos de regresión\n\n- B4C8: actual `"+b.FrozenCurrent+"`, candidata `"+b.Candidate+"`; embedded QR="+C(b.Embedded)+", raster QR="+C(b.Raster)+".\n- Casos con REMITO/RECIBO/ORDEN DE COMPRA en la traza candidata: "+historical.Count+".\n\n"+string.Join("\n",historical.Select(x=>"- "+x.Sha+" — label="+x.Label+", actual="+x.FrozenCurrent+", candidata="+x.Candidate+", "+x.Trace))+"\n",new UTF8Encoding(false));
   var curFalse=r.Count(x=>x.Label!="FACTURA"&&x.FrozenCurrent=="FACTURA");var candFalse=r.Count(x=>x.Label!="FACTURA"&&x.Candidate=="FACTURA");var invDiscard=r.Count(x=>x.Label=="FACTURA"&&x.Candidate=="DESCARTAR");var invReviewCurrent=r.Count(x=>x.Label=="FACTURA"&&x.FrozenCurrent=="REVISAR");var invReviewCandidate=r.Count(x=>x.Label=="FACTURA"&&x.Candidate=="REVISAR");var pdf=r.Where(x=>x.Format=="PDF").ToList();var rasterValid=pdf.Count(x=>x.Raster.IsValid);var embeddedValid=pdf.Count(x=>x.Embedded.IsValid);var gate=Gate(r);
   var metrics="# Métricas H1D8A — Fase A\n\n| Label | Selector | FACTURA | REVISAR | DESCARTAR |\n|---|---|---:|---:|---:|\n"+string.Join("\n",new[]{"FACTURA","OTRO_DOCUMENTO","NO_DOCUMENTO"}.SelectMany(l=>new[]{Line(r,l,"CURRENT",x=>x.FrozenCurrent),Line(r,l,"CANDIDATE",x=>x.Candidate)}))+"\n\n- FACTURA → DESCARTAR candidata: "+invDiscard+".\n- Falsos FACTURA actuales/candidata: "+curFalse+" / "+candFalse+".\n- FACTURA en REVISAR actuales/candidata: "+invReviewCurrent+" / "+invReviewCandidate+".\n- QR ARCA válido embedded/raster: "+embeddedValid+" / "+rasterValid+".\n- Costo raster total/media: "+pdf.Sum(x=>x.RasterMs)+" / "+pdf.Average(x=>x.RasterMs).ToString("0.##",CultureInfo.InvariantCulture)+" ms.\n- Gate Fase B: **"+gate+"**.\n";File.WriteAllText(Path.Combine(o,"metrics.md"),metrics,new UTF8Encoding(false));
   File.WriteAllText(Path.Combine(o,"resumen-fase-a.md"),"# H1D8A — resumen Fase A\n\n- Gate para integración productiva: **"+(gate?"APROBADO":"NO APROBADO")+"**.\n- B4C8: `"+b.FrozenCurrent+"` → `"+b.Candidate+"`.\n- FACTURA→DESCARTAR: "+invDiscard+"; falsos FACTURA: "+curFalse+" → "+candFalse+".\n- QR raster ARCA válidos: "+rasterValid+"; falsos QR ARCA válidos observados: "+r.Count(x=>x.Format=="PDF"&&x.Raster.IsValid&&x.Label=="NO_DOCUMENTO")+".\n- La imagen del helicóptero queda fuera de H1D8A y pendiente de un clasificador visual FACTURA/NO FACTURA real.\n- Fase A usa el corpus conocido como regresión experimental, no certificación independiente.\n",new UTF8Encoding(false));
  }
  static bool Gate(List<Row> r){var b=r.Single(x=>x.Sha==B4C8);return r.Count(x=>x.Label=="FACTURA"&&x.Candidate=="DESCARTAR")==0&&r.Count(x=>x.Label!="FACTURA"&&x.Candidate=="FACTURA")<=r.Count(x=>x.Label!="FACTURA"&&x.FrozenCurrent=="FACTURA")&&b.Candidate=="FACTURA"&&r.Count(x=>x.Format=="PDF"&&x.Raster.IsValid&&x.Label=="NO_DOCUMENTO")==0;}
  static string Line(List<Row>r,string l,string s,Func<Row,string>g){var q=r.Where(x=>x.Label==l);return"| "+l+" | "+s+" | "+q.Count(x=>g(x)=="FACTURA")+" | "+q.Count(x=>g(x)=="REVISAR")+" | "+q.Count(x=>g(x)=="DESCARTAR")+" |";}
  static void Initialize(string root){var c=new ConfiguracionAplicacion("RecepcionDocumental",Path.Combine(root,"Logs"),Path.Combine(root,"Trabajo"),Path.Combine(root,"Facturas"),Path.Combine(root,"Revisar"),100,25L*1024*1024,100L*1024*1024,3,"https://localhost/h1d8a");c.PrepararRutasOperativas();ConfiguracionSistema.Inicializar(c);Logs.Inicializar(c);}
  static List<Row> LoadDataset(string path){return ReadCsv(path,f=>{var p=f("Path");p=Path.IsPathRooted(p)?p:Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path),p));return new Row{Path=p,Label=f("Label"),Group=f("GroupId"),Sha=f("Sha256").ToUpperInvariant(),Format=Format(p)};});}
  static List<T> ReadCsv<T>(string p,Func<Func<string,string>,T> map){var z=new List<T>();using(var x=new TextFieldParser(p,Encoding.UTF8)){x.TextFieldType=FieldType.Delimited;x.HasFieldsEnclosedInQuotes=true;x.SetDelimiters(",");var h=x.ReadFields();var c=h.Select((n,i)=>new{n,i}).ToDictionary(y=>y.n,y=>y.i,StringComparer.OrdinalIgnoreCase);while(!x.EndOfData){var f=x.ReadFields();if(f!=null)z.Add(map(n=>c.ContainsKey(n)&&c[n]<f.Length?f[c[n]]:""));}}return z;}
  static bool Has(string n,string p){return(" "+n+" ").IndexOf(" "+p+" ",StringComparison.Ordinal)>=0;}static string Format(string p){using(var s=File.OpenRead(p)){var b=new byte[8];s.Read(b,0,8);if(b[0]==0x25&&b[1]==0x50)return"PDF";if(b[0]==0x89&&b[1]==0x50)return"PNG";if(b[0]==0xff&&b[1]==0xd8)return"JPEG";}throw new InvalidDataException("Formato no soportado.");}
  static string Normalize(string value){var d=(value??"").ToUpperInvariant().Normalize(NormalizationForm.FormD);var b=new StringBuilder(d.Length);foreach(var c in d)if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)b.Append(char.IsWhiteSpace(c)?' ':c);return string.Join(" ",b.ToString().Normalize(NormalizationForm.FormC).Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries));}
  static void Check(string p,string expected){if(Hash(p)!=expected)throw new InvalidDataException("SHA-256 inesperado para dataset.csv.");}static string Hash(string p){using(var s=SHA256.Create())using(var f=File.OpenRead(p))return BitConverter.ToString(s.ComputeHash(f)).Replace("-","");}
  static string Join(params string[]v){return string.Join(",",v.Select(x=>"\""+(x??"").Replace("\"","\"\"")+"\""));}static string B(bool x){return x?"true":"false";}static string N(int?x){return x.HasValue?x.Value.ToString():"";}static string C(ArcaQrEvidence x){return x.IsValid?"ARCA_VALIDO tipo="+N(x.TipoComprobante):x.QrDetected?"QR_NO_ARCA":"NO_DETECTADO";}
  sealed class Candidate{internal string Classification,Trace;internal bool StrongInvoice,StrongOther;internal Candidate(string c,bool i,bool o,string t){Classification=c;StrongInvoice=i;StrongOther=o;Trace=t;}}
  sealed class Previous{internal string Sha,Classification;}sealed class Row{internal string Path,Label,Group,Sha,Format,MdocText,OcrText,FrozenCurrent,ComputedCurrent,Candidate,Trace,RasterFailure;internal ArcaQrEvidence Embedded,Raster;internal int RasterMs,RasterPages;}
 }
}
