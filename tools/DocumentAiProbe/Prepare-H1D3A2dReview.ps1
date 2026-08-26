param(
 [string]$DmfRun='C:\temp\Pruebas\Facturas\_Extraidas\20260826-113613-1eb93599',
 [string]$ReviewRoot='C:\RecepcionDocumental\Revisar',
 [string]$Dataset='tools\DocumentAiProbe\dataset.csv',
 [string]$Output='C:\temp\Pruebas\Facturas\H1D3A2d_Revision',
 [string]$RendererBin='bin'
)
$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Drawing; Add-Type -AssemblyName System.IO.Compression.FileSystem
if(Test-Path -LiteralPath $Output){throw "La carpeta de salida ya existe: $Output"}; $zip=$Output+'.zip'; if(Test-Path -LiteralPath $zip){throw "El ZIP ya existe: $zip"}
[IO.Directory]::CreateDirectory($Output)|Out-Null; $scratch=Join-Path ([IO.Path]::GetTempPath()) ('H1D3A2d-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($scratch)|Out-Null
$renderer=[IO.Path]::GetFullPath($RendererBin);$env:PATH=(Join-Path $renderer 'x64')+';'+$renderer+';'+$env:PATH;[Reflection.Assembly]::LoadFrom((Join-Path $renderer 'SkiaSharp.dll'))|Out-Null;[Reflection.Assembly]::LoadFrom((Join-Path $renderer 'PDFtoImage.dll'))|Out-Null
function Hash($p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
function Render($path,$ext,$id){$target=Join-Path $scratch ($id+'.png');if($ext-eq'.pdf'){[PDFtoImage.Conversion]::SavePng($target,[IO.File]::ReadAllBytes($path),0,$null,[PDFtoImage.RenderOptions]::new(115));return $target};return $path}
function Sheets($items,$prefix){$index=0;$sheet=0;while($index-lt$items.Count){$sheet++;$canvas=[Drawing.Bitmap]::new(1600,2200);$g=[Drawing.Graphics]::FromImage($canvas);$g.Clear([Drawing.Color]::FromArgb(242,244,247));$g.SmoothingMode='HighQuality';$g.InterpolationMode='HighQualityBicubic';for($slot=0;$slot-lt 4-and$index-lt$items.Count;$slot++,$index++){$item=$items[$index];$x=20+($slot%2)*790;$y=20+[math]::Floor($slot/2)*1090;$g.FillRectangle([Drawing.Brushes]::White,$x,$y,770,1070);$font=[Drawing.Font]::new('Segoe UI',22,[Drawing.FontStyle]::Bold);$small=[Drawing.Font]::new('Segoe UI',14);$g.DrawString($item.CandidateId,$font,[Drawing.Brushes]::Black,$x+14,$y+10);$g.DrawString($item.Display,$small,[Drawing.Brushes]::DarkSlateGray,$x+14,$y+52);try{$img=[Drawing.Image]::FromFile($item.Thumbnail);$maxW=730;$maxH=965;$scale=[math]::Min($maxW/$img.Width,$maxH/$img.Height);$w=[int]($img.Width*$scale);$h=[int]($img.Height*$scale);$g.DrawImage($img,$x+20+[int](($maxW-$w)/2),$y+90+[int](($maxH-$h)/2),$w,$h);$img.Dispose()}catch{$g.DrawString('Miniatura no disponible',$small,[Drawing.Brushes]::DarkRed,$x+20,$y+120)}$font.Dispose();$small.Dispose()};$g.Dispose();$file=Join-Path $Output ('{0}-contact-{1:D2}.jpg'-f$prefix,$sheet);$canvas.Save($file,[Drawing.Imaging.ImageFormat]::Jpeg);$canvas.Dispose()}}
try{
 $incorporated=@('Z001023242559','Z001023242560','Z001023242608','Z001023242796','Z001023242882','Z001023242889','Z001023242890','Z001023242895','Z001023243074','Z001023243075','Z001023243327','Z001023243348','Z001023243349','Z001023243369','Z001023243373','Z001023243375','Z001023243377')
 $dmfFiles=@(Get-ChildItem (Join-Path $DmfRun 'Documentos') -Filter '*.pdf' -File -Recurse|Where-Object{$incorporated-notcontains$_.BaseName}|Sort-Object BaseName);$dmfRows=@();$dmfVisual=@();$n=0
 foreach($f in $dmfFiles){$n++;$id=$f.BaseName;$h=Hash $f.FullName;$dmfRows+=[pscustomobject]@{ID=$id;PDF=$f.Name;SHA256=$h;Path=$f.FullName};$thumb=Render $f.FullName '.pdf' ('dmf-'+$id);$dmfVisual+=[pscustomobject]@{CandidateId=$id;Display=$f.Name;Thumbnail=$thumb}}
 $dmfRows|Export-Csv (Join-Path $Output 'dmf-restantes.csv') -NoTypeInformation -Encoding UTF8;Sheets @($dmfVisual) 'dmf-restantes'
 $known=(Import-Csv $Dataset).SHA256|ForEach-Object{$_.ToUpperInvariant()};$all=@(Get-ChildItem -LiteralPath $ReviewRoot -File -Recurse);$raw=@();$excluded=0
 foreach($f in $all){$h=Hash $f.FullName;if($known-contains$h){$excluded++;continue};$relative=[IO.Path]::GetRelativePath($ReviewRoot,$f.FullName);$parts=$relative.Split('\');$message=if($parts.Count-ge 5){$parts[3]}else{''};$raw+=[pscustomobject]@{File=$f;Hash=$h;MessageId=$message}}
 $groups=@($raw|Group-Object Hash|Sort-Object{$_.Group[0].MessageId},{$_.Group[0].File.Name});$reviewRows=@();$reviewVisual=@();$n=0
 foreach($group in $groups){$n++;$first=$group.Group[0];$cid='R{0:D4}'-f$n;$ext=$first.File.Extension.ToLowerInvariant();$paths=($group.Group.File.FullName-join' | ');$reviewRows+=[pscustomobject]@{CandidateId=$cid;OriginalPath=$first.File.FullName;Filename=$first.File.Name;SHA256=$first.Hash;Extension=$ext;MessageId=$first.MessageId;OriginRelation=('Mismo MessageId/carpeta: '+$first.MessageId);RelatedPaths=$paths;CurrentResult='REVISAR'};if($ext-in@('.pdf','.jpg','.jpeg','.png')){$thumb=Render $first.File.FullName $ext ('rev-'+$cid);$reviewVisual+=[pscustomobject]@{CandidateId=$cid;Display=($first.MessageId+' / '+$first.File.Name);Thumbnail=$thumb}}}
 $reviewRows|Export-Csv (Join-Path $Output 'revisar-candidatos.csv') -NoTypeInformation -Encoding UTF8;Sheets @($reviewVisual) 'revisar'
 $pdfCount=@($reviewRows|Where-Object{$_.Extension -eq '.pdf'}).Count;$imgCount=@($reviewRows|Where-Object{$_.Extension-in@('.jpg','.jpeg','.png')}).Count;$origins=@($reviewRows.MessageId|Where-Object{$_}|Sort-Object -Unique).Count
 $summary=@"
# H1D3A2d - Resumen de revisión

- DMF restantes encontrados: $($dmfRows.Count)
- Candidatos nuevos únicos de Revisar: $($reviewRows.Count)
- Archivos de Revisar excluidos por SHA-256 ya presente en corpus: $excluded
- PDF nuevos en Revisar: $pdfCount
- Imágenes JPG/JPEG/PNG nuevas en Revisar: $imgCount
- Otros formatos nuevos en Revisar: $($reviewRows.Count-$pdfCount-$imgCount)
- Mensajes/carpetas de origen aproximados: $origins
- Contact sheets DMF: $([math]::Ceiling($dmfRows.Count/4))
- Contact sheets Revisar: $([math]::Ceiling($reviewVisual.Count/4))

No se asignaron etiquetas ni GroupId. `OriginRelation` sólo conserva contexto de origen para revisión humana. Los originales no fueron movidos ni modificados.
"@;$summary|Set-Content (Join-Path $Output 'resumen.md') -Encoding UTF8
 [IO.Compression.ZipFile]::CreateFromDirectory($Output,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
 [pscustomobject]@{Output=$Output;Zip=$zip;DmfRemaining=$dmfRows.Count;ReviewCandidates=$reviewRows.Count;ExcludedCorpusHashes=$excluded;ReviewPdf=$pdfCount;ReviewImages=$imgCount;ReviewOrigins=$origins;DmfSheets=[math]::Ceiling($dmfRows.Count/4);ReviewSheets=[math]::Ceiling($reviewVisual.Count/4)}|Format-List
}finally{if(Test-Path -LiteralPath $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}}
