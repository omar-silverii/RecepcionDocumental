using System;
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
            if(args.Length==3&&args[0]=="--inner")return Worker.Run(args[1],args[2]);
            if(args.Length<1||args.Length>2){Console.Error.WriteLine("Uso: RecepcionDocumental.SyncRunner.exe <raíz-producto> [--verify-config|--probe-lock]");return 2;}
            var mode=args.Length==2?args[1]:"--sync";
            if(mode!="--sync"&&mode!="--verify-config"&&mode!="--probe-lock"&&mode!="--probe-lock-hold")return 2;
            try
            {
                var root=Path.GetFullPath(args[0]);
                if(!File.Exists(Path.Combine(root,"Web.config"))||!File.Exists(Path.Combine(root,"RecepcionDocumental.ini")))throw new InvalidOperationException("Configuración de producto incompleta.");
                Directory.SetCurrentDirectory(root);
                if(!SetDllDirectory(Path.Combine(root,"bin")))throw new InvalidOperationException("No se pudo configurar la búsqueda de dependencias nativas.");
                var setup=new AppDomainSetup{ApplicationBase=root,PrivateBinPath="bin",ConfigurationFile=Path.Combine(root,"Web.config")};
                var domain=AppDomain.CreateDomain("RecepcionDocumental.SyncRunner.Product",null,setup);
                try{return domain.ExecuteAssembly(Assembly.GetExecutingAssembly().Location,new[]{"--inner",root,mode});}
                finally{AppDomain.Unload(domain);}
            }
            catch(Exception ex){Console.Error.WriteLine("SyncRunner | Failed="+ex.GetType().Name);return 1;}
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
                return RunSynchronization(()=>GmailSyncService.SynchronizeAsync("SCHEDULER").GetAwaiter().GetResult());
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
