import sys
import io
import os
import multiprocessing

# --noconsole時の標準出力エラー防止
class NullWriter:
    def write(self, text): pass
    def flush(self): pass

if sys.stdout is None:
    sys.stdout = NullWriter()
if sys.stderr is None:
    sys.stderr = NullWriter()

# 標準出力をUTF-8に設定
if hasattr(sys.stdout, 'buffer') and sys.stdout.buffer is not None:
    try:
        sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    except Exception:
        pass
if hasattr(sys.stderr, 'buffer') and sys.stderr.buffer is not None:
    try:
        sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')
    except Exception:
        pass

# ダウンロード高速化 (hf_transfer) を有効化
os.environ["HF_HUB_ENABLE_HF_TRANSFER"] = "1"

# プロジェクトルートをパスに追加
ROOT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.append(ROOT_DIR)

# セットアップマネージャの読み込み
from src.setup_manager import SetupManager, SetupWindow

def run_app():
    # 実行ファイル(EXE)の場所を基準にパスを設定
    if getattr(sys, 'frozen', False):
        base_dir = os.path.dirname(sys.executable)
    else:
        base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        
    # 1. 埋め込み環境の site-packages を追加
    lib_path = os.path.join(base_dir, "python_embeded", "Lib", "site-packages")
    if os.path.exists(lib_path) and lib_path not in sys.path:
        sys.path.insert(0, lib_path)
    
    # 2. 埋め込み環境の標準ライブラリ (python310.zip) も追加
    zip_path = os.path.join(base_dir, "python_embeded", "python310.zip")
    if os.path.exists(zip_path) and zip_path not in sys.path:
        sys.path.append(zip_path)
    
    # 3. 移植した標準ライブラリフォルダ (Lib) も追加
    standard_lib_path = os.path.join(base_dir, "python_embeded", "Lib")
    if os.path.exists(standard_lib_path) and standard_lib_path not in sys.path:
        sys.path.append(standard_lib_path)
    
    # 4. 埋め込み環境の本体ディレクトリとDLLパスも追加
    embed_dir = os.path.join(base_dir, "python_embeded")
    if os.path.exists(embed_dir) and embed_dir not in sys.path:
        sys.path.append(embed_dir)
        # WindowsのPython 3.8以降で必要なDLL読み込み設定
        if hasattr(os, 'add_dll_directory'):
            os.add_dll_directory(embed_dir)
            dll_dir = os.path.join(embed_dir, "DLLs")
            if os.path.exists(dll_dir):
                os.add_dll_directory(dll_dir)
    
    # 本番アプリのインポート（英語特化版のGUIをロード）
    from src.gui_en import App
    app = App()
    app.mainloop()

def main():
    # PyInstaller環境での二重起動防止
    multiprocessing.freeze_support()
    
    manager = SetupManager(None)
    
    if manager.check_dependencies():
        run_app()
    else:
        # セットアップが必要な場合
        setup_gui = SetupWindow(manager, run_app)
        setup_gui.run()

if __name__ == "__main__":
    main()
