param([string]$BankRun='C:\Users\omard\Desktop\Facturas_Extraidas\20260907-075438-eeeff48ec3834f7493300c6c6f48fa01')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$bin=Join-Path $PSScriptRoot 'bin'
# A resumed preparation must never replace the original integrity baseline.
if (Test-Path (Join-Path $PSScriptRoot 'bank-input.json')) {
    foreach($r in @(Import-Csv (Join-Path $PSScriptRoot 'originals-before.csv'))){
        $f=Get-Item -LiteralPath $r.OriginalPath
        if($f.Length -ne [long]$r.SizeBytes -or $f.LastWriteTimeUtc.ToString('o') -ne $r.LastWriteUtc -or (Get-FileHash -LiteralPath $f.FullName).Hash -ne $r.Sha256){throw 'Original integrity failed on resume'}
    }
    foreach($r in @(Import-Csv (Join-Path $PSScriptRoot 'protected-before.csv'))){if((Get-FileHash -LiteralPath $r.Path).Hash -ne $r.Sha256){throw 'Protected corpus/model changed on resume'}}
    foreach($r in @(Get-Content (Join-Path $PSScriptRoot 'bank-input.json') -Raw|ConvertFrom-Json)){if((Get-FileHash -LiteralPath $r.ExtractedPath).Hash -ne $r.Sha256){throw 'Extract changed on resume'}}
    Write-Output 'Existing inputs verified and preserved. Resume missing records with bin/PrepareBank.exe and the existing shard TSVs. Do not recreate a sealed manifest.'
    return
}
foreach($d in @('bin','assets','text','raw','work')){[void][IO.Directory]::CreateDirectory((Join-Path $PSScriptRoot $d))}
$csc='C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe'
$refs=@('RecepcionDocumental','Microsoft.ML.OnnxRuntime','Newtonsoft.Json','System.Memory','System.Buffers','System.Runtime.CompilerServices.Unsafe','System.Numerics.Vectors')|ForEach-Object {'/reference:'+(Join-Path $repo ('bin\'+$_+'.dll'))}
$facade='C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll'
& $csc /nologo /target:library /platform:x64 ('/out:'+(Join-Path $bin 'VisualNormalization.dll')) @refs ('/reference:'+$facade) (Join-Path $repo 'Services\VisualInvoiceShadowService.cs')
if($LASTEXITCODE -ne 0){throw 'Normalization compilation failed'}
& $csc /nologo /target:exe /platform:x64 ('/out:'+(Join-Path $bin 'PrepareBank.exe')) @refs ('/reference:'+$facade) /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'PrepareBank.cs')
if($LASTEXITCODE -ne 0){throw 'Adapter compilation failed'}
$rows=@(Import-Csv (Join-Path $BankRun 'dmf-inventory.csv'))
if(@($rows|Where-Object ExtractionStatus -ne 'OK').Count){throw 'Bank extraction errors'}
$review=@(Import-Csv (Join-Path $BankRun 'review-index.csv'))
$baseline=@(Import-Csv (Join-Path $BankRun 'source-baseline.csv'))
foreach($r in $baseline){$f=Get-Item -LiteralPath $r.OriginalPath;if($f.Length -ne [long]$r.SizeBytes -or $f.LastWriteTimeUtc.ToString('o') -ne $r.LastWriteUtc -or (Get-FileHash -LiteralPath $f.FullName).Hash -ne $r.Sha256){throw 'Original integrity failed'}}
$baseline|Export-Csv (Join-Path $PSScriptRoot 'originals-before.csv') -NoTypeInformation -Encoding UTF8
foreach($r in $rows){if((Get-FileHash -LiteralPath $r.ExtractedPath).Hash -ne $r.Sha256){throw 'Extract integrity failed'}}
$rows|ConvertTo-Json -Depth 5|Set-Content (Join-Path $PSScriptRoot 'bank-input.json') -Encoding UTF8
$observed=@($review|Where-Object { [int]$_.CandidateId.Substring(1) -le 200 }|Select-Object -ExpandProperty Sha256 -Unique)
$observed|Set-Content (Join-Path $PSScriptRoot 'observed-batch-hashes.txt')
$unique=@($rows|Group-Object Sha256|Sort-Object Name|ForEach-Object {$_.Group|Sort-Object ExtractedPath|Select-Object -First 1})
for($shard=0;$shard -lt 4;$shard++){$lines=@();for($i=$shard;$i -lt $unique.Count;$i+=4){$lines+=($unique[$i].Sha256+"`t"+$unique[$i].ExtractedPath)};$lines|Set-Content (Join-Path $PSScriptRoot "shard-$shard.tsv") -Encoding UTF8}
$ds=Join-Path $repo 'tools\DocumentAiProbe\dataset.csv';$gt=[Collections.Generic.List[object]]::new()
foreach($r in @(Import-Csv $ds)){$path=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($ds)) $r.Path));if((Get-FileHash -LiteralPath $path).Hash -ne $r.Sha256){throw 'Ground truth hash mismatch'};$gt.Add([pscustomobject]@{Sha256=$r.Sha256;Label=$r.Label;GroupId=$r.GroupId;Path=$path;Evidence='dataset.csv + verified content';Split=$r.Split})}
foreach($label in @('FACTURA','OTRO_DOCUMENTO','NO_DOCUMENTO')){foreach($f in @(Get-ChildItem (Join-Path $repo "tools\DocumentAiProbe\Corpus\$label") -File -Recurse)){$sha=(Get-FileHash -LiteralPath $f.FullName).Hash;$gt.Add([pscustomobject]@{Sha256=$sha;Label=$label;GroupId='';Path=$f.FullName;Evidence='existing canonical corpus folder';Split=''})}}
$gt|ConvertTo-Json -Depth 4|Set-Content (Join-Path $PSScriptRoot 'existing-ground-truth.json') -Encoding UTF8
$protected=@($ds,(Join-Path $repo 'App_Data\DocumentAi\Models\H1D9B-CANDIDATE-001\candidate.onnx'),(Join-Path $repo 'App_Data\DocumentAi\Models\H1D9B-CANDIDATE-001\runtime-manifest.json'))+@($gt.Path|Sort-Object -Unique)
$protected|ForEach-Object {$f=Get-Item -LiteralPath $_;[pscustomobject]@{Path=$f.FullName;Sha256=(Get-FileHash -LiteralPath $f.FullName).Hash;SizeBytes=$f.Length}}|Export-Csv (Join-Path $PSScriptRoot 'protected-before.csv') -NoTypeInformation -Encoding UTF8
# Historical preprocessing hashes only. Labels/scores in old manifests are not used.
$assets=@(Import-Csv (Join-Path $repo 'tools\DocumentAiProbe\experiments\H1D9B\asset-manifest.csv'))+@(Import-Csv (Join-Path $repo 'tools\DocumentAiProbe\experiments\H1D9C\test-asset-manifest.csv'))
$source=@{};Import-Csv (Join-Path $repo 'tools\DocumentAiProbe\experiments\H1D9D1C\source-rgb-parity.csv')|ForEach-Object {$source[$_.Sha256]=$_.PythonHash}
$target=@{};Import-Csv (Join-Path $repo 'tools\DocumentAiProbe\experiments\H1D9D1C\target-rgb-parity.csv')|ForEach-Object {$target[$_.Sha256]=$_.PythonHash}
$tensor=@{};Import-Csv (Join-Path $repo 'tools\DocumentAiProbe\experiments\H1D9D1C\tensor-parity.csv')|ForEach-Object {$tensor[$_.Sha256]=$_.PythonHash}
$assets|ForEach-Object {$_.Sha256+"`t"+$_.VisualAssetPath+"`t"+$source[$_.Sha256]+"`t"+$target[$_.Sha256]+"`t"+$tensor[$_.Sha256]}|Set-Content (Join-Path $PSScriptRoot 'regression.tsv') -Encoding UTF8
($review|Where-Object CandidateId -eq 'C000096').ExtractedPath|Set-Content (Join-Path $PSScriptRoot 'regression-tiff.txt') -NoNewline
$env:PATH=(Join-Path $repo 'bin\x64')+';'+(Join-Path $repo 'bin')+';'+$env:PATH
& (Join-Path $bin 'PrepareBank.exe') $repo $PSScriptRoot regression (Join-Path $PSScriptRoot 'regression.tsv')
if($LASTEXITCODE -ne 0){throw 'Normalization regression failed'}
Write-Output "Prepared inputs for $($unique.Count) unique hashes. Run the four shard TSVs with bin/PrepareBank.exe, then FinalizeBank.py."
