using System;
using System.Web.UI;
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
            pnlSuccess.Visible = string.Equals(Request.QueryString["connected"], "1", StringComparison.Ordinal);
            GmailCuentaInfo cuenta;
            if (!GmailRepository.TryGetCuenta(out cuenta)) { pnlDatabaseWarning.Visible = true; return; }
            if (cuenta == null) return;
            pnlSinCuenta.Visible = false;
            pnlCuenta.Visible = true;
            litEmail.Text = Server.HtmlEncode(cuenta.Email);
            litEstado.Text = cuenta.Activo ? "Activa" : "Inactiva";
            litUltimaConsulta.Text = cuenta.UltimaConsultaUtc.HasValue ? cuenta.UltimaConsultaUtc.Value.ToString("dd/MM/yyyy HH:mm") + " UTC" : "Sin consultas";
        }

        protected void ConectarGmail_Click(object sender, EventArgs e)
        {
            GoogleOAuthSettings settings;
            string error;
            if (!GoogleOAuthSettings.TryLoad(out settings, out error))
            {
                pnlOAuthError.Visible = true;
                litOAuthError.Text = Server.HtmlEncode(error);
                return;
            }

            var state = GmailOAuthService.GenerateState();
            Session[StateSessionKey] = state;
            Response.Redirect(GmailOAuthService.CreateAuthorizationUrl(settings, state), false);
            Context.ApplicationInstance.CompleteRequest();
        }
    }
}
