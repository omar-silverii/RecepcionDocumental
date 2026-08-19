using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.UI;
using Google;
using RecepcionDocumental.Data;
using RecepcionDocumental.Services;

namespace RecepcionDocumental
{
    public partial class Gmail_Bandeja : Page
    {
        private const string SyncSessionKey = "GmailSync.Running";

        protected void Page_Load(object sender, EventArgs e)
        {
            Server.ScriptTimeout = 600;
            if (!IsPostBack) LoadPageData();
        }

        protected void Buscar_Click(object sender, EventArgs e)
        {
            if (Session[SyncSessionKey] != null) { ShowError("Ya hay una búsqueda en curso para esta sesión."); return; }
            Session[SyncSessionKey] = true;
            RegisterAsyncTask(new PageAsyncTask(RunSyncAsync));
        }

        private async Task RunSyncAsync()
        {
            try
            {
                var result = await GmailSyncService.SynchronizeAsync();
                pnlResultado.Visible = true;
                pnlResultado.CssClass = result.Errores == 0 ? "alert alert-success" : "alert alert-warning";
                litEncontrados.Text = result.MensajesEncontrados.ToString(); litNuevos.Text = result.MensajesNuevos.ToString(); litDescargados.Text = result.AdjuntosDescargados.ToString(); litExistentes.Text = result.AdjuntosExistentes.ToString(); litErrores.Text = result.Errores.ToString();
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
            try
            {
                var account = GmailSyncRepository.GetActiveAccount();
                pnlSinCuenta.Visible = account == null;
                btnBuscar.Visible = account != null;
                IList<GmailMensajeInfo> messages;
                if (!GmailRepository.TryGetMensajes(out messages)) { ShowError("No se pudo consultar la bandeja. Verificá la base de datos."); return; }
                pnlSinMensajes.Visible = messages.Count == 0;
                pnlTabla.Visible = messages.Count > 0;
                rptMensajes.DataSource = messages; rptMensajes.DataBind();
            }
            catch (System.Data.SqlClient.SqlException) { ShowError("No se pudo consultar la estructura H1C. Ejecutá Database/003_GmailSync.sql."); }
        }

        private void ShowError(string message) { pnlDatabaseWarning.Visible = true; litDatabaseWarning.Text = Server.HtmlEncode(message); }
    }
}
