using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.VisualBasic.FileIO;

namespace DocumentAiProbe;

internal static class H1D4AExperiment
{
    const string Seed = "H1D4A-v1-sha256";
    static readonly string[] Classes = { "FACTURA", "OTRO_DOCUMENTO", "NO_DOCUMENTO" };
    public static int Run(string root)
    {
        var experiment = Path.Combine(root, "experiments", "H1D4A");
        var assetsPath = Path.Combine(experiment, "assets", "assets.csv");
        if (!File.Exists(assetsPath)) throw new FileNotFoundException("Falta assets.csv", assetsPath);
        foreach (var p in new[]{"split-manifest.csv","visual-metrics.md","textual-metrics.md","end-to-end-metrics.md","misclassified.csv","visual-model.zip","textual-model.zip","resumen.md","configuration.txt"}) { var x=Path.Combine(experiment,p); if(File.Exists(x)) throw new IOException("La salida ya existe: "+x); }
        var rows = Load(assetsPath); if (rows.Count != 80) throw new InvalidDataException("Se esperaban 80 assets.");
        var frozen = File.ReadAllLines(Path.Combine(root, "frozen-test-groups.txt")).Where(x=>x.Length>0).ToHashSet(StringComparer.Ordinal);
        AssignSplits(rows, frozen); Audit(rows, frozen); WriteManifest(Path.Combine(experiment,"split-manifest.csv"), rows);
        File.WriteAllText(Path.Combine(experiment,"configuration.txt"), "Seed="+Seed+"\nAlgoritmo=SHA256(seed|label|group|purpose), orden ascendente\nVisualFeatures=histogramas RGB y luminancia, 64 dimensiones, imagen 96x96\nVisualTrainer=SdcaLogisticRegression\nTextTrainer=FeaturizeText+SdcaLogisticRegression\nPDFVisual=primera pagina a 150 DPI normalizada\nTextPriority=MDOC luego OCR Tesseract local\n", new UTF8Encoding(false));
        var ml = new MLContext(seed: 17401);
        var visualTrain = rows.Where(x=>x.Split=="TRAIN").Select(x=>new VisualInput{Features=x.Features,Label=x.Label!="NO_DOCUMENTO"}).ToList();
        var visualData = ml.Data.LoadFromEnumerable(visualTrain);
        var visualPipeline = ml.Transforms.NormalizeMinMax("Features").Append(ml.BinaryClassification.Trainers.SdcaLogisticRegression(labelColumnName:"Label",featureColumnName:"Features",maximumNumberOfIterations:100));
        var visualModel = visualPipeline.Fit(visualData); ml.Model.Save(visualModel, visualData.Schema, Path.Combine(experiment,"visual-model.zip"));
        var visualEngine = ml.Model.CreatePredictionEngine<VisualInput,BinaryPrediction>(visualModel);
        var textTrain = rows.Where(x=>x.Split=="TRAIN"&&x.Label!="NO_DOCUMENTO").Select(x=>new TextInput{Text=x.Text,Label=x.Label=="FACTURA"}).ToList();
        var textData = ml.Data.LoadFromEnumerable(textTrain);
        var textPipeline = ml.Transforms.Text.FeaturizeText("Features",nameof(TextInput.Text)).Append(ml.BinaryClassification.Trainers.SdcaLogisticRegression(labelColumnName:"Label",featureColumnName:"Features",maximumNumberOfIterations:100));
        var textModel = textPipeline.Fit(textData); ml.Model.Save(textModel,textData.Schema,Path.Combine(experiment,"textual-model.zip"));
        var textEngine = ml.Model.CreatePredictionEngine<TextInput,BinaryPrediction>(textModel);
        foreach(var r in rows){var vp=visualEngine.Predict(new VisualInput{Features=r.Features});r.VisualDocument=vp.PredictedLabel;r.VisualProbability=vp.Probability;if(r.VisualDocument){var tp=textEngine.Predict(new TextInput{Text=r.Text});r.TextFactura=tp.PredictedLabel;r.TextProbability=tp.Probability;r.Prediction=tp.PredictedLabel?"FACTURA":"OTRO_DOCUMENTO";}else r.Prediction="NO_DOCUMENTO";}
        var test=rows.Where(x=>x.Split=="TEST").ToList();
        var visual=Binary(test,x=>x.Label!="NO_DOCUMENTO",x=>x.VisualDocument);
        var textTest=test.Where(x=>x.Label!="NO_DOCUMENTO").ToList();var textual=Binary(textTest,x=>x.Label=="FACTURA",x=>x.TextFactura);
        File.WriteAllText(Path.Combine(experiment,"visual-metrics.md"),BinaryMd("Modelo visual: DOCUMENTO vs NO_DOCUMENTO",visual,"DOCUMENTO -> NO_DOCUMENTO"),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(experiment,"textual-metrics.md"),BinaryMd("Modelo textual: FACTURA vs OTRO_DOCUMENTO",textual,"FACTURA -> OTRO_DOCUMENTO"),new UTF8Encoding(false));
        var matrix=Matrix(test);var end=EndMetrics(matrix,test.Count);File.WriteAllText(Path.Combine(experiment,"end-to-end-metrics.md"),EndMd(matrix,end),new UTF8Encoding(false));
        WriteMisclassified(Path.Combine(experiment,"misclassified.csv"),test);
        var critical=test.Where(x=>x.Label=="FACTURA"&&x.Prediction=="NO_DOCUMENTO").ToList();
        var suspicious=end.Accuracy>=.999||end.MacroF1>=.999;
        var summary="# H1D4A — primer experimento local/offline\n\n- Seed: `"+Seed+"`\n- TRAIN: "+rows.Where(x=>x.Split=="TRAIN").Select(x=>x.GroupId).Distinct().Count()+" grupos, "+rows.Count(x=>x.Split=="TRAIN")+" archivos\n- VALIDATION: "+rows.Where(x=>x.Split=="VALIDATION").Select(x=>x.GroupId).Distinct().Count()+" grupos, "+rows.Count(x=>x.Split=="VALIDATION")+" archivos\n- TEST: "+rows.Where(x=>x.Split=="TEST").Select(x=>x.GroupId).Distinct().Count()+" grupos, "+test.Count+" archivos\n- Texto MDOC: "+rows.Count(x=>x.TextOrigin=="MDOC")+"\n- Texto OCR: "+rows.Count(x=>x.TextOrigin=="OCR")+"\n- Sin texto: "+rows.Count(x=>x.TextOrigin=="NONE")+"\n- Visual accuracy: "+visual.Accuracy.ToString("0.0000")+"\n- Textual accuracy: "+textual.Accuracy.ToString("0.0000")+"\n- End-to-end accuracy: "+end.Accuracy.ToString("0.0000")+"\n- End-to-end macro F1: "+end.MacroF1.ToString("0.0000")+"\n- FACTURA -> NO_DOCUMENTO: "+critical.Count+"\n- Leakage GroupId/hash: 0\n- Resultado sospechosamente perfecto: "+(suspicious?"Sí; auditar similitud de templates y tamaño reducido de TEST.":"No")+"\n\n## Errores críticos\n"+string.Join("\n",critical.Select(x=>"- "+Path.GetFileName(x.Path)+"; GroupId="+x.GroupId+"; visualProb="+x.VisualProbability.ToString("0.0000")+"; textLen="+x.TextLen+"; formato="+x.Format))+"\n";
        File.WriteAllText(Path.Combine(experiment,"resumen.md"),summary,new UTF8Encoding(false));
        var zipPath=Path.Combine(root,"H1D4A_Resultados.zip");if(File.Exists(zipPath))throw new IOException("El ZIP ya existe.");using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create))foreach(var p in Directory.GetFiles(experiment).Where(x=>!x.EndsWith("assets.csv",StringComparison.OrdinalIgnoreCase)))zip.CreateEntryFromFile(p,Path.GetFileName(p),CompressionLevel.Optimal);
        Console.WriteLine($"H1D4A | TRAIN={rows.Count(x=>x.Split=="TRAIN")} | VALIDATION={rows.Count(x=>x.Split=="VALIDATION")} | TEST={test.Count} | VisualAccuracy={visual.Accuracy:0.0000} | TextAccuracy={textual.Accuracy:0.0000} | EndAccuracy={end.Accuracy:0.0000} | MacroF1={end.MacroF1:0.0000} | FacturaANoDocumento={critical.Count} | ZIP={zipPath}");
        return 0;
    }
    static void AssignSplits(List<Row> rows,HashSet<string> frozen){var targets=new Dictionary<string,(int Val,int Test)>{{"FACTURA",(2,3)},{"OTRO_DOCUMENTO",(3,3)},{"NO_DOCUMENTO",(3,3)}};foreach(var label in Classes){var groups=rows.Where(x=>x.Label==label).Select(x=>x.GroupId).Distinct().ToList();var test=groups.Where(frozen.Contains).ToHashSet();foreach(var g in groups.Where(x=>!test.Contains(x)).OrderBy(x=>Key(label,x,"TEST")).Take(Math.Max(0,targets[label].Test-test.Count)))test.Add(g);var val=groups.Where(x=>!test.Contains(x)).OrderBy(x=>Key(label,x,"VALIDATION")).Take(targets[label].Val).ToHashSet();foreach(var r in rows.Where(x=>x.Label==label))r.Split=test.Contains(r.GroupId)?"TEST":val.Contains(r.GroupId)?"VALIDATION":"TRAIN";}}
    static string Key(string l,string g,string p)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Seed+"|"+l+"|"+g+"|"+p)));
    static void Audit(List<Row> rows,HashSet<string> frozen){if(rows.GroupBy(x=>x.GroupId).Any(g=>g.Select(x=>x.Split).Distinct().Count()>1))throw new InvalidDataException("Leakage GroupId.");if(rows.GroupBy(x=>x.Sha256,StringComparer.OrdinalIgnoreCase).Any(g=>g.Select(x=>x.Split).Distinct().Count()>1))throw new InvalidDataException("Leakage hash.");if(rows.Any(x=>frozen.Contains(x.GroupId)&&x.Split!="TEST"))throw new InvalidDataException("Frozen fuera de TEST.");foreach(var s in new[]{"TRAIN","VALIDATION","TEST"})foreach(var l in Classes)if(!rows.Any(x=>x.Split==s&&x.Label==l))throw new InvalidDataException("Falta "+l+" en "+s);}
    static BinaryMetrics Binary<T>(List<T> rows,Func<T,bool> actual,Func<T,bool> pred){var tp=rows.Count(x=>actual(x)&&pred(x));var tn=rows.Count(x=>!actual(x)&&!pred(x));var fp=rows.Count(x=>!actual(x)&&pred(x));var fn=rows.Count(x=>actual(x)&&!pred(x));return new BinaryMetrics(tp,tn,fp,fn);}
    static int[,] Matrix(List<Row> rows){var m=new int[3,3];foreach(var r in rows)m[Array.IndexOf(Classes,r.Label),Array.IndexOf(Classes,r.Prediction)]++;return m;}
    static EndResult EndMetrics(int[,]m,int total){var f=new List<double>();for(var c=0;c<3;c++){var tp=m[c,c];var fp=Enumerable.Range(0,3).Where(x=>x!=c).Sum(x=>m[x,c]);var fn=Enumerable.Range(0,3).Where(x=>x!=c).Sum(x=>m[c,x]);var p=Div(tp,tp+fp);var r=Div(tp,tp+fn);f.Add(Div(2*p*r,p+r));}return new EndResult(Div(Enumerable.Range(0,3).Sum(x=>m[x,x]),total),f.Average());}
    static string BinaryMd(string title,BinaryMetrics m,string critical)=>"# "+title+"\n\n- Accuracy: "+m.Accuracy.ToString("0.0000")+"\n- Precision: "+m.Precision.ToString("0.0000")+"\n- Recall: "+m.Recall.ToString("0.0000")+"\n- F1: "+m.F1.ToString("0.0000")+"\n- "+critical+": "+m.FN+"\n\n| | Pred + | Pred - |\n|---|---:|---:|\n| Real + | "+m.TP+" | "+m.FN+" |\n| Real - | "+m.FP+" | "+m.TN+" |\n";
    static string EndMd(int[,]m,EndResult e){var sb=new StringBuilder("# Pipeline end-to-end\n\n| real/pred | FACTURA | OTRO_DOCUMENTO | NO_DOCUMENTO |\n|---|---:|---:|---:|\n");for(var r=0;r<3;r++)sb.AppendLine("| "+Classes[r]+" | "+m[r,0]+" | "+m[r,1]+" | "+m[r,2]+" |");sb.AppendLine("\n- Accuracy: "+e.Accuracy.ToString("0.0000"));sb.AppendLine("- Macro F1: "+e.MacroF1.ToString("0.0000"));for(var c=0;c<3;c++){var tp=m[c,c];var fp=Enumerable.Range(0,3).Where(x=>x!=c).Sum(x=>m[x,c]);var fn=Enumerable.Range(0,3).Where(x=>x!=c).Sum(x=>m[c,x]);var p=Div(tp,tp+fp);var rr=Div(tp,tp+fn);sb.AppendLine("- "+Classes[c]+": precision="+p.ToString("0.0000")+", recall="+rr.ToString("0.0000")+", F1="+Div(2*p*rr,p+rr).ToString("0.0000"));}return sb.ToString();}
    static void WriteManifest(string path,IEnumerable<Row>r){var l=new List<string>{"Path,Label,GroupId,SourceType,Sha256,Split,OriginalPath,Diversity,VisualPath,TextLen,TextOrigin,PhysicalFormat"};l.AddRange(r.Select(x=>string.Join(",",new[]{x.Path,x.Label,x.GroupId,x.SourceType,x.Sha256,x.Split,x.OriginalPath,x.Diversity,x.VisualPath,x.TextLen.ToString(),x.TextOrigin,x.Format}.Select(Csv))));File.WriteAllLines(path,l,new UTF8Encoding(false));}
    static void WriteMisclassified(string path,IEnumerable<Row>r){var l=new List<string>{"filename,GroupId,LabelReal,Prediccion,VisualProbability,TextProbability,Split,SourceType,Diversity,TextLen,TextOrigin,PhysicalFormat,ObservacionTecnica"};l.AddRange(r.Where(x=>x.Label!=x.Prediction).Select(x=>string.Join(",",new[]{Path.GetFileName(x.Path),x.GroupId,x.Label,x.Prediction,x.VisualProbability.ToString("R"),x.TextProbability.ToString("R"),x.Split,x.SourceType,x.Diversity,x.TextLen.ToString(),x.TextOrigin,x.Format,"Primera configuración; no corregir etiqueta automáticamente."}.Select(Csv))));File.WriteAllLines(path,l,new UTF8Encoding(false));}
    static List<Row> Load(string path){var z=new List<Row>();using var p=new TextFieldParser(path){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true};p.SetDelimiters(",");var h=p.ReadFields()!;var c=h.Select((n,i)=>(n,i)).ToDictionary(x=>x.n,x=>x.i);while(!p.EndOfData){var f=p.ReadFields();if(f==null)continue;string V(string n)=>f[c[n]];z.Add(new Row{Path=V("Path"),Label=V("Label"),GroupId=V("GroupId"),SourceType=V("SourceType"),Sha256=V("Sha256"),OriginalPath=V("OriginalPath"),Diversity=V("Diversity"),VisualPath=V("VisualPath"),Features=V("VisualFeatures").Split(';').Select(x=>float.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray(),Text=Encoding.UTF8.GetString(Convert.FromBase64String(V("TextBase64"))),TextOrigin=V("TextOrigin"),TextLen=int.Parse(V("TextLen")),Format=V("PhysicalFormat")});}return z;}
    static string Csv(string s)=>"\""+(s??"").Replace("\"","\"\"")+"\"";static double Div(double a,double b)=>b==0?0:a/b;
    sealed class Row{public string Path="",Label="",GroupId="",SourceType="",Sha256="",OriginalPath="",Diversity="",VisualPath="",Text="",TextOrigin="",Format="",Split="",Prediction="";public float[] Features=Array.Empty<float>();public int TextLen;public bool VisualDocument,TextFactura;public float VisualProbability,TextProbability;}
    sealed class VisualInput{[VectorType(64)]public float[] Features{get;set;}=Array.Empty<float>();public bool Label{get;set;}}
    sealed class TextInput{public string Text{get;set;}="";public bool Label{get;set;}}
    sealed class BinaryPrediction{[ColumnName("PredictedLabel")]public bool PredictedLabel{get;set;}public float Probability{get;set;}public float Score{get;set;}}
    sealed class BinaryMetrics{public BinaryMetrics(int tp,int tn,int fp,int fn){TP=tp;TN=tn;FP=fp;FN=fn;}public int TP,TN,FP,FN;public double Accuracy=>Div(TP+TN,TP+TN+FP+FN);public double Precision=>Div(TP,TP+FP);public double Recall=>Div(TP,TP+FN);public double F1=>Div(2*Precision*Recall,Precision+Recall);}
    sealed record EndResult(double Accuracy,double MacroF1);
}
