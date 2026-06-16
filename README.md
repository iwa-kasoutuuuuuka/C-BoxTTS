# C-Box TTS Native (Japanese & English Edition)

<p align="center">
  <img src="CBoxTTS.Native/assets/icon.png" alt="C-Box TTS Logo" width="128" height="128" />
</p>

完全ローカルかつ超軽量・高速に動作する .NET/C# ネイティブ（WPF）音声合成（TTS）アプリケーションです。  
Python 環境や PyTorch などの巨大な外部依存を一切必要とせず、Windows 上でスタンドアロンかつ即座に動作します。

---

## 🌟 主な特徴

- **完全ローカル・オフライン動作**: 初回起動時のモデル取得後は一切通信を行わず、プライバシーを完全に保護。
- **DirectML ネイティブサポート**: GPU（NVIDIA, AMD, Intel 等）によるハードウェアアクセラレーションを標準サポートし、高速な推論が可能。
- **Python不要・即時起動**: 重い Python 実行環境や、数GBに及ぶ PyTorch/CUDA の環境構築は一切不要。
- **言語別ポータブル構成（完全分離）**:
  - **日本語版 (JA)**: MeCab による形態素解析と言語辞書フォルダを同梱し、自然な日本語読み上げが可能な「Turbo（日本語専用・高速）」および「Multilingual」モデルをサポート。
  - **英語版 (EN)**: 日本語解析エンジン（MeCab）や関連辞書ファイルを完全に排除し、パッケージサイズとメモリフットプリントを極限まで軽量化した英語専用の高速・高品質パッケージ。
- **テキスト正規化 (English Normalizer)**:
  - 英語読み上げ時において、略語展開 (`Mr.`, `Dr.` 等)、カンマ区切り数値 (`12,345` -> `twelve thousand...`)、小数表記 (`12.34` -> `twelve point three four`)、通貨・セント表記 (`$1.50` -> `one dollar and fifty cents`) を自動的かつ高品質にテキスト展開して自然な発音を実現。
- **日本語処理のセーフガードとデバッグ**:
  - Unicode NFC (FormC) 正規化の導入により、濁音・半濁音の文字分離バグを根本解決。
  - 未知文字・全角記号が検出された場合に自動的にマッピングをクリップし、モデル境界エラーによるクラッシュを防ぐ安全装置（境界インデックスセーフガード）を搭載。
- **音声の頭切れ・ぶつ切り対策 (無音パディング)**:
  - 再生環境での音声開始遅延を吸収しポップノイズを防ぐため、合成音声の冒頭（0.15秒）と末尾（0.10秒）に自動的に最適な無音パディングを追加。
- **音声の安定性 (Stability / Temperature) 調整**:
  - 自己回帰推論時の安定性パラメータ（Temperature）の制御スライダーを追加。モデル別（英語専用: `0.5`, Turbo: `0.6`, 多言語: `0.7`）にデフォルト値を最適化し、吃りや機械音ノイズを排除したクリアな滑舌を実現。

---

## 📊 日本語版 (JA) と英語版 (EN) の違い

本アプリケーションは、言語ごとに最適化されたポータブル構成を採用しています。それぞれの主な違いは以下の通りです。

| 比較項目 | 🇯🇵 日本語版 (JA) | 🇺🇸 英語版 (EN) |
| :--- | :--- | :--- |
| **メイン実行ファイル** | `CBoxTTS.Native.JA.exe` | `CBoxTTS.Native.EN.exe` |
| **形態素解析エンジン** | **同梱 (MeCab)**<br>C#版 MeCab (`MeCab.DotNet.dll`) と形態素解析用 IPADIC 辞書 (`dic/`) をすべて内蔵。 | **非搭載 (完全排除)**<br>日本語の解析処理を行わないため、MeCab や辞書ファイルを完全に排除し軽量化。 |
| **サポートする音声モデル** | ・**`Turbo`** (日本語専用・超高速・低遅延)<br>・**`Multilingual`** (多言語・高品質・クローニング対応) | ・**`English`** (英語専用・超高速・高品質)<br>・**`Multilingual`** (多言語・高品質・クローニング対応) |
| **テキスト処理・正規化** | **日本語専用セーフガード**<br>・Unicode NFC 正規化による濁音・半濁音分離バグ修正<br>・インデックス超過 (2453超) 未知文字の自動クリップ回路 | **English Normalizer**<br>・略語展開 (`Mr.`, `Dr.` 等)<br>・数値・小数表記の英語展開<br>・通貨・セント表記 (`$1.50` -> `one dollar and...`) |
| **パッケージサイズ** | MeCab 辞書およびモデルを含むため、英語版よりやや大きい。 | 辞書ファイル一式が不要なため、ポータブル構成として極めて軽量。 |

---

## 🛠️ パッケージの構成と場所

ポータブルパッケージのビルドスクリプトを実行すると、以下のディレクトリに完全なポータブル版が構築されます。

### 1. 日本語ポータブル版 (Release_Portable_JA)
*   **パス**: `CBoxTTS.Native/Release_Portable_JA`
*   **メイン実行ファイル**: `CBoxTTS.Native.JA.exe`
*   **特徴**: MeCab辞書および日本語対応モデル（Turbo / Multilingual）を同梱。

### 2. 英語ポータブル版 (Release_Portable_EN)
*   **パス**: `CBoxTTS.Native/Release_Portable_EN`
*   **メイン実行ファイル**: `CBoxTTS.Native.EN.exe`
*   **特徴**: 日本語辞書やMeCabを徹底的に排除した超軽量仕様。英語専用モデル / Multilingualをサポート。

各パッケージの詳細については、同梱およびルートに配置されている仕様書をご覧ください。
- [ポータブル版仕様書_Native_JA.md](ポータブル版仕様書_Native_JA.md)
- [ポータブル版仕様書_Native_EN.md](ポータブル版仕様書_Native_EN.md)

---

## 🚀 使い方

### GUI（ウィンドウ起動）
パッケージ内の `CBoxTTS.Native.JA.exe` または `CBoxTTS.Native.EN.exe` をダブルクリックして起動します。
美麗なダークモード対応のモダンUIで、テキスト入力、パラメータ（話速、感情誇張度等）の調整、リアルタイム再生およびWAVファイルへの書き出し・一括保存を行えます。

### CLI（コマンドライン起動・テストハーネス）
コマンドプロンプトまたは PowerShell から `--test` 引数付きで実行することで、GUI を起動せずに音声合成の動作確認が可能です。

```powershell
# 日本語版のテスト実行
.\CBoxTTS.Native.JA.exe --test

# 英語版のテスト実行
.\CBoxTTS.Native.EN.exe --test
```
実行すると、カレントディレクトリにテスト結果の音声ファイル（`test_harness_japanese_out.wav` や `test_harness_english_exclusive_out.wav`）が出力されます。

---

## 📦 ビルド方法（ポータブルパッケージの作成）

開発環境（.NET 10 SDK 導入済み）でポータブルパッケージを自らビルド・パブリッシュするには、`CBoxTTS.Native` フォルダ内の以下のスクリプトを PowerShell から実行します。

*   **日本語版のビルド**: `.\build_portable_ja.ps1`
*   **英語版のビルド**: `.\build_portable_en.ps1`

実行すると、自動的に ReadyToRun の最適化ビルドが行われ、必要な依存ライブラリ、モデルファイル、仕様書がそれぞれのポータブルパッケージフォルダへ自動配置されます。

---

## ⚖️ ライセンス
[MIT License](LICENSE)

## 👏 クレジット
- 推論コアエンジン: [Chatterbox (Resemble AI)](https://github.com/resemble-ai/chatterbox)
- 音響モデル提供およびコミュニティ: [onnx-community](https://huggingface.co/onnx-community)
