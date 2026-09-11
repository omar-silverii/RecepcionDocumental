using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.UI;
using Google;
using RecepcionDocumental.Controls;
using RecepcionDocumental.Data;
using RecepcionDocumental.Services;

namespace RecepcionDocumental
{
    public partial class Gmail_Bandeja : Page
    {
        private const string SyncSessionKey = "GmailSync.Running";

        protected override void OnInit(EventArgs e)
        {
            base.OnInit(e);
            lstMensajes.FiltersChanged += Listado_FiltersChanged;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            Server.ScriptTimeout = 600;
            ConfigureList();

            if (!IsPostBack)
            {
                lstMensajes.EnsureDefaultFilters();
                LoadPageData();
            }
        }

        protected void Buscar_Click(object sender, EventArgs e)
        {
            if (Session[SyncSessionKey] != null) { ShowError("Ya hay una búsqueda en curso para esta sesión."); return; }
            Session[SyncSessionKey] = true;
            RegisterAsyncTask(new PageAsyncTask(RunSyncAsync));
        }

        private void Listado_FiltersChanged(object sender, EventArgs e)
        {
            pnlResultado.Visible = false;
            LoadPageData();
        }

        private async Task RunSyncAsync()
        {
            try
            {
                var result = await GmailSyncService.SynchronizeAsync();
                if (result.AlreadyRunning) { pnlResultado.Visible = false; ShowError("Ya hay una sincronización de Gmail en curso."); LoadPageData(); return; }
                pnlResultado.Visible = true;
                pnlResultado.CssClass = result.Errores == 0 ? "alert alert-success" : "alert alert-warning";
                litEncontrados.Text = result.MensajesEncontrados.ToString();
                litNuevos.Text = result.MensajesNuevos.ToString();
                litAnalizados.Text = result.AdjuntosAnalizados.ToString();
                litFacturas.Text = result.FacturasDetectadas.ToString();
                litRevisar.Text = result.ParaRevisar.ToString();
                litDescartados.Text = result.Descartados.ToString();
                litDocumentosExistentes.Text = result.DocumentosExistentes.ToString();
                litErrores.Text = result.Errores.ToString();
                var notices = result.UsoFallbackInicial ? "<p class=\"mt-2 mb-0\">El cursor de Gmail había vencido; se aplicó la búsqueda inicial limitada.</p>" : string.Empty;
                if (result.Errores > 0) notices += "<p class=\"mt-2 mb-0\">El cursor no se avanzó para permitir reintentar los elementos con error.</p>";
                litFallback.Text = notices;
                LoadPageData();
            }
            catch (GoogleApiException) { ShowError("Gmail no pudo completar la consulta. Revisá la autorización de la cuenta."); }
            catch (UnauthorizedAccessException) { ShowError("La aplicación no tiene permisos para escribir en la carpeta de adjuntos."); }
            catch (System.IO.IOException) { ShowError("No fue posible escribir los adjuntos en la carpeta configurada."); }
            catch (System.Data.SqlClient.SqlException) { ShowError("No fue posible completar la operación en la base de datos. Verificá el script 003."); }
            catch (InvalidOperationException ex) { ShowError(ex.Message); }
            catch (Exception) { ShowError("No fue posible completar la búsqueda de correos."); }
            finally { Session.Remove(SyncSessionKey); btnBuscar.Enabled = true; }
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
            litSyncStatus.Text = Server.HtmlEncode(latest == null
                ? "Todavía no hay ejecuciones de recepción registradas."
                : "Última recepción: " + latest.Inicio.ToLocalTime().ToString("dd/MM/yyyy HH:mm") + " | " + latest.Estado + " | " + latest.Origen + " | Mensajes: " + latest.Mensajes + " | Errores: " + latest.Errores);

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
            litDatabaseWarning.Text = Server.HtmlEncode(message);
        }
    }
}
