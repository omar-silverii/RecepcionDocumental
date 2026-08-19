using System;
using System.IO;
using RecepcionDocumental.Configuration;
using RecepcionDocumental.Infrastructure;

namespace RecepcionDocumental.Services
{
    public sealed class AttachmentWorkspace : IDisposable
    {
        public AttachmentWorkspace()
        {
            RootPath = Path.Combine(ConfiguracionSistema.Actual.RutaTrabajo, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; private set; }
        public string CreatePath(string extension) { return Path.Combine(RootPath, Guid.NewGuid().ToString("N") + (extension ?? string.Empty)); }

        public void Dispose()
        {
            try { if (Directory.Exists(RootPath)) Directory.Delete(RootPath, true); Logs.LogProc("DocumentAnalysis | Workspace eliminado"); }
            catch (Exception ex) { Logs.LogError("DocumentAnalysis | Operación=EliminarWorkspace | " + Logs.DescribirExcepcion(ex)); }
        }
    }
}
