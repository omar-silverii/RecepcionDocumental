USE [RecepcionDocumental];
GO
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO
BEGIN TRY
 BEGIN TRANSACTION;
 IF OBJECT_ID(N'dbo.DocumentoGroundTruth',N'U') IS NULL
 BEGIN
  CREATE TABLE dbo.DocumentoGroundTruth(
   Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentoGroundTruth PRIMARY KEY,
   DocumentoRecepcionId BIGINT NOT NULL, Secuencia INT NOT NULL, EsVigente BIT NOT NULL,
   EtiquetaBinaria NVARCHAR(20) NOT NULL, EtiquetaDetallada NVARCHAR(30) NOT NULL, Fuente NVARCHAR(50) NOT NULL,
   DocumentoSha256 CHAR(64) NOT NULL, TamanioBytes BIGINT NOT NULL, UsuarioRevision NVARCHAR(256) NULL,
   ObservacionRevision NVARCHAR(1000) NULL, FechaDecisionUtc DATETIME2(0) NOT NULL, DocumentoVisionShadowId BIGINT NULL,
   CONSTRAINT FK_DocumentoGroundTruth_DocumentoRecepcion FOREIGN KEY(DocumentoRecepcionId) REFERENCES dbo.DocumentoRecepcion(Id),
   CONSTRAINT FK_DocumentoGroundTruth_DocumentoVisionShadow FOREIGN KEY(DocumentoVisionShadowId) REFERENCES dbo.DocumentoVisionShadow(Id),
   CONSTRAINT CK_DocumentoGroundTruth_EtiquetaBinaria CHECK(EtiquetaBinaria IN(N'FACTURA',N'NO_FACTURA')),
   CONSTRAINT CK_DocumentoGroundTruth_EtiquetaDetallada CHECK(EtiquetaDetallada IN(N'FACTURA',N'OTRO_DOCUMENTO',N'NO_DOCUMENTO')),
   CONSTRAINT CK_DocumentoGroundTruth_Consistencia CHECK((EtiquetaBinaria=N'FACTURA' AND EtiquetaDetallada=N'FACTURA') OR (EtiquetaBinaria=N'NO_FACTURA' AND EtiquetaDetallada IN(N'OTRO_DOCUMENTO',N'NO_DOCUMENTO'))),
   CONSTRAINT CK_DocumentoGroundTruth_Fuente CHECK(Fuente IN(N'REVISION_OPERATIVA',N'MIGRACION_REVISION_EXISTENTE')),
   CONSTRAINT CK_DocumentoGroundTruth_Secuencia CHECK(Secuencia>=1),
   CONSTRAINT CK_DocumentoGroundTruth_Tamanio CHECK(TamanioBytes>=0),
   CONSTRAINT UQ_DocumentoGroundTruth_DocumentoSecuencia UNIQUE(DocumentoRecepcionId,Secuencia));
 END
 ELSE
 BEGIN
  IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.DocumentoGroundTruth'))<>13 THROW 51000,'DocumentoGroundTruth: cantidad de columnas incompatible.',1;
  IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.DocumentoGroundTruth') AND
    ((name=N'Id' AND system_type_id=127 AND is_nullable=0 AND is_identity=1) OR
     (name=N'DocumentoRecepcionId' AND system_type_id=127 AND is_nullable=0) OR (name=N'Secuencia' AND system_type_id=56 AND is_nullable=0) OR
     (name=N'EsVigente' AND system_type_id=104 AND is_nullable=0) OR (name=N'EtiquetaBinaria' AND system_type_id=231 AND max_length=40 AND is_nullable=0) OR
     (name=N'EtiquetaDetallada' AND system_type_id=231 AND max_length=60 AND is_nullable=0) OR (name=N'Fuente' AND system_type_id=231 AND max_length=100 AND is_nullable=0) OR
     (name=N'DocumentoSha256' AND system_type_id=175 AND max_length=64 AND is_nullable=0) OR (name=N'TamanioBytes' AND system_type_id=127 AND is_nullable=0) OR
     (name=N'UsuarioRevision' AND system_type_id=231 AND max_length=512 AND is_nullable=1) OR (name=N'ObservacionRevision' AND system_type_id=231 AND max_length=2000 AND is_nullable=1) OR
     (name=N'FechaDecisionUtc' AND system_type_id=42 AND scale=0 AND is_nullable=0) OR (name=N'DocumentoVisionShadowId' AND system_type_id=127 AND is_nullable=1)))<>13
   THROW 51000,'DocumentoGroundTruth: columnas incompatibles.',1;
  IF (SELECT COUNT(*) FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.DocumentoGroundTruth') AND name IN(N'PK_DocumentoGroundTruth',N'UQ_DocumentoGroundTruth_DocumentoSecuencia'))<>2 THROW 51000,'DocumentoGroundTruth: PK/UNIQUE incompatibles.',1;
  IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DocumentoGroundTruth') AND name IN(N'FK_DocumentoGroundTruth_DocumentoRecepcion',N'FK_DocumentoGroundTruth_DocumentoVisionShadow'))<>2 THROW 51000,'DocumentoGroundTruth: FKs incompatibles.',1;
  IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.DocumentoGroundTruth') AND name IN(N'CK_DocumentoGroundTruth_EtiquetaBinaria',N'CK_DocumentoGroundTruth_EtiquetaDetallada',N'CK_DocumentoGroundTruth_Consistencia',N'CK_DocumentoGroundTruth_Fuente',N'CK_DocumentoGroundTruth_Secuencia',N'CK_DocumentoGroundTruth_Tamanio') AND is_disabled=0 AND is_not_trusted=0)<>6 THROW 51000,'DocumentoGroundTruth: CHECKs incompatibles.',1;
 END;
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DocumentoGroundTruth') AND name=N'IX_DocumentoGroundTruth_EtiquetaFecha')
  CREATE INDEX IX_DocumentoGroundTruth_EtiquetaFecha ON dbo.DocumentoGroundTruth(EtiquetaBinaria,FechaDecisionUtc);
 ELSE IF NOT EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.DocumentoGroundTruth') AND i.name=N'IX_DocumentoGroundTruth_EtiquetaFecha' AND i.is_unique=0 AND i.has_filter=0) THROW 51000,'IX_DocumentoGroundTruth_EtiquetaFecha incompatible.',1;
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DocumentoGroundTruth') AND name=N'UX_DocumentoGroundTruth_DocumentoVigente')
  CREATE UNIQUE INDEX UX_DocumentoGroundTruth_DocumentoVigente ON dbo.DocumentoGroundTruth(DocumentoRecepcionId) WHERE EsVigente=1;
 ELSE IF NOT EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.DocumentoGroundTruth') AND i.name=N'UX_DocumentoGroundTruth_DocumentoVigente' AND i.is_unique=1 AND i.has_filter=1) THROW 51000,'UX_DocumentoGroundTruth_DocumentoVigente incompatible.',1;
 INSERT dbo.DocumentoGroundTruth(DocumentoRecepcionId,Secuencia,EsVigente,EtiquetaBinaria,EtiquetaDetallada,Fuente,DocumentoSha256,TamanioBytes,UsuarioRevision,ObservacionRevision,FechaDecisionUtc,DocumentoVisionShadowId)
 SELECT d.Id,1,1,CASE WHEN d.EtiquetaRevision=N'FACTURA' THEN N'FACTURA' ELSE N'NO_FACTURA' END,d.EtiquetaRevision,N'MIGRACION_REVISION_EXISTENTE',d.HashSha256,d.TamanioBytes,d.UsuarioRevision,d.ObservacionRevision,d.FechaRevisionUtc,NULL
 FROM dbo.DocumentoRecepcion d WHERE d.FechaRevisionUtc IS NOT NULL AND ((d.ResultadoRevision=N'FACTURA' AND d.EtiquetaRevision=N'FACTURA') OR (d.ResultadoRevision=N'DESCARTAR' AND d.EtiquetaRevision IN(N'OTRO_DOCUMENTO',N'NO_DOCUMENTO')))
 AND NOT EXISTS(SELECT 1 FROM dbo.DocumentoGroundTruth gt WITH(UPDLOCK,SERIALIZABLE) WHERE gt.DocumentoRecepcionId=d.Id);
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
GO
SELECT d.Id AS DocumentoRecepcionId,d.ResultadoRevision,d.EtiquetaRevision,d.FechaRevisionUtc,N'COMBINACION_INCOMPLETA_O_INCOHERENTE' AS Motivo
FROM dbo.DocumentoRecepcion d WHERE d.ResultadoRevision IS NOT NULL AND (d.FechaRevisionUtc IS NULL OR d.EtiquetaRevision IS NULL OR (d.ResultadoRevision=N'FACTURA' AND d.EtiquetaRevision<>N'FACTURA') OR (d.ResultadoRevision=N'DESCARTAR' AND d.EtiquetaRevision NOT IN(N'OTRO_DOCUMENTO',N'NO_DOCUMENTO'))) ORDER BY d.Id;
GO
