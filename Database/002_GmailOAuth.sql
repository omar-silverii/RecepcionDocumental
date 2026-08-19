USE [RecepcionDocumental];
GO
SET XACT_ABORT ON;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.GmailCuenta')
      AND name = N'UX_GmailCuenta_Email'
)
BEGIN
    CREATE UNIQUE INDEX UX_GmailCuenta_Email ON dbo.GmailCuenta(Email);
END;
GO
