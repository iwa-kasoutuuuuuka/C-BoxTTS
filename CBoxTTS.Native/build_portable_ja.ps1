# C-Box TTS Native Japanese Portable Build Script
$projectName = "CBoxTTS.Native"
$outputDirName = "Release_Portable_JA"
$publishDir = "bin\Release_JA\net10.0-windows\win-x64\publish"

Write-Host "--- 1. Publishing Native Binary (Japanese Build) ---"
dotnet publish -c Release_JA -r win-x64 --self-contained true /p:PublishReadyToRun=true

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

# Remove DirectML debug layers to prevent hangs
Remove-Item -Path (Join-Path $fullOutputPath "DirectML.Debug.dll") -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $fullOutputPath "DirectML.Debug.pdb") -ErrorAction SilentlyContinue

# Copy Models
$sourceModels = "bin\Debug\net10.0-windows\win-x64\models"
$targetModelsDir = Join-Path $fullOutputPath "models"
New-Item -ItemType Directory -Path $targetModelsDir -Force

Write-Host "Copying default voice prompt..."
if (Test-Path "$sourceModels\default_voice.wav") {
    Copy-Item -Path "$sourceModels\default_voice.wav" -Destination $targetModelsDir -Force
}

# 日本語版に必要なモデルのみをコピー (turbo, multilingual)
$modelsToCopy = @("turbo", "multilingual")
foreach ($model in $modelsToCopy) {
    $srcModelPath = Join-Path $sourceModels $model
    if (Test-Path $srcModelPath) {
        Write-Host "Copying model: $model..."
        Copy-Item -Path $srcModelPath -Destination $targetModelsDir -Recurse -Force
    }
}

# Copy Dictionary (MeCab)
$sourceDic = "bin\Debug\net10.0-windows\win-x64\dic"
if (Test-Path $sourceDic) {
    Write-Host "Copying dictionary..."
    Copy-Item -Path $sourceDic -Destination $fullOutputPath -Recurse -Force
}

# Copy Specification (仕様書)
$specPath = "..\ポータブル版仕様書_Native_JA.md"
if (Test-Path $specPath) {
    Write-Host "Copying Japanese specification..."
    Copy-Item -Path $specPath -Destination (Join-Path $fullOutputPath "ポータブル版仕様書_Native_JA.md") -Force
}

Write-Host "--- Japanese Build Finished! ---"
Write-Host "Output Directory: $fullOutputPath"
Get-ChildItem -Path $fullOutputPath
