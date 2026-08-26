param(
    [string]$Source = 'C:\temp\Pruebas\Facturas',
    [string]$OutputRoot = 'C:\temp\Pruebas\Facturas\_Extraidas',
    [string]$RendererBin = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Web

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$run = Join-Path $OutputRoot ($stamp + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
[IO.Directory]::CreateDirectory($run) | Out-Null
$documents = Join-Path $run 'Documentos'
$thumbs = Join-Path $run 'Miniaturas'
[IO.Directory]::CreateDirectory($documents) | Out-Null
[IO.Directory]::CreateDirectory($thumbs) | Out-Null

$rendererReady = $false
if ($RendererBin) {
    $resolvedRenderer = [IO.Path]::GetFullPath($RendererBin)
    $env:PATH = (Join-Path $resolvedRenderer 'x64') + ';' + $resolvedRenderer + ';' + $env:PATH
    try {
        [Reflection.Assembly]::LoadFrom((Join-Path $resolvedRenderer 'SkiaSharp.dll')) | Out-Null
        [Reflection.Assembly]::LoadFrom((Join-Path $resolvedRenderer 'PDFtoImage.dll')) | Out-Null
        $rendererReady = $true
    } catch { $rendererReady = $false }
}

$inventory = [Collections.Generic.List[object]]::new()
$review = [Collections.Generic.List[object]]::new()
$dmfs = @(Get-ChildItem -LiteralPath $Source -Filter '*.dmf' -File | Sort-Object Name)
$validCount = 0; $invalidCount = 0; $candidate = 0
foreach ($dmf in $dmfs) {
    $archive = $null; $valid = $false
    try {
        $header = [byte[]]::new(4); $stream = [IO.File]::OpenRead($dmf.FullName)
        try { if ($stream.Read($header,0,4) -lt 4 -or $header[0] -ne 0x50 -or $header[1] -ne 0x4b) { throw 'Firma ZIP PK ausente.' } } finally { $stream.Dispose() }
        $archive = [IO.Compression.ZipFile]::OpenRead($dmf.FullName); $valid = $true; $validCount++
        $dmfFolder = Join-Path $documents ([IO.Path]::GetFileNameWithoutExtension($dmf.Name))
        [IO.Directory]::CreateDirectory($dmfFolder) | Out-Null
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) { continue }
            $entryError = '' ; $extracted = '' ; $hash = ''
            try {
                if ([IO.Path]::IsPathRooted($entry.FullName)) { throw 'Ruta interna absoluta rechazada.' }
                $target = [IO.Path]::GetFullPath((Join-Path $dmfFolder $entry.FullName))
                $rootPrefix = [IO.Path]::GetFullPath($dmfFolder).TrimEnd('\') + '\'
                if (-not $target.StartsWith($rootPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Ruta interna fuera del destino rechazada.' }
                if (-not $seen.Add($target)) { throw 'Nombre interno duplicado rechazado.' }
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
                $input = $entry.Open(); $output = [IO.File]::Open($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
                try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
                $extracted = $target; $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
                $extension = [IO.Path]::GetExtension($target).ToLowerInvariant()
                if ($extension -in @('.pdf','.jpg','.jpeg','.png')) {
                    $candidate++; $thumb = $null; $thumbError = ''
                    if ($extension -eq '.pdf') {
                        if ($rendererReady) { try { $thumb=Join-Path $thumbs (('C{0:D4}.png' -f $candidate)); [PDFtoImage.Conversion]::SavePng($thumb,[IO.File]::ReadAllBytes($target),0,$null,[PDFtoImage.RenderOptions]::new(120)) } catch { $thumb=$null; $thumbError='Miniatura PDF no disponible: '+$_.Exception.Message } }
                        else { $thumbError='Renderer PDF no disponible.' }
                    } else { $thumb=$target }
                    $review.Add([pscustomobject]@{CandidateId=('C{0:D4}' -f $candidate);Dmf=$dmf.Name;Document=$entry.FullName;Extension=$extension;Extracted=$target;Thumbnail=$thumb;ThumbnailError=$thumbError})
                }
            } catch { $entryError=$_.Exception.Message }
            $inventory.Add([pscustomobject]@{DmfName=$dmf.Name;DmfOriginalPath=$dmf.FullName;DmfSize=$dmf.Length;ZipValid='SI';InternalFile=$entry.FullName;InternalExtension=[IO.Path]::GetExtension($entry.FullName).ToLowerInvariant();InternalSize=$entry.Length;Sha256=$hash;ExtractedPath=$extracted;Error=$entryError})
        }
    } catch {
        $invalidCount++
        $inventory.Add([pscustomobject]@{DmfName=$dmf.Name;DmfOriginalPath=$dmf.FullName;DmfSize=$dmf.Length;ZipValid='NO';InternalFile='';InternalExtension='';InternalSize=0;Sha256='';ExtractedPath='';Error=$_.Exception.Message})
    } finally { if ($archive) { $archive.Dispose() } }
}

$csv = Join-Path $run 'dmf-inventory.csv'; $inventory | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
$files = @($inventory | Where-Object { $_.ExtractedPath })
$unique = @($files.Sha256 | Sort-Object -Unique).Count
$duplicateGroups = @($files | Group-Object Sha256 | Where-Object Count -gt 1)
function CountExt([string[]]$extensions) { @($files | Where-Object { $_.InternalExtension -in $extensions }).Count }
$summary = [pscustomobject]@{RunDirectory=$run;DmfFound=$dmfs.Count;DmfValid=$validCount;DmfInvalid=$invalidCount;InternalFiles=$files.Count;PDF=(CountExt @('.pdf'));JpgJpeg=(CountExt @('.jpg','.jpeg'));PNG=(CountExt @('.png'));XML=(CountExt @('.xml'));TXT=(CountExt @('.txt'));Other=@($files | Where-Object {$_.InternalExtension -notin @('.pdf','.jpg','.jpeg','.png','.xml','.txt')}).Count;UniqueHashes=$unique;ExactDuplicateFiles=($files.Count-$unique);ExactDuplicateGroups=$duplicateGroups.Count;PotentiallyUseful=@($files|Where-Object{$_.InternalExtension-in @('.pdf','.jpg','.jpeg','.png')}).Count}
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'dmf-summary.json') -Encoding UTF8

$html = [Text.StringBuilder]::new(); [void]$html.AppendLine('<!doctype html><html lang="es"><head><meta charset="utf-8"><title>DMF review</title><style>body{font:14px Segoe UI,Arial;background:#f4f6f8;color:#17202a;margin:24px}h1{margin-bottom:4px}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:16px}.card{background:white;border:1px solid #d8dee4;border-radius:8px;padding:12px}.thumb{height:220px;display:flex;align-items:center;justify-content:center;background:#eef1f4;overflow:hidden}.thumb img{max-width:100%;max-height:100%}.meta{overflow-wrap:anywhere;margin-top:8px}.error{color:#a33}</style></head><body>')
[void]$html.AppendLine('<h1>Revisión visual DMF</h1><p>Sin clasificación automática. CandidateId es sólo trazabilidad.</p><div class="grid">')
foreach($item in $review){$rel=$null;if($item.Thumbnail){$rel=[IO.Path]::GetRelativePath($run,$item.Thumbnail).Replace('\','/')}[void]$html.Append('<article class="card"><div class="thumb">');if($rel){[void]$html.Append('<img loading="lazy" src="'+[Web.HttpUtility]::HtmlAttributeEncode($rel)+'">')}else{[void]$html.Append('<span>Sin miniatura</span>')}[void]$html.Append('</div><div class="meta"><b>'+[Web.HttpUtility]::HtmlEncode($item.CandidateId)+'</b><br>'+[Web.HttpUtility]::HtmlEncode($item.Dmf)+'<br>'+[Web.HttpUtility]::HtmlEncode($item.Document)+'<br>'+[Web.HttpUtility]::HtmlEncode($item.Extension));if($item.ThumbnailError){[void]$html.Append('<div class="error">'+[Web.HttpUtility]::HtmlEncode($item.ThumbnailError)+'</div>')}[void]$html.AppendLine('</div></article>')}
[void]$html.AppendLine('</div></body></html>'); $reviewPath=Join-Path $run 'dmf-review.html'; [IO.File]::WriteAllText($reviewPath,$html.ToString(),[Text.UTF8Encoding]::new($false))
$summary | Format-List
