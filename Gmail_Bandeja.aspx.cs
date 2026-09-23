using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.UI;
using RecepcionDocumental.Controls;
using RecepcionDocumental.Data;
using RecepcionDocumental.Services;

namespace RecepcionDocumental
{
    public sealed class GmailSyncStatusView
    {
        public long Id { get; set; }
        public bool EnEjecucion { get; set; }
        public bool EsTerminal { get; set; }
        public bool BloqueaUi { get; set; }
        public string Texto { get; set; }
    }

    public partial class Gmail_Bandeja : Page
    {
        protected override void OnInit(EventArgs e)
        {
            base.OnInit(e);
            lstMensajes.FiltersChanged += Listado_FiltersChanged;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            ConfigureList();

            if (!IsPostBack)
            {
                lstMensajes.EnsureDefaultFilters();
                LoadPageData();
            }
        }

        protected void Buscar_Click(object sender, EventArgs e)
        {
            pnlResultado.Visible = false;
            var previous = GmailSyncAuditRepository.Latest();
            var previousId = previous == null ? 0 : previous.Id;
            var launch = GmailManualSyncLauncher.Start();
            LoadPageData();
            if (launch.Started)
            {
                pnlResultado.Visible = false;
                btnBuscar.Enabled = false;
                hidSyncPolling.Value = "1";
                hidSyncBaseline.Value = previousId.ToString(CultureInfo.InvariantCulture);
                pnlSyncOverlay.CssClass = "sync-overlay is-active";
                litSyncStatus.Text = Server.HtmlEncode("BUSCANDO / EN EJECUCIÓN");
                ShowNotice(launch.Message);
            }
            else ShowError(launch.Message);
        }

        public static GmailSyncStatusView BuildSyncStatus(GmailSyncAuditInfo latest)
        {
            if (latest == null)
                return new GmailSyncStatusView { Texto = "Todavía no hay ejecuciones de recepción registradas." };

            var running = string.Equals(latest.Estado, "EJECUTANDO", StringComparison.OrdinalIgnoreCase);
            var localStart = DateTime.SpecifyKind(latest.Inicio, DateTimeKind.Utc).ToLocalTime();
            string text;
            if (running)
                text = "BUSCANDO / EN EJECUCIÓN desde " + localStart.ToString("HH:mm:ss") + " | Origen: " + latest.Origen;
            else if (string.Equals(latest.Estado, "COMPLETADA", StringComparison.OrdinalIgnoreCase))
                text = "Búsqueda completada | Mensajes: " + latest.Mensajes + " | Errores: " + latest.Errores;
            else if (string.Equals(latest.Estado, "COMPLETADA_CON_ERRORES", StringComparison.OrdinalIgnoreCase))
                text = "Búsqueda completada con errores | Mensajes: " + latest.Mensajes + " | Errores: " + latest.Errores;
            else if (string.Equals(latest.Estado, "FALLIDA", StringComparison.OrdinalIgnoreCase))
                text = "Búsqueda fallida | Errores: " + latest.Errores;
            else if (string.Equals(latest.Estado, "OMITIDA_YA_EN_EJECUCION", StringComparison.OrdinalIgnoreCase))
                text = "Búsqueda omitida: ya había otra ejecución en curso.";
            else
                text = "Última recepción: " + localStart.ToString("dd/MM/yyyy HH:mm") + " | " + latest.Estado + " | Mensajes: " + latest.Mensajes + " | Errores: " + latest.Errores;

            var blocksUi = running && string.Equals(latest.Origen, "WEB", StringComparison.OrdinalIgnoreCase);
            return new GmailSyncStatusView { Id = latest.Id, EnEjecucion = running, EsTerminal = IsTerminalState(latest.Estado), BloqueaUi = blocksUi, Texto = text };
        }

        internal static bool IsTerminalState(string state)
        {
            return string.Equals(state, "COMPLETADA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "COMPLETADA_CON_ERRORES", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "FALLIDA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "OMITIDA_YA_EN_EJECUCION", StringComparison.OrdinalIgnoreCase);
        }

        private void Listado_FiltersChanged(object sender, EventArgs e)
        {
            pnlResultado.Visible = false;
            LoadPageData();
        }

        private void ConfigureList()
        {
            lstMensajes.ShowStatusFilter = true;
            lstMensajes.StatusLabel = "Estado";
            lstMensajes.TextFilterLabel = "Asunto";
            lstMensajes.TextFilterPlaceholder = "Buscar texto";
            lstMensajes.PrimaryCountLabel = "mensajes";
            lstMensajes.MetricALabel = "adjuntos";
            lstMensajes.MetricBLabel = "procesados";
            lstMensajes.MetricCLabel = "errores";
            lstMensajes.MetricCCssClass = "text-danger fw-semibold";
            lstMensajes.TableCssClass = "gmail-inbox-table";
            lstMensajes.EmptyTitle = "No hay mensajes para los filtros seleccionados.";
            lstMensajes.EmptyText = "Probá otro período o restablecé los filtros.";
        }

        private void LoadPageData()
        {
            pnlDatabaseWarning.Visible = false;

            var latest = GmailSyncAuditRepository.Latest();
            var syncStatus = BuildSyncStatus(latest);
            litSyncStatus.Text = Server.HtmlEncode(syncStatus.Texto);
            hidSyncPolling.Value = syncStatus.BloqueaUi ? "1" : "0";
            hidSyncBaseline.Value = (syncStatus.BloqueaUi ? Math.Max(0, syncStatus.Id - 1) : syncStatus.Id).ToString(CultureInfo.InvariantCulture);
            pnlSyncOverlay.CssClass = syncStatus.BloqueaUi ? "sync-overlay is-active" : "sync-overlay";
            BindFinalSummary(latest, syncStatus);

            try
            {
                var account = GmailSyncRepository.GetActiveAccount();
                pnlSinCuenta.Visible = account == null;
                btnBuscar.Visible = account != null;
                btnBuscar.Enabled = account != null && !syncStatus.EnEjecucion;

                DateTime desdeUtc;
                DateTime hastaUtcExclusivo;
                if (!lstMensajes.TryGetUtcRange(out desdeUtc, out hastaUtcExclusivo)) return;

                IList<GmailMensajeInfo> messages;
                if (!GmailRepository.TryGetMensajes(
                    desdeUtc,
                    hastaUtcExclusivo,
                    lstMensajes.SenderFilter,
                    lstMensajes.StateFilter,
                    lstMensajes.TextFilter,
                    out messages))
                {
                    ShowError("No se pudo consultar la bandeja. Verificá la base de datos.");
                    return;
                }

                var columns = new List<WsListadoColumna>
                {
                    new WsListadoColumna { Titulo = "Fecha / hora", CssClass = "col-time" },
                    new WsListadoColumna { Titulo = "Remitente", CssClass = "col-sender" },
                    new WsListadoColumna { Titulo = "Asunto", CssClass = "col-subject" },
                    new WsListadoColumna { Titulo = "Adjuntos", CssClass = "col-attachments text-center" },
                    new WsListadoColumna { Titulo = "Estado", CssClass = "col-status" },
                    new WsListadoColumna { Titulo = string.Empty, CssClass = "col-action" }
                };

                var rows = messages.Select(ToListRow).ToList();
                lstMensajes.BindData(columns, rows);
            }
            catch (System.Data.SqlClient.SqlException) { ShowError("No se pudo consultar la estructura H1C. Ejecutá Database/003_GmailSync.sql."); }
        }

        private void BindFinalSummary(GmailSyncAuditInfo latest, GmailSyncStatusView status)
        {
            pnlResultado.Visible = latest != null && status.EsTerminal;
            if (!pnlResultado.Visible) return;

            var failed = string.Equals(latest.Estado, "FALLIDA", StringComparison.OrdinalIgnoreCase);
            var withErrors = string.Equals(latest.Estado, "COMPLETADA_CON_ERRORES", StringComparison.OrdinalIgnoreCase);
            var omitted = string.Equals(latest.Estado, "OMITIDA_YA_EN_EJECUCION", StringComparison.OrdinalIgnoreCase);
            pnlResultado.CssClass = failed ? "alert alert-danger" : withErrors || omitted ? "alert alert-warning" : "alert alert-success";
            litResultadoTitulo.Text = Server.HtmlEncode(failed ? "Búsqueda fallida" : omitted ? "Búsqueda omitida" : withErrors ? "Búsqueda completada con errores" : "Búsqueda completada");
            litEncontrados.Text = latest.Mensajes.ToString(CultureInfo.InvariantCulture);
            litNuevos.Text = latest.MensajesNuevos.ToString(CultureInfo.InvariantCulture);
            litAnalizados.Text = latest.AdjuntosAnalizados.ToString(CultureInfo.InvariantCulture);
            litFacturas.Text = latest.Facturas.ToString(CultureInfo.InvariantCulture);
            litRevisar.Text = latest.Revisar.ToString(CultureInfo.InvariantCulture);
            litDescartados.Text = latest.Descartados.ToString(CultureInfo.InvariantCulture);
            litDocumentosExistentes.Text = latest.DocumentosExistentes.ToString(CultureInfo.InvariantCulture);
            litErrores.Text = latest.Errores.ToString(CultureInfo.InvariantCulture);
            litFallback.Text = latest.UsoFallbackInicial ? "<p class=\"mb-0 mt-2\">Se utilizó la búsqueda inicial de Gmail.</p>" : string.Empty;
        }

        private static WsListadoFila ToListRow(GmailMensajeInfo item)
        {
            var fechaLocal = DateTime.SpecifyKind(item.FechaMensajeUtc, DateTimeKind.Utc).ToLocalTime();
            var estadoCss = "bg-secondary";
            if (string.Equals(item.Estado, "Con errores", StringComparison.OrdinalIgnoreCase)) estadoCss = "bg-danger";
            else if (string.Equals(item.Estado, "Procesado", StringComparison.OrdinalIgnoreCase)) estadoCss = "bg-success";
            else if (string.Equals(item.Estado, "Descargado", StringComparison.OrdinalIgnoreCase)) estadoCss = "bg-primary";

            var estadoHtml = "<span class=\"badge " + estadoCss + "\">" + HttpUtility.HtmlEncode(item.Estado) + "</span>";
            var actionHtml = "<a href=\"Gmail_Mensaje_Ver.aspx?id=" + item.Id + "\" class=\"text-decoration-none\">Ver</a>";

            return new WsListadoFila
            {
                FechaLocal = fechaLocal,
                Remitente = item.Remitente,
                MetricA = item.CantidadAdjuntos,
                MetricB = string.Equals(item.Estado, "Procesado", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                MetricC = string.Equals(item.Estado, "Con errores", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                Celdas = new List<WsListadoCelda>
                {
                    WsListadoCelda.Texto(fechaLocal.ToString("dd/MM HH:mm"), "text-nowrap"),
                    WsListadoCelda.Texto(item.Remitente, "text-truncate", item.Remitente),
                    WsListadoCelda.Texto(item.Asunto, "text-truncate", item.Asunto),
                    WsListadoCelda.Texto(item.CantidadAdjuntos.ToString(), "text-center"),
                    WsListadoCelda.Html(estadoHtml),
                    WsListadoCelda.Html(actionHtml, "text-end")
                }
            };
        }

        private void ShowError(string message)
        {
            pnlDatabaseWarning.Visible = true;
            pnlDatabaseWarning.CssClass = "alert alert-warning";
            litDatabaseWarning.Text = Server.HtmlEncode(message);
        }

        private void ShowNotice(string message)
        {
            pnlDatabaseWarning.Visible = true;
            pnlDatabaseWarning.CssClass = "alert alert-success";
            litDatabaseWarning.Text = Server.HtmlEncode(message);
        }
    }
}
