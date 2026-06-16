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
    $targetModelsDir = Join-Path $fullOutputPath "models"
    if (Test-Path $targetModelsDir) { Remove-Item -Path $targetModelsDir -Recurse -Force }
    Copy-Item -Path $sourceModels -Destination $fullOutputPath -Recurse -Force
}

if (Test-Path $sourceDic) {
    Write-Host "Copying dictionary..."
    $targetDicDir = Join-Path $fullOutputPath "dic"
    if (Test-Path $targetDicDir) { Remove-Item -Path $targetDicDir -Recurse -Force }
    Copy-Item -Path $sourceDic -Destination $fullOutputPath -Recurse -Force
}

Write-Host "--- Build Finished! ---"
Write-Host "Output Directory: $fullOutputPath"
Get-ChildItem -Path $fullOutputPath
