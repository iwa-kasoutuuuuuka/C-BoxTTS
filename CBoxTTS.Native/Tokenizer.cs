using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        
        // tokenizer.json 内の語彙ID最大値（LoadVocab 時に動的設定）
        private long _maxVocabTokenId = 2453;
        
        // embed_tokens.onnx の実際のEmbedding行列行数（モデルロード後に SetEmbeddingVocabSize で設定）。
        // この値が設定されている場合、_maxVocabTokenId より優先的にクランプ上限として使用される。
        private long _embeddingVocabSize = -1;
        private bool _isByteLevel = false;

        private static readonly Dictionary<byte, char> BytesToUnicode = GetBytesToUnicode();

        private static Dictionary<byte, char> GetBytesToUnicode()
        {
            var d = new Dictionary<byte, char>();
            var bs = new List<int>();
            for (int i = 33; i <= 126; i++) bs.Add(i);
            for (int i = 161; i <= 172; i++) bs.Add(i);
            for (int i = 174; i <= 255; i++) bs.Add(i);

            var cs = new List<int>(bs);
            int n = 0;
            for (int b = 0; b < 256; b++)
            {
                if (!bs.Contains(b))
                {
                    bs.Add(b);
                    cs.Add(256 + n);
                    n++;
                }
            }

            for (int i = 0; i < bs.Count; i++)
            {
                d[(byte)bs[i]] = (char)cs[i];
            }
            return d;
        }

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
                    try
                    {
                        _vocab[property.Name] = property.Value.GetInt64();
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"vocabのキー '{property.Name}' (ValueKind: {property.Value.ValueKind}) のパースに失敗しました。", ex);
                    }
                }
                if (_vocab.Count > 0)
                {
                    _maxVocabTokenId = _vocab.Values.Max();
                }
                _isByteLevel = _vocab.Count > 5000;
                Log($"語彙のロードに成功しました。総語彙数: {_vocab.Count}, 語彙最大ID: {_maxVocabTokenId}");

                _merges.Clear();
                if (doc.RootElement.GetProperty("model").TryGetProperty("merges", out var mergesElement))
                {
                    if (mergesElement.ValueKind == JsonValueKind.Array)
                    {
                        int idx = 0;
                        foreach (var item in mergesElement.EnumerateArray())
                        {
                            try
                            {
                                if (item.ValueKind == JsonValueKind.String)
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
                                else if (item.ValueKind == JsonValueKind.Array)
                                {
                                    var arr = item.EnumerateArray().ToArray();
                                    if (arr.Length == 2 && arr[0].ValueKind == JsonValueKind.String && arr[1].ValueKind == JsonValueKind.String)
                                    {
                                        _merges.Add((arr[0].GetString() ?? "", arr[1].GetString() ?? ""));
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException($"mergesのインデックス {idx} (ValueKind: {item.ValueKind}) のパースに失敗しました。", ex);
                            }
                            idx++;
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

        /// <summary>
        /// embed_tokens.onnx の実際のEmbedding行列サイズを設定する。
        /// モデルロード時に TTSEngine から呼び出され、トークンIDの安全なクランプ上限を決定する。
        /// </summary>
        public void SetEmbeddingVocabSize(long size)
        {
            _embeddingVocabSize = size;
            Log($"Embedding語彙サイズを設定しました: {size} (クランプ上限ID: {size - 1})");
        }

        /// <summary>
        /// トークンIDのクランプに使用する有効上限IDを返す。
        /// embed_tokens のEmbedding行列サイズが設定されている場合はそちらを優先する。
        /// </summary>
        private long GetMaxValidTokenId()
        {
            if (_embeddingVocabSize > 0)
            {
                return _embeddingVocabSize - 1; // 0-indexed なのでサイズ-1が最大有効ID
            }
            return _maxVocabTokenId;
        }

        public long[] Encode(string text, long languageToken)
        {
            Log($"=== Tokenizer.Encode 開始 ===");
            Log($"入力テキスト: \"{text}\", 言語トークンID: {languageToken}");

            bool isEnglish = (languageToken == 708 || languageToken == 1 || languageToken == 0);
            string processed = text;
            if (isEnglish)
            {
                // 記号正規化 (punc_norm)
                processed = PuncNorm(processed);
                // 小文字化 (Python版の preprocess_text と揃える)
                processed = processed.ToLowerInvariant();
            }

            // NFD正規化: 日本語の濁音分解（「が」→「か」+「゛」）に必要だが、
            // 英語では contractions（don't, it's）のアポストロフィが分解されて
            // トークン不一致・UNK落ちの原因になるため、英語では適用しない
            string normalized;
            if (isEnglish)
            {
                normalized = processed;
            }
            else
            {
                normalized = processed.Normalize(System.Text.NormalizationForm.FormD);
            }
            Log($"前処理・正規化後のテキスト: \"{normalized}\"");

            var ids = new List<long> { StartToken };
            if (!isEnglish && languageToken > 0)
            {
                ids.Add(languageToken);
                Log($"初期化トークン追加: StartToken({StartToken}), LanguageToken({languageToken})");
            }
            else
            {
                Log($"初期化トークン追加: StartToken({StartToken}) （英語モデルのため言語トークンは省略）");
            }

            if (_isByteLevel)
            {
                // GPT-2 BPE pre-tokenization using regex
                var matches = System.Text.RegularExpressions.Regex.Matches(normalized, @"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+");
                long maxValidId = GetMaxValidTokenId();

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    string matchVal = match.Value;
                    if (string.IsNullOrEmpty(matchVal)) continue;

                    // Convert to UTF-8 bytes
                    byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(matchVal);

                    // Map bytes to unicode characters
                    var symbols = new List<string>();
                    foreach (byte b in utf8Bytes)
                    {
                        if (BytesToUnicode.TryGetValue(b, out char c))
                        {
                            symbols.Add(c.ToString());
                        }
                    }

                    // Apply merges
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

                    // Map to vocab IDs
                    foreach (var sym in symbols)
                    {
                        if (_vocab.TryGetValue(sym, out long id))
                        {
                            if (id > maxValidId)
                            {
                                Log($"[警告/安全装置作動] トークン '{sym}' の辞書ID {id} がモデル上限 {maxValidId} を超過しています！ID を 1 (UNK) に安全マッピングします。");
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
                }
            }
            else
            {
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
                    long maxValidId = GetMaxValidTokenId();
                    foreach (var sym in symbols)
                    {
                        if (_vocab.TryGetValue(sym, out long id))
                        {
                            if (id > maxValidId)
                            {
                                Log($"[警告/安全装置作動] トークン '{sym}' の辞書ID {id} がモデル上限 {maxValidId} を超過しています！ID を 1 (UNK) に安全マッピングします。");
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
            }

            ids.Add(StopToken);
            Log($"終了トークン追加: StopToken({StopToken})");

            var result = ids.ToArray();
            Log($"エンコード完了。最終トークンID配列: [{string.Join(", ", result)}]");
            Log($"=== Tokenizer.Encode 終了 ===");

            return result;
        }

        private string PuncNorm(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            // 余分なスペースの統合
            var parts = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            string merged = string.Join(" ", parts);

            // 記号の置換 (Python版 punc_norm 準拠)
            // 時刻パターン（例: 3:00, 12:30）を保護してからコロン変換を行う
            // プレースホルダに置換 → コロン変換 → プレースホルダを復元
            merged = System.Text.RegularExpressions.Regex.Replace(
                merged, @"(\d{1,2}):(\d{2})", "$1\uE000COLON\uE000$2");

            var puncToReplace = new (string Old, string New)[]
            {
                ("...", ", "),
                ("…", ", "),
                (":", ","),
                (" - ", ", "),
                (";", ", "),
                ("—", "-"),
                ("–", "-"),
                (" ,", ","),
                ("“", "\""),
                ("”", "\""),
                ("‘", "'"),
                ("’", "'")
            };

            foreach (var item in puncToReplace)
            {
                merged = merged.Replace(item.Old, item.New);
            }

            // 時刻パターンのプレースホルダを復元
            merged = merged.Replace("\uE000COLON\uE000", ":");

            merged = merged.TrimEnd();

            // 文末の記号チェック (文末になければピリオドを追加)
            var sentenceEnders = new HashSet<char> { '.', '!', '?', '-', ',', '、', '，', '。', '？', '！' };
            if (merged.Length > 0 && !sentenceEnders.Contains(merged[merged.Length - 1]))
            {
                merged += ".";
            }

            return merged;
        }
    }
}
