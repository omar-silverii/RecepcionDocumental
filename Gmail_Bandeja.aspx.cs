using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using RecepcionDocumental.Controls;
using RecepcionDocumental.Data;
using RecepcionDocumental.Services;

namespace RecepcionDocumental
{
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
            var launch = GmailManualSyncLauncher.Start();
            LoadPageData();
            if (launch.Started) ShowNotice(launch.Message);
            else ShowError(launch.Message);
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
            var syncStatus = latest == null
                ? "Todavía no hay ejecuciones de recepción registradas."
                : string.Equals(latest.Estado, "EJECUTANDO", StringComparison.OrdinalIgnoreCase)
                    ? "Búsqueda en ejecución desde " + latest.Inicio.ToLocalTime().ToString("HH:mm:ss") + " | Origen: " + latest.Origen
                    : "Última recepción: " + latest.Inicio.ToLocalTime().ToString("dd/MM/yyyy HH:mm") + " | " + latest.Estado + " | " + latest.Origen + " | Mensajes: " + latest.Mensajes + " | Errores: " + latest.Errores;
            litSyncStatus.Text = Server.HtmlEncode(syncStatus);

            try
            {
                var account = GmailSyncRepository.GetActiveAccount();
                pnlSinCuenta.Visible = account == null;
                btnBuscar.Visible = account != null;

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
