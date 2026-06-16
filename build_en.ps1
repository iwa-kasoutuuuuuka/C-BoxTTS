# C-Box TTS English Edition Build Script (Simple & Stable)

$projectName = "C-BoxTTS-EN"
$distDir = "dist/$projectName"

# 1. Cleanup
Write-Host "Cleaning..."
if (Test-Path $distDir) {
    $tempDir = "$distDir-old-$(Get-Date -Format 'yyyyMMddHHmmss')"
    Write-Host "Moving existing dist directory to $tempDir to avoid file lock..."
    Move-Item -Path $distDir -Destination $tempDir -ErrorAction SilentlyContinue
    if (Test-Path $distDir) {
        Remove-Item -Path $distDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
if (Test-Path "build/$projectName") {
    $tempBuildDir = "build/$projectName-old-$(Get-Date -Format 'yyyyMMddHHmmss')"
    Move-Item -Path "build/$projectName" -Destination $tempBuildDir -ErrorAction SilentlyContinue
    if (Test-Path "build/$projectName") {
        Remove-Item -Path "build/$projectName" -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# 2. Build
Write-Host "Building EXE..."
& "C:\Users\ITS_Studio\AppData\Local\Programs\Python\Python310\python.exe" -m PyInstaller --noconsole --onedir --noconfirm `
    --name $projectName `
    --icon "src/assets/icon.ico" `
    --add-data "src;src" `
    --add-data "C:\Users\ITS_Studio\AppData\Local\Programs\Python\Python310\lib\site-packages\customtkinter;customtkinter" `
    --exclude-module torch `
    --exclude-module torchvision `
    --exclude-module torchaudio `
    --exclude-module transformers `
    --exclude-module diffusers `
    --exclude-module librosa `
    --exclude-module pykakasi `
    --exclude-module sudachipy `
    --exclude-module paddle `
    --exclude-module llvmlite `
    --exclude-module matplotlib `
    --collect-all requests `
    src/main_en.py

# 3. Assets
Write-Host "Copying assets..."
if (Test-Path "python_embeded") {
    Copy-Item -Path "python_embeded" -Destination "$distDir/" -Recurse -Force
}
Copy-Item -Path "README.md" -Destination "$distDir/" -Force
$docPath = Join-Path $PSScriptRoot "ポータブル版仕様書.md"
if (Test-Path $docPath) {
    Copy-Item -Path $docPath -Destination "$distDir/" -Force
}

# 4. Packages Guide
Write-Host "Creating packages folder..."
$packagesPath = "$distDir/packages"
New-Item -ItemType Directory -Path $packagesPath -Force

# プロジェクトルートに packages フォルダを作成し、中身があればキャッシュとしてコピー
if (!(Test-Path "packages")) {
    New-Item -ItemType Directory -Path "packages" -Force
} else {
    Write-Host "Copying cached packages from root packages folder..."
    Copy-Item -Path "packages\*" -Destination "$packagesPath\" -Force -ErrorAction SilentlyContinue
}

$guideFile = "$packagesPath/Manual_Download_Guide.txt"
"Manual Download Guide" | Out-File $guideFile -Encoding UTF8
"Place .whl files here to skip download." | Add-Content $guideFile
"" | Add-Content $guideFile
"torch: https://download.pytorch.org/whl/cu124/torch-2.6.0%2Bcu124-cp310-cp310-win_amd64.whl" | Add-Content $guideFile
"torchaudio: https://download.pytorch.org/whl/cu124/torchaudio-2.6.0%2Bcu124-cp310-cp310-win_amd64.whl" | Add-Content $guideFile
"torchvision: https://download.pytorch.org/whl/cu124/torchvision-0.21.0%2Bcu124-cp310-cp310-win_amd64.whl" | Add-Content $guideFile
"" | Add-Content $guideFile
"transformers: https://files.pythonhosted.org/packages/py3/t/transformers/transformers-4.48.3-py3-none-any.whl" | Add-Content $guideFile
"diffusers: https://files.pythonhosted.org/packages/py3/d/diffusers/diffusers-0.32.2-py3-none-any.whl" | Add-Content $guideFile
"chatterbox-tts: https://files.pythonhosted.org/packages/py3/c/chatterbox-tts/chatterbox_tts-0.1.7-py3-none-any.whl" | Add-Content $guideFile

Write-Host "Build finished."
Get-ChildItem -Path $distDir
