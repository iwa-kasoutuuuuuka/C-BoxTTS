import torch
import gc
import os
import traceback
import sys
import io

# 標準出力をUTF-8に設定（絵文字などによるcp932エラーを回避）
if sys.stdout is not None:
    try:
        sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    except AttributeError:
        pass  # すでにラップ済みまたはbufferがない場合
if sys.stderr is not None:
    try:
        sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')
    except AttributeError:
        pass

# HF Hubのシンボリックリンク問題を回避（Windowsのポータブル環境で有効）
os.environ["HF_HUB_DISABLE_SYMLINKS"] = "1"

class TTSEngine:
    """TTS音声合成エンジン。chatterboxモジュールは遅延インポートで読み込む。"""

    def __init__(self, device=None):
        if device is None:
            self.device = "cuda" if torch.cuda.is_available() else "cpu"
        else:
            self.device = device
            
        # CPU推論時のスレッド競合とオーバーヘッドを抑制する最適化
        if self.device == "cpu":
            try:
                # 通常4〜8スレッドが最も効率が良い
                torch.set_num_threads(4)
            except Exception as e:
                print(f"Warning: Failed to set PyTorch thread count: {e}")
            
        self.current_model = None
        self.model_type = None  # "Turbo", "Standard", "Multilingual"
        self.sample_rate = 24000  # デフォルト
        
    def _import_model_class(self, model_type):
        """
        モデルクラスを遅延インポートする。
        起動時にchatterbox/transformersの互換性問題でクラッシュすることを防ぐ。
        """
        if model_type == "Turbo":
            from chatterbox.tts_turbo import ChatterboxTurboTTS
            return ChatterboxTurboTTS
        elif model_type == "Standard":
            from chatterbox.tts import ChatterboxTTS
            return ChatterboxTTS
        elif model_type == "Multilingual":
            from chatterbox.mtl_tts import ChatterboxMultilingualTTS
            return ChatterboxMultilingualTTS
        else:
            raise ValueError(f"不明なモデルタイプ: {model_type}")

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
            # 遅延インポートでモデルクラスを取得
            model_class = self._import_model_class(model_type)
            self.current_model = model_class.from_pretrained(device=self.device)
            self.model_type = model_type
            
            if progress_callback:
                progress_callback("モデルのロードが完了しました。", 1.0)
                
        except Exception as e:
            # スタックトレースを詳細に記録（EXEと同じフォルダに出力）
            error_trace = traceback.format_exc()
            try:
                log_path = os.path.join(os.path.dirname(sys.executable), "error_log.txt")
                with open(log_path, "a", encoding="utf-8") as f:
                    f.write(f"\n--- Model Load Error ({model_type}) ---\n")
                    f.write(error_trace)
            except Exception:
                pass  # 書き込み失敗は無視
            
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
        """テキストから音声を生成する。"""
        if self.current_model is None:
            raise Exception("モデルがロードされていません。")
            
        gen_kwargs = {"text": text, "audio_prompt_path": audio_prompt_path}
        if self.model_type == "Multilingual":
            gen_kwargs["language_id"] = language_id
        
        try:
            # GPU使用時は自動混合精度 (Autocast/AMP) を用いて高速化
            if self.device == "cuda":
                with torch.amp.autocast(device_type="cuda"):
                    return self.current_model.generate(
                        **gen_kwargs,
                        exaggeration=exaggeration,
                        cfg_weight=cfg_weight,
                        temperature=temperature
                    )
            else:
                return self.current_model.generate(
                    **gen_kwargs,
                    exaggeration=exaggeration,
                    cfg_weight=cfg_weight,
                    temperature=temperature
                )
        except TypeError:
            # 一部パラメータ未対応のモデル向けフォールバック
            if self.device == "cuda":
                with torch.amp.autocast(device_type="cuda"):
                    return self.current_model.generate(**gen_kwargs)
            else:
                return self.current_model.generate(**gen_kwargs)

    def set_device(self, device):
        """デバイスを変更する。"""
        if self.device != device:
            self.device = device
            # デバイス変更時はリロードが必要
            if self.model_type:
                m_type = self.model_type
                self.load_model(m_type)
