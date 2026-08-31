using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace DocumentAiProbe;

internal static class Program
{
    const int Target = 20;
    static readonly string[] Labels = { "FACTURA", "OTRO_DOCUMENTO", "NO_DOCUMENTO" };
    static readonly string[] Splits = { "", "TRAIN", "VALIDATION", "TEST" };

    static int Main(string[] args)
    {
        try {
            var root = ProjectRoot(); var cmd = args.Length == 0 ? "audit" : args[0].ToLowerInvariant();
            var csv = Path.GetFullPath(Opt(args,"--dataset") ?? Path.Combine(root,"dataset.csv"));
            var corpus = Path.GetFullPath(Opt(args,"--corpus") ?? Path.Combine(root,"Corpus"));
            return cmd switch { "add"=>Add(args,csv,corpus), "import-reviewed"=>ImportReviewed(args,csv,corpus,root), "migrate"=>Migrate(csv,corpus), "split"=>Split(csv,root), "report"=>Report(csv,Path.GetFullPath(Opt(args,"--out")??Path.Combine(root,"corpus-report.md"))), "audit"=>Audit(csv,true), "experiment-h1d4a"=>H1D4AExperiment.Run(root), "diagnose-h1d4b"=>H1D4BDiagnostic.Run(root), "experiment-h1d4c"=>H1D4CExperiment.Run(root), _=>Usage() };
        } catch(Exception ex) { Console.Error.WriteLine("ERROR: "+ex.Message); return 1; }
    }

    static int Add(string[] a,string csv,string corpus)
    {
        var src=Req(a,"--file"); var label=Req(a,"--label").ToUpperInvariant(); var group=Req(a,"--group"); var type=Req(a,"--source-type").ToUpperInvariant(); var diversity=Opt(a,"--diversity")??"SIN_CLASIFICAR";
        AddEvidence(src,label,group,type,diversity,csv,corpus,null); return Audit(csv,false);
    }

    static void AddEvidence(string src,string label,string group,string type,string diversity,string csv,string corpus,string? expectedHash)
    {
        var rows=Load(csv); var errors=ValidateAdd(src,label,group,expectedHash,rows,corpus); if(errors.Count>0)throw new InvalidDataException(string.Join("; ",errors));var hash=Hash(src);
        var dir=Path.Combine(corpus,label);Directory.CreateDirectory(dir);var dst=Destination(dir,hash,src);File.Copy(src,dst,false);rows.Add(new(dst,label,group,type,hash,"",Path.GetFullPath(src),diversity));Save(csv,rows);Console.WriteLine($"INCORPORADO | Label={label} | GroupId={group} | SHA256={hash} | Copia={dst}");
    }

    static List<string> ValidateAdd(string src,string label,string group,string? expectedHash,List<Row> rows,string corpus)
    {
        var e=new List<string>();if(!Labels.Contains(label))e.Add("Label inválido");if(string.IsNullOrWhiteSpace(group)||!System.Text.RegularExpressions.Regex.IsMatch(group,@"^[a-z0-9][a-z0-9._-]{2,99}$"))e.Add("GroupId inválido");if(!File.Exists(src)){e.Add("Archivo inexistente");return e;}var hash=Hash(src);if(!string.IsNullOrWhiteSpace(expectedHash)&&!hash.Equals(expectedHash,StringComparison.OrdinalIgnoreCase))e.Add("SHA-256 distinto al esperado");if(rows.Any(x=>x.Sha256.Equals(hash,StringComparison.OrdinalIgnoreCase)))e.Add("Hash ya presente");if(rows.Any(x=>x.GroupId==group&&x.Label!=label))e.Add("Conflicto de Label para GroupId");if(Labels.Contains(label)&&!string.IsNullOrWhiteSpace(group)){var dst=Destination(Path.Combine(corpus,label),hash,src);if(File.Exists(dst))e.Add("Destino de copia ya existente");}return e;
    }

    static int ImportReviewed(string[] args,string csv,string corpus,string root,Action<int>? afterPrepared=null)
    {
        if(args.Length<2||args[1].StartsWith("--"))throw new ArgumentException("Falta reviewed-decisions.csv.");var decisionsPath=Path.GetFullPath(args[1]);var dry=args.Any(x=>x.Equals("--dry-run",StringComparison.OrdinalIgnoreCase));var decisions=LoadDecisions(decisionsPath);var rows=Load(csv);var errors=new List<string>();
        foreach(var g in decisions.GroupBy(x=>x.CandidateId,StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1))errors.Add("CandidateId repetido: "+g.Key);foreach(var g in decisions.Where(x=>x.ExpectedSha256.Length>0).GroupBy(x=>x.ExpectedSha256,StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1))errors.Add("Misma evidencia repetida en lote: "+g.Key);
        foreach(var g in decisions.Where(x=>!string.IsNullOrWhiteSpace(x.GroupId)).GroupBy(x=>x.GroupId,StringComparer.Ordinal).Where(x=>x.Select(y=>y.Label).Distinct(StringComparer.Ordinal).Count()>1))errors.Add("GroupId con etiquetas diferentes en lote: "+g.Key);
        foreach(var d in decisions){if(!new[]{"ADD","SKIP","PENDING"}.Contains(d.Action))errors.Add(d.CandidateId+": Action inválida");if(string.IsNullOrWhiteSpace(d.CandidateId))errors.Add("CandidateId vacío");if(!Labels.Contains(d.Label))errors.Add(d.CandidateId+": Label inválido");if(d.Action!="PENDING"&&!ValidGroup(d.GroupId))errors.Add(d.CandidateId+": GroupId inválido");if(d.Action=="PENDING"&&!string.IsNullOrWhiteSpace(d.GroupId)&&!ValidGroup(d.GroupId))errors.Add(d.CandidateId+": GroupId inválido");if(string.IsNullOrWhiteSpace(d.ExpectedSha256)||!System.Text.RegularExpressions.Regex.IsMatch(d.ExpectedSha256,@"^[0-9A-F]{64}$"))errors.Add(d.CandidateId+": ExpectedSha256 inválido");if(!File.Exists(d.OriginalPath))errors.Add(d.CandidateId+": archivo inexistente");else if(!Hash(d.OriginalPath).Equals(d.ExpectedSha256,StringComparison.OrdinalIgnoreCase))errors.Add(d.CandidateId+": SHA-256 distinto al inventariado");if(d.Action=="ADD")foreach(var x in ValidateAdd(d.OriginalPath,d.Label,d.GroupId,d.ExpectedSha256,rows,corpus))errors.Add(d.CandidateId+": "+x);}
        var plans=decisions.Where(x=>x.Action=="ADD"&&File.Exists(x.OriginalPath)).Select(d=>PlanAdd(d,corpus)).ToList();
        foreach(var g in plans.GroupBy(x=>x.Destination,StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1))errors.Add("Mismo destino proyectado en lote: "+g.Key);
        Console.WriteLine($"IMPORT_REVIEWED | DryRun={dry} | ADD={decisions.Count(x=>x.Action=="ADD")} | SKIP={decisions.Count(x=>x.Action=="SKIP")} | PENDING={decisions.Count(x=>x.Action=="PENDING")} | Errores={errors.Count}");foreach(var d in decisions){Console.WriteLine($"{d.Action} | {d.CandidateId} | Label={d.Label} | GroupId={d.GroupId} | Motivo={d.Notes}");if(d.Action=="ADD"&&File.Exists(d.OriginalPath)){var format=DetectFormat(d.OriginalPath);Console.WriteLine($"ADD_FORMAT | {d.CandidateId} | extension={DisplayExtension(Path.GetExtension(d.OriginalPath))} | detected={format.Name} | SourceType={format.SourceType} | normalized={DisplayExtension(DestinationExtension(d.OriginalPath,format))}");}}foreach(var e in errors)Console.WriteLine("ERROR | "+e);
        var before=Snapshot(rows);var additions=decisions.Where(x=>x.Action=="ADD").ToList();var projected=rows.Concat(additions.Select(d=>new Row("",d.Label,d.GroupId,"",d.ExpectedSha256,"",d.OriginalPath,""))).ToList();Console.WriteLine("CAMBIO_ESPERADO | "+DescribeDelta(before,Snapshot(projected)));
        if(errors.Count>0){WriteImportReport(root,decisionsPath,decisions,errors,before,before,false);return 2;}if(dry){Console.WriteLine("DRY_RUN | Copias=0 | DatasetModificado=No");return 0;}
        try{ApplyReviewedBatch(csv,corpus,rows,plans,afterPrepared);}catch(Exception ex){errors.Add("Aplicación revertida: "+ex.Message);Console.WriteLine("ERROR | "+errors[^1]);WriteImportReport(root,decisionsPath,decisions,errors,before,before,false);return 2;}var after=Snapshot(Load(csv));WriteImportReport(root,decisionsPath,decisions,errors,before,after,true);Console.WriteLine("IMPORTACION | Aplicada=Sí | "+DescribeDelta(before,after));return 0;
    }

    static AddPlan PlanAdd(Decision decision,string corpus){var hash=Hash(decision.OriginalPath);var format=DetectFormat(decision.OriginalPath);var destination=Destination(Path.Combine(corpus,decision.Label),hash,decision.OriginalPath,format);return new(decision,hash,format,destination);}

    static void ApplyReviewedBatch(string csv,string corpus,List<Row> current,List<AddPlan> plans,Action<int>? afterPrepared)
    {
        var id=Guid.NewGuid().ToString("N");var staging=Path.Combine(corpus,".import-reviewed-"+id);var csvDirectory=Path.GetDirectoryName(Path.GetFullPath(csv))!;var temporaryCsv=Path.Combine(csvDirectory,"."+Path.GetFileName(csv)+"."+id+".tmp");var backupCsv=Path.Combine(csvDirectory,"."+Path.GetFileName(csv)+"."+id+".bak");var created=new List<string>();var datasetReplaced=false;
        try{
            Directory.CreateDirectory(staging);var staged=new List<(AddPlan Plan,string Path)>();var prepared=0;
            foreach(var plan in plans){var path=Path.Combine(staging,prepared.ToString("D6")+DestinationExtension(plan.Decision.OriginalPath,plan.Format));File.Copy(plan.Decision.OriginalPath,path,false);staged.Add((plan,path));prepared++;afterPrepared?.Invoke(prepared);}
            var updated=current.Concat(plans.Select(p=>new Row(p.Destination,p.Decision.Label,p.Decision.GroupId,p.Format.SourceType,p.Hash,"",Path.GetFullPath(p.Decision.OriginalPath),"REVIEWED_BATCH"))).ToList();Save(temporaryCsv,updated);
            foreach(var item in staged){Directory.CreateDirectory(Path.GetDirectoryName(item.Plan.Destination)!);File.Move(item.Path,item.Plan.Destination,false);created.Add(item.Plan.Destination);}
            Directory.Delete(staging,true);File.Replace(temporaryCsv,csv,backupCsv,true);datasetReplaced=true;File.Delete(backupCsv);
        }catch{
            if(datasetReplaced&&File.Exists(backupCsv))File.Replace(backupCsv,csv,null,true);
            foreach(var path in created)if(File.Exists(path))File.Delete(path);
            if(File.Exists(temporaryCsv))File.Delete(temporaryCsv);if(File.Exists(backupCsv))File.Delete(backupCsv);if(Directory.Exists(staging))Directory.Delete(staging,true);throw;
        }
    }

    static List<Decision> LoadDecisions(string path){var result=new List<Decision>();using var p=new TextFieldParser(path){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true};p.SetDelimiters(",");var h=p.ReadFields()??throw new InvalidDataException("CSV sin header");var c=h.Select((n,i)=>(n,i)).ToDictionary(x=>x.n,x=>x.i,StringComparer.OrdinalIgnoreCase);foreach(var n in new[]{"CandidateId","OriginalPath","ExpectedSha256","Action","Label","GroupId","Notes"})if(!c.ContainsKey(n))throw new InvalidDataException("Falta columna "+n);while(!p.EndOfData){var f=p.ReadFields();if(f==null)continue;string V(string n)=>f[c[n]].Trim();result.Add(new(V("CandidateId"),V("OriginalPath"),V("ExpectedSha256").ToUpperInvariant(),V("Action").ToUpperInvariant(),V("Label").ToUpperInvariant(),V("GroupId"),V("Notes")));}return result;}
    static Dictionary<string,(int Files,int Groups)> Snapshot(List<Row> rows)=>Labels.ToDictionary(l=>l,l=>(rows.Count(x=>x.Label==l),rows.Where(x=>x.Label==l).Select(x=>x.GroupId).Distinct().Count()));
    static string DescribeDelta(Dictionary<string,(int Files,int Groups)> b,Dictionary<string,(int Files,int Groups)> a)=>string.Join(" | ",Labels.Select(l=>$"{l}: archivos {b[l].Files}->{a[l].Files}, grupos {b[l].Groups}->{a[l].Groups}"));
    static string SourceType(string path)=>DetectFormat(path).SourceType;
    static bool ValidGroup(string group)=>!string.IsNullOrWhiteSpace(group)&&System.Text.RegularExpressions.Regex.IsMatch(group,@"^[a-z0-9][a-z0-9._-]{2,99}$");
    static void WriteImportReport(string root,string source,List<Decision> d,List<string> errors,Dictionary<string,(int Files,int Groups)> before,Dictionary<string,(int Files,int Groups)> after,bool applied){var sb=new StringBuilder("# Reviewed import report\n\n");sb.AppendLine("- Fecha/hora: "+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));sb.AppendLine("- CSV: `"+source+"`");sb.AppendLine("- Aplicado: "+(applied?"Sí":"No"));sb.AppendLine($"- ADD: {d.Count(x=>x.Action=="ADD")}\n- SKIP: {d.Count(x=>x.Action=="SKIP")}\n- PENDING: {d.Count(x=>x.Action=="PENDING")}\n- Errores: {errors.Count}\n");sb.AppendLine("## Decisiones");foreach(var x in d)sb.AppendLine($"- {x.CandidateId}: {x.Action}; Label={x.Label}; GroupId={x.GroupId}; SHA-256={x.ExpectedSha256}; {x.Notes}");if(errors.Count>0){sb.AppendLine("\n## Errores");foreach(var e in errors)sb.AppendLine("- "+e);}sb.AppendLine("\n## Distribución\n\n"+DescribeDelta(before,after));File.WriteAllText(Path.Combine(root,"reviewed-import-report.md"),sb.ToString(),new UTF8Encoding(false));}

    static int Migrate(string csv,string corpus)
    {
        var rows=Load(csv); var n=0;
        foreach(var r in rows) { if(Under(r.Path,corpus)) continue; if(!File.Exists(r.Path)) throw new FileNotFoundException("Original inexistente.",r.Path); var dir=Path.Combine(corpus,r.Label); Directory.CreateDirectory(dir); var dst=Destination(dir,r.Sha256,r.Path); File.Copy(r.Path,dst,false); r.OriginalPath=Path.GetFullPath(r.Path); r.Path=dst; n++; }
        Save(csv,rows); Console.WriteLine($"MIGRACION | CopiasCreadas={n} | OriginalesMovidos=0"); return Audit(csv,false);
    }

    static int Split(string csv,string root)
    {
        var rows=Load(csv); var freezePath=Path.Combine(root,"frozen-test-groups.txt");
        var frozen=(File.Exists(freezePath)?File.ReadAllLines(freezePath):rows.Where(x=>x.Split=="TEST").Select(x=>x.GroupId)).Where(x=>x.Length>0).ToHashSet(StringComparer.Ordinal);
        foreach(var label in Labels) {
            var groups=rows.Where(x=>x.Label==label).Select(x=>x.GroupId).Distinct().OrderBy(x=>Key(label+"|"+x)).ToList();
            var desiredTest=groups.Count<3?0:Math.Max(1,(int)Math.Round(groups.Count*.15)); foreach(var g in groups.Where(x=>!frozen.Contains(x)).Take(Math.Max(0,desiredTest-groups.Count(frozen.Contains)))) frozen.Add(g);
            var nonTest=groups.Where(x=>!frozen.Contains(x)).ToList(); var valCount=nonTest.Count<2?0:Math.Max(1,(int)Math.Round(groups.Count*.15)); var validation=nonTest.Take(Math.Min(valCount,Math.Max(0,nonTest.Count-1))).ToHashSet();
            foreach(var r in rows.Where(x=>x.Label==label)) r.Split=frozen.Contains(r.GroupId)?"TEST":validation.Contains(r.GroupId)?"VALIDATION":"TRAIN";
        }
        File.WriteAllLines(freezePath,frozen.OrderBy(x=>x),new UTF8Encoding(false)); Save(csv,rows); Console.WriteLine($"SPLIT | PorGroupId=Sí | TestCongelado={frozen.Count} | Archivo={freezePath}"); return Audit(csv,false);
    }

    static int Audit(string csv,bool note)
    {
        var rows=Load(csv); var issues=Validate(rows); Console.WriteLine($"AUDITORIA | Archivos={rows.Count} | HashesUnicos={rows.Select(x=>x.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count()} | Grupos={rows.Select(x=>x.GroupId).Distinct().Count()}");
        foreach(var label in Labels) { var rs=rows.Where(x=>x.Label==label).ToList(); var g=rs.Select(x=>x.GroupId).Distinct().Count(); Console.WriteLine($"CLASE | Label={label} | Archivos={rs.Count} | Grupos={g} | Pendientes={Math.Max(0,Target-g)} | SourceTypes={Counts(rs,x=>x.SourceType)} | Diversidad={Counts(rs,x=>x.Diversity)}"); }
        foreach(var i in issues) Console.WriteLine(i.Level+" | "+i.Text); var state=issues.Any(x=>x.Level=="ERROR")||Labels.Any(l=>rows.Where(x=>x.Label==l).Select(x=>x.GroupId).Distinct().Count()<Target)?"INSUFICIENTE":"APTO_PARA_PRIMER_EXPERIMENTO";
        Console.WriteLine($"ESTADO_GLOBAL | {state} | ObjetivoGruposPorClase={Target}"); if(note) Console.WriteLine("El objetivo habilita un primer experimento; no garantiza aptitud productiva."); return issues.Any(x=>x.Level=="ERROR")?2:state=="INSUFICIENTE"?3:0;
    }

    static int Report(string csv,string output)
    {
        var rows=Load(csv); var issues=Validate(rows); var sb=new StringBuilder("# Informe de corpus H1D3A2\n\n"); sb.AppendLine($"- Archivos: {rows.Count}\n- Hashes únicos: {rows.Select(x=>x.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count()}\n- Grupos: {rows.Select(x=>x.GroupId).Distinct().Count()}\n"); sb.AppendLine("| Clase | Archivos | Grupos | Pendientes | SourceType | Diversidad |\n|---|---:|---:|---:|---|---|");
        foreach(var l in Labels){var rs=rows.Where(x=>x.Label==l).ToList();var g=rs.Select(x=>x.GroupId).Distinct().Count();sb.AppendLine($"| {l} | {rs.Count} | {g} | {Math.Max(0,Target-g)} | {Counts(rs,x=>x.SourceType)} | {Counts(rs,x=>x.Diversity)} |");}
        sb.AppendLine("\n## Splits"); foreach(var s in Splits.Where(x=>x.Length>0)) sb.AppendLine($"- {s}: {rows.Where(x=>x.Split==s).Select(x=>x.GroupId).Distinct().Count()} grupos."); sb.AppendLine("\n## Hallazgos"); if(issues.Count==0)sb.AppendLine("- Sin conflictos."); foreach(var i in issues)sb.AppendLine($"- {i.Level}: {i.Text}");
        var state=issues.Any(x=>x.Level=="ERROR")||Labels.Any(l=>rows.Where(x=>x.Label==l).Select(x=>x.GroupId).Distinct().Count()<Target)?"INSUFICIENTE":"APTO_PARA_PRIMER_EXPERIMENTO"; sb.AppendLine($"\n## Estado global\n\n**{state}**\n\n20 grupos por clase sólo habilitan el primer experimento. Augmentation futura se limita a TRAIN y conserva GroupId; no reemplaza grupos independientes."); File.WriteAllText(output,sb.ToString(),new UTF8Encoding(false)); Console.WriteLine("INFORME | "+output); return Audit(csv,false);
    }

    static List<Issue> Validate(List<Row> rows)
    {
        var z=new List<Issue>(); foreach(var r in rows){if(!Labels.Contains(r.Label))z.Add(new("ERROR","Etiqueta inválida: "+r.Label));if(!Splits.Contains(r.Split))z.Add(new("ERROR","Split inválido: "+r.Split));if(!File.Exists(r.Path)){z.Add(new("ERROR","Archivo desaparecido: "+r.Path));continue;}if(Hash(r.Path)!=r.Sha256)z.Add(new("ERROR","Cambio de hash: "+r.Path));}
        foreach(var g in rows.GroupBy(x=>x.Sha256,StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1))z.Add(new("ERROR","SHA-256 repetido: "+g.Key)); foreach(var g in rows.GroupBy(x=>x.GroupId)){if(g.Select(x=>x.Label).Distinct().Count()>1)z.Add(new("ERROR","GroupId con etiquetas diferentes: "+g.Key));if(g.Select(x=>x.Split).Where(x=>x.Length>0).Distinct().Count()>1)z.Add(new("ERROR","Fuga entre splits: "+g.Key));}
        var c=Labels.Select(l=>rows.Where(x=>x.Label==l).Select(x=>x.GroupId).Distinct().Count()).ToList();if(c.Min()>0&&c.Max()>c.Min()*2)z.Add(new("ADVERTENCIA","Desequilibrio de grupos mayor a 2:1."));foreach(var l in Labels.Where(l=>rows.Where(x=>x.Label==l).Select(x=>x.GroupId).Distinct().Count()<Target))z.Add(new("ADVERTENCIA","Clase insuficiente: "+l));return z;
    }

    static List<Row> Load(string path){var r=new List<Row>();var baseDir=Path.GetDirectoryName(Path.GetFullPath(path))!;using var p=new TextFieldParser(path){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true};p.SetDelimiters(",");var h=p.ReadFields()??throw new InvalidDataException("Sin header");var c=h.Select((n,i)=>(n,i)).ToDictionary(x=>x.n,x=>x.i,StringComparer.OrdinalIgnoreCase);while(!p.EndOfData){var f=p.ReadFields();if(f==null)continue;string V(string n)=>c.TryGetValue(n,out var i)&&i<f.Length?f[i]:"";var g=V("GroupId");var d=V("Diversity");var stored=V("Path");var resolved=Path.IsPathRooted(stored)?Path.GetFullPath(stored):Path.GetFullPath(Path.Combine(baseDir,stored));r.Add(new(resolved,V("Label"),g,V("SourceType"),V("Sha256"),V("Split"),V("OriginalPath"),string.IsNullOrWhiteSpace(d)?Diversity(g):d));}return r;}
    static void Save(string path,List<Row> rows){var baseDir=Path.GetDirectoryName(Path.GetFullPath(path))!;string Stored(Row x)=>string.IsNullOrWhiteSpace(x.OriginalPath)?x.Path:Path.GetRelativePath(baseDir,x.Path);var l=new List<string>{"Path,Label,GroupId,SourceType,Sha256,Split,OriginalPath,Diversity"};l.AddRange(rows.OrderBy(x=>x.Label).ThenBy(x=>x.GroupId).ThenBy(x=>x.Path).Select(x=>string.Join(",",new[]{Stored(x),x.Label,x.GroupId,x.SourceType,x.Sha256,x.Split,x.OriginalPath,x.Diversity}.Select(Csv))));File.WriteAllLines(path,l,new UTF8Encoding(false));}
    static string Hash(string p){using var s=File.OpenRead(p);return Convert.ToHexString(SHA256.HashData(s));} static string Key(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))); static string Csv(string s)=>"\""+(s??"").Replace("\"","\"\"")+"\""; static bool Under(string p,string r)=>Path.GetFullPath(p).StartsWith(Path.GetFullPath(r).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
    static string Destination(string d,string h,string p,FileFormat? format=null){var n=string.Concat(Path.GetFileNameWithoutExtension(p).Select(x=>Path.GetInvalidFileNameChars().Contains(x)?'_':x));if(n.Length>80)n=n[..80];format??=DetectFormat(p);return Path.Combine(d,h[..12].ToLowerInvariant()+"_"+n+DestinationExtension(p,format));}
    static FileFormat DetectFormat(string path){Span<byte> header=stackalloc byte[8];using var stream=File.OpenRead(path);var count=stream.ReadAtLeast(header,header.Length,false);if(count>=5&&header[..5].SequenceEqual("%PDF-"u8))return new("PDF","PDF",".pdf");if(count>=8&&header.SequenceEqual(new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A}))return new("PNG","IMAGE",".png");if(count>=3&&header[0]==0xFF&&header[1]==0xD8&&header[2]==0xFF)return new("JPEG","IMAGE",".jpg");return new("UNKNOWN","FILE","");}
    static string DestinationExtension(string path,FileFormat format)=>format.Extension.Length>0?format.Extension:Path.GetExtension(path).ToLowerInvariant();
    static string DisplayExtension(string extension)=>string.IsNullOrWhiteSpace(extension)?"(none)":extension.ToLowerInvariant();
    static string Counts(List<Row> r,Func<Row,string> f)=>string.Join("; ",r.GroupBy(f).OrderBy(x=>x.Key).Select(x=>(string.IsNullOrWhiteSpace(x.Key)?"SIN_CLASIFICAR":x.Key)+"="+x.Select(y=>y.GroupId).Distinct().Count())); static string Req(string[]a,string n)=>Opt(a,n)??throw new ArgumentException("Falta "+n); static string? Opt(string[]a,string n){for(int i=0;i+1<a.Length;i++)if(a[i].Equals(n,StringComparison.OrdinalIgnoreCase))return a[i+1];return null;} static int Usage(){Console.WriteLine("audit | migrate | split | report | add ... | import-reviewed <csv> [--dry-run]");return 2;} static string ProjectRoot(){var d=AppContext.BaseDirectory;while(d.Length>0){if(File.Exists(Path.Combine(d,"DocumentAiProbe.csproj")))return d;d=Directory.GetParent(d)?.FullName??"";}return Directory.GetCurrentDirectory();}
    static string Diversity(string g)=>g switch{"factura-sin-qr"=>"PDF_ESCANEADO","factura-prueba-ocr"=>"PDF_ESCANEADO","factura-c-validada"=>"JPG_ADJUNTO","orden-compra"=>"ORDEN_COMPRA","credencial"=>"CREDENCIAL","comprobante-pago-bancario"=>"COMPROBANTE_PAGO","fotos-familiares"=>"FOTOGRAFIA","firma-banco"=>"FIRMA","flightaware-newsletter"=>"NEWSLETTER_PUBLICIDAD",_=>"SIN_CLASIFICAR"};
    sealed class Row{public Row(string p,string l,string g,string t,string h,string s,string o,string d){Path=p;Label=l;GroupId=g;SourceType=t;Sha256=h;Split=s;OriginalPath=o;Diversity=d;}public string Path{get;set;}public string Label{get;}public string GroupId{get;}public string SourceType{get;}public string Sha256{get;}public string Split{get;set;}public string OriginalPath{get;set;}public string Diversity{get;}}
    sealed record Decision(string CandidateId,string OriginalPath,string ExpectedSha256,string Action,string Label,string GroupId,string Notes);
    sealed record FileFormat(string Name,string SourceType,string Extension);
    sealed record AddPlan(Decision Decision,string Hash,FileFormat Format,string Destination);
    sealed record Issue(string Level,string Text);
}
