param([string]$Image)
Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
$null = [Windows.Storage.StorageFile,Windows.Foundation,ContentType=WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder,Windows.Foundation,ContentType=WindowsRuntime]
$null = [Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]
$null = [Windows.Globalization.Language,Windows.Foundation,ContentType=WindowsRuntime]

$await = {
    param($task, $type)
    $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
    $netTask = $asTask.MakeGenericMethod($type).Invoke($null, @($task))
    $netTask.Wait()
    $netTask.Result
}

$file = [Windows.Storage.StorageFile]::GetFileFromPathAsync($Image) | ForEach-Object { $_ } # not used
$fileOp = [Windows.Storage.StorageFile]::GetFileFromPathAsync($Image)
$file = & $await $fileOp ([Windows.Storage.StorageFile])

$accessOp = $file.OpenAsync([Windows.Storage.FileAccessMode]::Read)
$stream = & $await $accessOp ([Windows.Storage.Streams.IRandomAccessStream])

$decOp = [Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)
$decoder = & $await $decOp ([Windows.Graphics.Imaging.BitmapDecoder])

$bmpOp = $decoder.GetSoftwareBitmapAsync()
$bmp = & $await $bmpOp ([Windows.Graphics.Imaging.SoftwareBitmap])

$ocr = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
if (-not $ocr) { $ocr = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::CreateLanguage("en-US")) }
$result = $ocr.RecognizeAsync($bmp); 
$resTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
$r = $resTask.MakeGenericMethod([Windows.Media.Ocr.OcrResult]).Invoke($null, @($result))
$r.Wait()
$out = $r.Result
foreach ($line in $out.Lines) {
    "{0}  @{1},{2}" -f $line.Text, [int]$line.Words[0].BoundingRect.X, [int]$line.Words[0].BoundingRect.Y
}
