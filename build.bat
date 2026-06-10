@echo off
setlocal

:: Get CustomTkinter path
for /f "delims=" %%i in ('C:\Users\ITS_Studio\AppData\Local\Programs\Python\Python310\python.exe -c "import customtkinter; import os; print(os.path.dirname(customtkinter.__file__))"') do set CTK_PATH=%%i

echo --- Building Thin Launcher (C-Box TTS) ---
echo Skipping large libraries: torch, torchvision, torchaudio, transformers, diffusers, librosa

C:\Users\ITS_Studio\AppData\Local\Programs\Python\Python310\python.exe -m PyInstaller ^
    --noconsole ^
    --onedir ^
    --noconfirm ^
    --name "C-BoxTTS" ^
    --icon "src/assets/icon.ico" ^
    --add-data "src;src" ^
    --add-data "%CTK_PATH%;customtkinter" ^
    --exclude-module torch ^
    --exclude-module torchvision ^
    --exclude-module torchaudio ^
    --exclude-module transformers ^
    --exclude-module diffusers ^
    --exclude-module librosa ^
    --exclude-module pykakasi ^
    --exclude-module sudachipy ^
    --exclude-module paddle ^
    --exclude-module llvmlite ^
    --exclude-module matplotlib ^
    --collect-all requests ^
    src/main.py

echo --- Copying additional assets ---
xcopy /E /I /Y "python_embeded" "dist\C-BoxTTS\python_embeded"
copy /Y "README.md" "dist\C-BoxTTS\"
copy /Y "ポータブル版仕様書.md" "dist\C-BoxTTS\"

echo Build finished. Check dist/C-BoxTTS
dir "dist\C-BoxTTS"
pause
endlocal
