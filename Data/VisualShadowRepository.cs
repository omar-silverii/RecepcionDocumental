using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Configuration;
using RecepcionDocumental.Infrastructure;
using RecepcionDocumental.Services;

namespace RecepcionDocumental.Data
{
    public static class VisualShadowRepository
    {
        private static string ConnectionString { get { return WebConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; } }
        public static bool Save(long documentId,VisualShadowResult value)
        {
            if(value==null||!value.Attempted)return false;
            const string sql=@"INSERT dbo.DocumentoVisionShadow(DocumentoRecepcionId,ModeloVersion,ModeloSha256,PreprocesamientoVersion,Estado,PNoFactura,PFactura,Zona,OrigenVisual,RasterReutilizado,DecodeMs,ResizeMs,NormalizacionMs,OnnxMs,TotalMs,ErrorCodigo,ErrorDetalle,FechaEvaluacionUtc)
SELECT @DocumentId,@ModelVersion,@ModelSha,@Preprocessing,@Status,@PNo,@PYes,@Zone,@Source,@Reused,@Decode,@Resize,@Normalize,@Onnx,@Total,@ErrorCode,@ErrorDetail,SYSUTCDATETIME()
WHERE NOT EXISTS(SELECT 1 FROM dbo.DocumentoVisionShadow WITH(UPDLOCK,SERIALIZABLE) WHERE DocumentoRecepcionId=@DocumentId AND ModeloVersion=@ModelVersion AND ModeloSha256=@ModelSha);";
            try{using(var cn=new SqlConnection(ConnectionString))using(var cmd=new SqlCommand(sql,cn)){cmd.Parameters.Add("@DocumentId",SqlDbType.BigInt).Value=documentId;cmd.Parameters.Add("@ModelVersion",SqlDbType.NVarChar,100).Value=value.ModelVersion;cmd.Parameters.Add("@ModelSha",SqlDbType.Char,64).Value=value.ModelSha256;cmd.Parameters.Add("@Preprocessing",SqlDbType.NVarChar,100).Value=value.PreprocessingVersion;cmd.Parameters.Add("@Status",SqlDbType.NVarChar,20).Value=value.Status;Add(cmd,"@PNo",SqlDbType.Float,value.PNoFactura);Add(cmd,"@PYes",SqlDbType.Float,value.PFactura);Add(cmd,"@Zone",SqlDbType.NVarChar,value.Zone,50);cmd.Parameters.Add("@Source",SqlDbType.NVarChar,100).Value=value.VisualSource;cmd.Parameters.Add("@Reused",SqlDbType.Bit).Value=value.RasterReused;Add(cmd,"@Decode",SqlDbType.Int,value.DecodeMilliseconds);Add(cmd,"@Resize",SqlDbType.Int,value.ResizeMilliseconds);Add(cmd,"@Normalize",SqlDbType.Int,value.NormalizeMilliseconds);Add(cmd,"@Onnx",SqlDbType.Int,value.OnnxMilliseconds);Add(cmd,"@Total",SqlDbType.Int,value.TotalMilliseconds);Add(cmd,"@ErrorCode",SqlDbType.NVarChar,value.ErrorCode,100);Add(cmd,"@ErrorDetail",SqlDbType.NVarChar,value.ErrorReason,1000);cn.Open();return cmd.ExecuteNonQuery()==1;}}
            catch(Exception ex){Logs.LogError("VisualShadow | Operación=Persistir | Estado=ERROR | "+Logs.DescribirExcepcion(ex));return false;}
        }
        private static void Add(SqlCommand cmd,string name,SqlDbType type,object value,int size=0){var p=size>0?cmd.Parameters.Add(name,type,size):cmd.Parameters.Add(name,type);p.Value=value??DBNull.Value;}
    }
}
