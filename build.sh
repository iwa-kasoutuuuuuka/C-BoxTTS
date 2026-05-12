#!/bin/bash
echo "C-Box TTS Japanese Edition ポータブル版をビルドしています..."

pip install -r requirements.txt

# PyInstaller 実行
# --add-data "SOURCE:DEST" (Unixは :)
pyinstaller --noconsole --onefile --name "C-BoxTTS" \
    --add-data "src:src" \
    src/main.py

echo ""
echo "ビルドが完了しました。dist フォルダを確認してください。"
