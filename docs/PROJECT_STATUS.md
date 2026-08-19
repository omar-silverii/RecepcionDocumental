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

## Siguiente hito

La clasificación documental todavía no está implementada. Tampoco hay OCR ni IA.

## Configuración operativa y logs

La aplicación carga una vez `RecepcionDocumental.ini` desde la raíz física al iniciar. El INI define el nombre del proyecto y la ruta absoluta de logs; el archivo real está excluido de Git y se proporciona `RecepcionDocumental.ini.example`.

El logger centralizado genera archivos diarios `RecepcionDocumental_Proc_yyyyMMdd.txt` y `RecepcionDocumental_Error_yyyyMMdd.txt`. Las rutas operativas nuevas no deben hardcodearse y los logs nunca deben contener secretos, tokens, connection strings, cuerpos de correo ni contenido de adjuntos.

## OAuth local

Scope único: `https://www.googleapis.com/auth/gmail.readonly`.

Variables de entorno de Windows requeridas:

- `RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_ID`
- `RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_SECRET`

Los valores reales no deben guardarse en Git, `Web.config`, SQL, JSON ni logs. La URI registrada es `https://localhost:44320/Gmail_OAuthCallback.aspx`.

Los refresh tokens se protegen con `MachineKey.Protect`. En producción se deberá configurar y conservar una estrategia estable de claves de máquina; cambiar las claves impediría recuperar tokens ya almacenados.
