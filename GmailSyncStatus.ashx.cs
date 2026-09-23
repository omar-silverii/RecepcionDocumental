using System;
using System.Globalization;
using System.Web;
using System.Web.Script.Serialization;
using RecepcionDocumental.Data;

namespace RecepcionDocumental
{
    public sealed class GmailSyncStatus : IHttpHandler
    {
        public void ProcessRequest(HttpContext context)
        {
            long baseline;
            if (!long.TryParse(context.Request.QueryString["baseline"], NumberStyles.Integer, CultureInfo.InvariantCulture, out baseline) || baseline < 0)
                baseline = 0;

            var latest = GmailSyncAuditRepository.LatestAfter(baseline, "WEB");
            var payload = Gmail_Bandeja.BuildSyncStatus(latest);

            context.Response.Clear();
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();
            context.Response.Write(new JavaScriptSerializer().Serialize(payload));
        }

        public bool IsReusable { get { return false; } }
    }
}
