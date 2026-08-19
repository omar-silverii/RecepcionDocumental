using System;
using System.Data.SqlClient;
using System.Web.UI;
using RecepcionDocumental.Data;

namespace RecepcionDocumental
{
    public partial class Documentos : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;
            var requested = Request.QueryString["clasificacion"];
            if (requested == "FACTURA" || requested == "REVISAR") ddlClasificacion.SelectedValue = requested;
            LoadDocuments();
        }
        protected void Filtro_Changed(object sender, EventArgs e) { LoadDocuments(); }
        private void LoadDocuments()
        {
            try { var items=DocumentRepository.List(ddlClasificacion.SelectedValue); pnlError.Visible=false; pnlVacio.Visible=items.Count==0; pnlTabla.Visible=items.Count>0; rptDocumentos.DataSource=items; rptDocumentos.DataBind(); }
            catch(SqlException){pnlError.Visible=true;pnlVacio.Visible=false;pnlTabla.Visible=false;}
        }
    }
}
