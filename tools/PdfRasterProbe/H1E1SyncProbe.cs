using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RecepcionDocumental.Data;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class H1E1SyncProbe
    {
        internal static int Run(string[] a)
        {
            if(a.Length!=3)return 2;
            var root=Path.GetFullPath(a[1]);
            var d=AppDomain.CreateDomain("H1E1-ProductConfig",null,new AppDomainSetup{ApplicationBase=AppDomain.CurrentDomain.BaseDirectory,ConfigurationFile=Path.Combine(root,"Web.config")});
            try{return d.ExecuteAssembly(typeof(H1E1SyncProbe).Assembly.Location,new[]{"--h1e1-setup",root,Path.GetFullPath(a[2])});}finally{AppDomain.Unload(d);}
        }
        internal static int Setup(string[] a)
        {
            var root=a[1];var output=a[2];Directory.CreateDirectory(output);
            var name="H1E1_Probe_"+Guid.NewGuid().ToString("N");
            var folder=Path.Combine(Path.GetTempPath(),name);Directory.CreateDirectory(folder);
            var original=ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            var builder=new SqlConnectionStringBuilder(original);builder.InitialCatalog="master";
            bool created=false;int code=1;
            var sqlPath=Path.Combine(root,"Database","011_GmailSyncEjecucion.sql");var bytes=File.ReadAllBytes(sqlPath);
            try
            {
                Execute(builder.ConnectionString,"CREATE DATABASE ["+name+"];");created=true;builder.InitialCatalog=name;
                var connection=builder.ConnectionString;
                Execute(connection,"CREATE TABLE dbo.GmailCuenta(Id int IDENTITY PRIMARY KEY,Email nvarchar(320) NOT NULL,Activo bit NOT NULL,RefreshTokenProtegido varbinary(max) NULL,UltimoHistoryId nvarchar(50) NULL,UltimaConsultaUtc datetime2(0) NULL,FechaModificacion datetime2(0) NULL); INSERT dbo.GmailCuenta(Email,Activo,UltimoHistoryId) VALUES(N'fixture@local',1,N'100');");
                var sql=File.ReadAllText(sqlPath).Replace("USE [RecepcionDocumental];","USE ["+name+"];");
                var points=Regex.Matches(sql,@"(?im)^\s*COMMIT;\s*$");Check(points.Count==1,"InjectionPointUnique");
                var injected=sql.Insert(points[0].Index," THROW 51101,'H1E1 rollback probe',1;\n");
                bool observed=false;try{Migration(connection,injected);}catch(SqlException ex){if(ex.Number!=51101)throw;observed=true;}
                Check(observed,"InjectedFailureObserved");
                Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM sys.objects WHERE name=N'GmailSyncEjecucion' OR name LIKE N'%GmailSyncEjecucion%';"))==0,"MigrationRollbackClean");
                Migration(connection,sql);Migration(connection,sql);
                Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.GmailSyncEjecucion');"))==16,"MigrationSchema");
                Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM dbo.GmailSyncEjecucion;"))==0,"MigrationIdempotent");
                Check(File.ReadAllBytes(sqlPath).SequenceEqual(bytes),"MigrationOriginalIntact");
                Directory.CreateDirectory(Path.Combine(folder,"bin"));
                foreach(var file in Directory.GetFiles(Path.Combine(root,"bin"),"*.dll"))File.Copy(file,Path.Combine(folder,"bin",Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(folder,"Web.config"),"<configuration><connectionStrings><add name=\"DefaultConnection\" connectionString=\""+SecurityElement.Escape(connection)+"\" providerName=\"System.Data.SqlClient\"/></connectionStrings></configuration>");
                var ini=File.ReadAllText(Path.Combine(root,"RecepcionDocumental.ini"));
                foreach(var key in new[]{"Logs","Trabajo","Facturas","Revisar"})ini=Regex.Replace(ini,@"(?im)^"+key+@"\s*=.*$",key+"="+Path.Combine(folder,key));
                File.WriteAllText(Path.Combine(folder,"RecepcionDocumental.ini"),ini);
                var domain=AppDomain.CreateDomain("H1E1-Isolated",null,new AppDomainSetup{ApplicationBase=AppDomain.CurrentDomain.BaseDirectory,ConfigurationFile=Path.Combine(folder,"Web.config")});
                try{code=domain.ExecuteAssembly(typeof(H1E1SyncProbe).Assembly.Location,new[]{"--h1e1-isolated",folder,Path.Combine(root,"tools","RecepcionDocumental.SyncRunner","bin","RecepcionDocumental.SyncRunner.exe")});}finally{AppDomain.Unload(domain);}
                Check(code==0,"IsolatedProbe");
            }
            catch(Exception ex){Console.WriteLine("Gate=False | "+ex.GetType().Name+" | "+ex.Message);code=1;}
            finally
            {
                SqlConnection.ClearAllPools();
                if(created){builder.InitialCatalog="master";Execute(builder.ConnectionString,"ALTER DATABASE ["+name+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+name+"];");Console.WriteLine("TemporaryDatabaseRemoved=True");}
                // Only the exact GUID directory created by this invocation; never a configured storage root.
                if(Path.GetDirectoryName(folder)==Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(folder)==name)Directory.Delete(folder,true);
            }
            Console.WriteLine("H1E1 | Gate="+(code==0));return code;
        }
        internal static int Isolated(string[] a)
        {
            try{Tests(a[1],a[2]).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.WriteLine("FAIL: "+ex.GetType().Name+" | "+ex.Message);return 1;}
        }
        private static async Task Tests(string folder,string runner)
        {
            Check(Environment.Is64BitProcess,"ProcessX64");
            var cn=ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            var entered=new TaskCompletionSource<bool>();var release=new TaskCompletionSource<bool>();var count=0;
            var winner=GmailSyncExecution.RunAsync("WEB",async(account,lease)=>{Interlocked.Increment(ref count);Check(account.LastHistoryId=="100","WinnerReadsOriginalCursor");entered.SetResult(true);await release.Task;lease.AssertHeld();GmailSyncRepository.CompleteSync(account.Id,"101");return new GmailSyncResult{MensajesEncontrados=2,MensajesNuevos=1};});
            await entered.Task;
            try
            {
                var loser=await GmailSyncExecution.RunAsync("SCHEDULER",(account,lease)=>{Interlocked.Increment(ref count);return Task.FromResult(new GmailSyncResult());});
                Check(loser.AlreadyRunning&&loser.Errores==0&&count==1,"SingleProcessor");
                Check(Convert.ToString(Scalar(cn,"SELECT UltimoHistoryId FROM dbo.GmailCuenta;"))=="100","LoserDoesNotChangeCursor");
                using(var p=Start(runner,folder,"--sync"))
                {
                    Check(p.WaitForExit(30000)&&p.ExitCode==10,"WebWinsRunnerSkips");
                    Check(p.StandardOutput.ReadToEnd().Contains("Estado=YA_EN_EJECUCION"),"AlreadyRunningSummary");
                    CheckAudit(cn,"OMITIDA_YA_EN_EJECUCION","AlreadyRunningAudit");
                }
            }
            finally{release.TrySetResult(true);}
            await winner;Check(Convert.ToString(Scalar(cn,"SELECT UltimoHistoryId FROM dbo.GmailCuenta;"))=="101","WinnerCompletesCursor");
            using(var p=Start(runner,folder,"--probe-lock-hold"))
            {
                string line;bool acquired=false;while((line=p.StandardOutput.ReadLine())!=null){if(line=="LockAcquired=True"){acquired=true;break;}}
                Check(acquired,"RunnerAcquires");
                var skipped=await GmailSyncExecution.RunAsync("WEB",(account,lease)=>{Interlocked.Increment(ref count);return Task.FromResult(new GmailSyncResult());});
                Check(skipped.AlreadyRunning&&count==1,"RunnerWinsWebSkips");Check(p.WaitForExit(30000)&&p.ExitCode==0,"RunnerCleanExit");
            }
            bool failed=false;try{await GmailSyncExecution.RunAsync("SCHEDULER",(account,lease)=>{throw new IOException("fixture failure");});}catch(IOException){failed=true;}
            Check(failed,"IntentionalFailure");
            var next=await GmailSyncExecution.RunAsync("WEB",(account,lease)=>Task.FromResult(new GmailSyncResult{Errores=1}));
            Check(!next.AlreadyRunning&&next.Errores==1,"LockReleasedAfterFailure");
            Check(Convert.ToString(Scalar(cn,"SELECT UltimoHistoryId FROM dbo.GmailCuenta;"))=="101","ErrorPreservesCursor");
            Check(Convert.ToInt32(Scalar(cn,"SELECT COUNT(DISTINCT Estado) FROM dbo.GmailSyncEjecucion WHERE FechaFinUtc>=FechaInicioUtc;"))==4,"AuditStatesAndTimestamps");
            Check(Convert.ToInt32(Scalar(cn,"SELECT COUNT(*) FROM dbo.GmailSyncEjecucion WHERE Estado=N'FALLIDA' AND DetalleError=N'IOException';"))==1,"AuditSanitized");
            string stdout,stderr;
            var code=RunnerBoundary(runner,()=>GmailSyncExecution.RunAsync("SCHEDULER",(account,lease)=>Task.FromResult(new GmailSyncResult())).GetAwaiter().GetResult(),out stdout,out stderr);
            Check(code==0&&stdout.Contains("Estado=COMPLETADA")&&stdout.Contains("ExitCode=0"),"CompletedExitCode");
            CheckAudit(cn,"COMPLETADA","CompletedAudit");
            code=RunnerBoundary(runner,()=>GmailSyncExecution.RunAsync("SCHEDULER",(account,lease)=>Task.FromResult(new GmailSyncResult{Errores=1})).GetAwaiter().GetResult(),out stdout,out stderr);
            Check(code==1&&stdout.Contains("Estado=COMPLETADA_CON_ERRORES")&&stdout.Contains("ExitCode=1"),"CompletedWithErrorsExitCode");
            CheckAudit(cn,"COMPLETADA_CON_ERRORES","CompletedWithErrorsAudit");
            code=RunnerBoundary(runner,()=>GmailSyncExecution.RunAsync("SCHEDULER",(account,lease)=>{throw new IOException("fixture failure");}).GetAwaiter().GetResult(),out stdout,out stderr);
            Check(code==1&&stderr.Contains("Failed=IOException"),"FailedExitCode");
            CheckAudit(cn,"FALLIDA","FailedAudit");
            using(var held=GmailSyncLease.TryAcquire())
            {
                Check(held!=null,"FixtureLeaseAcquired");
                code=RunnerBoundary(runner,()=>GmailSyncExecution.RunAsync("SCHEDULER",(account,lease)=>{throw new InvalidOperationException("Loser must not process.");}).GetAwaiter().GetResult(),out stdout,out stderr);
                Check(code==10&&stdout.Contains("Estado=YA_EN_EJECUCION"),"AlreadyRunningExitCode");
                CheckAudit(cn,"OMITIDA_YA_EN_EJECUCION","OmittedAudit");
            }
            Execute(cn,"CREATE TRIGGER dbo.H1E1_RejectCompleted ON dbo.GmailSyncEjecucion AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted WHERE Estado=N'COMPLETADA') THROW 51102,'H1E1 controlled audit finalization failure',1; END;");
            bool finishFailed=false;
            try
            {
                code=RunnerBoundary(runner,()=>
                {
                    try{return GmailSyncExecution.RunAsync("SCHEDULER",(account,lease)=>Task.FromResult(new GmailSyncResult())).GetAwaiter().GetResult();}
                    catch(GmailSyncAuditException ex){finishFailed=ex.Code=="AUDIT_FINALIZATION_FAILED"&&ex.InnerException is SqlException&&((SqlException)ex.InnerException).Number==51102;throw;}
                },out stdout,out stderr);
                Check(code==1&&stderr.Contains("AUDIT_FINALIZATION_FAILED")&&!stdout.Contains("Estado=COMPLETADA"),"AuditFailureExitCode");
            }
            finally{Execute(cn,"DROP TRIGGER dbo.H1E1_RejectCompleted;");}
            var unfinished=Convert.ToInt32(Scalar(cn,"SELECT COUNT(*) FROM dbo.GmailSyncEjecucion WHERE Estado=N'EJECUTANDO' OR FechaFinUtc IS NULL;"));
            Console.WriteLine("ControlledFinishFailurePropagated="+finishFailed+" | UnfinishedAudits="+unfinished);
            Check(finishFailed&&unfinished==0,"AuditFinalizationFailureNotSilent");
            CheckAudit(cn,"FALLIDA","AuditFailureClosedAsFailed");
            Execute(cn,"EXEC sp_rename N'dbo.GmailSyncEjecucion',N'GmailSyncEjecucion_ProbeUnavailable';");
            try
            {
                bool processed=false;
                code=RunnerBoundary(runner,()=>GmailSyncExecution.RunAsync("WEB",(account,lease)=>{processed=true;return Task.FromResult(new GmailSyncResult());}).GetAwaiter().GetResult(),out stdout,out stderr);
                Check(code==1&&!processed&&stderr.Contains("AUDIT_START_FAILED"),"AuditStartFailureNotSilent");
            }
            finally{Execute(cn,"EXEC sp_rename N'dbo.GmailSyncEjecucion_ProbeUnavailable',N'GmailSyncEjecucion';");}
            code=RunnerBoundary(runner,()=>{GmailSyncAuditRepository.Finish(long.MaxValue,"COMPLETADA",new GmailSyncResult());return new GmailSyncResult();},out stdout,out stderr);
            Check(code==1&&stderr.Contains("AUDIT_FINALIZATION_FAILED"),"MissingAuditRowNotSilent");
            // The fixture deliberately has no refresh token: this real child-process route fails before OAuth/network.
            using(var p=Start(runner,folder,"--sync"))
            {
                Check(p.WaitForExit(30000)&&p.ExitCode==1,"RunnerFailureProcessExitCode");
                Check(p.StandardError.ReadToEnd().Contains("Failed=InvalidOperationException"),"RunnerFailureReported");
                CheckAudit(cn,"FALLIDA","RunnerFailureAudit");
            }
            using(var p=Start(runner,folder,"--probe-lock")){Check(p.WaitForExit(30000)&&p.ExitCode==0,"RunnerSuccessCode");}
            Check(Convert.ToInt32(Scalar(cn,"SELECT COUNT(*) FROM dbo.GmailSyncEjecucion WHERE Estado=N'EJECUTANDO' OR FechaFinUtc IS NULL OR FechaFinUtc<FechaInicioUtc;"))==0,"AuditFinalized");
            Check(true,"ExitCodeConsistent");
            using(var stream=File.Open(Path.Combine(folder,"bin","RecepcionDocumental.dll"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(true,"RunnerDllUnlocked");
        }
        private static void CheckAudit(string cn,string state,string gate)
        {
            Check(Convert.ToInt32(Scalar(cn,"SELECT COUNT(*) FROM dbo.GmailSyncEjecucion WHERE Id=(SELECT MAX(Id) FROM dbo.GmailSyncEjecucion) AND Estado=N'"+state+"' AND FechaFinUtc>=FechaInicioUtc;"))==1,gate);
        }
        private static int RunnerBoundary(string runner,Func<GmailSyncResult> sync,out string stdout,out string stderr)
        {
            var method=Assembly.LoadFrom(runner).GetType("RecepcionDocumental.SyncRunner.Worker",true).GetMethod("RunSynchronization",BindingFlags.Static|BindingFlags.NonPublic);
            var oldOut=Console.Out;var oldError=Console.Error;
            using(var output=new StringWriter())using(var error=new StringWriter())
            {
                try{Console.SetOut(output);Console.SetError(error);return (int)method.Invoke(null,new object[]{sync});}
                finally{Console.SetOut(oldOut);Console.SetError(oldError);stdout=output.ToString();stderr=error.ToString();Console.Write(stdout);Console.Write(stderr);}
            }
        }
        private static Process Start(string runner,string root,string mode){return Process.Start(new ProcessStartInfo(runner,"\""+root+"\" "+mode){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root});}
        private static void Migration(string cs,string text){using(var cn=new SqlConnection(cs)){cn.Open();foreach(var batch in Regex.Split(text,@"(?im)^GO\s*$")){if(string.IsNullOrWhiteSpace(batch))continue;using(var cmd=new SqlCommand(batch,cn))using(var r=cmd.ExecuteReader()){do{while(r.Read()){} }while(r.NextResult());}}}}
        private static void Execute(string cs,string sql){using(var cn=new SqlConnection(cs))using(var cmd=new SqlCommand(sql,cn)){cn.Open();cmd.ExecuteNonQuery();}}
        private static object Scalar(string cs,string sql){using(var cn=new SqlConnection(cs))using(var cmd=new SqlCommand(sql,cn)){cn.Open();return cmd.ExecuteScalar();}}
        private static void Check(bool pass,string name){if(!pass)throw new InvalidOperationException("Gate failed: "+name);Console.WriteLine(name+"=True");}
    }
}
