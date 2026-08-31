USE [RecepcionDocumental];
GO
SET XACT_ABORT ON;
GO
IF COL_LENGTH(N'dbo.DocumentoRecepcion', N'ResultadoRevision') IS NULL ALTER TABLE dbo.DocumentoRecepcion ADD ResultadoRevision NVARCHAR(20) NULL;
IF COL_LENGTH(N'dbo.DocumentoRecepcion', N'FechaRevisionUtc') IS NULL ALTER TABLE dbo.DocumentoRecepcion ADD FechaRevisionUtc DATETIME2(0) NULL;
IF COL_LENGTH(N'dbo.DocumentoRecepcion', N'UsuarioRevision') IS NULL ALTER TABLE dbo.DocumentoRecepcion ADD UsuarioRevision NVARCHAR(256) NULL;
IF COL_LENGTH(N'dbo.DocumentoRecepcion', N'ObservacionRevision') IS NULL ALTER TABLE dbo.DocumentoRecepcion ADD ObservacionRevision NVARCHAR(1000) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.DocumentoRecepcion') AND name=N'CK_DocumentoRecepcion_ResultadoRevision')
    ALTER TABLE dbo.DocumentoRecepcion WITH CHECK ADD CONSTRAINT CK_DocumentoRecepcion_ResultadoRevision CHECK (ResultadoRevision IS NULL OR ResultadoRevision IN (N'FACTURA',N'DESCARTAR'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DocumentoRecepcion') AND name=N'IX_DocumentoRecepcion_RevisionPendiente')
    CREATE INDEX IX_DocumentoRecepcion_RevisionPendiente ON dbo.DocumentoRecepcion(Clasificacion,ResultadoRevision,FechaClasificacionUtc DESC);
GO
