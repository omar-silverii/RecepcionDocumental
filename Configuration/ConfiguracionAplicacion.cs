using System;
using System.IO;
using System.Text;

namespace RecepcionDocumental.Configuration
{
    public sealed class ConfiguracionAplicacion
    {
        public ConfiguracionAplicacion(string nombreProyecto, string rutaLogs, string rutaTrabajo, string rutaFacturas, string rutaRevisar,
            int zipMaxEntradas, long zipMaxBytesPorArchivo, long zipMaxBytesDescomprimidos, int zipMaxProfundidad)
        {
            NombreProyecto = ValidarNombreProyecto(nombreProyecto);
            RutaLogs = ValidarRuta("Rutas/Logs", rutaLogs);
            RutaTrabajo = ValidarRuta("Rutas/Trabajo", rutaTrabajo);
            RutaFacturas = ValidarRuta("Rutas/Facturas", rutaFacturas);
            RutaRevisar = ValidarRuta("Rutas/Revisar", rutaRevisar);
            ZipMaxEntradas = ValidarEnteroPositivo("Zip/MaxEntradas", zipMaxEntradas);
            ZipMaxBytesPorArchivo = ValidarLongPositivo("Zip/MaxBytesPorArchivo", zipMaxBytesPorArchivo);
            ZipMaxBytesDescomprimidos = ValidarLongPositivo("Zip/MaxBytesDescomprimidos", zipMaxBytesDescomprimidos);
            ZipMaxProfundidad = ValidarEnteroPositivo("Zip/MaxProfundidad", zipMaxProfundidad);
            if (ZipMaxBytesDescomprimidos < ZipMaxBytesPorArchivo)
                throw new ConfiguracionAplicacionException("Zip/MaxBytesDescomprimidos no puede ser menor que Zip/MaxBytesPorArchivo.");
        }

        public string NombreProyecto { get; private set; }
        public string RutaLogs { get; private set; }
        public string RutaTrabajo { get; private set; }
        public string RutaFacturas { get; private set; }
        public string RutaRevisar { get; private set; }
        public int ZipMaxEntradas { get; private set; }
        public long ZipMaxBytesPorArchivo { get; private set; }
        public long ZipMaxBytesDescomprimidos { get; private set; }
        public int ZipMaxProfundidad { get; private set; }

        public void PrepararRutasOperativas()
        {
            PrepararRuta(RutaLogs); PrepararRuta(RutaTrabajo); PrepararRuta(RutaFacturas); PrepararRuta(RutaRevisar);
        }

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

        private static int ValidarEnteroPositivo(string clave, int valor)
        { if (valor <= 0) throw new ConfiguracionAplicacionException(clave + " debe ser mayor que cero."); return valor; }

        private static long ValidarLongPositivo(string clave, long valor)
        { if (valor <= 0) throw new ConfiguracionAplicacionException(clave + " debe ser mayor que cero."); return valor; }

        private static void PrepararRuta(string ruta)
        {
            Directory.CreateDirectory(ruta);
            var prueba = Path.Combine(ruta, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            try { using (new FileStream(prueba, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { } }
            finally { if (File.Exists(prueba)) File.Delete(prueba); }
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
