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
 IF OBJECT_ID(N'dbo.DocumentoGroundTruth',N'U') IS NULL THROW 51000,'010 requiere 009.',1;
 -- Single authoritative bucket helper, shared by backfill and live ingestion.
 IF OBJECT_ID(N'dbo.H1D7C2Bucket',N'FN') IS NULL
  EXEC(N'CREATE FUNCTION dbo.H1D7C2Bucket(@sha varchar(64)) RETURNS int AS BEGIN RETURN NULL; END');
 EXEC(N'ALTER FUNCTION dbo.H1D7C2Bucket(@sha varchar(64)) RETURNS int WITH SCHEMABINDING AS
 BEGIN
  IF LEN(@sha)<>64 OR @sha COLLATE Latin1_General_100_BIN2 LIKE ''%[^0-9a-fA-F]%'' RETURN NULL;
  RETURN CONVERT(int,CONVERT(bigint,CONVERT(binary(4),LEFT(@sha,8),2)) % 10);
 END');
 IF OBJECT_ID(N'dbo.DocumentoRevisionMuestra',N'U') IS NULL
 BEGIN
  CREATE TABLE dbo.DocumentoRevisionMuestra(
   Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentoRevisionMuestra PRIMARY KEY,
   DocumentoRecepcionId BIGINT NOT NULL,
   TipoMuestra NVARCHAR(50) NOT NULL,
   ReglaVersion NVARCHAR(50) NOT NULL,
   Modulo INT NOT NULL,
   Bucket INT NOT NULL,
   FechaSeleccionUtc DATETIME2(0) NOT NULL,
   DocumentoGroundTruthId BIGINT NULL,
   FechaResolucionUtc DATETIME2(0) NULL,
   CONSTRAINT FK_Muestra_Documento FOREIGN KEY(DocumentoRecepcionId) REFERENCES dbo.DocumentoRecepcion(Id),
   CONSTRAINT FK_Muestra_GroundTruth FOREIGN KEY(DocumentoGroundTruthId) REFERENCES dbo.DocumentoGroundTruth(Id),
   CONSTRAINT UQ_Muestra_Documento UNIQUE(DocumentoRecepcionId),
   CONSTRAINT CK_Muestra_Tipo CHECK(TipoMuestra=N'FACTURA_AUTOMATICA'),
   CONSTRAINT CK_Muestra_Modulo CHECK(Modulo>1),
   CONSTRAINT CK_Muestra_Bucket CHECK(Bucket>=0 AND Bucket<Modulo),
   CONSTRAINT CK_Muestra_Resolucion CHECK((DocumentoGroundTruthId IS NULL AND FechaResolucionUtc IS NULL) OR (DocumentoGroundTruthId IS NOT NULL AND FechaResolucionUtc IS NOT NULL)));
 END;
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DocumentoRevisionMuestra') AND name=N'IX_Muestra_Pendientes')
  CREATE INDEX IX_Muestra_Pendientes ON dbo.DocumentoRevisionMuestra(DocumentoGroundTruthId,Id) INCLUDE(DocumentoRecepcionId);
 -- Replace only the known CHECK, never data or tables. Rollback restores it on failure.
 IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.DocumentoGroundTruth') AND name=N'CK_DocumentoGroundTruth_Fuente')
  ALTER TABLE dbo.DocumentoGroundTruth DROP CONSTRAINT CK_DocumentoGroundTruth_Fuente;
 ALTER TABLE dbo.DocumentoGroundTruth WITH CHECK ADD CONSTRAINT CK_DocumentoGroundTruth_Fuente
  CHECK(Fuente IN(N'REVISION_OPERATIVA',N'MIGRACION_REVISION_EXISTENTE',N'MUESTREO_FACTURA_CIEGO'));
 EXEC(N' DECLARE @Eligible int=(SELECT COUNT(*) FROM dbo.DocumentoRecepcion WHERE Clasificacion=N''FACTURA'' AND ResultadoRevision IS NULL);
 DECLARE @Excluded int=(SELECT COUNT(*) FROM dbo.DocumentoRecepcion WHERE Clasificacion=N''FACTURA'' AND ResultadoRevision IS NOT NULL);
 DECLARE @Selected int=(SELECT COUNT(*) FROM dbo.DocumentoRecepcion WHERE Clasificacion=N''FACTURA'' AND ResultadoRevision IS NULL AND dbo.H1D7C2Bucket(HashSha256)=0);
 DECLARE @Existing int=(SELECT COUNT(*) FROM dbo.DocumentoRecepcion d JOIN dbo.DocumentoRevisionMuestra s ON s.DocumentoRecepcionId=d.Id WHERE d.Clasificacion=N''FACTURA'' AND d.ResultadoRevision IS NULL AND dbo.H1D7C2Bucket(d.HashSha256)=0);
 INSERT dbo.DocumentoRevisionMuestra(DocumentoRecepcionId,TipoMuestra,ReglaVersion,Modulo,Bucket,FechaSeleccionUtc)
 SELECT d.Id,N''FACTURA_AUTOMATICA'',N''H1D7C2-V1'',10,dbo.H1D7C2Bucket(d.HashSha256),SYSUTCDATETIME()
 FROM dbo.DocumentoRecepcion d WHERE d.Clasificacion=N''FACTURA'' AND d.ResultadoRevision IS NULL AND dbo.H1D7C2Bucket(d.HashSha256)=0
 AND NOT EXISTS(SELECT 1 FROM dbo.DocumentoRevisionMuestra s WITH(UPDLOCK,HOLDLOCK) WHERE s.DocumentoRecepcionId=d.Id);
 DECLARE @Inserted int=@@ROWCOUNT;
 SELECT @Eligible AS Elegibles,@Selected AS Seleccionadas,CAST(100.0*@Selected/NULLIF(@Eligible,0) AS decimal(9,2)) AS Porcentaje,
 @Excluded AS RevisadasExcluidas,@Existing AS DuplicadosEvitados,@Inserted AS Insertadas;');
 -- H1D7C2_ROLLBACK_TEST_POINT
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
GO
