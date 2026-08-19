using System;
using System.Web.UI;
using RecepcionDocumental.Data;

namespace RecepcionDocumental
{
    public partial class _Default : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;
            DashboardResumen resumen;
            if (GmailRepository.TryGetDashboardResumen(out resumen))
            {
                litCuentaGmail.Text = resumen.CuentasActivas.ToString();
                litMensajes.Text = resumen.Mensajes.ToString();
                try { int facturas, revisar; DocumentRepository.GetCounts(out facturas, out revisar); litFacturas.Text=facturas.ToString(); litRevisar.Text=revisar.ToString(); } catch (System.Data.SqlClient.SqlException) { }
            }
            else pnlDatabaseWarning.Visible = true;
        }
    }
}
