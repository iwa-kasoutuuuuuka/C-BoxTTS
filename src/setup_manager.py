import os
import sys
import subprocess
import threading
import tkinter as tk
from tkinter import messagebox, ttk
import urllib.request
import zipfile

class SetupManager:
    """
    Python Embeddableの展開とライブラリの自動インストールを管理する。
    """
    PYTHON_EMBED_URL = "https://www.python.org/ftp/python/3.10.11/python-3.10.11-embed-amd64.zip"
    GET_PIP_URL = "https://bootstrap.pypa.io/get-pip.py"
    
    def __init__(self, root):
        self.root = root
        # 実行ファイル(EXE)またはスクリプトの親ディレクトリを基準にする
        if getattr(sys, 'frozen', False):
            # EXE実行時
            self.base_dir = os.path.dirname(sys.executable)
        else:
            # スクリプト実行時
            self.base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
            
        self.python_dir = os.path.join(self.base_dir, "python_embeded")
        self.python_exe = os.path.join(self.python_dir, "python.exe")
        
        # sys.path に埋め込みPythonのsite-packagesを追加
        site_packages = os.path.join(self.python_dir, "Lib", "site-packages")
        if site_packages not in sys.path:
            sys.path.insert(0, site_packages)

    def check_dependencies(self):
        """
        python.exe と必須パッケージが存在するかチェック。
        """
        if not os.path.exists(self.python_exe):
            return False
        try:
            # 埋め込みPython側のライブラリをチェック
            subprocess.check_call([self.python_exe, "-c", "import torch; import chatterbox"], 
                                  creationflags=subprocess.CREATE_NO_WINDOW)
            return True
        except:
            return False

    def install_dependencies(self, on_complete_callback, progress_update_callback):
        """
        1. Python Embeddableの展開と展開
        2. pipのインストール
        3. 高速転送ライブラリ(hf_transfer)の導入
        4. 重量級ライブラリのインストール
        """
        # ダウンロード高速化のための環境変数をセット
        os.environ["HF_HUB_ENABLE_HF_TRANSFER"] = "1"
        
        def task():
            try:
                # 1. Python Embeddableのセットアップ
                zip_path = os.path.join(self.base_dir, "python_embed.zip")
                if not os.path.exists(self.python_exe):
                    if os.path.exists(zip_path):
                        progress_update_callback("ローカルのpython_embed.zipを使用します...", 0.1)
                    else:
                        progress_update_callback("Python本体をダウンロード中...", 0.1)
                        urllib.request.urlretrieve(self.PYTHON_EMBED_URL, zip_path)
                    
                    progress_update_callback("Pythonを展開中...", 0.2)
                    with zipfile.ZipFile(zip_path, 'r') as zip_ref:
                        zip_ref.extractall(self.python_dir)
                    # ダウンロードしたファイルなら削除、手動配置なら残しても良いがクリーンアップのため削除
                    if os.path.exists(zip_path): os.remove(zip_path)
                    
                    # pthファイルの修正（site-packagesを有効にする）
                    pth_file = os.path.join(self.python_dir, "python310._pth")
                    if os.path.exists(pth_file):
                        with open(pth_file, "a") as f:
                            f.write("\nimport site\n")

                # 2. pipのインストール
                get_pip_path = os.path.join(self.base_dir, "get-pip.py")
                if not os.path.exists(os.path.join(self.python_dir, "Scripts", "pip.exe")):
                    if os.path.exists(get_pip_path):
                        progress_update_callback("ローカルのget-pip.pyを使用します...", 0.3)
                    else:
                        progress_update_callback("pipをダウンロード中...", 0.3)
                        urllib.request.urlretrieve(self.GET_PIP_URL, get_pip_path)
                    
                    subprocess.check_call([self.python_exe, get_pip_path], creationflags=subprocess.CREATE_NO_WINDOW)
                    if os.path.exists(get_pip_path): os.remove(get_pip_path)

                # 2.5 高速転送ライブラリの導入 (AIモデルのダウンロードを爆速にする)
                progress_update_callback("高速転送ライブラリをセットアップ中...", 0.4)
                subprocess.check_call([
                    self.python_exe, "-m", "pip", "install", "--no-cache-dir", "hf_transfer"
                ], creationflags=subprocess.CREATE_NO_WINDOW)

                # 3. ライブラリのインストール
                progress_update_callback("AIエンジン (PyTorch CUDA) をインストール中...\n(packagesフォルダのファイルを確認中)", 0.5)
                
                package_dir = os.path.join(self.base_dir, "packages")
                if os.path.exists(package_dir) and any(f.endswith(".whl") for f in os.listdir(package_dir)):
                    # ローカルファイルからインストール
                    progress_update_callback("ローカルのパッケージを使用してインストール中...", 0.5)
                    subprocess.check_call([
                        self.python_exe, "-m", "pip", "install", "--no-cache-dir",
                        "--find-links", package_dir, "torch", "torchaudio", "torchvision"
                    ], creationflags=subprocess.CREATE_NO_WINDOW)
                else:
                    # ネットワークからダウンロード
                    progress_update_callback("AIエンジン (PyTorch CUDA) をダウンロード中...\n(約2GB: 数分かかります)", 0.5)
                    subprocess.check_call([
                        self.python_exe, "-m", "pip", "install", "--no-cache-dir",
                        "torch", "torchaudio", "torchvision",
                        "--index-url", "https://download.pytorch.org/whl/cu124"
                    ], creationflags=subprocess.CREATE_NO_WINDOW)

                progress_update_callback("その他のライブラリをインストール中...", 0.8)
                subprocess.check_call([
                    self.python_exe, "-m", "pip", "install", "--no-cache-dir",
                    "--find-links", package_dir,
                    "chatterbox-tts", "transformers", "diffusers", "librosa", 
                    "pykakasi", "sudachipy", "customtkinter", "pygame"
                ], creationflags=subprocess.CREATE_NO_WINDOW)
                
                # クリーンアップ（__pycache__の削除など）
                progress_update_callback("クリーンアップを実行中...", 0.9)
                subprocess.run([self.python_exe, "-m", "pip", "cache", "purge"], creationflags=subprocess.CREATE_NO_WINDOW)

                on_complete_callback(True)
            except Exception as e:
                on_complete_callback(False, str(e))

        threading.Thread(target=task, daemon=True).start()

class SetupWindow:
    def __init__(self, manager, on_success):
        self.manager = manager
        self.on_success = on_success
        self.window = tk.Tk()
        self.window.title("C-Box TTS - 自動セットアップ")
        self.window.geometry("450x300")
        
        # アイコン設定
        try:
            icon_path = os.path.join(os.path.dirname(__file__), "assets", "icon.ico")
            if os.path.exists(icon_path):
                self.window.iconbitmap(icon_path)
        except:
            pass
        
        tk.Label(self.window, text="AI音声合成エンジンの初回構築", font=("Arial", 14, "bold")).pack(pady=20)
        self.desc_label = tk.Label(self.window, text="実行に必要なPython環境とAIモデルを構築します。\n（約2GB〜 の容量とインターネット接続が必要です）")
        self.desc_label.pack(pady=10)
        
        self.progress = ttk.Progressbar(self.window, mode='determinate', length=350)
        
        self.start_btn = tk.Button(self.window, text="セットアップを開始", font=("Arial", 11), command=self.start_setup)
        self.start_btn.pack(pady=20)
        
        self.status_label = tk.Label(self.window, text="", fg="blue")
        self.status_label.pack()

    def start_setup(self):
        self.start_btn.config(state="disabled")
        self.progress.pack(pady=10)
        self.status_label.config(text="準備中...")
        self.manager.install_dependencies(self.on_complete, self.update_progress)

    def update_progress(self, text, value):
        self.status_label.config(text=text)
        self.progress['value'] = value * 100
        self.window.update_idletasks()

    def on_complete(self, success, error_msg=""):
        if success:
            messagebox.showinfo("成功", "セットアップがすべて完了しました！\nアプリを起動します。")
            self.window.destroy()
            self.on_success()
        else:
            messagebox.showerror("エラー", f"セットアップ中にエラーが発生しました:\n{error_msg}")
            self.window.destroy()
            sys.exit(1)

    def run(self):
        self.window.mainloop()
