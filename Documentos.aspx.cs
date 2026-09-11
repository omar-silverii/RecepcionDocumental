using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Web;
using System.Web.UI;
using RecepcionDocumental.Controls;
using RecepcionDocumental.Data;

namespace RecepcionDocumental
{
    public partial class Documentos : Page
    {
        protected override void OnInit(EventArgs e)
        {
            base.OnInit(e);
            lstDocumentos.FiltersChanged += Listado_FiltersChanged;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            ConfigureList();
            if (IsPostBack) return;

            var requested = Request.QueryString["clasificacion"];
            if (requested == "FACTURA" || requested == "REVISAR" || requested == "DESCARTAR") ddlClasificacion.SelectedValue = requested;
            lstDocumentos.EnsureDefaultFilters();
            LoadDocuments();
        }

        protected void Filtro_Changed(object sender, EventArgs e)
        {
            ConfigureList();
            LoadDocuments();
        }

        private void Listado_FiltersChanged(object sender, EventArgs e)
        {
            LoadDocuments();
        }

        private void ConfigureList()
        {
            lstDocumentos.ShowStatusFilter = false;
            lstDocumentos.TextFilterLabel = "Asunto / documento";
            lstDocumentos.TextFilterPlaceholder = "Buscar asunto o nombre";
            lstDocumentos.TableCssClass = "documents-table table-striped table-sm";
            lstDocumentos.EmptyTitle = "No hay documentos para los filtros seleccionados.";
            lstDocumentos.EmptyText = "Probá otro período o restablecé los filtros.";

            var classification = ddlClasificacion.SelectedValue;
            lstDocumentos.MetricALabel = string.Empty;
            lstDocumentos.MetricBLabel = string.Empty;
            lstDocumentos.MetricCLabel = string.Empty;
            lstDocumentos.MetricACssClass = string.Empty;
            lstDocumentos.MetricBCssClass = string.Empty;
            lstDocumentos.MetricCCssClass = string.Empty;

            switch (classification)
            {
                case "FACTURA":
                    lstDocumentos.PrimaryCountLabel = "facturas";
                    lstDocumentos.MetricALabel = "automáticas";
                    lstDocumentos.MetricBLabel = "manuales";
                    break;
                case "REVISAR":
                    lstDocumentos.PrimaryCountLabel = "pendientes";
                    break;
                case "DESCARTAR":
                    lstDocumentos.PrimaryCountLabel = "descartados";
                    lstDocumentos.MetricALabel = "automáticos";
                    lstDocumentos.MetricBLabel = "IA";
                    lstDocumentos.MetricCLabel = "revisados";
                    break;
                default:
                    lstDocumentos.PrimaryCountLabel = "documentos";
                    lstDocumentos.MetricALabel = "facturas";
                    lstDocumentos.MetricBLabel = "para revisar";
                    break;
            }
        }

        private void LoadDocuments()
        {
            lnkSample.Visible = false;
            pnlError.Visible = false;

            try
            {
                var count = InvoiceSampleRepository.CountPending();
                lnkSample.Visible = count > 0;
                lnkSample.Text = "Revisión de control (" + count + ")";
            }
            catch (SqlException)
            {
                // Sampling availability must not hide the operational list.
            }

            try
            {
                DateTime desdeUtc;
                DateTime hastaUtcExclusivo;
                if (!lstDocumentos.TryGetUtcRange(out desdeUtc, out hastaUtcExclusivo)) return;

                var classification = ddlClasificacion.SelectedValue;
                var items = DocumentRepository.List(
                    classification,
                    desdeUtc,
                    hastaUtcExclusivo,
                    lstDocumentos.SenderFilter,
                    lstDocumentos.TextFilter);

                var first = classification == "REVISAR" ? DocumentRepository.GetFirstPending() : null;
                pnlComenzarRevision.Visible = first.HasValue;
                if (first.HasValue) lnkComenzarRevision.NavigateUrl = "Documento_Revisar.aspx?id=" + first.Value;

                ConfigureList();
                lstDocumentos.BindData(BuildColumns(), items.Select(ToListRow).ToList());
            }
            catch (SqlException)
            {
                pnlError.Visible = true;
                pnlComenzarRevision.Visible = false;
            }
        }

        private static IList<WsListadoColumna> BuildColumns()
        {
            return new List<WsListadoColumna>
            {
                new WsListadoColumna { Titulo = "Fecha", CssClass = "col-date" },
                new WsListadoColumna { Titulo = "Remitente", CssClass = "col-sender" },
                new WsListadoColumna { Titulo = "Asunto", CssClass = "col-subject" },
                new WsListadoColumna { Titulo = "Nombre documento", CssClass = "col-name" },
                new WsListadoColumna { Titulo = "Clasificación automática", CssClass = "col-classification" },
                new WsListadoColumna { Titulo = "Resultado humano", CssClass = "col-human-result" },
                new WsListadoColumna { Titulo = "Método", CssClass = "col-method" },
                new WsListadoColumna { Titulo = "Confianza", CssClass = "col-confidence" },
                new WsListadoColumna { Titulo = "Motivo", CssClass = "col-reason" },
                new WsListadoColumna { Titulo = "Origen", CssClass = "col-origin" },
                new WsListadoColumna { Titulo = "Acciones", CssClass = "col-actions" }
            };
        }

        private WsListadoFila ToListRow(DocumentInfo item)
        {
            var fechaLocal = DateTime.SpecifyKind(item.Fecha, DateTimeKind.Utc).ToLocalTime();
            var classification = ddlClasificacion.SelectedValue;
            var metricA = 0;
            var metricB = 0;
            var metricC = 0;

            if (classification == "FACTURA")
            {
                if (item.ResultadoRevision == null) metricA = 1;
                else metricB = 1;
            }
            else if (classification == "DESCARTAR")
            {
                if (item.EsDescarteDeterministico) metricA = 1;
                else if (item.EsDescarteIa) metricB = 1;
                else metricC = 1;
            }
            else if (string.IsNullOrWhiteSpace(classification))
            {
                if (string.Equals(item.EstadoEfectivo, "FACTURA", StringComparison.Ordinal)) metricA = 1;
                else if (item.PendienteRevision) metricB = 1;
            }

            var classificationHtml = "<span class=\"badge rounded-pill text-bg-secondary\">" + HttpUtility.HtmlEncode(item.ClasificacionMostrada) + "</span>";
            var methodHtml = "<span class=\"badge bg-light text-dark border fw-normal\">" + HttpUtility.HtmlEncode(item.MetodoDeteccion) + "</span>";
            var actionsHtml = BuildActionsHtml(item);

            return new WsListadoFila
            {
                FechaLocal = fechaLocal,
                Remitente = item.Remitente,
                MetricA = metricA,
                MetricB = metricB,
                MetricC = metricC,
                Celdas = new List<WsListadoCelda>
                {
                    WsListadoCelda.Texto(fechaLocal.ToString("dd/MM/yyyy HH:mm"), "doc-date text-nowrap"),
                    WsListadoCelda.Texto(item.Remitente, "doc-sender text-truncate", item.Remitente),
                    WsListadoCelda.Texto(item.Asunto, "doc-subject text-truncate", item.Asunto),
                    WsListadoCelda.Texto(item.NombreOriginal, "doc-name text-truncate", item.NombreOriginal),
                    WsListadoCelda.Html(classificationHtml, "doc-classification"),
                    WsListadoCelda.Texto(item.ResultadoHumanoMostrado, "doc-human-result"),
                    WsListadoCelda.Html(methodHtml, "doc-method"),
                    WsListadoCelda.Texto(item.Confianza.HasValue ? item.Confianza.Value.ToString() : string.Empty, "doc-confidence"),
                    WsListadoCelda.Texto(item.Motivo, "doc-reason text-truncate", item.Motivo),
                    WsListadoCelda.Texto(item.OrigenTipo, "doc-origin"),
                    WsListadoCelda.Html(actionsHtml, "text-nowrap")
                }
            };
        }

        private static string BuildActionsHtml(DocumentInfo item)
        {
            if (!item.PuedeVer) return "<span class=\"text-secondary\">—</span>";

            var parts = new List<string>();
            if (item.PendienteRevision)
            {
                parts.Add("<a class=\"btn btn-sm btn-primary me-1\" href=\"Documento_Revisar.aspx?id=" + item.Id + "\">Revisar</a>");
            }
            parts.Add("<a class=\"btn btn-sm btn-outline-secondary\" target=\"_blank\" href=\"Documento_Ver.aspx?id=" + item.Id + "\">Ver</a>");
            return string.Join(string.Empty, parts);
        }
    }
}
