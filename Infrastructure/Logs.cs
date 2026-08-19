using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RecepcionDocumental.Configuration;

namespace RecepcionDocumental.Infrastructure
{
    public static class Logs
    {
        private static readonly object Sincronizacion = new object();
        private static readonly UTF8Encoding Utf8SinBom = new UTF8Encoding(false, true);
        private static string carpeta;
        private static string nombreProyecto;

        public static bool EstaInicializado { get { lock (Sincronizacion) { return carpeta != null && nombreProyecto != null; } } }

        public static void Inicializar(ConfiguracionAplicacion configuracion)
        {
            if (configuracion == null) throw new ArgumentNullException("configuracion");
            lock (Sincronizacion)
            {
                Directory.CreateDirectory(configuracion.RutaLogs);
                var prueba = Path.Combine(configuracion.RutaLogs, ".log-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var flujo = new FileStream(prueba, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var escritor = new StreamWriter(flujo, Utf8SinBom)) { escritor.Write("OK"); }
                }
                finally { if (File.Exists(prueba)) File.Delete(prueba); }
                carpeta = configuracion.RutaLogs;
                nombreProyecto = configuracion.NombreProyecto;
            }
        }

        public static void LogProc(string mensaje) { Escribir("Proc", mensaje); }
        public static void LogError(string mensaje) { Escribir("Error", mensaje); }

        public static string DescribirExcepcion(Exception excepcion)
        {
            if (excepcion == null) return "Excepción no disponible.";
            return excepcion.GetType().Name + ": " + SanitizarMensaje(excepcion.Message);
        }

        public static string SanitizarMensaje(string mensaje)
        {
            var seguro = (mensaje ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            seguro = Regex.Replace(seguro, @"https?://\S+", "[URL omitida]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            seguro = Regex.Replace(seguro, @"(client[_ ]?secret|access[_ ]?token|refresh[_ ]?token|authorization[_ ]?code|code|token)\s*[:=]\s*[^\s,;]+", "$1=[REDACTADO]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (seguro.Length > 500) seguro = seguro.Substring(0, 500) + "...";
            return seguro;
        }

        private static void Escribir(string tipo, string mensaje)
        {
            if (mensaje == null) throw new ArgumentNullException("mensaje");
            lock (Sincronizacion)
            {
                if (carpeta == null || nombreProyecto == null) throw new InvalidOperationException("El módulo de logs no fue inicializado.");
                var ahora = DateTime.Now;
                var archivo = nombreProyecto + "_" + tipo + "_" + ahora.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".txt";
                var linea = ahora.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " | " + SanitizarMensaje(mensaje);
                using (var flujo = new FileStream(Path.Combine(carpeta, archivo), FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var escritor = new StreamWriter(flujo, Utf8SinBom)) { escritor.WriteLine(linea); }
            }
        }
    }
}
