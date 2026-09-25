using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using System.Threading;

namespace RecepcionDocumental.SyncRunner
{
    internal sealed class CorporateMonitorSession : IDisposable
    {
        private const int MonitorPollMilliseconds = 3000;
        private readonly string _connectionString;
        private readonly string _programName;
        private readonly ManualResetEvent _stopRequestedEvent = new ManualResetEvent(false);
        private Thread _heartbeatThread;
        private volatile bool _stopRequested;
        private volatile bool _disposed;
        private bool _monitorErrorReported;

        private CorporateMonitorSession(string connectionString, string programName)
        {
            _connectionString = connectionString;
            _programName = programName;
        }

        internal bool StopRequested { get { return _stopRequested; } }

        internal static CorporateMonitorSession Create(string executableDirectory)
        {
            if (string.IsNullOrWhiteSpace(executableDirectory))
                throw new ArgumentException("Directorio del ejecutable inválido.", "executableDirectory");

            string connectionString = LoadMonitorConnectionString(executableDirectory);
            string programName = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrWhiteSpace(programName))
                throw new InvalidOperationException("No se pudo determinar el nombre del ejecutable para LogTrabajos.");

            return new CorporateMonitorSession(connectionString, programName);
        }

        internal void Start()
        {
            // Primera consulta sincrónica: si el monitor ya pidió finalizar, no se inicia otra captura.
            CheckAndHeartbeat();
            if (_stopRequested) return;

            _heartbeatThread = new Thread(HeartbeatLoop);
            _heartbeatThread.IsBackground = true;
            _heartbeatThread.Name = "RecepcionDocumental.MonitorHeartbeat";
            _heartbeatThread.Start();
        }

        internal bool WaitForStop(TimeSpan timeout)
        {
            return _stopRequestedEvent.WaitOne(timeout);
        }

        internal void FinalizeWhenPossible()
        {
            // Igual que el contrato legado: si SQL está temporalmente caído, no se declara finalizado
            // hasta poder actualizar LogTrabajos. No se inicia una nueva sincronización mientras tanto.
            while (true)
            {
                try
                {
                    using (var cn = new SqlConnection(_connectionString))
                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandTimeout = 600;
                        cmd.CommandText =
                            "UPDATE LogTrabajos SET Finalizacion=GETDATE(), ACTIVO='NO' " +
                            "WHERE LEFT(Nombre,39)=@Nombre AND Finalizacion IS NULL";
                        cmd.Parameters.Add("@Nombre", SqlDbType.VarChar, 39).Value = _programName;
                        cn.Open();
                        cmd.ExecuteNonQuery();
                    }
                    Console.WriteLine("Monitor | Estado=FINALIZADO | Nombre=" + _programName);
                    return;
                }
                catch (Exception ex)
                {
                    ReportMonitorError("FINALIZACION", ex);
                    Thread.Sleep(MonitorPollMilliseconds);
                }
            }
        }

        private void HeartbeatLoop()
        {
            while (!_disposed && !_stopRequested)
            {
                if (_stopRequestedEvent.WaitOne(MonitorPollMilliseconds)) break;
                CheckAndHeartbeat();
            }
        }

        private void CheckAndHeartbeat()
        {
            try
            {
                long flag = 0;
                bool hasActiveRow = false;

                using (var cn = new SqlConnection(_connectionString))
                {
                    cn.Open();
                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandTimeout = 600;
                        cmd.CommandText =
                            "SELECT TOP (1) FlagFozarFinalizar FROM LogTrabajos " +
                            "WHERE LEFT(Nombre,39)=@Nombre AND Finalizacion IS NULL";
                        cmd.Parameters.Add("@Nombre", SqlDbType.VarChar, 39).Value = _programName;
                        object value = cmd.ExecuteScalar();
                        if (value != null)
                        {
                            hasActiveRow = true;
                            if (value != DBNull.Value) flag = Convert.ToInt64(value);
                        }
                    }

                    if (hasActiveRow)
                    {
                        using (var heartbeat = cn.CreateCommand())
                        {
                            heartbeat.CommandTimeout = 600;
                            heartbeat.CommandText =
                                "UPDATE LogTrabajos SET UltimoFlag=GETDATE() " +
                                "WHERE LEFT(Nombre,39)=@Nombre AND Finalizacion IS NULL";
                            heartbeat.Parameters.Add("@Nombre", SqlDbType.VarChar, 39).Value = _programName;
                            heartbeat.ExecuteNonQuery();
                        }
                    }
                }

                _monitorErrorReported = false;
                if (flag == 1)
                {
                    _stopRequested = true;
                    _stopRequestedEvent.Set();
                    Console.WriteLine("Monitor | Estado=FINALIZACION_SOLICITADA | Nombre=" + _programName);
                }
            }
            catch (Exception ex)
            {
                // El proceso de referencia no termina por una falla transitoria de ChkMonitor.
                ReportMonitorError("CHKMONITOR", ex);
            }
        }

        private void ReportMonitorError(string stage, Exception ex)
        {
            if (_monitorErrorReported) return;
            _monitorErrorReported = true;
            Console.Error.WriteLine("Monitor | Estado=ERROR | Etapa=" + stage + " | Tipo=" + ex.GetType().Name);
        }

        private static string LoadMonitorConnectionString(string executableDirectory)
        {
            string previousDirectory = Environment.CurrentDirectory;
            try
            {
                // EdmsCapDN0160 obtiene SQLDBCN mediante EdmsCFG.clsConfig/TraeValor.
                // Se conserva ese contrato y se fuerza como base el directorio del EXE,
                // donde el estándar corporativo ubica Edms_CFG.xml.
                Directory.SetCurrentDirectory(executableDirectory);
                var cfg = new EdmsCFG.clsConfig();
                string value = Convert.ToString(cfg.get_TraeValor("SQLDBCN"));
                value = value == null ? string.Empty : value.Trim();
                if (value.Length == 0)
                    throw new InvalidOperationException("SQLDBCN no está configurado en Edms_CFG.xml.");
                return value;
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _stopRequestedEvent.Set();
            if (_heartbeatThread != null && _heartbeatThread.IsAlive)
                _heartbeatThread.Join(5000);
            _stopRequestedEvent.Dispose();
        }
    }
}
