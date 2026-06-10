using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;

namespace CBoxTTS.Native
{
    public enum ModelType
    {
        Multilingual,
        Turbo,
        English
    }

    public class TTSEngine : IDisposable
    {
        private InferenceSession? _speechEncoder;
        private InferenceSession? _languageModel;
        private InferenceSession? _condDecoder;
        private InferenceSession? _embedTokens;

        private readonly string _modelsDir;
        private ModelType _currentModelType = ModelType.Multilingual;

        // モデルごとのファイル定義
        private string GetLMFileName(ModelType type) => type switch
        {
            ModelType.Turbo => "language_model_turbo_q4.onnx",
            ModelType.English => "language_model_en_q4.onnx",
            _ => "language_model_q4.onnx"
        };

        private string GetLMDataFileName(ModelType type) => GetLMFileName(type) + "_data";

        private string GetModelFileName(string baseName, ModelType type)
        {
            string suffix = type switch
            {
                ModelType.Turbo => "_turbo",
                ModelType.English => "_en",
                _ => "_mtl"
            };
            
            if (baseName.EndsWith("_data"))
            {
                return baseName.Replace(".onnx_data", suffix + ".onnx_data");
            }
            if (baseName.EndsWith(".onnx"))
            {
                return baseName.Replace(".onnx", suffix + ".onnx");
            }
            if (baseName.EndsWith(".json"))
            {
                return baseName.Replace(".json", suffix + ".json");
            }
            return baseName;
        }

        private string GetBaseUrl(string fileName)
        {
            if (fileName.Contains("turbo")) return "https://huggingface.co/onnx-community/chatterbox-turbo-ONNX/resolve/main/onnx/";
            if (fileName.Contains("_en_")) return "https://huggingface.co/onnx-community/chatterbox-english-ONNX/resolve/main/onnx/";
            return "https://huggingface.co/onnx-community/chatterbox-multilingual-ONNX/resolve/main/onnx/";
        }


        public TTSEngine(string baseDir)
        {
            _modelsDir = Path.Combine(baseDir, "models");
        }

        public async Task EnsureModelExistsAsync(ModelType type, Action<string, double> progressCallback)
        {
            if (!Directory.Exists(_modelsDir)) Directory.CreateDirectory(_modelsDir);

            var filesToDownload = new List<(string RemoteName, string LocalName)>();
            
            // default_voice.wav は共通
            filesToDownload.Add(("default_voice.wav", "default_voice.wav"));

            // その他のファイルはモデル固有の名前で保存・ダウンロード
            string[] baseFiles = {
                "tokenizer.json",
                "speech_encoder.onnx", "speech_encoder.onnx_data",
                "embed_tokens.onnx", "embed_tokens.onnx_data",
                "conditional_decoder.onnx", "conditional_decoder.onnx_data"
            };

            foreach (var bf in baseFiles)
            {
                filesToDownload.Add((bf, GetModelFileName(bf, type)));
            }

            filesToDownload.Add((GetLMFileName(type), GetLMFileName(type)));
            filesToDownload.Add((GetLMDataFileName(type), GetLMDataFileName(type)));

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "CBoxTTS-Native-Downloader");

                foreach (var item in filesToDownload)
                {
                    string localPath = Path.Combine(_modelsDir, item.LocalName);
                    if (File.Exists(localPath)) continue;

                    string baseUrl = GetBaseUrl(item.RemoteName);
                    string url = (item.RemoteName.EndsWith(".json") || item.RemoteName == "default_voice.wav")
                        ? baseUrl.Replace("/onnx/", "/") + item.RemoteName
                        : baseUrl + item.RemoteName;

                    Log($"{item.LocalName} のダウンロードを開始します: {url}");
                    
                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!response.IsSuccessStatusCode) {
                            Log($"警告: {item.RemoteName} の取得に失敗しました。スキップします。");
                            continue;
                        }

                        var totalBytes = response.Content.Headers.ContentLength ?? -1;
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            var totalRead = 0L;
                            int read;

                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;
                                if (totalBytes > 0)
                                    progressCallback?.Invoke($"{item.LocalName} をダウンロード中... ({(double)totalRead / totalBytes * 100:F1}%)", (double)totalRead / totalBytes * 100);
                            }
                        }
                    }
                }
            }
        }

        public void LoadModel(ModelType type, Action<string, double> progressCallback)
        {
            Log($"=== LoadModel ({type}) 開始 ===");
            try
            {
                Dispose();

                string lmFile = GetLMFileName(type);
                string[] baseModelFiles = { "speech_encoder.onnx", "embed_tokens.onnx", "conditional_decoder.onnx", lmFile };
                
                for (int i = 0; i < baseModelFiles.Length; i++)
                {
                    string baseName = baseModelFiles[i];
                    string m = (baseName == lmFile) ? lmFile : GetModelFileName(baseName, type);
                    
                    double pct = (double)i / baseModelFiles.Length * 100;
                    progressCallback?.Invoke($"{baseName} をロード中... ({i+1}/{baseModelFiles.Length})", pct);

                    string path = Path.Combine(_modelsDir, m);
                    if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, m);
                    
                    Log($"[Session開始] {m}");
                    
                    InferenceSession? session = null;
                    try
                    {
                        // 動作の安定性を重視し、デフォルトでは CPU 推論を使用
                        // (DirectML GPU加速が必要な場合はコメントアウトを解除し VRAM プレッシャー等に配慮すること)
                        var cpuOptions = new SessionOptions();
                        session = new InferenceSession(path, cpuOptions);
                        Log($"[Session成功:CPU] {m}");
                    }
                    catch (Exception ex)
                    {
                        Log($"[ロード失敗] {m}: {ex.Message}");
                        throw;
                    }
                    
                    if (baseName.StartsWith("speech")) _speechEncoder = session;
                    else if (baseName.StartsWith("embed")) _embedTokens = session;
                    else if (baseName.StartsWith("conditional")) _condDecoder = session;
                    else _languageModel = session;

                    progressCallback?.Invoke($"{baseName} ロード完了", (double)(i+1) / baseModelFiles.Length * 100);
                }
                
                _currentModelType = type;
                Log("=== 全エンジンのロードに成功しました ===");
            }
            catch (Exception ex)
            {
                Log($"FATAL ERROR during LoadModel: {ex}");
                throw;
            }
        }


        private void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "debug.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                Console.WriteLine(message);
            }
            catch { }
        }

        private static DenseTensor<T> CloneTensor<T>(Tensor<T> src)
        {
            return new DenseTensor<T>(src.ToArray(), src.Dimensions);
        }

        public async Task<float[]> GenerateAsync(long[] inputIds, string voicePath, float exaggeration = 0.5f)
        {
            if (_speechEncoder == null || _languageModel == null || _condDecoder == null || _embedTokens == null)
                throw new InvalidOperationException("モデルがロードされていません。");

            return await Task.Run(() =>
            {
                Log("=== GenerateAsync 開始 ===");
                var random = new Random(42); // サンプリング用の乱数生成器
                
                // 1. 参照音声（ボイスプロンプト）のロード（24000Hz: リファレンス準拠 S3GEN_SR=24000）
                float[] refAudio;
                using (var reader = new AudioFileReader(voicePath))
                {
                    var outFormat = new WaveFormat(24000, 16, 1);
                    using (var resampler = new MediaFoundationResampler(reader, outFormat))
                    {
                        var sampleProvider = resampler.ToSampleProvider();
                        var buffer = new List<float>();
                        float[] samples = new float[24000];
                        int read;
                        while ((read = sampleProvider.Read(samples, 0, samples.Length)) > 0)
                        {
                            buffer.AddRange(samples.Take(read));
                        }
                        refAudio = buffer.ToArray();
                    }
                }
                Log($"参照音声ロード完了: {refAudio.Length} サンプル");

                // 2. 音声エンコーダーの実行
                var audioTensor = new DenseTensor<float>(refAudio, new[] { 1, refAudio.Length });
                var encoderInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("audio_values", audioTensor)
                };
                
                Log("音声エンコーダー実行中...");
                using var encoderResults = _speechEncoder.Run(encoderInputs);
                // リファレンス: cond_emb, prompt_token, ref_x_vector, prompt_feat
                var condEmb = CloneTensor(encoderResults.ElementAt(0).AsTensor<float>());
                var promptToken = CloneTensor(encoderResults.ElementAt(1).AsTensor<long>());
                var speakerEmbeddings = CloneTensor(encoderResults.ElementAt(2).AsTensor<float>());
                var speakerFeatures = CloneTensor(encoderResults.ElementAt(3).AsTensor<float>());
                Log($"音声エンコード成功。condEmb: [{string.Join(",", condEmb.Dimensions.ToArray())}], promptToken長さ: {promptToken.Length}");

                // 3. テキストトークンの埋め込み処理
                // リファレンス: position_ids = np.where(input_ids >= START_SPEECH_TOKEN, 0, np.arange(...) - 1)
                var inputIdsLong = inputIds.ToArray();
                var inputIdsTensor = new DenseTensor<long>(inputIdsLong, new[] { 1, inputIdsLong.Length });
                
                const long START_SPEECH_TOKEN = 6561;
                var positionIds = new long[inputIdsLong.Length];
                for (int i = 0; i < inputIdsLong.Length; i++)
                {
                    positionIds[i] = inputIdsLong[i] >= START_SPEECH_TOKEN ? 0 : (long)(i - 1);
                }
                var positionIdsTensor = new DenseTensor<long>(positionIds, new[] { 1, positionIds.Length });
                var exaggerationTensor = new DenseTensor<float>(new[] { exaggeration }, new[] { 1 });

                var embedInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                    NamedOnnxValue.CreateFromTensor("position_ids", positionIdsTensor),
                    NamedOnnxValue.CreateFromTensor("exaggeration", exaggerationTensor)
                };
                
                Log("テキスト埋め込み実行中...");
                using var embedResults = _embedTokens.Run(embedInputs);
                var inputsEmbeds = CloneTensor(embedResults.First(o => o.Name == "inputs_embeds").AsTensor<float>());

                // 4. 初回ステップ: cond_emb をテキスト埋め込みの前に結合（リファレンス準拠）
                // リファレンス: inputs_embeds = np.concatenate((cond_emb, inputs_embeds), axis=1)
                var condEmbData = condEmb.ToArray();
                var inputsEmbedsData = inputsEmbeds.ToArray();
                int condSeqLen = condEmb.Dimensions[1];
                int textSeqLen = inputsEmbeds.Dimensions[1];
                int embedDim = inputsEmbeds.Dimensions[2];
                int totalSeqLen = condSeqLen + textSeqLen;
                
                var combinedEmbeds = new float[totalSeqLen * embedDim];
                Array.Copy(condEmbData, 0, combinedEmbeds, 0, condSeqLen * embedDim);
                Array.Copy(inputsEmbedsData, 0, combinedEmbeds, condSeqLen * embedDim, textSeqLen * embedDim);
                var currentEmbeds = new DenseTensor<float>(combinedEmbeds, new[] { 1, totalSeqLen, embedDim });
                Log($"cond_emb結合完了: condSeqLen={condSeqLen}, textSeqLen={textSeqLen}, totalSeqLen={totalSeqLen}");

                // アテンションマスクの初期化（cond_emb + テキスト長分）
                var currentMaskValues = Enumerable.Repeat(1L, totalSeqLen).ToArray();
                var currentMask = new DenseTensor<long>(currentMaskValues, new[] { 1, currentMaskValues.Length });

                // 過去のKey/Valueキャッシュ（30レイヤー分）の初期化
                var pastKeyValues = new Dictionary<string, DenseTensor<float>>();
                for (int i = 0; i < 30; i++)
                {
                    pastKeyValues[$"past_key_values.{i}.key"] = new DenseTensor<float>(new float[0], new[] { 1, 16, 0, 64 });
                    pastKeyValues[$"past_key_values.{i}.value"] = new DenseTensor<float>(new float[0], new[] { 1, 16, 0, 64 });
                }

                // generate_tokens の初期化（リファレンス: generate_tokens = np.array([[START_SPEECH_TOKEN]])）
                var generateTokens = new List<long> { START_SPEECH_TOKEN };
                
                // 反復ペナルティ係数（リファレンス: repetition_penalty = 1.2）
                const float repetitionPenalty = 1.2f;

                int maxNewTokens = 400; // 安全のための最大トークン制限
                Log("自己回帰ループ開始...");
                
                for (int step = 0; step < maxNewTokens; step++)
                {
                    var lmInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("inputs_embeds", currentEmbeds),
                        NamedOnnxValue.CreateFromTensor("attention_mask", currentMask)
                    };
                    for (int i = 0; i < 30; i++)
                    {
                        lmInputs.Add(NamedOnnxValue.CreateFromTensor($"past_key_values.{i}.key", pastKeyValues[$"past_key_values.{i}.key"]));
                        lmInputs.Add(NamedOnnxValue.CreateFromTensor($"past_key_values.{i}.value", pastKeyValues[$"past_key_values.{i}.value"]));
                    }

                    using var lmResults = _languageModel.Run(lmInputs);
                    var logitsTensor = lmResults.First(o => o.Name == "logits").AsTensor<float>();

                    // 次のステップ用にKVキャッシュを更新
                    for (int i = 0; i < 30; i++)
                    {
                        var presentKey = lmResults.First(o => o.Name == $"present.{i}.key").AsTensor<float>();
                        var presentVal = lmResults.First(o => o.Name == $"present.{i}.value").AsTensor<float>();
                        pastKeyValues[$"past_key_values.{i}.key"] = CloneTensor(presentKey);
                        pastKeyValues[$"past_key_values.{i}.value"] = CloneTensor(presentVal);
                    }

                    // 最後のステップのLogitsを取得
                    int seqLen = logitsTensor.Dimensions[1];
                    int vocabSize = logitsTensor.Dimensions[2];
                    
                    // Logitsを配列に抽出（最後のタイムステップのみ）
                    float[] logits = new float[vocabSize];
                    for (int v = 0; v < vocabSize; v++)
                    {
                        logits[v] = logitsTensor[0, seqLen - 1, v];
                    }

                    // 反復ペナルティの適用（リファレンス: RepetitionPenaltyLogitsProcessor）
                    var uniqueTokens = new HashSet<long>(generateTokens);
                    foreach (long tokenId in uniqueTokens)
                    {
                        if (tokenId >= 0 && tokenId < vocabSize)
                        {
                            if (logits[tokenId] < 0)
                                logits[tokenId] *= repetitionPenalty;
                            else
                                logits[tokenId] /= repetitionPenalty;
                        }
                    }

                    // 確率的サンプリング (temperature = 0.8f, topP = 0.95f, minP = 0.05f)
                    long nextToken = Sample(logits, 0.8f, 0.95f, 0.05f, random);

                    // generate_tokens に追加
                    generateTokens.Add(nextToken);

                    // 終了トークン（6562）の検出
                    if (nextToken == 6562)
                    {
                        Log($"終了トークンを検出しました。ステップ: {step}");
                        break;
                    }

                    if (step < 20 || step % 50 == 0)
                    {
                        Log($"ステップ {step}: 生成トークンID = {nextToken} (Logit: {logits[(int)nextToken]})");
                    }

                    // 次のステップの入力埋め込みを生成
                    // リファレンス: position_ids = np.full((input_ids.shape[0], 1), i + 1, dtype=np.int64)
                    var nextTokenTensor = new DenseTensor<long>(new[] { nextToken }, new[] { 1, 1 });
                    var nextPositionTensor = new DenseTensor<long>(new[] { (long)(step + 1) }, new[] { 1, 1 });
                    
                    var nextEmbedInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", nextTokenTensor),
                        NamedOnnxValue.CreateFromTensor("position_ids", nextPositionTensor),
                        NamedOnnxValue.CreateFromTensor("exaggeration", exaggerationTensor)
                    };
                    using var nextEmbedResults = _embedTokens.Run(nextEmbedInputs);
                    currentEmbeds = CloneTensor(nextEmbedResults.First(o => o.Name == "inputs_embeds").AsTensor<float>());

                    // アテンションマスクを拡張
                    var newMaskValues = currentMask.ToArray().Concat(new[] { 1L }).ToArray();
                    currentMask = new DenseTensor<long>(newMaskValues, new[] { 1, newMaskValues.Length });
                }

                // speech_tokens の組み立て（リファレンス: generate_tokens[:, 1:-1] → START_SPEECH_TOKENと最後のSTOP_TOKENを除去）
                // generateTokens = [6561, token1, token2, ..., (6562 if stopped)]
                var finalSpeechTokens = generateTokens.Skip(1).ToList(); // START_SPEECH_TOKEN をスキップ
                if (finalSpeechTokens.Count > 0 && finalSpeechTokens.Last() == 6562)
                {
                    finalSpeechTokens.RemoveAt(finalSpeechTokens.Count - 1); // STOP_SPEECH_TOKEN を除去
                }
                
                if (finalSpeechTokens.Count == 0)
                {
                    finalSpeechTokens.Add(START_SPEECH_TOKEN);
                }
                Log($"自己回帰ループ完了。生成された音声トークン数: {finalSpeechTokens.Count}");
                Log($"生成された先頭20個のトークン: [{string.Join(", ", finalSpeechTokens.Take(20))}]");

                // 5. 拡散デコーダー（conditional_decoder）による波形合成
                // リファレンス: speech_tokens = np.concatenate([prompt_token, speech_tokens], axis=1)
                var promptTokenArray = promptToken.ToArray();
                var speechTokensArray = finalSpeechTokens.ToArray();
                var speechTokensAll = promptTokenArray.Concat(speechTokensArray).ToArray();
                var speechTokensAllTensor = new DenseTensor<long>(speechTokensAll, new[] { 1, speechTokensAll.Length });

                var decoderInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("speech_tokens", speechTokensAllTensor),
                    NamedOnnxValue.CreateFromTensor("speaker_embeddings", speakerEmbeddings),
                    NamedOnnxValue.CreateFromTensor("speaker_features", speakerFeatures)
                };
                
                Log("波形合成デコード中...");
                using var decoderResults = _condDecoder.Run(decoderInputs);
                var outputWav = decoderResults.First().AsTensor<float>();
                
                var wavData = outputWav.ToArray();
                float maxAbs = wavData.Length > 0 ? wavData.Max(Math.Abs) : 0f;
                float minVal = wavData.Length > 0 ? wavData.Min() : 0f;
                float maxValOut = wavData.Length > 0 ? wavData.Max() : 0f;
                Log($"波形合成成功: {wavData.Length} サンプル. MaxAbsAmp: {maxAbs}, Min: {minVal}, Max: {maxValOut}");

                return wavData;
            });
        }

        /// <summary>
        /// 長文を句読点で分割して個別に合成し、波形を結合するバッチ合成メソッド。
        /// 長文入力による自己回帰ループ崩壊を防ぐ。
        /// </summary>
        public async Task<float[]> GenerateBatchAsync(string fullText, string voicePath, float exaggeration, 
            MorphemeEngine morph, Tokenizer tokenizer, long langToken, Action<string>? statusCallback = null)
        {
            Log("=== GenerateBatchAsync 開始 ===");
            
            // 句読点で分割（。、！？!?.）
            var sentences = SplitSentences(fullText);
            Log($"文分割結果: {sentences.Count} 文");
            
            if (sentences.Count <= 1)
            {
                // 1文以下の場合はそのまま合成
                string processedText = fullText;
                if (langToken == 723) // 日本語
                {
                    var analysis = morph.Analyze(fullText);
                    processedText = string.Concat(analysis.Select(a => a.Reading));
                }
                var ids = tokenizer.Encode(processedText, langToken);
                return await GenerateAsync(ids, voicePath, exaggeration);
            }

            var allWavChunks = new List<float[]>();
            
            for (int i = 0; i < sentences.Count; i++)
            {
                string sentence = sentences[i];
                if (string.IsNullOrWhiteSpace(sentence)) continue;
                
                Log($"チャンク {i+1}/{sentences.Count}: \"{sentence}\"");
                statusCallback?.Invoke($"音声合成中... ({i+1}/{sentences.Count})");
                
                string processedText = sentence;
                if (langToken == 723) // 日本語
                {
                    var analysis = morph.Analyze(sentence);
                    processedText = string.Concat(analysis.Select(a => a.Reading));
                }
                
                var sentenceIds = tokenizer.Encode(processedText, langToken);
                var wav = await GenerateAsync(sentenceIds, voicePath, exaggeration);
                
                if (wav != null && wav.Length > 0)
                {
                    allWavChunks.Add(wav);
                }
            }

            if (allWavChunks.Count == 0)
            {
                Log("警告: 全チャンクの合成結果が空でした");
                return new float[0];
            }

            // 全波形を結合（チャンク間に短い無音を挿入）
            int silenceGap = 2400; // 0.1秒 (24000Hz * 0.1s)
            int totalLength = 0;
            foreach (var chunk in allWavChunks) totalLength += chunk.Length;
            totalLength += (allWavChunks.Count - 1) * silenceGap;

            var result = new float[totalLength];
            int offset = 0;
            for (int i = 0; i < allWavChunks.Count; i++)
            {
                Array.Copy(allWavChunks[i], 0, result, offset, allWavChunks[i].Length);
                offset += allWavChunks[i].Length;
                if (i < allWavChunks.Count - 1)
                {
                    // 無音ギャップ（0で埋め）
                    offset += silenceGap;
                }
            }

            Log($"バッチ合成完了。合計長: {result.Length} サンプル, チャンク数: {allWavChunks.Count}");
            return result;
        }

        /// <summary>
        /// テキストを句読点で文単位に分割する。
        /// </summary>
        private List<string> SplitSentences(string text)
        {
            var sentences = new List<string>();
            // 句読点を保持しつつ分割。連続する句読点（！？など）を一つの区切りとして扱う
            var segments = System.Text.RegularExpressions.Regex.Split(text, @"([。！？\n\.!\?]+)");
            
            int i = 0;
            while (i < segments.Length)
            {
                string seg = segments[i].Trim();
                if (i + 1 < segments.Length)
                {
                    string sep = segments[i + 1];
                    if (!string.IsNullOrEmpty(seg) || !string.IsNullOrWhiteSpace(sep))
                    {
                        sentences.Add(seg + sep);
                    }
                    i += 2;
                }
                else
                {
                    if (!string.IsNullOrEmpty(seg))
                    {
                        sentences.Add(seg);
                    }
                    i += 1;
                }
            }
            
            return sentences.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        private static int Sample(float[] logits, float temperature, float topP, float minP, Random random)
        {
            int n = logits.Length;
            
            // 1. Temperatureの適用と最大値の取得
            double maxLogit = double.NegativeInfinity;
            double[] tempLogits = new double[n];
            for (int i = 0; i < n; i++)
            {
                tempLogits[i] = logits[i] / temperature;
                if (tempLogits[i] > maxLogit)
                {
                    maxLogit = tempLogits[i];
                }
            }

            // 2. Softmax (アンダーフロー・オーバーフロー防止のために最大値を引く)
            double[] probs = new double[n];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                probs[i] = Math.Exp(tempLogits[i] - maxLogit);
                sum += probs[i];
            }

            // 正規化
            for (int i = 0; i < n; i++)
            {
                probs[i] /= sum;
            }

            // ソート
            var sorted = new List<(int Index, double Prob)>(n);
            for (int i = 0; i < n; i++)
            {
                sorted.Add((i, probs[i]));
            }
            sorted.Sort((a, b) => b.Prob.CompareTo(a.Prob));

            // 3. min_p フィルタリング
            double maxProb = sorted[0].Prob;
            double minThreshold = minP * maxProb;
            var minFiltered = new List<(int Index, double Prob)>();
            double sumMin = 0;
            foreach (var item in sorted)
            {
                if (item.Prob >= minThreshold)
                {
                    minFiltered.Add(item);
                    sumMin += item.Prob;
                }
                else
                {
                    break; // 降順ソートされているので、これ以降はすべて閾値未満
                }
            }

            // 再正規化
            for (int i = 0; i < minFiltered.Count; i++)
            {
                minFiltered[i] = (minFiltered[i].Index, minFiltered[i].Prob / sumMin);
            }

            // 4. top_p フィルタリング
            var topFiltered = new List<(int Index, double Prob)>();
            double sumTop = 0;
            double cumSum = 0;
            foreach (var item in minFiltered)
            {
                topFiltered.Add(item);
                sumTop += item.Prob;
                cumSum += item.Prob;
                if (cumSum > topP)
                {
                    break;
                }
            }

            // 再正規化
            for (int i = 0; i < topFiltered.Count; i++)
            {
                topFiltered[i] = (topFiltered[i].Index, topFiltered[i].Prob / sumTop);
            }

            // 5. サンプリング
            double r = random.NextDouble();
            double cumulative = 0;
            foreach (var item in topFiltered)
            {
                cumulative += item.Prob;
                if (r <= cumulative)
                {
                    return item.Index;
                }
            }

            return topFiltered[0].Index; // フォールバック
        }

        public void Dispose()
        {
            _speechEncoder?.Dispose();
            _languageModel?.Dispose();
            _condDecoder?.Dispose();
            _embedTokens?.Dispose();
        }
    }
}
