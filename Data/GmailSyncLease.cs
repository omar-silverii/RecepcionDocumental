using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace RecepcionDocumental.Data
{
    public sealed class GmailSyncLease : IDisposable
    {
        // Global lock intentionally precedes account selection and cursor reads.
        public const string Resource="RecepcionDocumental:GmailSync";
        private SqlConnection connection;
        private GmailSyncLease(SqlConnection cn){connection=cn;}
        public static GmailSyncLease TryAcquire()
        {
            var builder=new SqlConnectionStringBuilder(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString);
            builder.Pooling=false;builder.ConnectRetryCount=0;
            var cn=new SqlConnection(builder.ConnectionString);
            try
            {
                cn.Open();using(var cmd=new SqlCommand("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @r;",cn))
                {
                    cmd.Parameters.Add("@Resource",SqlDbType.NVarChar,255).Value=Resource;
                    var result=Convert.ToInt32(cmd.ExecuteScalar());
                    if(result>=0)return new GmailSyncLease(cn);
                    if(result == -1){cn.Dispose();return null;}
                    throw new InvalidOperationException("No se pudo obtener la exclusión SQL de Gmail. Código="+result);
                }
            }
            catch{cn.Dispose();throw;}
        }
        public void AssertHeld()
        {
            if(connection==null||connection.State!=ConnectionState.Open)throw new InvalidOperationException("Se perdió la sesión de exclusión Gmail.");
            using(var cmd=new SqlCommand("SELECT APPLOCK_MODE('public',@Resource,'Session');",connection))
            {cmd.Parameters.Add("@Resource",SqlDbType.NVarChar,255).Value=Resource;if(Convert.ToString(cmd.ExecuteScalar())!="Exclusive")throw new InvalidOperationException("Se perdió la exclusión Gmail.");}
        }
        public void Dispose(){var cn=connection;connection=null;if(cn!=null)cn.Dispose();}
    }
}
