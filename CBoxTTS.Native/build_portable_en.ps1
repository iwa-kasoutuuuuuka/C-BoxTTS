# C-Box TTS Native English Portable Build Script
$projectName = "CBoxTTS.Native"
$outputDirName = "Release_Portable_EN"
$publishDir = "bin\Release_EN\net10.0-windows\win-x64\publish"

Write-Host "--- 1. Publishing Native Binary (English Build) ---"
dotnet publish -c Release_EN -r win-x64 --self-contained true /p:PublishReadyToRun=true

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

# Remove MeCab dependency dll as it is not needed for English Exclusive Build
Remove-Item -Path (Join-Path $fullOutputPath "MeCab.DotNet.dll") -ErrorAction SilentlyContinue

# Remove DirectML debug layers to prevent hangs
Remove-Item -Path (Join-Path $fullOutputPath "DirectML.Debug.dll") -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $fullOutputPath "DirectML.Debug.pdb") -ErrorAction SilentlyContinue

# Copy Models
$sourceModels = "bin\Release_EN\net10.0-windows\win-x64\models"
$targetModelsDir = Join-Path $fullOutputPath "models"
New-Item -ItemType Directory -Path $targetModelsDir -Force

Write-Host "Copying default voice prompt..."
if (Test-Path "$sourceModels\default_voice.wav") {
    Copy-Item -Path "$sourceModels\default_voice.wav" -Destination $targetModelsDir -Force
}

# 英語版に必要なモデルのみをコピー (english, multilingual)
$modelsToCopy = @("english", "multilingual")
foreach ($model in $modelsToCopy) {
    $srcModelPath = Join-Path $sourceModels $model
    if (Test-Path $srcModelPath) {
        Write-Host "Copying model: $model..."
        Copy-Item -Path $srcModelPath -Destination $targetModelsDir -Recurse -Force
    }
}

# 形態素解析辞書 (dic) はコピーしない（MeCabを使用しないため）

# Copy Assets
$sourceAssets = "assets"
if (Test-Path $sourceAssets) {
    Write-Host "Copying assets..."
    Copy-Item -Path $sourceAssets -Destination $fullOutputPath -Recurse -Force
}

# Copy Specification (仕様書)
$specPath = "..\ポータブル版仕様書_Native_EN.md"
if (Test-Path $specPath) {
    Write-Host "Copying English specification..."
    Copy-Item -Path $specPath -Destination (Join-Path $fullOutputPath "ポータブル版仕様書_Native_EN.md") -Force
}

Write-Host "--- English Build Finished! ---"
Write-Host "Output Directory: $fullOutputPath"
Get-ChildItem -Path $fullOutputPath
