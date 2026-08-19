USE [RecepcionDocumental];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#AdjuntosRanked') IS NOT NULL DROP TABLE #AdjuntosRanked;

SELECT
    a.Id,
    a.GmailMensajeId,
    a.GmailPartId,
    ROW_NUMBER() OVER
    (
        PARTITION BY a.GmailMensajeId, a.GmailPartId
        ORDER BY
            CASE WHEN a.Estado = N'Descargado' AND a.RutaLocal IS NOT NULL AND a.HashSha256 IS NOT NULL THEN 0
                 WHEN a.Estado = N'Descargado' THEN 1 ELSE 2 END,
            a.Id
    ) AS OrdenCanonico
INTO #AdjuntosRanked
FROM dbo.GmailAdjunto a
WHERE a.GmailPartId IS NOT NULL;

SELECT
    r.GmailMensajeId,
    r.GmailPartId,
    COUNT(*) AS FilasDetectadas,
    SUM(CASE WHEN r.OrdenCanonico > 1 THEN 1 ELSE 0 END) AS FilasAConsolidar
FROM #AdjuntosRanked r
GROUP BY r.GmailMensajeId, r.GmailPartId
HAVING COUNT(*) > 1
ORDER BY r.GmailMensajeId, r.GmailPartId;

SELECT
    N'RUTA_HUERFANA_PARA_LIMPIEZA' AS Resultado,
    a.Id AS GmailAdjuntoIdEliminado,
    a.GmailMensajeId,
    a.GmailPartId,
    a.RutaLocal
FROM dbo.GmailAdjunto a
INNER JOIN #AdjuntosRanked r ON r.Id = a.Id
WHERE r.OrdenCanonico > 1 AND a.RutaLocal IS NOT NULL
ORDER BY a.GmailMensajeId, a.GmailPartId, a.Id;

DELETE a
OUTPUT
    N'FILA_CONSOLIDADA' AS Resultado,
    DELETED.Id AS GmailAdjuntoIdEliminado,
    DELETED.GmailMensajeId,
    DELETED.GmailPartId,
    DELETED.RutaLocal
FROM dbo.GmailAdjunto a
INNER JOIN #AdjuntosRanked r ON r.Id = a.Id
WHERE r.OrdenCanonico > 1;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.GmailAdjunto') AND name = N'UX_GmailAdjunto_AttachmentId')
    DROP INDEX UX_GmailAdjunto_AttachmentId ON dbo.GmailAdjunto;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.GmailAdjunto') AND name = N'UX_GmailAdjunto_PartId')
    DROP INDEX UX_GmailAdjunto_PartId ON dbo.GmailAdjunto;

CREATE UNIQUE INDEX UX_GmailAdjunto_PartId
    ON dbo.GmailAdjunto(GmailMensajeId, GmailPartId)
    WHERE GmailPartId IS NOT NULL;

COMMIT TRANSACTION;
GO
