using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Data;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class H1D7C2InvoiceSampleProbe
    {
        private static readonly List<long> Messages=new List<long>();
        private static readonly List<string> Evidence=new List<string>();
        private static string RunId,Root,Repo,Output;
        private static int Sequence;
        internal static int Run(string[] args)
        {
            if(args.Length!=3)return 2;
            var setup=new AppDomainSetup{ApplicationBase=AppDomain.CurrentDomain.BaseDirectory,ConfigurationFile=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"Web.config")};
            var domain=AppDomain.CreateDomain("H1D7C2-WebConfig",null,setup);
            try{return domain.ExecuteAssembly(typeof(H1D7C2InvoiceSampleProbe).Assembly.Location,new[]{"--h1d7c2-sample-inner",args[1],args[2]});}finally{AppDomain.Unload(domain);}
        }
        internal static int RunInner(string[] args)
        {
            Repo=Path.GetDirectoryName(Path.GetFullPath(args[1]));Output=Path.GetFullPath(args[2]);Directory.CreateDirectory(Output);
            var cfg=ConfiguracionIni.Cargar(args[1]);cfg.PrepararRutasOperativas();ConfiguracionSistema.Inicializar(cfg);Logs.Inicializar(cfg);
            RunId="h1d7c2-"+Guid.NewGuid().ToString("N");Root=Path.Combine(cfg.RutaRevisar,RunId);Directory.CreateDirectory(Root);
            var ok=false;
            try
            {
                Check("ProcessX64",Environment.Is64BitProcess);
                var bins=new int[10];
                for(var i=0;i<1000;i++)
                {
                    var hash=Hash(Encoding.UTF8.GetBytes("H1D7C2-distribution-"+i));
                    var bucket=InvoiceSampleRepository.Bucket(hash);
                    CheckSilent(bucket.HasValue&&bucket==InvoiceSampleRepository.Bucket(hash.ToLowerInvariant()),"Deterministic");bins[bucket.Value]++;
                }
                Evidence.Add("Buckets="+string.Join(",",bins));Check("DeterministicDistribution",bins.All(n=>n>=60&&n<=140));
                Check("InvalidHashRejected",InvoiceSampleRepository.Bucket(new string('z',64))==null);
                var cursor=Cursor();
                Check("NoSelectionBeforePersist",!InvoiceSampleRepository.SelectNew(-1));
                var a=Create("FACTURA",true);Check("SelectedOnce",CountSample(a.Id)==1&&!InvoiceSampleRepository.SelectNew(a.Id)&&Read(a.Id).Classification=="FACTURA");
                Check("DuplicatePersistRejected",!DocumentRepository.Save(a.MessageId,"fixture",a.Candidate,a.Stored)&&CountSample(a.Id)==1);
                var b=Create("FACTURA",false);Check("NotSelected",CountSample(b.Id)==0);
                var c=Create("REVISAR",true);Check("ReviewExcluded",CountSample(c.Id)==0);
                var d=Create("REVISAR",true);Exec("UPDATE dbo.DocumentoRecepcion SET ResultadoRevision=N'DESCARTAR',EtiquetaRevision=N'NO_DOCUMENTO' WHERE Id="+d.Id);Check("DiscardExcluded",!InvoiceSampleRepository.SelectNew(d.Id)&&CountSample(d.Id)==0);
                var reviewed=Create("FACTURA",false);Exec("UPDATE dbo.DocumentoRecepcion SET ResultadoRevision=N'FACTURA',EtiquetaRevision=N'FACTURA' WHERE Id="+reviewed.Id);Check("HumanResolvedExcluded",!InvoiceSampleRepository.SelectNew(reviewed.Id));
                var e=Create("FACTURA",true);var f=Create("FACTURA",true);var g=Create("FACTURA",true);
                Check("SeparateQueue",InvoiceSampleRepository.Pending(c.Id)==null&&DocumentRepository.GetPendingForReview(a.Id)==null&&InvoiceSampleRepository.Pending(a.Id)!=null);
                Check("Navigation",InvoiceSampleRepository.Next(a.Id)==e.Id&&InvoiceSampleRepository.Previous(e.Id)==a.Id&&InvoiceSampleRepository.Pending(e.Id).Position==InvoiceSampleRepository.Pending(a.Id).Position+1&&InvoiceSampleRepository.CountPending()>=4&&InvoiceSampleRepository.First().HasValue);
                var shadow=Convert.ToInt64(Scalar("INSERT dbo.DocumentoVisionShadow(DocumentoRecepcionId,ModeloVersion,ModeloSha256,PreprocesamientoVersion,Estado,PNoFactura,PFactura,Zona,OrigenVisual,RasterReutilizado,FechaEvaluacionUtc) OUTPUT inserted.Id VALUES("+e.Id+",N'H1D7C2-FIXTURE',REPLICATE('A',64),N'FIXTURE',N'OK',.99,.01,N'NO_FACTURA_FUERTE',N'FIXTURE',0,SYSUTCDATETIME());"));
                Check("ConfirmInvoice",InvoiceSampleService.Resolve(a.Id,"FACTURA","fixture",null).Success);Validate(a,"FACTURA",false,null);
                Check("OtherDocument",InvoiceSampleService.Resolve(e.Id,"OTRO_DOCUMENTO","fixture",null).Success);Validate(e,"OTRO_DOCUMENTO",true,shadow);
                Check("NonDocument",InvoiceSampleService.Resolve(f.Id,"NO_DOCUMENTO","fixture",null).Success);Validate(f,"NO_DOCUMENTO",true,null);
                var tasks=new[]{Task.Run(()=>InvoiceSampleService.Resolve(g.Id,"FACTURA","race-a",null)),Task.Run(()=>InvoiceSampleService.Resolve(g.Id,"NO_DOCUMENTO","race-b",null))};
                Task.WaitAll(tasks);Check("ConcurrencyOneWinner",tasks.Count(t=>t.Result.Success)==1);var gr=Read(g.Id);Validate(g,gr.Label,gr.Label!="FACTURA",null);
                Check("SecondAttemptFalse",!InvoiceSampleService.Resolve(g.Id,"OTRO_DOCUMENTO","fixture",null).Success);
                // Preserve a pre-existing review destination, including a losing/repeated operation.
                var p=Create("FACTURA",true);var pre=DocumentStorage.Save(p.Stored.FullPath,"REVISAR",p.Date,p.GmailId,p.Candidate.OriginalName,p.Candidate.OriginHash);
                Check("PreexistingDecision",InvoiceSampleService.Resolve(p.Id,"NO_DOCUMENTO","fixture",null).Success);Validate(p,"NO_DOCUMENTO",true,null);
                Check("PreexistingPreserved",File.Exists(pre.FullPath)&&!InvoiceSampleService.Resolve(p.Id,"FACTURA","fixture",null).Success&&File.Exists(pre.FullPath));
                var x=Create("FACTURA",true);
                Exec("ALTER TABLE dbo.DocumentoGroundTruth ADD CONSTRAINT CK_H1D7C2_AtomicProbe CHECK(DocumentoRecepcionId<>"+x.Id+");");
                var failed=false;try{InvoiceSampleService.Resolve(x.Id,"NO_DOCUMENTO","fixture",null);}catch(SqlException){failed=true;}finally{Exec("ALTER TABLE dbo.DocumentoGroundTruth DROP CONSTRAINT CK_H1D7C2_AtomicProbe;");}
                var xr=Read(x.Id);Check("AtomicRollback",failed&&xr.Result==null&&xr.Label==null&&xr.GtCount==0&&xr.SampleGt==null&&xr.Resolved==null&&File.Exists(x.Stored.FullPath)&&StoredFiles(cfg.RutaRevisar,x.GmailId).Count==0);
                // Failure scoped to the next synthetic message, never to real Gmail traffic.
                var failMessage=NewMessage();
                Exec("CREATE TRIGGER dbo.TR_H1D7C2_SampleFailure ON dbo.DocumentoRevisionMuestra AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted s JOIN dbo.DocumentoRecepcion d ON d.Id=s.DocumentoRecepcionId WHERE d.GmailMensajeId="+failMessage.Item1+") THROW 51000,'H1D7C2 controlled selection failure',1; END;");
                Fixture z;
                try{z=Create("FACTURA",true,failMessage);}finally{Exec("DROP TRIGGER dbo.TR_H1D7C2_SampleFailure;");}
                Check("GmailFailureIsolated",Read(z.Id).Classification=="FACTURA"&&CountSample(z.Id)==0&&!DocumentRepository.Save(z.MessageId,"fixture",z.Candidate,z.Stored)&&Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.DocumentoRecepcion WHERE GmailMensajeId="+z.MessageId))==1&&Cursor()==cursor);
                Check("RetrySelectionIdempotent",InvoiceSampleRepository.SelectNew(z.Id)&&!InvoiceSampleRepository.SelectNew(z.Id)&&CountSample(z.Id)==1);
                BlindUi();Frozen();Check("GmailCursorIntact",Cursor()==cursor);ok=true;
            }
            catch(Exception ex){Evidence.Add("FAIL="+ex.GetType().Name+": "+ex.Message);Console.WriteLine(Evidence.Last());}
            finally
            {
                try{Cleanup();Check("FixtureCleanup",!Directory.Exists(Root)&&!StoredFiles(cfg.RutaFacturas,RunId).Any()&&!StoredFiles(cfg.RutaRevisar,RunId).Any());}
                catch(Exception ex){ok=false;Evidence.Add("CleanupFailure="+ex.Message);}
                Evidence.Add("Gate="+ok);File.WriteAllLines(Path.Combine(Output,"probe-evidence.txt"),Evidence,new UTF8Encoding(false));Console.WriteLine("H1D7C2 | Gate="+ok);
            }
            return ok?0:1;
        }
        private static void BlindUi()
        {
            var markup=File.ReadAllText(Path.Combine(Repo,"Documento_Revisar.aspx"));var code=File.ReadAllText(Path.Combine(Repo,"Documento_Revisar.aspx.cs"));
            var start=markup.IndexOf("<asp:PlaceHolder ID=\"phAutomatic\"",StringComparison.Ordinal);var end=markup.IndexOf("</asp:PlaceHolder>",start,StringComparison.Ordinal);CheckSilent(start>=0&&end>start,"Automatic container");
            var blind=markup.Remove(start,end+"</asp:PlaceHolder>".Length-start);
            Check("UiBlindContract",!new[]{"litClassification","litMethod","litConfidence","litReason","PFactura","PNoFactura","VisualSource","Zona visual"}.Any(blind.Contains)&&code.Contains("phAutomatic.Visible=!IsSample")&&code.Contains("IsSample?InvoiceSampleRepository.Pending(id)")&&markup.Contains("Revisión de control"));
            var select=File.ReadAllText(Path.Combine(Repo,"Data","InvoiceSampleRepository.cs"));
            var selection=select.Substring(select.IndexOf("public static bool SelectNew"),select.IndexOf("private static long? Scalar")-select.IndexOf("public static bool SelectNew"));
            Check("SamplingIndependentOfShadow",!selection.Contains("Shadow")&&!selection.Contains("Confianza")&&selection.Contains("dbo.H1D7C2Bucket"));
            var gmail=File.ReadAllText(Path.Combine(Repo,"Services","GmailSyncService.cs"));Check("GmailIngestionContract",gmail.Contains("if (DocumentRepository.Save("));
        }
        private static void Frozen()
        {
            var paths=new[]{"tools/DocumentAiProbe/dataset.csv","tools/DocumentAiProbe/frozen-test-groups.txt","tools/DocumentAiProbe/experiments/H1D9B/fold-manifest.csv","tools/DocumentAiProbe/experiments/H1D9B/candidate.onnx","tools/DocumentAiProbe/experiments/H1D9B/candidate-checkpoint.pt"};
            var hashes=new[]{"AFECA7A2F995CE1B2DF6F9DAF3501A392CC7FB4DD1509900A19D1588E26794C2","FADEA71A298125E8CE0EB65C31F6232EAAE72EB71F33141B912D23F4E59603E4","9E4A9ACC7DB4B042A96A28502745ADC32F78AF7866A45918000668C127D895D9","A1DC24FE90C3C14303C3319EC0BD6D9EF95E91CC82C9B38E2FBAAEB0EF826811","F6F552CF5FAD856D7FB57352C63C4CD68C3E3E0F6C039C3C7623B030FD965F27"};
            for(var i=0;i<paths.Length;i++)Check("Frozen:"+Path.GetFileName(paths[i]),Hash(File.ReadAllBytes(Path.Combine(Repo,paths[i])))==hashes[i]);
        }
        private static void Validate(Fixture f,string label,bool moved,long? shadow)
        {
            var row=Read(f.Id);Check("Decision:"+f.Id,row.Classification=="FACTURA"&&row.Result==(label=="FACTURA"?"FACTURA":"DESCARTAR")&&row.Label==label&&row.Binary==(label=="FACTURA"?"FACTURA":"NO_FACTURA")&&row.Source=="MUESTREO_FACTURA_CIEGO"&&row.GtCount==1&&row.Sequence==1&&row.Current&&row.SampleGt==row.Gt&&row.Resolved.HasValue&&row.Shadow==shadow);
            var paths=StoredFiles(ConfiguracionSistema.Actual.RutaFacturas,f.GmailId);
            Check("Physical:"+f.Id,File.Exists(row.Path)&&Hash(File.ReadAllBytes(row.Path))==f.Candidate.OriginHash&&new FileInfo(row.Path).Length==f.Stored.Size&&(moved?paths.Count==0:paths.Count==1));
            Check("EffectiveList:"+f.Id,DocumentRepository.List("FACTURA").Any(d=>d.Id==f.Id)==!moved);
        }
        private static Tuple<long,string> NewMessage()
        {
            var gm=RunId+"-"+(++Sequence);var id=Convert.ToInt64(Scalar("INSERT dbo.GmailMensaje(GmailCuentaId,GmailMessageId,FechaMensajeUtc,Remitente,Asunto) OUTPUT inserted.Id SELECT TOP(1) Id,N'"+gm+"',SYSUTCDATETIME(),N'h1d7c2@local',N'fixture' FROM dbo.GmailCuenta ORDER BY Activo DESC,Id;"));Messages.Add(id);return Tuple.Create(id,gm);
        }
        private static Fixture Create(string classification,bool selected,Tuple<long,string> message=null)
        {
            byte[] bytes;string hash;var n=0;do{bytes=Encoding.UTF8.GetBytes(RunId+"-"+Sequence+"-"+(n++));hash=Hash(bytes);}while((InvoiceSampleRepository.Bucket(hash)==0)!=selected);
            message=message??NewMessage();var date=DateTime.UtcNow;var path=Path.Combine(Root,message.Item1+".bin");File.WriteAllBytes(path,bytes);
            var candidate=new DocumentCandidate{SourcePath=path,OriginalName="fixture.bin",MimeType="application/octet-stream",OriginType="DIRECTO",OriginHash=hash,SizeBytes=bytes.LongLength,Selection=new InvoiceSelection{Classification=classification,DetectionMethod="H1D7C2_FIXTURE",Reason="fixture"}};
            var stored=DocumentStorage.Save(path,classification,date,message.Item2,candidate.OriginalName,hash);
            CheckSilent(DocumentRepository.Save(message.Item1,"fixture",candidate,stored),"Fixture persistence");
            var id=DocumentRepository.GetId(message.Item1,"fixture",hash);CheckSilent(id.HasValue,"Fixture id");
            return new Fixture{Id=id.Value,MessageId=message.Item1,GmailId=message.Item2,Date=date,Candidate=candidate,Stored=stored};
        }
        private static State Read(long id)
        {
            using(var cn=Cn())using(var cmd=new SqlCommand(@"SELECT d.Clasificacion,d.ResultadoRevision,d.EtiquetaRevision,d.RutaLocal,g.EtiquetaBinaria,g.Fuente,g.Secuencia,g.EsVigente,g.Id,s.DocumentoGroundTruthId,s.FechaResolucionUtc,g.DocumentoVisionShadowId,(SELECT COUNT(*) FROM dbo.DocumentoGroundTruth WHERE DocumentoRecepcionId=d.Id) FROM dbo.DocumentoRecepcion d LEFT JOIN dbo.DocumentoGroundTruth g ON g.DocumentoRecepcionId=d.Id AND g.EsVigente=1 LEFT JOIN dbo.DocumentoRevisionMuestra s ON s.DocumentoRecepcionId=d.Id WHERE d.Id=@Id;",cn))
            {cmd.Parameters.AddWithValue("@Id",id);cn.Open();using(var r=cmd.ExecuteReader()){r.Read();return new State{Classification=S(r,0),Result=S(r,1),Label=S(r,2),Path=S(r,3),Binary=S(r,4),Source=S(r,5),Sequence=r.IsDBNull(6)?0:r.GetInt32(6),Current=!r.IsDBNull(7)&&r.GetBoolean(7),Gt=r.IsDBNull(8)?(long?)null:r.GetInt64(8),SampleGt=r.IsDBNull(9)?(long?)null:r.GetInt64(9),Resolved=r.IsDBNull(10)?(DateTime?)null:r.GetDateTime(10),Shadow=r.IsDBNull(11)?(long?)null:r.GetInt64(11),GtCount=r.GetInt32(12)};}}
        }
        private static List<string> StoredFiles(string root,string token){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>p.IndexOf(token,StringComparison.OrdinalIgnoreCase)>=0).ToList();}
        private static int CountSample(long id){return Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.DocumentoRevisionMuestra WHERE DocumentoRecepcionId="+id));}
        private static string Cursor(){return Convert.ToString(Scalar("SELECT ISNULL(CONVERT(nvarchar(50),UltimoHistoryId),N'<NULL>')+N'|'+ISNULL(CONVERT(nvarchar(40),UltimaConsultaUtc,126),N'<NULL>') FROM dbo.GmailCuenta WHERE Activo=1;"));}
        private static void Cleanup()
        {
            Exec("IF OBJECT_ID(N'dbo.TR_H1D7C2_SampleFailure',N'TR') IS NOT NULL DROP TRIGGER dbo.TR_H1D7C2_SampleFailure; IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_H1D7C2_AtomicProbe') ALTER TABLE dbo.DocumentoGroundTruth DROP CONSTRAINT CK_H1D7C2_AtomicProbe;");
            if(Messages.Count>0){var ids="SELECT Id FROM dbo.DocumentoRecepcion WHERE GmailMensajeId IN("+string.Join(",",Messages)+")";Exec("DELETE dbo.DocumentoRevisionMuestra WHERE DocumentoRecepcionId IN("+ids+");DELETE dbo.DocumentoGroundTruth WHERE DocumentoRecepcionId IN("+ids+");DELETE dbo.DocumentoVisionShadow WHERE DocumentoRecepcionId IN("+ids+");DELETE dbo.DocumentoRecepcion WHERE GmailMensajeId IN("+string.Join(",",Messages)+");DELETE dbo.GmailMensaje WHERE Id IN("+string.Join(",",Messages)+");");}
            foreach(var root in new[]{ConfiguracionSistema.Actual.RutaFacturas,ConfiguracionSistema.Actual.RutaRevisar})foreach(var file in StoredFiles(root,RunId))File.Delete(file);
            if(Directory.Exists(Root))Directory.Delete(Root,false);
        }
        private static SqlConnection Cn(){return new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString);}
        private static object Scalar(string sql){using(var cn=Cn())using(var cmd=new SqlCommand("SET ARITHABORT ON;"+sql,cn)){cn.Open();return cmd.ExecuteScalar();}}
        private static void Exec(string sql){using(var cn=Cn()){cn.Open();using(var session=new SqlCommand("SET ARITHABORT ON;",cn))session.ExecuteNonQuery();using(var cmd=new SqlCommand(sql,cn))cmd.ExecuteNonQuery();}}
        private static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
        private static string S(SqlDataReader r,int i){return r.IsDBNull(i)?null:r.GetString(i);}
        private static void Check(string gate,bool pass){CheckSilent(pass,gate);Evidence.Add(gate+"=True");Console.WriteLine(gate+"=True");}
        private static void CheckSilent(bool pass,string gate){if(!pass)throw new InvalidOperationException("Gate failed: "+gate);}
        private sealed class Fixture{internal long Id,MessageId;internal string GmailId;internal DateTime Date;internal DocumentCandidate Candidate;internal DocumentStoredFile Stored;}
        private sealed class State{internal string Classification,Result,Label,Path,Binary,Source;internal int Sequence,GtCount;internal bool Current;internal long? Gt,SampleGt,Shadow;internal DateTime? Resolved;}
    }
}
