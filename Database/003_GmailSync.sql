USE [RecepcionDocumental];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.GmailCuenta', N'UltimoHistoryId') IS NULL
BEGIN
    ALTER TABLE dbo.GmailCuenta ADD UltimoHistoryId NVARCHAR(50) NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.GmailAdjunto') AND name = N'UX_GmailAdjunto_AttachmentId')
BEGIN
    CREATE UNIQUE INDEX UX_GmailAdjunto_AttachmentId
        ON dbo.GmailAdjunto(GmailMensajeId, GmailAttachmentId)
        WHERE GmailAttachmentId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.GmailAdjunto') AND name = N'UX_GmailAdjunto_PartId')
BEGIN
    CREATE UNIQUE INDEX UX_GmailAdjunto_PartId
        ON dbo.GmailAdjunto(GmailMensajeId, GmailPartId)
        WHERE GmailAttachmentId IS NULL AND GmailPartId IS NOT NULL;
END;
GO
