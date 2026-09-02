using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Data;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class H1D9EBackfill
    {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern bool SetDllDirectory(string path);

        internal static int Run(string[] args)
        {
            if (args.Length != 4 || (args[3] != "inventory" && args[3] != "apply")) return 2;
            var root=Path.GetFullPath(args[1]);
            if(!SetDllDirectory(Path.Combine(root,"bin"))) return 2;
            var domain=AppDomain.CreateDomain("H1D9E-Backfill",null,new AppDomainSetup
            {
                ApplicationBase=root,PrivateBinPath="bin;tools/PdfRasterProbe/bin",ConfigurationFile=Path.Combine(root,"Web.config")
            });
            try { return domain.ExecuteAssembly(typeof(H1D9EBackfill).Assembly.Location,new[]{"--h1d9e-backfill-inner",root,Path.GetFullPath(args[2]),args[3]}); }
            finally { AppDomain.Unload(domain); }
        }

        internal static int RunInner(string[] args)
        {
            try
            {
                Check(Environment.Is64BitProcess,"ProcessX64");
                var root=args[1];var output=args[2];Directory.SetCurrentDirectory(root);Directory.CreateDirectory(output);
                var config=ConfiguracionIni.Cargar(Path.Combine(root,ConfiguracionIni.NombreArchivo));
                Check(config.VisionShadowEnabled,"VisionShadowEnabled");
                Check(config.VisionShadowModelVersion==VisualInvoiceShadowService.ExpectedModelVersion,"ModelVersion");
                ConfiguracionSistema.Inicializar(config);Logs.Inicializar(config);
                var cs=ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
                using(var lease=GmailSyncLease.TryAcquire())
                {
                    Check(lease!=null,"ExclusiveGmailLease");
                    var documents=ReadDocuments(cs);
                    var database=DatabaseState(cs);var files=FileState(documents);
                    var databasePath=Path.Combine(output,"protected-database-before.tsv");
                    var filesPath=Path.Combine(output,"protected-files-before.tsv");
                    if(args[3]=="inventory")
                    {
                        Check(!File.Exists(databasePath)&&!File.Exists(filesPath),"NewBaseline");
                        File.WriteAllLines(databasePath,database,Encoding.UTF8);File.WriteAllLines(filesPath,files,Encoding.UTF8);
                        File.WriteAllLines(Path.Combine(output,"inventory.csv"),new[]{"Id,Name,Classification,FilePresent,SupportedVisualFormat"}.Concat(documents.Select(d=>Csv(d.Id,d.Name,d.Classification,File.Exists(d.Path),Supported(d.Name)))),Encoding.UTF8);
                        Console.WriteLine("InventoryDocuments="+documents.Count+" | Available="+documents.Count(d=>File.Exists(d.Path))+" | AvailablePdfImages="+documents.Count(d=>File.Exists(d.Path)&&Supported(d.Name)));
                        Console.WriteLine("InventoryOnly=True | WritesToSql=0");return 0;
                    }
                    Check(File.Exists(databasePath)&&File.Exists(filesPath),"BaselineExists");
                    var baseline=File.ReadAllLines(databasePath);var baselineFiles=File.ReadAllLines(filesPath);
                    AssertSame(baseline,database,"ProtectedDatabaseBefore");AssertSame(baselineFiles,files,"ProtectedFilesBefore");
                    var stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture);
                    var rows=new List<string>{"Id,Name,Classification,Disposition,Status,PFactura,Zone,ErrorCode"};
                    int inserted=0,existing=0,missing=0,ok=0,error=0;
                    var shadowBefore=TableState(cs,"dbo","DocumentoVisionShadow");
                    foreach(var document in documents)
                    {
                        lease.AssertHeld();AssertSame(baseline,DatabaseState(cs),"PROTECTED_DATABASE_CHANGED");
                        AssertSame(new[]{baselineFiles.Single(f=>f.StartsWith(document.Id+"\t",StringComparison.Ordinal))},FileState(new List<Document>{document}),"PROTECTED_FILE_CHANGED");
                        var value=ReadShadow(cs,document.Id);var disposition="EXISTING";
                        if(value!=null) existing++;
                        else if(!File.Exists(document.Path))
                        {
                            missing++;rows.Add(Csv(document.Id,document.Name,document.Classification,"SKIPPED_FILE_MISSING","","","","FILE_MISSING"));
                            Console.WriteLine("Document="+document.Id+" | SKIPPED_FILE_MISSING");continue;
                        }
                        else
                        {
                            string workspacePath;
                            using(var workspace=new AttachmentWorkspace())
                            {
                                workspacePath=workspace.RootPath;
                                value=VisualDocumentShadowService.Evaluate(document.Path,document.Name,workspace).Result;
                            }
                            Check(!Directory.Exists(workspacePath),"WorkspaceRemoved");
                            Check(value!=null&&value.Attempted,"ShadowAttempted");
                            lease.AssertHeld();AssertSame(baseline,DatabaseState(cs),"PROTECTED_DATABASE_CHANGED");
                            Check(VisualShadowRepository.Save(document.Id,value),"ShadowPersisted");
                            value=ReadShadow(cs,document.Id);Check(value!=null,"ShadowReadBack");inserted++;disposition="INSERTED";
                            AssertSame(baseline,DatabaseState(cs),"PROTECTED_DATABASE_CHANGED");
                        }
                        if(value.Status=="OK")ok++;else error++;
                        rows.Add(Csv(document.Id,document.Name,document.Classification,disposition,value.Status,value.PFactura,value.Zone,value.ErrorCode));
                        Console.WriteLine("Document="+document.Id+" | "+disposition+" | "+value.Status+" | Zone="+value.Zone);
                    }
                    var after=DatabaseState(cs);var afterFiles=FileState(documents);
                    File.WriteAllLines(Path.Combine(output,"protected-database-after-"+stamp+".tsv"),after,Encoding.UTF8);
                    File.WriteAllLines(Path.Combine(output,"protected-files-after-"+stamp+".tsv"),afterFiles,Encoding.UTF8);
                    File.WriteAllLines(Path.Combine(output,"results-"+stamp+".csv"),rows,Encoding.UTF8);
                    AssertSame(baseline,after,"ProtectedDatabaseUnchanged");AssertSame(baselineFiles,afterFiles,"ProtectedFilesUnchanged");
                    var shadowAfter=TableState(cs,"dbo","DocumentoVisionShadow");
                    File.WriteAllLines(Path.Combine(output,"summary-"+stamp+".txt"),new[]{"Documents="+documents.Count,"Inserted="+inserted,"Existing="+existing,"SkippedMissing="+missing,"OK="+ok,"ERROR="+error,"ShadowBefore="+shadowBefore,"ShadowAfter="+shadowAfter,"ProtectedDatabaseUnchanged=True","ProtectedFilesUnchanged=True"},Encoding.UTF8);
                    if(inserted==0)Check(shadowBefore==shadowAfter,"IdempotencyShadowUnchanged");
                    Console.WriteLine("Backfill | Documents="+documents.Count+" | Inserted="+inserted+" | Existing="+existing+" | SkippedMissing="+missing+" | OK="+ok+" | ERROR="+error+" | SessionsCreated="+VisualInvoiceShadowService.SessionsCreated+" | Gate=True");
                    return 0;
                }
            }
            catch(Exception ex) { Console.Error.WriteLine("Backfill | Gate=False | Error="+ex.GetType().Name+" | Gate="+(ex is GateException?ex.Message:"See sanitized operational logs"));return 1; }
        }

        private static bool Supported(string name){return string.Equals(Path.GetExtension(name),".pdf",StringComparison.OrdinalIgnoreCase)||VisualDocumentShadowService.IsImage(name);}
        private sealed class Document { public long Id,Size;public string Name,Path,Hash,Classification; }
        private static List<Document> ReadDocuments(string cs)
        {
            var documents=new List<Document>();
            using(var cn=new SqlConnection(cs))using(var cmd=new SqlCommand("SELECT Id,NombreOriginal,RutaLocal,HashSha256,TamanioBytes,ISNULL(ResultadoRevision,Clasificacion) FROM dbo.DocumentoRecepcion ORDER BY Id;",cn))
            {cn.Open();using(var r=cmd.ExecuteReader())while(r.Read())documents.Add(new Document{Id=r.GetInt64(0),Name=r.GetString(1),Path=r.GetString(2),Hash=r.GetString(3).Trim(),Size=r.GetInt64(4),Classification=r.GetString(5)});}
            return documents;
        }
        private static VisualShadowResult ReadShadow(string cs,long id)
        {
            using(var cn=new SqlConnection(cs))using(var cmd=new SqlCommand("SELECT Estado,PFactura,Zona,ErrorCodigo FROM dbo.DocumentoVisionShadow WHERE DocumentoRecepcionId=@Id AND ModeloVersion=@Version AND ModeloSha256=@Hash;",cn))
            {
                cmd.Parameters.AddWithValue("@Id",id);cmd.Parameters.AddWithValue("@Version",VisualInvoiceShadowService.ExpectedModelVersion);cmd.Parameters.AddWithValue("@Hash",VisualInvoiceShadowService.ExpectedModelSha256);
                cn.Open();using(var r=cmd.ExecuteReader())return !r.Read()?null:new VisualShadowResult{Attempted=true,Status=r.GetString(0),PFactura=r.IsDBNull(1)?(double?)null:r.GetDouble(1),Zone=r.IsDBNull(2)?null:r.GetString(2),ErrorCode=r.IsDBNull(3)?null:r.GetString(3)};
            }
        }
        private static string[] FileState(List<Document> documents)
        {
            return documents.Select(d=>
            {
                if(!File.Exists(d.Path))return d.Id+"\tMISSING";
                var info=new FileInfo(d.Path);string hash;using(var f=File.OpenRead(d.Path))using(var sha=SHA256.Create())hash=Hex(sha.ComputeHash(f));
                Check(info.Length==d.Size&&string.Equals(hash,d.Hash,StringComparison.OrdinalIgnoreCase),"FileHashAndSize/"+d.Id);
                return d.Id+"\t"+info.Length+"\t"+hash;
            }).ToArray();
        }
        private static string[] DatabaseState(string cs)
        {
            // Hash every existing user table except the only table this operation may write.
            // No raw rows, OAuth credentials, email content or connection strings are emitted.
            var tables=new List<Tuple<string,string>>();
            using(var cn=new SqlConnection(cs))using(var cmd=new SqlCommand("SELECT SCHEMA_NAME(schema_id),name FROM sys.tables WHERE is_ms_shipped=0 AND NOT(schema_id=SCHEMA_ID(N'dbo') AND name=N'DocumentoVisionShadow') ORDER BY SCHEMA_NAME(schema_id),name;",cn))
            {cn.Open();using(var r=cmd.ExecuteReader())while(r.Read())tables.Add(Tuple.Create(r.GetString(0),r.GetString(1)));}
            return tables.Select(t=>TableState(cs,t.Item1,t.Item2)).ToArray();
        }
        private static string TableState(string cs,string schema,string table)
        {
            var hashes=new List<string>();string columns;
            using(var cn=new SqlConnection(cs))using(var cmd=new SqlCommand("SELECT * FROM ["+schema.Replace("]","]]")+"].["+table.Replace("]","]]")+"];",cn))
            {
                cn.Open();using(var r=cmd.ExecuteReader())
                {
                    columns=string.Join("|",Enumerable.Range(0,r.FieldCount).Select(i=>r.GetName(i)+":"+r.GetDataTypeName(i)));
                    while(r.Read())using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory,Encoding.UTF8,true))
                    {
                        for(int i=0;i<r.FieldCount;i++)
                        {
                            writer.Write(r.IsDBNull(i));if(r.IsDBNull(i))continue;
                            var value=r.GetValue(i);writer.Write(value.GetType().FullName);
                            var bytes=value as byte[];
                            if(bytes!=null){writer.Write(bytes.Length);writer.Write(bytes);}
                            else if(value is DateTime)writer.Write(((DateTime)value).ToString("O",CultureInfo.InvariantCulture));
                            else if(value is double)writer.Write(((double)value).ToString("R",CultureInfo.InvariantCulture));
                            else if(value is float)writer.Write(((float)value).ToString("R",CultureInfo.InvariantCulture));
                            else writer.Write(Convert.ToString(value,CultureInfo.InvariantCulture));
                        }
                        writer.Flush();using(var sha=SHA256.Create())hashes.Add(Hex(sha.ComputeHash(memory.ToArray())));
                    }
                }
            }
            hashes.Sort(StringComparer.Ordinal);using(var sha=SHA256.Create())return schema+"."+table+"\t"+hashes.Count+"\t"+Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(columns+"\n"+string.Join("\n",hashes))));
        }
        private static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","");}
        private static string Csv(params object[] values){return string.Join(",",values.Select(v=>"\""+Convert.ToString(v,CultureInfo.InvariantCulture).Replace("\"","\"\"")+"\""));}
        private static void AssertSame(string[] before,string[] after,string gate){if(!before.SequenceEqual(after))throw new GateException(gate);}
        private static void Check(bool pass,string gate){if(!pass)throw new GateException(gate);}
        private sealed class GateException:Exception { public GateException(string gate):base(gate){} }
    }
}
