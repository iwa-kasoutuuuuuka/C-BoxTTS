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
        self.title("C-Box TTS Japanese Edition")
        self.geometry("900x700")
        ctk.set_appearance_mode("dark")
        ctk.set_default_color_theme("blue")

        # エンジン初期化
        self.engine = TTSEngine()
        self.current_audio_tensor = None
        
        # 感情・スタイルパラメータ
        self.exaggeration_var = ctk.DoubleVar(value=0.5)
        self.cfg_weight_var = ctk.DoubleVar(value=0.5)
        self.temperature_var = ctk.DoubleVar(value=1.0)
        
        # UI構築
        self._setup_ui()
        
        # 音声再生用
        try:
            pygame.mixer.init()
        except Exception as e:
            print(f"警告: オーディオデバイスの初期化に失敗しました: {e}")

    def _setup_ui(self):
        # メインフレーム
        self.grid_columnconfigure(1, weight=1)
        self.grid_rowconfigure(0, weight=1)

        # サイドバー
        self.sidebar = ctk.CTkFrame(self, width=200, corner_radius=0)
        self.sidebar.grid(row=0, column=0, sticky="nsew")
        
        self.logo_label = ctk.CTkLabel(self.sidebar, text="C-Box TTS", font=ctk.CTkFont(size=20, weight="bold"))
        self.logo_label.pack(pady=20, padx=20)

        self.model_label = ctk.CTkLabel(self.sidebar, text="モデル設定")
        self.model_label.pack(pady=(10, 0))
        
        self.model_option = ctk.CTkOptionMenu(self.sidebar, values=["Turbo", "Standard", "Multilingual"], command=self._on_model_change)
        self.model_option.pack(pady=10, padx=20)
        
        self.device_label = ctk.CTkLabel(self.sidebar, text="デバイス")
        self.device_label.pack(pady=(10, 0))
        self.device_option = ctk.CTkOptionMenu(self.sidebar, values=["cuda", "cpu"], command=self._on_device_change)
        self.device_option.pack(pady=10, padx=20)
        if not self.engine.device == "cuda":
            self.device_option.set("cpu")
        else:
            self.device_option.set("cuda")

        self.lang_label = ctk.CTkLabel(self.sidebar, text="言語 (Multilingualのみ)")
        self.lang_label.pack(pady=(10, 0))
        self.lang_option = ctk.CTkOptionMenu(self.sidebar, values=["ja", "en", "zh", "ko", "de", "fr", "it", "es", "pt", "pl", "tr", "ru", "nl", "cs", "ar", "hu", "hi", "he", "vi", "th", "id", "sv", "da"])
        self.lang_option.pack(pady=10, padx=20)

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

        # テキスト入力
        self.text_area = ctk.CTkTextbox(self.content_frame, font=ctk.CTkFont(size=14))
        self.text_area.grid(row=1, column=0, sticky="nsew", padx=20, pady=10)
        self.text_area.insert("0.0", "ここに読み上げたいテキストを入力してください。")

        # 進捗バーとステータス
        self.status_label = ctk.CTkLabel(self.content_frame, text="準備完了", text_color="gray")
        self.status_label.grid(row=2, column=0, sticky="w", padx=25)
        
        self.progress_bar = ctk.CTkProgressBar(self.content_frame)
        self.progress_bar.grid(row=3, column=0, sticky="ew", padx=20, pady=(0, 20))
        self.progress_bar.set(0)

        # 下部ボタン類
        self.button_frame = ctk.CTkFrame(self.content_frame, fg_color="transparent")
        self.button_frame.grid(row=4, column=0, sticky="ew", padx=20, pady=(0, 20))
        
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

    def _on_model_change(self, model_type):
        def task():
            try:
                self._set_ui_state("disabled")
                self.engine.load_model(model_type, self._update_progress)
                self._set_ui_state("normal")
                messagebox.showinfo("成功", f"モデル {model_type} をロードしました。")
            except Exception as e:
                # スタックトレースを取得して表示
                import traceback
                error_trace = traceback.format_exc()
                self._update_progress(f"エラー: {type(e).__name__}", 0)
                # メッセージボックスに詳細を表示
                error_msg = f"モデルのロードに失敗しました:\n\n{error_trace}"
                messagebox.showerror("エラー", error_msg)
                self._set_ui_state("normal")

        threading.Thread(target=task, daemon=True).start()

    def _on_device_change(self, device):
        self.engine.set_device(device)
        self._on_model_change(self.model_option.get())

    def _update_progress(self, text, value):
        self.status_label.configure(text=text)
        self.progress_bar.set(value)
        self.update_idletasks()

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
        try:
            # モデルがロードされていない場合はデフォルト(Turbo)をロード
            if self.engine.current_model is None:
                self.engine.load_model(self.model_option.get(), self._update_progress)

            # 長文分割
            segments = split_text(text)
            total = len(segments)
            tensors = []
            
            for i, seg in enumerate(segments):
                self._update_progress(f"生成中 ({i+1}/{total}): {seg[:20]}...", (i / total))
                tensor = self.engine.generate(
                    seg, 
                    audio_prompt_path=ref_path, 
                    language_id=lang,
                    exaggeration=self.exaggeration_var.get(),
                    cfg_weight=self.cfg_weight_var.get(),
                    temperature=self.temperature_var.get()
                )
                tensors.append(tensor)
                
            self._update_progress("音声を結合中...", 0.9)
            self.current_audio_tensor = merge_audio(tensors, self.engine.sample_rate)
            
            self._update_progress("生成完了", 1.0)
            self._set_ui_state("normal")
            self.play_button.configure(state="normal")
            self.save_button.configure(state="normal")
            
        except Exception as e:
            error_msg = f"{type(e).__name__}: {str(e)}"
            self._update_progress(f"エラー: {error_msg}", 0)
            messagebox.showerror("生成エラー", error_msg)
            self._set_ui_state("normal")

    def _play_audio(self):
        if self.current_audio_tensor is not None:
            ensure_dir("temp")
            temp_path = "temp/preview.wav"
            save_wav(self.current_audio_tensor, self.engine.sample_rate, temp_path)
            pygame.mixer.music.load(temp_path)
            pygame.mixer.music.play()

    def _save_audio(self):
        if self.current_audio_tensor is not None:
            file_path = filedialog.asksaveasfilename(defaultextension=".wav", initialfile=f"output_{get_timestamp()}.wav")
            if file_path:
                save_wav(self.current_audio_tensor, self.engine.sample_rate, file_path)
                messagebox.showinfo("成功", f"音声を保存しました: {file_path}")

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

        def batch_task():
            self._set_ui_state("disabled")
            ref_path = self.ref_path_var.get() if "選択してください" not in self.ref_path_var.get() else None
            lang = self.lang_option.get()
            
            for i, filename in enumerate(files):
                file_path = os.path.join(folder_path, filename)
                with open(file_path, "r", encoding="utf-8") as f:
                    text = f.read().strip()
                
                if not text: continue
                
                self._update_progress(f"一括処理中 ({i+1}/{len(files)}): {filename}", (i / len(files)))
                
                segments = split_text(text)
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
                
                combined = merge_audio(tensors, self.engine.sample_rate)
                out_name = f"{os.path.splitext(filename)[0]}_{get_timestamp()}.wav"
                save_wav(combined, self.engine.sample_rate, os.path.join(output_dir, out_name))
            
            self._update_progress("一括処理完了", 1.0)
            self._set_ui_state("normal")
            messagebox.showinfo("完了", f"{len(files)} 件の処理が完了しました。")
            from .utils import open_folder
            open_folder(output_dir)

        threading.Thread(target=batch_task, daemon=True).start()

    def _open_settings(self):
        settings_win = ctk.CTkToplevel(self)
        settings_win.title("詳細設定 / 感情表現")
        settings_win.geometry("450x450")
        settings_win.attributes("-topmost", True)
        
        main_label = ctk.CTkLabel(settings_win, text="詳細設定", font=ctk.CTkFont(size=16, weight="bold"))
        main_label.pack(pady=20)
        
        # Exaggeration
        ctk.CTkLabel(settings_win, text=f"感情の強調度 (Exaggeration): {self.exaggeration_var.get():.1f}").pack(pady=(10, 0))
        exag_slider = ctk.CTkSlider(settings_win, from_=0.0, to_=2.0, variable=self.exaggeration_var)
        exag_slider.pack(pady=10, padx=40, fill="x")
        ctk.CTkLabel(settings_win, text="0.0: フラット, 0.5: 標準, 1.0+: ドラマチック", font=ctk.CTkFont(size=10)).pack()

        # CFG Weight
        ctk.CTkLabel(settings_win, text=f"抑揚の制御 (CFG Weight): {self.cfg_weight_var.get():.1f}").pack(pady=(20, 0))
        cfg_slider = ctk.CTkSlider(settings_win, from_=0.0, to_=2.0, variable=self.cfg_weight_var)
        cfg_slider.pack(pady=10, padx=40, fill="x")
        ctk.CTkLabel(settings_win, text="低いと動的、高いと安定・単調", font=ctk.CTkFont(size=10)).pack()

        # Temperature
        ctk.CTkLabel(settings_win, text=f"表現の多様性 (Temperature): {self.temperature_var.get():.1f}").pack(pady=(20, 0))
        temp_slider = ctk.CTkSlider(settings_win, from_=0.0, to_=2.0, variable=self.temperature_var)
        temp_slider.pack(pady=10, padx=40, fill="x")
        ctk.CTkLabel(settings_win, text="高いと表現が変化しやすく、低いと一貫性が増す", font=ctk.CTkFont(size=10)).pack()

        tip_label = ctk.CTkLabel(settings_win, text="ヒント: テキストに [laugh] や [chuckle] を入れると\n笑い声を混ぜることができます。", font=ctk.CTkFont(size=12), text_color="gray")
        tip_label.pack(pady=20)
        
        close_btn = ctk.CTkButton(settings_win, text="閉じる", command=settings_win.destroy)
        close_btn.pack(pady=20)

if __name__ == "__main__":
    app = App()
    app.mainloop()
