using System;
using System.Data.SqlClient;
using System.IO;
using System.Web.UI;
using System.Web.UI.WebControls;
using RecepcionDocumental.Data;
using RecepcionDocumental.Services;

namespace RecepcionDocumental
{
    public partial class Documento_Revisar : Page
    {
        protected string PreviewUrl { get; private set; }

        protected void Page_Load(object sender,EventArgs e)
        {
            if(IsPostBack)return;
            long id;
            if(!long.TryParse(Request.QueryString["id"],out id)||id<=0)
            {
                var first=DocumentRepository.GetFirstPending();
                if(first.HasValue){Response.Redirect("Documento_Revisar.aspx?id="+first.Value,false);Context.ApplicationInstance.CompleteRequest();return;}
                ShowEmpty();return;
            }
            LoadPending(id);
        }

        protected void Resolve_Click(object sender,EventArgs e)
        {
            long id;if(!long.TryParse(hfDocumentId.Value,out id))return;
            var next=DocumentRepository.GetNextPending(id);var identity=Context.User!=null&&Context.User.Identity!=null&&Context.User.Identity.IsAuthenticated?Context.User.Identity.Name:null;
            try
            {
                var command=((Button)sender).CommandName;
                var result=command=="FACTURA"?DocumentReviewService.ConfirmInvoice(id,identity,txtObservation.Text):command=="OTRO_DOCUMENTO"?DocumentReviewService.DiscardOtherDocument(id,identity,txtObservation.Text):DocumentReviewService.DiscardNonDocument(id,identity,txtObservation.Text);
                if(result.Success){Response.Redirect(next.HasValue?"Documento_Revisar.aspx?id="+next.Value:"Documento_Revisar.aspx?done=1",false);Context.ApplicationInstance.CompleteRequest();return;}
                ShowResolved(id,"Este documento ya fue resuelto.");
            }
            catch(Exception ex)when(ex is IOException||ex is UnauthorizedAccessException||ex is SqlException){ShowResolved(id,"No se pudo resolver el documento de forma segura. Podés volver a intentarlo o continuar con otro pendiente.");}
        }

        private void LoadPending(long id)
        {
            var item=DocumentRepository.GetPendingForReview(id);if(item==null){ShowResolved(id,"Este documento ya fue resuelto.");return;}
            pnlWork.Visible=true;pnlEmpty.Visible=false;pnlMessage.Visible=false;hfDocumentId.Value=id.ToString();
            litPosition.Text="<p class=\"text-secondary mb-0\">Pendiente "+item.Position+" de "+item.Total+"</p>";
            litName.Text=Server.HtmlEncode(item.NombreOriginal);litDate.Text=item.Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm");litSender.Text=Server.HtmlEncode(item.Remitente);litSubject.Text=Server.HtmlEncode(item.Asunto);litClassification.Text=Server.HtmlEncode(item.Clasificacion);litMethod.Text=Server.HtmlEncode(item.MetodoDeteccion);litConfidence.Text=item.Confianza.HasValue?item.Confianza.Value+"%":"No informada";litReason.Text=Server.HtmlEncode(item.Motivo??"Sin motivo informado");
            PreviewUrl="Documento_Ver.aspx?id="+id;lnkOpenDocument.NavigateUrl=PreviewUrl;var ext=Path.GetExtension(item.NombreOriginal).ToLowerInvariant();var inline=ext==".pdf"||ext==".png"||ext==".jpg"||ext==".jpeg"||ext==".gif"||ext==".bmp"||ext==".tif"||ext==".tiff";pnlInline.Visible=inline;pnlDownload.Visible=!inline;
            SetNavigation(lnkPrevious,DocumentRepository.GetPreviousPending(id));SetNavigation(lnkNext,DocumentRepository.GetNextPending(id));
        }
        private void ShowResolved(long id,string message){pnlWork.Visible=false;pnlEmpty.Visible=false;pnlMessage.Visible=true;litMessage.Text=Server.HtmlEncode(message);var next=DocumentRepository.GetNextPending(id);lnkContinue.Visible=next.HasValue;if(next.HasValue)lnkContinue.NavigateUrl="Documento_Revisar.aspx?id="+next.Value;}
        private void ShowEmpty(){pnlWork.Visible=false;pnlMessage.Visible=false;pnlEmpty.Visible=true;}
        private static void SetNavigation(HyperLink link,long? id){link.Enabled=id.HasValue;link.NavigateUrl=id.HasValue?"Documento_Revisar.aspx?id="+id.Value:string.Empty;link.CssClass="btn btn-sm btn-outline-secondary"+(id.HasValue?string.Empty:" disabled");}
    }
}
