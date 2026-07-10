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
        private static readonly object _logLock = new object();
        private InferenceSession? _speechEncoder;
        private InferenceSession? _languageModel;
        private InferenceSession? _condDecoder;
        private InferenceSession? _embedTokens;

        private readonly string _modelsDir;
        private ModelType _currentModelType = ModelType.Multilingual;
        public string ActiveBackend { get; private set; } = "None";
        
        // embed_tokens.onnx のEmbedding行列の語彙サイズ（LoadModel 優先時自動検出）
        private long _embedVocabSize = -1;

        // モデルごとのファイル定義
        private string GetLMFileName(ModelType type) => type switch
        {
            ModelType.Turbo => "language_model.onnx",
            ModelType.English => "language_model.onnx",
            _ => "language_model_q4.onnx"
        };

        private string GetLMDataFileName(ModelType type) => type switch
        {
            ModelType.Turbo => "language_model.onnx_data",
            ModelType.English => "language_model.onnx_data",
            _ => GetLMFileName(type) + "_data"
        };

        private string GetModelSubDir(ModelType type) => type switch
        {
            ModelType.Turbo => "turbo",
            ModelType.English => "english",
            _ => "multilingual"
        };

        private string GetBaseUrl(ModelType type) => type switch
        {
            ModelType.Turbo => "https://huggingface.co/ResembleAI/chatterbox-turbo-ONNX/resolve/main/onnx/",
            ModelType.English => "https://huggingface.co/onnx-community/chatterbox-ONNX/resolve/main/onnx/",
            _ => "https://huggingface.co/onnx-community/chatterbox-multilingual-ONNX/resolve/main/onnx/"
        };


        public TTSEngine(string baseDir)
        {
            _modelsDir = Path.Combine(baseDir, "models");
            CleanupLegacyModels();
        }

        private void CleanupLegacyModels()
        {
            try
            {
                if (!Directory.Exists(_modelsDir)) return;

                string[] legacyFiles = {
                    "tokenizer.json", "tokenizer_mtl.json",
                    "speech_encoder.onnx", "speech_encoder.onnx_data", "speech_encoder_mtl.onnx", "speech_encoder_mtl.onnx_data",
                    "embed_tokens.onnx", "embed_tokens.onnx_data", "embed_tokens_mtl.onnx", "embed_tokens_mtl.onnx_data",
                    "conditional_decoder.onnx", "conditional_decoder.onnx_data", "conditional_decoder_mtl.onnx", "conditional_decoder_mtl.onnx_data",
                    "language_model_q4.onnx", "language_model_q4.onnx_data", "language_model.onnx", "language_model.onnx_data"
                };

                foreach (var file in legacyFiles)
                {
                    string path = Path.Combine(_modelsDir, file);
                    if (File.Exists(path))
                    {
                        Log($"古いモデルファイルをクリーンアップします: {path}");
                        File.Delete(path);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"古いモデルファイルのクリーンアップ中にエラーが発生しました: {ex.Message}");
            }
        }

        public async Task EnsureModelExistsAsync(ModelType type, Action<string, double> progressCallback)
        {
            string subDir = Path.Combine(_modelsDir, GetModelSubDir(type));
            if (!Directory.Exists(subDir)) Directory.CreateDirectory(subDir);

            var filesToDownload = new List<(string RemoteName, string LocalName)>();
            
            // default_voice.wav は共通 (_modelsDir 直下)
            filesToDownload.Add(("default_voice.wav", "../default_voice.wav"));

            // その他のファイルはモデル固有のフォルダへ元の名前のままダウンロード
            string[] baseFiles = {
                "tokenizer.json",
                "speech_encoder.onnx", "speech_encoder.onnx_data",
                "embed_tokens.onnx", "embed_tokens.onnx_data",
                "conditional_decoder.onnx", "conditional_decoder.onnx_data"
            };

            foreach (var bf in baseFiles)
            {
                filesToDownload.Add((bf, bf));
            }

            filesToDownload.Add((GetLMFileName(type), GetLMFileName(type)));
            filesToDownload.Add((GetLMDataFileName(type), GetLMDataFileName(type)));

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(10);
                client.DefaultRequestHeaders.Add("User-Agent", "CBoxTTS-Native-Downloader");

                foreach (var item in filesToDownload)
                {
                    string localPath = Path.GetFullPath(Path.Combine(subDir, item.LocalName));
                    if (File.Exists(localPath))
                    {
                        var info = new FileInfo(localPath);
                        long minSize = GetMinimumExpectedSize(item.LocalName);
                        if (info.Length >= minSize)
                        {
                            continue;
                        }
                        else
                        {
                            Log($"警告: {item.LocalName} のサイズが小さすぎます ({info.Length} bytes < 期待値 {minSize} bytes)。破損とみなして削除・再ダウンロードします。");
                            File.Delete(localPath);
                        }
                    }

                    string baseUrl = GetBaseUrl(type);
                    string url = (item.RemoteName.EndsWith(".json") || item.RemoteName == "default_voice.wav")
                        ? baseUrl.Replace("/onnx/", "/") + item.RemoteName
                        : baseUrl + item.RemoteName;

                    Log($"{Path.GetFileName(localPath)} のダウンロードを開始します: {url}");
                    
                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!response.IsSuccessStatusCode) {
                            Log($"警告: {item.RemoteName} の取得に失敗しました。スキップします。");
                            continue;
                        }

                        var totalBytes = response.Content.Headers.ContentLength ?? -1;
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        {
                            // 不完全ダウンロード保護: 一時ファイルに書き込み、完了後にリネーム
                            string tempPath = localPath + ".tmp";
                            long totalRead = 0L;
                            try
                            {
                                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                                {
                                    var buffer = new byte[8192];
                                    int read;

                                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                                    {
                                        await fileStream.WriteAsync(buffer, 0, read);
                                        totalRead += read;
                                        if (totalBytes > 0)
                                            progressCallback?.Invoke($"{Path.GetFileName(localPath)} をダウンロード中... ({(double)totalRead / totalBytes * 100:F1}%)", (double)totalRead / totalBytes * 100);
                                    }
                                }

                                if (totalBytes > 0 && totalRead != totalBytes)
                                {
                                    throw new IOException($"ダウンロードされたサイズ ({totalRead} bytes) が Content-Length ({totalBytes} bytes) と一致しません。");
                                }

                                // ダウンロード完了後にリネーム（アトミック操作）
                                if (File.Exists(localPath)) File.Delete(localPath);
                                File.Move(tempPath, localPath);
                            }
                            catch
                            {
                                // ダウンロード失敗時は不完全な一時ファイルを削除
                                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                                throw;
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

                string subDir = Path.Combine(_modelsDir, GetModelSubDir(type));
                string lmFile = GetLMFileName(type);
                string[] baseModelFiles = { "speech_encoder.onnx", "embed_tokens.onnx", "conditional_decoder.onnx", lmFile };
                
                string detectedBackend = "CPU";

                for (int i = 0; i < baseModelFiles.Length; i++)
                {
                    string baseName = baseModelFiles[i];
                    double pct = (double)i / baseModelFiles.Length * 100;
                    progressCallback?.Invoke($"{baseName} をロード中... ({i+1}/{baseModelFiles.Length})", pct);

                    string path = Path.Combine(subDir, baseName);
                    if (!File.Exists(path))
                    {
                        path = Path.Combine(AppContext.BaseDirectory, "models", GetModelSubDir(type), baseName);
                    }
                    
                    // ONNXモデルをロードする前に、GQA（GroupQueryAttention）の入力数パッチを自動適用
                    if (File.Exists(path) && baseName.StartsWith("language_model") && baseName.EndsWith(".onnx"))
                    {
                        OnnxGqaPatcher.PatchModel(path);
                    }
                    
                    Log($"[Session開始] {baseName} (Path: {path})");
                    
                    InferenceSession? session = null;
                    int retryCount = 5;
                    while (retryCount > 0)
                    {
                        try
                        {
                            var options = new SessionOptions();
#if USE_CUDA
                            try
                            {
                                using (var cudaProviderOptions = new OrtCUDAProviderOptions())
                                {
                                    var providerOptionsDict = new Dictionary<string, string>
                                    {
                                        { "device_id", "0" },
                                        { "cudnn_conv_use_max_workspace", "1" },
                                        { "cudnn_conv_algo_search", "HEURISTIC" }
                                    };
                                    cudaProviderOptions.UpdateOptions(providerOptionsDict);
                                    options.AppendExecutionProvider_CUDA(cudaProviderOptions);
                                }
                                session = new InferenceSession(path, options);
                                detectedBackend = "CUDA";
                                Log($"[Session成功:CUDA] {baseName}");
                            }
                            catch (Exception cudaEx)
                            {
                                Log($"[CUDA初期化失敗、CPUにフォールバックします] {baseName}: {cudaEx.Message}");
                                options.Dispose();
                                options = new SessionOptions();
                                options.IntraOpNumThreads = Math.Min(4, Environment.ProcessorCount);
                                session = new InferenceSession(path, options);
                                detectedBackend = "CPU (Fallback)";
                                Log($"[Session成功:CPU (CUDAフォールバック)] {baseName}");
                            }
#elif USE_DML
                            try
                            {
                                options.AppendExecutionProvider_DML(0);
                                session = new InferenceSession(path, options);
                                detectedBackend = "DirectML";
                                Log($"[Session成功:DirectML] {baseName}");
                            }
                            catch (Exception dmlEx)
                            {
                                Log($"[DirectML初期化失敗、CPUにフォールバックします] {baseName}: {dmlEx.Message}");
                                options.Dispose();
                                options = new SessionOptions();
                                options.IntraOpNumThreads = Math.Min(4, Environment.ProcessorCount);
                                session = new InferenceSession(path, options);
                                detectedBackend = "CPU (Fallback)";
                                Log($"[Session成功:CPU (DirectMLフォールバック)] {baseName}");
                            }
#else
                            options.IntraOpNumThreads = Math.Min(4, Environment.ProcessorCount);
                            session = new InferenceSession(path, options);
                            detectedBackend = "CPU";
                            Log($"[Session成功:CPU] {baseName}");
#endif
                            break;
                        }
                        catch (Exception ex) when (ex.Message.Contains("errcode = 32") || ex.Message.Contains("access") || ex.Message.Contains("locked") || ex.Message.Contains("使用中"))
                        {
                            retryCount--;
                            if (retryCount == 0)
                            {
                                Log($"[ロード失敗] {baseName}: ファイルロックが解除されませんでした。 {ex.Message}");
                                throw;
                            }
                            Log($"[ロード再試行] {baseName} がロックされています。1.5秒後にリトライします... (残り試行回数: {retryCount})");
                            Thread.Sleep(1500);
                        }
                        catch (Exception ex)
                        {
                            Log($"[ロード失敗] {baseName}: {ex.Message}");
                            throw;
                        }
                    }
                    
                    if (baseName.StartsWith("speech")) _speechEncoder = session;
                    else if (baseName.StartsWith("embed"))
                    {
                        _embedTokens = session;
                        // embed_tokens.onnx のEmbedding行列の語彙サイズを検出する。
                        _embedVocabSize = type switch
                        {
                            ModelType.English => 704,   // chatterbox-ONNX の embed_tokens は 704 行
                            ModelType.Turbo => 50257,   // chatterbox-turbo-ONNX
                            _ => 2454                   // chatterbox-multilingual-ONNX
                        };
                        Log($"[embed_tokens] モデルタイプ {type} の Embedding 語彙サイズを設定: {_embedVocabSize}");
                    }
                    else if (baseName.StartsWith("conditional")) _condDecoder = session;
                    else _languageModel = session;

                    Log($"[Metadata] {baseName} Inputs:");
                    foreach (var input in session!.InputMetadata)
                    {
                        Log($"  Input: {input.Key}, Shape: [{string.Join(",", input.Value.Dimensions)}]");
                    }
                    Log($"[Metadata] {baseName} Outputs:");
                    foreach (var output in session.OutputMetadata)
                    {
                        Log($"  Output: {output.Key}, Shape: [{string.Join(",", output.Value.Dimensions)}]");
                    }

                    progressCallback?.Invoke($"{baseName} ロード完了", (double)(i+1) / baseModelFiles.Length * 100);
                }
                
                _currentModelType = type;
                ActiveBackend = detectedBackend;
                Log($"=== 全エンジンのロードに成功しました (Embedding語彙サイズ: {_embedVocabSize}, バックエンド: {ActiveBackend}) ===");
            }
            catch (Exception ex)
            {
                Log($"FATAL ERROR during LoadModel: {ex}");
                throw;
            }
        }


        private static long GetMinimumExpectedSize(string fileName)
        {
            if (fileName.EndsWith(".json")) return 1024L; // 1KB
            if (fileName.EndsWith(".wav")) return 1024L; // 1KB
            
            // ONNXデータファイル (モデルの重み)
            if (fileName.EndsWith(".onnx_data"))
            {
                if (fileName.StartsWith("embed_tokens")) return 10 * 1024 * 1024L; // 10MB
                return 100 * 1024 * 1024L; // 100MB
            }
            
            // ONNXモデル構造ファイル
            if (fileName.EndsWith(".onnx"))
            {
                if (fileName.StartsWith("embed_tokens")) return 1024L; // 1KB
                if (fileName.StartsWith("language_model")) return 50 * 1024L; // 50KB
                return 500 * 1024L; // 500KB
            }
            
            return 0L;
        }

        private void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "debug.log");
                lock (_logLock)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                }
                Console.WriteLine(message);
            }
            catch { }
        }

        private static DenseTensor<T> CloneTensor<T>(Tensor<T> src)
        {
            return new DenseTensor<T>(src.ToArray(), src.Dimensions);
        }

        public async Task<float[]> GenerateAsync(long[] inputIds, string voicePath, float exaggeration = 0.5f, float temperature = 0.8f, float cfgWeight = 0.5f, float repetitionPenalty = 1.1f)
        {
            if (_speechEncoder == null || _languageModel == null || _condDecoder == null || _embedTokens == null)
                throw new InvalidOperationException("モデルがロードされていません。");

            return await Task.Run(() =>
            {
                Log("=== GenerateAsync 開始 ===");
                var random = new Random(); // サンプリング用の乱数生成器（非固定シード）
                
                long startSpeechToken = 6561L;
                long stopSpeechToken = 6562L;
                Log($"開始トークン={startSpeechToken}, 終了トークン={stopSpeechToken} (モデルタイプ={_currentModelType})");
                
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
                Log($"参照音声ロード完了: {refAudio.Length} サンプル ({(double)refAudio.Length / 24000:F2}秒)");

                // 1b. 参照音声の前処理（無音トリム + 音量正規化 + 10秒制限）
                refAudio = TrimSilence(refAudio, 0.01f);
                refAudio = NormalizeAudioVolume(refAudio);
                const int maxRefSamples = 24000 * 10; // 10秒制限
                if (refAudio.Length > maxRefSamples)
                {
                    // 先頭10秒を切り出す（末尾よりも先頭の方が話者特徴が安定しやすい）
                    refAudio = refAudio.Take(maxRefSamples).ToArray();
                    Log($"参照音声を10秒に制限しました: {refAudio.Length} サンプル");
                }
                else
                {
                    Log($"参照音声前処理後: {refAudio.Length} サンプル ({(double)refAudio.Length / 24000:F2}秒)");
                }

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
                
                var positionIds = new long[inputIdsLong.Length];
                for (int i = 0; i < inputIdsLong.Length; i++)
                {
                    positionIds[i] = inputIdsLong[i] >= startSpeechToken ? 0 : (long)(i - 1);
                }
                var positionIdsTensor = new DenseTensor<long>(positionIds, new[] { 1, positionIds.Length });
                var exaggerationTensor = new DenseTensor<float>(new[] { exaggeration }, new[] { 1 });

                var embedInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor)
                };
                if (_embedTokens.InputMetadata.ContainsKey("position_ids"))
                {
                    embedInputs.Add(NamedOnnxValue.CreateFromTensor("position_ids", positionIdsTensor));
                }
                if (_embedTokens.InputMetadata.ContainsKey("exaggeration"))
                {
                    embedInputs.Add(NamedOnnxValue.CreateFromTensor("exaggeration", exaggerationTensor));
                }
                
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
                // 音声開始トークン (startSpeechToken = 6561L) の埋め込みを取得して結合する (リファレンス準拠)
                var bosTokens = new long[] { startSpeechToken };
                var bosTokensTensor = new DenseTensor<long>(bosTokens, new[] { 1, bosTokens.Length });
                var bosPos = new long[] { 0L };
                var bosPosTensor = new DenseTensor<long>(bosPos, new[] { 1, bosPos.Length });
                
                var bosEmbedInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", bosTokensTensor)
                };
                if (_embedTokens.InputMetadata.ContainsKey("position_ids"))
                {
                    bosEmbedInputs.Add(NamedOnnxValue.CreateFromTensor("position_ids", bosPosTensor));
                }
                if (_embedTokens.InputMetadata.ContainsKey("exaggeration"))
                {
                    bosEmbedInputs.Add(NamedOnnxValue.CreateFromTensor("exaggeration", exaggerationTensor));
                }
                
                Log("BOSトークン埋め込み実行中...");
                using var bosEmbedResults = _embedTokens.Run(bosEmbedInputs);
                var bosEmbed = CloneTensor(bosEmbedResults.First(o => o.Name == "inputs_embeds").AsTensor<float>());
                var bosEmbedData = bosEmbed.ToArray();
                int bosSeqLen = bosEmbed.Dimensions[1]; // 通常は 1
                
                int totalSeqLen = condSeqLen + textSeqLen + bosSeqLen;
                
                var combinedEmbeds = new float[totalSeqLen * embedDim];
                Array.Copy(condEmbData, 0, combinedEmbeds, 0, condSeqLen * embedDim);
                Array.Copy(inputsEmbedsData, 0, combinedEmbeds, condSeqLen * embedDim, textSeqLen * embedDim);
                Array.Copy(bosEmbedData, 0, combinedEmbeds, (condSeqLen + textSeqLen) * embedDim, bosSeqLen * embedDim);
                var currentEmbeds = new DenseTensor<float>(combinedEmbeds, new[] { 1, totalSeqLen, embedDim });
                Log($"cond_emb + text_emb + bos_embed 結合完了: condSeqLen={condSeqLen}, textSeqLen={textSeqLen}, bosSeqLen={bosSeqLen}, totalSeqLen={totalSeqLen}");

                // CFG（Classifier-Free Guidance）用の無条件埋め込みを準備
                // 無条件パス: cond_emb + ゼロクリアされたテキスト埋め込み + bos_embed で推論する（リファレンス準拠）
                bool useCfg = cfgWeight > 0.01f && cfgWeight < 1.0f;
                DenseTensor<float>? uncondEmbeds = null;
                int uncondSeqLen = totalSeqLen; // 条件付きパスと完全に同一
                if (useCfg)
                {
                    var uncondData = new float[uncondSeqLen * embedDim];
                    Array.Copy(condEmbData, 0, uncondData, 0, condSeqLen * embedDim);
                    // テキスト埋め込み領域（condSeqLen から textSeqLen 長分）は 0.0f（ゼロクリア）のまま
                    // 末尾の bos_embed 部分をコピー
                    Array.Copy(bosEmbedData, 0, uncondData, (condSeqLen + textSeqLen) * embedDim, bosSeqLen * embedDim);
                    uncondEmbeds = new DenseTensor<float>(uncondData, new[] { 1, uncondSeqLen, embedDim });
                    Log($"CFG有効: cfg_weight={cfgWeight}, 無条件埋め込み長={uncondSeqLen} (テキスト埋め込み領域を0クリア、BOS埋め込みを保持しました)");
                }
                else
                {
                    Log($"CFG無効: cfg_weight={cfgWeight} (CFGなしで生成します)");
                }

                // アテンションマスクの初期化（cond_emb + テキスト長 + BOSトークン分）
                var currentMaskValues = Enumerable.Repeat(1L, totalSeqLen).ToArray();
                var currentMask = new DenseTensor<long>(currentMaskValues, new[] { 1, currentMaskValues.Length });

                // past_key_values の入力ポート数をメタデータから判定 (例: past_key_values.X.key の最大インデックスを取得)
                int numKvLayers = 0;
                while (_languageModel.InputMetadata.ContainsKey($"past_key_values.{numKvLayers}.key"))
                {
                    numKvLayers++;
                }
                Log($"言語モデルのKVレイヤー数を検出しました: {numKvLayers} レイヤー");

                // generate_tokens の初期化
                var generateTokens = new List<long> { startSpeechToken };
                
                int maxNewTokens = 760; // 安全のための最大トークン制限（英語長文でも途中打ち切りを防ぐ）
                Log("自己回帰ループ開始...");

                if (ActiveBackend == "CUDA")
                {
                    var cudaPastKeyValues = new Dictionary<string, OrtValue>();
                    var cudaUncondPastKeyValues = new Dictionary<string, OrtValue>();

                    OrtValue? currentEmbedsOrtVal = null;
                    OrtValue? uncondEmbedsOrtVal = null;
                    OrtValue? exaggerationOrt = null;

                    try
                    {
                        var cpuMemInfo = OrtMemoryInfo.DefaultInstance;
                        var cudaMemInfo = new OrtMemoryInfo("Cuda", OrtAllocatorType.DeviceAllocator, 0, OrtMemType.Default);
                        var outputNames = _languageModel.OutputNames.ToArray();

                        // 空のテンソルを初期値としてGPUキャッシュを構成 (0バイトなのでCPUで作成して問題なし)
                        var emptyArr = new float[0];
                        var emptyDims = new long[] { 1, 16, 0, 64 };
                        for (int i = 0; i < numKvLayers; i++)
                        {
                            cudaPastKeyValues[$"past_key_values.{i}.key"] = OrtValue.CreateTensorValueFromMemory<float>(emptyArr, emptyDims);
                            cudaPastKeyValues[$"past_key_values.{i}.value"] = OrtValue.CreateTensorValueFromMemory<float>(emptyArr, emptyDims);
                            if (useCfg)
                            {
                                cudaUncondPastKeyValues[$"past_key_values.{i}.key"] = OrtValue.CreateTensorValueFromMemory<float>(emptyArr, emptyDims);
                                cudaUncondPastKeyValues[$"past_key_values.{i}.value"] = OrtValue.CreateTensorValueFromMemory<float>(emptyArr, emptyDims);
                            }
                        }

                        // ボキャブラリーサイズをメタデータから検出
                        int vocabSize = 8194;
                        var lmOutputMetadata = _languageModel.OutputMetadata;
                        if (lmOutputMetadata.ContainsKey("logits"))
                        {
                            vocabSize = (int)lmOutputMetadata["logits"].Dimensions[2];
                        }

                        float[] condLogits = new float[vocabSize];
                        float[] uncondLogits = new float[vocabSize];
                        float[] step0LogitsBuffer = null;
                        float[] uncondStep0LogitsBuffer = null;

                        // ステップ 0 の初期埋め込みを OrtValue にラップ
                        currentEmbedsOrtVal = OrtValue.CreateTensorValueFromMemory<float>(cpuMemInfo, currentEmbeds.Buffer, currentEmbeds.Dimensions.ToArray().Select(d => (long)d).ToArray());
                        if (useCfg)
                        {
                            uncondEmbedsOrtVal = OrtValue.CreateTensorValueFromMemory<float>(cpuMemInfo, uncondEmbeds!.Buffer, uncondEmbeds.Dimensions.ToArray().Select(d => (long)d).ToArray());
                        }
                        if (_embedTokens.InputMetadata.ContainsKey("exaggeration"))
                        {
                            exaggerationOrt = OrtValue.CreateTensorValueFromMemory<float>(cpuMemInfo, exaggerationTensor.Buffer, exaggerationTensor.Dimensions.ToArray().Select(d => (long)d).ToArray());
                        }

                        for (int step = 0; step < maxNewTokens; step++)
                        {
                            DenseTensor<long>? posTensor = null;
                            if (_languageModel.InputMetadata.ContainsKey("position_ids"))
                            {
                                long[] posIds;
                                if (step == 0)
                                {
                                    posIds = new long[totalSeqLen];
                                    for (int p = 0; p < totalSeqLen; p++) posIds[p] = (long)p;
                                }
                                else
                                {
                                    posIds = new long[] { (long)(totalSeqLen - 1) };
                                }
                                posTensor = new DenseTensor<long>(posIds, new[] { 1, posIds.Length });
                            }

                            // ----------------- 1. 条件付きパスの推論 -----------------
                            using (var ioBinding = _languageModel.CreateIoBinding())
                            {
                                using var currentMaskOrt = OrtValue.CreateTensorValueFromMemory<long>(cpuMemInfo, currentMask.Buffer, currentMask.Dimensions.ToArray().Select(d => (long)d).ToArray());
                                ioBinding.BindInput("inputs_embeds", currentEmbedsOrtVal);
                                ioBinding.BindInput("attention_mask", currentMaskOrt);

                                OrtValue? posTensorOrt = null;
                                try
                                {
                                    if (posTensor != null)
                                    {
                                        posTensorOrt = OrtValue.CreateTensorValueFromMemory<long>(cpuMemInfo, posTensor.Buffer, posTensor.Dimensions.ToArray().Select(d => (long)d).ToArray());
                                        ioBinding.BindInput("position_ids", posTensorOrt);
                                    }

                                    for (int i = 0; i < numKvLayers; i++)
                                    {
                                        ioBinding.BindInput($"past_key_values.{i}.key", cudaPastKeyValues[$"past_key_values.{i}.key"]);
                                        ioBinding.BindInput($"past_key_values.{i}.value", cudaPastKeyValues[$"past_key_values.{i}.value"]);
                                    }

                                    // logitsをCPUバッファにバインド（CPU-GPU転送をこの32KBの配列のみに限定）
                                    OrtValue logitsOrtValue;
                                    if (step == 0)
                                    {
                                        step0LogitsBuffer = new float[totalSeqLen * vocabSize];
                                        logitsOrtValue = OrtValue.CreateTensorValueFromMemory<float>(cpuMemInfo, step0LogitsBuffer, new long[] { 1, totalSeqLen, vocabSize });
                                    }
                                    else
                                    {
                                        logitsOrtValue = OrtValue.CreateTensorValueFromMemory<float>(cpuMemInfo, condLogits, new long[] { 1, 1, vocabSize });
                                    }
                                    ioBinding.BindOutput("logits", logitsOrtValue);

                                    // KVキャッシュ出力はGPUメモリに直接書き込み
                                    for (int i = 0; i < numKvLayers; i++)
                                    {
                                        ioBinding.BindOutputToDevice($"present.{i}.key", cudaMemInfo);
                                        ioBinding.BindOutputToDevice($"present.{i}.value", cudaMemInfo);
                                    }

                                    _languageModel.RunWithBinding(new RunOptions(), ioBinding);

                                    var outputsList = ioBinding.GetOutputValues();
                                    var outputsMap = new Dictionary<string, OrtValue>();
                                    for (int idx = 0; idx < outputNames.Length; idx++)
                                    {
                                        outputsMap[outputNames[idx]] = outputsList[idx];
                                    }

                                    // 古いKVキャッシュOrtValueを解放し、新しいGPU上のOrtValueへの所有権を移譲（コピーなし）
                                    for (int i = 0; i < numKvLayers; i++)
                                    {
                                        cudaPastKeyValues[$"past_key_values.{i}.key"].Dispose();
                                        cudaPastKeyValues[$"past_key_values.{i}.value"].Dispose();
                                        cudaPastKeyValues[$"past_key_values.{i}.key"] = outputsMap[$"present.{i}.key"];
                                        cudaPastKeyValues[$"past_key_values.{i}.value"] = outputsMap[$"present.{i}.value"];
                                    }

                                    // 使わなかった出力（logitsなど）を明示的に破棄
                                    foreach (var name in outputNames)
                                    {
                                        if (name == "logits" || name.StartsWith("present")) continue;
                                        outputsMap[name].Dispose();
                                    }

                                    logitsOrtValue.Dispose();
                                }
                                finally
                                {
                                    posTensorOrt?.Dispose();
                                }
                            }

                            if (step == 0)
                            {
                                for (int v = 0; v < vocabSize; v++)
                                {
                                    condLogits[v] = step0LogitsBuffer[(totalSeqLen - 1) * vocabSize + v];
                                }
                            }

                            // ----------------- 2. 無条件パス（CFG）の推論 -----------------
                            float[] logits;
                            if (useCfg)
                            {
                                var targetEmbedsOrtVal = (step == 0) ? uncondEmbedsOrtVal! : currentEmbedsOrtVal;
                                using (var uncondIoBinding = _languageModel.CreateIoBinding())
                                {
                                    using var currentMaskOrt = OrtValue.CreateTensorValueFromMemory<long>(cpuMemInfo, currentMask.Buffer, currentMask.Dimensions.ToArray().Select(d => (long)d).ToArray());
                                    uncondIoBinding.BindInput("inputs_embeds", targetEmbedsOrtVal);
                                    uncondIoBinding.BindInput("attention_mask", currentMaskOrt);

                                    OrtValue? posTensorOrt = null;
                                    try
                                    {
                                        if (posTensor != null)
                                        {
                                            posTensorOrt = OrtValue.CreateTensorValueFromMemory<long>(cpuMemInfo, posTensor.Buffer, posTensor.Dimensions.ToArray().Select(d => (long)d).ToArray());
                                            uncondIoBinding.BindInput("position_ids", posTensorOrt);
                                        }

                                        for (int i = 0; i < numKvLayers; i++)
                                        {
                                            uncondIoBinding.BindInput($"past_key_values.{i}.key", cudaUncondPastKeyValues[$"past_key_values.{i}.key"]);
                                            uncondIoBinding.BindInput($"past_key_values.{i}.value", cudaUncondPastKeyValues[$"past_key_values.{i}.value"]);
                                        }

                                        OrtValue uncondLogitsOrtValue;
                                        if (step == 0)
                                        {
                                            uncondStep0LogitsBuffer = new float[totalSeqLen * vocabSize];
                                            uncondLogitsOrtValue = OrtValue.CreateTensorValueFromMemory<float>(cpuMemInfo, uncondStep0LogitsBuffer, new long[] { 1, totalSeqLen, vocabSize });
                                        }
                                        else
                                        {
                                            uncondLogitsOrtValue = OrtValue.CreateTensorValueFromMemory<float>(cpuMemInfo, uncondLogits, new long[] { 1, 1, vocabSize });
                                        }
                                        uncondIoBinding.BindOutput("logits", uncondLogitsOrtValue);

                                        for (int i = 0; i < numKvLayers; i++)
                                        {
                                            uncondIoBinding.BindOutputToDevice($"present.{i}.key", cudaMemInfo);
                                            uncondIoBinding.BindOutputToDevice($"present.{i}.value", cudaMemInfo);
                                        }

                                        _languageModel.RunWithBinding(new RunOptions(), uncondIoBinding);

                                        var uncondOutputsList = uncondIoBinding.GetOutputValues();
                                        var uncondOutputsMap = new Dictionary<string, OrtValue>();
                                        for (int idx = 0; idx < outputNames.Length; idx++)
                                        {
                                            uncondOutputsMap[outputNames[idx]] = uncondOutputsList[idx];
                                        }

                                        for (int i = 0; i < numKvLayers; i++)
                                        {
                                            cudaUncondPastKeyValues[$"past_key_values.{i}.key"].Dispose();
                                            cudaUncondPastKeyValues[$"past_key_values.{i}.value"].Dispose();
                                            cudaUncondPastKeyValues[$"past_key_values.{i}.key"] = uncondOutputsMap[$"present.{i}.key"];
                                            cudaUncondPastKeyValues[$"past_key_values.{i}.value"] = uncondOutputsMap[$"present.{i}.value"];
                                        }

                                        foreach (var name in outputNames)
                                        {
                                            if (name == "logits" || name.StartsWith("present")) continue;
                                            uncondOutputsMap[name].Dispose();
                                        }

                                        uncondLogitsOrtValue.Dispose();
                                    }
                                    finally
                                    {
                                        posTensorOrt?.Dispose();
                                    }
                                }

                                if (step == 0)
                                {
                                    for (int v = 0; v < vocabSize; v++)
                                    {
                                        uncondLogits[v] = uncondStep0LogitsBuffer[(totalSeqLen - 1) * vocabSize + v];
                                    }
                                }

                                logits = new float[vocabSize];
                                for (int v = 0; v < vocabSize; v++)
                                {
                                    logits[v] = condLogits[v] + (condLogits[v] - uncondLogits[v]) * cfgWeight;
                                }
                            }
                            else
                            {
                                logits = condLogits;
                            }

                            // ----------------- 3. ペナルティの適用とサンプリング -----------------
                            var uniqueTokens = new HashSet<long>(generateTokens);
                            foreach (long tokenId in uniqueTokens)
                            {
                                if (tokenId >= 0 && tokenId < vocabSize)
                                {
                                    if (logits[tokenId] < 0) logits[tokenId] *= repetitionPenalty;
                                    else logits[tokenId] /= repetitionPenalty;
                                }
                            }

                            const int recentWindow = 64;
                            float strongPenalty = repetitionPenalty * 1.3f;
                            int recentStart = Math.Max(0, generateTokens.Count - recentWindow);
                            var recentTokens = new HashSet<long>(generateTokens.Skip(recentStart));
                            foreach (long tokenId in recentTokens)
                            {
                                if (tokenId >= 0 && tokenId < vocabSize)
                                {
                                    if (logits[tokenId] < 0) logits[tokenId] *= strongPenalty;
                                    else logits[tokenId] /= strongPenalty;
                                }
                            }

                            if (generateTokens.Count >= 16)
                            {
                                bool loopDetected = false;
                                int checkCount = Math.Min(generateTokens.Count, 48);
                                var recent = generateTokens.Skip(generateTokens.Count - checkCount).ToArray();
                                for (int patLen = 3; patLen <= 8 && !loopDetected; patLen++)
                                {
                                    if (recent.Length < patLen * 3) continue;
                                    var tail = recent.Skip(recent.Length - patLen).ToArray();
                                    int matchCount = 1;
                                    for (int pos = 0; pos <= recent.Length - patLen - patLen; pos++)
                                    {
                                        bool match = true;
                                        for (int k = 0; k < patLen; k++)
                                        {
                                            if (recent[pos + k] != tail[k]) { match = false; break; }
                                        }
                                        if (match) matchCount++;
                                    }
                                    if (matchCount >= 3)
                                    {
                                        Log($"[ループ検出] パターン長={patLen} が {matchCount} 回繰り返されました。強制終了します。(ステップ={step})");
                                        loopDetected = true;
                                    }
                                }
                                if (loopDetected) break;
                            }

                            long nextToken = Sample(logits, temperature, 0.95f, 0.05f, random);
                            generateTokens.Add(nextToken);

                            if (nextToken == stopSpeechToken)
                            {
                                Log($"終了トークン({stopSpeechToken})を検出しました。ステップ: {step}");
                                break;
                            }

                            if (step < 20 || step % 50 == 0)
                            {
                                Log($"ステップ {step}: 生成トークンID = {nextToken} (Logit: {logits[(int)nextToken]})");
                            }

                            // 次の入力埋め込みを生成 (GPU-direct binding)
                            var nextTokenTensor = new DenseTensor<long>(new[] { nextToken }, new[] { 1, 1 });
                            var nextPositionTensor = new DenseTensor<long>(new[] { (long)(step + 1) }, new[] { 1, 1 });

                            using (var embedIoBinding = _embedTokens.CreateIoBinding())
                            {
                                using var nextTokenOrt = OrtValue.CreateTensorValueFromMemory<long>(cpuMemInfo, nextTokenTensor.Buffer, nextTokenTensor.Dimensions.ToArray().Select(d => (long)d).ToArray());
                                embedIoBinding.BindInput("input_ids", nextTokenOrt);

                                OrtValue? nextPosOrt = null;
                                try
                                {
                                    if (_embedTokens.InputMetadata.ContainsKey("position_ids"))
                                    {
                                        nextPosOrt = OrtValue.CreateTensorValueFromMemory<long>(cpuMemInfo, nextPositionTensor.Buffer, nextPositionTensor.Dimensions.ToArray().Select(d => (long)d).ToArray());
                                        embedIoBinding.BindInput("position_ids", nextPosOrt);
                                    }
                                    if (exaggerationOrt != null)
                                    {
                                        embedIoBinding.BindInput("exaggeration", exaggerationOrt);
                                    }

                                    embedIoBinding.BindOutputToDevice("inputs_embeds", cudaMemInfo);

                                    _embedTokens.RunWithBinding(new RunOptions(), embedIoBinding);

                                    var embedOutputs = embedIoBinding.GetOutputValues();
                                    var nextEmbedsOrtVal = embedOutputs.First();

                                    currentEmbedsOrtVal.Dispose();
                                    currentEmbedsOrtVal = nextEmbedsOrtVal;
                                }
                                finally
                                {
                                    nextPosOrt?.Dispose();
                                }
                            }

                            var newMaskValues = currentMask.ToArray().Concat(new[] { 1L }).ToArray();
                            currentMask = new DenseTensor<long>(newMaskValues, new[] { 1, newMaskValues.Length });

                            totalSeqLen++;
                        }
                    }
                    finally
                    {
                        foreach (var val in cudaPastKeyValues.Values) val.Dispose();
                        foreach (var val in cudaUncondPastKeyValues.Values) val.Dispose();
                        currentEmbedsOrtVal?.Dispose();
                        uncondEmbedsOrtVal?.Dispose();
                        exaggerationOrt?.Dispose();
                    }
                }
                else
                {
                    // CPU / DirectML フォールバックパス
                    var pastKeyValues = new Dictionary<string, DenseTensor<float>>();
                    for (int i = 0; i < numKvLayers; i++)
                    {
                        pastKeyValues[$"past_key_values.{i}.key"] = new DenseTensor<float>(new float[0], new[] { 1, 16, 0, 64 });
                        pastKeyValues[$"past_key_values.{i}.value"] = new DenseTensor<float>(new float[0], new[] { 1, 16, 0, 64 });
                    }

                    var uncondPastKeyValues = new Dictionary<string, DenseTensor<float>>();
                    if (useCfg)
                    {
                        for (int i = 0; i < numKvLayers; i++)
                        {
                            uncondPastKeyValues[$"past_key_values.{i}.key"] = new DenseTensor<float>(new float[0], new[] { 1, 16, 0, 64 });
                            uncondPastKeyValues[$"past_key_values.{i}.value"] = new DenseTensor<float>(new float[0], new[] { 1, 16, 0, 64 });
                        }
                    }

                    int vocabSize = 8194;
                    var lmOutputMetadata = _languageModel.OutputMetadata;
                    if (lmOutputMetadata.ContainsKey("logits"))
                    {
                        vocabSize = (int)lmOutputMetadata["logits"].Dimensions[2];
                    }

                    for (int step = 0; step < maxNewTokens; step++)
                    {
                        var lmInputs = new List<NamedOnnxValue>
                        {
                            NamedOnnxValue.CreateFromTensor("inputs_embeds", currentEmbeds),
                            NamedOnnxValue.CreateFromTensor("attention_mask", currentMask)
                        };
                        
                        DenseTensor<long>? posTensor = null;
                        if (_languageModel.InputMetadata.ContainsKey("position_ids"))
                        {
                            long[] posIds;
                            if (step == 0)
                            {
                                posIds = new long[totalSeqLen];
                                for (int p = 0; p < totalSeqLen; p++) posIds[p] = (long)p;
                            }
                            else
                            {
                                posIds = new long[] { (long)(totalSeqLen - 1) };
                            }
                            posTensor = new DenseTensor<long>(posIds, new[] { 1, posIds.Length });
                            lmInputs.Add(NamedOnnxValue.CreateFromTensor("position_ids", posTensor));
                        }

                        for (int i = 0; i < numKvLayers; i++)
                        {
                            lmInputs.Add(NamedOnnxValue.CreateFromTensor($"past_key_values.{i}.key", pastKeyValues[$"past_key_values.{i}.key"]));
                            lmInputs.Add(NamedOnnxValue.CreateFromTensor($"past_key_values.{i}.value", pastKeyValues[$"past_key_values.{i}.value"]));
                        }

                        using var lmResults = _languageModel.Run(lmInputs);
                        var logitsTensor = lmResults.First(o => o.Name == "logits").AsTensor<float>();

                        for (int i = 0; i < numKvLayers; i++)
                        {
                            var presentKey = lmResults.First(o => o.Name == $"present.{i}.key").AsTensor<float>();
                            var presentVal = lmResults.First(o => o.Name == $"present.{i}.value").AsTensor<float>();
                            pastKeyValues[$"past_key_values.{i}.key"] = CloneTensor(presentKey);
                            pastKeyValues[$"past_key_values.{i}.value"] = CloneTensor(presentVal);
                        }

                        int seqLen = logitsTensor.Dimensions[1];
                        float[] condLogits = new float[vocabSize];
                        for (int v = 0; v < vocabSize; v++)
                        {
                            condLogits[v] = logitsTensor[0, seqLen - 1, v];
                        }

                        float[] logits;
                        if (useCfg)
                        {
                            DenseTensor<float> uncondCurrentEmbeds = (step == 0) ? uncondEmbeds! : currentEmbeds;
                            var uncondLmInputs = new List<NamedOnnxValue>
                            {
                                NamedOnnxValue.CreateFromTensor("inputs_embeds", uncondCurrentEmbeds),
                                NamedOnnxValue.CreateFromTensor("attention_mask", currentMask)
                            };

                            if (posTensor != null)
                            {
                                uncondLmInputs.Add(NamedOnnxValue.CreateFromTensor("position_ids", posTensor));
                            }

                            for (int i = 0; i < numKvLayers; i++)
                            {
                                uncondLmInputs.Add(NamedOnnxValue.CreateFromTensor($"past_key_values.{i}.key", uncondPastKeyValues[$"past_key_values.{i}.key"]));
                                uncondLmInputs.Add(NamedOnnxValue.CreateFromTensor($"past_key_values.{i}.value", uncondPastKeyValues[$"past_key_values.{i}.value"]));
                            }

                            using var uncondLmResults = _languageModel.Run(uncondLmInputs);
                            var uncondLogitsTensor = uncondLmResults.First(o => o.Name == "logits").AsTensor<float>();

                            for (int i = 0; i < numKvLayers; i++)
                            {
                                var presentKey = uncondLmResults.First(o => o.Name == $"present.{i}.key").AsTensor<float>();
                                var presentVal = uncondLmResults.First(o => o.Name == $"present.{i}.value").AsTensor<float>();
                                uncondPastKeyValues[$"past_key_values.{i}.key"] = CloneTensor(presentKey);
                                uncondPastKeyValues[$"past_key_values.{i}.value"] = CloneTensor(presentVal);
                            }

                            int uncondSeqLenOut = uncondLogitsTensor.Dimensions[1];
                            float[] uncondLogits = new float[vocabSize];
                            for (int v = 0; v < vocabSize; v++)
                            {
                                uncondLogits[v] = uncondLogitsTensor[0, uncondSeqLenOut - 1, v];
                            }

                            logits = new float[vocabSize];
                            for (int v = 0; v < vocabSize; v++)
                            {
                                logits[v] = condLogits[v] + cfgWeight * (condLogits[v] - uncondLogits[v]);
                            }
                        }
                        else
                        {
                            logits = condLogits;
                        }

                        var uniqueTokens = new HashSet<long>(generateTokens);
                        foreach (long tokenId in uniqueTokens)
                        {
                            if (tokenId >= 0 && tokenId < vocabSize)
                            {
                                if (logits[tokenId] < 0) logits[tokenId] *= repetitionPenalty;
                                else logits[tokenId] /= repetitionPenalty;
                            }
                        }

                        const int recentWindow = 64;
                        float strongPenalty = repetitionPenalty * 1.3f;
                        int recentStart = Math.Max(0, generateTokens.Count - recentWindow);
                        var recentTokens = new HashSet<long>(generateTokens.Skip(recentStart));
                        foreach (long tokenId in recentTokens)
                        {
                            if (tokenId >= 0 && tokenId < vocabSize)
                            {
                                if (logits[tokenId] < 0) logits[tokenId] *= strongPenalty;
                                else logits[tokenId] /= strongPenalty;
                            }
                        }

                        if (generateTokens.Count >= 16)
                        {
                            bool loopDetected = false;
                            int checkCount = Math.Min(generateTokens.Count, 48);
                            var recent = generateTokens.Skip(generateTokens.Count - checkCount).ToArray();
                            for (int patLen = 3; patLen <= 8 && !loopDetected; patLen++)
                            {
                                if (recent.Length < patLen * 3) continue;
                                var tail = recent.Skip(recent.Length - patLen).ToArray();
                                int matchCount = 1;
                                for (int pos = 0; pos <= recent.Length - patLen - patLen; pos++)
                                {
                                    bool match = true;
                                    for (int k = 0; k < patLen; k++)
                                    {
                                        if (recent[pos + k] != tail[k]) { match = false; break; }
                                    }
                                    if (match) matchCount++;
                                }
                                if (matchCount >= 3)
                                {
                                    Log($"[ループ検出] パターン長={patLen} が {matchCount} 回繰り返されました。強制終了します。(ステップ={step})");
                                    loopDetected = true;
                                }
                            }
                            if (loopDetected) break;
                        }

                        long nextToken = Sample(logits, temperature, 0.95f, 0.05f, random);
                        generateTokens.Add(nextToken);

                        if (nextToken == stopSpeechToken)
                        {
                            Log($"終了トークン({stopSpeechToken})を検出しました。ステップ: {step}");
                            break;
                        }

                        if (step < 20 || step % 50 == 0)
                        {
                            Log($"ステップ {step}: 生成トークンID = {nextToken} (Logit: {logits[(int)nextToken]})");
                        }

                        var nextTokenTensor = new DenseTensor<long>(new[] { nextToken }, new[] { 1, 1 });
                        var nextPositionTensor = new DenseTensor<long>(new[] { (long)(step + 1) }, new[] { 1, 1 });
                        var nextEmbedInputs = new List<NamedOnnxValue>
                        {
                            NamedOnnxValue.CreateFromTensor("input_ids", nextTokenTensor)
                        };
                        if (_embedTokens.InputMetadata.ContainsKey("position_ids"))
                        {
                            nextEmbedInputs.Add(NamedOnnxValue.CreateFromTensor("position_ids", nextPositionTensor));
                        }
                        if (_embedTokens.InputMetadata.ContainsKey("exaggeration"))
                        {
                            nextEmbedInputs.Add(NamedOnnxValue.CreateFromTensor("exaggeration", exaggerationTensor));
                        }
                        using var nextEmbedResults = _embedTokens.Run(nextEmbedInputs);
                        currentEmbeds = CloneTensor(nextEmbedResults.First(o => o.Name == "inputs_embeds").AsTensor<float>());

                        var newMaskValues = currentMask.ToArray().Concat(new[] { 1L }).ToArray();
                        currentMask = new DenseTensor<long>(newMaskValues, new[] { 1, newMaskValues.Length });

                            totalSeqLen++;
                        }
                    }

                // speech_tokens の組み立て（リファレンス: generate_tokens[:, 1:-1] → START_SPEECH_TOKENと最後のSTOP_TOKENを除去）
                // generateTokens = [6561, token1, token2, ..., (6562 if stopped)]
                var finalSpeechTokens = generateTokens.Skip(1).ToList(); // startSpeechToken をスキップ
                if (finalSpeechTokens.Count > 0 && finalSpeechTokens.Last() == stopSpeechToken)
                {
                    finalSpeechTokens.RemoveAt(finalSpeechTokens.Count - 1); // stopSpeechToken を除去
                }
                
                if (finalSpeechTokens.Count == 0)
                {
                    finalSpeechTokens.Add(startSpeechToken);
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
        public async Task<float[]> GenerateBatchAsync(string fullText, string voicePath, float exaggeration, float temperature, 
            MorphemeEngine morph, Tokenizer tokenizer, long langToken, float cfgWeight = 0.5f, float repetitionPenalty = 1.1f, Action<string>? statusCallback = null)
        {
            Log("=== GenerateBatchAsync 開始 ===");
            
            // embed_tokens の Embedding 語彙サイズを Tokenizer に伝達（範囲外アクセス防止）
            if (_embedVocabSize > 0)
            {
                tokenizer.SetEmbeddingVocabSize(_embedVocabSize);
            }
            
            string normalizedText = fullText;
            if (langToken == 708 || langToken == 1) // 英語 ([en] トークン または 英語専用モデルの UNK トークン)
            {
                normalizedText = EnglishNormalizer.Normalize(fullText);
            }

            // 英語の場合、テキストが比較的短い（350文字以下）なら分割せずに1文として処理して流暢性を劇的に向上させる
            // ただし、文頭に数字マーカー（1, や 2. など）がある場合は分割して間を空けるためにショートカットを回避する
            bool isEnglish = (langToken == 708 || langToken == 1);
            bool hasNumberMarker = System.Text.RegularExpressions.Regex.IsMatch(normalizedText, @"(?<=^|\n)[0-9]+\s*[\.,\)]+\s+", System.Text.RegularExpressions.RegexOptions.Multiline);
            List<string> sentences;
            if (isEnglish && normalizedText.Length <= 350 && !hasNumberMarker)
            {
                sentences = new List<string> { normalizedText };
                Log("英語テキストが350文字以下のため、分割せずに1つの文として合成します。");
            }
            else
            {
                sentences = SplitSentences(normalizedText);
                // 英語の場合、短い文を前の文に結合してチャンク数を減らし流暢性を向上
                if (isEnglish && sentences.Count > 1)
                {
                    sentences = MergeShortSentencesForEnglish(sentences, 350);
                    Log($"英語文結合後: {sentences.Count} チャンク");
                }
            }
            Log($"文分割結果: {sentences.Count} 文");
            
            if (sentences.Count <= 1)
            {
                // 1文以下の場合はそのまま合成
                string processedText = normalizedText;
                if (langToken == 723) // 日本語
                {
                    var analysis = morph.Analyze(normalizedText);
                    processedText = string.Concat(analysis.Select(a => a.Reading));
                }
                var ids = tokenizer.Encode(processedText, langToken);
                var singleWav = await GenerateAsync(ids, voicePath, exaggeration, temperature, cfgWeight, repetitionPenalty);
                return PadAudio(singleWav);
            }

            var allWavChunks = new List<float[]>();
            
            for (int i = 0; i < sentences.Count; i++)
            {
                string sentence = sentences[i];
                if (string.IsNullOrWhiteSpace(sentence))
                {
                    allWavChunks.Add(Array.Empty<float>()); // インデックスを sentences と完全に一致させるため空の波形を追加
                    continue;
                }
                
                Log($"チャンク {i+1}/{sentences.Count}: \"{sentence}\"");
                statusCallback?.Invoke($"音声合成中... ({i+1}/{sentences.Count})");
                
                string processedText = sentence;
                if (langToken == 723) // 日本語
                {
                    var analysis = morph.Analyze(sentence);
                    processedText = string.Concat(analysis.Select(a => a.Reading));
                }
                
                var sentenceIds = tokenizer.Encode(processedText, langToken);
                var wav = await GenerateAsync(sentenceIds, voicePath, exaggeration, temperature, cfgWeight, repetitionPenalty);
                
                if (wav != null && wav.Length > 0)
                {
                    wav = TrimSilence(wav, 0.02f); // 各チャンクの冒頭・末尾の無音を除去
                    allWavChunks.Add(wav);
                }
                else
                {
                    allWavChunks.Add(Array.Empty<float>()); // 合成失敗時もインデックス維持のため追加
                }
            }

            if (allWavChunks.Count == 0)
            {
                Log("警告: 全チャンクの合成結果が空でした");
                return new float[0];
            }

            // 全波形を結合
            float[] result;
            if (isEnglish)
            {
                // 英語: クロスフェード結合でスラスラと自然に接続
                result = CrossfadeJoinChunks(allWavChunks, crossfadeSamples: 1200); // 50ms @ 24kHz
            }
            else
            {
                // 日本語: 従来通り無音ギャップで結合
                int silenceGap = 2400;
                int totalLength = 0;
                foreach (var chunk in allWavChunks) totalLength += chunk.Length;
                totalLength += (allWavChunks.Count - 1) * silenceGap;

                result = new float[totalLength];
                int offset = 0;
                for (int i = 0; i < allWavChunks.Count; i++)
                {
                    Array.Copy(allWavChunks[i], 0, result, offset, allWavChunks[i].Length);
                    offset += allWavChunks[i].Length;
                    if (i < allWavChunks.Count - 1)
                    {
                        offset += silenceGap;
                    }
                }
            }

            Log($"バッチ合成完了。合計長: {result.Length} サンプル, チャンク数: {allWavChunks.Count}");
            return PadAudio(result);
        }

        /// <summary>
        /// 音声波形の冒頭と末尾に無音パディングを追加して、再生時の頭切れやぶつ切りを防ぐ。
        /// さらに、無音境界部でのプチ音（ポップノイズ）を防ぐため、極めて短いフェードイン・フェードアウト（5ms = 120サンプル）を適用する。
        /// </summary>
        private float[] PadAudio(float[] audio)
        {
            if (audio == null || audio.Length == 0) return audio ?? Array.Empty<float>();

            // 冒頭に 0.15秒 (3600サンプル)、末尾に 0.10秒 (2400サンプル) の無音を追加
            int startPadding = 3600;
            int endPadding = 2400;

            var padded = new float[audio.Length + startPadding + endPadding];
            Array.Copy(audio, 0, padded, startPadding, audio.Length);

            // ポップノイズ低減用フェードイン・アウト (5ms @ 24kHz = 120サンプル)
            int fadeSamples = Math.Min(120, audio.Length / 2);
            if (fadeSamples > 0)
            {
                // 音声開始部（インデックス: startPadding から startPadding + fadeSamples - 1）
                for (int i = 0; i < fadeSamples; i++)
                {
                    float factor = (float)i / fadeSamples;
                    padded[startPadding + i] *= factor;
                }

                // 音声終了部（インデックス: startPadding + audio.Length - fadeSamples から同終了位置）
                int endStart = startPadding + audio.Length - fadeSamples;
                for (int i = 0; i < fadeSamples; i++)
                {
                    float factor = 1.0f - ((float)i / fadeSamples);
                    padded[endStart + i] *= factor;
                }
            }

            Log($"無音パディング・フェード適用: 冒頭 {startPadding} サンプル, 末尾 {endPadding} サンプルを追加 (フェード長: {fadeSamples} サンプル)");
            return padded;
        }

        /// <summary>
        /// 参照音声の音量を正規化（ピークを最大振幅 -1.0 dB ≒ 0.89 にスケーリング）する。
        /// 話者特徴抽出における音量依存のばらつきを排除する。
        /// </summary>
        private float[] NormalizeAudioVolume(float[] audio)
        {
            if (audio == null || audio.Length == 0) return audio ?? Array.Empty<float>();

            float maxAbs = 0f;
            for (int i = 0; i < audio.Length; i++)
            {
                float absVal = Math.Abs(audio[i]);
                if (absVal > maxAbs) maxAbs = absVal;
            }

            if (maxAbs < 1e-5f)
            {
                Log("参照音声の音量が極めて小さいため、音量正規化をスキップします。");
                return audio; // 無音に近い場合は処理しない
            }

            // ピークを 0.89 (-1.0 dB) に正規化
            float targetPeak = 0.89f;
            float scale = targetPeak / maxAbs;

            // スケール変更が微小（1%未満）な場合はスキップ
            if (Math.Abs(scale - 1.0f) < 0.01f)
            {
                return audio;
            }

            var normalized = new float[audio.Length];
            for (int i = 0; i < audio.Length; i++)
            {
                normalized[i] = audio[i] * scale;
            }

            Log($"参照音声音量正規化適用: Peak {maxAbs:F4} → {targetPeak:F2} (Scale: {scale:F4})");
            return normalized;
        }

        /// <summary>
        /// 音声波形の先頭と末尾の無音区間をトリムする。
        /// 参照音声の前処理で使用し、エンコーダへの入力品質を向上させる。
        /// </summary>
        /// <param name="audio">音声波形データ</param>
        /// <param name="threshold">無音判定しきい値（振幅の絶対値）</param>
        private float[] TrimSilence(float[] audio, float threshold = 0.01f)
        {
            if (audio == null || audio.Length == 0) return audio ?? Array.Empty<float>();

            // 先頭の無音を検出
            int startIdx = 0;
            for (int i = 0; i < audio.Length; i++)
            {
                if (Math.Abs(audio[i]) > threshold)
                {
                    startIdx = i;
                    break;
                }
            }

            // 末尾の無音を検出
            int endIdx = audio.Length - 1;
            for (int i = audio.Length - 1; i >= startIdx; i--)
            {
                if (Math.Abs(audio[i]) > threshold)
                {
                    endIdx = i;
                    break;
                }
            }

            // トリム前後に少し余裕を持たせる（480サンプル = 0.02秒）
            int margin = 480;
            startIdx = Math.Max(0, startIdx - margin);
            endIdx = Math.Min(audio.Length - 1, endIdx + margin);

            int trimmedLength = endIdx - startIdx + 1;
            if (trimmedLength <= 0 || trimmedLength >= audio.Length)
            {
                return audio; // トリム不要またはすべて無音
            }

            var trimmed = new float[trimmedLength];
            Array.Copy(audio, startIdx, trimmed, 0, trimmedLength);
            Log($"無音トリム: {audio.Length} → {trimmedLength} サンプル (先頭 {startIdx} / 末尾 {audio.Length - endIdx - 1} サンプル除去)");
            return trimmed;
        }

        private static bool IsNumberMarker(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            // 1つ以上の記号（カンマ、ピリオド、右括弧）の連続を許容する
            return System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), @"^[0-9]+\s*[\.,\)]+\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
        }

        /// <summary>
        /// テキストを句読点で文単位に分割する。文頭の数字マーカー(1, や 2. など)も個別に分割する。
        /// </summary>
        private List<string> SplitSentences(string text)
        {
            var sentences = new List<string>();
            
            // 文頭の数字リストマーカー (例: "1, ", "1,, ", "2. ", "10) ") の後に改行を挟んで通常分割に回す
            string processedText = System.Text.RegularExpressions.Regex.Replace(
                text, 
                @"(?<=^|\n)([0-9]+\s*[\.,\)]+)\s+", 
                "$1\n",
                System.Text.RegularExpressions.RegexOptions.Multiline
            );

            // 句読点を保持しつつ分割。連続する句読点（！？など）を一つの区切りとして扱う
            var segments = System.Text.RegularExpressions.Regex.Split(processedText, @"([。！？\n\.!\?]+)");
            
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

        /// <summary>
        /// 英語専用: 短い文を前の文に結合し、チャンク数を削減して流暢性を向上させる。
        /// ただし、数字マーカー(1, や 2. など)は他の文と結合せず独立したチャンクとする。
        /// </summary>
        private List<string> MergeShortSentencesForEnglish(List<string> sentences, int maxChunkLength)
        {
            if (sentences.Count <= 1) return sentences;

            var merged = new List<string>();
            var current = new System.Text.StringBuilder();

            foreach (var sentence in sentences)
            {
                bool isMarker = IsNumberMarker(sentence);
                
                if (isMarker)
                {
                    // 数字マーカーを発見した場合、構築中のチャンクがあれば一旦確定する
                    if (current.Length > 0)
                    {
                        merged.Add(current.ToString());
                        current.Clear();
                    }
                    // 数字マーカーは単独チャンクとして追加
                    merged.Add(sentence);
                }
                else
                {
                    if (current.Length == 0)
                    {
                        current.Append(sentence);
                    }
                    else if (current.Length + 1 + sentence.Length <= maxChunkLength && !IsNumberMarker(current.ToString()))
                    {
                        // 結合してもチャンク上限内なら結合
                        current.Append(" ");
                        current.Append(sentence);
                    }
                    else
                    {
                        // 上限を超える、または直前の確定分が数字マーカーなら新規開始
                        merged.Add(current.ToString());
                        current.Clear();
                        current.Append(sentence);
                    }
                }
            }

            if (current.Length > 0)
            {
                merged.Add(current.ToString());
            }

            return merged;
        }

        /// <summary>
        /// 複数の音声チャンクをクロスフェードで結合し、チャンク間の継ぎ目を自然にする。
        /// 前のチャンクの末尾と次のチャンクの先頭を重ね合わせ、フェードアウト/フェードインで滑らかに遷移する。
        /// </summary>
        private float[] CrossfadeJoinChunks(List<float[]> chunks, int crossfadeSamples = 1200)
        {
            if (chunks.Count == 0) return Array.Empty<float>();
            if (chunks.Count == 1) return chunks[0];

            // 合計長を計算（クロスフェードによる重なり分を差し引く）
            int totalLength = chunks[0].Length;
            for (int i = 1; i < chunks.Count; i++)
            {
                int overlap = Math.Min(crossfadeSamples, Math.Min(chunks[i - 1].Length / 2, chunks[i].Length / 2));
                totalLength += chunks[i].Length - overlap;
            }

            var result = new float[totalLength];
            Array.Copy(chunks[0], 0, result, 0, chunks[0].Length);
            int writePos = chunks[0].Length;

            for (int c = 1; c < chunks.Count; c++)
            {
                float[] prev = chunks[c - 1];
                float[] next = chunks[c];
                int overlap = Math.Min(crossfadeSamples, Math.Min(prev.Length / 2, next.Length / 2));

                // 書き込み位置を重なり分だけ戻す
                writePos -= overlap;

                // 重なり区間: 前チャンクのフェードアウト + 次チャンクのフェードイン
                for (int i = 0; i < overlap; i++)
                {
                    float fadeOut = 1.0f - (float)i / overlap; // 前チャンク: 1.0→0.0
                    float fadeIn = (float)i / overlap;          // 次チャンク: 0.0→1.0
                    result[writePos + i] = result[writePos + i] * fadeOut + next[i] * fadeIn;
                }

                // 重なり区間以降の残りデータをコピー
                int remaining = next.Length - overlap;
                if (remaining > 0)
                {
                    Array.Copy(next, overlap, result, writePos + overlap, remaining);
                }

                writePos += next.Length;
            }

            Log($"クロスフェード結合完了: {chunks.Count} チャンク, 重なり {crossfadeSamples} サンプル (50ms), 合計 {totalLength} サンプル");
            return result;
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
            _speechEncoder = null;
            _languageModel?.Dispose();
            _languageModel = null;
            _condDecoder?.Dispose();
            _condDecoder = null;
            _embedTokens?.Dispose();
            _embedTokens = null;
        }
    }

    public class PbElement
    {
        public uint Tag { get; set; }
        public int FieldNumber => (int)(Tag >> 3);
        public int WireType => (int)(Tag & 7);

        public ulong VarintVal { get; set; }
        public ulong Fixed64Val { get; set; }
        public uint Fixed32Val { get; set; }
        public byte[]? RawBytes { get; set; }
        public List<PbElement>? SubElements { get; set; }

        public void WriteTo(Stream stream)
        {
            WriteVarint(stream, Tag);
            switch (WireType)
            {
                case 0:
                    WriteVarint(stream, VarintVal);
                    break;
                case 1:
                    WriteFixed64(stream, Fixed64Val);
                    break;
                case 2:
                    if (SubElements != null)
                    {
                        using (var ms = new MemoryStream())
                        {
                            foreach (var sub in SubElements)
                            {
                                sub.WriteTo(ms);
                            }
                            var bytes = ms.ToArray();
                            WriteVarint(stream, (ulong)bytes.Length);
                            stream.Write(bytes, 0, bytes.Length);
                        }
                    }
                    else if (RawBytes != null)
                    {
                        WriteVarint(stream, (ulong)RawBytes.Length);
                        stream.Write(RawBytes, 0, RawBytes.Length);
                    }
                    else
                    {
                        WriteVarint(stream, 0);
                    }
                    break;
                case 5:
                    WriteFixed32(stream, Fixed32Val);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported wire type: {WireType}");
            }
        }

        private static void WriteVarint(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }

        private static void WriteFixed64(Stream stream, ulong value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteFixed32(Stream stream, uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    public static class OnnxGqaPatcher
    {
        public static bool PatchModel(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            // パッチ済みマーカーファイルが存在すれば、再パッチをスキップ（大容量ファイルのメモリ圧迫回避）
            string markerPath = filePath + ".patched";
            if (File.Exists(markerPath)) return false;

            byte[] modelData;
            try
            {
                modelData = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading ONNX file for patch: {ex.Message}");
                return false;
            }

            try
            {
                using (var ms = new MemoryStream(modelData))
                {
                    // 1. ModelProto (ルート) をフラットにパース
                    var elements = ParseElements(ms);

                    bool patched = false;
                    foreach (var elem in elements)
                    {
                        // ModelProto において、graph は field 7 (wire type 2)
                        if (elem.FieldNumber == 7 && elem.WireType == 2 && elem.RawBytes != null)
                        {
                            // 2. GraphProto をフラットにパース
                            using (var graphMs = new MemoryStream(elem.RawBytes))
                            {
                                elem.SubElements = ParseElements(graphMs);
                                elem.RawBytes = null;

                                foreach (var nodeElem in elem.SubElements)
                                {
                                    // GraphProto において、node は field 1 (wire type 2)
                                    if (nodeElem.FieldNumber == 1 && nodeElem.WireType == 2 && nodeElem.RawBytes != null)
                                    {
                                        // 3. NodeProto をフラットにパース (子は一切サブパースしない)
                                        using (var nodeMs = new MemoryStream(nodeElem.RawBytes))
                                        {
                                            nodeElem.SubElements = ParseElements(nodeMs);
                                            nodeElem.RawBytes = null;

                                            PbElement? opTypeElem = null;
                                            var inputElems = new List<PbElement>();

                                            foreach (var child in nodeElem.SubElements)
                                            {
                                                if (child.FieldNumber == 4 && child.WireType == 2) // op_type
                                                {
                                                    opTypeElem = child;
                                                }
                                                else if (child.FieldNumber == 1 && child.WireType == 2) // input
                                                {
                                                    inputElems.Add(child);
                                                }
                                            }

                                            if (opTypeElem != null && opTypeElem.RawBytes != null)
                                            {
                                                string opType = Encoding.ASCII.GetString(opTypeElem.RawBytes);
                                                if (opType == "GroupQueryAttention" && inputElems.Count == 11)
                                                {
                                                    // 最後の2つの入力を削除
                                                    var toRemove9 = inputElems[9];
                                                    var toRemove10 = inputElems[10];
                                                    nodeElem.SubElements.Remove(toRemove10);
                                                    nodeElem.SubElements.Remove(toRemove9);
                                                    patched = true;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (patched)
                    {
                        using (var outMs = new MemoryStream())
                        {
                            foreach (var elem in elements)
                            {
                                elem.WriteTo(outMs);
                            }
                            File.WriteAllBytes(filePath, outMs.ToArray());
                        }
                        Console.WriteLine($"C# GQA PATCH SUCCESS: {Path.GetFileName(filePath)}");
                        // パッチ済みマーカーを作成（次回以降のスキップ用）
                        try { File.WriteAllText(markerPath, "patched"); } catch { }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error patching ONNX file: {ex.Message}");
            }

            return false;
        }

        private static List<PbElement> ParseElements(Stream stream)
        {
            var list = new List<PbElement>();
            while (stream.Position < stream.Length)
            {
                long pos = stream.Position;
                uint tag;
                try
                {
                    tag = (uint)ReadVarint(stream);
                }
                catch (EndOfStreamException)
                {
                    break;
                }

                if (tag == 0) break;

                var elem = new PbElement { Tag = tag };
                switch (elem.WireType)
                {
                    case 0: // Varint
                        elem.VarintVal = ReadVarint(stream);
                        break;
                    case 1: // Fixed64
                        elem.Fixed64Val = ReadFixed64(stream);
                        break;
                    case 2: // Length-delimited
                        ulong len = ReadVarint(stream);
                        byte[] bytes = new byte[(int)len];
                        try
                        {
                            ReadExactly(stream, bytes, bytes.Length);
                        }
                        catch (EndOfStreamException)
                        {
                            Console.WriteLine($"[PARSE_ERROR] Field={elem.FieldNumber}, Wire=2, Len={len} at pos {pos}. Stream remaining={stream.Length - stream.Position}");
                            throw;
                        }
                        elem.RawBytes = bytes;
                        break;
                    case 5: // Fixed32
                        elem.Fixed32Val = ReadFixed32(stream);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported wire type: {elem.WireType} at pos {pos}");
                }
                list.Add(elem);
            }
            return list;
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException($"Unexpected end of stream. Required: {count}, Read: {offset}");
                offset += read;
            }
        }

        private static ulong ReadVarint(Stream stream)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1) throw new EndOfStreamException();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }

        private static ulong ReadFixed64(Stream stream)
        {
            byte[] bytes = new byte[8];
            ReadExactly(stream, bytes, bytes.Length);
            return BitConverter.ToUInt64(bytes, 0);
        }

        private static uint ReadFixed32(Stream stream)
        {
            byte[] bytes = new byte[4];
            ReadExactly(stream, bytes, bytes.Length);
            return BitConverter.ToUInt32(bytes, 0);
        }
    }
}
