using System;

namespace RecepcionDocumental.Configuration
{
    public static class ConfiguracionSistema
    {
        private static readonly object Sincronizacion = new object();
        private static ConfiguracionAplicacion actual;

        public static void Inicializar(ConfiguracionAplicacion configuracion)
        {
            if (configuracion == null) throw new ArgumentNullException("configuracion");
            lock (Sincronizacion)
            {
                if (actual != null) throw new InvalidOperationException("La configuración del sistema ya fue inicializada.");
                actual = configuracion;
            }
        }

        public static ConfiguracionAplicacion Actual
        {
            get { lock (Sincronizacion) { if (actual == null) throw new InvalidOperationException("La configuración del sistema aún no fue inicializada."); return actual; } }
        }
    }
}
