using System;
using System.Web;
using System.Web.Security;
using System.Web.UI;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Security;

namespace RecepcionDocumental
{
    public partial class Login : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack && Request.IsAuthenticated)
            {
                RedirectAfterLogin();
            }
        }

        protected void btnIngresar_Click(object sender, EventArgs e)
        {
            litError.Text = string.Empty;
            var clientIp = Request.UserHostAddress;

            int retryAfterSeconds;
            if (!LoginAttemptGuard.CanAttempt(clientIp, out retryAfterSeconds))
            {
                int minutes = Math.Max(1, (int)Math.Ceiling(retryAfterSeconds / 60.0));
                ShowError("Demasiados intentos fallidos. Intentá nuevamente en " + minutes + " minuto" + (minutes == 1 ? "." : "s."));
                return;
            }

            string authenticatedUser;
            string configurationError;
            if (SingleUserLoginService.TryValidate(txtUsuario.Text, txtPassword.Text, out authenticatedUser, out configurationError))
            {
                LoginAttemptGuard.ReportSuccess(clientIp);
                FormsAuthentication.SetAuthCookie(authenticatedUser, false);
                Session.Clear();

                if (Logs.EstaInicializado)
                {
                    Logs.LogProc("Login correcto | Usuario=" + Logs.SanitizarMensaje(authenticatedUser) + " | IP=" + Logs.SanitizarMensaje(clientIp));
                }

                RedirectAfterLogin();
                return;
            }

            LoginAttemptGuard.ReportFailure(clientIp);
            if (Logs.EstaInicializado)
            {
                Logs.LogProc("Login rechazado | IP=" + Logs.SanitizarMensaje(clientIp));
            }

            ShowError(string.IsNullOrWhiteSpace(configurationError) ? "Usuario o contraseña incorrectos." : configurationError);
        }

        private void RedirectAfterLogin()
        {
            var returnUrl = Request.QueryString["ReturnUrl"];
            var target = IsSafeLocalReturnUrl(returnUrl) ? returnUrl : ResolveUrl("~/");
            Response.Redirect(target, false);
            Context.ApplicationInstance.CompleteRequest();
        }

        private static bool IsSafeLocalReturnUrl(string returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl)) return false;
            if (!returnUrl.StartsWith("/", StringComparison.Ordinal)) return false;
            if (returnUrl.StartsWith("//", StringComparison.Ordinal) || returnUrl.StartsWith(@"/\", StringComparison.Ordinal)) return false;
            return !Uri.IsWellFormedUriString(returnUrl, UriKind.Absolute);
        }

        private void ShowError(string message)
        {
            litError.Text = "<div class=\"error\">" + Server.HtmlEncode(message) + "</div>";
        }
    }
}
