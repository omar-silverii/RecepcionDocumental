using System;
using System.Data.SqlClient;
using System.Web.UI;
using System.Web.UI.WebControls;
using RecepcionDocumental.Data;
using RecepcionDocumental.Services;

namespace RecepcionDocumental
{
    public partial class Documentos : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;
            var requested = Request.QueryString["clasificacion"];
            if (requested == "FACTURA" || requested == "REVISAR" || requested == "DESCARTAR") ddlClasificacion.SelectedValue = requested;
            LoadDocuments();
        }
        protected void Filtro_Changed(object sender, EventArgs e) { LoadDocuments(); }
        protected void Documentos_ItemCommand(object source,RepeaterCommandEventArgs e)
        {
            long id;if(!long.TryParse(Convert.ToString(e.CommandArgument),out id))return;var box=(TextBox)e.Item.FindControl("txtObservacion");var identity=Context.User!=null&&Context.User.Identity!=null&&Context.User.Identity.IsAuthenticated?Context.User.Identity.Name:null;
            try{var observation=box==null?null:box.Text;var result=e.CommandName=="FACTURA"?DocumentReviewService.ConfirmInvoice(id,identity,observation):e.CommandName=="OTRO_DOCUMENTO"?DocumentReviewService.DiscardOtherDocument(id,identity,observation):DocumentReviewService.DiscardNonDocument(id,identity,observation);litResultado.Text=Server.HtmlEncode(result.Message);pnlResultado.Visible=true;}catch(Exception ex)when(ex is System.IO.IOException||ex is UnauthorizedAccessException||ex is SqlException){litResultado.Text="No se pudo resolver el documento de forma segura.";pnlResultado.Visible=true;}LoadDocuments();
        }
        private void LoadDocuments()
        {
            try { var items=DocumentRepository.List(ddlClasificacion.SelectedValue); pnlError.Visible=false; pnlVacio.Visible=items.Count==0; pnlTabla.Visible=items.Count>0; rptDocumentos.DataSource=items; rptDocumentos.DataBind(); }
            catch(SqlException){pnlError.Visible=true;pnlVacio.Visible=false;pnlTabla.Visible=false;}
        }
    }
}
