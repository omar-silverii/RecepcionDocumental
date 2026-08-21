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
- La bandeja y el detalle Gmail priorizan `DocumentoRecepcion` para mensajes H1D y mantienen el fallback histórico a `GmailAdjunto` sin sumar ambos modelos.
- No se implementaron OCR, QR, IA, ML, rasterización ni integración ARCA.

Validación funcional real completada para adjuntos directos, ZIP, RAR y ZIP anidado dentro de RAR. El soporte 7Z está implementado y queda pendiente de validación física con un archivo real 7Z.

## Configuración operativa y logs

La aplicación carga una vez `RecepcionDocumental.ini` desde la raíz física al iniciar. El INI define el nombre del proyecto, las rutas absolutas `Logs`, `Trabajo`, `Facturas` y `Revisar`, y los límites ZIP. El archivo real está excluido de Git y se proporciona `RecepcionDocumental.ini.example`.

El logger centralizado genera archivos diarios `RecepcionDocumental_Proc_yyyyMMdd.txt` y `RecepcionDocumental_Error_yyyyMMdd.txt`. Las rutas operativas nuevas no deben hardcodearse y los logs nunca deben contener secretos, tokens, connection strings, cuerpos de correo ni contenido de adjuntos.

## OAuth local

Scope único: `https://www.googleapis.com/auth/gmail.readonly`.

Variables de entorno de Windows requeridas:

- `RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_ID`
- `RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_SECRET`

Los valores reales no deben guardarse en Git, `Web.config`, SQL, JSON ni logs. La URI registrada es `https://localhost:44320/Gmail_OAuthCallback.aspx`.

Los refresh tokens se protegen con `MachineKey.Protect`. En producción se deberá configurar y conservar una estrategia estable de claves de máquina; cambiar las claves impediría recuperar tokens ya almacenados.
