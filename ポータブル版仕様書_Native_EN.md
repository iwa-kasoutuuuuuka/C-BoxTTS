# C-Box TTS Native English Edition 仕様書 (ポータブル版)

本ソフトウェアは、Python 環境や複雑な外部依存を一切必要とせず、Windows 上で完全ローカルかつ超軽量・高速に動作する .NET/C# ネイティブ音声合成アプリケーション「C-Box TTS English Edition」の英語用ポータブル配信パッケージです。

DirectML を採用し、NVIDIA、AMD、Intel 等の各種 GPU によるハードウェアアクセラレーションをネイティブにサポートしています。

---

## 1. パッケージフォルダ構成

`Release_Portable_EN/` 内は以下のように構成されています。

```text
Release_Portable_EN/
├── CBoxTTS.Native.EN.exe    # 英語版メイン実行ファイル (GUI / CLI 両対応)
├── CBoxTTS.Native.dll       # コアロジックライブラリ
├── onnxruntime.dll          # ONNX Runtime 推論エンジン (C++ ネイティブ)
├── DirectML.dll             # DirectML テンソル演算ライブラリ (GPU加速用)
├── models/                  # 音声合成 ONNX AI モデルフォルダ
│   ├── default_voice.wav    # デフォルトの参照音声（クローニング用）
│   ├── english/             # 英語専用・高品質モデル (ONNX)
│   └── multilingual/        # 多言語モデル (ONNX)
└── debug.log                # [自動作成] 実行トレース・デバッグログ
```

> [!NOTE]
> **MeCabおよび辞書フォルダの排除による超軽量化**:
> 英語専用パッケージでは、日本語の形態素解析（MeCab）関連ファイル（`MeCab.DotNet.dll` および `dic/` 辞書ディレクトリ）を完全に排除したことにより、パッケージサイズとメモリ使用量を劇的にスリム化しました。

---

## 2. システムの特徴・独自改善

### ① 英語完全対応のUIローカライズ
英語版（`Release_EN` ビルド）では、アプリケーションのウィンドウタイトル、ラベル、ボタン、および詳細設定のテキストが完全に英語でローカライズ表示されます。

### ② 英文テキスト正規化 (English Normalizer) の大幅なデバッグと強化
英文の読み上げ時において、以下の表記が分断されず高品質に発音されるよう、テキスト正規化エンジンを大幅に強化しました。
*   **略語展開**: `Mr.` -> `mister`, `Dr.` -> `doctor`, `etc.` -> `et cetera` などの自動展開。
*   **カンマ区切り数値**: `12,345` などのカンマ区切り数値を分断せず、英語のスペルアウト表記（`twelve thousand three hundred and forty-five`）へ自動変換。
*   **小数表記**: `12.34` などを `twelve point three four` のように小数点以下を各桁読みで展開。
*   **通貨（ドル）表記**: `$12,345.67` や `$1.50` を、ドルの単位数とセント（小数点以下2桁）に分解し、単数形・複数形を意識したテキスト（`twelve thousand three hundred and forty-five dollars and sixty-seven cents`）へ展開。

---

## 3. 使用方法

### GUI (ウィンドウ起動)
`CBoxTTS.Native.EN.exe` をダブルクリックして起動します。
英語表記の WPF UI が表示され、テキストの入力、パラメータ（話速、抑揚、感情等）の調整、およびリアルタイムでの高品質な音声合成・ファイル出力・再生が簡単に行えます。

### CLI テストハーネス (コマンドライン起動)
コマンドプロンプトまたは PowerShell から以下の引数を指定して起動することで、GUI を表示させずに一気通貫の合成合成テストを実行し、動作確認用の WAV ファイルを出力できます。

```powershell
.\CBoxTTS.Native.EN.exe --test
```

*   **動作ログ**: `test_harness.log` に処理ステップ（トークナイズ -> 推論 -> 音声出力）の全記録が出力されます。
*   **出力ファイル**: 合成された英語音声が `test_harness_english_exclusive_out.wav` などとして保存されます。
