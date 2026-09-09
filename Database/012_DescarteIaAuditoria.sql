USE [RecepcionDocumental];
GO
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.DocumentoDescarteIa', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentoDescarteIa
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentoDescarteIa PRIMARY KEY,
        GmailMessageId NVARCHAR(255) NOT NULL,
        GmailPartId NVARCHAR(255) NOT NULL,
        FechaMensajeUtc DATETIME2(0) NOT NULL,
        Remitente NVARCHAR(500) NOT NULL,
        Asunto NVARCHAR(1000) NULL,
        NombreOriginal NVARCHAR(500) NOT NULL,
        OrigenTipo NVARCHAR(20) NOT NULL,
        RutaInternaContenedor NVARCHAR(2000) NULL,
        OrigenHash CHAR(64) NOT NULL,
        MetodoDeteccion NVARCHAR(50) NOT NULL,
        Confianza TINYINT NULL,
        MotivoClasificacion NVARCHAR(2000) NULL,
        FechaClasificacionUtc DATETIME2(0) NOT NULL CONSTRAINT DF_DocumentoDescarteIa_FechaClasificacionUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_DocumentoDescarteIa_OrigenTipo CHECK (OrigenTipo IN (N'DIRECTO',N'ZIP')),
        CONSTRAINT CK_DocumentoDescarteIa_Confianza CHECK (Confianza IS NULL OR Confianza BETWEEN 0 AND 100)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DocumentoDescarteIa') AND name=N'UX_DocumentoDescarteIa_Origen')
    CREATE UNIQUE INDEX UX_DocumentoDescarteIa_Origen ON dbo.DocumentoDescarteIa(GmailMessageId,GmailPartId,OrigenHash);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DocumentoDescarteIa') AND name=N'IX_DocumentoDescarteIa_Fecha')
    CREATE INDEX IX_DocumentoDescarteIa_Fecha ON dbo.DocumentoDescarteIa(FechaClasificacionUtc DESC);
GO
