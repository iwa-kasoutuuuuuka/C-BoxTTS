param(
    [string]$OnnxBackend = "DirectML"
)

# C-Box TTS Native English Portable Build Script
$projectName = "CBoxTTS.Native"
$suffix = if ($OnnxBackend -eq "GPU") { "_CUDA" } else { "" }
$outputDirName = "Release_Portable_EN" + $suffix
$publishDir = "bin\Release_EN\net10.0-windows\win-x64\publish"

Write-Host "Cleaning build folders to prevent DLL caching issues..."
Remove-Item -Path "obj", "bin" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "--- 1. Publishing Native Binary (English Build) with Backend: $OnnxBackend ---"
# dotnet publish will automatically run restore on the cleaned project using the correct OnnxBackend property
& "C:\Program Files\dotnet\dotnet.exe" publish -c Release_EN -r win-x64 --self-contained true /p:PublishReadyToRun=true /p:OnnxBackend=$OnnxBackend

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

if ($OnnxBackend -eq "GPU") {
    Write-Host "Copying CUDA/cuDNN/NVRTC DLLs to portable folder..."
    
    # 1. cuDNN DLLs from lib folder
    $libCudnnPath = "lib"
    if (Test-Path "$libCudnnPath\cudnn64_9.dll") {
        Write-Host "Copying cuDNN DLLs from lib folder..."
        Copy-Item -Path "$libCudnnPath\cud*.dll" -Destination $fullOutputPath -Force
    }
    
    # 2. Copy zlibwapi.dll from lib folder
    $libZlibPath = "lib\zlibwapi.dll"
    if (Test-Path $libZlibPath) {
        Write-Host "Copying zlibwapi.dll from lib folder..."
        Copy-Item -Path $libZlibPath -Destination $fullOutputPath -Force
    }
    
    # 2. Search for CUDA toolkit bin path
    $cudaPath = $null
    if ($env:CUDA_PATH) {
        $cudaPath = Join-Path $env:CUDA_PATH "bin"
    }
    if (-not $cudaPath -or -not (Test-Path $cudaPath)) {
        $cudaPath = Get-ChildItem -Path "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA" -ErrorAction SilentlyContinue | 
            Sort-Object Name -Descending | 
            Select-Object -First 1 | 
            ForEach-Object { Join-Path $_.FullName "bin" }
    }
    
    if ($cudaPath -and (Test-Path $cudaPath)) {
        Write-Host "Found CUDA Toolkit at: $cudaPath"
        # Copy cublas, nvrtc and dependent dlls
        $cudaDlls = @("cublas64_12.dll", "cublasLt64_12.dll", "cusparse64_12.dll", "cusolver64_11.dll", "cufft64_11.dll", "curand64_10.dll", "nv*.dll")
        foreach ($dll in $cudaDlls) {
            if (Test-Path "$cudaPath\$dll") {
                Copy-Item -Path "$cudaPath\$dll" -Destination $fullOutputPath -Force
            }
        }
    } else {
        Write-Warning "CUDA Toolkit bin directory not found. Please ensure CUDA DLLs are copied manually."
    }
}

# Remove MeCab dependency dll as it is not needed for English Exclusive Build
Remove-Item -Path (Join-Path $fullOutputPath "MeCab.DotNet.dll") -ErrorAction SilentlyContinue

# Remove DirectML debug layers to prevent hangs
Remove-Item -Path (Join-Path $fullOutputPath "DirectML.Debug.dll") -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $fullOutputPath "DirectML.Debug.pdb") -ErrorAction SilentlyContinue

# Copy Models
$sourceModels = "bin\Release_EN\net10.0-windows\win-x64\models"
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

# 英語版に必要なモデルのみをコピー (english, multilingual)
$modelsToCopy = @("english", "multilingual")
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

# 形態素解析辞書 (dic) はコピーしない（MeCabを使用しないため）

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
$specPath = "..\ポータブル版仕様書_Native_EN.md"
if (Test-Path $specPath) {
    Write-Host "Copying English specification..."
    Copy-Item -Path $specPath -Destination (Join-Path $fullOutputPath "ポータブル版仕様書_Native_EN.md") -Force
}

Write-Host "--- English Build Finished! ---"
Write-Host "Output Directory: $fullOutputPath"
Get-ChildItem -Path $fullOutputPath
