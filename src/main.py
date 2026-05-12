import sys
import io
import os

# 標準出力をUTF-8に設定
if sys.stdout is not None:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
if sys.stderr is not None:
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

# プロジェクトルートをパスに追加（srcディレクトリ外から実行する場合のため）
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from src.gui import App

def main():
    """
    C-Box TTS Japanese Edition エントリポイント
    """
    app = App()
    app.mainloop()

if __name__ == "__main__":
    main()
