USE [RecepcionDocumental];
GO
SET XACT_ABORT ON;
GO
IF COL_LENGTH(N'dbo.DocumentoRecepcion',N'EtiquetaRevision') IS NULL
    ALTER TABLE dbo.DocumentoRecepcion ADD EtiquetaRevision NVARCHAR(30) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.DocumentoRecepcion') AND name=N'CK_DocumentoRecepcion_EtiquetaRevision')
    ALTER TABLE dbo.DocumentoRecepcion WITH CHECK ADD CONSTRAINT CK_DocumentoRecepcion_EtiquetaRevision CHECK (EtiquetaRevision IS NULL OR EtiquetaRevision IN (N'FACTURA',N'OTRO_DOCUMENTO',N'NO_DOCUMENTO'));
GO
