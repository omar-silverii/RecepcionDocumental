SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.DocumentoVisionShadow',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DocumentoVisionShadow(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentoVisionShadow PRIMARY KEY,
  DocumentoRecepcionId BIGINT NOT NULL, ModeloVersion NVARCHAR(100) NOT NULL, ModeloSha256 CHAR(64) NOT NULL,
  PreprocesamientoVersion NVARCHAR(100) NOT NULL, Estado NVARCHAR(20) NOT NULL,
  PNoFactura FLOAT NULL, PFactura FLOAT NULL, Zona NVARCHAR(50) NULL, OrigenVisual NVARCHAR(100) NOT NULL,
  RasterReutilizado BIT NOT NULL, DecodeMs INT NULL, ResizeMs INT NULL, NormalizacionMs INT NULL, OnnxMs INT NULL, TotalMs INT NULL,
  ErrorCodigo NVARCHAR(100) NULL, ErrorDetalle NVARCHAR(1000) NULL, FechaEvaluacionUtc DATETIME2(0) NOT NULL,
  CONSTRAINT FK_DocumentoVisionShadow_DocumentoRecepcion FOREIGN KEY(DocumentoRecepcionId) REFERENCES dbo.DocumentoRecepcion(Id),
  CONSTRAINT UQ_DocumentoVisionShadow_Modelo UNIQUE(DocumentoRecepcionId,ModeloVersion,ModeloSha256),
  CONSTRAINT CK_DocumentoVisionShadow_Estado CHECK(Estado IN(N'OK',N'ERROR')),
  CONSTRAINT CK_DocumentoVisionShadow_Probabilidades CHECK((PNoFactura IS NULL OR PNoFactura BETWEEN 0 AND 1) AND (PFactura IS NULL OR PFactura BETWEEN 0 AND 1)),
  CONSTRAINT CK_DocumentoVisionShadow_Zona CHECK((Estado=N'ERROR' AND Zona IS NULL) OR (Estado=N'OK' AND Zona IN(N'NO_FACTURA_FUERTE',N'INCIERTO_VISUAL',N'FACTURA_FUERTE')))
 );
 CREATE INDEX IX_DocumentoVisionShadow_ModeloEstadoFecha ON dbo.DocumentoVisionShadow(ModeloVersion,Estado,FechaEvaluacionUtc);
END;
COMMIT TRANSACTION;
