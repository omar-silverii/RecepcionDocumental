[CmdletBinding(DefaultParameterSetName='Extract')]
param(
    [Parameter(Mandatory=$true,ParameterSetName='Extract')][Alias('Source')][string[]]$Sources,
    [Parameter(Mandatory=$true,ParameterSetName='Extract')][string]$OutputRoot,
    [Parameter(Mandatory=$true,ParameterSetName='Reorganize')][string]$ReorganizeRun,
    [string]$RendererBin = '',
    [switch]$PreflightOnly
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing

function IsUnder([string]$path,[string]$root) {
    $path.Equals($root,[StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith($root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)
}
function AssertNoLinks([string]$path) {
    $node = Get-Item -LiteralPath $path -Force
    while ($node) {
        if ($node.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Ruta con reparse point rechazada: $($node.FullName)" }
        $node = $node.Parent
    }
}
function Signature([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    try {
        $h = [byte[]]::new(4)
        ($stream.Read($h,0,4) -eq 4 -and $h[0] -eq 80 -and $h[1] -eq 75 -and
            (($h[2] -eq 3 -and $h[3] -eq 4) -or ($h[2] -eq 5 -and $h[3] -eq 6) -or ($h[2] -eq 7 -and $h[3] -eq 8)))
    } finally { $stream.Dispose() }
}
function WriteCsv($rows,[string]$path,[string]$emptyHeader) {
    if (@($rows).Count) { $rows | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding UTF8 }
    else { Set-Content -LiteralPath $path -Value $emptyHeader -Encoding UTF8 }
}
function AssertInternalPath([string]$path) {
    $parts=$path.Replace('/','\').Split('\')
    if ([IO.Path]::IsPathRooted($path) -or $path.Contains(':') -or @($parts|Where-Object { $_ -eq '..' -or $_ -eq '.' }).Count) { throw 'Ruta interna peligrosa rechazada.' }
    foreach ($part in $parts) {
        if (-not $part -or $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or $part -match '[ .]$' -or $part -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)') { throw 'Nombre interno inseguro rechazado.' }
    }
}
function FlatPath([string]$root,[string]$name) {
    AssertInternalPath $name
    $name=[IO.Path]::GetFileName($name.Replace('/','\'))
    $extension=[IO.Path]::GetExtension($name).ToLowerInvariant()
    $type='Other'
    if ($extension -eq '.pdf') { $type='PDF' }
    elseif ($extension -in @('.tif','.tiff')) { $type='TIFF' }
    elseif ($extension -in @('.jpg','.jpeg','.png')) { $type='Images' }
    Join-Path (Join-Path $root $type) $name
}
function CollisionRows($plans) {
    @($plans|Group-Object { $_.Target.ToUpperInvariant() }|Where-Object Count -gt 1|ForEach-Object {
        $count=$_.Count
        foreach ($item in $_.Group) {
            [pscustomobject]@{FileName=[IO.Path]::GetFileName($item.Target);Count=$count;Target=$item.Target;SourceFolder=$item.SourceFolder;SourceRelativePath=$item.SourceRelativePath;DmfName=$item.DmfName;DmfOriginalPath=$item.DmfOriginalPath;InternalPath=$item.InternalPath;Sha256=$item.Sha256;ExtractedPath=$item.ExtractedPath}
        }
    })
}
function VerifySources($baseline) {
    $current=@{}
    foreach ($root in @($baseline.SourceFolder|Sort-Object -Unique)) {
        AssertNoLinks $root
        foreach ($file in @(Get-ChildItem -LiteralPath $root -File -Recurse -Force)) { $current[$file.FullName]=$file }
    }
    if ($current.Count -ne $baseline.Count) { throw 'Cambio en cantidad de archivos originales.' }
    foreach ($row in $baseline) {
        $file=$current[$row.OriginalPath]
        if (-not $file -or $file.Length -ne [long]$row.SizeBytes -or $file.LastWriteTimeUtc.ToString('o') -ne $row.LastWriteUtc -or (Get-FileHash -LiteralPath $row.OriginalPath -Algorithm SHA256).Hash -ne $row.Sha256) { throw "Integridad original no confirmada: $($row.OriginalPath)" }
    }
}
if ($PSCmdlet.ParameterSetName -eq 'Reorganize') {
    # Reuse validated extracts. Never open an archive or regenerate a thumbnail here.
    $run=(Get-Item -LiteralPath $ReorganizeRun).FullName.TrimEnd('\')
    AssertNoLinks $run
    $inventory=@(Import-Csv (Join-Path $run 'dmf-inventory.csv'))
    $review=@(Import-Csv (Join-Path $run 'review-index.csv'))
    $baseline=@(Import-Csv (Join-Path $run 'source-baseline.csv'))
    foreach ($root in @($baseline.SourceFolder|Sort-Object -Unique)) {
        if ((IsUnder $run $root) -or (IsUnder $root $run)) { throw 'El run no es independiente de los originales.' }
    }
    VerifySources $baseline
    $plans=@(foreach ($row in $inventory) {
        if ($row.ExtractionStatus -ne 'OK') { continue }
        $old=[IO.Path]::GetFullPath($row.ExtractedPath)
        if (-not (IsUnder $old $run) -or $old -eq $run) { throw 'Extracto fuera del run.' }
        AssertNoLinks ([IO.Path]::GetDirectoryName($old))
        if ((Get-Item -LiteralPath $old).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Extracto con enlace rechazado.' }
        if ((Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -ne $row.Sha256) { throw "Hash del extracto incorrecto: $old" }
        [pscustomobject]@{Target=(FlatPath $run ([IO.Path]::GetFileName($old)));SourceFolder=$row.SourceFolder;SourceRelativePath=$row.SourceRelativePath;DmfName=$row.DmfName;DmfOriginalPath=$row.DmfOriginalPath;InternalPath=$row.InternalPath;Sha256=$row.Sha256;ExtractedPath=$old;Row=$row}
    })
    $collisions=@(CollisionRows $plans)
    $blocked=@{}; foreach ($item in $collisions) { $blocked[$item.Target]=$true }
    $moves=@($plans|Where-Object { -not $blocked.ContainsKey($_.Target) -and $_.Target -cne $_.ExtractedPath })
    foreach ($item in $moves) {
        if (Test-Path -LiteralPath $item.Target) { throw "Destino existente; no se sobrescribe: $($item.Target)" }
        $parent=[IO.Path]::GetDirectoryName($item.Target)
        if (Test-Path -LiteralPath $parent) { AssertNoLinks $parent }
    }
    $byOld=@{}; foreach ($item in $plans) { $byOld[$item.ExtractedPath]=$item }
    foreach ($item in $review) {
        if (-not $byOld.ContainsKey($item.ExtractedPath) -or $byOld[$item.ExtractedPath].Sha256 -ne $item.Sha256) { throw 'Review sin correspondencia en inventario.' }
        if ($item.Thumbnail -and -not (Test-Path -LiteralPath $item.Thumbnail)) { throw 'Miniatura ausente.' }
    }
    # Keep a recovery journal and metadata snapshot before any move.
    $audit=Join-Path $run ('LayoutAudit-'+[Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($audit)
    foreach ($name in @('dmf-inventory.csv','review-index.csv','duplicate-groups.csv','dmf-summary.json','resultado.md')) { [IO.File]::Copy((Join-Path $run $name),(Join-Path $audit $name),$false) }
    $moves|Select-Object ExtractedPath,Target,Sha256|Export-Csv (Join-Path $audit 'moves.csv') -NoTypeInformation -Encoding UTF8
    $completed=[Collections.Generic.List[object]]::new()
    try {
        foreach ($item in $moves) {
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($item.Target))
            # File.Move has no overwrite flag: an unexpected collision fails closed.
            [IO.File]::Move($item.ExtractedPath,$item.Target)
            $completed.Add($item)
            if ((Get-FileHash -LiteralPath $item.Target -Algorithm SHA256).Hash -ne $item.Sha256) { throw 'Hash despues del movimiento incorrecto.' }
            $item.Row.ExtractedPath=$item.Target
        }
        foreach ($item in $review) { $item.ExtractedPath=$byOld[$item.ExtractedPath].Row.ExtractedPath }
        VerifySources $baseline
        $inventory|Export-Csv (Join-Path $run 'dmf-inventory.csv') -NoTypeInformation -Encoding UTF8
        $review|Export-Csv (Join-Path $run 'review-index.csv') -NoTypeInformation -Encoding UTF8
        $duplicates=@($inventory|Where-Object ExtractionStatus -eq 'OK'|Group-Object Sha256|Where-Object Count -gt 1|ForEach-Object {
            [pscustomobject]@{Sha256=$_.Name;Count=$_.Count;Provenances=(@($_.Group|Select-Object SourceFolder,SourceRelativePath,InternalPath,ExtractedPath)|ConvertTo-Json -Compress -Depth 4)}
        })
        WriteCsv $duplicates (Join-Path $run 'duplicate-groups.csv') 'Sha256,Count,Provenances'
        WriteCsv $collisions (Join-Path $run 'name-collisions.csv') 'FileName,Count,Target,SourceFolder,SourceRelativePath,DmfName,DmfOriginalPath,InternalPath,Sha256,ExtractedPath'
        $pdf=@($plans|Where-Object { [IO.Path]::GetExtension($_.Target) -ieq '.pdf' })
        $layout=[pscustomobject]@{PdfTotal=$pdf.Count;PdfDistinctNames=@($pdf.Target|Sort-Object -Unique).Count;PdfConsolidated=@($pdf|Where-Object { $_.Row.ExtractedPath -ceq $_.Target }).Count;PdfBlocked=@($pdf|Where-Object { $blocked.ContainsKey($_.Target) }).Count;CollisionNames=$blocked.Count;FilesMoved=$moves.Count;HashesVerified=$plans.Count;OriginalsIntact=$true;AuditDirectory=$audit;Policy='Nombres repetidos permanecen en su ubicacion; nunca sobrescribir, renombrar ni deduplicar.'}
        $summary=Get-Content (Join-Path $run 'dmf-summary.json') -Raw|ConvertFrom-Json
        $summary|Add-Member -NotePropertyName FlatLayout -NotePropertyValue $layout -Force
        $summary|ConvertTo-Json -Depth 8|Set-Content (Join-Path $run 'dmf-summary.json') -Encoding UTF8
        $layout|ConvertTo-Json -Depth 4|Set-Content (Join-Path $run 'flat-layout.json') -Encoding UTF8
        @('', '## Reorganizacion plana', ($layout|ConvertTo-Json -Depth 4), 'Carpetas: PDF, TIFF, Images, Other (si aplica) y Review. Colisiones detalladas en name-collisions.csv. Los casos bloqueados permanecen en Documentos. LayoutAudit conserva metadatos historicos y el mapa de movimientos para recuperacion.')|Add-Content (Join-Path $run 'resultado.md') -Encoding UTF8
    } catch {
        foreach ($item in $completed) { [IO.File]::Move($item.Target,$item.ExtractedPath) }
        foreach ($name in @('dmf-inventory.csv','review-index.csv','duplicate-groups.csv','dmf-summary.json','resultado.md')) { [IO.File]::Copy((Join-Path $audit $name),(Join-Path $run $name),$true) }
        throw
    }
    $layout|ConvertTo-Json -Depth 4|Write-Output
    return
}
$roots = @($Sources | ForEach-Object { (Get-Item -LiteralPath $_ -Force).FullName.TrimEnd('\') })
$destination = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\')
foreach ($root in $roots) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Fuente inexistente: $root" }
    AssertNoLinks $root
    if ((IsUnder $destination $root) -or (IsUnder $root $destination)) { throw 'El destino debe ser independiente de todas las fuentes.' }
    foreach ($other in $roots) { if ($root -ne $other -and (IsUnder $root $other)) { throw 'Fuentes superpuestas.' } }
}
if (@($roots | Sort-Object -Unique).Count -ne $roots.Count) { throw 'Fuentes repetidas.' }
$ancestor = $destination
while (-not (Test-Path -LiteralPath $ancestor)) { $ancestor = [IO.Path]::GetDirectoryName($ancestor) }
AssertNoLinks $ancestor

# Complete read-only preflight and baseline before creating the run.
$baseline = [Collections.Generic.List[object]]::new()
$preflight = [Collections.Generic.List[object]]::new()
$sourceId = 0
foreach ($root in $roots) {
    $sourceId++
    $items = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
    if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw "Fuente con enlaces rechazada: $root" }
    $files = @($items | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)
    $valid=0; $invalid=0; $readErrors=0
    foreach ($file in $files) {
        $hash=''; $errorText=''; $zip=$null
        try { $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash; if ($file.Extension -ieq '.dmf') { $zip=Signature $file.FullName; if($zip){$valid++}else{$invalid++} } }
        catch { $errorText=$_.Exception.Message; $readErrors++ }
        $baseline.Add([pscustomobject]@{SourceId=$sourceId;SourceFolder=$root;SourceRelativePath=$file.FullName.Substring($root.Length+1);OriginalPath=$file.FullName;Name=$file.Name;Extension=$file.Extension.ToLowerInvariant();SizeBytes=$file.Length;LastWriteUtc=$file.LastWriteTimeUtc.ToString('o');Sha256=$hash;ZipSignature=$zip;ReadError=$errorText})
    }
    $preflight.Add([pscustomobject]@{SourceFolder=$root;FilesFound=$files.Count;Subdirectories=@($items|Where-Object PSIsContainer).Count;SizeBytes=[long](($files|Measure-Object Length -Sum).Sum);Extensions=@($files|Group-Object Extension|Select-Object Name,Count);DmfFound=@($files|Where-Object Extension -eq '.dmf').Count;ZipSignatureValid=$valid;ZipSignatureInvalid=$invalid;ReadErrors=$readErrors})
}
$preflight | ConvertTo-Json -Depth 5 | Write-Output
if ($PreflightOnly) { return }
$run = Join-Path $destination ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$baseline | Export-Csv (Join-Path $run 'source-baseline.csv') -NoTypeInformation -Encoding UTF8
$preflight | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'preflight.json') -Encoding UTF8
# Inspect central directories before extracting anything: block every occurrence of
# a repeated flat name, including equal hashes and case-only Windows collisions.
$flatCounts=@{}
foreach ($source in $baseline) {
    $zip=$null
    try {
        if ($source.Extension -eq '.dmf') {
            if ($source.ReadError -or -not $source.ZipSignature) { continue }
            $zip=[IO.Compression.ZipFile]::OpenRead($source.OriginalPath)
            foreach ($entry in $zip.Entries) {
                if (-not $entry.Name) { continue }
                try { $target=FlatPath $run $entry.FullName } catch { continue }
                if (-not $flatCounts.ContainsKey($target)) { $flatCounts[$target]=0 }
                $flatCounts[$target]++
            }
        } else {
            $target=FlatPath $run $source.Name
            if (-not $flatCounts.ContainsKey($target)) { $flatCounts[$target]=0 }
            $flatCounts[$target]++
        }
    } catch { } # Archive/read errors are recorded by the regular extraction pass.
    finally { if ($zip) { $zip.Dispose() } }
}
function ExtractionTarget([string]$name,[string]$fallback) {
    $flat=FlatPath $run $name
    if (-not $flatCounts.ContainsKey($flat)) { throw 'Entrada no incluida en el preflight de nombres.' }
    if ($flatCounts[$flat] -gt 1) { return $fallback }
    $flat
}
$rendererReady=$false; $rendererError='Renderer PDF no disponible.'
if (-not $RendererBin) { $RendererBin=Join-Path $PSScriptRoot '../../bin' }
if (Test-Path -LiteralPath $RendererBin) {
    $RendererBin=[IO.Path]::GetFullPath($RendererBin)
    $env:PATH=(Join-Path $RendererBin 'x64')+';'+$RendererBin+';'+$env:PATH
    try {
        [Reflection.Assembly]::LoadFrom((Join-Path $RendererBin 'SkiaSharp.dll')) | Out-Null
        [Reflection.Assembly]::LoadFrom((Join-Path $RendererBin 'PDFtoImage.dll')) | Out-Null
        $rendererReady=$true
    } catch { $rendererError=$_.Exception.Message }
}
$inventory=[Collections.Generic.List[object]]::new()
$review=[Collections.Generic.List[object]]::new()
$archives=[Collections.Generic.List[object]]::new()
$known=@('.pdf','.tif','.tiff','.jpg','.jpeg','.png')
function NewRow($source,[string]$internal,[long]$size) {
    $dmf=$source.Extension -eq '.dmf'
    $extension=$source.Extension; if ($dmf) { $extension=[IO.Path]::GetExtension($internal).ToLowerInvariant() }
    [pscustomobject]@{SourceFolder=$source.SourceFolder;SourceRelativePath=$source.SourceRelativePath;SourceType=$(if($dmf){'DMF'}else{'DIRECT'});DmfName=$(if($dmf){$source.Name}else{''});DmfOriginalPath=$(if($dmf){$source.OriginalPath}else{''});InternalPath=$internal;Extension=$extension;SizeBytes=$size;Sha256='';ExtractedPath='';ExtractionStatus='ERROR';Error='';UnexpectedType=($extension -notin $known);FrameCount=$null;Thumbnail='';ThumbnailError=''}
}
function AddReview($row) {
    $id='C{0:D6}' -f ($review.Count+1)
    $batch=Join-Path $run ('Review\Batch{0:D3}' -f ([int][Math]::Floor($review.Count/100)+1))
    [IO.Directory]::CreateDirectory($batch) | Out-Null
    $thumb=Join-Path $batch ($id+'.png')
    try {
        if ($row.Extension -eq '.pdf') {
            if (-not $rendererReady) { throw $rendererError }
            [PDFtoImage.Conversion]::SavePng($thumb,[IO.File]::ReadAllBytes($row.ExtractedPath),0,$null,[PDFtoImage.RenderOptions]::new(120))
        } elseif ($row.Extension -in @('.tif','.tiff','.jpg','.jpeg','.png')) {
            $img=[Drawing.Image]::FromFile($row.ExtractedPath)
            try {
                if ($row.Extension -in @('.tif','.tiff')) {
                    $dimension=[Drawing.Imaging.FrameDimension]::Page
                    $row.FrameCount=$img.GetFrameCount($dimension)
                    [void]$img.SelectActiveFrame($dimension,0)
                }
                $scale=[Math]::Min(1,1600.0/[Math]::Max($img.Width,$img.Height))
                $bitmap=[Drawing.Bitmap]::new([int][Math]::Max(1,$img.Width*$scale),[int][Math]::Max(1,$img.Height*$scale))
                try { $g=[Drawing.Graphics]::FromImage($bitmap); try { $g.Clear([Drawing.Color]::White); $g.DrawImage($img,0,0,$bitmap.Width,$bitmap.Height) } finally { $g.Dispose() }; $bitmap.Save($thumb,[Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
            } finally { $img.Dispose() }
        } else { throw 'Tipo inesperado conservado; sin renderer de miniatura.' }
        $row.Thumbnail=$thumb
    } catch { $row.ThumbnailError=$_.Exception.Message }
    $review.Add([pscustomobject]@{CandidateId=$id;Sha256=$row.Sha256;SourceFolder=$row.SourceFolder;SourceRelativePath=$row.SourceRelativePath;SourceType=$row.SourceType;DmfName=$row.DmfName;InternalPath=$row.InternalPath;Extension=$row.Extension;ExtractedPath=$row.ExtractedPath;FrameCount=$row.FrameCount;Thumbnail=$row.Thumbnail;ThumbnailError=$row.ThumbnailError})
}
$fileNumber=0
foreach ($source in $baseline) {
    $fileNumber++
    $folder=Join-Path $run ('Documentos\S{0:D3}\F{1:D6}' -f $source.SourceId,$fileNumber)
    if ($source.Extension -ne '.dmf') {
        $row=NewRow $source '' $source.SizeBytes
        try {
            if ($source.ReadError) { throw $source.ReadError }
            $target=ExtractionTarget $source.Name (Join-Path $folder $source.Name)
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.File]::Copy($source.OriginalPath,$target,$false)
            $row.ExtractedPath=$target; $row.Sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
            if ($row.Sha256 -ne $source.Sha256) { throw 'La copia no coincide con el hash de origen.' }
            $row.ExtractionStatus='OK'
        } catch { $row.Error=$_.Exception.Message }
        if ($row.ExtractionStatus -eq 'OK') { AddReview $row }
        $inventory.Add($row); continue
    }
    $archive=$null
    $archiveRow=[pscustomobject]@{SourceFolder=$source.SourceFolder;SourceRelativePath=$source.SourceRelativePath;Status='ERROR';Error=''}
    try {
        if ($source.ReadError) { throw $source.ReadError }
        if (-not $source.ZipSignature) { throw 'Firma ZIP no valida.' }
        $archive=[IO.Compression.ZipFile]::OpenRead($source.OriginalPath)
        $entries=$archive.Entries
        $archiveRow.Status='OK'
        $entryNumber=0
        foreach ($entry in $entries) {
            $entryNumber++
            if ([string]::IsNullOrEmpty($entry.Name)) { continue }
            $row=NewRow $source $entry.FullName $entry.Length
            try {
                AssertInternalPath $entry.FullName
                # Only colliding names keep isolated namespaces; no arbitrary rename.
                $entryRoot=Join-Path $folder ('E{0:D6}' -f $entryNumber)
                $target=[IO.Path]::GetFullPath((Join-Path $entryRoot $entry.FullName))
                if (-not (IsUnder $target $entryRoot)) { throw 'Zip Slip rechazado.' }
                $target=ExtractionTarget $entry.FullName $target
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
                $inputStream=$null; $outputStream=$null
                try { $inputStream=$entry.Open(); $outputStream=[IO.File]::Open($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None); $inputStream.CopyTo($outputStream) }
                finally { if($outputStream){$outputStream.Dispose()}; if($inputStream){$inputStream.Dispose()} }
                $row.ExtractedPath=$target
                if ((Get-Item -LiteralPath $target).Length -ne $entry.Length) { throw 'Tamano extraido incorrecto.' }
                $row.Sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
                $row.ExtractionStatus='OK'
            } catch { $row.Error=$_.Exception.Message }
            if ($row.ExtractionStatus -eq 'OK') { AddReview $row }
            $inventory.Add($row)
        }
    } catch {
        $archiveRow.Status='ERROR'; $archiveRow.Error=$_.Exception.Message
        $row=NewRow $source '' 0; $row.Error=$archiveRow.Error; $inventory.Add($row)
    } finally { if($archive){$archive.Dispose()} }
    $archives.Add($archiveRow)
    if ($fileNumber % 100 -eq 0) { Write-Output "Procesados $fileNumber / $($baseline.Count) archivos fuente." }
}

# Re-enumerate and rehash ALL source files, including direct images.
$integrity=[Collections.Generic.List[object]]::new()
$afterPaths=@{}
foreach ($root in $roots) {
    foreach ($file in @(Get-ChildItem -LiteralPath $root -File -Recurse -Force)) { $afterPaths[$file.FullName]=$file }
}
foreach ($before in $baseline) {
    $errorText=''; $ok=$false
    try {
        $after=$afterPaths[$before.OriginalPath]
        if (-not $after) { throw 'Original ausente.' }
        $hash=(Get-FileHash -LiteralPath $after.FullName -Algorithm SHA256).Hash
        $ok=$before.Sha256 -and $hash -eq $before.Sha256 -and $after.Length -eq $before.SizeBytes -and $after.LastWriteTimeUtc.ToString('o') -eq $before.LastWriteUtc
        if (-not $ok) { $errorText='Hash, tamano o fecha distintos, o baseline no verificable.' }
    } catch { $errorText=$_.Exception.Message }
    $integrity.Add([pscustomobject]@{OriginalPath=$before.OriginalPath;Extension=$before.Extension;Unchanged=[bool]$ok;Error=$errorText})
}
$intact=($afterPaths.Count -eq $baseline.Count -and @($integrity|Where-Object { -not $_.Unchanged }).Count -eq 0)
WriteCsv $inventory (Join-Path $run 'dmf-inventory.csv') 'SourceFolder,SourceRelativePath,SourceType,DmfName,DmfOriginalPath,InternalPath,Extension,SizeBytes,Sha256,ExtractedPath,ExtractionStatus,Error'
WriteCsv $review (Join-Path $run 'review-index.csv') 'CandidateId,Sha256,SourceFolder,SourceRelativePath,Thumbnail,ThumbnailError'
WriteCsv $archives (Join-Path $run 'dmf-archives.csv') 'SourceFolder,SourceRelativePath,Status,Error'
WriteCsv $integrity (Join-Path $run 'source-integrity.csv') 'OriginalPath,Extension,Unchanged,Error'
$success=@($inventory|Where-Object ExtractionStatus -eq 'OK')
$namePlans=@(foreach ($row in $inventory) {
    $name=$row.InternalPath
    if ($row.SourceType -eq 'DIRECT') { $name=[IO.Path]::GetFileName($row.SourceRelativePath) }
    if (-not $name) { continue }
    try { $target=FlatPath $run $name } catch { continue }
    [pscustomobject]@{Target=$target;SourceFolder=$row.SourceFolder;SourceRelativePath=$row.SourceRelativePath;DmfName=$row.DmfName;DmfOriginalPath=$row.DmfOriginalPath;InternalPath=$row.InternalPath;Sha256=$row.Sha256;ExtractedPath=$row.ExtractedPath}
})
$nameCollisions=@(CollisionRows $namePlans)
WriteCsv $nameCollisions (Join-Path $run 'name-collisions.csv') 'FileName,Count,Target,SourceFolder,SourceRelativePath,DmfName,DmfOriginalPath,InternalPath,Sha256,ExtractedPath'
$duplicates=@($success|Group-Object Sha256|Where-Object Count -gt 1|ForEach-Object {
    [pscustomobject]@{Sha256=$_.Name;Count=$_.Count;Provenances=(@($_.Group|Select-Object SourceFolder,SourceRelativePath,InternalPath,ExtractedPath)|ConvertTo-Json -Compress -Depth 4)}
})
WriteCsv $duplicates (Join-Path $run 'duplicate-groups.csv') 'Sha256,Count,Provenances'
function Summarize([string]$root) {
    $rows=@($inventory|Where-Object { -not $root -or $_.SourceFolder -eq $root })
    $files=@($rows|Where-Object ExtractionStatus -eq 'OK')
    $src=@($baseline|Where-Object { -not $root -or $_.SourceFolder -eq $root })
    $arc=@($archives|Where-Object { -not $root -or $_.SourceFolder -eq $root })
    $groups=@($files|Group-Object Sha256)
    [pscustomobject]@{SourceFolder=$(if($root){$root}else{'TOTAL'});FilesFound=$src.Count;DmfFound=$arc.Count;DmfValid=@($arc|Where-Object Status -eq 'OK').Count;DmfInvalid=@($arc|Where-Object Status -ne 'OK').Count;InternalFilesExtracted=@($files|Where-Object SourceType -eq 'DMF').Count;PDF=@($files|Where-Object Extension -eq '.pdf').Count;TifTiff=@($files|Where-Object Extension -in @('.tif','.tiff')).Count;JpgJpeg=@($files|Where-Object Extension -in @('.jpg','.jpeg')).Count;PNG=@($files|Where-Object Extension -eq '.png').Count;Other=@($files|Where-Object UnexpectedType).Count;DirectFiles=@($src|Where-Object Extension -ne '.dmf').Count;DirectFilesCopied=@($files|Where-Object SourceType -eq 'DIRECT').Count;UniqueHashes=$groups.Count;ExactDuplicateFiles=($files.Count-$groups.Count);DuplicateGroups=@($groups|Where-Object Count -gt 1).Count;ExtractionErrors=@($rows|Where-Object ExtractionStatus -ne 'OK').Count;ReadErrors=@($src|Where-Object ReadError).Count;ThumbnailErrors=@($files|Where-Object ThumbnailError).Count;ExtractedSizeBytes=[long](($files|Measure-Object SizeBytes -Sum).Sum)}
}
$summary=[pscustomobject]@{RunDirectory=$run;BySource=@($roots|ForEach-Object { Summarize $_ });Total=(Summarize '');OriginalsIntact=$intact;OriginalFilesBefore=$baseline.Count;OriginalFilesAfter=$afterPaths.Count;DmfBefore=@($baseline|Where-Object Extension -eq '.dmf').Count;DmfAfter=@($afterPaths.Values|Where-Object Extension -eq '.dmf').Count;IntegrityFailures=@($integrity|Where-Object { -not $_.Unchanged }).Count;DuplicatePolicy='Conservar todos; duplicados por fuente calculados dentro de cada fuente, total cruza todo el banco.';ClassificationExecuted=$false}
$pdfPlans=@($namePlans|Where-Object { [IO.Path]::GetExtension($_.Target) -ieq '.pdf' })
$summary|Add-Member -NotePropertyName FlatLayout -NotePropertyValue ([pscustomobject]@{PdfTotal=$pdfPlans.Count;PdfDistinctNames=@($pdfPlans.Target|Sort-Object -Unique).Count;PdfConsolidated=@($pdfPlans|Where-Object { $_.ExtractedPath -ceq $_.Target }).Count;PdfBlocked=@($nameCollisions|Where-Object { [IO.Path]::GetExtension($_.Target) -ieq '.pdf' }).Count;CollisionNames=@($nameCollisions.Target|Sort-Object -Unique).Count;Policy='Nombres repetidos conservados bajo Documentos; ver name-collisions.csv. Nombres unicos directamente en PDF, TIFF, Images u Other.'})
$summary|ConvertTo-Json -Depth 6|Set-Content (Join-Path $run 'dmf-summary.json') -Encoding UTF8
$html=[Text.StringBuilder]::new()
[void]$html.AppendLine('<!doctype html><html lang="es"><meta charset="utf-8"><title>Revision externa DMF</title><style>body{font:16px sans-serif}article{display:inline-block;vertical-align:top;width:300px;margin:12px;overflow-wrap:anywhere}img{max-width:290px;max-height:360px}</style><h1>Revision externa</h1><p>Sin clasificacion automatica. CandidateId solo identifica el documento.</p>')
foreach ($item in $review) {
    [void]$html.Append('<article><h2>'+ $item.CandidateId +'</h2>')
    if ($item.Thumbnail) { $rel=$item.Thumbnail.Substring($run.Length+1).Replace('\','/'); [void]$html.Append('<img loading="lazy" src="'+[Net.WebUtility]::HtmlEncode($rel)+'">') }
    [void]$html.Append('<p>'+[Net.WebUtility]::HtmlEncode($item.Extension+' '+$item.ThumbnailError)+'</p></article>')
}
[void]$html.Append('</html>')
[IO.File]::WriteAllText((Join-Path $run 'dmf-review.html'),$html.ToString(),[Text.UTF8Encoding]::new($false))
$report=@('# Resultado del banco externo DMF', '', "Run: $run", '', "Originales intactos (SHA-256, tamano, fecha y cantidad): $intact", "DMF antes/despues: $($summary.DmfBefore) / $($summary.DmfAfter)", '', 'Sin IA, conversion TIFF a PDF, importacion ni reentrenamiento. SQL, Gmail, servicios y configuracion no utilizados.', '', 'Duplicados conservados. Las estadisticas por fuente son locales; TOTAL cruza ambas fuentes.', '', '## Preflight', ($preflight|ConvertTo-Json -Depth 5), '', '## Resultado por fuente y total', ($summary|ConvertTo-Json -Depth 6), '', 'Evidencia: source-baseline.csv y source-integrity.csv. Trazabilidad: dmf-inventory.csv. Revision: review-index.csv y dmf-review.html. Errores separados en ExtractionStatus/Error y ThumbnailError.')
$report | Set-Content (Join-Path $run 'resultado.md') -Encoding UTF8
$summary|ConvertTo-Json -Depth 6|Write-Output
if (-not $intact) { throw "No se pudo confirmar integridad de origen. Ver $run" }
