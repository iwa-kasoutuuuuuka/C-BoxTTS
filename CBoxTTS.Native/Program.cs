using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace CBoxTTS.Native
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var cmdArgs = Environment.GetCommandLineArgs();
            try { File.WriteAllText("startup.log", $"[{DateTime.Now}] Main called. Args: {string.Join(", ", cmdArgs)}\r\n"); } catch { }

            if (cmdArgs.Any(a => a.Equals("--verify-single", StringComparison.OrdinalIgnoreCase)) ||
                (args != null && args.Any(a => a.Equals("--verify-single", StringComparison.OrdinalIgnoreCase))))
            {
                try
                {
                    File.AppendAllText("startup.log", "Running Single Sentence Verification...\r\n");
                    RunVerifySingleSentence().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FATAL VERIFY ERROR: {ex}");
                    File.WriteAllText("verify_error.log", ex.ToString());
                    Environment.Exit(1);
                }
                Environment.Exit(0);
            }
            else if (cmdArgs.Any(a => a.Equals("--batch-folder", StringComparison.OrdinalIgnoreCase) || a.Equals("--batch", StringComparison.OrdinalIgnoreCase)) ||
                (args != null && args.Any(a => a.Equals("--batch-folder", StringComparison.OrdinalIgnoreCase) || a.Equals("--batch", StringComparison.OrdinalIgnoreCase))))
            {
                try
                {
                    File.AppendAllText("startup.log", "Running Batch Folder Pipeline...\r\n");
                    string targetFolder = @"C:\Users\Gisa_M3\Desktop\異常検知Eng音声";
                    string voicePath = @"C:\Users\Gisa_M3\Desktop\異常検知Eng音声\男性4Sample.wav";
                    float talkSpeed = 0.95f;

                    for (int i = 0; i < cmdArgs.Length; i++)
                    {
                        if ((cmdArgs[i].Equals("--batch-folder", StringComparison.OrdinalIgnoreCase) || cmdArgs[i].Equals("--batch", StringComparison.OrdinalIgnoreCase)) && i + 1 < cmdArgs.Length)
                            targetFolder = cmdArgs[i + 1];
                        if (cmdArgs[i].Equals("--voice", StringComparison.OrdinalIgnoreCase) && i + 1 < cmdArgs.Length)
                            voicePath = cmdArgs[i + 1];
                        if (cmdArgs[i].Equals("--speed", StringComparison.OrdinalIgnoreCase) && i + 1 < cmdArgs.Length && float.TryParse(cmdArgs[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float spd))
                            talkSpeed = spd;
                    }

                    RunBatchFolderPipeline(targetFolder, voicePath, talkSpeed).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FATAL BATCH ERROR: {ex}");
                    File.WriteAllText("batch_error.log", ex.ToString());
                    Environment.Exit(1);
                }
                Environment.Exit(0);
            }
            else if (cmdArgs.Any(a => a.Equals("--auto-debug", StringComparison.OrdinalIgnoreCase) || a.Equals("--verify-samples", StringComparison.OrdinalIgnoreCase)) ||
                (args != null && args.Any(a => a.Equals("--auto-debug", StringComparison.OrdinalIgnoreCase) || a.Equals("--verify-samples", StringComparison.OrdinalIgnoreCase))))
            {
                try
                {
                    File.AppendAllText("startup.log", "Running Automated Debug Pipeline...\r\n");
                    RunAutoDebugPipeline().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FATAL AUTO-DEBUG ERROR: {ex}");
                    File.WriteAllText("auto_debug_error.log", ex.ToString());
                    Environment.Exit(1);
                }
                Environment.Exit(0);
            }
            else if (cmdArgs.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase)) || (args != null && args.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase))))
            {
                try
                {
                    File.AppendAllText("startup.log", "Running Test Harness...\r\n");
                    RunTestHarness().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FATAL ERROR: {ex}");
                    File.WriteAllText("test_harness_error.log", ex.ToString());
                    Environment.Exit(1);
                }
                Environment.Exit(0);
            }
            else
            {
                // 通常起動: WPF アプリケーションを起動
                var app = new App();
                app.InitializeComponent(); // App.xaml からリソース等をロード
                app.Run();
            }
        }

        private static async Task RunTestHarness()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string modelsDir = Path.Combine(baseDir, "models");
            string dicDir = Path.Combine(baseDir, "dic");
            string voicePath = Path.Combine(modelsDir, "default_voice.wav");

            Console.WriteLine("Starting Extended Test Harness (Console Mode)...");
            File.WriteAllText("test_harness.log", $"[{DateTime.Now}] Starting Extended Test Harness...{Environment.NewLine}");

            Console.WriteLine("\n--- Verifying EnglishNormalizer ---");
            string[] testTexts = {
                "NASA",
                "FBI",
                "12",
                "3:00 pm",
                "Mr. Smith",
                "This is 12.34% of the total.",
                "IT IS A WONDERFUL DAY.",
                "The target is 100% correct.",
                "I have 12 apples, Mr. Smith. NASA works with FBI.",
                // PC/FA業界専門用語テスト
                "The CPU utilizes GPU acceleration via PCIe interface.",
                "The PLC controls the SCADA system through PROFINET.",
                "OMRON's HMI connects to the VFD via MODBUS.",
                "USB, HDMI, and SSD are common PC components.",
                // 英語短縮形テスト
                "I don't know what it's about.",
                "We're going to the API endpoint.",
                // AI 表記揺れテスト
                "AI models",
                "ai models",
                "Ai models",
                "A.I. models",
                // 時刻パターン保護テスト
                "The meeting is at 3:00 pm and ends at 5:30 pm."
            };
            foreach (var t in testTexts)
            {
                string norm = EnglishNormalizer.Normalize(t);
                Console.WriteLine($"  Input:  \"{t}\"");
                Console.WriteLine($"  Output: \"{norm}\"");
                File.AppendAllText("test_harness.log", $"  Input:  \"{t}\"{Environment.NewLine}  Output: \"{norm}\"{Environment.NewLine}");
            }
            Console.WriteLine("------------------------------------\n");

#if EN_BUILD
            Console.WriteLine("English Build Mode: Skipping MorphemeEngine (MeCab) initialization.");
            using var morph = new MorphemeEngine(baseDir); // ダミーインスタンス
#else
            Console.WriteLine($"Loading MorphemeEngine from: {dicDir}");
            using var morph = new MorphemeEngine(baseDir);
            morph.Initialize();
#endif

            Console.WriteLine("Initializing TTSEngine...");
            using var engine = new TTSEngine(baseDir);

#if EN_BUILD
            // Multilingual model test skipped in English-only build mode

            // ==================== 3. English モデルテスト (英語専用) ====================
            {
                Console.WriteLine("\n--- 3. Testing English Model ---");
                File.AppendAllText("test_harness.log", $"{Environment.NewLine}--- 3. Testing English Model ---{Environment.NewLine}");

                await engine.EnsureModelExistsAsync(ModelType.English, (msg, pct) => {
                    Console.WriteLine($"  [Download] {msg} ({pct:F1}%)");
                });

                var tokenizer = new Tokenizer(Path.Combine(modelsDir, "english", "tokenizer.json"));

                Console.WriteLine("Loading English models...");
                engine.LoadModel(ModelType.English, (msg, pct) => {
                    Console.WriteLine($"  [Load] {msg} ({pct:F1}%)");
                });





                string textEn = @"In this LINE, the inspector makes a judgment using images for monitoring.
This system uses the multi-line random triggering function of FH.
By setting up the imaging station and the monitor inspection station on
separate lines using a multi-line random trigger function,
imaging and monitor inspection can be processed in parallel.
This prevents the entire system from shutting down due to waiting for
inspector's judgment,
allowing processing to proceed efficiently and asynchronously.
Next is explanation about monitoring process between LINE0 and LINE3.
1, Images taken on the imaging line are processed for monitor inspection and
saved to the RAM disk of the FH controller.
2,,The monitor inspection line reads the image from the RAMDISK memory and
displays the image on the monitor.
3,,, Next, the inspector looks at the image on the monitor and makes a
judgment of OK or NG. If there are any defects,
you can also enter details such as ""scratches"" or ""burrs.""
4,Finally, the captured images and inspection results are saved to external
storage.";
                Console.WriteLine($"[English EN Test] Input: {textEn}");
                float[] wavEn = await engine.GenerateBatchAsync(textEn, voicePath, 0.5f, 0.5f,
                    morph, tokenizer, 1, 0.5f, 1.1f, msg => Console.WriteLine($"  [English EN Status] {msg}"));

                string outPathEn = Path.Combine(baseDir, "test_harness_english_exclusive_out.wav");
                using (var audio = new AudioEngine()) { audio.SaveWav(wavEn, outPathEn); }
                Console.WriteLine($"[English EN Test] Saved to: {outPathEn}");
            }
#elif JA_BUILD
            // ==================== 1. Multilingual モデルテスト (日本語のみ) ====================
            {
                Console.WriteLine("\n--- 1. Testing Multilingual Model (Japanese) ---");
                File.AppendAllText("test_harness.log", $"{Environment.NewLine}--- 1. Testing Multilingual Model (Japanese) ---{Environment.NewLine}");

                await engine.EnsureModelExistsAsync(ModelType.Multilingual, (msg, pct) => {
                    Console.WriteLine($"  [Download] {msg} ({pct:F1}%)");
                });

                var tokenizer = new Tokenizer(Path.Combine(modelsDir, "multilingual", "tokenizer.json"));

                Console.WriteLine("Loading Multilingual models...");
                engine.LoadModel(ModelType.Multilingual, (msg, pct) => {
                    Console.WriteLine($"  [Load] {msg} ({pct:F1}%)");
                });

                // 日本語
                string textJa = "せかいはかくのほのおにつつまれた。だがじんるいはぜつめつしてはいなかった。";
                Console.WriteLine($"[JA Test] Input: {textJa}");
                float[] wavJa = await engine.GenerateBatchAsync(textJa, voicePath, 0.5f, 0.7f,
                    morph, tokenizer, 723, 0.5f, 1.1f, msg => Console.WriteLine($"  [JA Status] {msg}"));
                
                string outPathJa = Path.Combine(baseDir, "test_harness_japanese_out.wav");
                using (var audio = new AudioEngine()) { audio.SaveWav(wavJa, outPathJa); }
                Console.WriteLine($"[JA Test] Saved to: {outPathJa}");
            }

            // ==================== 2. Turbo モデルテスト (日本語専用) ====================
            {
                Console.WriteLine("\n--- 2. Testing Turbo Model ---");
                File.AppendAllText("test_harness.log", $"{Environment.NewLine}--- 2. Testing Turbo Model ---{Environment.NewLine}");

                await engine.EnsureModelExistsAsync(ModelType.Turbo, (msg, pct) => {
                    Console.WriteLine($"  [Download] {msg} ({pct:F1}%)");
                });

                var tokenizer = new Tokenizer(Path.Combine(modelsDir, "turbo", "tokenizer.json"));

                Console.WriteLine("Loading Turbo models...");
                engine.LoadModel(ModelType.Turbo, (msg, pct) => {
                    Console.WriteLine($"  [Load] {msg} ({pct:F1}%)");
                });

                string textJa = "ターボモデルのテストです。素早く合成が行われます。";
                Console.WriteLine($"[Turbo JA Test] Input: {textJa}");
                float[] wavJa = await engine.GenerateBatchAsync(textJa, voicePath, 0.5f, 0.6f,
                    morph, tokenizer, 723, 0.5f, 1.15f, msg => Console.WriteLine($"  [Turbo JA Status] {msg}"));

                string outPathJa = Path.Combine(baseDir, "test_harness_turbo_japanese_out.wav");
                using (var audio = new AudioEngine()) { audio.SaveWav(wavJa, outPathJa); }
                Console.WriteLine($"[Turbo JA Test] Saved to: {outPathJa}");
            }
#else
            // ==================== 1. Multilingual モデルテスト (日本語 & 英語) ====================
            {
                Console.WriteLine("\n--- 1. Testing Multilingual Model ---");
                File.AppendAllText("test_harness.log", $"{Environment.NewLine}--- 1. Testing Multilingual Model ---{Environment.NewLine}");

                await engine.EnsureModelExistsAsync(ModelType.Multilingual, (msg, pct) => {
                    Console.WriteLine($"  [Download] {msg} ({pct:F1}%)");
                });

                var tokenizer = new Tokenizer(Path.Combine(modelsDir, "multilingual", "tokenizer.json"));

                Console.WriteLine("Loading Multilingual models...");
                engine.LoadModel(ModelType.Multilingual, (msg, pct) => {
                    Console.WriteLine($"  [Load] {msg} ({pct:F1}%)");
                });

                // 日本語
                string textJa = "せかいはかくのほのおにつつまれた。だがじんるいはぜつめつしてはいなかった。";
                Console.WriteLine($"[JA Test] Input: {textJa}");
                float[] wavJa = await engine.GenerateBatchAsync(textJa, voicePath, 0.5f, 0.7f,
                    morph, tokenizer, 723, 0.5f, 1.1f, msg => Console.WriteLine($"  [JA Status] {msg}"));
                
                string outPathJa = Path.Combine(baseDir, "test_harness_japanese_out.wav");
                using (var audio = new AudioEngine()) { audio.SaveWav(wavJa, outPathJa); }
                Console.WriteLine($"[JA Test] Saved to: {outPathJa}");

                // 英語
                string textEn = "It works beautifully on multilingual model!";
                Console.WriteLine($"[EN Test] Input: {textEn}");
                float[] wavEn = await engine.GenerateBatchAsync(textEn, voicePath, 0.5f, 0.7f,
                    morph, tokenizer, 708, 0.5f, 1.1f, msg => Console.WriteLine($"  [EN Status] {msg}"));

                string outPathEn = Path.Combine(baseDir, "test_harness_english_out.wav");
                using (var audio = new AudioEngine()) { audio.SaveWav(wavEn, outPathEn); }
                Console.WriteLine($"[EN Test] Saved to: {outPathEn}");

                // 英語 (ユーザー報告の再現/検証用)
                string textOmron = "OMRON Corporation is a leading global electronics and automation company based in Japan";
                Console.WriteLine($"[OMRON Test] Input: {textOmron}");
                float[] wavOmron = await engine.GenerateBatchAsync(textOmron, voicePath, 0.5f, 0.7f,
                    morph, tokenizer, 708, 0.5f, 1.1f, msg => Console.WriteLine($"  [OMRON Status] {msg}"));

                string outPathOmron = Path.Combine(baseDir, "test_harness_omron_out.wav");
                using (var audio = new AudioEngine()) { audio.SaveWav(wavOmron, outPathOmron); }
                Console.WriteLine($"[OMRON Test] Saved to: {outPathOmron}");
            }

            // ==================== 2. Turbo モデルテスト (日本語専用) ====================
            {
                Console.WriteLine("\n--- 2. Testing Turbo Model ---");
                File.AppendAllText("test_harness.log", $"{Environment.NewLine}--- 2. Testing Turbo Model ---{Environment.NewLine}");

                await engine.EnsureModelExistsAsync(ModelType.Turbo, (msg, pct) => {
                    Console.WriteLine($"  [Download] {msg} ({pct:F1}%)");
                });

                var tokenizer = new Tokenizer(Path.Combine(modelsDir, "turbo", "tokenizer.json"));

                Console.WriteLine("Loading Turbo models...");
                engine.LoadModel(ModelType.Turbo, (msg, pct) => {
                    Console.WriteLine($"  [Load] {msg} ({pct:F1}%)");
                });

                string textJa = "ターボモデルのテストです。素早く合成が行われます。";
                Console.WriteLine($"[Turbo JA Test] Input: {textJa}");
                float[] wavJa = await engine.GenerateBatchAsync(textJa, voicePath, 0.5f, 0.6f,
                    morph, tokenizer, 723, 0.5f, 1.15f, msg => Console.WriteLine($"  [Turbo JA Status] {msg}"));

                string outPathJa = Path.Combine(baseDir, "test_harness_turbo_japanese_out.wav");
                using (var audio = new AudioEngine()) { audio.SaveWav(wavJa, outPathJa); }
                Console.WriteLine($"[Turbo JA Test] Saved to: {outPathJa}");
            }

            // ==================== 3. English モデルテスト (英語専用) ====================
            {
                Console.WriteLine("\n--- 3. Testing English Model ---");
                File.AppendAllText("test_harness.log", $"{Environment.NewLine}--- 3. Testing English Model ---{Environment.NewLine}");

                await engine.EnsureModelExistsAsync(ModelType.English, (msg, pct) => {
                    Console.WriteLine($"  [Download] {msg} ({pct:F1}%)");
                });

                var tokenizer = new Tokenizer(Path.Combine(modelsDir, "english", "tokenizer.json"));

                Console.WriteLine("Loading English models...");
                engine.LoadModel(ModelType.English, (msg, pct) => {
                    Console.WriteLine($"  [Load] {msg} ({pct:F1}%)");
                });

                string textEn = "This is a test of the English exclusive model. Hello world!";
                Console.WriteLine($"[English EN Test] Input: {textEn}");
                float[] wavEn = await engine.GenerateBatchAsync(textEn, voicePath, 0.5f, 0.8f,
                    morph, tokenizer, 1, 0.5f, 1.1f, msg => Console.WriteLine($"  [English EN Status] {msg}"));

                string outPathEn = Path.Combine(baseDir, "test_harness_english_exclusive_out.wav");
                using (var audio = new AudioEngine()) { audio.SaveWav(wavEn, outPathEn); }
                Console.WriteLine($"[English EN Test] Saved to: {outPathEn}");
            }
#endif

            Console.WriteLine("\n=== Extended Test Harness Finished Successfully ===");
            File.AppendAllText("test_harness.log", $"{Environment.NewLine}=== Finished Successfully ==={Environment.NewLine}");
        }

        private static async Task RunAutoDebugPipeline()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string modelsDir = Path.Combine(baseDir, "models");
            string voicePath = Path.Combine(modelsDir, "default_voice.wav");
            string customVoice = @"C:\Users\Gisa_M3\Desktop\異常検知Eng音声\男性4Sample.wav";
            if (File.Exists(customVoice))
            {
                voicePath = customVoice;
                Console.WriteLine($"Using Custom Voice Prompt:\n  {voicePath}\n");
            }
            string reportPath = Path.Combine(baseDir, "auto_debug_report.log");

            Console.WriteLine("=================================================");
            Console.WriteLine("  CBoxTTS Automated Debugging & STT Pipeline     ");
            Console.WriteLine("=================================================\n");

            // 1. サンプル文章ファイルの探索・読込
            string sampleFile = Path.Combine(baseDir, "sample_sentences_en.txt");
            var currentDir = new DirectoryInfo(baseDir);
            int depth = 5;
            while (!File.Exists(sampleFile) && currentDir != null && depth > 0)
            {
                currentDir = currentDir.Parent;
                if (currentDir != null)
                {
                    sampleFile = Path.Combine(currentDir.FullName, "sample_sentences_en.txt");
                }
                depth--;
            }

            if (!File.Exists(sampleFile))
            {
                Console.WriteLine($"Error: Sample sentence file not found at {sampleFile}");
                return;
            }

            var sentences = File.ReadAllLines(sampleFile, System.Text.Encoding.UTF8)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .ToList();

            Console.WriteLine($"Loaded {sentences.Count} sample sentence(s) from:\n  {sampleFile}\n");

            // 2. Whisper STT モデル準備
            using var verifier = new WhisperVerifier(baseDir);
            await verifier.EnsureModelExistsAsync((msg, pct) => {
                Console.WriteLine($"  [Whisper Model] {msg} ({pct:F0}%)");
            });
            verifier.LoadModel();

            // 3. CBoxTTS エンジン準備
            using var engine = new TTSEngine(baseDir);
            await engine.EnsureModelExistsAsync(ModelType.English, (msg, pct) => {
                Console.WriteLine($"  [TTS Model Download] {msg} ({pct:F0}%)");
            });

            Console.WriteLine("Loading English TTS models...");
            engine.LoadModel(ModelType.English, (msg, pct) => {
                Console.WriteLine($"  [TTS Load] {msg} ({pct:F0}%)");
            });

            var tokenizer = new Tokenizer(Path.Combine(modelsDir, "english", "tokenizer.json"));
            using var morph = new MorphemeEngine(baseDir);

            // 4. 自動検証の実行
            var reportLines = new System.Collections.Generic.List<string>();
            reportLines.Add($"=== CBoxTTS Automated Debugging Report [{DateTime.Now}] ===");
            reportLines.Add($"Sample File: {sampleFile}");
            reportLines.Add($"Total Sentences: {sentences.Count}\n");

            double totalMatch = 0.0;
            int itemIndex = 1;

            foreach (var sentence in sentences)
            {
                Console.WriteLine($"-------------------------------------------------");
                Console.WriteLine($"[Sample {itemIndex}/{sentences.Count}]");
                Console.WriteLine($"  Original:   \"{sentence}\"");

                string norm = EnglishNormalizer.Normalize(sentence);
                Console.WriteLine($"  Normalized: \"{norm}\"");

                string wavOutPath = Path.Combine(baseDir, $"auto_debug_sample_{itemIndex}.wav");
                Console.WriteLine("  Synthesizing audio...");
                float[] wav = await engine.GenerateBatchAsync(norm, voicePath, 0.50f, 0.35f, morph, tokenizer, 1, 0.5f, 1.20f, status => { });

                using (var audio = new AudioEngine())
                {
                    audio.SaveWav(wav, wavOutPath);
                }

                Console.WriteLine("  Transcribing & verifying with Whisper STT...");
                var res = verifier.VerifySynthesis(sentence, norm, wav, wavOutPath);

                totalMatch += res.MatchPercentage;

                Console.WriteLine($"  STT Result: \"{res.TranscribedText}\"");
                Console.WriteLine($"  Match Rate: {res.MatchPercentage:F1}% (Audio: {res.AudioDurationSeconds:F2}s)");

                if (res.MissingWords.Count > 0)
                {
                    Console.WriteLine($"  [Missing Words]: {string.Join(", ", res.MissingWords)}");
                }
                if (res.ExtraWords.Count > 0)
                {
                    Console.WriteLine($"  [Extra Words]:   {string.Join(", ", res.ExtraWords)}");
                }
                if (res.SubstitutedWords.Count > 0)
                {
                    Console.WriteLine($"  [Substitutions]: {string.Join("; ", res.SubstitutedWords)}");
                }

                // ログの構築
                reportLines.Add($"Sample {itemIndex}:");
                reportLines.Add($"  Original:      {res.OriginalText}");
                reportLines.Add($"  Normalized:    {res.NormalizedText}");
                reportLines.Add($"  Transcribed:   {res.TranscribedText}");
                reportLines.Add($"  Match Rate:    {res.MatchPercentage:F1}%");
                reportLines.Add($"  Audio File:    {res.AudioWavPath} ({res.AudioDurationSeconds:F2}s)");
                if (res.MissingWords.Count > 0) reportLines.Add($"  Missing Words: {string.Join(", ", res.MissingWords)}");
                if (res.ExtraWords.Count > 0) reportLines.Add($"  Extra Words:   {string.Join(", ", res.ExtraWords)}");
                if (res.SubstitutedWords.Count > 0) reportLines.Add($"  Substitutions: {string.Join("; ", res.SubstitutedWords)}");
                reportLines.Add("");

                itemIndex++;
            }

            double avgMatch = sentences.Count > 0 ? totalMatch / sentences.Count : 0.0;
            Console.WriteLine($"=================================================");
            Console.WriteLine($"  Summary: Tested {sentences.Count} sentence(s), Avg Match Rate: {avgMatch:F1}%");
            Console.WriteLine($"  Report saved to: {reportPath}");
            Console.WriteLine($"=================================================");

            reportLines.Add($"Summary: Average Match Rate = {avgMatch:F1}% across {sentences.Count} sentence(s).");
            File.WriteAllLines(reportPath, reportLines, System.Text.Encoding.UTF8);
        }

        private static async Task RunBatchFolderPipeline(string rootFolder, string voicePath, float speed)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string modelsDir = Path.Combine(baseDir, "models");

            Console.WriteLine($"=== CBoxTTS Batch Folder Processing ===");
            Console.WriteLine($"Target Folder: {rootFolder}");
            Console.WriteLine($"Voice Prompt:  {voicePath}");
            Console.WriteLine($"Speech Speed:  {speed:F2}");

            if (!Directory.Exists(rootFolder))
            {
                throw new DirectoryNotFoundException($"Target folder not found: {rootFolder}");
            }
            if (!File.Exists(voicePath))
            {
                throw new FileNotFoundException($"Voice prompt not found: {voicePath}");
            }

            using var engine = new TTSEngine(baseDir);
            await engine.EnsureModelExistsAsync(ModelType.English, (msg, pct) => {
                Console.WriteLine($"  [TTS Model Download] {msg} ({pct:F0}%)");
            });

            Console.WriteLine("Loading English TTS models...");
            engine.LoadModel(ModelType.English, (msg, pct) => {
                Console.WriteLine($"  [TTS Load] {msg} ({pct:F0}%)");
            });

            var tokenizer = new Tokenizer(Path.Combine(modelsDir, "english", "tokenizer.json"));
            using var morph = new MorphemeEngine(baseDir);

            var pageDirs = Directory.GetDirectories(rootFolder)
                                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                                    .ToList();

            Console.WriteLine($"Found {pageDirs.Count} subfolder(s) to process.");
            int processedCount = 0;

            using (var audio = new AudioEngine())
            {
                foreach (var dir in pageDirs)
                {
                    string folderName = Path.GetFileName(dir);
                    var txtFiles = Directory.GetFiles(dir, "*.txt");
                    if (txtFiles.Length == 0)
                    {
                        Console.WriteLine($"[SKIP] {folderName}: No .txt file found.");
                        continue;
                    }

                    string txtPath = txtFiles[0];
                    string rawText = File.ReadAllText(txtPath).Trim();
                    if (string.IsNullOrWhiteSpace(rawText))
                    {
                        Console.WriteLine($"[SKIP] {folderName}: Empty text file.");
                        continue;
                    }

                    processedCount++;
                    Console.WriteLine($"\n-------------------------------------------------");
                    Console.WriteLine($"[{processedCount}/{pageDirs.Count}] Processing Folder: {folderName}");
                    Console.WriteLine($"  Text File: {Path.GetFileName(txtPath)}");
                    Console.WriteLine($"  Content:   \"{rawText}\"");

                    string norm = EnglishNormalizer.Normalize(rawText);
                    Console.WriteLine($"  Normalized: \"{norm}\"");

                    string outWavPath = Path.Combine(dir, $"{folderName}.wav");
                    Console.WriteLine($"  Synthesizing audio...");

                    float[] wav = await engine.GenerateBatchAsync(norm, voicePath, 0.50f, 0.35f, morph, tokenizer, 1, 0.5f, 1.20f, status => { });

                    audio.SaveWav(wav, outWavPath, speed);
                    float durationSec = (wav.Length / 24000f) / speed;
                    Console.WriteLine($"  [DONE] Saved: {outWavPath} (Duration: {durationSec:F2}s, Speed: {speed:F2})");
                }
            }

            Console.WriteLine($"\n=================================================");
            Console.WriteLine($"  Successfully processed {processedCount} folder(s).");
            Console.WriteLine($"=================================================");
        }

        private static async Task RunVerifySingleSentence()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string modelsDir = Path.Combine(baseDir, "models");
            string voicePath = @"C:\Users\Gisa_M3\Desktop\異常検知Eng音声\男性4Sample.wav";
            float speed = 0.95f;

            string targetSentence = "In this section, we will first explain the AI processing items that can be used with DeVIEW, and then provide an overview of \"Anormaly detection and Segmentation AI,\" which is the main topic of this video.";

            Console.WriteLine("=================================================");
            Console.WriteLine("=== SINGLE SENTENCE VERIFICATION & TRANSCRIPTION ===");
            Console.WriteLine($"Original Text:\n  \"{targetSentence}\"\n");

            // 1. Whisper STT
            using var verifier = new WhisperVerifier(baseDir);
            await verifier.EnsureModelExistsAsync((msg, pct) => { });
            verifier.LoadModel();

            // 2. CBoxTTS
            using var engine = new TTSEngine(baseDir);
            await engine.EnsureModelExistsAsync(ModelType.English, (msg, pct) => { });
            engine.LoadModel(ModelType.English, (msg, pct) => { });

            var tokenizer = new Tokenizer(Path.Combine(modelsDir, "english", "tokenizer.json"));
            using var morph = new MorphemeEngine(baseDir);
            using var audio = new AudioEngine();

            string norm = EnglishNormalizer.Normalize(targetSentence);
            Console.WriteLine($"Normalized Text:\n  \"{norm}\"\n");

            Console.WriteLine("Synthesizing audio...");
            float[] wav = await engine.GenerateBatchAsync(norm, voicePath, 0.50f, 0.35f, morph, tokenizer, 1, 0.5f, 1.20f, status => { });
            string wavPath = Path.Combine(baseDir, "single_sentence_verification.wav");
            audio.SaveWav(wav, wavPath, speed);

            var res = verifier.VerifySynthesis(targetSentence, norm, wav, wavPath);
            Console.WriteLine("\n=== WHISPER STT VERIFICATION RESULT ===");
            Console.WriteLine($"Original:    \"{res.OriginalText}\"");
            Console.WriteLine($"Normalized:  \"{res.NormalizedText}\"");
            Console.WriteLine($"Transcribed: \"{res.TranscribedText}\"");
            Console.WriteLine($"Match Rate:  {res.MatchPercentage:F1}% (Audio: {res.AudioDurationSeconds:F2}s)");
            Console.WriteLine("=================================================");
        }
    }
}
