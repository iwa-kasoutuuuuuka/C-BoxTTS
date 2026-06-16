import customtkinter as ctk
from tkinter import filedialog, messagebox
import os
import threading
import pygame
from .tts_engine import TTSEngine
from .utils import split_text, merge_audio, save_wav, get_timestamp, ensure_dir

class App(ctk.CTk):
    def __init__(self):
        super().__init__()

        # ウィンドウ設定
        self.title("C-Box TTS English Edition")
        self.geometry("900x700")
        ctk.set_appearance_mode("dark")
        ctk.set_default_color_theme("blue")

        # アイコン設定
        try:
            icon_path = os.path.join(os.path.dirname(__file__), "assets", "icon.ico")
            if os.path.exists(icon_path):
                self.after(200, lambda: self.iconbitmap(icon_path))
        except:
            pass

        # エンジン初期化
        self.engine = TTSEngine()
        self.current_audio_tensor = None
        
        # ベースディレクトリとユーザー辞書パスの設定
        import sys
        if getattr(sys, 'frozen', False):
            self.base_dir = os.path.dirname(sys.executable)
        else:
            self.base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        self.user_dict_path = os.path.join(self.base_dir, "user_dict_en.txt")
        self._ensure_user_dictionary_template()
        
        # 感情・スタイルパラメータ（英語版向け精度向上調整：デフォルト 0.3）
        self.exaggeration_var = ctk.DoubleVar(value=0.3)
        self.cfg_weight_var = ctk.DoubleVar(value=0.3)
        self.temperature_var = ctk.DoubleVar(value=1.0)
        self.speed_var = ctk.DoubleVar(value=1.0) # 1.0 = 100%
        
        # UI構築
        self._setup_ui()
        
        # 初期状態は Standard なので、言語オプションは en に設定し無効化する
        try:
            self.lang_option.set("en")
            self.lang_option.configure(state="disabled")
        except Exception:
            pass
            
        # 音声再生用
        try:
            pygame.mixer.init()
        except Exception as e:
            print(f"警告: オーディオデバイスの初期化に失敗しました: {e}")

    def _ensure_user_dictionary_template(self):
        """ユーザー辞書のテンプレートファイルが存在しない場合は自動作成する"""
        if not os.path.exists(self.user_dict_path):
            try:
                template = (
                    "# C-Box TTS User Dictionary (English Edition)\n"
                    "# Format: Word,Read\n"
                    "# Lines starting with # are comments.\n"
                    "# Examples:\n"
                    "# AI,Artificial Intelligence\n"
                    "# ChatGPT,Chat Gee Pee Tee\n"
                    "# TTS,Text To Speech\n"
                )
                with open(self.user_dict_path, "w", encoding="utf-8") as f:
                    f.write(template)
            except Exception as e:
                print(f"Warning: Failed to create user dictionary template: {e}")

    def _setup_ui(self):
        # メインフレーム
        self.grid_columnconfigure(1, weight=1)
        self.grid_rowconfigure(0, weight=1)

        # サイドバー
        self.sidebar = ctk.CTkFrame(self, width=200, corner_radius=0)
        self.sidebar.grid(row=0, column=0, sticky="nsew")
        
        self.logo_label = ctk.CTkLabel(self.sidebar, text="C-Box TTS EN", font=ctk.CTkFont(size=20, weight="bold"))
        self.logo_label.pack(pady=20, padx=20)

        self.model_label = ctk.CTkLabel(self.sidebar, text="モデル設定")
        self.model_label.pack(pady=(10, 0))
        
        # 英語特化版のため、選択肢から Turbo を除外し Standard デフォルトにする
        self.model_option = ctk.CTkOptionMenu(self.sidebar, values=["Standard", "Multilingual"], command=self._on_model_change)
        self.model_option.pack(pady=10, padx=20)
        self.model_option.set("Standard")
        
        self.device_label = ctk.CTkLabel(self.sidebar, text="デバイス")
        self.device_label.pack(pady=(10, 0))
        self.device_option = ctk.CTkOptionMenu(self.sidebar, values=["cuda", "cpu"], command=self._on_device_change)
        self.device_option.pack(pady=10, padx=20)
        if not self.engine.device == "cuda":
            self.device_option.set("cpu")
        else:
            self.device_option.set("cuda")

        self.lang_label = ctk.CTkLabel(self.sidebar, text="対象言語 (Multilingual)")
        self.lang_label.pack(pady=(10, 0))
        # 英語(en)を最上位に配置
        self.lang_option = ctk.CTkOptionMenu(self.sidebar, values=[
            "en", "ja", "zh", "ko", "de", "fr", "it", "es", 
            "pt", "pl", "tr", "ru", "nl", "cs", "ar", "hu", 
            "hi", "el", "ro", "sv", "sk", "da", "fi"
        ])
        self.lang_option.pack(pady=10, padx=20)

        self.speed_label = ctk.CTkLabel(self.sidebar, text="話速 (Speed): 100%")
        self.speed_label.pack(pady=(20, 0))
        self.speed_slider = ctk.CTkSlider(self.sidebar, from_=0.1, to=2.0, variable=self.speed_var, command=self._on_speed_change)
        self.speed_slider.pack(pady=10, padx=20)
        
        self.settings_button = ctk.CTkButton(self.sidebar, text="詳細設定", command=self._open_settings)
        self.settings_button.pack(side="bottom", pady=20, padx=20)

        # メインコンテンツ
        self.content_frame = ctk.CTkFrame(self, corner_radius=10)
        self.content_frame.grid(row=0, column=1, sticky="nsew", padx=20, pady=20)
        self.content_frame.grid_columnconfigure(0, weight=1)
        self.content_frame.grid_rowconfigure(1, weight=1)

        # 参照音声
        self.ref_frame = ctk.CTkFrame(self.content_frame, fg_color="transparent")
        self.ref_frame.grid(row=0, column=0, sticky="ew", padx=20, pady=(20, 10))
        
        self.ref_path_var = ctk.StringVar(value="参照音声（クローニング用）を選択してください...")
        self.ref_entry = ctk.CTkEntry(self.ref_frame, textvariable=self.ref_path_var, width=400)
        self.ref_entry.pack(side="left", padx=(0, 10), fill="x", expand=True)
        
        self.ref_button = ctk.CTkButton(self.ref_frame, text="参照音声を選択", width=120, command=self._browse_ref_audio)
        self.ref_button.pack(side="right")

        # テキスト入力（英語サンプル文を設定）
        self.text_area = ctk.CTkTextbox(self.content_frame, font=ctk.CTkFont(size=14))
        self.text_area.grid(row=1, column=0, sticky="nsew", padx=20, pady=10)
        self.text_area.insert("0.0", "Hello, this is the English edition of C-Box TTS.\n\nType the text you want to read aloud here.\n- You can adjust the speed using the slider on the left.\n- Type [laugh] or [chuckle] to insert laughter into the speech.")
        self.text_area.bind("<KeyRelease>", self._on_text_change)

        # 文字数カウント
        self.char_count_label = ctk.CTkLabel(self.content_frame, text="文字数: 0", text_color="gray")
        self.char_count_label.grid(row=2, column=0, sticky="e", padx=25)

        # 進捗バーとステータス
        self.status_label = ctk.CTkLabel(self.content_frame, text="準備完了", text_color="gray")
        self.status_label.grid(row=3, column=0, sticky="w", padx=25)
        
        self.progress_bar = ctk.CTkProgressBar(self.content_frame)
        self.progress_bar.grid(row=4, column=0, sticky="ew", padx=20, pady=(0, 20))
        self.progress_bar.set(0)

        # 下部ボタン類
        self.button_frame = ctk.CTkFrame(self.content_frame, fg_color="transparent")
        self.button_frame.grid(row=5, column=0, sticky="ew", padx=20, pady=(0, 20))
        
        self.gen_button = ctk.CTkButton(self.button_frame, text="音声を生成", font=ctk.CTkFont(size=16, weight="bold"), height=40, command=self._start_generation)
        self.gen_button.pack(side="left", fill="x", expand=True, padx=(0, 10))
        
        self.batch_button = ctk.CTkButton(self.button_frame, text="一括処理", width=100, height=40, command=self._batch_process)
        self.batch_button.pack(side="left", padx=(0, 10))

        self.play_button = ctk.CTkButton(self.button_frame, text="再生", width=80, height=40, state="disabled", command=self._play_audio)
        self.play_button.pack(side="left", padx=(0, 10))
        
        self.save_button = ctk.CTkButton(self.button_frame, text="保存", width=80, height=40, state="disabled", command=self._save_audio)
        self.save_button.pack(side="left")

    def _set_ui_state(self, state="normal"):
        """UI要素の有効/無効を一括切り替え"""
        self.gen_button.configure(state=state)
        self.batch_button.configure(state=state)
        self.model_option.configure(state=state)
        self.device_option.configure(state=state)
        self.lang_option.configure(state=state)
        self.ref_button.configure(state=state)
        self.settings_button.configure(state=state)

    def _browse_ref_audio(self):
        file_path = filedialog.askopenfilename(filetypes=[("Audio Files", "*.wav *.mp3")])
        if file_path:
            self.ref_path_var.set(file_path)

    def _on_speed_change(self, value):
        self.speed_label.configure(text=f"話速 (Speed): {int(value * 100)}%")

    def _on_text_change(self, event=None):
        text = self.text_area.get("0.0", "end").strip()
        self.char_count_label.configure(text=f"文字数: {len(text)}")

    def _on_model_change(self, model_type):
        """モデル変更時の処理。ワーカースレッドでモデルをロードし、UIはメインスレッドで更新する。"""
        self._set_ui_state("disabled")

        def task():
            try:
                self.engine.load_model(model_type, self._update_progress)
                self.after(0, lambda: self._on_model_load_success(model_type))
            except Exception as e:
                import traceback
                error_trace = traceback.format_exc()
                self.after(0, lambda: self._on_model_load_error(error_trace))

        threading.Thread(target=task, daemon=True).start()

    def _on_model_load_success(self, model_type):
        """モデルロード成功時のUI更新（メインスレッドで実行）"""
        self._set_ui_state("normal")
        # モデルタイプに応じて言語オプションの有効・無効を切り替える
        if model_type == "Multilingual":
            self.lang_option.configure(state="normal")
        else:
            self.lang_option.configure(state="disabled")
        self._update_progress(f"モデル {model_type} をロードしました。", 1.0)
        messagebox.showinfo("成功", f"モデル {model_type} をロードしました。")

    def _on_model_load_error(self, error_trace):
        """モデルロードエラー時のUI更新（メインスレッドで実行）"""
        self._update_progress("エラーが発生しました", 0)
        error_msg = f"モデルのロードに失敗しました:\n\n{error_trace}"
        messagebox.showerror("エラー", error_msg)
        self._set_ui_state("normal")
        # モデルドロップダウンの表示を、現在エンジンにロードされているモデル（またはデフォルトのStandard）に戻す
        current_model = self.engine.model_type if self.engine.model_type else "Standard"
        self.model_option.set(current_model)
        # 言語オプションの状態も現在ロードされているモデルに合わせる
        if current_model == "Multilingual":
            self.lang_option.configure(state="normal")
        else:
            self.lang_option.configure(state="disabled")

    def _on_device_change(self, device):
        """デバイス変更時の処理。set_device内部のリロードを抑止し、明示的にリロードする。"""
        self.engine.device = device
        if self.engine.current_model is not None:
            # 現在のモデルをアンロードして新デバイスで再ロード
            current_type = self.engine.model_type
            self.engine.unload_model()
            self._on_model_change(current_type)

    def _update_progress(self, text, value):
        """進捗表示を更新する。メインスレッドからもワーカースレッドからも安全に呼べる。"""
        def _do_update():
            try:
                self.status_label.configure(text=text)
                self.progress_bar.set(value)
            except Exception:
                pass  # ウィンドウが閉じられた場合など
        # メインスレッドかどうかに関わらず安全に更新
        try:
            self.after(0, _do_update)
        except Exception:
            pass  # ウィンドウが破棄済みの場合

    def _start_generation(self):
        text = self.text_area.get("0.0", "end").strip()
        if not text:
            messagebox.showwarning("警告", "テキストを入力してください。")
            return
            
        ref_path = self.ref_path_var.get()
        if "選択してください" in ref_path:
            ref_path = None
            
        lang = self.lang_option.get()
        
        self._set_ui_state("disabled")
        self.play_button.configure(state="disabled")
        self.save_button.configure(state="disabled")
        
        threading.Thread(target=self._generate_task, args=(text, ref_path, lang), daemon=True).start()

    def _generate_task(self, text, ref_path, lang):
        """音声生成タスク（ワーカースレッドで実行）"""
        try:
            # モデルがロードされていない場合はデフォルト(Standard)をロード
            if self.engine.current_model is None:
                self.engine.load_model(self.model_option.get(), self._update_progress)

            # ユーザー辞書および正規化ツールのインポートと適用
            from .utils import load_user_dict, apply_user_dict, normalize_english_text
            rules = load_user_dict(self.user_dict_path)

            # 行（改行）ごとに分割
            raw_lines = [line.strip() for line in text.split("\n") if line.strip()]
            total_lines = len(raw_lines)
            
            self.current_audio_segments = []
            
            for i, line in enumerate(raw_lines):
                # テキスト正規化とユーザー辞書の適用
                line_normalized = normalize_english_text(line)
                line_processed = apply_user_dict(line_normalized, rules)

                # 各行をさらに文単位で分割（長文エラー防止・最適長分割）
                segments = split_text(line_processed)
                line_tensors = []
                
                for seg in segments:
                    self._update_progress(f"生成中 (行 {i+1}/{total_lines}): {seg[:20]}...", (i / total_lines))
                    tensor = self.engine.generate(
                        seg, 
                        audio_prompt_path=ref_path, 
                        language_id=lang,
                        exaggeration=self.exaggeration_var.get(),
                        cfg_weight=self.cfg_weight_var.get(),
                        temperature=self.temperature_var.get()
                    )
                    line_tensors.append(tensor)
                
                # 行内の音声を結合
                line_audio = merge_audio(line_tensors, self.engine.sample_rate, lead_in_sec=0, lead_out_sec=0)
                
                # 話速調整
                from .utils import adjust_speed
                line_audio = adjust_speed(line_audio, self.speed_var.get())
                
                self.current_audio_segments.append((line, line_audio))
                
            self._update_progress("音声を結合中...", 0.9)
            
            # 全体プレビュー用に結合した音声テンソルを作成
            all_tensors = [seg_audio for _, seg_audio in self.current_audio_segments]
            self.current_audio_tensor = merge_audio(all_tensors, self.engine.sample_rate)
            
            self.after(0, self._on_generate_success)
            
        except Exception as e:
            error_msg = f"{type(e).__name__}: {str(e)}"
            self.after(0, lambda: self._on_generate_error(error_msg))

    def _on_generate_success(self):
        """生成成功時のUI更新（メインスレッドで実行）"""
        self._update_progress("生成完了", 1.0)
        self._set_ui_state("normal")
        self.play_button.configure(state="normal")
        self.save_button.configure(state="normal")

    def _on_generate_error(self, error_msg):
        """生成エラー時のUI更新（メインスレッドで実行）"""
        self._update_progress(f"エラー: {error_msg}", 0)
        messagebox.showerror("生成エラー", error_msg)
        self._set_ui_state("normal")

    def _play_audio(self):
        if self.current_audio_tensor is not None:
            ensure_dir("temp")
            temp_path = "temp/preview.wav"
            
            # 再生中の音楽を停止し、ファイルをアンロードしてファイルロックを解除（PermissionError対策）
            try:
                if pygame.mixer.music.get_busy():
                    pygame.mixer.music.stop()
                pygame.mixer.music.unload()
            except Exception:
                pass
                
            save_wav(self.current_audio_tensor, self.engine.sample_rate, temp_path)
            pygame.mixer.music.load(temp_path)
            pygame.mixer.music.play()

    def _save_audio(self):
        if not hasattr(self, "current_audio_segments") or not self.current_audio_segments:
            if self.current_audio_tensor is not None:
                file_path = filedialog.asksaveasfilename(defaultextension=".wav", initialfile=f"output_{get_timestamp()}.wav")
                if file_path:
                    save_wav(self.current_audio_tensor, self.engine.sample_rate, file_path)
                    messagebox.showinfo("成功", f"音声を保存しました: {file_path}")
            return

        if len(self.current_audio_segments) == 1:
            _, tensor = self.current_audio_segments[0]
            file_path = filedialog.asksaveasfilename(defaultextension=".wav", initialfile=f"output_{get_timestamp()}.wav")
            if file_path:
                save_wav(tensor, self.engine.sample_rate, file_path)
                messagebox.showinfo("成功", f"音声を保存しました: {file_path}")
        else:
            file_path = filedialog.asksaveasfilename(
                defaultextension=".wav", 
                initialfile=f"output_{get_timestamp()}.wav",
                title="複数ファイルの保存先ベース名を入力"
            )
            if file_path:
                base, ext = os.path.splitext(file_path)
                saved_paths = []
                for idx, (text_line, tensor) in enumerate(self.current_audio_segments, start=1):
                    out_path = f"{base}_{idx:03d}{ext}"
                    save_wav(tensor, self.engine.sample_rate, out_path)
                    saved_paths.append(out_path)
                
                messagebox.showinfo(
                    "成功", 
                    f"複数行の音声を個別に保存しました（全 {len(saved_paths)} ファイル）:\n" + \
                    f"保存先: {os.path.dirname(file_path)}\n\n" + \
                    f"例: {os.path.basename(saved_paths[0])}"
                )

    def _batch_process(self):
        folder_path = filedialog.askdirectory(title="テキストファイルが含まれるフォルダを選択")
        if not folder_path:
            return
            
        files = [f for f in os.listdir(folder_path) if f.endswith(".txt")]
        if not files:
            messagebox.showwarning("警告", "選択したフォルダに .txt ファイルが見つかりません。")
            return
            
        output_dir = filedialog.askdirectory(title="保存先フォルダを選択")
        if not output_dir:
            return

        self._set_ui_state("disabled")

        def batch_task():
            try:
                ref_path = self.ref_path_var.get() if "選択してください" not in self.ref_path_var.get() else None
                lang = self.lang_option.get()
                
                # モデルがロードされていない場合はデフォルトをロード
                if self.engine.current_model is None:
                    self.engine.load_model(self.model_option.get(), self._update_progress)
                
                # ユーザー辞書および正規化ツールのインポートとロード
                from .utils import load_user_dict, apply_user_dict, normalize_english_text
                rules = load_user_dict(self.user_dict_path)

                for i, filename in enumerate(files):
                    file_path = os.path.join(folder_path, filename)
                    with open(file_path, "r", encoding="utf-8") as f:
                        text = f.read().strip()
                    
                    if not text: continue
                    
                    self._update_progress(f"一括処理中 ({i+1}/{len(files)}): {filename}", (i / len(files)))
                    
                    # 正規化とユーザー辞書の適用
                    text_normalized = normalize_english_text(text)
                    text_processed = apply_user_dict(text_normalized, rules)

                    segments = split_text(text_processed)
                    tensors = []
                    for seg in segments:
                        tensor = self.engine.generate(
                            seg, 
                            audio_prompt_path=ref_path, 
                            language_id=lang,
                            exaggeration=self.exaggeration_var.get(),
                            cfg_weight=self.cfg_weight_var.get(),
                            temperature=self.temperature_var.get()
                        )
                        tensors.append(tensor)
                    
                    combined = merge_audio(tensors, self.engine.sample_rate, lead_in_sec=0, lead_out_sec=0)
                    
                    # 話速調整
                    from .utils import adjust_speed
                    combined = adjust_speed(combined, self.speed_var.get())
                    
                    out_name = f"{os.path.splitext(filename)[0]}_{get_timestamp()}.wav"
                    save_wav(combined, self.engine.sample_rate, os.path.join(output_dir, out_name))
                
                self.after(0, lambda: self._on_batch_success(len(files), output_dir))
            except Exception as e:
                error_msg = f"{type(e).__name__}: {str(e)}"
                self.after(0, lambda: self._on_batch_error(error_msg))

        threading.Thread(target=batch_task, daemon=True).start()

    def _on_batch_success(self, file_count, output_dir):
        """一括処理成功時のUI更新（メインスレッドで実行）"""
        self._update_progress("一括処理完了", 1.0)
        self._set_ui_state("normal")
        messagebox.showinfo("完了", f"{file_count} 件の処理が完了しました。")
        from .utils import open_folder
        open_folder(output_dir)

    def _on_batch_error(self, error_msg):
        """一括処理エラー時のUI更新（メインスレッドで実行）"""
        self._update_progress(f"エラー: {error_msg}", 0)
        messagebox.showerror("一括処理エラー", error_msg)
        self._set_ui_state("normal")

    def _open_settings(self):
        settings_win = ctk.CTkToplevel(self)
        settings_win.title("詳細設定 / 感情表現")
        settings_win.geometry("450x450")
        settings_win.attributes("-topmost", True)
        
        main_label = ctk.CTkLabel(settings_win, text="詳細設定", font=ctk.CTkFont(size=16, weight="bold"))
        main_label.pack(pady=20)
        
        # Exaggeration
        exag_label = ctk.CTkLabel(settings_win, text=f"感情の強調度 (Exaggeration): {self.exaggeration_var.get():.1f}")
        exag_label.pack(pady=(10, 0))
        def update_exag(v): exag_label.configure(text=f"感情の強調度 (Exaggeration): {float(v):.1f}")
        exag_slider = ctk.CTkSlider(settings_win, from_=0.0, to=2.0, variable=self.exaggeration_var, command=update_exag)
        exag_slider.pack(pady=10, padx=40, fill="x")
        ctk.CTkLabel(settings_win, text="0.0: フラット, 0.5: 標準, 1.0+: ドラマチック", font=ctk.CTkFont(size=10)).pack()

        # CFG Weight
        cfg_label = ctk.CTkLabel(settings_win, text=f"抑揚の制御 (CFG Weight): {self.cfg_weight_var.get():.1f}")
        cfg_label.pack(pady=(20, 0))
        def update_cfg(v): cfg_label.configure(text=f"抑揚の制御 (CFG Weight): {float(v):.1f}")
        cfg_slider = ctk.CTkSlider(settings_win, from_=0.0, to=2.0, variable=self.cfg_weight_var, command=update_cfg)
        cfg_slider.pack(pady=10, padx=40, fill="x")
        ctk.CTkLabel(settings_win, text="低いと動的、高いと安定・単調", font=ctk.CTkFont(size=10)).pack()

        # Temperature
        temp_label = ctk.CTkLabel(settings_win, text=f"表現の多様性 (Temperature): {self.temperature_var.get():.1f}")
        temp_label.pack(pady=(20, 0))
        def update_temp(v): temp_label.configure(text=f"表現の多様性 (Temperature): {float(v):.1f}")
        temp_slider = ctk.CTkSlider(settings_win, from_=0.0, to=2.0, variable=self.temperature_var, command=update_temp)
        temp_slider.pack(pady=10, padx=40, fill="x")
        ctk.CTkLabel(settings_win, text="高いと表現が変化しやすく、低いと一貫性が増す", font=ctk.CTkFont(size=10)).pack()

        tip_label = ctk.CTkLabel(settings_win, text="ヒント: テキストに [laugh] や [chuckle] を入れると\n笑い声を混ぜることができます。", font=ctk.CTkFont(size=12), text_color="gray")
        tip_label.pack(pady=20)
        
        close_btn = ctk.CTkButton(settings_win, text="閉じる", command=settings_win.destroy)
        close_btn.pack(pady=20)

if __name__ == "__main__":
    app = App()
    app.mainloop()
