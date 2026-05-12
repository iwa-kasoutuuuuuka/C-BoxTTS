import torch
import gc
from chatterbox.tts_turbo import ChatterboxTurboTTS
from chatterbox.tts import ChatterboxTTS
from chatterbox.mtl_tts import ChatterboxMultilingualTTS
from huggingface_hub import snapshot_download
import os
import traceback
import sys
import io

# 標準出力をUTF-8に設定（絵文字などによるcp932エラーを回避）
if sys.stdout is not None:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
if sys.stderr is not None:
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

# HF Hubのシンボリックリンク問題を回避（Windowsのポータブル環境で有効）
os.environ["HF_HUB_DISABLE_SYMLINKS"] = "1"

class TTSEngine:
    def __init__(self, device=None):
        if device is None:
            self.device = "cuda" if torch.cuda.is_available() else "cpu"
        else:
            self.device = device
            
        self.current_model = None
        self.model_type = None # "Turbo", "Standard", "Multilingual"
        self.sample_rate = 24000 # デフォルト
        
    def load_model(self, model_type, progress_callback=None):
        """
        モデルをロードする。既存のモデルはアンロードする。
        """
        if self.model_type == model_type and self.current_model is not None:
            return
            
        self.unload_model()
        
        if progress_callback:
            progress_callback("モデルをダウンロード/ロード中... (数GBかかる場合があります)", 0.1)

        try:
            if model_type == "Turbo":
                self.current_model = ChatterboxTurboTTS.from_pretrained(device=self.device)
                self.model_type = "Turbo"
            elif model_type == "Standard":
                self.current_model = ChatterboxTTS.from_pretrained(device=self.device)
                self.model_type = "Standard"
            elif model_type == "Multilingual":
                self.current_model = ChatterboxMultilingualTTS.from_pretrained(
                    device=self.device
                )
                self.model_type = "Multilingual"
            
            if progress_callback:
                progress_callback("モデルのロードが完了しました。", 1.0)
                
        except Exception as e:
            # スタックトレースを詳細に記録（EXEと同じフォルダに出力）
            error_trace = traceback.format_exc()
            log_path = os.path.join(os.path.dirname(sys.executable), "error_log.txt")
            try:
                with open(log_path, "a", encoding="utf-8") as f:
                    f.write(f"\n--- Model Load Error ({model_type}) ---\n")
                    f.write(error_trace)
            except:
                pass # 書き込み失敗は無視
            
            if progress_callback:
                progress_callback(f"エラー: {str(e)}", 0)
            raise e

    def unload_model(self):
        """
        VRAMを解放するためにモデルを削除する。
        """
        if self.current_model is not None:
            del self.current_model
            self.current_model = None
            self.model_type = None
            gc.collect()
            if torch.cuda.is_available():
                torch.cuda.empty_cache()

    def generate(self, text, audio_prompt_path=None, language_id="ja", exaggeration=0.5, cfg_weight=0.5, temperature=1.0):
        if self.current_model is None:
            raise Exception("モデルがロードされていません。")
            
        gen_kwargs = {"text": text, "audio_prompt_path": audio_prompt_path}
        if self.model_type == "Multilingual":
            gen_kwargs["language_id"] = language_id
        
        try:
            return self.current_model.generate(
                **gen_kwargs,
                exaggeration=exaggeration,
                cfg_weight=cfg_weight,
                temperature=temperature
            )
        except TypeError:
            return self.current_model.generate(**gen_kwargs)

    def set_device(self, device):
        if self.device != device:
            self.device = device
            # デバイス変更時はリロードが必要
            if self.model_type:
                m_type = self.model_type
                self.load_model(m_type)
