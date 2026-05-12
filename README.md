# C-Box TTS Japanese Edition

完全ローカル・ポータブル動作の日本語読み上げ（TTS）アプリケーションです。
Resemble AIのChatterboxエンジンを使用し、高品質な音声合成をオフラインで提供します。

## 特徴

- **完全ローカル動作**: 初回起動時にモデルをダウンロードした後は、インターネット接続なしで動作します。
- **3つのモデルを搭載**: 
  - **Turbo**: 高速な英語モデル（350M）
  - **Standard**: 標準的な英語モデル
  - **Multilingual**: 日本語を含む23言語対応モデル（音声クローニング対応）
- **多機能GUI**: 長文の自動分割、一括処理機能、音声再生・保存機能を搭載。
- **ポータブル版**: インストール不要で実行可能なポータブル形式での配布が可能です。

## インストール（開発者用）

Python 3.10環境を推奨します。

```bash
pip install -r requirements.txt
```

## 実行方法

```bash
python -m src.main
```

## ビルド方法（ポータブル版作成）

Windows環境で `build.bat` を実行してください。`dist/C-BoxTTS` フォルダに成果物が出力されます。

## ライセンス

[MIT License](LICENSE)

## クレジット

- Engine: [Chatterbox (Resemble AI)](https://github.com/resemble-ai/chatterbox)
- UI: [CustomTkinter](https://github.com/TomSchimansky/CustomTkinter)
