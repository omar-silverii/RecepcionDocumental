using System;
using System.Data.SqlClient;
using System.Web.UI;
using Google;
using RecepcionDocumental.Data;
using RecepcionDocumental.Services;

namespace RecepcionDocumental
{
    public partial class Gmail_Config : Page
    {
        private const string StateSessionKey = "GmailOAuth.State";

        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;

            if (string.Equals(Request.QueryString["connected"], "1", StringComparison.Ordinal))
                ShowSuccess("Cuenta Gmail conectada correctamente.");

            LoadAccountState();
        }

        private void LoadAccountState()
        {
            GmailCuentaInfo cuenta;
            if (!GmailRepository.TryGetCuenta(out cuenta))
            {
                pnlDatabaseWarning.Visible = true;
                return;
            }

            pnlDatabaseWarning.Visible = false;

            if (cuenta == null)
            {
                pnlSinCuenta.Visible = true;
                pnlCuenta.Visible = false;
                return;
            }

            pnlSinCuenta.Visible = false;
            pnlCuenta.Visible = true;
            litEmail.Text = Server.HtmlEncode(cuenta.Email);
            litEstado.Text = cuenta.Activo ? "Activa" : "Inactiva";
            litInicioRecepcion.Text = cuenta.TieneCursor ? "Inicializada" : "Pendiente";
            litUltimaConsulta.Text = cuenta.UltimaConsultaUtc.HasValue
                ? cuenta.UltimaConsultaUtc.Value.ToString("dd/MM/yyyy HH:mm") + " UTC"
                : "Sin consultas";

            pnlInicializar.Visible = cuenta.Activo && cuenta.TieneRefreshToken && !cuenta.TieneCursor;
        }

        protected void ConectarGmail_Click(object sender, EventArgs e)
        {
            GoogleOAuthSettings settings;
            string error;
            if (!GoogleOAuthSettings.TryLoad(out settings, out error))
            {
                ShowError(error);
                return;
            }

            var state = GmailOAuthService.GenerateState();
            Session[StateSessionKey] = state;
            Response.Redirect(GmailOAuthService.CreateAuthorizationUrl(settings, state), false);
            Context.ApplicationInstance.CompleteRequest();
        }

        protected async void InicializarDesdeAhora_Click(object sender, EventArgs e)
        {
            pnlOAuthError.Visible = false;

            try
            {
                var initialized = await GmailSyncService.InitializeFromNowAsync();

                if (initialized)
                    ShowSuccess("Recepción inicializada correctamente. A partir de ahora se procesarán únicamente los correos nuevos.");
                else
                    ShowSuccess("La recepción ya estaba inicializada. No se modificó el cursor existente.");

                LoadAccountState();
            }
            catch (GoogleApiException)
            {
                ShowError("No fue posible obtener el punto de inicio actual de Gmail. Intentá nuevamente.");
            }
            catch (SqlException)
            {
                ShowError("No fue posible guardar el punto de inicio de recepción en la base de datos.");
            }
            catch (InvalidOperationException ex)
            {
                ShowError(ex.Message);
            }
            catch (Exception)
            {
                ShowError("No fue posible inicializar la recepción de Gmail.");
            }
        }

        private void ShowSuccess(string message)
        {
            pnlSuccess.Visible = true;
            litSuccess.Text = Server.HtmlEncode(message);
        }

        private void ShowError(string message)
        {
            pnlOAuthError.Visible = true;
            litOAuthError.Text = Server.HtmlEncode(message);
        }
    }
}
