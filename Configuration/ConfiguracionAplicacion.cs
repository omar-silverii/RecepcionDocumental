using System;
using System.IO;
using System.Text;

namespace RecepcionDocumental.Configuration
{
    public sealed class ConfiguracionAplicacion
    {
        public ConfiguracionAplicacion(string nombreProyecto, string rutaLogs)
        {
            NombreProyecto = ValidarNombreProyecto(nombreProyecto);
            RutaLogs = ValidarRuta("Rutas/Logs", rutaLogs);
        }

        public string NombreProyecto { get; private set; }
        public string RutaLogs { get; private set; }

        private static string ValidarNombreProyecto(string valor)
        {
            var nombre = Normalizar(valor);
            if (!string.Equals(nombre, "RecepcionDocumental", StringComparison.Ordinal))
                throw new ConfiguracionAplicacionException("General/NombreProyecto debe ser RecepcionDocumental.");
            if (nombre.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ConfiguracionAplicacionException("General/NombreProyecto no es válido para nombrar archivos.");
            return nombre;
        }

        private static string ValidarRuta(string clave, string valor)
        {
            var ruta = Normalizar(valor);
            if (string.IsNullOrWhiteSpace(ruta)) throw new ConfiguracionAplicacionException("La clave " + clave + " es obligatoria.");
            if (!Path.IsPathRooted(ruta)) throw new ConfiguracionAplicacionException("La clave " + clave + " debe contener una ruta absoluta.");
            try { return Path.GetFullPath(ruta); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            { throw new ConfiguracionAplicacionException("La clave " + clave + " contiene una ruta inválida.", ex); }
        }

        internal static string Normalizar(string valor)
        {
            if (valor == null) return string.Empty;
            try { return valor.Trim().Normalize(NormalizationForm.FormC); }
            catch (ArgumentException ex) { throw new ConfiguracionAplicacionException("La configuración contiene texto Unicode inválido.", ex); }
        }
    }

    public sealed class ConfiguracionAplicacionException : InvalidOperationException
    {
        public ConfiguracionAplicacionException(string mensaje) : base(mensaje) { }
        public ConfiguracionAplicacionException(string mensaje, Exception innerException) : base(mensaje, innerException) { }
    }
}
