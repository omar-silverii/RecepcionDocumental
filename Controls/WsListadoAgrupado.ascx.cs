using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.UI;

namespace RecepcionDocumental.Controls
{
    public sealed class WsListadoColumna
    {
        public string Titulo { get; set; }
        public string CssClass { get; set; }
    }

    public sealed class WsListadoCelda
    {
        public string DisplayHtml { get; set; }
        public string CssClass { get; set; }
        public string Title { get; set; }

        public static WsListadoCelda Texto(string value, string cssClass = null, string title = null)
        {
            return new WsListadoCelda
            {
                DisplayHtml = HttpUtility.HtmlEncode(value ?? string.Empty),
                CssClass = cssClass ?? string.Empty,
                Title = title ?? value ?? string.Empty
            };
        }

        public static WsListadoCelda Html(string html, string cssClass = null, string title = null)
        {
            return new WsListadoCelda
            {
                DisplayHtml = html ?? string.Empty,
                CssClass = cssClass ?? string.Empty,
                Title = title ?? string.Empty
            };
        }
    }

    public sealed class WsListadoFila
    {
        public DateTime FechaLocal { get; set; }
        public string Remitente { get; set; }
        public IList<WsListadoCelda> Celdas { get; set; }
        public int MetricA { get; set; }
        public int MetricB { get; set; }
        public int MetricC { get; set; }
    }

    public sealed class WsListadoStat
    {
        public int Valor { get; set; }
        public string Etiqueta { get; set; }
        public string CssClass { get; set; }
    }

    public sealed class WsListadoGrupo
    {
        public string Titulo { get; set; }
        public string CollapseId { get; set; }
        public string CollapseCss { get; set; }
        public string ExpandedAria { get; set; }
        public string TableCssClass { get; set; }
        public IList<WsListadoColumna> Columnas { get; set; }
        public IList<WsListadoFila> Filas { get; set; }
        public IList<WsListadoStat> Stats { get; set; }
    }

    public partial class WsListadoAgrupado : UserControl
    {
        public event EventHandler FiltersChanged;

        public bool ShowStatusFilter
        {
            get { return pnlEstado.Visible; }
            set { pnlEstado.Visible = value; }
        }

        public string StatusLabel
        {
            get { return litEstadoLabel.Text; }
            set { litEstadoLabel.Text = value ?? "Estado"; }
        }

        public string TextFilterLabel
        {
            get { return litTextoLabel.Text; }
            set { litTextoLabel.Text = value ?? "Asunto"; }
        }

        public string TextFilterPlaceholder
        {
            get { return txtTexto.Attributes["placeholder"] ?? string.Empty; }
            set { txtTexto.Attributes["placeholder"] = value ?? string.Empty; }
        }

        public string PrimaryCountLabel { get; set; }
        public string MetricALabel { get; set; }
        public string MetricBLabel { get; set; }
        public string MetricCLabel { get; set; }
        public string MetricACssClass { get; set; }
        public string MetricBCssClass { get; set; }
        public string MetricCCssClass { get; set; }
        public string TableCssClass { get; set; }
        public string EmptyTitle { get; set; }
        public string EmptyText { get; set; }

        public string SenderFilter { get { return (txtRemitente.Text ?? string.Empty).Trim(); } }
        public string StateFilter { get { return ShowStatusFilter ? ddlEstado.SelectedValue : string.Empty; } }
        public string TextFilter { get { return (txtTexto.Text ?? string.Empty).Trim(); } }
        public string Grouping { get { return ddlAgrupar.SelectedValue; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            var customPeriodScript = "document.getElementById('" + ddlPeriodo.ClientID + "').value='custom';";
            txtDesde.Attributes["onchange"] = customPeriodScript;
            txtHasta.Attributes["onchange"] = customPeriodScript;
        }

        public void EnsureDefaultFilters()
        {
            if (string.IsNullOrWhiteSpace(ddlPeriodo.SelectedValue)) ddlPeriodo.SelectedValue = "30";
            ddlAgrupar.SelectedValue = "date";
            ApplyPresetDates("30");
            pnlFiltroError.Visible = false;
        }

        public void ResetFilters()
        {
            ddlPeriodo.SelectedValue = "30";
            ddlAgrupar.SelectedValue = "date";
            txtRemitente.Text = string.Empty;
            ddlEstado.SelectedValue = string.Empty;
            txtTexto.Text = string.Empty;
            ApplyPresetDates("30");
            pnlFiltroError.Visible = false;
        }

        public bool TryGetUtcRange(out DateTime desdeUtc, out DateTime hastaUtcExclusivo)
        {
            DateTime desdeLocal;
            DateTime hastaLocal;
            desdeUtc = default(DateTime);
            hastaUtcExclusivo = default(DateTime);
            pnlFiltroError.Visible = false;

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
                ShowFilterError("Ingresá un período válido.");
                return false;
            }

            if (desdeLocal.Date > hastaLocal.Date)
            {
                ShowFilterError("La fecha desde no puede ser posterior a la fecha hasta.");
                return false;
            }

            if ((hastaLocal.Date - desdeLocal.Date).TotalDays > 366)
            {
                ShowFilterError("El período máximo de consulta es de 366 días.");
                return false;
            }

            desdeUtc = DateTime.SpecifyKind(desdeLocal.Date, DateTimeKind.Local).ToUniversalTime();
            hastaUtcExclusivo = DateTime.SpecifyKind(hastaLocal.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
            return true;
        }

        public void BindData(IList<WsListadoColumna> columnas, IList<WsListadoFila> filas)
        {
            columnas = columnas ?? new List<WsListadoColumna>();
            filas = filas ?? new List<WsListadoFila>();

            var grupos = BuildGroups(columnas, filas);
            pnlVacio.Visible = grupos.Count == 0;
            pnlTabla.Visible = grupos.Count > 0;
            litEmptyTitle.Text = HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(EmptyTitle) ? "No hay elementos para los filtros seleccionados." : EmptyTitle);
            litEmptyText.Text = HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(EmptyText) ? "Probá otro período o restablecé los filtros." : EmptyText);
            rptGrupos.DataSource = grupos;
            rptGrupos.DataBind();

            DateTime desdeUtc;
            DateTime hastaUtc;
            if (TryGetUtcRange(out desdeUtc, out hastaUtc))
            {
                var desdeLocal = desdeUtc.ToLocalTime().Date;
                var hastaLocal = hastaUtc.ToLocalTime().Date.AddDays(-1);
                litRangeSummary.Text = HttpUtility.HtmlEncode(BuildRangeSummary(desdeLocal, hastaLocal, filas));
            }
        }

        protected void AplicarFiltros_Click(object sender, EventArgs e)
        {
            RaiseFiltersChanged();
        }

        protected void LimpiarFiltros_Click(object sender, EventArgs e)
        {
            ResetFilters();
            RaiseFiltersChanged();
        }

        private void RaiseFiltersChanged()
        {
            var handler = FiltersChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private bool ApplyPresetDates(string preset)
        {
            var today = DateTime.Now.Date;
            DateTime desde;

            switch (preset)
            {
                case "1": desde = today; break;
                case "7": desde = today.AddDays(-6); break;
                case "30": desde = today.AddDays(-29); break;
                case "custom": return true;
                default: return false;
            }

            txtDesde.Text = desde.ToString("yyyy-MM-dd");
            txtHasta.Text = today.ToString("yyyy-MM-dd");
            return true;
        }

        private IList<WsListadoGrupo> BuildGroups(IList<WsListadoColumna> columnas, IList<WsListadoFila> filas)
        {
            var result = new List<WsListadoGrupo>();
            IEnumerable<IGrouping<string, WsListadoFila>> groups;

            if (string.Equals(Grouping, "sender", StringComparison.Ordinal))
            {
                groups = filas
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Remitente) ? "(Sin remitente)" : x.Remitente)
                    .OrderByDescending(x => x.Max(m => m.FechaLocal))
                    .ThenBy(x => x.Key);
            }
            else
            {
                groups = filas
                    .GroupBy(x => x.FechaLocal.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
                    .OrderByDescending(x => x.Key);
            }

            var index = 0;
            foreach (var group in groups)
            {
                var items = group.OrderByDescending(x => x.FechaLocal).ToList();
                var title = string.Equals(Grouping, "sender", StringComparison.Ordinal)
                    ? group.Key
                    : FormatDayTitle(items[0].FechaLocal.Date);
                var collapseId = MakeCollapseId(index);

                result.Add(new WsListadoGrupo
                {
                    Titulo = title,
                    CollapseId = collapseId,
                    CollapseCss = index == 0 ? "show" : string.Empty,
                    ExpandedAria = index == 0 ? "true" : "false",
                    TableCssClass = TableCssClass ?? string.Empty,
                    Columnas = columnas,
                    Filas = items,
                    Stats = BuildStats(items)
                });
                index++;
            }

            return result;
        }

        private IList<WsListadoStat> BuildStats(IList<WsListadoFila> items)
        {
            var stats = new List<WsListadoStat>
            {
                new WsListadoStat { Valor = items.Count, Etiqueta = string.IsNullOrWhiteSpace(PrimaryCountLabel) ? "elementos" : PrimaryCountLabel, CssClass = string.Empty }
            };
            AddMetric(stats, items.Sum(x => x.MetricA), MetricALabel, MetricACssClass);
            AddMetric(stats, items.Sum(x => x.MetricB), MetricBLabel, MetricBCssClass);
            AddMetric(stats, items.Sum(x => x.MetricC), MetricCLabel, MetricCCssClass);
            return stats;
        }

        private static void AddMetric(ICollection<WsListadoStat> stats, int value, string label, string cssClass)
        {
            if (string.IsNullOrWhiteSpace(label)) return;
            var effectiveCss = string.IsNullOrWhiteSpace(cssClass) ? string.Empty : (value > 0 ? cssClass : "text-secondary");
            stats.Add(new WsListadoStat { Valor = value, Etiqueta = label, CssClass = effectiveCss });
        }

        private string BuildRangeSummary(DateTime desdeLocal, DateTime hastaLocal, IList<WsListadoFila> filas)
        {
            var parts = new List<string>
            {
                "Período: " + desdeLocal.ToString("dd/MM/yyyy") + " al " + hastaLocal.ToString("dd/MM/yyyy"),
                (string.IsNullOrWhiteSpace(PrimaryCountLabel) ? "Elementos" : Capitalize(PrimaryCountLabel)) + ": " + filas.Count
            };
            if (!string.IsNullOrWhiteSpace(MetricALabel)) parts.Add(Capitalize(MetricALabel) + ": " + filas.Sum(x => x.MetricA));
            if (!string.IsNullOrWhiteSpace(MetricBLabel)) parts.Add(Capitalize(MetricBLabel) + ": " + filas.Sum(x => x.MetricB));
            if (!string.IsNullOrWhiteSpace(MetricCLabel)) parts.Add(Capitalize(MetricCLabel) + ": " + filas.Sum(x => x.MetricC));
            parts.Add("Agrupado por: " + (Grouping == "sender" ? "remitente" : "fecha"));
            return string.Join(" | ", parts);
        }

        private static string Capitalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            return char.ToUpper(value[0], CultureInfo.GetCultureInfo("es-AR")) + value.Substring(1);
        }

        private string MakeCollapseId(int index)
        {
            var baseId = (ClientID ?? "ws-list").Replace('_', '-');
            return baseId + "-group-" + index.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatDayTitle(DateTime date)
        {
            var today = DateTime.Now.Date;
            if (date == today) return "Hoy · " + date.ToString("dd/MM/yyyy");
            if (date == today.AddDays(-1)) return "Ayer · " + date.ToString("dd/MM/yyyy");
            return date.ToString("dddd dd/MM/yyyy", CultureInfo.GetCultureInfo("es-AR"));
        }

        private void ShowFilterError(string message)
        {
            pnlFiltroError.Visible = true;
            litFiltroError.Text = HttpUtility.HtmlEncode(message);
        }
    }
}
