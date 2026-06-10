# C-Box TTS Japanese Native Edition 仕様書 (ポータブル版)

本ソフトウェアは、Python 環境や複雑な外部依存を一切必要とせず、Windows 上で完全ローカルかつ超軽量・高速に動作する .NET/C# ネイティブ音声合成アプリケーション「C-Box TTS Japanese Edition」のポータブル配信パッケージです。

DirectML を採用し、NVIDIA、AMD、Intel 等の各種 GPU によるハードウェアアクセラレーションをネイティブにサポートしています。

---

## 1. パッケージフォルダ構成

`Release_Portable/` 内は以下のように構成されています。

```text
Release_Portable/
├── CBoxTTS.Native.exe       # メイン実行ファイル (GUI / CLI 両対応)
├── CBoxTTS.Native.dll       # コアロジックライブラリ
├── onnxruntime.dll          # ONNX Runtime 推論エンジン (C++ ネイティブ)
├── DirectML.dll             # DirectML テンソル演算ライブラリ (GPU加速用)
├── MeCab.DotNet.dll         # C#用 MeCab バインディング
├── dic/                     # 形態素解析 (MeCab IPADIC) 辞書フォルダ
│   ├── sys.dic              # システム辞書
│   └── (その他の関連辞書ファイル)
├── models/                  # 音声合成 ONNX AI モデルフォルダ
│   ├── tokenizer_mtl.json   # 多言語トークナイザー辞書
│   ├── speech_encoder_mtl.onnx        # 音声エンコーダーモデル
│   ├── speech_encoder_mtl.onnx_data   # 同上 ウェイトデータ
│   ├── embed_tokens_mtl.onnx          # トークン埋め込みモデル
│   ├── embed_tokens_mtl.onnx_data     # 同上 ウェイトデータ
│   ├── conditional_decoder_mtl.onnx   # 条件付きデコーダーモデル
│   ├── conditional_decoder_mtl.onnx_data # 同上 ウェイトデータ
│   ├── language_model_q4.onnx         # 自己回帰言語モデル (INT4量子化)
│   └── language_model_q4.onnx_data    # 同上 ウェイトデータ
└── debug.log                # [自動作成] 実行トレース・デバッグログ
```

---

## 2. システムの特徴・独自改善

### ① 濁音・半濁音の分離バグ修正 (Unicode NFC 正規化)
C# の標準トークナイザー処理において濁音（例：「が」）や半濁音（例：「ぱ」）が清音と濁点・半濁点（例：「か」＋「 ゙」）に分離され、モデルのトークン埋め込み層でインデックス範囲外エラーを引き起こす不具合を解消しました。  
Unicode 正規化形式に **NFC (FormC)** を導入したことにより、文字の結合状態を完璧に維持し、極めて高品質で安定したエンコードを保証します。

### ② 境界インデックス超過セーフガード (安全装置)
トークナイザーが `tokenizer_mtl.json` から文字IDを引き当てる際、ONNXモデルの埋め込み層上限インデックス（`2351`）を超える未知文字や全角記号（例：全角の「？」）が検出された場合、クラッシュを防止するために自動的に未知語トークン `1 (UNK)` にクリップ・マッピングする安全ガード回路を搭載しています。

### ③ 100% 完全オフライン・ローカル動作保証
モデルのローカル保存ファイルに `_mtl` サフィックスを付与し、ビルド時に自動的にマッピング・コピーするパッケージシステムを導入しました。これにより、初回起動時に Hugging Face からのギガバイト単位の巨大ファイルダウンロードが一切発生せず、完全なスタンドアロン（オフライン）環境での即時起動が可能です。

---

## 3. 使用方法

### GUI (ウィンドウ起動)
`CBoxTTS.Native.exe` をダブルクリックして起動します。  
美麗なモダン WPF UI が表示され、テキストの入力、パラメータ（話速、抑揚、感情等）の調整、およびリアルタイムでの高品質な音声合成・ファイル出力・再生が簡単に行えます。

### CLI テストハーネス (コマンドライン起動)
コマンドプロンプトまたは PowerShell から以下の引数を指定して起動することで、GUI を表示させずに一気通貫の合成合成テストを実行し、動作確認用の WAV ファイルを出力できます。

```powershell
.\CBoxTTS.Native.exe --test
```

*   **動作ログ**: `test_harness.log` に処理ステップ（形態素解析 -> トークナイズ -> 推論 -> 音声出力）の全記録が出力されます。
*   **出力ファイル**: 合成された音声が `test_harness_out.wav` としてカレントディレクトリに保存されます。

---

## 4. 動作要件

*   **OS**: Windows 10 / 11 (x64)
*   **メモリ**: 8GB 以上推奨
*   **GPU**: DirectX 12 対応 GPU (NVIDIA GeForce, AMD Radeon, Intel Arc / Iris など)
*   **ランタイム**: 不要 (.NET 10.0 ランタイムはアプリケーションに完全内包されています)
