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
    internal static class H1D5CProductValidationProbe
    {
        private const string DatasetHash="AF1FCC230B279D7DA4ACD77F5641BB1835D7D42EA0E8A41AD524D473FEC48158";
        private const string FusionHash="B7E823012762FE917D6E5C73F122731FE6180E96D7560755F68FB7A552FFA604";
        private const string Promotion1="2D43B3648B73EFBB36772933B8ED7CE0CD779C0945E776A136EC6123FA5D5551";
        private const string Promotion2="C177CD77BAA5F5587D302FDCC9F5615424F6BCED6057AA300113D6E3A600DC02";

        internal static int Run(string[] args)
        {
            if(args.Length!=4){Console.Error.WriteLine("Uso: --h1d5c-product-validation <dataset.csv> <H1D5B-fusion-results.csv> <output-H1D5C>");return 2;}
            string runtime=null;
            try
            {
                var datasetPath=Path.GetFullPath(args[1]);var fusionPath=Path.GetFullPath(args[2]);var output=Path.GetFullPath(args[3]);
                if(Hash(datasetPath)!=DatasetHash)throw new InvalidDataException("SHA-256 dataset.csv inesperado.");
                if(Hash(fusionPath)!=FusionHash)throw new InvalidDataException("SHA-256 fusion-results.csv H1D5B inesperado.");
                var dataset=LoadDataset(datasetPath);var expected=LoadExpected(fusionPath);Validate(dataset,expected);
                var policy=RunPolicyTests();if(policy.Any(x=>!x.Pass))throw new InvalidOperationException("Falló la tabla de verdad de fusión.");
                Directory.CreateDirectory(output);runtime=Path.Combine(Path.GetTempPath(),"RecepcionDocumental-H1D5C-"+Guid.NewGuid().ToString("N"));InitializeRuntime(runtime);
                var rows=new List<ResultRow>();
                foreach(var item in dataset.Values.OrderBy(x=>x.Order))
                {
                    var exp=expected[item.Sha];Console.WriteLine("H1D5C | "+(rows.Count+1)+"/80 | "+item.Sha);
                    rows.Add(Analyze(item,exp));
                }
                var ocrActivations=CountLogs(Path.Combine(runtime,"Logs"),"OCR requerido=Sí");
                WriteResults(Path.Combine(output,"product-validation.csv"),rows);
                WritePdf(Path.Combine(output,"pdf-validation.md"),rows.Where(x=>x.Format=="PDF").ToList(),ocrActivations);
                WriteCorpus(Path.Combine(output,"corpus-validation.md"),rows);
                WritePolicy(Path.Combine(output,"fusion-policy-tests.md"),policy);
                WriteSummary(Path.Combine(output,"resumen.md"),rows,policy,ocrActivations);
                var pdf=rows.Where(x=>x.Format=="PDF").ToList();var ok=pdf.Count(x=>x.Match)==36&&rows.All(x=>x.Match)&&policy.All(x=>x.Pass)&&CriticalChecks(pdf);
                Console.WriteLine("H1D5C_COMPLETO | Filas="+rows.Count+" | MatchPDF="+pdf.Count(x=>x.Match)+"/36 | MatchCorpus="+rows.Count(x=>x.Match)+"/80 | OCRActivadoPDF="+ocrActivations+" | Politica="+policy.Count(x=>x.Pass)+"/"+policy.Count+" | OK="+ok+" | Output="+output);
                return ok?0:1;
            }
            catch(Exception ex){Console.Error.WriteLine("ERROR H1D5C | "+ex.GetType().Name+": "+ex.Message);return 1;}
            finally{if(runtime!=null&&Directory.Exists(runtime))Directory.Delete(runtime,true);}
        }

        private static void InitializeRuntime(string root){var c=new ConfiguracionAplicacion("RecepcionDocumental",Path.Combine(root,"Logs"),Path.Combine(root,"Trabajo"),Path.Combine(root,"Facturas"),Path.Combine(root,"Revisar"),100,25L*1024*1024,100L*1024*1024,3,"https://localhost/h1d5c");c.PrepararRutasOperativas();ConfiguracionSistema.Inicializar(c);Logs.Inicializar(c);}

        private static ResultRow Analyze(DatasetRow item,ExpectedRow expected)
        {
            if(Hash(item.Path)!=item.Sha)throw new InvalidDataException("Hash físico distinto: "+item.Sha);
            var watch=Stopwatch.StartNew();AttachmentAnalysis analysis;using(var workspace=new AttachmentWorkspace())analysis=DocumentAnalysisService.Analyze(File.ReadAllBytes(item.Path),Path.GetFileName(item.Path),Mime(item.Format),workspace);watch.Stop();
            var candidate=analysis.Candidates.FirstOrDefault();var classification=candidate==null?"DESCARTAR":candidate.Selection.Classification;var method=candidate==null?"DESCARTADO_PRODUCTIVO":candidate.Selection.DetectionMethod;var confidence=candidate==null?(byte?)null:candidate.Selection.Confidence;var qrDetected=candidate!=null&&candidate.QrDetected;int? tipo=candidate==null?null:candidate.TipoComprobanteArca;
            if(item.Format=="PDF"&&candidate==null){var qr=MdocPdfQrDetector.Detect(item.Path);qrDetected=qr.QrDetected;tipo=qr.IsValid?qr.TipoComprobante:null;}
            var notes=new List<string>();if(expected.Current!=expected.Expected)notes.Add("CAMBIO_DESDE_CURRENT_PRODUCT="+expected.Current+"→"+expected.Expected);if(method=="MDOC_OCR_CONFLICTO")notes.Add("OCR_DESCARTAR_BLOQUEADO");if(classification!=expected.Expected)notes.Add("DIFERENCIA_H1D5B");
            return new ResultRow{Sha=item.Sha,Label=item.Label,GroupId=item.GroupId,Format=item.Format,Expected=expected.Expected,Product=classification,Match=classification==expected.Expected,Method=method,Confidence=confidence,Duration=(int)Math.Min(int.MaxValue,watch.ElapsedMilliseconds),QrDetected=qrDetected,Tipo=tipo,Notes=string.Join(";",notes),Current=expected.Current};
        }

        private static List<PolicyTest> RunPolicyTests()
        {
            var values=new[]{"FACTURA","REVISAR","DESCARTAR"};var tests=new List<PolicyTest>();foreach(var m in values)foreach(var o in values){var expected=m=="FACTURA"?"FACTURA":m=="DESCARTAR"?"DESCARTAR":o=="FACTURA"?"FACTURA":"REVISAR";var result=DocumentAnalysisService.FusePdfSelections(Sel(m,"MDOC"),Sel(o,"OCR"));tests.Add(new PolicyTest{Name=m+" + "+o,Mdoc=m,Ocr=o,Expected=expected,Actual=result.Classification,Method=result.DetectionMethod,Pass=result.Classification==expected});}
            var noText=DocumentAnalysisService.FusePdfSelections(InvoiceSelector.SelectPdf("",false),Sel("DESCARTAR","OCR"));tests.Add(new PolicyTest{Name="MDOC_SIN_TEXTO + DESCARTAR",Mdoc="REVISAR (sin texto)",Ocr="DESCARTAR",Expected="REVISAR",Actual=noText.Classification,Method=noText.DetectionMethod,Pass=noText.Classification=="REVISAR"&&noText.DetectionMethod=="MDOC_OCR_CONFLICTO"});return tests;
        }
        private static InvoiceSelection Sel(string c,string method){return c=="REVISAR"?InvoiceSelector.Review(method,"Prueba sintética.",null):new InvoiceSelection{Classification=c,DetectionMethod=method,Reason="Prueba sintética.",Confidence=c=="FACTURA"?(byte?)80:null};}

        private static void WriteResults(string path,List<ResultRow> rows){var lines=new List<string>{"Sha256,Label,GroupId,PhysicalFormat,H1D5BExpectedClassification,ProductClassification,Match,DetectionMethod,Confidence,DurationMs,QrDetected,TipoComprobanteArca,ValidationNotes"};lines.AddRange(rows.Select(x=>Join(x.Sha,x.Label,x.GroupId,x.Format,x.Expected,x.Product,B(x.Match),x.Method,x.Confidence.HasValue?N(x.Confidence.Value):"",N(x.Duration),B(x.QrDetected),x.Tipo.HasValue?N(x.Tipo.Value):"",x.Notes)));File.WriteAllLines(path,lines,new UTF8Encoding(false));}
        private static void WritePdf(string path,List<ResultRow> rows,int ocr)
        {
            var times=rows.Select(x=>x.Duration).OrderBy(x=>x).ToList();var sb=new StringBuilder("# H1D5C — Validación productiva de 36 PDF\n\n");sb.AppendLine("- Coincidencias con `FusionThenQrClassification`: **"+rows.Count(x=>x.Match)+"/36**.");sb.AppendLine("- OCR activado según logs: **"+ocr+"/36**.");sb.AppendLine("- Fallos/límites finales: "+rows.Count(x=>x.Method=="OCR_ERROR"||x.Method=="OCR_RENDER_ERROR"||x.Method=="OCR_LIMITE")+".");sb.AppendLine("- Tiempo total/media/mediana/P95: "+times.Sum()+" / "+D(times.Average())+" / "+D(P(times,.5))+" / "+D(P(times,.95))+" ms.\n");AppendMatrix(sb,rows);
            sb.AppendLine("## Criterios críticos\n").AppendLine("- `FACTURA → DESCARTAR`: "+rows.Count(x=>x.Label=="FACTURA"&&x.Product=="DESCARTAR")+".").AppendLine("- `OTRO_DOCUMENTO → FACTURA`: "+rows.Count(x=>x.Label=="OTRO_DOCUMENTO"&&x.Product=="FACTURA")+".");
            sb.AppendLine("- Promociones esperadas: "+rows.Count(x=>(x.Sha==Promotion1||x.Sha==Promotion2)&&x.Current=="REVISAR"&&x.Product=="FACTURA")+"/2.");sb.AppendLine("- Conflictos bloqueados `MDOC_OCR_CONFLICTO`: "+rows.Count(x=>x.Label=="FACTURA"&&x.Method=="MDOC_OCR_CONFLICTO")+"/3.");
            var differences=rows.Where(x=>!x.Match).ToList();sb.AppendLine("- Diferencias: "+(differences.Count==0?"ninguna.":string.Join(", ",differences.Select(x=>x.Sha+" esperado="+x.Expected+" real="+x.Product))+"."));File.WriteAllText(path,sb.ToString(),new UTF8Encoding(false));
        }
        private static void WriteCorpus(string path,List<ResultRow> rows){var sb=new StringBuilder("# H1D5C — Regresión documental del corpus completo\n\n> Vista secundaria; no es una evaluación end-to-end definitiva.\n\n");sb.AppendLine("- Coincidencias H1D5B: **"+rows.Count(x=>x.Match)+"/80**.\n");AppendMatrix(sb,rows);File.WriteAllText(path,sb.ToString(),new UTF8Encoding(false));}
        private static void WritePolicy(string path,List<PolicyTest> tests){var lines=new List<string>{"# H1D5C — Tabla de verdad de fusión","","| Caso | Mdoc | OCR | Esperado | Real | Método | Resultado |","|---|---|---|---|---|---|---|"};lines.AddRange(tests.Select(x=>"| "+x.Name+" | "+x.Mdoc+" | "+x.Ocr+" | "+x.Expected+" | "+x.Actual+" | "+x.Method+" | "+(x.Pass?"PASS":"FAIL")+" |"));lines.Add("");lines.Add("Resultado: **"+tests.Count(x=>x.Pass)+"/"+tests.Count+" PASS**.");File.WriteAllLines(path,lines,new UTF8Encoding(false));}
        private static void WriteSummary(string path,List<ResultRow> rows,List<PolicyTest> policy,int ocr){var pdf=rows.Where(x=>x.Format=="PDF").ToList();var times=pdf.Select(x=>x.Duration).OrderBy(x=>x).ToList();var sb=new StringBuilder("# H1D5C — Resumen ejecutivo\n\n");sb.AppendLine("- Producto y probe compilados con .NET Framework 4.8.");sb.AppendLine("- PDF coincidentes con H1D5B: **"+pdf.Count(x=>x.Match)+"/36**; corpus: **"+rows.Count(x=>x.Match)+"/80**.");sb.AppendLine("- Matriz PDF FACTURA: "+pdf.Count(x=>x.Label=="FACTURA"&&x.Product=="FACTURA")+" FACTURA, "+pdf.Count(x=>x.Label=="FACTURA"&&x.Product=="REVISAR")+" REVISAR, "+pdf.Count(x=>x.Label=="FACTURA"&&x.Product=="DESCARTAR")+" DESCARTAR.");sb.AppendLine("- Matriz PDF OTRO_DOCUMENTO: "+pdf.Count(x=>x.Label=="OTRO_DOCUMENTO"&&x.Product=="FACTURA")+" FACTURA, "+pdf.Count(x=>x.Label=="OTRO_DOCUMENTO"&&x.Product=="REVISAR")+" REVISAR, "+pdf.Count(x=>x.Label=="OTRO_DOCUMENTO"&&x.Product=="DESCARTAR")+" DESCARTAR.");sb.AppendLine("- Promociones esperadas: "+pdf.Count(x=>(x.Sha==Promotion1||x.Sha==Promotion2)&&x.Product=="FACTURA")+"/2; conflictos conservados en REVISAR con método de conflicto: "+pdf.Count(x=>x.Method=="MDOC_OCR_CONFLICTO")+"/3.");sb.AppendLine("- Tabla de verdad: "+policy.Count(x=>x.Pass)+"/"+policy.Count+" PASS, incluido Mdoc sin texto + OCR DESCARTAR.");sb.AppendLine("- OCR activado en "+ocr+"/36 PDF según instrumentación real.");sb.AppendLine("- Tiempo PDF total/media/mediana/P95: "+times.Sum()+" / "+D(times.Average())+" / "+D(P(times,.5))+" / "+D(P(times,.95))+" ms.");sb.AppendLine("- La medición corresponde a esta computadora y corpus; no constituye capacidad productiva final ni despliegue.");File.WriteAllText(path,sb.ToString(),new UTF8Encoding(false));}

        private static void AppendMatrix(StringBuilder sb,IEnumerable<ResultRow> rows){sb.AppendLine("| Label | FACTURA | REVISAR | DESCARTAR |").AppendLine("|---|---:|---:|---:|");foreach(var l in new[]{"FACTURA","OTRO_DOCUMENTO","NO_DOCUMENTO"}){var q=rows.Where(x=>x.Label==l);if(q.Any())sb.AppendLine("| "+l+" | "+q.Count(x=>x.Product=="FACTURA")+" | "+q.Count(x=>x.Product=="REVISAR")+" | "+q.Count(x=>x.Product=="DESCARTAR")+" |");}sb.AppendLine();}
        private static bool CriticalChecks(List<ResultRow> pdf){return pdf.Count(x=>x.Label=="FACTURA"&&x.Product=="DESCARTAR")==0&&pdf.Count(x=>x.Label=="OTRO_DOCUMENTO"&&x.Product=="FACTURA")==0&&pdf.Count(x=>(x.Sha==Promotion1||x.Sha==Promotion2)&&x.Product=="FACTURA")==2&&pdf.Count(x=>x.Method=="MDOC_OCR_CONFLICTO")==3;}

        private static Dictionary<string,DatasetRow> LoadDataset(string path){var baseDir=Path.GetDirectoryName(path);var order=0;return ReadCsv(path,f=>{var stored=f("Path");var full=Path.IsPathRooted(stored)?stored:Path.GetFullPath(Path.Combine(baseDir,stored));return new DatasetRow{Order=order++,Sha=f("Sha256").ToUpperInvariant(),Path=full,Label=f("Label"),GroupId=f("GroupId"),Format=Format(full)};}).ToDictionary(x=>x.Sha);}
        private static Dictionary<string,ExpectedRow> LoadExpected(string path){return ReadCsv(path,f=>new ExpectedRow{Sha=f("Sha256").ToUpperInvariant(),Label=f("Label"),GroupId=f("GroupId"),Format=f("PhysicalFormat"),Expected=f("FusionThenQrClassification"),Current=f("CurrentProductClassification")}).ToDictionary(x=>x.Sha);}
        private static void Validate(Dictionary<string,DatasetRow>d,Dictionary<string,ExpectedRow>e){if(d.Count!=80||e.Count!=80||d.Values.Select(x=>x.GroupId).Distinct().Count()!=54)throw new InvalidDataException("Conteos inesperados.");foreach(var x in d.Values){ExpectedRow y;if(!e.TryGetValue(x.Sha,out y)||x.Label!=y.Label||x.GroupId!=y.GroupId||x.Format!=y.Format)throw new InvalidDataException("Entradas no coinciden: "+x.Sha);}}
        private static int CountLogs(string dir,string value){if(!Directory.Exists(dir))return 0;return Directory.GetFiles(dir,"*",System.IO.SearchOption.AllDirectories).Sum(p=>File.ReadLines(p).Count(l=>l.Contains(value)));}
        private static double P(List<int>v,double p){if(v.Count==0)return 0;var r=(v.Count-1)*p;var l=(int)Math.Floor(r);var h=(int)Math.Ceiling(r);return v[l]+(v[h]-v[l])*(r-l);}
        private static List<T> ReadCsv<T>(string path,Func<Func<string,string>,T> map){var r=new List<T>();using(var p=new TextFieldParser(path,Encoding.UTF8)){p.TextFieldType=FieldType.Delimited;p.HasFieldsEnclosedInQuotes=true;p.SetDelimiters(",");var h=p.ReadFields();var c=h.Select((n,i)=>new{n,i}).ToDictionary(x=>x.n,x=>x.i,StringComparer.OrdinalIgnoreCase);while(!p.EndOfData){var f=p.ReadFields();if(f==null)continue;Func<string,string>v=n=>c.ContainsKey(n)&&c[n]<f.Length?f[c[n]]:"";r.Add(map(v));}}return r;}
        private static string Hash(string path){using(var s=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(s.ComputeHash(f)).Replace("-","");}
        private static string Format(string p){using(var s=File.OpenRead(p)){var b=new byte[8];s.Read(b,0,8);if(b[0]==0x25&&b[1]==0x50&&b[2]==0x44)return"PDF";if(b[0]==0x89&&b[1]==0x50)return"PNG";if(b[0]==0xff&&b[1]==0xd8)return"JPEG";}throw new InvalidDataException("Formato inválido.");}
        private static string Mime(string f){return f=="PDF"?"application/pdf":f=="PNG"?"image/png":"image/jpeg";}
        private static string Join(params string[]v){return string.Join(",",v.Select(x=>"\""+(x??"").Replace("\"","\"\"")+"\""));}private static string B(bool v){return v?"true":"false";}private static string N(int v){return v.ToString(CultureInfo.InvariantCulture);}private static string D(double v){return v.ToString("0.##",CultureInfo.InvariantCulture);}
        private sealed class DatasetRow{internal int Order;internal string Sha,Path,Label,GroupId,Format;}private sealed class ExpectedRow{internal string Sha,Label,GroupId,Format,Expected,Current;}private sealed class ResultRow{internal string Sha,Label,GroupId,Format,Expected,Product,Method,Notes,Current;internal bool Match,QrDetected;internal byte? Confidence;internal int Duration;internal int? Tipo;}private sealed class PolicyTest{internal string Name,Mdoc,Ocr,Expected,Actual,Method;internal bool Pass;}
    }
}
