using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Data;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Security;
using RecepcionDocumental.Services;

namespace RecepcionDocumental.SyncRunner
{
    internal static class Program
    {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        private static extern bool SetDllDirectory(string path);
        private static int Main(string[] args)
        {
            if(args.Length==3&&args[0]=="--inner")return IsValidMode(args[2])?Worker.Run(args[1],args[2]):2;

            string monitorRoot;
            if(TryGetMonitorInvocation(args,out monitorRoot))return RunMonitor(monitorRoot);

            if(args.Length<1||args.Length>2){Console.Error.WriteLine("Uso: RecepcionDocumental.SyncRunner.exe <raíz-producto> [--sync|--sync-web|--verify-config|--probe-lock|--probe-lock-hold] | MONITOR [raíz-producto]");return 2;}
            var mode=args.Length==2?args[1]:"--sync";
            if(!IsValidMode(mode))return 2;
            return RunProductMode(args[0],mode);
        }
        private static bool IsValidMode(string mode)
        {return mode=="--sync"||mode=="--sync-web"||mode=="--verify-config"||mode=="--probe-lock"||mode=="--probe-lock-hold";}

        private static bool TryGetMonitorInvocation(string[] args,out string explicitRoot)
        {
            explicitRoot=null;
            if(args.Length==1&&string.Equals(args[0],"MONITOR",StringComparison.OrdinalIgnoreCase))return true;
            if(args.Length==2&&string.Equals(args[0],"MONITOR",StringComparison.OrdinalIgnoreCase)){explicitRoot=args[1];return true;}
            if(args.Length==2&&string.Equals(args[1],"MONITOR",StringComparison.OrdinalIgnoreCase)){explicitRoot=args[0];return true;}
            return false;
        }

        private static int RunMonitor(string explicitRoot)
        {
            try
            {
                if(!Environment.Is64BitProcess)return 2;
                bool firstMonitorInstance;
                using(var monitorMutex=new System.Threading.Mutex(true,"Global\\RecepcionDocumental.SyncRunner.MONITOR",out firstMonitorInstance))
                {
                    if(!firstMonitorInstance)
                    {
                        Console.WriteLine("Monitor | Estado=YA_EN_EJECUCION");
                        return 0;
                    }

                    var root=string.IsNullOrWhiteSpace(explicitRoot)?ResolveMonitorProductRoot():Path.GetFullPath(explicitRoot);
                    if(!IsProductRoot(root))throw new InvalidOperationException("No se pudo localizar la raíz de RecepcionDocumental para modo MONITOR.");

                    var executableDirectory=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
                    using(var monitor=CorporateMonitorSession.Create(executableDirectory))
                    {
                        Console.WriteLine("Monitor | Estado=INICIADO | Nombre="+Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location));
                        monitor.Start();
                        var nextRunUtc=DateTime.UtcNow;
                        while(!monitor.StopRequested)
                        {
                            var wait=nextRunUtc-DateTime.UtcNow;
                            if(wait>TimeSpan.Zero&&monitor.WaitForStop(wait))break;
                            if(monitor.StopRequested)break;

                            var code=RunProductMode(root,"--sync");
                            Console.WriteLine("Monitor | SincronizacionExitCode="+code);

                            nextRunUtc=nextRunUtc.AddMinutes(5);
                            while(nextRunUtc<=DateTime.UtcNow)nextRunUtc=nextRunUtc.AddMinutes(5);
                        }
                        monitor.FinalizeWhenPossible();
                    }
                    GC.KeepAlive(monitorMutex);
                    return 0;
                }
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine("Monitor | Failed="+ex.GetType().Name);
                return 1;
            }
        }

        private static int RunProductMode(string rootArgument,string mode)
        {
            try
            {
                var root=Path.GetFullPath(rootArgument);
                if(!IsProductRoot(root))throw new InvalidOperationException("Configuración de producto incompleta.");
                Directory.SetCurrentDirectory(root);
                if(!SetDllDirectory(Path.Combine(root,"bin")))throw new InvalidOperationException("No se pudo configurar la búsqueda de dependencias nativas.");
                var setup=new AppDomainSetup{ApplicationBase=root,PrivateBinPath="bin",ConfigurationFile=Path.Combine(root,"Web.config")};
                var domain=AppDomain.CreateDomain("RecepcionDocumental.SyncRunner.Product",null,setup);
                try{return domain.ExecuteAssembly(Assembly.GetExecutingAssembly().Location,new[]{"--inner",root,mode});}
                finally{AppDomain.Unload(domain);}
            }
            catch(Exception ex){Console.Error.WriteLine("SyncRunner | Failed="+ex.GetType().Name);return 1;}
        }

        private static string ResolveMonitorProductRoot()
        {
            var candidates=new List<string>();
            AddCandidate(candidates,Environment.CurrentDirectory);
            var executableDirectory=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            AddCandidate(candidates,executableDirectory);

            var current=new DirectoryInfo(executableDirectory);
            for(var i=0;current!=null&&i<6;i++,current=current.Parent)
            {
                AddCandidate(candidates,current.FullName);
                AddCandidate(candidates,Path.Combine(current.FullName,"Site"));
            }

            foreach(var candidate in candidates)
                if(IsProductRoot(candidate))return Path.GetFullPath(candidate);
            throw new InvalidOperationException("No se encontró Web.config y RecepcionDocumental.ini para modo MONITOR.");
        }

        private static void AddCandidate(List<string> candidates,string candidate)
        {
            if(string.IsNullOrWhiteSpace(candidate))return;
            string full;
            try{full=Path.GetFullPath(candidate);}catch{return;}
            foreach(var existing in candidates)
                if(string.Equals(existing,full,StringComparison.OrdinalIgnoreCase))return;
            candidates.Add(full);
        }

        private static bool IsProductRoot(string root)
        {
            if(string.IsNullOrWhiteSpace(root))return false;
            try{return File.Exists(Path.Combine(root,"Web.config"))&&File.Exists(Path.Combine(root,"RecepcionDocumental.ini"));}
            catch{return false;}
        }
    }
    internal static class Worker
    {
        internal static int Run(string root,string mode)
        {
            try
            {
                if(!Environment.Is64BitProcess)return 2;
                var cfg=ConfiguracionIni.Cargar(Path.Combine(root,ConfiguracionIni.NombreArchivo));
                cfg.PrepararRutasOperativas();ConfiguracionSistema.Inicializar(cfg);Logs.Inicializar(cfg);
                Console.WriteLine("SyncRunner | ProcessX64=True");
                if(mode=="--probe-lock"||mode=="--probe-lock-hold"||mode=="--verify-config")
                {
                    using(var lease=GmailSyncLease.TryAcquire())
                    {
                        if(lease==null){Console.WriteLine("YA_EN_EJECUCION");return 10;}
                        if(mode=="--verify-config")
                        {
                            var account=GmailSyncRepository.GetActiveAccount();
                            if(account==null||string.IsNullOrEmpty(RefreshTokenProtector.Unprotect(account.ProtectedRefreshToken)))throw new InvalidOperationException("Credencial existente no disponible.");
                            GoogleOAuthSettings settings;string error;if(!GoogleOAuthSettings.TryLoad(out settings,out error))throw new InvalidOperationException("Variables OAuth no disponibles.");
                            Console.WriteLine("ExistingTokenDecryptable=True; ExistingOAuthConfiguration=True; NoGmailRequest=True");
                        }
                        Console.WriteLine("LockAcquired=True");Console.Out.Flush();
                        if(mode=="--probe-lock-hold")System.Threading.Thread.Sleep(5000);
                        return 0;
                    }
                }
                var origin=mode=="--sync-web"?"WEB":"SCHEDULER";
                return RunSynchronization(()=>GmailSyncService.SynchronizeAsync(origin).GetAwaiter().GetResult());
            }
            catch(Exception ex){return ReportFailure(ex);}
        }
        // This same boundary is exercised by the isolated probe without issuing Gmail requests.
        internal static int RunSynchronization(Func<GmailSyncResult> synchronize)
        {
            try
            {
                var result=synchronize();
                var code=result.AlreadyRunning?10:result.Errores==0?0:1;
                var summary="SyncRunner | Estado="+(result.AlreadyRunning?"YA_EN_EJECUCION":result.Errores==0?"COMPLETADA":"COMPLETADA_CON_ERRORES")+" | Mensajes="+result.MensajesEncontrados+" | Nuevos="+result.MensajesNuevos+" | Errores="+result.Errores+" | ExitCode="+code;
                Console.WriteLine(summary);try{Logs.LogProc(summary);}catch{}
                return code;
            }
            catch(Exception ex){return ReportFailure(ex);}
        }
        private static int ReportFailure(Exception ex)
        {
            var audit=ex as GmailSyncAuditException;
            var auditCode=audit==null?ex.Data["AuditFailureCode"] as string:audit.Code;
            var message="SyncRunner | Failed="+ex.GetType().Name+(auditCode==null?"":" | "+auditCode)+" | ExitCode=1";
            Console.Error.WriteLine(message);try{Logs.LogError(message);}catch{}return 1;
        }
    }
}
