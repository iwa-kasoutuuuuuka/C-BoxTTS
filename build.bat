@echo off
setlocal
echo Building C-Box TTS Japanese Edition Portable (Fixing dependencies)...

set PYTHON_EXE=C:\Users\ITS_Studio\AppData\Local\Programs\Python\Python310\python.exe

echo Getting CustomTkinter path...
for /f "delims=" %%i in ('%PYTHON_EXE% -c "import customtkinter; import os; print(os.path.dirname(customtkinter.__file__))"') do set CTK_PATH=%%i

echo Running PyInstaller with intensive collection...
:: torchvision::nms エラー対策として --collect-all を使用
%PYTHON_EXE% -m PyInstaller --noconsole --onefile --name "C-BoxTTS" ^
    --add-data "src;src" ^
    --add-data "%CTK_PATH%;customtkinter" ^
    --collect-all torchvision ^
    --collect-all torch ^
    --collect-all transformers ^
    --collect-all chatterbox ^
    src/main.py

echo.
echo Build complete. Check the dist folder.
pause
endlocal
