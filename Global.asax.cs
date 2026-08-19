using System;
using System.Web;
using System.Web.Optimization;
using System.Web.Routing;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;

namespace RecepcionDocumental
{
    public class Global : HttpApplication
    {
        void Application_Start(object sender, EventArgs e)
        {
            var configuracion = ConfiguracionIni.CargarDesdeRaizAplicacion();
            configuracion.PrepararRutasOperativas();
            ConfiguracionSistema.Inicializar(configuracion);
            Logs.Inicializar(configuracion);
            Logs.LogProc("Aplicación iniciada. Configuración y logging inicializados.");

            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
        }

        void Application_End(object sender, EventArgs e)
        {
            if (!Logs.EstaInicializado) return;
            try { Logs.LogProc("Aplicación finalizada."); }
            catch (Exception) { }
        }

        void Application_Error(object sender, EventArgs e)
        {
            if (!Logs.EstaInicializado) return;
            try
            {
                var exception = Server.GetLastError();
                var path = Context != null && Context.Request != null ? Context.Request.Path : "(ruta no disponible)";
                Logs.LogError("ASP.NET global | Ruta=" + Logs.SanitizarMensaje(path) + " | " + Logs.DescribirExcepcion(exception));
            }
            catch (Exception) { }
        }
    }
}
