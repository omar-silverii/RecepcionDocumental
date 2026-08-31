using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class H1D5BFusionProbe
    {
        private const string DatasetHash = "AF1FCC230B279D7DA4ACD77F5641BB1835D7D42EA0E8A41AD524D473FEC48158";
        private const string EvidenceHash = "7E28386AECDCF2CD137277B44649DE8DFCC63D5DCFFEB7B700710C5BC3C8D3AB";

        internal static int Run(string[] args)
        {
            if (args.Length != 4) { Console.Error.WriteLine("Uso: --h1d5b-fusion <dataset.csv> <H1D5A-evidence-audit.csv> <output-H1D5B>"); return 2; }
            string runtime = null;
            try
            {
                var datasetPath=Path.GetFullPath(args[1]); var evidencePath=Path.GetFullPath(args[2]); var output=Path.GetFullPath(args[3]);
                if(Hash(datasetPath)!=DatasetHash) throw new InvalidDataException("SHA-256 dataset.csv inesperado.");
                if(Hash(evidencePath)!=EvidenceHash) throw new InvalidDataException("SHA-256 evidence-audit.csv H1D5A inesperado.");
                var dataset=LoadDataset(datasetPath); var rows=LoadEvidence(evidencePath); Validate(dataset,rows);
                foreach(var r in rows) Decide(r);
                var pdf=rows.Where(x=>x.Format=="PDF").ToList();
                ValidateSanity(pdf);
                Directory.CreateDirectory(output);
                runtime=Path.Combine(Path.GetTempPath(),"RecepcionDocumental-H1D5B-"+Guid.NewGuid().ToString("N")); InitializeRuntime(runtime);
                var conflicts=DiagnoseConflicts(rows,dataset);
                WriteResults(Path.Combine(output,"fusion-results.csv"),rows);
                WritePdfMetrics(Path.Combine(output,"pdf-metrics.md"),pdf);
                WriteCorpus(Path.Combine(output,"corpus-diagnostic.md"),rows);
                WriteCosts(Path.Combine(output,"ocr-cost.csv"),pdf);
                WriteGroups(Path.Combine(output,"group-summary.csv"),rows);
                WriteConflicts(Path.Combine(output,"ocr-conflicts.csv"),conflicts);
                WriteSummary(Path.Combine(output,"resumen.md"),pdf,rows,conflicts);
                Console.WriteLine("H1D5B_COMPLETO | Filas="+rows.Count+" | PDF="+pdf.Count+" | Grupos="+rows.Select(x=>x.GroupId).Distinct().Count()+" | ConflictosOCR="+conflicts.Count+" | C1vsC2="+pdf.Count(x=>x.FusionThenQr!=x.QrThenFusion)+" | Output="+output);
                return 0;
            }
            catch(Exception ex){Console.Error.WriteLine("ERROR H1D5B | "+ex.GetType().Name+": "+ex.Message);return 1;}
            finally{if(runtime!=null&&Directory.Exists(runtime))Directory.Delete(runtime,true);}
        }

        private static void InitializeRuntime(string root)
        {
            var c=new ConfiguracionAplicacion("RecepcionDocumental",Path.Combine(root,"Logs"),Path.Combine(root,"Trabajo"),Path.Combine(root,"Facturas"),Path.Combine(root,"Revisar"),100,25L*1024*1024,100L*1024*1024,3,"https://localhost/h1d5b");
            c.PrepararRutasOperativas(); ConfiguracionSistema.Inicializar(c); Logs.Inicializar(c);
        }

        private static void Decide(Row r)
        {
            if(r.Format!="PDF")
            {
                r.Current=r.Direct=r.Conservative=r.FusionThenQr=r.QrThenFusion=r.Ocr;
                r.Trace="IMAGEN: clasificación OCR H1D5A conservada"; return;
            }
            r.CurrentWouldOcr=!r.MdocUseful; r.ConservativeWouldOcr=r.Mdoc=="REVISAR";
            r.Current=r.MdocUseful?r.QrMdoc:r.QrOcr;
            r.Direct=r.Mdoc=="REVISAR"?r.QrOcr:r.QrMdoc;
            r.Conservative=Fuse(r.Mdoc,r.Ocr);
            var qr=new ArcaQrEvidence{QrDetected=r.QrDetected,IsValid=r.QrValid,TipoComprobante=r.Tipo};
            r.FusionThenQr=ArcaQrDecoder.Combine(qr,Selection(r.Conservative,"FUSION_CONSERVADORA")).Classification;
            r.QrThenFusion=Fuse(r.QrMdoc,r.QrOcr);
            r.Trace="CURRENT="+(r.MdocUseful?"QR(MDOC)":"QR(OCR)")+"; DIRECT="+(r.Mdoc=="REVISAR"?"QR(OCR)":"QR(MDOC)")+"; CONSERVATIVE="+TraceFuse(r.Mdoc,r.Ocr)+"; QR_VALID="+r.QrValid.ToString().ToLowerInvariant();
        }

        private static string Fuse(string mdoc,string ocr){if(mdoc=="FACTURA"||mdoc=="DESCARTAR")return mdoc;return ocr=="FACTURA"?"FACTURA":"REVISAR";}
        private static string TraceFuse(string m,string o){return m=="REVISAR"?(o=="FACTURA"?"OCR_PROMUEVE_FACTURA":o=="DESCARTAR"?"OCR_DESCARTAR_BLOQUEADO":"MANTIENE_REVISAR"):"MDOC_"+m+"_CONSERVADO";}
        private static InvoiceSelection Selection(string c,string method){return new InvoiceSelection{Classification=c,DetectionMethod=method,Reason="Decisión experimental H1D5B.",Confidence=null};}

        private static List<Conflict> DiagnoseConflicts(List<Row> rows,Dictionary<string,DatasetRow> dataset)
        {
            var selected=rows.Where(x=>x.Format=="PDF"&&x.Label=="FACTURA"&&x.Mdoc=="REVISAR"&&x.Ocr=="DESCARTAR").ToList();
            var result=new List<Conflict>();
            foreach(var row in selected)
            {
                var item=dataset[row.Sha]; if(Hash(item.Path)!=row.Sha)throw new InvalidDataException("Hash físico distinto en conflicto "+row.Sha);
                var mdoc=MdocPdfTextExtractor.Extract(item.Path); OcrResult ocr; InvoiceSelection os;
                using(var workspace=new AttachmentWorkspace())
                {
                    var raster=PdfPageRasterizer.Rasterize(item.Path,workspace);
                    ocr=raster.Images.Count>0?DocumentOcrService.Recognize(raster.Images):new OcrResult{Text="",FailureReason=raster.FailureReason};
                    os=InvoiceSelector.SelectOcrText(ocr.Text,ocr.HasUsefulText);
                    if(os.Classification=="REVISAR"&&raster.Images.Count>0){var h=DocumentOcrService.RecognizeHeader(raster.Images);if(h.Success){ocr=DocumentOcrService.Combine(ocr,h);os=InvoiceSelector.SelectOcrText(ocr.Text,ocr.HasUsefulText);}}
                }
                var signal=ExtractSignal(os.Reason); var context=Context(ocr.Text,signal); var inMdoc=ContainsNormalized(mdoc.Text,signal); var mdocSelection=InvoiceSelector.SelectPdf(mdoc.Text,mdoc.HasUsefulText); var positive=(mdocSelection.Reason??"").IndexOf("Factura explícita",StringComparison.OrdinalIgnoreCase)>=0||ContainsPositive(mdoc.Text)||ContainsPositive(ocr.Text)||row.MdocReason.IndexOf("Factura explícita",StringComparison.OrdinalIgnoreCase)>=0;
                var mode=ContainsLiteral(ocr.Text,signal)?"COINCIDENCIA_LITERAL":"COINCIDENCIA_COMPACTA_O_NO_LOCALIZADA";
                var interpretation="La señal aparece literalmente en el OCR dentro de un campo o referencia comercial; por sí sola no demuestra que el documento sea "+signal+". "+(inMdoc?"También está presente en la extracción Mdoc independiente.":"No fue localizada en Mdoc; puede ser texto visible que Mdoc omitió o un artefacto OCR.")+(positive?" Coexiste evidencia explícita de FACTURA.":" No se localizó evidencia explícita de FACTURA en ambas fuentes.");
                result.Add(new Conflict{Sha=row.Sha,GroupId=row.GroupId,Signal=signal,Mdoc=row.Mdoc,Ocr=row.Ocr,Positive=positive,Context=context,Interpretation=mode+". "+interpretation,Reexecuted=os.Classification});
            }
            return result;
        }

        private static string ExtractSignal(string reason){var m=Regex.Match(reason??"",@"Documento identificado como (.+)\.",RegexOptions.CultureInvariant);return m.Success?m.Groups[1].Value:"NO_IDENTIFICADA";}
        private static bool ContainsLiteral(string text,string signal){return (text??"").IndexOf(signal??"",StringComparison.OrdinalIgnoreCase)>=0;}
        private static bool ContainsNormalized(string text,string signal){return Compact(text).Contains(Compact(signal));}
        private static bool ContainsPositive(string text){var n=Normalize(text);return Regex.IsMatch(" "+n+" ",@" FACTURA (A|B|C|M|E) ",RegexOptions.CultureInvariant)||n.Contains("FACTURA DE CREDITO ELECTRONICA");}
        private static string Context(string text,string signal)
        {
            var value=Regex.Replace(text??"",@"\s+"," ").Trim(); var i=value.IndexOf(signal??"",StringComparison.OrdinalIgnoreCase);
            if(i<0){var compact=Compact(signal); var tokens=value.Split(' '); for(var n=0;n<tokens.Length;n++){var sample=string.Join(" ",tokens.Skip(n).Take(8));if(Compact(sample).Contains(compact)){i=value.IndexOf(tokens[n],StringComparison.Ordinal);break;}}}
            if(i<0)return "Señal no localizada literalmente en el texto reejecutado."; var start=Math.Max(0,i-80); var len=Math.Min(value.Length-start,(i-start)+Math.Max((signal??"").Length,1)+80); return value.Substring(start,len);
        }
        private static string Normalize(string value){var s=(value??"").ToUpperInvariant().Normalize(NormalizationForm.FormD);var b=new StringBuilder();foreach(var c in s)if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)b.Append(char.IsWhiteSpace(c)?' ':c);return Regex.Replace(b.ToString(),@"\s+"," ").Trim();}
        private static string Compact(string value){return new string(Normalize(value).Where(char.IsLetterOrDigit).ToArray());}

        private static void ValidateSanity(List<Row> pdf)
        {
            var actual=Counts(pdf,x=>x.Current); var expected="FACTURA:13/9/0;OTRO_DOCUMENTO:0/8/6";
            var got=CompactCounts(actual); if(got!=expected)throw new InvalidDataException("CURRENT_PRODUCT difiere del sanity check. Esperado="+expected+"; obtenido="+got);
        }

        private static void Validate(Dictionary<string,DatasetRow> dataset,List<Row> rows)
        {
            if(dataset.Count!=80||rows.Count!=80||rows.Select(x=>x.Sha).Distinct().Count()!=80||rows.Select(x=>x.GroupId).Distinct().Count()!=54)throw new InvalidDataException("Conteos de entrada inesperados.");
            foreach(var r in rows){DatasetRow d;if(!dataset.TryGetValue(r.Sha,out d))throw new InvalidDataException("Hash H1D5A ausente en dataset: "+r.Sha);if(d.Label!=r.Label||d.GroupId!=r.GroupId||d.Format!=r.Format)throw new InvalidDataException("Metadatos H1D5A no coinciden: "+r.Sha);}
        }

        private static Dictionary<string,DatasetRow> LoadDataset(string path)
        {
            var baseDir=Path.GetDirectoryName(path);return ReadCsv(path,f=>{var stored=f("Path");var full=Path.IsPathRooted(stored)?stored:Path.GetFullPath(Path.Combine(baseDir,stored));return new DatasetRow{Sha=f("Sha256").ToUpperInvariant(),Path=full,Label=f("Label"),GroupId=f("GroupId"),Format=DetectFormat(full)};}).ToDictionary(x=>x.Sha);
        }
        private static List<Row> LoadEvidence(string path){return ReadCsv(path,f=>new Row{Sha=f("Sha256").ToUpperInvariant(),Label=f("Label"),GroupId=f("GroupId"),Split=f("Split"),Format=f("PhysicalFormat"),MdocUseful=Bool(f("MdocHasUsefulText")),Mdoc=f("MdocClassification"),MdocReason=f("MdocReason"),Ocr=f("OcrClassification"),QrDetected=Bool(f("QrDetected")),QrValid=Bool(f("QrValid")),Tipo=IntN(f("TipoComprobanteArca")),QrMdoc=f("QrMdocClassification"),QrOcr=f("QrOcrClassification"),OcrDuration=Int(f("OcrDurationMs")),RasterPages=Int(f("RasterPageCount")),OcrExecuted=Bool(f("OcrExecuted")),OcrSuccess=Bool(f("OcrSuccess")),OcrLimit=Bool(f("OcrLimitExceeded")),OcrFailure=f("OcrFailureReason")});}

        private static void WriteResults(string path,List<Row> rows)
        {
            var lines=new List<string>{"Sha256,Label,GroupId,Split,PhysicalFormat,MdocHasUsefulText,MdocClassification,OcrClassification,QrValid,TipoComprobanteArca,CurrentProductClassification,DirectReplacementClassification,ConservativeFusionClassification,FusionThenQrClassification,QrThenFusionClassification,CurrentProductWouldRunOcr,ConservativeWouldRunOcr,DecisionTrace"};
            lines.AddRange(rows.Select(r=>Join(r.Sha,r.Label,r.GroupId,r.Split,r.Format,B(r.MdocUseful),r.Mdoc,r.Ocr,B(r.QrValid),r.Tipo.HasValue?r.Tipo.Value.ToString(CultureInfo.InvariantCulture):"",r.Current,r.Direct,r.Conservative,r.FusionThenQr,r.QrThenFusion,B(r.CurrentWouldOcr),B(r.ConservativeWouldOcr),r.Trace)));File.WriteAllLines(path,lines,new UTF8Encoding(false));
        }

        private static void WritePdfMetrics(string path,List<Row> rows)
        {
            var sb=new StringBuilder("# H1D5B — Métricas primarias de 36 PDF\n\n> Regresión/diagnóstico experimental; no certificación final independiente.\n\n");
            AppendMatrix(sb,"CURRENT_PRODUCT",rows,x=>x.Current);AppendMatrix(sb,"DIRECT_REPLACEMENT",rows,x=>x.Direct);AppendMatrix(sb,"CONSERVATIVE_FUSION",rows,x=>x.Conservative);AppendMatrix(sb,"FUSION_THEN_QR (C1)",rows,x=>x.FusionThenQr);AppendMatrix(sb,"QR_THEN_FUSION (C2)",rows,x=>x.QrThenFusion);
            sb.AppendLine("## Indicadores prioritarios\n").AppendLine("| Estrategia | FACTURA→DESCARTAR | Falso FACTURA | Recall FACTURA | REVISAR | OCR requeridos |").AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach(var s in Strategies())sb.AppendLine("| "+s.Name+" | "+rows.Count(x=>x.Label=="FACTURA"&&s.Get(x)=="DESCARTAR")+" | "+rows.Count(x=>x.Label=="OTRO_DOCUMENTO"&&s.Get(x)=="FACTURA")+" | "+rows.Count(x=>x.Label=="FACTURA"&&s.Get(x)=="FACTURA")+"/22 | "+rows.Count(x=>s.Get(x)=="REVISAR")+" | "+(s.Name=="CURRENT_PRODUCT"?rows.Count(x=>x.CurrentWouldOcr):s.Name.Contains("CONSERVATIVE")||s.Name.StartsWith("C1")||s.Name.StartsWith("C2")?rows.Count(x=>x.ConservativeWouldOcr):rows.Count(x=>x.Mdoc=="REVISAR"))+"/36 |");
            var diff=rows.Where(x=>x.FusionThenQr!=x.QrThenFusion).ToList();sb.AppendLine().AppendLine("## Orden QR\n").AppendLine("C1 vs C2 difieren en **"+diff.Count+"** PDF."+(diff.Count==0?" En este corpus son equivalentes.":" Hashes: "+string.Join(", ",diff.Select(x=>x.Sha))+"."));File.WriteAllText(path,sb.ToString(),new UTF8Encoding(false));
        }
        private static void WriteCorpus(string path,List<Row> rows){var sb=new StringBuilder("# H1D5B — Diagnóstico documental del corpus completo\n\n> Vista secundaria de 80 archivos; no es evaluación end-to-end. NO_DOCUMENTO pertenece conceptualmente a la etapa visual.\n\n");AppendMatrix(sb,"CURRENT_PRODUCT",rows,x=>x.Current);AppendMatrix(sb,"CONSERVATIVE_FUSION",rows,x=>x.Conservative);File.WriteAllText(path,sb.ToString(),new UTF8Encoding(false));}
        private static void WriteCosts(string path,List<Row> pdf)
        {
            var lines=new List<string>{"Strategy,PdfsRequiringOcr,Percent,OcrDurationTotalMs,OcrDurationMeanMs,OcrDurationMedianMs,OcrDurationP95Ms,Rasterizations,RasterPageCount,OcrFailuresOrLimits"};
            AddCost(lines,"CURRENT_PRODUCT",pdf.Where(x=>x.CurrentWouldOcr).ToList());AddCost(lines,"CONSERVATIVE_FUSION",pdf.Where(x=>x.ConservativeWouldOcr).ToList());File.WriteAllLines(path,lines,new UTF8Encoding(false));
        }
        private static void AddCost(List<string> lines,string name,List<Row> rows){var times=rows.Select(x=>x.OcrDuration).OrderBy(x=>x).ToList();lines.Add(Join(name,N(rows.Count),D(100d*rows.Count/36),N(times.Sum()),D(times.Count==0?0:times.Average()),D(Percentile(times,.5)),D(Percentile(times,.95)),N(rows.Count(x=>x.OcrExecuted)),N(rows.Sum(x=>x.RasterPages)),N(rows.Count(x=>!x.OcrSuccess||x.OcrLimit))));}
        private static double Percentile(List<int> v,double p){if(v.Count==0)return 0;var rank=(v.Count-1)*p;var low=(int)Math.Floor(rank);var high=(int)Math.Ceiling(rank);return v[low]+(v[high]-v[low])*(rank-low);}

        private static void WriteGroups(string path,List<Row> rows)
        {
            var lines=new List<string>{"GroupId,Label,Files,CurrentProductGroupResult,DirectReplacementGroupResult,ConservativeFusionGroupResult,FusionThenQrGroupResult,QrThenFusionGroupResult,CurrentHomogeneous,ConservativeHomogeneous,ContainsFacturaDiscardedCurrent,ContainsFacturaDiscardedConservative,ContainsFalseFacturaCurrent,ContainsFalseFacturaConservative"};
            foreach(var g in rows.GroupBy(x=>new{x.GroupId,x.Label}).OrderBy(x=>x.Key.GroupId))lines.Add(Join(g.Key.GroupId,g.Key.Label,N(g.Count()),GroupResult(g,x=>x.Current),GroupResult(g,x=>x.Direct),GroupResult(g,x=>x.Conservative),GroupResult(g,x=>x.FusionThenQr),GroupResult(g,x=>x.QrThenFusion),B(g.Select(x=>x.Current).Distinct().Count()==1),B(g.Select(x=>x.Conservative).Distinct().Count()==1),B(g.Any(x=>x.Label=="FACTURA"&&x.Current=="DESCARTAR")),B(g.Any(x=>x.Label=="FACTURA"&&x.Conservative=="DESCARTAR")),B(g.Any(x=>x.Label!="FACTURA"&&x.Current=="FACTURA")),B(g.Any(x=>x.Label!="FACTURA"&&x.Conservative=="FACTURA"))));File.WriteAllLines(path,lines,new UTF8Encoding(false));
        }
        private static string GroupResult(IEnumerable<Row> g,Func<Row,string> f){var d=g.Select(f).Distinct().ToList();return d.Count==1?d[0]:"RESULTADO_MIXTO";}
        private static void WriteConflicts(string path,List<Conflict> rows){var lines=new List<string>{"Sha256,GroupId,NegativeSignal,MdocClassification,OcrClassification,ReexecutedOcrClassification,SimultaneousPositiveInvoiceEvidence,LimitedContext,TechnicalInterpretation"};lines.AddRange(rows.Select(x=>Join(x.Sha,x.GroupId,x.Signal,x.Mdoc,x.Ocr,x.Reexecuted,B(x.Positive),x.Context,x.Interpretation)));File.WriteAllLines(path,lines,new UTF8Encoding(false));}

        private static void WriteSummary(string path,List<Row> pdf,List<Row> all,List<Conflict> conflicts)
        {
            var currentInv=pdf.Count(x=>x.Label=="FACTURA"&&x.Current=="FACTURA");var consInv=pdf.Count(x=>x.Label=="FACTURA"&&x.Conservative=="FACTURA");var currentReview=pdf.Count(x=>x.Current=="REVISAR");var consReview=pdf.Count(x=>x.Conservative=="REVISAR");var curOcr=pdf.Where(x=>x.CurrentWouldOcr).ToList();var conOcr=pdf.Where(x=>x.ConservativeWouldOcr).ToList();var additional=conOcr.Except(curOcr).ToList();
            var sb=new StringBuilder("# H1D5B — Resumen ejecutivo\n\n> Benchmark experimental con evidencia H1D5A congelada. No implementa H1D5C.\n\n");
            sb.AppendLine("1. **FACTURA → DESCARTAR conservadora:** "+pdf.Count(x=>x.Label=="FACTURA"&&x.Conservative=="DESCARTAR")+"; se mantiene en cero.");
            sb.AppendLine("2. **FACTURA adicionales reconocidas:** "+(consInv-currentInv)+" (de "+currentInv+" a "+consInv+").");
            sb.AppendLine("3. **Falsos FACTURA desde OTRO_DOCUMENTO:** "+pdf.Count(x=>x.Label=="OTRO_DOCUMENTO"&&x.Conservative=="FACTURA")+".");
            sb.AppendLine("4. **REVISAR eliminados:** "+(currentReview-consReview)+" (de "+currentReview+" a "+consReview+").");
            sb.AppendLine("5. **Uso de OCR:** "+curOcr.Count+"/36 actual frente a "+conOcr.Count+"/36 conservador; aumento de "+(conOcr.Count-curOcr.Count)+" PDF.");
            sb.AppendLine("6. **Costo OCR adicional estimado:** "+additional.Sum(x=>x.OcrDuration)+" ms acumulados H1D5A para "+additional.Count+" PDF adicionales.");
            sb.AppendLine("7. **Sustitución directa insegura:** sí; produce "+pdf.Count(x=>x.Label=="FACTURA"&&x.Direct=="DESCARTAR")+" FACTURA → DESCARTAR.");
            sb.AppendLine("8. **Orden QR C1/C2:** "+pdf.Count(x=>x.FusionThenQr!=x.QrThenFusion)+" diferencias; "+(pdf.All(x=>x.FusionThenQr==x.QrThenFusion)?"equivalentes en este corpus.":"ver hashes en pdf-metrics.md.")+"");
            sb.AppendLine("9. **Tres falsos descartes OCR:** "+string.Join("; ",conflicts.Select(x=>x.Signal+" — "+x.Interpretation))+"");
            sb.AppendLine("10. **Evidencia para proponer H1D5C:** sí como candidata a validación productiva controlada, no como integración automática cerrada.");
            sb.AppendLine("11. **Candidata:** CONSERVATIVE_FUSION, porque sólo permite promoción positiva desde REVISAR y preserva FACTURA → DESCARTAR = 0 sin falsos FACTURA en estos 36 PDF.");
            sb.AppendLine("12. **Riesgos abiertos:** costo OCR mayor, dos PDF con límites OCR, estabilidad fuera del corpus, semántica de señales negativas y orden QR aún no diferenciable empíricamente con los siete QR actuales.");File.WriteAllText(path,sb.ToString(),new UTF8Encoding(false));
        }

        private static void AppendMatrix(StringBuilder sb,string title,IEnumerable<Row> rows,Func<Row,string> get){sb.AppendLine("## "+title+"\n").AppendLine("| Label | FACTURA | REVISAR | DESCARTAR |").AppendLine("|---|---:|---:|---:|");foreach(var l in new[]{"FACTURA","OTRO_DOCUMENTO","NO_DOCUMENTO"}){var q=rows.Where(x=>x.Label==l);if(q.Any())sb.AppendLine("| "+l+" | "+q.Count(x=>get(x)=="FACTURA")+" | "+q.Count(x=>get(x)=="REVISAR")+" | "+q.Count(x=>get(x)=="DESCARTAR")+" |");}sb.AppendLine();}
        private static Dictionary<string,int[]> Counts(IEnumerable<Row> rows,Func<Row,string> get){return rows.GroupBy(x=>x.Label).ToDictionary(g=>g.Key,g=>new[]{g.Count(x=>get(x)=="FACTURA"),g.Count(x=>get(x)=="REVISAR"),g.Count(x=>get(x)=="DESCARTAR")});}
        private static string CompactCounts(Dictionary<string,int[]> c){return string.Join(";",new[]{"FACTURA","OTRO_DOCUMENTO"}.Select(k=>k+":"+string.Join("/",c[k])));}
        private static List<Strategy> Strategies(){return new List<Strategy>{new Strategy("CURRENT_PRODUCT",x=>x.Current),new Strategy("DIRECT_REPLACEMENT",x=>x.Direct),new Strategy("CONSERVATIVE_FUSION",x=>x.Conservative),new Strategy("C1_FUSION_THEN_QR",x=>x.FusionThenQr),new Strategy("C2_QR_THEN_FUSION",x=>x.QrThenFusion)};}
        private static List<T> ReadCsv<T>(string path,Func<Func<string,string>,T> map){var result=new List<T>();using(var p=new TextFieldParser(path,Encoding.UTF8)){p.TextFieldType=FieldType.Delimited;p.HasFieldsEnclosedInQuotes=true;p.SetDelimiters(",");var h=p.ReadFields();var c=h.Select((n,i)=>new{n,i}).ToDictionary(x=>x.n,x=>x.i,StringComparer.OrdinalIgnoreCase);while(!p.EndOfData){var f=p.ReadFields();if(f==null)continue;Func<string,string> v=n=>c.ContainsKey(n)&&c[n]<f.Length?f[c[n]]:"";result.Add(map(v));}}return result;}
        private static string DetectFormat(string path){using(var s=File.OpenRead(path)){var b=new byte[8];s.Read(b,0,8);if(b[0]==0x25&&b[1]==0x50&&b[2]==0x44&&b[3]==0x46)return"PDF";if(b[0]==0x89&&b[1]==0x50)return"PNG";if(b[0]==0xff&&b[1]==0xd8)return"JPEG";}throw new InvalidDataException("Formato no soportado.");}
        private static string Hash(string path){using(var s=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(s.ComputeHash(f)).Replace("-","");}
        private static bool Bool(string v){return string.Equals(v,"true",StringComparison.OrdinalIgnoreCase);}private static int Int(string v){int n;return int.TryParse(v,NumberStyles.Integer,CultureInfo.InvariantCulture,out n)?n:0;}private static int? IntN(string v){int n;return int.TryParse(v,NumberStyles.Integer,CultureInfo.InvariantCulture,out n)?(int?)n:null;}
        private static string Join(params string[] v){return string.Join(",",v.Select(x=>"\""+(x??"").Replace("\"","\"\"")+"\""));}private static string B(bool v){return v?"true":"false";}private static string N(int v){return v.ToString(CultureInfo.InvariantCulture);}private static string D(double v){return v.ToString("0.##",CultureInfo.InvariantCulture);}
        private sealed class DatasetRow{internal string Sha,Path,Label,GroupId,Format;}
        private sealed class Row{internal string Sha,Label,GroupId,Split,Format,Mdoc,MdocReason,Ocr,QrMdoc,QrOcr,Current,Direct,Conservative,FusionThenQr,QrThenFusion,Trace,OcrFailure;internal bool MdocUseful,QrDetected,QrValid,CurrentWouldOcr,ConservativeWouldOcr,OcrExecuted,OcrSuccess,OcrLimit;internal int? Tipo;internal int OcrDuration,RasterPages;}
        private sealed class Conflict{internal string Sha,GroupId,Signal,Mdoc,Ocr,Context,Interpretation,Reexecuted;internal bool Positive;}
        private sealed class Strategy{internal string Name;internal Func<Row,string> Get;internal Strategy(string n,Func<Row,string> g){Name=n;Get=g;}}
    }
}
