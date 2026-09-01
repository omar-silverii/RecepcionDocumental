using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
 internal static class H1D8BProductRegressionProbe
 {
  const string DatasetHash="AFECA7A2F995CE1B2DF6F9DAF3501A392CC7FB4DD1509900A19D1588E26794C2";
  const string B4C8="B4C8FA3786E8BA119F3C5F40F1BF5868D7591D0E865FF2AF84F7235674F33C88";const string E8066="E8066B5FA877947A47862B566C3A752FD1BA514973CD2B01A7AD31803BBDD28B";
  internal static int Run(string[] a)
  {
   if(a.Length!=4){Console.Error.WriteLine("Uso: --h1d8b-product-regression <dataset.csv> <H1D5C2-product-validation.csv> <output>");return 2;}
   try
   {
    var dataset=Path.GetFullPath(a[1]);var previousPath=Path.GetFullPath(a[2]);var output=Path.GetFullPath(a[3]);Check(dataset);var rows=Load(dataset);var previous=ReadCsv(previousPath,f=>new Old{Sha=f("Sha256"),Classification=f("ProductClassification")}).ToDictionary(x=>x.Sha,StringComparer.OrdinalIgnoreCase);if(rows.Count!=80||previous.Count!=80)throw new InvalidDataException("Se esperaban 80 documentos.");Directory.CreateDirectory(output);
    var runtime=Path.Combine(Path.GetTempPath(),"RecepcionDocumental-H1D8B-"+Guid.NewGuid().ToString("N"));try{Initialize(runtime);for(var i=0;i<rows.Count;i++){Console.WriteLine("H1D8B | "+(i+1)+"/"+rows.Count+" | "+rows[i].Sha);Analyze(rows[i],previous[rows[i].Sha].Classification);}}finally{if(Directory.Exists(runtime))Directory.Delete(runtime,true);}
    Write(output,rows);var ok=Gate(rows);Console.WriteLine("H1D8B | Filas="+rows.Count+" | Cambios="+rows.Count(x=>x.Old!=x.Now)+" | Gate="+ok+" | Output="+output);return ok?0:1;
   }catch(Exception ex){Console.Error.WriteLine("ERROR H1D8B | "+ex.GetType().Name+": "+ex.Message);return 1;}
  }
  static void Analyze(Row r,string old)
  {
   r.Old=old;using(var workspace=new AttachmentWorkspace()){var analysis=DocumentAnalysisService.Analyze(File.ReadAllBytes(r.Path),Path.GetFileName(r.Path),Mime(r.Format),workspace);var candidate=analysis.Candidates.FirstOrDefault();r.Now=candidate==null?"DESCARTAR":candidate.Selection.Classification;r.Method=candidate==null?"DESCARTADO_PRODUCTIVO":candidate.Selection.DetectionMethod;r.Confidence=candidate==null?(byte?)null:candidate.Selection.Confidence;if(candidate!=null){r.Embedded=candidate.EmbeddedQrDetected;r.Raster=candidate.RasterQrDetected;r.RasterValid=candidate.RasterQrArcaValid;r.Tipo=candidate.TipoComprobanteArca??candidate.RasterTipoComprobanteArca;r.QrSource=candidate.QrSource;r.DecodeMs=candidate.RasterQrDurationMilliseconds;r.Rasterizations=candidate.PdfRasterizationCount;}}
  }
  static bool Gate(List<Row> r)
  {
   var b=r.Single(x=>x.Sha==B4C8);var e=r.Single(x=>x.Sha==E8066);return r.Count==80&&b.Now=="FACTURA"&&e.Now=="FACTURA"&&r.All(x=>x.Label!="FACTURA"||x.Now!="DESCARTAR")&&r.All(x=>x.Label=="FACTURA"||x.Now!="FACTURA"||(x.RasterValid&&x.Tipo.HasValue&&ArcaQrDecoder.IsInvoiceType(x.Tipo.Value)))&&r.All(x=>x.Label!="NO_DOCUMENTO"||x.Now!="FACTURA")&&r.Where(x=>x.Label=="FACTURA"&&x.Old=="FACTURA").All(x=>x.Now=="FACTURA")&&r.All(x=>x.Rasterizations<=1);
  }
  static void Write(string o,List<Row> r)
  {
   var lines=new List<string>{"Sha256,Label,H1D5C2,H1D8B,Changed,EmbeddedQr,RasterQr,TipoCmp,DetectionMethod,Confidence"};lines.AddRange(r.Select(x=>Join(x.Sha,x.Label,x.Old,x.Now,B(x.Old!=x.Now),B(x.Embedded),B(x.Raster),N(x.Tipo),x.Method,x.Confidence.HasValue?x.Confidence.Value.ToString():"")));File.WriteAllLines(Path.Combine(o,"product-regression.csv"),lines,new UTF8Encoding(false));
   var changed=r.Where(x=>x.Old!=x.Now).ToList();var ocr=r.Where(x=>x.Rasterizations==1).ToList();var qr="# Regresión QR H1D8B\n\n- PDF con raster OCR reutilizado: "+ocr.Count+".\n- Segunda rasterización por QR: **"+r.Count(x=>x.Rasterizations>1)+"**.\n- QR raster detectados/válidos: "+r.Count(x=>x.Raster)+" / "+r.Count(x=>x.RasterValid)+".\n- Tiempo ZXing adicional total/media/P95: "+ocr.Sum(x=>x.DecodeMs)+" / "+D(ocr.Count==0?0:ocr.Average(x=>x.DecodeMs))+" / "+D(P(ocr.Select(x=>x.DecodeMs).OrderBy(x=>x).ToList(),.95))+" ms.\n- Diferencias de clasificación: "+changed.Count+".\n\n"+string.Join("\n",changed.Select(x=>"- "+x.Sha+": "+x.Old+" → "+x.Now+"; QR="+x.QrSource+"; tipoCmp="+N(x.Tipo)+"."))+"\n";File.WriteAllText(Path.Combine(o,"qr-regression.md"),qr,new UTF8Encoding(false));
   var invDiscard=r.Count(x=>x.Label=="FACTURA"&&x.Now=="DESCARTAR");var falseInv=r.Count(x=>x.Label!="FACTURA"&&x.Now=="FACTURA");var regress=r.Count(x=>x.Label=="FACTURA"&&x.Old=="FACTURA"&&x.Now!="FACTURA");var b=r.Single(x=>x.Sha==B4C8);var e=r.Single(x=>x.Sha==E8066);var gate=Gate(r);File.WriteAllText(Path.Combine(o,"metrics.md"),"# Métricas H1D8B\n\n- Procesados: "+r.Count+"/80.\n- FACTURA→DESCARTAR: "+invDiscard+".\n- Falsos FACTURA: "+falseInv+".\n- Regresiones de FACTURA previamente correctas: "+regress+".\n- B4C8: "+b.Old+" → "+b.Now+"; método="+b.Method+", confianza="+(b.Confidence.HasValue?b.Confidence.Value.ToString():"NULL")+", QR="+b.QrSource+", tipoCmp="+N(b.Tipo)+".\n- E8066: "+e.Now+".\n- Cambios totales: "+changed.Count+".\n- Gate: **"+gate+"**.\n",new UTF8Encoding(false));
   File.WriteAllText(Path.Combine(o,"resumen.md"),"# H1D8B — QR ARCA raster integrado\n\n**"+(gate?"APROBADO":"NO APROBADO")+"**\n\n- La clasificación textual permanece H1D5C2; `InvoiceSelector` y `FusePdfSelections` no cambiaron.\n- El mismo raster alimenta OCR y QR; rasterizaciones adicionales por QR: "+r.Count(x=>x.Rasterizations>1)+".\n- B4C8: "+b.Old+" → "+b.Now+" mediante "+b.Method+".\n- No se ejecutó Gmail ni se escribió SQL.\n- El problema visual FACTURA/NO FACTURA, incluido helicóptero, queda fuera de este hito.\n",new UTF8Encoding(false));
  }
  static double P(List<int>x,double p){if(x.Count==0)return 0;var rank=(x.Count-1)*p;var lo=(int)Math.Floor(rank);var hi=(int)Math.Ceiling(rank);return x[lo]+(x[hi]-x[lo])*(rank-lo);}static string D(double x){return x.ToString("0.##",CultureInfo.InvariantCulture);}
  static void Initialize(string root){var c=new ConfiguracionAplicacion("RecepcionDocumental",Path.Combine(root,"Logs"),Path.Combine(root,"Trabajo"),Path.Combine(root,"Facturas"),Path.Combine(root,"Revisar"),100,25L*1024*1024,100L*1024*1024,3,"https://localhost/h1d8b");c.PrepararRutasOperativas();ConfiguracionSistema.Inicializar(c);Logs.Inicializar(c);}
  static List<Row>Load(string p){return ReadCsv(p,f=>{var x=f("Path");x=Path.IsPathRooted(x)?x:Path.GetFullPath(Path.Combine(Path.GetDirectoryName(p),x));return new Row{Path=x,Sha=f("Sha256").ToUpperInvariant(),Label=f("Label"),Format=Format(x)};});}static List<T>ReadCsv<T>(string p,Func<Func<string,string>,T>m){var z=new List<T>();using(var x=new TextFieldParser(p,Encoding.UTF8)){x.TextFieldType=FieldType.Delimited;x.HasFieldsEnclosedInQuotes=true;x.SetDelimiters(",");var h=x.ReadFields();var c=h.Select((n,i)=>new{n,i}).ToDictionary(y=>y.n,y=>y.i,StringComparer.OrdinalIgnoreCase);while(!x.EndOfData){var f=x.ReadFields();if(f!=null)z.Add(m(n=>c.ContainsKey(n)&&c[n]<f.Length?f[c[n]]:""));}}return z;}
  static string Format(string p){using(var s=File.OpenRead(p)){var b=new byte[8];s.Read(b,0,8);if(b[0]==0x25&&b[1]==0x50)return"PDF";if(b[0]==0x89&&b[1]==0x50)return"PNG";if(b[0]==0xff&&b[1]==0xd8)return"JPEG";}throw new InvalidDataException("Formato no soportado.");}static string Mime(string f){return f=="PDF"?"application/pdf":f=="PNG"?"image/png":"image/jpeg";}
  static void Check(string p){using(var s=SHA256.Create())using(var f=File.OpenRead(p)){var h=BitConverter.ToString(s.ComputeHash(f)).Replace("-","");if(h!=DatasetHash)throw new InvalidDataException("SHA dataset inesperado.");}}static string Join(params string[]v){return string.Join(",",v.Select(x=>"\""+(x??"").Replace("\"","\"\"")+"\""));}static string B(bool x){return x?"true":"false";}static string N(int?x){return x.HasValue?x.Value.ToString():"";}
  sealed class Old{internal string Sha,Classification;}sealed class Row{internal string Path,Sha,Label,Format,Old,Now,Method,QrSource;internal bool Embedded,Raster,RasterValid;internal byte? Confidence;internal int? Tipo;internal int DecodeMs,Rasterizations;}
 }
}
