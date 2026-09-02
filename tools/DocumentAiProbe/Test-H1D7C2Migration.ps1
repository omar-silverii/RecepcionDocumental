param([switch]$ApplyProduction)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot)
$output=Join-Path $PSScriptRoot 'experiments\H1D7C2'
$log=Join-Path $output 'migration-evidence.txt'
Start-Transcript -Path $log
$temporary='H1D7C2_Atomic_'+[Guid]::NewGuid().ToString('N')
$created=$false
$config=[xml](Get-Content -Raw -LiteralPath (Join-Path $repo 'Web.config'))
$connectionValue=($config.configuration.connectionStrings.add | Where-Object name -eq 'DefaultConnection').connectionString
$builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($connectionValue)
$sourceDatabase=$builder.InitialCatalog
if($sourceDatabase -ne 'RecepcionDocumental'){throw 'Unexpected source database.'}
$sourcePath=Join-Path $repo 'Database\010_MuestraFacturaHumana.sql'
$sourceBytes=[System.IO.File]::ReadAllBytes($sourcePath)
$sourceSql=Get-Content -Raw -LiteralPath $sourcePath
$injection="THROW 51001,'H1D7C2 controlled rollback before commit',1;"
$commitPattern='(?im)^[\t ]*COMMIT[\t ]*;[\t ]*\r?$'
function AssertOriginalUnchanged {
 if([Convert]::ToBase64String([System.IO.File]::ReadAllBytes($sourcePath)) -cne [Convert]::ToBase64String($sourceBytes)){throw 'Original SQL bytes changed.'}
 Write-Output 'OriginalSqlByteIdentical=True'
}
function PrepareMigration([string]$database,[bool]$inject) {
 if($database -notmatch '^H1D7C2_Atomic_[a-f0-9]{32}$'){throw 'Only an isolated H1D7C2 database is allowed.'}
 $use='USE [RecepcionDocumental];'
 if([regex]::Matches($sourceSql,[regex]::Escape($use)).Count -ne 1){throw 'Expected unique USE statement not found.'}
 $text=$sourceSql.Replace($use,"USE [$database];")
 if($inject){
  $points=[regex]::Matches($text,$commitPattern)
  Write-Host ('LegacyMarkerMatches='+[regex]::Matches($sourceSql,[regex]::Escape('-- H1D7C2_ROLLBACK_TEST_POINT')).Count)
  Write-Host "InjectionPointMatches=$($points.Count)"
  if($points.Count -ne 1){throw 'Injection point not found exactly once.'}
  $point=$points[0]
  $begin=[regex]::Matches($text,'(?im)^[\t ]*BEGIN TRANSACTION[\t ]*;')
  $ddl=$text.IndexOf('CREATE TABLE dbo.DocumentoRevisionMuestra',[StringComparison]::Ordinal)
  if($begin.Count -ne 1 -or $begin[0].Index -ge $ddl -or $ddl -ge $point.Index){throw 'Injection point is not after DDL inside the main transaction.'}
  $altered=$text.Insert($point.Index,' '+$injection+"`r`n")
  $throws=[regex]::Matches($altered,[regex]::Escape($injection))
  if($throws.Count -ne 1){throw 'Injected THROW not found exactly once.'}
  $commit=[regex]::Matches($altered,$commitPattern)
  if($commit.Count -ne 1 -or $throws[0].Index -ge $commit[0].Index){throw 'Injected THROW does not precede COMMIT.'}
  if($altered -ceq $text -or $altered -ceq $sourceSql){throw 'Injection did not change SQL.'}
  AssertOriginalUnchanged | Out-Host
  Write-Host 'InjectedThrowCount=1; ThrowBeforeCommit=True; InsideTransactionAfterDDL=True; AlteredSqlDifferent=True'
  Write-Host 'AlteredSqlTail:'
  Write-Host $altered.Substring([Math]::Max(0,$throws[0].Index-60))
  return $altered
 }
 AssertOriginalUnchanged | Out-Host
 return $text
}
function Connection([string]$database) {
 $b=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($connectionValue)
 $b['Initial Catalog']=$database
 $cn=New-Object System.Data.SqlClient.SqlConnection($b.ConnectionString)
 $cn.Open()
 return $cn
}
function Execute([System.Data.SqlClient.SqlConnection]$cn,[string]$sql) {
 $cmd=$cn.CreateCommand();$cmd.CommandText=$sql;$cmd.CommandTimeout=60
 try{[void]$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}
}
function Query([System.Data.SqlClient.SqlConnection]$cn,[string]$sql) {
 $cmd=$cn.CreateCommand();$cmd.CommandText=$sql;$cmd.CommandTimeout=60
 $adapter=New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
 $table=New-Object System.Data.DataTable
 try{[void]$adapter.Fill($table);return ,$table}finally{$adapter.Dispose();$cmd.Dispose()}
}
function AssertSchema([System.Data.SqlClient.SqlConnection]$cn) {
 $columns=Query $cn "SELECT name,TYPE_NAME(system_type_id) AS TypeName,max_length,scale,is_nullable,is_identity FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.DocumentoRevisionMuestra') ORDER BY column_id;"
 $actual=($columns.Rows | ForEach-Object { '{0}:{1}:{2}:{3}:{4}:{5}' -f $_.name,$_.TypeName,$_.max_length,$_.scale,[int]$_.is_nullable,[int]$_.is_identity }) -join '|'
 $expected='Id:bigint:8:0:0:1|DocumentoRecepcionId:bigint:8:0:0:0|TipoMuestra:nvarchar:100:0:0:0|ReglaVersion:nvarchar:100:0:0:0|Modulo:int:4:0:0:0|Bucket:int:4:0:0:0|FechaSeleccionUtc:datetime2:6:0:0:0|DocumentoGroundTruthId:bigint:8:0:1:0|FechaResolucionUtc:datetime2:6:0:1:0'
 if($actual -cne $expected){throw "Migration column schema mismatch: $actual"}
 $state=Query $cn @"
SELECT
(SELECT COUNT(*) FROM sys.key_constraints k JOIN sys.index_columns ic ON ic.object_id=k.parent_object_id AND ic.index_id=k.unique_index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE k.parent_object_id=OBJECT_ID(N'dbo.DocumentoRevisionMuestra') AND ic.key_ordinal=1 AND ((k.name=N'PK_DocumentoRevisionMuestra' AND k.type=N'PK' AND c.name=N'Id') OR (k.name=N'UQ_Muestra_Documento' AND k.type=N'UQ' AND c.name=N'DocumentoRecepcionId'))) AS ValidKeys,
(SELECT COUNT(*) FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id WHERE f.parent_object_id=OBJECT_ID(N'dbo.DocumentoRevisionMuestra') AND f.is_disabled=0 AND f.is_not_trusted=0 AND COL_NAME(fc.referenced_object_id,fc.referenced_column_id)=N'Id' AND ((f.name=N'FK_Muestra_Documento' AND fc.referenced_object_id=OBJECT_ID(N'dbo.DocumentoRecepcion') AND COL_NAME(fc.parent_object_id,fc.parent_column_id)=N'DocumentoRecepcionId') OR (f.name=N'FK_Muestra_GroundTruth' AND fc.referenced_object_id=OBJECT_ID(N'dbo.DocumentoGroundTruth') AND COL_NAME(fc.parent_object_id,fc.parent_column_id)=N'DocumentoGroundTruthId'))) AS ValidForeignKeys,
(SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.DocumentoRevisionMuestra') AND name IN(N'CK_Muestra_Tipo',N'CK_Muestra_Modulo',N'CK_Muestra_Bucket',N'CK_Muestra_Resolucion') AND is_disabled=0 AND is_not_trusted=0) AS ValidChecks,
(SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DocumentoRevisionMuestra')) AS IndexCount,
(SELECT COUNT(*) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id WHERE i.object_id=OBJECT_ID(N'dbo.DocumentoRevisionMuestra') AND i.name=N'IX_Muestra_Pendientes' AND i.has_filter=0 AND i.is_unique=0 AND i.is_disabled=0 AND ((ic.key_ordinal=1 AND COL_NAME(ic.object_id,ic.column_id)=N'DocumentoGroundTruthId') OR (ic.key_ordinal=2 AND COL_NAME(ic.object_id,ic.column_id)=N'Id') OR (ic.is_included_column=1 AND COL_NAME(ic.object_id,ic.column_id)=N'DocumentoRecepcionId'))) AS PendingIndexColumns,
(SELECT COUNT(*) FROM sys.check_constraints WHERE name=N'CK_DocumentoGroundTruth_Fuente' AND is_disabled=0 AND is_not_trusted=0 AND definition LIKE N'%MUESTREO_FACTURA_CIEGO%' AND definition LIKE N'%REVISION_OPERATIVA%' AND definition LIKE N'%MIGRACION_REVISION_EXISTENTE%') AS SourceCheck,
OBJECTPROPERTY(OBJECT_ID(N'dbo.H1D7C2Bucket'),N'IsSchemaBound') AS BoundFunction,
(SELECT COUNT(*) FROM dbo.DocumentoRevisionMuestra s JOIN dbo.DocumentoRecepcion d ON d.Id=s.DocumentoRecepcionId WHERE s.TipoMuestra=N'FACTURA_AUTOMATICA' AND s.ReglaVersion=N'H1D7C2-V1' AND s.Modulo=10 AND s.Bucket=0 AND s.FechaSeleccionUtc IS NOT NULL AND s.DocumentoGroundTruthId IS NULL AND s.FechaResolucionUtc IS NULL AND d.Clasificacion=N'FACTURA' AND d.ResultadoRevision IS NULL AND dbo.H1D7C2Bucket(d.HashSha256)=s.Bucket) AS ValidSampleRows;
"@
 $r=$state.Rows[0]
 if($r.ValidKeys -ne 2 -or $r.ValidForeignKeys -ne 2 -or $r.ValidChecks -ne 4 -or $r.IndexCount -ne 3 -or $r.PendingIndexColumns -ne 3 -or $r.SourceCheck -ne 1 -or $r.BoundFunction -ne 1 -or $r.ValidSampleRows -ne 1){throw ('Migration schema contract failed: '+($state | Out-String))}
 Write-Output 'CompleteSchema=True; Columns=9; Keys=2; ForeignKeys=2; Checks=4; Indexes=3; PendingIndexUnfiltered=True; SourceCheckPreservedAndExtended=True; BoundBucketFunction=True'
}
function Migration([string]$database,[bool]$inject) {
 $text=PrepareMigration $database $inject
 $cn=Connection $database
 try{
  foreach($batch in [regex]::Split($text,'(?im)^GO\s*$')){
   if(![string]::IsNullOrWhiteSpace($batch)){
    # Fill(DataTable) only handles one result table. Drain every result to observe errors after SELECT.
    $cmd=$cn.CreateCommand();$cmd.CommandText=$batch;$cmd.CommandTimeout=60
    $reader=$null
    try{
     $reader=$cmd.ExecuteReader()
     do{
      while($reader.Read()){
       $fields=for($i=0;$i -lt $reader.FieldCount;$i++){ $reader.GetName($i)+'='+[string]$reader.GetValue($i) }
       Write-Output ($fields -join '; ')
      }
     }while($reader.NextResult())
    }finally{if($reader){$reader.Dispose()};$cmd.Dispose()}
   }
  }
 }finally{$cn.Dispose()}
}
try {
 if($ApplyProduction){throw 'Production application is disabled for this isolated gate.'}
 # Validate in memory before creating a database or executing any SQL.
 $null=PrepareMigration $temporary $true
 Write-Output ('OriginalSqlSHA256='+(Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash)
 $master=Connection 'master'
 try{Execute $master "CREATE DATABASE [$temporary];";$created=$true}finally{$master.Dispose()}
 $cn=Connection $temporary
 try{
  Execute $cn @"
SELECT TOP(0) * INTO dbo.DocumentoRecepcion FROM [RecepcionDocumental].dbo.DocumentoRecepcion;
ALTER TABLE dbo.DocumentoRecepcion ADD CONSTRAINT PK_TempDocumento PRIMARY KEY(Id);
SELECT TOP(0) * INTO dbo.DocumentoGroundTruth FROM [RecepcionDocumental].dbo.DocumentoGroundTruth;
ALTER TABLE dbo.DocumentoGroundTruth ADD CONSTRAINT PK_TempGroundTruth PRIMARY KEY(Id);
ALTER TABLE dbo.DocumentoGroundTruth ADD CONSTRAINT CK_DocumentoGroundTruth_Fuente CHECK(Fuente IN(N'REVISION_OPERATIVA',N'MIGRACION_REVISION_EXISTENTE'));
INSERT dbo.DocumentoRecepcion(GmailMensajeId,GmailPartId,OrigenTipo,OrigenHash,NombreOriginal,TamanioBytes,RutaLocal,HashSha256,Clasificacion,MetodoDeteccion,QrDetectado,FechaClasificacionUtc,FechaAltaUtc,ResultadoRevision)
SELECT 1,N'fixture',N'DIRECTO',REPLICATE('0',64),N'fixture',0,N'fixture',REPLICATE('0',64),N'FACTURA',N'FIXTURE',0,SYSUTCDATETIME(),SYSUTCDATETIME(),NULL UNION ALL
SELECT 1,N'fixture2',N'DIRECTO',REPLICATE('0',64),N'fixture',0,N'fixture',REPLICATE('0',64),N'REVISAR',N'FIXTURE',0,SYSUTCDATETIME(),SYSUTCDATETIME(),NULL UNION ALL
SELECT 1,N'fixture3',N'DIRECTO',REPLICATE('0',64),N'fixture',0,N'fixture',REPLICATE('0',64),N'FACTURA',N'FIXTURE',0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'FACTURA' UNION ALL
SELECT 1,N'fixture4',N'DIRECTO',REPLICATE('1',64),N'fixture',0,N'fixture','00000001'+REPLICATE('0',56),N'FACTURA',N'FIXTURE',0,SYSUTCDATETIME(),SYSUTCDATETIME(),NULL;
"@
  $baseline=Query $cn "SELECT definition,is_disabled,is_not_trusted FROM sys.check_constraints WHERE name=N'CK_DocumentoGroundTruth_Fuente';"
  $originalCheck=[string]$baseline.Rows[0].definition
 }finally{$cn.Dispose()}
 $injected=$false
 try{Migration $temporary $true}catch{
  $errorObject=$_.Exception
  while($errorObject -and $errorObject -isnot [System.Data.SqlClient.SqlException]){$errorObject=$errorObject.InnerException}
  if($errorObject -and @($errorObject.Errors | Where-Object {$_.Number -eq 51001 -and $_.Message -eq 'H1D7C2 controlled rollback before commit'}).Count -eq 1){
   $injected=$true;Write-Output 'ObservedSqlError=51001; H1D7C2 controlled rollback before commit'
  }else{throw}
 }
 if(!$injected){throw 'Expected injected failure did not occur.'}
 Write-Output 'InjectedFailureObserved=True'
 $cn=Connection $temporary
 try{
  $state=Query $cn @"
SELECT (SELECT COUNT(*) FROM sys.objects WHERE name IN(N'DocumentoRevisionMuestra',N'H1D7C2Bucket',N'PK_DocumentoRevisionMuestra',N'FK_Muestra_Documento',N'FK_Muestra_GroundTruth',N'UQ_Muestra_Documento',N'CK_Muestra_Tipo',N'CK_Muestra_Modulo',N'CK_Muestra_Bucket',N'CK_Muestra_Resolucion')) AS ObjectsRemaining,
(SELECT COUNT(*) FROM sys.indexes WHERE name IN(N'IX_Muestra_Pendientes',N'PK_DocumentoRevisionMuestra',N'UQ_Muestra_Documento')) AS IndexesRemaining,
(SELECT COUNT(*) FROM dbo.DocumentoGroundTruth) AS GroundTruthRows,
(SELECT COUNT(*) FROM dbo.DocumentoRecepcion) AS DocumentRows;
"@
  $restored=Query $cn "SELECT definition,is_disabled,is_not_trusted FROM sys.check_constraints WHERE name=N'CK_DocumentoGroundTruth_Fuente';"
  if($state.Rows[0].ObjectsRemaining -ne 0 -or $state.Rows[0].IndexesRemaining -ne 0 -or $state.Rows[0].GroundTruthRows -ne 0 -or $state.Rows[0].DocumentRows -ne 4 -or $restored.Rows.Count -ne 1 -or $restored.Rows[0].definition -cne $originalCheck -or $restored.Rows[0].is_disabled -or $restored.Rows[0].is_not_trusted){throw 'Migration rollback not clean.'}
  Write-Output 'RollbackObjects=0; RollbackIndexes=0; OriginalCheckRestoredExactly=True; PartialBackfill=0 (sample table absent); GroundTruthRows=0; DocumentsUnchanged=4'
  Write-Output 'MigrationRollbackClean=True'
 }finally{$cn.Dispose()}
 Migration $temporary $false
 Write-Output 'OriginalMigrationFirstExitCode=0'
 $cn=Connection $temporary
 try{
  AssertSchema $cn
  $first=Query $cn 'SELECT * FROM dbo.DocumentoRevisionMuestra ORDER BY Id;'
  $firstSnapshot=($first.Rows | ForEach-Object {($_.ItemArray | ForEach-Object {if($_ -is [DateTime]){$_.ToString('o')}else{[string]$_}}) -join '|'}) -join "`n"
 }finally{$cn.Dispose()}
 Migration $temporary $false
 Write-Output 'OriginalMigrationSecondExitCode=0'
 $cn=Connection $temporary
 try{
  $state=Query $cn "SELECT COUNT(*) AS Samples,COUNT(DISTINCT DocumentoRecepcionId) AS DistinctDocuments FROM dbo.DocumentoRevisionMuestra;"
  if($state.Rows[0].Samples -ne 1 -or $state.Rows[0].DistinctDocuments -ne 1){throw 'Backfill fixture or idempotence failed.'}
  AssertSchema $cn
  $second=Query $cn 'SELECT * FROM dbo.DocumentoRevisionMuestra ORDER BY Id;'
  $secondSnapshot=($second.Rows | ForEach-Object {($_.ItemArray | ForEach-Object {if($_ -is [DateTime]){$_.ToString('o')}else{[string]$_}}) -join '|'}) -join "`n"
  if($firstSnapshot -cne $secondSnapshot){throw 'Second migration changed sample rows.'}
  Write-Output 'SampleRowsIdentical=True'
  Write-Output 'MigrationIdempotent=True; TemporaryBackfill=1/2; ReviewedExcluded=1; ReviewExcluded=1'
 }finally{$cn.Dispose()}
 AssertOriginalUnchanged
 Write-Output 'MigrationGate=True'
}catch{Write-Output ('FAIL: '+$_.Exception.Message);exit 1}
finally{
 if($created){
  if($temporary -notmatch '^H1D7C2_Atomic_[a-f0-9]{32}$'){throw 'Unsafe temporary database name.'}
  [System.Data.SqlClient.SqlConnection]::ClearAllPools()
  $master=Connection 'master'
  try{Execute $master "ALTER DATABASE [$temporary] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$temporary];";Write-Output 'TemporaryDatabaseRemoved=True'}finally{$master.Dispose()}
 }
 Stop-Transcript
}
