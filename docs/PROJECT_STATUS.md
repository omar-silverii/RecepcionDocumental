# Estado de RecepcionDocumental

## Arquitectura

Aplicación ASP.NET WebForms en C# para .NET Framework 4.8, Visual Studio 2022 y SQL Server. El acceso a datos usa ADO.NET mediante `DefaultConnection`.

## Hitos

### H1A — VALIDADO

- Aplicación ASP.NET WebForms sobre .NET Framework 4.8.
- SQL Server, `DefaultConnection` y estructura base de cuentas, mensajes y adjuntos.

### H1B — VALIDADO

- OAuth interno de Google Workspace.
- Scope único `gmail.readonly`.
- Refresh token protegido antes de persistirlo.

### H1C — VALIDADO

- Sincronización incremental mediante `historyId`.
- Full sync inicial limitado a 30 días y 100 mensajes.
- MIME recursivo y soporte para `AttachmentId` y `Body.Data`.
- Almacenamiento físico configurable y hash SHA-256.
- Idempotencia por `GmailMensajeId + GmailPartId`.
- Un 404 individual de mensaje se omite sin bloquear el cursor; un 404 por `historyId` vencido activa el fallback inicial.
- Logs diarios separados `Proc`/`Error` y timeout WebForms de 600 segundos.
- Prueba real validada con PDF, JPG y XLSX; repetición validada sin redescarga.
- La recolección MIME distingue adjuntos documentales de recursos inline: omite partes con `Content-Disposition: inline` y, cuando ese header falta, partes con `Content-ID`; conserva `Content-Disposition: attachment` y el comportamiento compatible para partes sin ambos headers. Esto evita procesar logos, firmas y recursos HTML como documentos sin filtrar por extensión, MIME, tamaño, nombre o remitente.

Los adjuntos se guardan fuera del sitio en la carpeta configurable `AdjuntosRootPath` (localmente `C:\RecepcionDocumental\Adjuntos\`).

### H1D1A — VALIDADO Y CERRADO

- Selector base con persistencia selectiva: sólo `FACTURA` y `REVISAR`; `DESCARTAR` no genera archivo definitivo ni fila documental.
- `Mdoc.dll` local, assembly `Mdoc` versión `2.0.0.0`, para lectura prudente de contenido PDF.
- `SharpCompress` 0.50.4 administrado por NuGet, para ZIP, RAR y 7Z, incluidos contenedores anidados hasta el límite configurado.
- Workspaces aislados por attachment en `Trabajo`, siempre eliminados al finalizar.
- Almacenamiento definitivo separado en `Facturas` y `Revisar`.
- ZIP controlado contra Zip Slip, colisiones, entradas, tamaño individual, expansión total y profundidad.
- Idempotencia documental por `GmailMensajeId + GmailPartId + OrigenHash`.
- PDF sin texto útil e imágenes se conservan en `REVISAR` para OCR futuro.
- Un PDF con texto aparentemente útil pero sin evidencia inequívoca se conserva como `REVISAR / PDF_TEXTO_NO_CONCLUYENTE`; sólo señales explícitas de otro tipo documental permiten descartarlo.
- El selector descarta determinísticamente comprobantes de pago y documentos de fondo de cese laboral cuando no existe una identificación explícita de factura; una factura explícita conserva prioridad sobre esas nuevas señales negativas.
- La bandeja y el detalle Gmail priorizan `DocumentoRecepcion` para mensajes H1D y mantienen el fallback histórico a `GmailAdjunto` sin sumar ambos modelos.
- H1D1A se cerró originalmente sin OCR ni QR. Posteriormente H1D1B incorporó QR ARCA best-effort, H1D2 incorporó OCR local/offline y H1D2A incorporó un rasterizador PDF exclusivamente como último fallback; el proyecto continúa sin IA/ML y sin validación ARCA online.

Validación funcional real completada para adjuntos directos, ZIP, RAR y ZIP anidado dentro de RAR. El soporte 7Z está implementado y queda pendiente de validación física con un archivo real 7Z.

### H1D1B — QR ARCA BEST-EFFORT

- ZXing se utiliza exclusivamente sobre imágenes que Mdoc puede extraer del PDF.
- La ausencia de un QR extraíble no cambia por sí sola la clasificación; el texto Mdoc conserva prioridad como evidencia documental.
- Esta fase se implementó originalmente sin renderer adicional; H1D2A agregó después un fallback de rasterización que no altera la lectura QR basada en imágenes Mdoc.

### H1D2 — OCR LOCAL/OFFLINE — VALIDADO Y CERRADO

- Tesseract 5.2.0 y el modelo oficial `tessdata_fast/spa` se usan como fallback cuando no existe texto nativo útil.
- Las imágenes JPG/JPEG/PNG/BMP/TIF/TIFF pasan por OCR; TIFF admite hasta cinco frames mediante `System.Drawing`.
- Un segundo pase OCR acotado al encabezado se ejecuta únicamente cuando el primer pase de página completa resulta no concluyente; ambos textos alimentan el mismo `InvoiceSelector`.
- Los PDF pasan primero por Mdoc: texto útil conserva prioridad y, si no lo hay, se usan sus imágenes compatibles. Desde H1D2A, sólo cuando Mdoc no entrega texto útil ni imágenes y no informa un límite, las páginas se rasterizan como último fallback.
- El texto OCR alimenta las mismas reglas de `InvoiceSelector`; no se persiste ni se registra el texto completo.
- Los fallos, límites o resultados no concluyentes se conservan en `REVISAR`.
- La validación real confirmó una Factura C JPG inicialmente dudosa y luego clasificada como `FACTURA / OCR` mediante el segundo pase, dos extractos bancarios PDF descartados correctamente, cero documentos para revisar y cero errores en esa sincronización.
- Dos PDF escaneados sin texto nativo quedaron originalmente en `REVISAR / OCR_NO_DISPONIBLE` porque Mdoc no suministró una imagen procesable. Ese hallazgo histórico motivó H1D2A; Mdoc continúa siendo la primera opción y PDFtoImage sólo cubre ese caso residual.
- El camino PDF sin texto → imagen extraíble por Mdoc → OCR está implementado y probado técnicamente, pero todavía no fue recorrido por un caso Gmail real; no se declara validado de manera independiente.

### H1D2A — RASTERIZACIÓN PDF → OCR — INTEGRACIÓN PRODUCTIVA VALIDADA

- Un probe aislado con `PDFtoImage 5.4.0`, PDFium y SkiaSharp rasterizó a 300 DPI los dos PDF escaneados históricos que Mdoc no podía entregar a OCR.
- Ambos respetaron los límites actuales y terminaron como `FACTURA / OCR`; uno necesitó el segundo pase de encabezado ya existente.
- La integración productiva mantiene el orden: texto Mdoc → imágenes Mdoc → rasterización PDFtoImage. No rasteriza si Mdoc ya produjo imágenes aunque su OCR quede en `REVISAR`.
- El renderer procesa secuencialmente hasta cinco páginas a 300 DPI dentro de `AttachmentWorkspace` y reutiliza `DocumentOcrService`, incluido su segundo pase de encabezado y el selector existente.
- Los límites OCR quedaron centralizados y sin aumentos: 25 MB de origen, 5 páginas/imágenes, 16.000.000 píxeles por imagen, 40.000.000 acumulados y 200.000 caracteres.
- La validación del flujo productivo confirmó ambos PDF como `FACTURA / OCR`, bypass con texto Mdoc, bypass con imagen Mdoc, `REVISAR / OCR_LIMITE` ante exceso, fallo controlado como `REVISAR / OCR_RENDER_ERROR` y eliminación de workspaces temporales.
- El output contiene `PDFtoImage.dll`, `SkiaSharp.dll`, `x64/pdfium.dll` y `x64/libSkiaSharp.dll`. En IIS debe mantenerse el Application Pool x64 (`Enable 32-Bit Applications = False`) y verificarse la presencia/carga de esos assets nativos.
- Detalle del probe y de la validación productiva en `docs/H1D2A_PDFTOIMAGE_PROBE.md`.

## Configuración operativa y logs

La aplicación carga una vez `RecepcionDocumental.ini` desde la raíz física al iniciar. El INI define el nombre del proyecto, las rutas absolutas `Logs`, `Trabajo`, `Facturas` y `Revisar`, los límites ZIP y `Gmail/RedirectUri`. El archivo real está excluido de Git y se proporciona `RecepcionDocumental.ini.example`.

El logger centralizado genera archivos diarios `RecepcionDocumental_Proc_yyyyMMdd.txt` y `RecepcionDocumental_Error_yyyyMMdd.txt`. Las rutas operativas nuevas no deben hardcodearse y los logs nunca deben contener secretos, tokens, connection strings, cuerpos de correo ni contenido de adjuntos.

## OAuth local

Scope único: `https://www.googleapis.com/auth/gmail.readonly`.

Variables de entorno de Windows requeridas:

- `RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_ID`
- `RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_SECRET`

Los valores reales no deben guardarse en Git, `Web.config`, SQL, JSON ni logs. El redirect URI se carga obligatoriamente desde `Gmail/RedirectUri` en el INI y debe ser una URI HTTPS absoluta válida; no existe un fallback hardcodeado.

Los refresh tokens se protegen con `MachineKey.Protect`. En producción se deberá configurar y conservar una estrategia estable de claves de máquina; cambiar las claves impediría recuperar tokens ya almacenados.
