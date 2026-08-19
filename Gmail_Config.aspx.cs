using System;
using System.Web.UI;
using RecepcionDocumental.Data;
namespace RecepcionDocumental { public partial class Gmail_Config : Page { protected void Page_Load(object sender, EventArgs e) { if (IsPostBack) return; GmailCuentaInfo cuenta; if (!GmailRepository.TryGetCuenta(out cuenta)) { pnlDatabaseWarning.Visible = true; return; } if (cuenta == null) return; pnlSinCuenta.Visible = false; pnlCuenta.Visible = true; litEmail.Text = Server.HtmlEncode(cuenta.Email); litEstado.Text = cuenta.Activo ? "Activa" : "Inactiva"; litUltimaConsulta.Text = cuenta.UltimaConsultaUtc.HasValue ? cuenta.UltimaConsultaUtc.Value.ToString("dd/MM/yyyy HH:mm") : "Sin consultas"; } } }
