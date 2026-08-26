# H1D2A — Probe PDFtoImage 5.4.0

Fecha: 2026-08-25  
Estado: probe técnico aprobado e **integración productiva implementada y validada localmente**; sin publicación.

## Alcance

Se evaluó `PDFtoImage 5.4.0` sobre los dos PDF escaneados históricos que Mdoc reporta sin texto útil y sin imágenes extraíbles. El probe rasteriza a PNG a 300 DPI, entrega las imágenes a `DocumentOcrService` y clasifica el texto mediante `InvoiceSelector.SelectOcrText`. Cuando el primer pase queda en `REVISAR`, utiliza el segundo pase de encabezado ya existente (`RecognizeHeader` + `Combine`).

Esta sección describe el probe que precedió a la aprobación. En esa fase no se modificaron `DocumentAnalysisService`, `DocumentOcrService`, Mdoc, OCR productivo, Gmail, QR, SQL, almacenamiento ni UI.

## Resultados

| PDF | Páginas | Dimensiones PNG | Píxeles | PNG | Render | OCR | Resultado |
|---|---:|---:|---:|---:|---:|---:|---|
| `FacturaSINQR_fa8700c24dbd.pdf` | 1 | 3299 × 2550 | 8.412.450 | 482.343 bytes | 502 ms (559 ms total de raster) | 1.094 ms; 957 caracteres; confianza 0,940 | `FACTURA / OCR`, 5 señales fiscales |
| `PRUEBA_OCR_PDF_MDOC_8ae4509aefa6.pdf` | 1 | 2480 × 3509 | 8.702.320 | 784.972 bytes | 549 ms (605 ms total de raster) | 1.290 ms acumulados; 1.019 caracteres; confianza 0,880 | `FACTURA / OCR`, 4 señales fiscales después del segundo pase de encabezado |

Medición aproximada durante rasterización:

- Primer PDF: delta administrado 861.136 bytes; delta de memoria privada 3.895.296 bytes.
- Segundo PDF: delta administrado 1.138.744 bytes; delta de memoria privada 503.808 bytes.

Estas cifras son del proceso de prueba y no representan un pico garantizado: PDFium/Skia mantienen caches nativas entre operaciones. Para producción deberá medirse el working set del worker IIS durante lotes reales.

## Límites

Ambos casos respetaron sin cambios los límites actuales:

- 1 página de un máximo de 5;
- menos de 16.000.000 píxeles por página;
- menos de 40.000.000 píxeles acumulados;
- origen menor a 25 MB;
- texto OCR menor a 200.000 caracteres.

No fue necesario proponer aumentos. El probe aborta antes de OCR si cualquiera de esos límites se supera.

## Dependencias

El probe aislado utilizó y la integración productiva incorporó al proyecto web exactamente:

- `PDFtoImage 5.4.0` (`lib/net471`, compatible con el proyecto net48);
- `SkiaSharp 4.150.1` (`lib/net48`);
- `SkiaSharp.NativeAssets.Win32 4.150.1`;
- `bblanchon.PDFium.Win32 152.0.7961`;
- las dependencias administradas ya presentes en el producto, incluido `System.Memory 4.6.3`;
- `DocumentOcrService`, Tesseract 5.2.0 y `spa.traineddata` existentes.

El output x64 comprobado contiene:

- `PDFtoImage.dll`;
- `SkiaSharp.dll`;
- `x64/pdfium.dll` (PE x64);
- `x64/libSkiaSharp.dll` (PE x64);
- `x64/tesseract50.dll` y `x64/leptonica-1.82.0.dll`;
- `App_Data/Tessdata/spa.traineddata`.

Las DLL nativas PDFium y Skia dependen de componentes Windows. En particular, `libSkiaSharp.dll` declara `D3D12.dll` y `D3DCompiler_47.dll`; su presencia debe verificarse en el servidor.

## Integración productiva validada

La implementación agrega `PdfPageRasterizer` como servicio aislado y conserva este orden determinístico:

1. texto útil de Mdoc;
2. imágenes extraídas por Mdoc;
3. rasterización PDFtoImage únicamente si los dos pasos anteriores no entregan material y Mdoc no informó un límite.

No se reintenta con el renderer cuando el OCR de imágenes Mdoc queda en `REVISAR`. Cada página se guarda secuencialmente como PNG a 300 DPI dentro de `AttachmentWorkspace`, se entrega al `DocumentOcrService` existente y pasa por el mismo `InvoiceSelector`, incluido el segundo pase de encabezado.

El harness del flujo productivo comprobó:

- los dos PDF escaneados reales: `FACTURA / OCR`;
- PDF con texto útil Mdoc: renderer no ejecutado;
- PDF con imagen Mdoc: renderer no ejecutado, incluso con resultado OCR no concluyente;
- PDF que supera el límite: `REVISAR / OCR_LIMITE`;
- PDF inválido/no rasterizable: `REVISAR / OCR_RENDER_ERROR`, sin excepción no controlada;
- eliminación del workspace en todos los casos.

La trazabilidad `PDFRaster` registra solamente si se ejecutó, páginas, DPI, duración o motivo de bypass; no registra contenido documental.

## Limpieza

Cada ejecución crea una carpeta temporal única bajo `%TEMP%`, guarda allí las PNG y la elimina en un bloque `finally`. Las dos ejecuciones terminaron con `TEMP | Eliminado=True` y no quedó ninguna carpeta `RecepcionDocumental-PdfRasterProbe-*`.

## Riesgos y verificación en IIS

- PDFium no es thread-safe; PDFtoImage serializa internamente sus llamadas. La integración no debe asumir rasterización paralela dentro del worker.
- El Application Pool debe ejecutar en x64 (`Enable 32-Bit Applications = False`) y deben desplegarse los assets x64 en la ubicación que resuelva el loader.
- Deben verificarse permisos de lectura del PDF y escritura/borrado en la ruta temporal bajo la identidad del Application Pool.
- El output local verificado contiene `PDFtoImage.dll`, `SkiaSharp.dll`, `x64/pdfium.dll` y `x64/libSkiaSharp.dll`. `D3D12.dll` y `D3DCompiler_47.dll` son dependencias del sistema declaradas por Skia y deben estar disponibles en el servidor; no se copian como binarios del proyecto.
- La memoria nativa debe medirse con lotes y PDFs de hasta cinco páginas antes de habilitar producción.
- La integración deberá mantener Mdoc como primera opción y activar el renderer sólo cuando no haya texto útil ni imagen Mdoc procesable.

## Resultado

**INTEGRACIÓN PRODUCTIVA VALIDADA LOCALMENTE**. La solución compila y los casos técnicos requeridos pasan. Queda expresamente pendiente la verificación operativa del worker IIS x64 en el servidor antes de publicar; este cambio no incluye publicación.
