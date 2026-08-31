using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    internal static class H1D5C1OcrSourceBenchmarkProbe
    {
        private const string DatasetHash="AF1FCC230B279D7DA4ACD77F5641BB1835D7D42EA0E8A41AD524D473FEC48158";
        private const string FusionHash="B7E823012762FE917D6E5C73F122731FE6180E96D7560755F68FB7A552FFA604";
        private const string ProductHash="C861AAEB73711A29A6082CCF6371187F164312177C32D908DEFB8C25E9BED293";
        private const string SanityE8066="E8066B5FA877947A47862B566C3A752FD1BA514973CD2B01A7AD31803BBDD28B";
        private const string SanityRemito="B4C8FA3786E8BA119F3C5F40F1BF5868D7591D0E865FF2AF84F7235674F33C88";

        internal static int Run(string[] args)
        {
            if(args.Length!=5){Console.Error.WriteLine("Uso: --h1d5c1-ocr-source-benchmark <dataset.csv> <H1D5B-fusion-results.csv> <H1D5C-product-validation.csv> <output-H1D5C1>");return 2;}
            string runtime=null;
            try
            {
                var datasetPath=Path.GetFullPath(args[1]);var fusionPath=Path.GetFullPath(args[2]);var productPath=Path.GetFullPath(args[3]);var output=Path.GetFullPath(args[4]);
                CheckHash(datasetPath,DatasetHash,"dataset.csv");CheckHash(fusionPath,FusionHash,"fusion-results.csv");CheckHash(productPath,ProductHash,"product-validation.csv");
                var dataset=LoadDataset(datasetPath);var fusion=LoadFusion(fusionPath);var product=LoadProduct(productPath);Validate(dataset,fusion,product);
                Directory.CreateDirectory(output);runtime=Path.Combine(Path.GetTempPath(),"RecepcionDocumental-H1D5C1-"+Guid.NewGuid().ToString("N"));InitializeRuntime(runtime);
                var rows=new List<Row>();var images=new List<ImageRow>();var pdfs=fusion.Values.Where(x=>x.Format=="PDF").OrderBy(x=>dataset[x.Sha].Order).ToList();
                foreach(var f in pdfs)
                {
                    Console.WriteLine("H1D5C1 | "+(rows.Count+1)+"/36 | "+f.Sha);
                    rows.Add(Benchmark(dataset[f.Sha],f,product[f.Sha],images));
                }
                WriteResults(Path.Combine(output,"ocr-source-results.csv"),rows);WriteImages(Path.Combine(output,"embedded-images.csv"),images);WriteDifferences(Path.Combine(output,"source-differences.csv"),rows);WritePolicies(Path.Combine(output,"policy-metrics.md"),rows);WriteCosts(Path.Combine(output,"cost-metrics.csv"),rows);WriteSummary(Path.Combine(output,"resumen.md"),rows);
                var primary=rows.Where(x=>x.OcrRequired).ToList();Console.WriteLine("H1D5C1_COMPLETO | PDF=36 | OCR="+primary.Count+" | PDFConImagenes="+rows.Count(x=>x.EmbeddedCount>0)+" | DiferenciasFuente="+primary.Count(SourceDiff)+" | MatchP0="+rows.Count(x=>x.P0==x.Expected)+"/36 | MatchP1="+rows.Count(x=>x.P1==x.Expected)+"/36 | MatchP2="+rows.Count(x=>x.P2==x.Expected)+"/36 | MatchP3="+rows.Count(x=>x.P3==x.Expected)+"/36 | Output="+output);
                return 0;
            }
            catch(Exception ex){Console.Error.WriteLine("ERROR H1D5C1 | "+ex.GetType().Name+": "+ex.Message);return 1;}
            finally{if(runtime!=null&&Directory.Exists(runtime))Directory.Delete(runtime,true);}
        }

        private static void InitializeRuntime(string root){var c=new ConfiguracionAplicacion("RecepcionDocumental",Path.Combine(root,"Logs"),Path.Combine(root,"Trabajo"),Path.Combine(root,"Facturas"),Path.Combine(root,"Revisar"),100,25L*1024*1024,100L*1024*1024,3,"https://localhost/h1d5c1");c.PrepararRutasOperativas();ConfiguracionSistema.Inicializar(c);Logs.Inicializar(c);}

        private static Row Benchmark(DatasetRow d,FusionRow f,ProductRow p,List<ImageRow> inventory)
        {
            if(Hash(d.Path)!=d.Sha)throw new InvalidDataException("Hash físico distinto: "+d.Sha);
            var mdoc=MdocPdfTextExtractor.Extract(d.Path);var mdocSelection=InvoiceSelector.SelectPdf(mdoc.Text,mdoc.HasUsefulText);var qr=MdocPdfQrDetector.Detect(d.Path);
            var ew=Stopwatch.StartNew();var extraction=MdocPdfImageExtractor.Extract(d.Path);ew.Stop();
            for(var i=0;i<extraction.Images.Count;i++){var image=extraction.Images[i];inventory.Add(new ImageRow{Sha=d.Sha,Index=i+1,Width=image.Width,Height=image.Height,Format=DetectBytesFormat(image.Bytes),Bytes=image.Bytes==null?0:image.Bytes.Length});}
            var row=new Row{Sha=d.Sha,Label=d.Label,GroupId=d.GroupId,MdocUseful=mdoc.HasUsefulText,Mdoc=mdocSelection,MdocReason=mdocSelection.Reason,OcrRequired=f.ConservativeOcr,EmbeddedCount=extraction.Images.Count,EmbeddedDimensions=string.Join(";",extraction.Images.Select(x=>x.Width+"x"+x.Height)),EmbeddedExtractionMs=(int)Math.Min(int.MaxValue,ew.ElapsedMilliseconds),EmbeddedLimit=extraction.LimitExceeded,EmbeddedFailure=extraction.FailureReason,Expected=f.Expected,Actual=p.Actual,QrValid=qr.IsValid,Tipo=qr.TipoComprobante};
            if(row.OcrRequired)
            {
                row.Embedded=extraction.LimitExceeded?SourceReview("OCR_LIMITE",extraction.FailureReason):extraction.Images.Count>0?Recognize(extraction.Images,"EMBEDDED"):SourceReview("SIN_IMAGEN_EMBEBIDA",extraction.FailureReason);
                using(var workspace=new AttachmentWorkspace())row.Raster=RasterizeAndRecognize(d.Path,workspace);
            }
            Decide(row,mdocSelection,qr);return row;
        }

        private static SourceResult RasterizeAndRecognize(string path,AttachmentWorkspace workspace)
        {
            var raster=PdfPageRasterizer.Rasterize(path,workspace);if(raster.LimitExceeded)return new SourceResult{Selection=InvoiceSelector.Review("OCR_LIMITE",raster.FailureReason,null),DurationMs=raster.DurationMilliseconds,RasterMs=raster.DurationMilliseconds,ImageCount=0,PageCount=raster.PageCount,Limit=true,Failure=raster.FailureReason};
            if(raster.Images.Count==0)return new SourceResult{Selection=InvoiceSelector.Review("OCR_RENDER_ERROR",raster.FailureReason??"No se pudo rasterizar.",null),DurationMs=raster.DurationMilliseconds,RasterMs=raster.DurationMilliseconds,PageCount=raster.PageCount,Failure=raster.FailureReason,Structural=raster.StructuralFailure};
            var result=Recognize(raster.Images,"RASTER");result.RasterMs=raster.DurationMilliseconds;result.DurationMs+=raster.DurationMilliseconds;result.PageCount=raster.PageCount;return result;
        }

        private static SourceResult Recognize(IEnumerable<OcrImageData> source,string type)
        {
            var images=source.ToList();var ocr=DocumentOcrService.Recognize(images);var result=new SourceResult{OcrExecuted=true,Success=ocr.Success,DurationMs=ocr.DurationMilliseconds,OcrMs=ocr.DurationMilliseconds,ImageCount=images.Count,Failure=ocr.FailureReason};
            if(!ocr.Success){result.Selection=InvoiceSelector.Review("OCR_ERROR",ocr.FailureReason??"OCR no disponible.",null);return result;}
            result.Selection=InvoiceSelector.SelectOcrText(ocr.Text,ocr.HasUsefulText);if(result.Selection.Classification!="REVISAR")return result;
            var header=DocumentOcrService.RecognizeHeader(images);result.HeaderUsed=true;result.HeaderMs=header.DurationMilliseconds;result.DurationMs+=header.DurationMilliseconds;if(header.Success){var combined=DocumentOcrService.Combine(ocr,header);result.Selection=InvoiceSelector.SelectOcrText(combined.Text,combined.HasUsefulText);}return result;
        }
        private static SourceResult SourceReview(string method,string reason){return new SourceResult{Selection=InvoiceSelector.Review(method,reason??"Fuente no disponible.",null),Failure=reason};}

        private static void Decide(Row r,InvoiceSelection mdoc,ArcaQrEvidence qr)
        {
            if(!r.OcrRequired){var final=ArcaQrDecoder.Combine(qr,mdoc).Classification;r.P0=r.P1=r.P2=r.P3=final;r.TraceP0=r.TraceP1=r.TraceP2=r.TraceP3="MDOC_CONCLUYENTE";return;}
            var embeddedAvailable=r.EmbeddedCount>0&&!r.EmbeddedLimit;
            var p0Evidence=embeddedAvailable?r.Embedded.Selection:r.EmbeddedLimit?r.Embedded.Selection:r.Raster.Selection;r.P0=Final(qr,mdoc,p0Evidence);r.TraceP0=embeddedAvailable?"EMBEDDED":"RASTER_FALLBACK_SIN_EMBEDDED";
            r.P1=Final(qr,mdoc,r.Raster.Selection);r.TraceP1="FULL_PAGE_RASTER";
            var p2Raster=!embeddedAvailable||r.Embedded.Selection.Classification=="REVISAR";var p2Evidence=p2Raster?r.Raster.Selection:r.Embedded.Selection;r.P2=Final(qr,mdoc,p2Evidence);r.TraceP2=p2Raster?(embeddedAvailable?"EMBEDDED_REVISAR→RASTER":"RASTER_SIN_EMBEDDED"):"EMBEDDED_CONCLUYENTE";
            var p3Raster=!embeddedAvailable||r.Embedded.Selection.Classification!="FACTURA";InvoiceSelection p3Evidence;if(!p3Raster)p3Evidence=r.Embedded.Selection;else if(r.Raster.Selection.Classification=="FACTURA")p3Evidence=r.Raster.Selection;else p3Evidence=InvoiceSelector.Review("OCR_SIN_EVIDENCIA_POSITIVA","Ninguna fuente OCR produjo evidencia positiva de factura.",null);r.P3=Final(qr,mdoc,p3Evidence);r.TraceP3=!p3Raster?"EMBEDDED_FACTURA":r.Raster.Selection.Classification=="FACTURA"?"RASTER_RESCATA_FACTURA":"SIN_EVIDENCIA_POSITIVA";
            r.P0Cost=r.EmbeddedExtractionMs+(embeddedAvailable?r.Embedded.DurationMs:r.Raster.DurationMs);r.P1Cost=r.Raster.DurationMs;r.P2Cost=r.EmbeddedExtractionMs+(embeddedAvailable?r.Embedded.DurationMs:0)+(p2Raster?r.Raster.DurationMs:0);r.P3Cost=r.EmbeddedExtractionMs+(embeddedAvailable?r.Embedded.DurationMs:0)+(p3Raster?r.Raster.DurationMs:0);
            r.P0Embedded=embeddedAvailable?1:0;r.P0Raster=embeddedAvailable?0:1;r.P1Raster=1;r.P2Embedded=embeddedAvailable?1:0;r.P2Raster=p2Raster?1:0;r.P3Embedded=embeddedAvailable?1:0;r.P3Raster=p3Raster?1:0;
            r.P0Headers=(embeddedAvailable?r.Embedded.HeaderUsed:r.Raster.HeaderUsed)?1:0;r.P1Headers=r.Raster.HeaderUsed?1:0;r.P2Headers=(embeddedAvailable&&r.Embedded.HeaderUsed?1:0)+(p2Raster&&r.Raster.HeaderUsed?1:0);r.P3Headers=(embeddedAvailable&&r.Embedded.HeaderUsed?1:0)+(p3Raster&&r.Raster.HeaderUsed?1:0);
        }
        private static string Final(ArcaQrEvidence qr,InvoiceSelection mdoc,InvoiceSelection ocr){return ArcaQrDecoder.Combine(qr,DocumentAnalysisService.FusePdfSelections(mdoc,ocr)).Classification;}

        private static void WriteResults(string path,List<Row> rows)
        {
            var h="Sha256,Label,GroupId,MdocHasUsefulText,MdocClassification,MdocMethod,MdocReason,OcrRequired,EmbeddedImageCount,EmbeddedImageDimensions,EmbeddedExtractionMs,EmbeddedOcrClassification,EmbeddedOcrMethod,EmbeddedOcrConfidence,EmbeddedOcrDurationMs,EmbeddedHeaderUsed,EmbeddedLimit,EmbeddedFailure,RasterPageCount,RasterImageCount,RasterOcrClassification,RasterOcrMethod,RasterOcrConfidence,RasterTotalDurationMs,RasterOcrDurationMs,RasterHeaderUsed,RasterLimit,RasterStructuralFailure,RasterFailure,QrValid,TipoComprobanteArca,P0Classification,P1Classification,P2Classification,P3Classification,H1D5BExpected,H1D5CActual,P0Trace,P1Trace,P2Trace,P3Trace";var lines=new List<string>{h};lines.AddRange(rows.Select(r=>Join(r.Sha,r.Label,r.GroupId,B(r.MdocUseful),C(r.Mdoc),M(r.Mdoc),r.MdocReason,B(r.OcrRequired),N(r.EmbeddedCount),r.EmbeddedDimensions,N(r.EmbeddedExtractionMs),C(r.Embedded),M(r.Embedded),F(r.Embedded),N(Dur(r.Embedded)),B(Hdr(r.Embedded)),B(r.EmbeddedLimit),r.EmbeddedFailure,N(Page(r.Raster)),N(Count(r.Raster)),C(r.Raster),M(r.Raster),F(r.Raster),N(Dur(r.Raster)),N(OcrDur(r.Raster)),B(Hdr(r.Raster)),B(Lim(r.Raster)),B(Structural(r.Raster)),Failure(r.Raster),B(r.QrValid),r.Tipo.HasValue?N(r.Tipo.Value):"",r.P0,r.P1,r.P2,r.P3,r.Expected,r.Actual,r.TraceP0,r.TraceP1,r.TraceP2,r.TraceP3)));File.WriteAllLines(path,lines,new UTF8Encoding(false));
        }
        private static void WriteImages(string path,List<ImageRow> rows){var lines=new List<string>{"Sha256,ImageIndex,Width,Height,Format,Bytes"};lines.AddRange(rows.Select(x=>Join(x.Sha,N(x.Index),N(x.Width),N(x.Height),x.Format,N(x.Bytes))));File.WriteAllLines(path,lines,new UTF8Encoding(false));}
        private static void WriteDifferences(string path,List<Row> rows){var lines=new List<string>{"Sha256,Label,GroupId,EmbeddedClassification,EmbeddedMethod,RasterClassification,RasterMethod,FinalClassificationSame,TechnicalTrace"};foreach(var r in rows.Where(x=>x.OcrRequired&&SourceDiff(x)))lines.Add(Join(r.Sha,r.Label,r.GroupId,C(r.Embedded),M(r.Embedded),C(r.Raster),M(r.Raster),B(r.P0==r.P1),"La fuente OCR difiere; igualdad final no implica igualdad de evidencia."));File.WriteAllLines(path,lines,new UTF8Encoding(false));}
        private static void WritePolicies(string path,List<Row> rows)
        {
            var primary=rows.Where(x=>x.OcrRequired).ToList();var sb=new StringBuilder("# H1D5C1 — Comparación de políticas OCR PDF\n\n> Benchmark experimental. Label se usa sólo después de calcular P0–P3.\n\n");foreach(var p in Policies()){sb.AppendLine("## "+p.Name+"\n");sb.AppendLine("### Universo primario: 21 PDF OCR\n");AppendMatrix(sb,primary,p.Get);sb.AppendLine("### Vista de 36 PDF\n");AppendMatrix(sb,rows,p.Get);sb.AppendLine("- Coincidencia H1D5B: "+rows.Count(x=>p.Get(x)==x.Expected)+"/36.");sb.AppendLine("- `FACTURA → DESCARTAR`: "+rows.Count(x=>x.Label=="FACTURA"&&p.Get(x)=="DESCARTAR")+".");sb.AppendLine("- `OTRO_DOCUMENTO → FACTURA`: "+rows.Count(x=>x.Label=="OTRO_DOCUMENTO"&&p.Get(x)=="FACTURA")+".\n");}
            var e=rows.Single(x=>x.Sha==SanityE8066);var b=rows.Single(x=>x.Sha==SanityRemito);sb.AppendLine("## Sanity checks\n").AppendLine("- E8066: embebidas="+e.EmbeddedCount+" ("+e.EmbeddedDimensions+"), embedded="+C(e.Embedded)+", raster="+C(e.Raster)+", P0/P1/P2/P3="+e.P0+"/"+e.P1+"/"+e.P2+"/"+e.P3+".").AppendLine("- REMITO B4C8: embedded="+C(b.Embedded)+" ("+M(b.Embedded)+"), raster="+C(b.Raster)+" ("+M(b.Raster)+"), clasificación final P0/P1="+b.P0+"/"+b.P1+".");File.WriteAllText(path,sb.ToString(),new UTF8Encoding(false));
        }
        private static void WriteCosts(string path,List<Row> rows){var lines=new List<string>{"Policy,EmbeddedOcrDocuments,FullPageRasterizations,HeaderPasses,TotalDurationMs,MeanDurationMs,MedianDurationMs,P95DurationMs,LimitsOrFailures"};foreach(var p in Policies()){var costs=rows.Where(x=>x.OcrRequired).Select(p.Cost).OrderBy(x=>x).ToList();lines.Add(Join(p.Name,N(rows.Sum(p.Embedded)),N(rows.Sum(p.Raster)),N(rows.Sum(p.Headers)),N(costs.Sum()),D(costs.Average()),D(P(costs,.5)),D(P(costs,.95)),N(PolicyFailures(rows,p))));}File.WriteAllLines(path,lines,new UTF8Encoding(false));}
        private static void WriteSummary(string path,List<Row> rows)
        {
            var primary=rows.Where(x=>x.OcrRequired).ToList();var candidates=Policies().Where(p=>rows.All(x=>p.Get(x)==x.Expected)&&rows.Count(x=>x.Label=="FACTURA"&&p.Get(x)=="DESCARTAR")==0&&rows.Count(x=>x.Label=="OTRO_DOCUMENTO"&&p.Get(x)=="FACTURA")==0).ToList();var recommended=candidates.OrderBy(p=>rows.Sum(p.Raster)).ThenBy(p=>primary.Sum(p.Cost)).FirstOrDefault();var e=rows.Single(x=>x.Sha==SanityE8066);
            var embeddedPositive=primary.Where(x=>x.EmbeddedCount>0&&C(x.Embedded)=="FACTURA").ToList();var sb=new StringBuilder("# H1D5C1 — Resumen ejecutivo\n\n> H1D5C continúa no aprobado. No se implementó ninguna política productiva.\n\n");sb.AppendLine("1. PDF con imágenes Mdoc utilizables: **"+rows.Count(x=>x.EmbeddedCount>0)+"/36**; dentro del universo OCR: **"+primary.Count(x=>x.EmbeddedCount>0)+"/21**.");sb.AppendLine("2. Casos OCR con ambas fuentes y distinta clasificación embedded/raster: **"+primary.Count(ClassificationDiff)+"/21** ("+primary.Count(ClassificationDiff)+"/"+primary.Count(x=>x.EmbeddedCount>0)+" entre los que tienen fuente embebida).");foreach(var p in Policies().Skip(1))sb.AppendLine((p.Name=="P1_FULL_PAGE_RASTER"?"3":p.Name=="P2_EMBEDDED_THEN_RASTER_IF_REVIEW"?"4":"5")+". "+p.Name+" reproduce H1D5B: **"+rows.Count(x=>p.Get(x)==x.Expected)+"/36**.");sb.AppendLine("6. Políticas con FACTURA→DESCARTAR: "+string.Join(", ",Policies().Where(p=>rows.Any(x=>x.Label=="FACTURA"&&p.Get(x)=="DESCARTAR")).Select(x=>x.Name).DefaultIfEmpty("ninguna"))+".");sb.AppendLine("7. Políticas con falsos FACTURA: "+string.Join(", ",Policies().Where(p=>rows.Any(x=>x.Label=="OTRO_DOCUMENTO"&&p.Get(x)=="FACTURA")).Select(x=>x.Name).DefaultIfEmpty("ninguna"))+".");sb.AppendLine("8. Rasterizaciones P2/P3: "+rows.Sum(x=>x.P2Raster)+"/"+rows.Sum(x=>x.P3Raster)+".");sb.AppendLine("9. Costos medidos completos en `cost-metrics.csv`; incluyen extracción embebida, OCR y rasterización según cada política.");sb.AppendLine("10. Ventaja de P3 al aceptar FACTURA embebida sin raster: **no evaluable en este corpus**, porque hubo "+embeddedPositive.Count+" casos de FACTURA embedded entre los 21 PDF OCR; P3 resultó idéntica a P2.");sb.AppendLine("11. Casos con límites raster controlados: "+primary.Count(x=>x.Raster!=null&&x.Raster.Limit)+".");sb.AppendLine("12. Evidencia para H1D5C2: "+(recommended==null?"insuficiente; ninguna política satisface todos los criterios.":"sí; candidata **"+recommended.Name+"**, la más simple entre las que logran 36/36 sin descartes de FACTURA ni falsos FACTURA.")+"");sb.AppendLine().AppendLine("E8066 se resolvió sin excepción por hash en P1/P2/P3: embedded="+C(e.Embedded)+", raster="+C(e.Raster)+", resultados="+e.P1+"/"+e.P2+"/"+e.P3+".");File.WriteAllText(path,sb.ToString(),new UTF8Encoding(false));
        }

        private static void AppendMatrix(StringBuilder sb,List<Row> rows,Func<Row,string> get){sb.AppendLine("| Label | FACTURA | REVISAR | DESCARTAR |").AppendLine("|---|---:|---:|---:|");foreach(var l in new[]{"FACTURA","OTRO_DOCUMENTO"}){var q=rows.Where(x=>x.Label==l);sb.AppendLine("| "+l+" | "+q.Count(x=>get(x)=="FACTURA")+" | "+q.Count(x=>get(x)=="REVISAR")+" | "+q.Count(x=>get(x)=="DESCARTAR")+" |");}sb.AppendLine();}
        private static List<Policy> Policies(){return new List<Policy>{new Policy("P0_PRODUCT_EMBEDDED_FIRST",x=>x.P0,x=>x.P0Cost,x=>x.P0Embedded,x=>x.P0Raster,x=>x.P0Headers),new Policy("P1_FULL_PAGE_RASTER",x=>x.P1,x=>x.P1Cost,x=>0,x=>x.P1Raster,x=>x.P1Headers),new Policy("P2_EMBEDDED_THEN_RASTER_IF_REVIEW",x=>x.P2,x=>x.P2Cost,x=>x.P2Embedded,x=>x.P2Raster,x=>x.P2Headers),new Policy("P3_POSITIVE_RESCUE",x=>x.P3,x=>x.P3Cost,x=>x.P3Embedded,x=>x.P3Raster,x=>x.P3Headers)};}
        private static int PolicyFailures(List<Row> rows,Policy p){return rows.Count(x=>x.OcrRequired&&((p.Raster(x)>0&&(x.Raster.Limit||!string.IsNullOrEmpty(x.Raster.Failure)))||(p.Embedded(x)>0&&(x.EmbeddedLimit||!string.IsNullOrEmpty(x.Embedded.Failure)))));}
        private static bool SourceDiff(Row r){return r.EmbeddedCount>0&&!r.EmbeddedLimit&&(C(r.Embedded)!=C(r.Raster)||M(r.Embedded)!=M(r.Raster));}
        private static bool ClassificationDiff(Row r){return r.EmbeddedCount>0&&!r.EmbeddedLimit&&C(r.Embedded)!=C(r.Raster);}

        private static Dictionary<string,DatasetRow> LoadDataset(string path){var b=Path.GetDirectoryName(path);var n=0;return ReadCsv(path,f=>{var s=f("Path");return new DatasetRow{Order=n++,Sha=f("Sha256").ToUpperInvariant(),Path=Path.IsPathRooted(s)?s:Path.GetFullPath(Path.Combine(b,s)),Label=f("Label"),GroupId=f("GroupId")};}).ToDictionary(x=>x.Sha);}
        private static Dictionary<string,FusionRow> LoadFusion(string path){return ReadCsv(path,f=>new FusionRow{Sha=f("Sha256").ToUpperInvariant(),Label=f("Label"),GroupId=f("GroupId"),Format=f("PhysicalFormat"),ConservativeOcr=Bool(f("ConservativeWouldRunOcr")),Expected=f("FusionThenQrClassification")}).ToDictionary(x=>x.Sha);}
        private static Dictionary<string,ProductRow> LoadProduct(string path){return ReadCsv(path,f=>new ProductRow{Sha=f("Sha256").ToUpperInvariant(),Actual=f("ProductClassification")}).ToDictionary(x=>x.Sha);}
        private static void Validate(Dictionary<string,DatasetRow>d,Dictionary<string,FusionRow>f,Dictionary<string,ProductRow>p){if(d.Count!=80||f.Count!=80||p.Count!=80||f.Values.Count(x=>x.Format=="PDF")!=36||f.Values.Count(x=>x.Format=="PDF"&&x.ConservativeOcr)!=21)throw new InvalidDataException("Conteos congelados inesperados.");foreach(var x in f.Values){DatasetRow y;ProductRow z;if(!d.TryGetValue(x.Sha,out y)||!p.TryGetValue(x.Sha,out z)||x.Label!=y.Label||x.GroupId!=y.GroupId)throw new InvalidDataException("Entradas no coinciden: "+x.Sha);}}
        private static void CheckHash(string path,string expected,string name){var actual=Hash(path);if(actual!=expected)throw new InvalidDataException("SHA-256 inesperado para "+name+": "+actual);}
        private static List<T> ReadCsv<T>(string path,Func<Func<string,string>,T> map){var r=new List<T>();using(var p=new TextFieldParser(path,Encoding.UTF8)){p.TextFieldType=FieldType.Delimited;p.HasFieldsEnclosedInQuotes=true;p.SetDelimiters(",");var h=p.ReadFields();var c=h.Select((x,i)=>new{x,i}).ToDictionary(x=>x.x,x=>x.i,StringComparer.OrdinalIgnoreCase);while(!p.EndOfData){var f=p.ReadFields();if(f==null)continue;Func<string,string>v=k=>c.ContainsKey(k)&&c[k]<f.Length?f[c[k]]:"";r.Add(map(v));}}return r;}
        private static string Hash(string path){using(var s=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(s.ComputeHash(f)).Replace("-","");}
        private static string DetectBytesFormat(byte[] b){if(b!=null&&b.Length>=8&&b[0]==0x89&&b[1]==0x50)return"PNG_CONVERTED";if(b!=null&&b.Length>=3&&b[0]==0xff&&b[1]==0xd8)return"JPEG";return"UNKNOWN";}
        private static bool Bool(string v){return string.Equals(v,"true",StringComparison.OrdinalIgnoreCase);}private static string Join(params string[]v){return string.Join(",",v.Select(x=>"\""+(x??"").Replace("\"","\"\"")+"\""));}private static string B(bool v){return v?"true":"false";}private static string N(int v){return v.ToString(CultureInfo.InvariantCulture);}private static string D(double v){return v.ToString("0.##",CultureInfo.InvariantCulture);}private static double P(List<int>v,double p){if(v.Count==0)return 0;var r=(v.Count-1)*p;var l=(int)Math.Floor(r);var h=(int)Math.Ceiling(r);return v[l]+(v[h]-v[l])*(r-l);}
        private static string C(InvoiceSelection s){return s==null?"":s.Classification;}private static string C(SourceResult s){return s==null?"":C(s.Selection);}private static string M(InvoiceSelection s){return s==null?"":s.DetectionMethod;}private static string M(SourceResult s){return s==null?"":M(s.Selection);}private static string F(SourceResult s){return s==null||s.Selection==null||!s.Selection.Confidence.HasValue?"":N(s.Selection.Confidence.Value);}private static int Dur(SourceResult s){return s==null?0:s.DurationMs;}private static int OcrDur(SourceResult s){return s==null?0:s.OcrMs;}private static bool Hdr(SourceResult s){return s!=null&&s.HeaderUsed;}private static bool Lim(SourceResult s){return s!=null&&s.Limit;}private static bool Structural(SourceResult s){return s!=null&&s.Structural;}private static int Page(SourceResult s){return s==null?0:s.PageCount;}private static int Count(SourceResult s){return s==null?0:s.ImageCount;}private static string Failure(SourceResult s){return s==null?"":s.Failure;}
        private sealed class DatasetRow{internal int Order;internal string Sha,Path,Label,GroupId;}private sealed class FusionRow{internal string Sha,Label,GroupId,Format,Expected;internal bool ConservativeOcr;}private sealed class ProductRow{internal string Sha,Actual;}
        private sealed class SourceResult{internal InvoiceSelection Selection;internal bool OcrExecuted,Success,HeaderUsed,Limit,Structural;internal int DurationMs,OcrMs,RasterMs,HeaderMs,ImageCount,PageCount;internal string Failure;}
        private sealed class ImageRow{internal string Sha,Format;internal int Index,Width,Height,Bytes;}
        private sealed class Row{internal string Sha,Label,GroupId,MdocReason,EmbeddedDimensions,EmbeddedFailure,Expected,Actual,P0,P1,P2,P3,TraceP0,TraceP1,TraceP2,TraceP3;internal bool MdocUseful,OcrRequired,EmbeddedLimit,QrValid;internal InvoiceSelection Mdoc;internal SourceResult Embedded,Raster;internal int EmbeddedCount,EmbeddedExtractionMs,P0Cost,P1Cost,P2Cost,P3Cost,P0Embedded,P0Raster,P1Raster,P2Embedded,P2Raster,P3Embedded,P3Raster,P0Headers,P1Headers,P2Headers,P3Headers;internal int? Tipo;}
        private sealed class Policy{internal string Name;internal Func<Row,string>Get;internal Func<Row,int>Cost,Embedded,Raster,Headers;internal Policy(string n,Func<Row,string>g,Func<Row,int>c,Func<Row,int>e,Func<Row,int>r,Func<Row,int>h){Name=n;Get=g;Cost=c;Embedded=e;Raster=r;Headers=h;}}
    }
}
