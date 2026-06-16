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
            
    # さらに長文に対する追加分割（英語のコンマ、セミコロン、コロン、ダッシュによる分割）
    # 1文が長く（目安として100文字超）、かつ英語の区切り文字が含まれる場合に再分割する
    final_segments = []
    for s in combined_segments:
        s_strip = s.strip()
        if not s_strip:
            continue
        
        # 簡易英文判定：アルファベットとスペースの割合が5割以上
        is_english = len(re.findall(r'[a-zA-Z\s]', s_strip)) > (len(s_strip) * 0.5)
        
        if is_english and len(s_strip) > 100 and any(c in s_strip for c in [',', ';', ':', '--']):
            # コンマ、コロン、セミコロン、ダッシュで分割
            sub_segs = re.split(r'([,;:、，；：]|--)', s_strip)
            temp = []
            j = 0
            while j < len(sub_segs):
                sub_txt = sub_segs[j].strip()
                if j + 1 < len(sub_segs):
                    sub_sep = sub_segs[j+1]
                    if sub_txt or sub_sep.strip():
                        # 音声の自然な一休みのためにカンマの後に空白を挿入
                        temp.append(sub_txt + sub_sep + " ")
                    j += 2
                else:
                    if sub_txt:
                        temp.append(sub_txt)
                    j += 1
            
            # 各サブセグメントがあまりに短くなりすぎないよう結合する（目安として40文字未満なら結合）
            current_seg = ""
            for t in temp:
                if len(current_seg) + len(t) < 40:
                    current_seg += t
                else:
                    if current_seg:
                        final_segments.append(current_seg.strip())
                    current_seg = t
            if current_seg:
                final_segments.append(current_seg.strip())
        else:
            final_segments.append(s_strip)
            
    return [s for s in final_segments if s.strip()]

def merge_audio(tensors, sample_rate, silence_sec=0.3, lead_in_sec=0.2, lead_out_sec=0.1):
    """
    複数の音声テンソルを、指定した秒数の無音を挟んで連結する。
    先頭にリードイン無音、末尾にリードアウト無音を追加し、
    音声プレーヤーのバッファリング遅延による冒頭の聞き漏れを防止する。
    """
    # None や空テンソル、非テンソルを除外するガード
    valid_tensors = []
    if tensors:
        for t in tensors:
            if t is not None:
                if isinstance(t, torch.Tensor) and t.numel() > 0:
                    valid_tensors.append(t)
                    
    if not valid_tensors:
        return None
    
    silence_len = int(sample_rate * silence_sec)
    silence = torch.zeros((1, silence_len))
    
    combined = []
    
    # 先頭にリードイン無音を追加（冒頭カット防止）
    if lead_in_sec > 0:
        lead_in = torch.zeros((1, int(sample_rate * lead_in_sec)))
        combined.append(lead_in)
    
    for i, t in enumerate(valid_tensors):
        # 2次元 (1, N) に統一
        if t.ndim == 1:
            t = t.unsqueeze(0)
        combined.append(t)
        if i < len(valid_tensors) - 1:
            combined.append(silence)
    
    # 末尾にリードアウト無音を追加（自然な終端）
    if lead_out_sec > 0:
        lead_out = torch.zeros((1, int(sample_rate * lead_out_sec)))
        combined.append(lead_out)
            
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
        
    # 極端に短い音声（0.1秒未満 = 2400サンプル未満）は処理をスキップして音切れを防ぐ
    if len(y) < 2400:
        return audio_tensor

    # メモリを連続化し、float32型を保証（処理の高速化）
    import numpy as np
    y = np.ascontiguousarray(y, dtype=np.float32)

    # 端数の欠落を防ぐため、前後に0.5秒分のパディング（無音）を追加
    pad_len = 12000
    y_padded = np.pad(y, pad_len, mode='constant')

    # タイムストレッチを実行
    y_stretched = librosa.effects.time_stretch(y_padded, rate=speed)

    # 速度に合わせて伸縮したパディング部分をカット
    start_cut = int(pad_len / speed)
    end_cut = int(pad_len / speed)
    y_final = y_stretched[start_cut : len(y_stretched) - end_cut]

    # tensorに戻す
    return torch.from_numpy(y_final).unsqueeze(0)

def normalize_english_text(text: str) -> str:
    """
    英語テキストの略語の展開、数値表現の簡易変換、表記揺れのクリーンアップを行い、
    TTSエンジンの発音精度を向上させる。
    """
    # 1. 略語の置換（単語境界を意識）
    replacements = {
        r'\bMr\b\.?': 'Mister',
        r'\bMrs\b\.?': 'Misses',
        r'\bMs\b\.?': 'Miss',
        r'\bDr\b\.?': 'Doctor',
        r'\bSt\b\.?': 'Street',
        r'\bCo\b\.?': 'Company',
        r'\bCorp\b\.?': 'Corporation',
        r'\betc\b\.?': 'et cetera',
        r'\beg\b\.?': 'for example',
        r'\bie\b\.?': 'that is',
        r'\bvs\b\.?': 'versus',
        r'\bVol\b\.?': 'Volume',
        r'\bCh\b\.?': 'Chapter',
        r'\bNo\b\.?': 'Number',
        r'\bJan\b\.?': 'January',
        r'\bFeb\b\.?': 'February',
        r'\bMar\b\.?': 'March',
        r'\bApr\b\.?': 'April',
        r'\bJun\b\.?': 'June',
        r'\bJul\b\.?': 'July',
        r'\bAug\b\.?': 'August',
        r'\bSep\b\.?': 'September',
        r'\bOct\b\.?': 'October',
        r'\bNov\b\.?': 'November',
        r'\bDec\b\.?': 'December',
        r'\bpp\b\.': 'pages',
        r'\bp\b\.': 'page',
    }
    
    for pattern, replacement in replacements.items():
        text = re.sub(pattern, replacement, text, flags=re.IGNORECASE)
        
    # 2. 記号の簡易的な展開
    text = re.sub(r'\s+&\s+', ' and ', text)
    text = re.sub(r'\b&\b', 'and', text)
    text = re.sub(r'%', ' percent', text)
    
    # 3. 連続する余分なスペースの整理
    text = re.sub(r'\s+', ' ', text).strip()
    
    return text

def load_user_dict(dict_path: str):
    """
    ユーザー定義の置換辞書をロードする（CSV形式: 置換前,置換後）
    """
    user_rules = []
    if os.path.exists(dict_path):
        try:
            with open(dict_path, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if not line or line.startswith("#"):
                        continue
                    parts = line.split(",", 1)
                    if len(parts) == 2:
                        user_rules.append((parts[0].strip(), parts[1].strip()))
        except Exception as e:
            print(f"User dictionary load error: {e}")
    return user_rules

def apply_user_dict(text: str, rules) -> str:
    """
    ユーザー定義辞書による置換を適用する。単語境界または単純置換を行う。
    """
    for word, read in rules:
        if word.isalnum():
            # 英語アルファベット単語の場合は境界を意識
            pattern = rf'\b{re.escape(word)}\b'
            text = re.sub(pattern, read, text, flags=re.IGNORECASE)
        else:
            text = text.replace(word, read)
    return text
