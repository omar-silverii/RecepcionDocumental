using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Web.UI;
using Google;
using RecepcionDocumental.Data;
using RecepcionDocumental.Services;

namespace RecepcionDocumental
{
    public sealed class GmailMensajeBandejaView
    {
        public long Id { get; set; }
        public DateTime FechaLocal { get; set; }
        public string FechaHoraTexto { get { return FechaLocal.ToString("dd/MM HH:mm"); } }
        public string Remitente { get; set; }
        public string Asunto { get; set; }
        public int CantidadAdjuntos { get; set; }
        public string Estado { get; set; }
        public string EstadoCss
        {
            get
            {
                if (string.Equals(Estado, "Con errores", StringComparison.OrdinalIgnoreCase)) return "bg-danger";
                if (string.Equals(Estado, "Procesado", StringComparison.OrdinalIgnoreCase)) return "bg-success";
                if (string.Equals(Estado, "Descargado", StringComparison.OrdinalIgnoreCase)) return "bg-primary";
                return "bg-secondary";
            }
        }
    }

    public sealed class GmailGrupoBandejaView
    {
        public string Titulo { get; set; }
        public string CollapseId { get; set; }
        public string CollapseCss { get; set; }
        public string ExpandedAria { get; set; }
        public IList<GmailMensajeBandejaView> Mensajes { get; set; }
        public int TotalMensajes { get; set; }
        public int TotalAdjuntos { get; set; }
        public int Procesados { get; set; }
        public int Errores { get; set; }
        public string ErroresCss { get { return Errores > 0 ? "text-danger fw-semibold" : "text-secondary"; } }
    }

    public partial class Gmail_Bandeja : Page
    {
        private const string SyncSessionKey = "GmailSync.Running";

        protected void Page_Load(object sender, EventArgs e)
        {
            Server.ScriptTimeout = 600;
            var customPeriodScript = "document.getElementById('" + ddlPeriodo.ClientID + "').value='custom';";
            txtDesde.Attributes["onchange"] = customPeriodScript;
            txtHasta.Attributes["onchange"] = customPeriodScript;

            if (!IsPostBack)
            {
                ddlPeriodo.SelectedValue = "30";
                ApplyPresetDates("30");
                LoadPageData();
            }
        }

        protected void Buscar_Click(object sender, EventArgs e)
        {
            if (Session[SyncSessionKey] != null) { ShowError("Ya hay una búsqueda en curso para esta sesión."); return; }
            Session[SyncSessionKey] = true;
            RegisterAsyncTask(new PageAsyncTask(RunSyncAsync));
        }

        protected void AplicarFiltros_Click(object sender, EventArgs e)
        {
            pnlResultado.Visible = false;
            LoadPageData();
        }

        protected void LimpiarFiltros_Click(object sender, EventArgs e)
        {
            ddlPeriodo.SelectedValue = "30";
            ddlAgrupar.SelectedValue = "date";
            txtRemitente.Text = string.Empty;
            ddlEstado.SelectedValue = string.Empty;
            txtTexto.Text = string.Empty;
            ApplyPresetDates("30");
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
                litEncontrados.Text = result.MensajesEncontrados.ToString(); litNuevos.Text = result.MensajesNuevos.ToString(); litAnalizados.Text = result.AdjuntosAnalizados.ToString(); litFacturas.Text = result.FacturasDetectadas.ToString(); litRevisar.Text = result.ParaRevisar.ToString(); litDescartados.Text = result.Descartados.ToString(); litDocumentosExistentes.Text = result.DocumentosExistentes.ToString(); litErrores.Text = result.Errores.ToString();
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

                DateTime desdeLocal;
                DateTime hastaLocal;
                if (!TryResolvePeriod(out desdeLocal, out hastaLocal)) return;

                var desdeUtc = DateTime.SpecifyKind(desdeLocal.Date, DateTimeKind.Local).ToUniversalTime();
                var hastaUtcExclusivo = DateTime.SpecifyKind(hastaLocal.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();

                IList<GmailMensajeInfo> messages;
                if (!GmailRepository.TryGetMensajes(
                    desdeUtc,
                    hastaUtcExclusivo,
                    txtRemitente.Text,
                    ddlEstado.SelectedValue,
                    txtTexto.Text,
                    out messages))
                {
                    ShowError("No se pudo consultar la bandeja. Verificá la base de datos.");
                    return;
                }

                var views = messages.Select(ToView).ToList();
                var groups = BuildGroups(views, ddlAgrupar.SelectedValue);

                pnlSinMensajes.Visible = groups.Count == 0;
                pnlTabla.Visible = groups.Count > 0;
                rptDias.DataSource = groups;
                rptDias.DataBind();

                var totalAdjuntos = views.Sum(x => x.CantidadAdjuntos);
                var totalErrores = views.Count(x => string.Equals(x.Estado, "Con errores", StringComparison.OrdinalIgnoreCase));
                litRangeSummary.Text = Server.HtmlEncode(
                    "Período: " + desdeLocal.ToString("dd/MM/yyyy") + " al " + hastaLocal.ToString("dd/MM/yyyy")
                    + " | Mensajes: " + views.Count
                    + " | Adjuntos: " + totalAdjuntos
                    + " | Errores: " + totalErrores
                    + " | Agrupado por: " + (ddlAgrupar.SelectedValue == "sender" ? "remitente" : "fecha"));
            }
            catch (System.Data.SqlClient.SqlException) { ShowError("No se pudo consultar la estructura H1C. Ejecutá Database/003_GmailSync.sql."); }
        }

        private bool TryResolvePeriod(out DateTime desdeLocal, out DateTime hastaLocal)
        {
            var today = DateTime.Now.Date;
            var preset = ddlPeriodo.SelectedValue;

            if (!string.Equals(preset, "custom", StringComparison.Ordinal))
            {
                if (!ApplyPresetDates(preset))
                {
                    ddlPeriodo.SelectedValue = "30";
                    ApplyPresetDates("30");
                }
            }

            if (!DateTime.TryParseExact(txtDesde.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out desdeLocal)
                || !DateTime.TryParseExact(txtHasta.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out hastaLocal))
            {
                desdeLocal = today.AddDays(-29);
                hastaLocal = today;
                ShowError("Ingresá un período válido.");
                return false;
            }

            if (desdeLocal.Date > hastaLocal.Date)
            {
                ShowError("La fecha desde no puede ser posterior a la fecha hasta.");
                return false;
            }

            if ((hastaLocal.Date - desdeLocal.Date).TotalDays > 366)
            {
                ShowError("El período máximo de consulta es de 366 días.");
                return false;
            }

            desdeLocal = desdeLocal.Date;
            hastaLocal = hastaLocal.Date;
            return true;
        }

        private bool ApplyPresetDates(string preset)
        {
            var today = DateTime.Now.Date;
            DateTime desde;

            switch (preset)
            {
                case "1":
                    desde = today;
                    break;
                case "7":
                    desde = today.AddDays(-6);
                    break;
                case "30":
                    desde = today.AddDays(-29);
                    break;
                case "custom":
                    return true;
                default:
                    return false;
            }

            txtDesde.Text = desde.ToString("yyyy-MM-dd");
            txtHasta.Text = today.ToString("yyyy-MM-dd");
            return true;
        }

        private static GmailMensajeBandejaView ToView(GmailMensajeInfo item)
        {
            return new GmailMensajeBandejaView
            {
                Id = item.Id,
                FechaLocal = DateTime.SpecifyKind(item.FechaMensajeUtc, DateTimeKind.Utc).ToLocalTime(),
                Remitente = item.Remitente,
                Asunto = item.Asunto,
                CantidadAdjuntos = item.CantidadAdjuntos,
                Estado = item.Estado
            };
        }

        private static IList<GmailGrupoBandejaView> BuildGroups(IList<GmailMensajeBandejaView> messages, string grouping)
        {
            var result = new List<GmailGrupoBandejaView>();

            if (string.Equals(grouping, "sender", StringComparison.Ordinal))
            {
                var senderGroups = messages
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Remitente) ? "(Sin remitente)" : x.Remitente)
                    .OrderByDescending(x => x.Max(m => m.FechaLocal))
                    .ThenBy(x => x.Key)
                    .ToList();

                for (var i = 0; i < senderGroups.Count; i++)
                {
                    var items = senderGroups[i].OrderByDescending(x => x.FechaLocal).ToList();
                    result.Add(CreateGroup(senderGroups[i].Key, "gmail-sender-" + i, items, i == 0));
                }

                return result;
            }

            var dateGroups = messages
                .GroupBy(x => x.FechaLocal.Date)
                .OrderByDescending(x => x.Key)
                .ToList();

            for (var i = 0; i < dateGroups.Count; i++)
            {
                var items = dateGroups[i].OrderByDescending(x => x.FechaLocal).ToList();
                result.Add(CreateGroup(FormatDayTitle(dateGroups[i].Key), "gmail-day-" + dateGroups[i].Key.ToString("yyyyMMdd"), items, i == 0));
            }

            return result;
        }

        private static GmailGrupoBandejaView CreateGroup(string title, string collapseId, IList<GmailMensajeBandejaView> items, bool expanded)
        {
            return new GmailGrupoBandejaView
            {
                Titulo = title,
                CollapseId = collapseId,
                CollapseCss = expanded ? "show" : string.Empty,
                ExpandedAria = expanded ? "true" : "false",
                Mensajes = items,
                TotalMensajes = items.Count,
                TotalAdjuntos = items.Sum(x => x.CantidadAdjuntos),
                Procesados = items.Count(x => string.Equals(x.Estado, "Procesado", StringComparison.OrdinalIgnoreCase)),
                Errores = items.Count(x => string.Equals(x.Estado, "Con errores", StringComparison.OrdinalIgnoreCase))
            };
        }

        private static string FormatDayTitle(DateTime date)
        {
            var today = DateTime.Now.Date;
            if (date == today) return "Hoy · " + date.ToString("dd/MM/yyyy");
            if (date == today.AddDays(-1)) return "Ayer · " + date.ToString("dd/MM/yyyy");
            return date.ToString("dddd dd/MM/yyyy", CultureInfo.GetCultureInfo("es-AR"));
        }

        private void ShowError(string message)
        {
            pnlDatabaseWarning.Visible = true;
            litDatabaseWarning.Text = Server.HtmlEncode(message);
        }
    }
}
