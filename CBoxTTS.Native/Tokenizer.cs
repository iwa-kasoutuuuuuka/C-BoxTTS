using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CBoxTTS.Native
{
    public class Tokenizer
    {
        private readonly Dictionary<string, long> _vocab = new();
        private readonly List<(string First, string Second)> _merges = new();
        private const long StartToken = 255;
        private const long JaToken = 723;
        private const long StopToken = 0;
        
        // embed_tokens.onnx の入力対応インデックス境界は [0, 2351] のため、これを上限とする
        private const long MaxValidTokenId = 2351; 

        public Tokenizer(string jsonPath)
        {
            LoadVocab(jsonPath);
        }

        private void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "debug.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] [Tokenizer] {message}{Environment.NewLine}");
            }
            catch { }
        }

        private void LoadVocab(string jsonPath)
        {
            Log($"語彙ファイルのロードを開始します: {jsonPath}");
            try
            {
                var json = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(json);
                var vocabElement = doc.RootElement.GetProperty("model").GetProperty("vocab");
                
                _vocab.Clear();
                foreach (var property in vocabElement.EnumerateObject())
                {
                    _vocab[property.Name] = property.Value.GetInt64();
                }
                Log($"語彙のロードに成功しました。総語彙数: {_vocab.Count}");

                _merges.Clear();
                if (doc.RootElement.GetProperty("model").TryGetProperty("merges", out var mergesElement))
                {
                    foreach (var item in mergesElement.EnumerateArray())
                    {
                        var rule = item.GetString();
                        if (rule != null)
                        {
                            var parts = rule.Split(' ');
                            if (parts.Length == 2)
                            {
                                _merges.Add((parts[0], parts[1]));
                            }
                        }
                    }
                    Log($"マージルールのロードに成功しました。総ルール数: {_merges.Count}");
                }
            }
            catch (Exception ex)
            {
                Log($"語彙ロード中に致命的エラーが発生しました: {ex}");
                throw;
            }
        }

        public long[] Encode(string text, long languageToken)
        {
            Log($"=== Tokenizer.Encode 開始 ===");
            Log($"入力テキスト: \"{text}\", 言語トークンID: {languageToken}");

            // NFD正規化 (日本語の濁音分解に必要)
            string normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            Log($"NFD正規化後のテキスト: \"{normalized}\"");

            var ids = new List<long> { StartToken, languageToken };
            Log($"初期化トークン追加: StartToken({StartToken}), LanguageToken({languageToken})");

            // 空白で事前分割
            var words = normalized.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.None);

            for (int wIdx = 0; wIdx < words.Length; wIdx++)
            {
                string word = words[wIdx];
                
                // 空白文字だった場合は、対応するスペーストークンを追加
                if (string.IsNullOrEmpty(word))
                {
                    if (wIdx < words.Length - 1)
                    {
                        if (_vocab.TryGetValue("[SPACE]", out long spaceId))
                        {
                            ids.Add(spaceId);
                        }
                        else if (_vocab.TryGetValue(" ", out long spaceId2))
                        {
                            ids.Add(spaceId2);
                        }
                    }
                    continue;
                }

                // 単語を文字のリストに分解
                var symbols = new List<string>();
                for (int i = 0; i < word.Length; i++)
                {
                    if (char.IsHighSurrogate(word[i]) && i + 1 < word.Length)
                    {
                        symbols.Add(word.Substring(i, 2));
                        i++;
                    }
                    else
                    {
                        symbols.Add(word[i].ToString());
                    }
                }

                // BPE マージルールの適用
                foreach (var pair in _merges)
                {
                    int i = 0;
                    while (i < symbols.Count - 1)
                    {
                        if (symbols[i] == pair.First && symbols[i + 1] == pair.Second)
                        {
                            symbols[i] = symbols[i] + symbols[i + 1];
                            symbols.RemoveAt(i + 1);
                        }
                        else
                        {
                            i++;
                        }
                    }
                }

                // 語彙 ID に変換
                foreach (var sym in symbols)
                {
                    if (_vocab.TryGetValue(sym, out long id))
                    {
                        if (id > MaxValidTokenId)
                        {
                            Log($"[警告/安全装置作動] トークン '{sym}' の辞書ID {id} がモデル上限 {MaxValidTokenId} を超過しています！ID を 1 (UNK) に安全マッピングします。");
                            id = 1;
                        }
                        else
                        {
                            Log($"トークン: '{sym}' -> ID: {id}");
                        }
                        ids.Add(id);
                    }
                    else
                    {
                        Log($"トークン: '{sym}' -> [UNK] 未知語のため ID 1 を割り当て");
                        ids.Add(1);
                    }
                }

                // 単語間のスペースを追加（最後の単語以外）
                if (wIdx < words.Length - 1)
                {
                    if (_vocab.TryGetValue("[SPACE]", out long spaceId))
                    {
                        ids.Add(spaceId);
                    }
                    else if (_vocab.TryGetValue(" ", out long spaceId2))
                    {
                        ids.Add(spaceId2);
                    }
                }
            }

            ids.Add(StopToken);
            Log($"終了トークン追加: StopToken({StopToken})");

            var result = ids.ToArray();
            Log($"エンコード完了。最終トークンID配列: [{string.Join(", ", result)}]");
            Log($"=== Tokenizer.Encode 終了 ===");

            return result;
        }
    }
}
