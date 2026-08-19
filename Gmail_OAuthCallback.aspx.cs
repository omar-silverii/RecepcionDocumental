using System;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Web.UI;
using Google;
using Google.Apis.Auth.OAuth2.Responses;
using RecepcionDocumental.Data;
using RecepcionDocumental.Security;
using RecepcionDocumental.Services;

namespace RecepcionDocumental
{
    public partial class Gmail_OAuthCallback : Page
    {
        private const string StateSessionKey = "GmailOAuth.State";

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) RegisterAsyncTask(new PageAsyncTask(ProcessCallbackAsync));
        }

        private async Task ProcessCallbackAsync()
        {
            var expectedState = Session[StateSessionKey] as string;
            Session.Remove(StateSessionKey);

            var oauthError = Request.QueryString["error"];
            if (!string.IsNullOrEmpty(oauthError))
            {
                ShowError(oauthError == "access_denied" ? "La autorización de Gmail fue cancelada." : "Google no pudo completar la autorización de Gmail.");
                return;
            }

            var state = Request.QueryString["state"];
            var code = Request.QueryString["code"];
            if (string.IsNullOrEmpty(expectedState) || !string.Equals(expectedState, state, StringComparison.Ordinal))
            {
                ShowError("La respuesta de autorización no superó la validación de seguridad. Iniciá la conexión nuevamente.");
                return;
            }
            if (string.IsNullOrWhiteSpace(code))
            {
                ShowError("La respuesta de Google está incompleta. Iniciá la conexión nuevamente.");
                return;
            }

            GoogleOAuthSettings settings;
            string configurationError;
            if (!GoogleOAuthSettings.TryLoad(out settings, out configurationError)) { ShowError(configurationError); return; }

            try
            {
                var result = await GmailOAuthService.CompleteAuthorizationAsync(settings, code);
                GmailCuentaInfo existing;
                if (!GmailRepository.TryGetCuentaPorEmail(result.Email, out existing)) { ShowError("No se pudo consultar la cuenta en la base de datos."); return; }

                byte[] protectedToken = null;
                if (!string.IsNullOrWhiteSpace(result.RefreshToken)) protectedToken = RefreshTokenProtector.Protect(result.RefreshToken);
                else if (existing == null || !existing.TieneRefreshToken) { ShowError("Google no entregó un permiso offline. Revocá el acceso previo en Google y volvé a conectar la cuenta."); return; }

                if (!GmailRepository.GuardarCuentaAutorizada(result.Email, protectedToken)) { ShowError("No se pudo guardar la cuenta autorizada."); return; }
                Response.Redirect("Gmail_Config.aspx?connected=1", false);
                Context.ApplicationInstance.CompleteRequest();
            }
            catch (TokenResponseException) { ShowError("Google rechazó el intercambio de autorización. Iniciá la conexión nuevamente."); }
            catch (GoogleApiException) { ShowError("No fue posible consultar el perfil de la cuenta Gmail autorizada."); }
            catch (SqlException) { ShowError("No fue posible guardar la cuenta en la base de datos."); }
            catch (Exception) { ShowError("No fue posible completar la conexión con Gmail."); }
        }

        private void ShowError(string message)
        {
            pnlResultado.CssClass = "alert alert-danger";
            litResultado.Text = Server.HtmlEncode(message);
        }
    }
}
