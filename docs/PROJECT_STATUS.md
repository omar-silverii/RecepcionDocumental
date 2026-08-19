# Estado de RecepcionDocumental

## Arquitectura

Aplicación ASP.NET WebForms en C# para .NET Framework 4.8, Visual Studio 2022 y SQL Server. El acceso a datos usa ADO.NET mediante `DefaultConnection`.

## Hitos

- H1A: validado. Interfaz inicial y tablas `GmailCuenta`, `GmailMensaje` y `GmailAdjunto`.
- H1B: validado. OAuth de servidor para Google Workspace/Gmail API, conexión y reconexión de cuenta.
- H1C: implementado, pendiente de validación manual. La primera sincronización consulta adjuntos de los últimos 30 días con máximo 100 mensajes; las siguientes usan `historyId` como cursor incremental y hacen fallback controlado si vence.
- Los adjuntos se guardan fuera del sitio en la carpeta configurable `AdjuntosRootPath` (localmente `C:\RecepcionDocumental\Adjuntos\`).
- No hay clasificación, OCR ni IA.

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
