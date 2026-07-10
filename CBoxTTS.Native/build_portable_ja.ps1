param(
    [string]$OnnxBackend = "DirectML"
)

# C-Box TTS Native Japanese Portable Build Script
$projectName = "CBoxTTS.Native"
$suffix = if ($OnnxBackend -eq "GPU") { "_CUDA" } else { "" }
$outputDirName = "Release_Portable_JA" + $suffix
$publishDir = "bin\Release_JA\net10.0-windows\win-x64\publish"

Write-Host "--- 1. Publishing Native Binary (Japanese Build) with Backend: $OnnxBackend ---"
dotnet publish -c Release_JA -r win-x64 --self-contained true /p:PublishReadyToRun=true /p:OnnxBackend=$OnnxBackend

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Publish failed."
    exit $LASTEXITCODE
}

Write-Host "--- 2. Packaging ---"
$currentPath = Get-Location
$fullOutputPath = Join-Path $currentPath $outputDirName

if (Test-Path $fullOutputPath) {
    Write-Host "Keeping existing models folder if present..."
    Get-ChildItem -Path $fullOutputPath | Where-Object { $_.Name -ne "models" } | Remove-Item -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $fullOutputPath -Force
}

# Copy all files from publish (EXE, DLLs, etc.)
Write-Host "Copying binaries and DLLs..."
Copy-Item -Path "$publishDir\*" -Destination $fullOutputPath -Force

# Remove DirectML debug layers to prevent hangs
Remove-Item -Path (Join-Path $fullOutputPath "DirectML.Debug.dll") -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $fullOutputPath "DirectML.Debug.pdb") -ErrorAction SilentlyContinue

# Copy Models
$sourceModels = "bin\Release_JA\net10.0-windows\win-x64\models"
$fallbackModelsPaths = @(
    "bin\Release\net10.0-windows\win-x64\models",
    "bin\Debug\net10.0-windows\win-x64\models",
    "..\models",
    "models"
)

$targetModelsDir = Join-Path $fullOutputPath "models"
if (-not (Test-Path $targetModelsDir)) {
    New-Item -ItemType Directory -Path $targetModelsDir -Force
}

$actualSourceModels = $sourceModels
if (-not (Test-Path $actualSourceModels)) {
    foreach ($path in $fallbackModelsPaths) {
        if (Test-Path $path) {
            $actualSourceModels = $path
            break
        }
    }
}

Write-Host "Using source models path: $actualSourceModels"

Write-Host "Copying default voice prompt..."
if (Test-Path "$actualSourceModels\default_voice.wav") {
    Copy-Item -Path "$actualSourceModels\default_voice.wav" -Destination $targetModelsDir -Force
}

# 日本語版に必要なモデルのみをコピー (turbo, multilingual)
$modelsToCopy = @("turbo", "multilingual")
foreach ($model in $modelsToCopy) {
    $srcModelPath = Join-Path $actualSourceModels $model
    $targetModelPath = Join-Path $targetModelsDir $model
    if (Test-Path $srcModelPath) {
        if (-not (Test-Path $targetModelPath)) {
            Write-Host "Copying model: $model..."
            Copy-Item -Path $srcModelPath -Destination $targetModelsDir -Recurse -Force
        } else {
            Write-Host "Model $model already exists in output directory. Skipping copy."
        }
    } else {
        Write-Host "Warning: Model source not found for $model at $srcModelPath"
    }
}

# Copy Dictionary (MeCab)
$sourceDic = "bin\Release_JA\net10.0-windows\win-x64\dic"
if (Test-Path $sourceDic) {
    Write-Host "Copying dictionary..."
    Copy-Item -Path $sourceDic -Destination $fullOutputPath -Recurse -Force
}

# Copy Assets
$sourceAssets = "assets"
if (Test-Path $sourceAssets) {
    Write-Host "Copying assets..."
    Copy-Item -Path $sourceAssets -Destination $fullOutputPath -Recurse -Force
}

# Copy User Dictionary (ユーザー辞書)
$dictPath = "..\user_dict_en.txt"
if (Test-Path $dictPath) {
    Write-Host "Copying English user dictionary..."
    Copy-Item -Path $dictPath -Destination (Join-Path $fullOutputPath "user_dict_en.txt") -Force
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
