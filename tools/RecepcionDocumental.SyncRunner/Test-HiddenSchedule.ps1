[CmdletBinding()]
param([int]$Seconds=720)
$ErrorActionPreference='Stop'
$events=New-Object 'System.Collections.Generic.List[object]'
$seen=@{}
$start=[DateTime]::UtcNow
$names=@('RecepcionDocumental.SyncLauncher','RecepcionDocumental.SyncRunner','PdfRasterProbe','conhost')
foreach($name in $names){foreach($p in [Diagnostics.Process]::GetProcessesByName($name)){$seen[$p.Id]=$true;$p.Dispose()}}
 $until=(Get-Date).AddSeconds($Seconds)
 while((Get-Date) -lt $until){
  foreach($name in $names){foreach($p in [Diagnostics.Process]::GetProcessesByName($name)){
   try{if(!$seen.ContainsKey($p.Id)){
    $seen[$p.Id]=$true
    $detail=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" -ErrorAction SilentlyContinue
    $row=[pscustomobject]@{Utc=[DateTime]::UtcNow.ToString('o');Name=$name;Pid=$p.Id;ParentPid=$detail.ParentProcessId;Path=$detail.ExecutablePath;MainWindowHandle=$p.MainWindowHandle.ToInt64()}
    $events.Add($row);$row|ConvertTo-Json -Compress
   }}finally{$p.Dispose()}
  }}
  Start-Sleep -Milliseconds 25
 }
$dir=Join-Path $PSScriptRoot '..\DocumentAiProbe\experiments\H1D9F'
[void](New-Item -ItemType Directory -Path $dir -Force)
$events|Export-Csv -LiteralPath (Join-Path $dir 'process-starts.csv') -NoTypeInformation
"ObservationStartUtc=$($start.ToString('o')); ObservationEndUtc=$([DateTime]::UtcNow.ToString('o'))"
"Method=25ms polling; very short-lived processes may be missed; this does not replace human visual confirmation."
