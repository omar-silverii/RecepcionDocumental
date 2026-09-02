# Diagnóstico del proceso residual — 2026-09-02

Estado actual: H1D7C1 APROBADO; ProbeLifecycleClean=True. Se conserva debajo la cronología del diagnóstico, incluidos estados NO APROBADO anteriores al cierre.

- PID: 23360; nombre reportado: PdfRasterProbe.exe.
- Parent PID: 18744; no apareció en la consulta actual.
- CreationDate reportada: 2026-09-02 12:26:26 (hora local).
- CPU de usuario/kernel reportada por CIM: 0/0.
- WorkingSetSize: 131072 bytes; HandleCount: 0; threads visibles: 0.
- ExecutionState, ruta, command line y propietario: no disponibles.
- GetOwner: ReturnValue=2.
- OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION): ERROR_ACCESS_DENIED (5).
- No aparecieron hijos en la consulta Win32_Process.
- Apertura exclusiva ReadWrite de tools/PdfRasterProbe/bin/RecepcionDocumental.dll: rechazada por archivo en uso.
- No se pudo atribuir concluyentemente el bloqueo al PID ni confirmar su ruta exacta.

El historial de esta tarea contiene invocaciones síncronas mediante `&` y lectura de `$LASTEXITCODE`; no demuestra un lanzamiento abandonado mediante Start-Process. El probe inspeccionado espera ambos pares Task.Run con Task.WaitAll y no contiene fire-and-forget. Esto no demuestra por sí solo la causa del proceso residual.

No se detuvo ningún proceso. No se modificó código. Los tres ciclos build/run/build no se iniciaron porque no se cumplen las precondiciones de ausencia de proceso y DLL desbloqueada. Se requiere diagnóstico con privilegios suficientes para identificar el proceso y el propietario del bloqueo antes de terminarlo.

## Captura elevada posterior, 15:30–15:31 (-03:00)

PowerShell iniciado mediante UAC: `Administrator=True` comprobado. Herramientas descargadas exclusivamente desde Microsoft Sysinternals, firmas Authenticode válidas.

- Handle 5.0, búsqueda por ruta completa: `No matching handles found.`, exit code 1. Salida íntegra en `elevated-diagnostic-20260902/handle-full-path.txt`.
- PID 23360: ruta confirmada `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\PdfRasterProbe\bin\PdfRasterProbe.exe`.
- Command line confirma `--h1d7c1-ground-truth .\RecepcionDocumental.ini .\tools\DocumentAiProbe\experiments\H1D7C1`.
- Owner: `OMARD\omard`, GetOwner=0. Parent=18744, inicio=12:26:26. Threads=0, handles=0, HasExited=False; exit code no disponible.
- Apertura exclusiva de la DLL: sigue fallando por archivo en uso.
- ListDLLs 3.2 `-d RecepcionDocumental.dll`: sin coincidencias, pero con accesos denegados a varios procesos protegidos; NO demuestra ausencia global de referencias.
- ListDLLs sobre PID 23360: enumeró módulos, sin RecepcionDocumental.dll. La presencia de módulos de terceros no demuestra causalidad.
- Process Explorer elevado iniciado (PID 25968); pendiente captura de Find Handle or DLL, solicitada al usuario.

Clasificación provisional D: no se encontró handle de archivo y persiste el bloqueo; propietario aún NO identificado. No se atribuye a PID 23360 únicamente por confirmar que es un probe H1D7C1. No se terminaron procesos ni se cerraron handles. No se iniciaron builds ni ciclos de certificación. H1D7C1 NO APROBADO.

Transcripciones completas y resultados ListDLLs conservados en `elevated-diagnostic-20260902/`. No se modificó código productivo ni del probe durante este diagnóstico.

## Identificación, liberación y cierre, 15:35–15:37 (-03:00)

La captura aportada por el usuario de Process Explorer elevado muestra exactamente un resultado: `PdfRasterProbe.exe`, PID `23360`, tipo `DLL`, ruta completa `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\PdfRasterProbe\bin\RecepcionDocumental.dll`. Evidencia: `elevated-diagnostic-20260902/process-explorer-owner.png`. Es referencia de módulo, no un handle de archivo; no se inventa un número de handle.

Se revalidaron executable path, command line H1D7C1 y fecha de creación antes de terminar exclusivamente PID 23360 mediante Stop-Process elevado. Motivo: proceso de prueba abandonado identificado como propietario de la referencia DLL. CheckRemoteDebuggerPresent: consulta exitosa, debugger no detectado. No se demostró el mecanismo original que mantuvo el proceso residual.

PID 23360 desapareció. La captura de liberación se interrumpió después de mostrar `PdfRasterProbeCount=0` y `No matching handles found.` por el tratamiento de stderr de Handle en PowerShell. Esa interrupción del script no se contabiliza como fallo funcional del producto. Se repitieron las verificaciones al inicio de los ciclos: consulta CIM exitosa, cero probes, Handle sin coincidencias y apertura exclusiva exitosa.

Proceso residual confirmado, causa de origen no reproducida.

Los tres ciclos síncronos consecutivos terminaron con:

- 3/3 builds iniciales y 3/3 rebuilds posteriores: exit code 0.
- 3/3 probes: exit code 0, Gate=True.
- Antes y después de cada probe: PdfRasterProbeCount=0, Handle sin coincidencias y ExclusiveOpen=True.
- 0 MSB3026; 0 terminaciones o limpiezas manuales entre ciclos.
- Labels, Files, Idempotent, Concurrency, ConcurrentPhysical, PreexistingSafe, Atomic, ShadowOptional y Gmail=True en los tres ciclos.
- OrphanInvoiceFiles=0 en los tres ciclos; decisiones ganadoras con una fila ground truth vigente y Secuencia=1; fixture X sin decisión ni ground truth.
- ProbeLifecycleClean=True.

Residuo previo no reproducido bajo ejecución estándar síncrona. No se atribuye su origen a una fuga, antivirus u otro mecanismo sin evidencia.

Evidencia íntegra de los ciclos: `lifecycle-20260902/lifecycle.txt`, seis logs de build, seis búsquedas Handle, tres salidas de probe y sus directorios `cycle-1`, `cycle-2`, `cycle-3`. Los warnings MSB3277 preexistentes permanecen; no se corrigieron. Se preservaron las evidencias anteriores de atomicidad de migración 009 sin reejecutarla. Sin nuevos cambios en código productivo/probe, SQL/009, UI, Gmail, OCR, cadena visual, thresholds, modelo, dataset ni H1D9E1A; sin training ni tuning.
