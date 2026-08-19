using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RecepcionDocumental.Configuration
{
    public static class ConfiguracionIni
    {
        public const string NombreArchivo = "RecepcionDocumental.ini";

        public static ConfiguracionAplicacion CargarDesdeRaizAplicacion()
        {
            return Cargar(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NombreArchivo));
        }

        public static ConfiguracionAplicacion Cargar(string rutaArchivo)
        {
            if (string.IsNullOrWhiteSpace(rutaArchivo)) throw new ConfiguracionAplicacionException("La ruta del archivo INI es obligatoria.");
            var ruta = Path.GetFullPath(rutaArchivo);
            if (!File.Exists(ruta)) throw new ConfiguracionAplicacionException("No se encontró " + NombreArchivo + " en la raíz física de la aplicación.");

            string[] lineas;
            try { lineas = File.ReadAllLines(ruta, new UTF8Encoding(false, true)); }
            catch (DecoderFallbackException ex) { throw new ConfiguracionAplicacionException("El archivo INI no contiene UTF-8 válido.", ex); }

            var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var secciones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seccion = string.Empty;
            for (var indice = 0; indice < lineas.Length; indice++)
            {
                var linea = lineas[indice].Trim();
                if (linea.Length == 0 || linea.StartsWith(";", StringComparison.Ordinal) || linea.StartsWith("#", StringComparison.Ordinal)) continue;
                if (linea.StartsWith("[", StringComparison.Ordinal) && linea.EndsWith("]", StringComparison.Ordinal))
                {
                    seccion = ConfiguracionAplicacion.Normalizar(linea.Substring(1, linea.Length - 2));
                    if (seccion.Length == 0) throw ErrorLinea(indice, "La sección está vacía.");
                    if (!secciones.Add(seccion)) throw ErrorLinea(indice, "La sección está repetida.");
                    continue;
                }

                var separador = linea.IndexOf('=');
                if (separador <= 0 || seccion.Length == 0) throw ErrorLinea(indice, "La entrada no tiene formato Sección/Clave=Valor.");
                var clave = ConfiguracionAplicacion.Normalizar(linea.Substring(0, separador));
                var valor = ConfiguracionAplicacion.Normalizar(linea.Substring(separador + 1));
                var claveCompleta = seccion + "/" + clave;
                if (clave.Length == 0 || valores.ContainsKey(claveCompleta)) throw ErrorLinea(indice, "La clave está vacía o repetida.");
                valores.Add(claveCompleta, valor);
            }

            return new ConfiguracionAplicacion(Obtener(valores, "General/NombreProyecto"), Obtener(valores, "Rutas/Logs"));
        }

        private static string Obtener(IDictionary<string, string> valores, string clave)
        {
            string valor;
            if (!valores.TryGetValue(clave, out valor) || string.IsNullOrWhiteSpace(valor)) throw new ConfiguracionAplicacionException("Falta la clave obligatoria " + clave + ".");
            return valor;
        }

        private static ConfiguracionAplicacionException ErrorLinea(int indice, string detalle)
        { return new ConfiguracionAplicacionException("INI inválido en la línea " + (indice + 1) + ". " + detalle); }
    }
}
