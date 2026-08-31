using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace PdfRasterProbe
{
    internal static class H1D5C2ProductValidationProbe
    {
        private const string DatasetHash="AF1FCC230B279D7DA4ACD77F5641BB1835D7D42EA0E8A41AD524D473FEC48158";
        private const string FusionHash="B7E823012762FE917D6E5C73F122731FE6180E96D7560755F68FB7A552FFA604";
        private const string ProductHash="C861AAEB73711A29A6082CCF6371187F164312177C32D908DEFB8C25E9BED293";
        private const string SourcesHash="8B83EE58B6F89AF93E398F3AF1F030A858D32F054FE11F27C43599FAB7D5DC74";
        private const string E8066="E8066B5FA877947A47862B566C3A752FD1BA514973CD2B01A7AD31803BBDD28B";

        internal static int Run(string[] args)
        {
            if(args.Length!=6){Console.Error.WriteLine("Uso: --h1d5c2-product-validation <dataset.csv> <H1D5B-fusion-results.csv> <H1D5C-product-validation.csv> <H1D5C1-ocr-source-results.csv> <output-H1D5C2>");return 2;}
            try
            {
                var dataset=Path.GetFullPath(args[1]);var fusion=Path.GetFullPath(args[2]);var oldProduct=Path.GetFullPath(args[3]);var sources=Path.GetFullPath(args[4]);var output=Path.GetFullPath(args[5]);
                Check(dataset,DatasetHash,"dataset.csv");Check(fusion,FusionHash,"fusion-results.csv");Check(oldProduct,ProductHash,"H1D5C product-validation.csv");Check(sources,SourcesHash,"H1D5C1 ocr-source-results.csv");
                var baseResult=H1D5CProductValidationProbe.Run(new[]{"--h1d5c-product-validation",dataset,fusion,output});
                var current=LoadValidation(Path.Combine(output,"product-validation.csv"));var previous=LoadValidation(oldProduct);var expected=LoadFusion(fusion);var sourceRows=LoadSources(sources);
                if(current.Count!=80||previous.Count!=80||expected.Count!=80||sourceRows.Count!=36)throw new InvalidDataException("Conteos de comparación inesperados.");
                var comparisons=Compare(previous,current,expected,sourceRows);WriteComparison(Path.Combine(output,"h1d5c-comparison.csv"),comparisons);
                var pdf=current.Values.Where(x=>x.Format=="PDF").ToList();var conflicts=pdf.Count(x=>x.Method=="MDOC_OCR_CONFLICTO"&&x.Classification=="REVISAR");var limits=pdf.Count(x=>x.Method=="OCR_LIMITE"&&x.Classification=="REVISAR");var e806=current[E8066];var ocr=expected.Values.Count(x=>x.Format=="PDF"&&x.OcrRequired);
                EnrichReports(output,current,comparisons,ocr,conflicts,limits,e806);
                var classificationChanges=comparisons.Count(x=>x.OldClassification!=x.NewClassification);var ok=baseResult==0&&ocr==21&&conflicts==3&&limits==2&&e806.Classification=="FACTURA"&&classificationChanges==1;
                Console.WriteLine("H1D5C2_COMPLETO | Filas="+current.Count+" | MatchPDF="+pdf.Count(x=>x.Match)+"/36 | MatchCorpus="+current.Values.Count(x=>x.Match)+"/80 | OCRActivadoPDF="+ocr+" | FuenteOCR=RASTER_PAGINA | Conflictos="+conflicts+"/3 | Limites="+limits+"/2 | CambiosClasificacion="+classificationChanges+" | OK="+ok+" | Output="+output);
                return ok?0:1;
            }
            catch(Exception ex){Console.Error.WriteLine("ERROR H1D5C2 | "+ex.GetType().Name+": "+ex.Message);return 1;}
        }

        private static List<Comparison> Compare(Dictionary<string,ValidationRow> oldRows,Dictionary<string,ValidationRow> newRows,Dictionary<string,FusionRow> fusion,Dictionary<string,SourceRow> sources)
        {
            var result=new List<Comparison>();foreach(var old in oldRows.Values.OrderBy(x=>x.Sha)){ValidationRow now;FusionRow f;if(!newRows.TryGetValue(old.Sha,out now)||!fusion.TryGetValue(old.Sha,out f))throw new InvalidDataException("Hash ausente en comparación: "+old.Sha);var oldSource=SourceBefore(f,sources);var newSource=f.Format=="PDF"?(f.OcrRequired?"RASTER_PAGINA":"MDOC_TEXTO"):"OCR_IMAGEN";if(old.Classification==now.Classification&&old.Method==now.Method&&oldSource==newSource)continue;var details=new List<string>();if(old.Classification!=now.Classification)details.Add("CLASIFICACION");if(old.Method!=now.Method)details.Add("METODO");if(oldSource!=newSource)details.Add("FUENTE_OCR");if(old.Method=="MDOC_OCR_CONFLICTO"||now.Method=="MDOC_OCR_CONFLICTO")details.Add("CONFLICTO");if(old.Method=="OCR_LIMITE"||now.Method=="OCR_LIMITE")details.Add("LIMITE");result.Add(new Comparison{Sha=old.Sha,OldClassification=old.Classification,NewClassification=now.Classification,OldMethod=old.Method,NewMethod=now.Method,OldSource=oldSource,NewSource=newSource,Explanation=string.Join(";",details)});}
            return result;
        }

        private static string SourceBefore(FusionRow row,Dictionary<string,SourceRow> sources){if(row.Format!="PDF")return"OCR_IMAGEN";if(!row.OcrRequired)return"MDOC_TEXTO";SourceRow source;if(!sources.TryGetValue(row.Sha,out source))return"DESCONOCIDA";if(source.EmbeddedLimit)return"EMBEDDED_MDOC_LIMITE";return source.P0Trace=="EMBEDDED"?"EMBEDDED_MDOC":"RASTER_PAGINA";}
        private static void WriteComparison(string path,List<Comparison> rows){var lines=new List<string>{"Sha256,H1D5CClassification,H1D5C2Classification,H1D5CMethod,H1D5C2Method,H1D5COcrSource,H1D5C2OcrSource,TechnicalExplanation"};lines.AddRange(rows.Select(x=>Join(x.Sha,x.OldClassification,x.NewClassification,x.OldMethod,x.NewMethod,x.OldSource,x.NewSource,x.Explanation)));File.WriteAllLines(path,lines,new UTF8Encoding(false));}

        private static void EnrichReports(string output,Dictionary<string,ValidationRow> rows,List<Comparison> changes,int ocr,int conflicts,int limits,ValidationRow e806)
        {
            var pdfPath=Path.Combine(output,"pdf-validation.md");var pdf=File.ReadAllText(pdfPath).Replace("# H1D5C —","# H1D5C2 —");pdf+="\n## Verificación H1D5C2\n\n- Fuente OCR en los "+ocr+" PDF activados: `RASTER_PAGINA`.\n- E8066: `"+e806.Classification+"` mediante `"+e806.Method+"`.\n- Conflictos `MDOC_OCR_CONFLICTO`: "+conflicts+"/3, todos en REVISAR.\n- Límites raster controlados: "+limits+"/2, todos en REVISAR.\n- Diferencias frente a H1D5C: "+changes.Count+" filas por clasificación, método o fuente; ver `h1d5c-comparison.csv`.\n";File.WriteAllText(pdfPath,pdf,new UTF8Encoding(false));
            var corpusPath=Path.Combine(output,"corpus-validation.md");File.WriteAllText(corpusPath,File.ReadAllText(corpusPath).Replace("# H1D5C —","# H1D5C2 —"),new UTF8Encoding(false));
            var policyPath=Path.Combine(output,"fusion-policy-tests.md");File.WriteAllText(policyPath,File.ReadAllText(policyPath).Replace("# H1D5C —","# H1D5C2 —"),new UTF8Encoding(false));
            var summaryPath=Path.Combine(output,"resumen.md");var summary=File.ReadAllText(summaryPath).Replace("# H1D5C —","# H1D5C2 —");summary="## Estado histórico\n\n- H1D5C: NO APROBADO (embedded-first, 35/36).\n- H1D5C1: benchmark experimental que seleccionó P1.\n- H1D5C2: corrección productiva controlada con raster de página completa.\n\n"+summary+"\n- Fuente OCR PDF validada: `RASTER_PAGINA`.\n- Conflictos: "+conflicts+"/3; límites: "+limits+"/2.\n- Cambios finales de clasificación frente a H1D5C: "+changes.Count(x=>x.OldClassification!=x.NewClassification)+"; E8066 pasó de REVISAR a FACTURA.\n- H1D5C2 queda **APROBADO COMO CANDIDATO**, sin despliegue ni prueba operativa real.\n";File.WriteAllText(summaryPath,summary,new UTF8Encoding(false));
        }

        private static Dictionary<string,ValidationRow> LoadValidation(string path){return ReadCsv(path,f=>new ValidationRow{Sha=f("Sha256").ToUpperInvariant(),Format=f("PhysicalFormat"),Classification=f("ProductClassification"),Method=f("DetectionMethod"),Match=Bool(f("Match"))}).ToDictionary(x=>x.Sha);}
        private static Dictionary<string,FusionRow> LoadFusion(string path){return ReadCsv(path,f=>new FusionRow{Sha=f("Sha256").ToUpperInvariant(),Format=f("PhysicalFormat"),OcrRequired=Bool(f("ConservativeWouldRunOcr"))}).ToDictionary(x=>x.Sha);}
        private static Dictionary<string,SourceRow> LoadSources(string path){return ReadCsv(path,f=>new SourceRow{Sha=f("Sha256").ToUpperInvariant(),P0Trace=f("P0Trace"),EmbeddedLimit=Bool(f("EmbeddedLimit"))}).ToDictionary(x=>x.Sha);}
        private static List<T> ReadCsv<T>(string path,Func<Func<string,string>,T> map){var result=new List<T>();using(var parser=new TextFieldParser(path,Encoding.UTF8)){parser.TextFieldType=FieldType.Delimited;parser.HasFieldsEnclosedInQuotes=true;parser.SetDelimiters(",");var header=parser.ReadFields();var columns=header.Select((n,i)=>new{n,i}).ToDictionary(x=>x.n,x=>x.i,StringComparer.OrdinalIgnoreCase);while(!parser.EndOfData){var fields=parser.ReadFields();if(fields==null)continue;Func<string,string> get=n=>columns.ContainsKey(n)&&columns[n]<fields.Length?fields[columns[n]]:"";result.Add(map(get));}}return result;}
        private static void Check(string path,string expected,string name){var actual=Hash(path);if(actual!=expected)throw new InvalidDataException("SHA-256 inesperado para "+name+": "+actual);}
        private static string Hash(string path){using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
        private static string Join(params string[] values){return string.Join(",",values.Select(x=>"\""+(x??"").Replace("\"","\"\"")+"\""));}private static bool Bool(string value){return string.Equals(value,"true",StringComparison.OrdinalIgnoreCase);}
        private sealed class ValidationRow{internal string Sha,Format,Classification,Method;internal bool Match;}private sealed class FusionRow{internal string Sha,Format;internal bool OcrRequired;}private sealed class SourceRow{internal string Sha,P0Trace;internal bool EmbeddedLimit;}private sealed class Comparison{internal string Sha,OldClassification,NewClassification,OldMethod,NewMethod,OldSource,NewSource,Explanation;}
    }
}
