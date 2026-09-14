using System;
using System.Web.Security;
using System.Web.UI;

namespace RecepcionDocumental
{
    public partial class Logout : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            FormsAuthentication.SignOut();
            Session.Clear();
            Session.Abandon();
            Response.Redirect(ResolveUrl("~/Login.aspx"), false);
            Context.ApplicationInstance.CompleteRequest();
        }
    }
}
