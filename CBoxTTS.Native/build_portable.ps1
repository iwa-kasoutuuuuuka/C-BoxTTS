# C-Box TTS Native Portable Build Script
$projectName = "CBoxTTS.Native"
$outputDirName = "Release_Portable"
$publishDir = "bin\Release\net10.0-windows\win-x64\publish"

Write-Host "--- 1. Publishing Native Binary ---"
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishReadyToRun=true


if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Publish failed."
    exit $LASTEXITCODE
}

Write-Host "--- 2. Packaging ---"
$currentPath = Get-Location
$fullOutputPath = Join-Path $currentPath $outputDirName

if (Test-Path $fullOutputPath) { Remove-Item -Path $fullOutputPath -Recurse -Force }
New-Item -ItemType Directory -Path $fullOutputPath -Force

# Copy all files from publish (EXE, DLLs, etc.)
Write-Host "Copying binaries and DLLs..."
Copy-Item -Path "$publishDir\*" -Destination $fullOutputPath -Force

# Remove DirectML debug layers to prevent hangs in non-interactive sessions
Remove-Item -Path (Join-Path $fullOutputPath "DirectML.Debug.dll") -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $fullOutputPath "DirectML.Debug.pdb") -ErrorAction SilentlyContinue


# Copy Models and Dictionary
$sourceModels = "bin\Debug\net10.0-windows\win-x64\models"
$sourceDic = "bin\Debug\net10.0-windows\win-x64\dic"

if (Test-Path $sourceModels) {
    Write-Host "Copying models..."
    Copy-Item -Path $sourceModels -Destination $fullOutputPath -Recurse -Force

    # Generate offline copies with '_mtl' suffix to prevent HF downloads
    $targetModelsDir = Join-Path $fullOutputPath "models"
    $baseFiles = "speech_encoder.onnx", "speech_encoder.onnx_data", "embed_tokens.onnx", "embed_tokens.onnx_data", "conditional_decoder.onnx", "conditional_decoder.onnx_data", "tokenizer.json"
    foreach ($bf in $baseFiles) {
        $srcFile = Join-Path $targetModelsDir $bf
        if (Test-Path $srcFile) {
            if ($bf.EndsWith(".onnx_data")) {
                $dstName = $bf.Replace(".onnx_data", "_mtl.onnx_data")
            } else {
                $ext = [System.IO.Path]::GetExtension($bf)
                $nameWithoutExt = [System.IO.Path]::GetFileNameWithoutExtension($bf)
                $dstName = $nameWithoutExt + "_mtl" + $ext
            }
            $dstFile = Join-Path $targetModelsDir $dstName
            if (!(Test-Path $dstFile)) {
                Write-Host "Generating local offline copy: $dstName"
                Copy-Item -Path $srcFile -Destination $dstFile -Force
            }
        }
    }
}

if (Test-Path $sourceDic) {
    Write-Host "Copying dictionary..."
    Copy-Item -Path $sourceDic -Destination $fullOutputPath -Recurse -Force
}

Write-Host "--- Build Finished! ---"
Write-Host "Output Directory: $fullOutputPath"
Get-ChildItem -Path $fullOutputPath
