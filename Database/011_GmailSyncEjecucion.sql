USE [RecepcionDocumental];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
BEGIN TRY
 BEGIN TRANSACTION;
 IF OBJECT_ID(N'dbo.GmailSyncEjecucion',N'U') IS NULL
 BEGIN
  CREATE TABLE dbo.GmailSyncEjecucion(
   Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_GmailSyncEjecucion PRIMARY KEY,
   GmailCuentaId INT NULL,
   Origen NVARCHAR(20) NOT NULL,
   FechaInicioUtc DATETIME2(0) NOT NULL,
   FechaFinUtc DATETIME2(0) NULL,
   Estado NVARCHAR(40) NOT NULL,
   MensajesEncontrados INT NOT NULL DEFAULT(0),
   MensajesNuevos INT NOT NULL DEFAULT(0),
   AdjuntosAnalizados INT NOT NULL DEFAULT(0),
   Facturas INT NOT NULL DEFAULT(0),
   Revisar INT NOT NULL DEFAULT(0),
   Descartados INT NOT NULL DEFAULT(0),
   DocumentosExistentes INT NOT NULL DEFAULT(0),
   Errores INT NOT NULL DEFAULT(0),
   UsoFallbackInicial BIT NOT NULL DEFAULT(0),
   DetalleError NVARCHAR(500) NULL,
   CONSTRAINT FK_GmailSyncEjecucion_Cuenta FOREIGN KEY(GmailCuentaId) REFERENCES dbo.GmailCuenta(Id),
   CONSTRAINT CK_GmailSyncEjecucion_Origen CHECK(Origen IN(N'WEB',N'SCHEDULER')),
   CONSTRAINT CK_GmailSyncEjecucion_Estado CHECK(Estado IN(N'EJECUTANDO',N'COMPLETADA',N'COMPLETADA_CON_ERRORES',N'FALLIDA',N'OMITIDA_YA_EN_EJECUCION')),
   CONSTRAINT CK_GmailSyncEjecucion_Fechas CHECK((Estado=N'EJECUTANDO' AND FechaFinUtc IS NULL) OR (Estado<>N'EJECUTANDO' AND FechaFinUtc>=FechaInicioUtc)));
 END;
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.GmailSyncEjecucion') AND name=N'IX_GmailSyncEjecucion_Inicio')
  CREATE INDEX IX_GmailSyncEjecucion_Inicio ON dbo.GmailSyncEjecucion(FechaInicioUtc DESC,Id DESC);
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
GO
