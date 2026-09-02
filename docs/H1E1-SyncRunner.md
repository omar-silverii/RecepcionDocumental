# H1E1 — operación de SyncRunner

Ejecutable net48/x64 de una sola ejecución. No inicia IIS, timers, servicios ni tareas automáticamente. Usa `GmailSyncService.SynchronizeAsync("SCHEDULER")`; WebForms usa el mismo servicio con origen WEB.

## Despliegue y seguridad

Compilar primero WebForms Release|Any CPU y después `tools/RecepcionDocumental.SyncRunner/RecepcionDocumental.SyncRunner.csproj` Release. Desplegar el producto completo, incluido `bin`, dependencias nativas y `App_Data`. El runner recibe la raíz física del producto y configura un AppDomain con esa raíz, `PrivateBinPath=bin` y el Web.config existente. OCR y modelo encuentran sus archivos mediante la misma raíz; no se modifican.

La cuenta Windows de ejecución necesita .NET Framework 4.8, acceso a SQL mediante DefaultConnection, lectura del INI y MachineKey.config existentes, y permisos en las rutas operativas. Provisionar las mismas variables `RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_ID` y `RECEPCIONDOCUMENTAL_GOOGLE_CLIENT_SECRET` en el entorno de esa cuenta/tarea. No copiar secretos al script, argumentos o repositorio. No crear otro OAuth ni cambiar MachineKey. Antes de sincronizar, `--verify-config` prueba el descifrado existente sin llamadas a Gmail y sin imprimir el token.

El lock SQL `RecepcionDocumental:GmailSync`, Exclusive/Session, timeout cero, es global por base para cubrir incluso la selección de la cuenta activa antes de leer su cursor. Es más restrictivo que un lock por cuenta. Usa una conexión dedicada sin pooling ni reconexión automática, dispuesta en todos los caminos. La ejecución perdedora no lee cuenta/cursor ni entra al procesamiento; registra, si SQL permite, una auditoría omitida con cuenta nullable. La sesión web sigue siendo sólo una protección de UX.

La auditoría no revierte documentos ni cursor ya persistidos. Sin embargo, un fallo al crearla impide iniciar el procesamiento, y un fallo al cerrarla es un fallo operativo: `AUDIT_START_FAILED` / `AUDIT_FINALIZATION_FAILED`, código 1, nunca éxito silencioso. El cierre comprueba en SQL el estado final y FechaFinUtc; si falla, el coordinador intenta registrar FALLIDA y conserva la excepción primaria si ese segundo intento también falla. La liberación del lock no depende del reporting. DetalleError conserva únicamente el tipo de excepción. Un cierre abrupto o indisponibilidad persistente de SQL puede dejar una fila EJECUTANDO sin fin; no significa que el applock SQL siga retenido. Investigar proceso/SQL antes de intervenir. No borrar handles ni matar procesos como operación normal.

## Comandos

Desde la raíz de producto:

```powershell
& .\tools\RecepcionDocumental.SyncRunner\bin\RecepcionDocumental.SyncRunner.exe $PWD.Path --verify-config
& .\tools\RecepcionDocumental.SyncRunner\bin\RecepcionDocumental.SyncRunner.exe $PWD.Path
```

Exit codes: 0 COMPLETADA sin errores y auditoría confirmada; 10 OMITIDA_YA_EN_EJECUCION con auditoría cerrada; 1 COMPLETADA_CON_ERRORES, FALLIDA o fallo operativo de auditoría; 2 argumentos/configuración de arquitectura inválidos. `--probe-lock` sólo adquiere/libera el lock, sin Gmail ni modificación del cursor; sus códigos 0/10 son diagnósticos y no representan una sincronización auditada.

## Windows Task Scheduler — instalación explícita, no ejecutada por el probe

Ejecutar PowerShell como administrador sólo después de autorización de Omar:

```powershell
& .\tools\RecepcionDocumental.SyncRunner\Manage-ScheduledTask.ps1 -Action Install -ProductRoot $PWD.Path
& .\tools\RecepcionDocumental.SyncRunner\Manage-ScheduledTask.ps1 -Action Status
& .\tools\RecepcionDocumental.SyncRunner\Manage-ScheduledTask.ps1 -Action Uninstall
```

Instala una repetición cada cinco minutos, con working directory explícito, logon por credenciales (no requiere sesión interactiva) y política IgnoreNew. La contraseña se solicita interactivamente y se entrega a Windows, no se guarda en archivos. El script no reemplaza tareas existentes. SQL sigue siendo la autoridad de exclusión frente a web, invocaciones manuales u otras máquinas. `Get-ScheduledTaskInfo` informa LastTaskResult (10 no es error operativo). Revisar también GmailSyncEjecucion y logs Proc/Error. No se establece una terminación forzada por timeout durante una persistencia documental; investigar una ejecución excesivamente larga.

El usuario puede seguir usando “Buscar nuevos correos”; no desactiva la recepción externa. No se instala ninguna tarea durante certificación.
