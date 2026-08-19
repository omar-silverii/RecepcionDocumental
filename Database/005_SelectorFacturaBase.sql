USE [RecepcionDocumental];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.DocumentoRecepcion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentoRecepcion
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentoRecepcion PRIMARY KEY,
        GmailMensajeId BIGINT NOT NULL,
        GmailPartId NVARCHAR(255) NOT NULL,
        OrigenTipo NVARCHAR(20) NOT NULL,
        RutaInternaContenedor NVARCHAR(2000) NULL,
        OrigenHash CHAR(64) NOT NULL,
        NombreOriginal NVARCHAR(500) NOT NULL,
        MimeType NVARCHAR(255) NULL,
        TamanioBytes BIGINT NOT NULL,
        RutaLocal NVARCHAR(2000) NOT NULL,
        HashSha256 CHAR(64) NOT NULL,
        Clasificacion NVARCHAR(20) NOT NULL,
        MetodoDeteccion NVARCHAR(50) NOT NULL,
        Confianza TINYINT NULL,
        MotivoClasificacion NVARCHAR(2000) NULL,
        QrDetectado BIT NOT NULL CONSTRAINT DF_DocumentoRecepcion_QrDetectado DEFAULT (0),
        TipoComprobanteArca INT NULL,
        FechaClasificacionUtc DATETIME2(0) NOT NULL,
        FechaAltaUtc DATETIME2(0) NOT NULL CONSTRAINT DF_DocumentoRecepcion_FechaAltaUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_DocumentoRecepcion_GmailMensaje FOREIGN KEY (GmailMensajeId) REFERENCES dbo.GmailMensaje(Id),
        CONSTRAINT CK_DocumentoRecepcion_Clasificacion CHECK (Clasificacion IN (N'FACTURA', N'REVISAR')),
        CONSTRAINT CK_DocumentoRecepcion_OrigenTipo CHECK (OrigenTipo IN (N'DIRECTO', N'ZIP')),
        CONSTRAINT CK_DocumentoRecepcion_Tamanio CHECK (TamanioBytes >= 0),
        CONSTRAINT CK_DocumentoRecepcion_Confianza CHECK (Confianza IS NULL OR Confianza BETWEEN 0 AND 100)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DocumentoRecepcion') AND name=N'UX_DocumentoRecepcion_Origen')
    CREATE UNIQUE INDEX UX_DocumentoRecepcion_Origen ON dbo.DocumentoRecepcion(GmailMensajeId, GmailPartId, OrigenHash);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DocumentoRecepcion') AND name=N'IX_DocumentoRecepcion_ClasificacionFecha')
    CREATE INDEX IX_DocumentoRecepcion_ClasificacionFecha ON dbo.DocumentoRecepcion(Clasificacion, FechaClasificacionUtc DESC);
GO
