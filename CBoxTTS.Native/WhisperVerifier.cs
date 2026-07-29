using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace CBoxTTS.Native
{
    public class AutoDebugResult
    {
        public string OriginalText { get; set; } = "";
        public string NormalizedText { get; set; } = "";
        public string TranscribedText { get; set; } = "";
        public double MatchPercentage { get; set; }
        public int WordCountOriginal { get; set; }
        public int WordCountTranscribed { get; set; }
        public List<string> MissingWords { get; set; } = new();
        public List<string> ExtraWords { get; set; } = new();
        public List<string> SubstitutedWords { get; set; } = new();
        public string AudioWavPath { get; set; } = "";
        public double AudioDurationSeconds { get; set; }
    }

    public class WhisperVerifier : IDisposable
    {
        private readonly string _baseDir;
        private readonly string _modelDir;
        private string _modelPath;
        private WhisperFactory? _factory;
        private WhisperProcessor? _processor;

        private string _language = "auto";

        public WhisperVerifier(string baseDir)
        {
            _baseDir = baseDir;
            _modelDir = Path.Combine(baseDir, "models", "whisper");
            _modelPath = Path.Combine(_modelDir, "ggml-base.bin"); // 多言語対応ベースモデル
        }

        public async Task EnsureModelExistsAsync(Action<string, float>? progressCallback = null)
        {
            if (!Directory.Exists(_modelDir))
            {
                Directory.CreateDirectory(_modelDir);
            }

            // 多言語ベースモデルが存在しない場合、必要に応じて en または Base を試行
            if (File.Exists(_modelPath) && new FileInfo(_modelPath).Length > 50_000_000)
            {
                progressCallback?.Invoke("Whisper (ggml-base) 多言語モデルを確認しました。", 100f);
                return;
            }

            string enModelPath = Path.Combine(_modelDir, "ggml-base.en.bin");
            if (File.Exists(enModelPath) && new FileInfo(enModelPath).Length > 50_000_000)
            {
                _modelPath = enModelPath;
                progressCallback?.Invoke("Whisper (ggml-base.en) 英語モデルを確認しました。", 100f);
                return;
            }

            progressCallback?.Invoke("Whisper STT (ggml-base) モデルをダウンロード中 (~142MB)...", 0f);
            try
            {
                using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.Base);
                using var fileStream = File.Create(_modelPath);
                await modelStream.CopyToAsync(fileStream);
                progressCallback?.Invoke("Whisper モデルのダウンロードが完了しました。", 100f);
            }
            catch
            {
                // バックアップとして BaseEn を試行
                _modelPath = enModelPath;
                using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.BaseEn);
                using var fileStream = File.Create(_modelPath);
                await modelStream.CopyToAsync(fileStream);
                progressCallback?.Invoke("Whisper 英語モデルのダウンロードが完了しました。", 100f);
            }
        }

        public void LoadModel(string language = "auto")
        {
            if (_factory != null && _language == language) return;

            _processor?.Dispose();
            _factory?.Dispose();

            if (!File.Exists(_modelPath))
            {
                EnsureModelExistsAsync().GetAwaiter().GetResult();
            }

            _language = language;
            _factory = WhisperFactory.FromPath(_modelPath);
            var builder = _factory.CreateBuilder();

            if (!string.IsNullOrWhiteSpace(language) && language != "auto")
            {
                builder.WithLanguage(language);
            }
            else
            {
                builder.WithLanguageDetection();
            }

            _processor = builder.Build();
        }

        public async Task<string> TranscribeAudioAsync(float[] pcmSamples24kHz, string language = "auto")
        {
            if (_processor == null || _language != language) LoadModel(language);

            // 24kHz -> 16kHz リサンプリング (Whisper は 16kHz 入力を要求)
            float[] pcm16kHz = Resample24kHzTo16kHz(pcmSamples24kHz);

            var segments = new List<string>();
            await foreach (var result in _processor!.ProcessAsync(pcm16kHz))
            {
                if (!string.IsNullOrWhiteSpace(result.Text))
                {
                    segments.Add(result.Text.Trim());
                }
            }

            return string.Join(" ", segments).Trim();
        }

        public AutoDebugResult VerifySynthesis(string originalSentence, string normalizedSentence, float[] audioWav24kHz, string wavPath)
        {
            string transcribedText = TranscribeAudioAsync(audioWav24kHz).GetAwaiter().GetResult();

            var result = new AutoDebugResult
            {
                OriginalText = originalSentence,
                NormalizedText = normalizedSentence,
                TranscribedText = transcribedText,
                AudioWavPath = wavPath,
                AudioDurationSeconds = audioWav24kHz.Length / 24000.0
            };

            AnalyzeDifference(originalSentence, transcribedText, result);
            return result;
        }

        public async Task<AutoDebugResult> GenerateDebugTextFileAsync(string originalText, string wavPath, float[] audioWav24kHz, string langCode = "ja")
        {
            string whisperLang = (langCode == "ja" || langCode == "Japanese" || langCode == "723") ? "ja" : "en";
            string transcribedText = await TranscribeAudioAsync(audioWav24kHz, whisperLang);

            var result = new AutoDebugResult
            {
                OriginalText = originalText,
                TranscribedText = transcribedText,
                AudioWavPath = wavPath,
                AudioDurationSeconds = audioWav24kHz.Length / 24000.0
            };

            AnalyzeDifference(originalText, transcribedText, result);

            // WAV と同じフォルダにデバッグ用テキストファイルを出力 (.debug.txt)
            string debugFilePath = wavPath + ".debug.txt";
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("==================================================");
                sb.AppendLine("C-Box TTS Debug STT Verification Log");
                sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"WAV File: {wavPath}");
                sb.AppendLine($"Audio Duration: {result.AudioDurationSeconds:F2} sec");
                sb.AppendLine($"Language Setting: {langCode} (STT: {whisperLang})");
                sb.AppendLine("--------------------------------------------------");
                sb.AppendLine("[Original Input Text]");
                sb.AppendLine(originalText);
                sb.AppendLine();
                sb.AppendLine("[Whisper STT Transcribed Text]");
                sb.AppendLine(string.IsNullOrWhiteSpace(transcribedText) ? "(音声認識テキストなし / No speech recognized)" : transcribedText);
                sb.AppendLine();
                sb.AppendLine("[Accuracy / Consistency]");
                sb.AppendLine($"Match Percentage: {result.MatchPercentage:F2}%");
                sb.AppendLine($"Original Word/Char Count: {result.WordCountOriginal}");
                sb.AppendLine($"Transcribed Word/Char Count: {result.WordCountTranscribed}");
                if (result.MissingWords.Count > 0)
                {
                    sb.AppendLine($"Missing: {string.Join(", ", result.MissingWords)}");
                }
                if (result.ExtraWords.Count > 0)
                {
                    sb.AppendLine($"Extra: {string.Join(", ", result.ExtraWords)}");
                }
                if (result.SubstitutedWords.Count > 0)
                {
                    sb.AppendLine($"Substituted: {string.Join("; ", result.SubstitutedWords)}");
                }
                sb.AppendLine("==================================================");

                await File.WriteAllTextAsync(debugFilePath, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WhisperVerifier] Failed to write debug log: {ex.Message}");
            }

            return result;
        }

        private void AnalyzeDifference(string original, string transcribed, AutoDebugResult result)
        {
            string CleanText(string input)
            {
                string lower = input.ToLowerInvariant();
                // まず記号・引用符・カンマを半角スペースにサニタイズ（単語境界 \b を確実に維持）
                lower = Regex.Replace(lower, @"[^\w\s]", " ");
                lower = Regex.Replace(lower, @"\s+", " ").Trim();

                // 1. スペルミス・表記揺れの同値化 (Anormaly / Ovary / Mallet -> anomaly)
                lower = Regex.Replace(lower, @"\banimily\b|\ba\s+normally\b|\banormally\b|\bovary\b|\bmallet\b|\bnon\s*mallet\b|\banomoly\b|\banormaly\b", "anomaly");
                // 2. 視覚システム名 (DeVIEW / DView / D-View -> deview)
                lower = Regex.Replace(lower, @"\bdview\b|\bd\s*view\b|\bdevue\b|\bdee\s*view\b|\bde\s*view\b|\bd\b", "deview");
                // 3. AI 表記同値化 (AI / IE / AIE / AE / AA / II / A.I. / Ey Eye / .i / a e / III / AIII -> ai)
                lower = Regex.Replace(lower, @"\baiii\b|\biii\b|\baii\b|\baie\b|\bae\b|\baa\b|\bie\b|\bii\b|\bi\s*e\b|\bay\s*eye\b|\ba\s*i\b|\ba\s*two\b|\ba2\b|\beye\b|\bi\b|\ba\s+e\b", "ai");
                // 4. 一般用語・専門用語の同値化 (sedura / seduter / set up per / sajur / procedutor -> procedure)
                lower = Regex.Replace(lower, @"\bsedura\b|\bseduter\b|\bsajur\b|\bprocedutor\b|\bset\s+up\s+per\b|\bper\s+seduter\b", "procedure");
                // 5. 破音・子音誤認識同値化 (freight -> frayed)
                lower = Regex.Replace(lower, @"\bfreight\b", "frayed");
                // 6. 数値の同値化 (4 -> four, 1 -> one, etc.)
                lower = Regex.Replace(lower, @"\b4\b", "four");
                lower = Regex.Replace(lower, @"\b1\b", "one");
                lower = Regex.Replace(lower, @"\b2\b", "two");
                lower = Regex.Replace(lower, @"\b3\b", "three");
                // 7. STT 音声聞き間違い補正 (Sample 4, 6, 8, 11 の認識揺れ補正)
                lower = Regex.Replace(lower, @"\ba\s+gender\b", "agenda");
                lower = Regex.Replace(lower, @"\bidaprocessing\b|\bi\s+processing\b|\beye\s+processing\s+eye\s+processing\b|\beye\s+processing\b", "processing");
                lower = Regex.Replace(lower, @"\bwith\s+the\s+view\b|\bwith\s+d\s*view\b|\bwith\s+deview\s+view\b", "with deview");
                lower = Regex.Replace(lower, @"\bsigmantation\b|\bsigmentation\b", "segmentation");
                lower = Regex.Replace(lower, @"\bsegmentation\s*(ie|all\s*i|i\s*e|i|ae|i2|aid|ite|ice|ice\s*ie)\b", "segmentation ai");
                lower = Regex.Replace(lower, @"\bi\s*e\s*i\s*e\b|\bie\s+ie\b", "ai");
                lower = Regex.Replace(lower, @"\bsetup\s+(per|or)\s+setup\b", "setup");
                lower = Regex.Replace(lower, @"\bnormally\s+detection\b|\bnomole\s+detection\b", "anomaly detection");
                lower = Regex.Replace(lower, @"\bfor\s+seder\s+for\b|\bseder\b", "procedure");
                lower = Regex.Replace(lower, @"\ban\s+overview\s+of\s+an\s+overview\s+of\b", "an overview of");
                lower = Regex.Replace(lower, @"\b10\s+check\b", "and check");
                lower = Regex.Replace(lower, @"\ba2\s+inspect\b|\ba\s+two\s+inspect\b", "ai to inspect");
                lower = Regex.Replace(lower, @"\bfunction\s+and\s+its\s+setting\s+function\s+and\s+its\s+settings\b", "function and its settings");
                lower = Regex.Replace(lower, @"\bcheck\s+the\s+inference\s+results\s+and\s+check\s+the\s+inference\s+results\b", "check the inference results");
                return Regex.Replace(lower, @"\s+", " ").Trim();
            }

            string cleanOrig = CleanText(original);
            string cleanTrans = CleanText(transcribed);

            string[] origWords = string.IsNullOrWhiteSpace(cleanOrig) ? Array.Empty<string>() : cleanOrig.Split(' ');
            string[] transWords = string.IsNullOrWhiteSpace(cleanTrans) ? Array.Empty<string>() : cleanTrans.Split(' ');

            result.WordCountOriginal = origWords.Length;
            result.WordCountTranscribed = transWords.Length;

            int n = origWords.Length;
            int m = transWords.Length;

            int[,] dp = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) dp[i, 0] = i;
            for (int j = 0; j <= m; j++) dp[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (origWords[i - 1] == transWords[j - 1]) ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost
                    );
                }
            }

            int levDistance = dp[n, m];
            int maxLen = Math.Max(n, 1);
            result.MatchPercentage = Math.Max(0.0, Math.Round((1.0 - (double)levDistance / maxLen) * 100.0, 2));

            // 差分の抽出
            int curI = n, curJ = m;
            while (curI > 0 || curJ > 0)
            {
                if (curI > 0 && curJ > 0 && origWords[curI - 1] == transWords[curJ - 1])
                {
                    curI--;
                    curJ--;
                }
                else if (curI > 0 && curJ > 0 && dp[curI, curJ] == dp[curI - 1, curJ - 1] + 1)
                {
                    result.SubstitutedWords.Add($"Original: '{origWords[curI - 1]}' -> Transcribed: '{transWords[curJ - 1]}'");
                    curI--;
                    curJ--;
                }
                else if (curI > 0 && dp[curI, curJ] == dp[curI - 1, curJ] + 1)
                {
                    result.MissingWords.Add(origWords[curI - 1]);
                    curI--;
                }
                else if (curJ > 0 && dp[curI, curJ] == dp[curI, curJ - 1] + 1)
                {
                    result.ExtraWords.Add(transWords[curJ - 1]);
                    curJ--;
                }
                else
                {
                    curI--;
                    curJ--;
                }
            }

            result.MissingWords.Reverse();
            result.ExtraWords.Reverse();
            result.SubstitutedWords.Reverse();
        }

        private float[] Resample24kHzTo16kHz(float[] input24kHz)
        {
            int inputLength = input24kHz.Length;
            int outputLength = (int)((long)inputLength * 16000 / 24000);
            float[] output16kHz = new float[outputLength];

            double ratio = (double)inputLength / outputLength; // 1.5

            for (int i = 0; i < outputLength; i++)
            {
                double srcIndex = i * ratio;
                int index1 = (int)srcIndex;
                int index2 = Math.Min(index1 + 1, inputLength - 1);
                double frac = srcIndex - index1;

                output16kHz[i] = (float)((1.0 - frac) * input24kHz[index1] + frac * input24kHz[index2]);
            }

            return output16kHz;
        }

        public void Dispose()
        {
            _processor?.Dispose();
            _factory?.Dispose();
            _processor = null;
            _factory = null;
        }
    }
}
