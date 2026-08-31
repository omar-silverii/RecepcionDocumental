using System;
using System.IO;
using System.Web;
using System.Web.UI;
using RecepcionDocumental.Services;
namespace RecepcionDocumental { public partial class Documento_Ver:Page { protected void Page_Load(object sender,EventArgs e){long id;if(!long.TryParse(Request.QueryString["id"],out id)||id<=0){Response.StatusCode=400;return;}try{var d=DocumentReviewService.GetSafeDocument(id);if(d==null){Response.StatusCode=404;return;}var ext=Path.GetExtension(d.NombreOriginal).ToLowerInvariant();var inline=ext==".pdf"||ext==".png"||ext==".jpg"||ext==".jpeg"||ext==".gif"||ext==".bmp"||ext==".tif"||ext==".tiff";Response.Clear();Response.ContentType=MimeMapping.GetMimeMapping(d.NombreOriginal);Response.AddHeader("Content-Disposition",(inline?"inline":"attachment")+"; filename*=UTF-8''"+Uri.EscapeDataString(Path.GetFileName(d.NombreOriginal)));Response.TransmitFile(d.RutaLocal);HttpContext.Current.ApplicationInstance.CompleteRequest();}catch(UnauthorizedAccessException){Response.StatusCode=403;}catch(FileNotFoundException){Response.StatusCode=404;}} } }
