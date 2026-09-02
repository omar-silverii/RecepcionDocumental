$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot)
Set-Location -LiteralPath $repo
$out=Join-Path $PSScriptRoot 'experiments\H1D7C2'
Start-Transcript -Path (Join-Path $out 'development-certification.txt')
$config=[xml](Get-Content -Raw -LiteralPath (Join-Path $repo 'Web.config'))
$value=($config.configuration.connectionStrings.add | Where-Object name -eq 'DefaultConnection').connectionString
$builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($value)
if($builder.InitialCatalog -ne 'RecepcionDocumental' -or !$builder.IntegratedSecurity){throw 'Unexpected database or authentication configuration.'}
$sqlcmd='C:\Program Files\Microsoft SQL Server\110\Tools\Binn\SQLCMD.EXE'
$msbuild='C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe'
function ReadTable([string]$sql){
 $cn=New-Object System.Data.SqlClient.SqlConnection($value)
 $cmd=$cn.CreateCommand();$cmd.CommandText=$sql
 $adapter=New-Object System.Data.SqlClient.SqlDataAdapter($cmd);$table=New-Object System.Data.DataTable
 try{$cn.Open();[void]$adapter.Fill($table);return ,$table}finally{$adapter.Dispose();$cmd.Dispose();$cn.Dispose()}
}
function Fingerprint([string]$sql){
 $table=ReadTable $sql
 $stream=New-Object System.IO.MemoryStream
 try{$table.TableName='Snapshot';$table.WriteXml($stream,[System.Data.XmlWriteMode]::WriteSchema);$sha=[System.Security.Cryptography.SHA256]::Create();try{return [BitConverter]::ToString($sha.ComputeHash($stream.ToArray())).Replace('-','')}finally{$sha.Dispose()}}finally{$stream.Dispose()}
}
function Counts([string]$stage){
 $table=ReadTable "SELECT (SELECT COUNT(*) FROM dbo.DocumentoRecepcion WHERE Clasificacion=N'FACTURA' AND ResultadoRevision IS NULL) AS FacturaElegibles,(SELECT COUNT(*) FROM dbo.DocumentoRevisionMuestra) AS MuestrasSeleccionadas,(SELECT COUNT(*) FROM dbo.DocumentoRevisionMuestra WHERE DocumentoGroundTruthId IS NULL) AS MuestrasPendientes,(SELECT COUNT(*) FROM dbo.DocumentoGroundTruth) AS GroundTruthExistentes;"
 Write-Output $stage;$table | Format-Table -AutoSize | Out-Host
}
function Lifecycle {
 $count=@(Get-Process -Name PdfRasterProbe -ErrorAction SilentlyContinue).Count
 Write-Output "PdfRasterProbeCount=$count"
 $stream=[IO.File]::Open((Join-Path $repo 'tools\PdfRasterProbe\bin\RecepcionDocumental.dll'),'Open','ReadWrite','None');$stream.Dispose()
 Write-Output 'DllExclusiveOpen=True'
 if($count -ne 0){throw 'Residual probe detected; no process will be terminated.'}
}
try{
 $humanSql='SELECT Id,Clasificacion,ResultadoRevision,EtiquetaRevision,FechaRevisionUtc,UsuarioRevision,ObservacionRevision FROM dbo.DocumentoRecepcion ORDER BY Id;'
 $gtSql='SELECT * FROM dbo.DocumentoGroundTruth ORDER BY Id;'
 $beforeHuman=Fingerprint $humanSql;$beforeGt=Fingerprint $gtSql
 $migration=Join-Path $repo 'Database\010_MuestraFacturaHumana.sql'
 if((Get-FileHash -LiteralPath $migration -Algorithm SHA256).Hash -ne '6051BBA70A5EB0D0B6616576A3EC7FD532819CCD6B703D5DE39085FA54BA33B0'){throw '010 differs from isolated certified migration.'}
 for($run=1;$run -le 2;$run++){
  & $sqlcmd -S $builder.DataSource -E -d RecepcionDocumental -b -r 1 -i $migration -o (Join-Path $out "development-migration-$run.txt")
  $code=$LASTEXITCODE;Write-Output "MigrationRun=$run ExitCode=$code"
  Get-Content -LiteralPath (Join-Path $out "development-migration-$run.txt")
  if($code -ne 0){throw "Migration run $run failed."}
  Counts "AfterMigration$run"
  $sample=Fingerprint 'SELECT * FROM dbo.DocumentoRevisionMuestra ORDER BY Id;'
  if($run -eq 1){$firstSample=$sample}else{if($firstSample -ne $sample){throw 'Migration idempotence failed.'};Write-Output 'MigrationIdempotent=True'}
  if((Fingerprint $humanSql) -ne $beforeHuman -or (Fingerprint $gtSql) -ne $beforeGt){throw 'Historical decisions changed.'}
 }
 Write-Output 'HistoricalDecisionsUnchanged=True'
 Lifecycle
 & $msbuild RecepcionDocumental.sln /t:Build /p:Configuration=Release '/p:Platform=Any CPU' /m /v:quiet "/flp:logfile=$out\development-build-webforms.log;verbosity=normal"
 $code=$LASTEXITCODE;Write-Output "WebFormsBuildExitCode=$code";if($code -ne 0){throw 'WebForms build failed.'}
 & $msbuild tools\PdfRasterProbe\PdfRasterProbe.csproj /t:Build /p:Configuration=Release /m /v:quiet "/flp:logfile=$out\development-build-probe.log;verbosity=normal"
 $code=$LASTEXITCODE;Write-Output "ProbeBuildExitCode=$code";if($code -ne 0){throw 'Probe build failed.'}
 & .\tools\PdfRasterProbe\bin\PdfRasterProbe.exe --h1d7c2-sample .\RecepcionDocumental.ini $out
 $code=$LASTEXITCODE;Write-Output "ProbeExitCode=$code"
 Lifecycle
 Counts 'AfterProbeCleanup'
 if((Fingerprint $humanSql) -ne $beforeHuman -or (Fingerprint $gtSql) -ne $beforeGt){throw 'Historical decisions differ after probe cleanup.'}
 if($code -ne 0){throw 'Functional probe failed; stop without changes.'}
 Write-Output 'DevelopmentCertificationCompleted=True'
}catch{Write-Output ('STOP: '+$_.Exception.Message);exit 1}finally{Stop-Transcript}
