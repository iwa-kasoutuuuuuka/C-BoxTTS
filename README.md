# C-Box TTS Japanese Edition

完全ローカル・ポータブル動作の高品質日本語音声合成（TTS）アプリケーションです。
Resemble AIのChatterboxエンジンをベースに、日本語環境への完全最適化と高度なスタイル制御を実装しています。

## 特徴

- **完全ローカル動作**: 初回セットアップ後はオフラインで動作し、外部サーバーへデータを送信しません。
- **高度なスタイル制御**:
  - **話速調整**: 10% 〜 200% の範囲で、声の質を変えずにスピードを調整可能。
  - **詳細設定**: 感情の強調度 (Exaggeration)、抑揚の制御 (CFG Weight)、表現の多様性 (Temperature) をスライダーで直感的に操作。
  - **感情タグ対応**: テキストに `[laugh]`, `[chuckle]`, `[cough]` などを入れることで非言語表現が可能。
- **多言語・多モデル対応**: 
  - Turbo (高速), Standard (標準), Multilingual (23言語・クローニング対応) の3モデルを切り替え可能。
  - C# (Native) 版では、英語読み上げ時の数値・略語展開 (`EnglishNormalizer`) や小文字化、記号正規化を独自実装し、英語の品質を大幅に高めています。
- **ポータブル・自動構築**: 
  - 配布サイズを最小化した「ランチャー方式」。起動時に最適なAI環境を自動構築します。

## 操作方法

1. **モデルの選択**: サイドバーから使用したいモデルを選びます（日本語はMultilingual推奨）。
2. **テキスト入力**: 読み上げたい文章を入力します。長文は自動的に適切な句読点で分割処理されます。
3. **スタイル調整**: サイドバーのスピードスライダーや「詳細設定」から声を調整します。
4. **生成・再生**: 「音声を生成」ボタンを押し、完了後に「再生」で確認、「保存」でWAV出力します。
5. **一括処理**: フォルダ内の複数テキストファイルを一度に音声化できます。

## セットアップ（ポータブル版）

1. `C-BoxTTS.exe` を起動します。
2. 「セットアップを開始」をクリックすると、必要なAIエンジンが自動ダウンロードされます。

### ネットワークが不安定な場合（手動ダウンロード）
以下のファイルを事前にダウンロードして `C-BoxTTS.exe` と同じ場所に配置することで、構築を高速化・オフライン化できます。

- **AIエンジンパッケージ (約2GB)**:
  - `packages` というフォルダを新規作成し、その中に以下の3つを入れてください。
  - [PyTorch (2.0GB)](https://download.pytorch.org/whl/cu124/torch-2.6.0%2Bcu124-cp310-cp310-win_amd64.whl)
  - [Torchaudio (4MB)](https://download.pytorch.org/whl/cu124/torchaudio-2.6.0%2Bcu124-cp310-cp310-win_amd64.whl)
  - [Torchvision (7MB)](https://download.pytorch.org/whl/cu124/torchvision-0.21.0%2Bcu124-cp310-cp310-win_amd64.whl)
- **pip管理ツール**:
  - [get-pip.py](https://bootstrap.pypa.io/get-pip.py) (EXEと同じ場所に配置)

## ライセンス
[MIT License](LICENSE)

## クレジット
- Engine: [Chatterbox (Resemble AI)](https://github.com/resemble-ai/chatterbox)
- UI: [CustomTkinter](https://github.com/TomSchimansky/CustomTkinter)
