import re
import os
import torch
import torchaudio
import librosa
from datetime import datetime

def split_text(text: str):
    """
    テキストを日本語の句読点および英語のピリオド等で分割する。
    """
    # 句読点を保持しつつ分割。連続する句読点（！？など）を一つの区切りとして扱う
    segments = re.split(r'([。！？\n\.!\?]+)', text)
    
    combined_segments = []
    
    # re.split(r'(...)') は [text, sep, text, sep, ...] の形式になる
    i = 0
    while i < len(segments):
        seg = segments[i].strip()
        if i + 1 < len(segments):
            sep = segments[i+1]
            if seg or sep.strip():
                combined_segments.append(seg + sep)
            i += 2
        else:
            if seg:
                combined_segments.append(seg)
            i += 1
            
    return [s for s in combined_segments if s.strip()]

def merge_audio(tensors, sample_rate, silence_sec=0.3):
    """
    複数の音声テンソルを、指定した秒数の無音を挟んで連結する。
    """
    if not tensors:
        return None
    
    silence_len = int(sample_rate * silence_sec)
    silence = torch.zeros((1, silence_len))
    
    combined = []
    for i, t in enumerate(tensors):
        # 2次元 (1, N) に統一
        if t.ndim == 1:
            t = t.unsqueeze(0)
        combined.append(t)
        if i < len(tensors) - 1:
            combined.append(silence)
            
    return torch.cat(combined, dim=1)

def save_wav(tensor, sample_rate, output_path):
    """
    音声テンソルをWAVとして保存する。
    """
    if tensor.ndim == 1:
        tensor = tensor.unsqueeze(0)
    torchaudio.save(output_path, tensor, sample_rate)

def get_timestamp():
    """
    ファイル名用のタイムスタンプを取得。
    """
    return datetime.now().strftime("%Y%m%d_%H%M%S")

def ensure_dir(path):
    """
    ディレクトリが存在することを確認する。
    """
    if not os.path.exists(path):
        os.makedirs(path)

def open_folder(path):
    """
    OSに応じた方法でフォルダを開く。
    """
    import platform
    import subprocess
    
    if platform.system() == "Windows":
        os.startfile(path)
    elif platform.system() == "Darwin": # macOS
        subprocess.run(["open", path])
    else: # Linux
        subprocess.run(["xdg-open", path])

def adjust_speed(audio_tensor, speed):
    """
    音声の速度を調整する（ピッチ維持）。
    speed: 1.0 が標準。
    """
    if speed == 1.0:
        return audio_tensor
        
    # numpyに変換
    y = audio_tensor.cpu().numpy()
    if y.ndim > 1:
        y = y.squeeze()
        
    # タイムストレッチ
    y_stretched = librosa.effects.time_stretch(y, rate=speed)
    
    # tensorに戻す
    return torch.from_numpy(y_stretched).unsqueeze(0)
