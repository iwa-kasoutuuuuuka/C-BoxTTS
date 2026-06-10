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
            if (args.Contains("--test"))
            {
                try
                {
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

            Console.WriteLine("Starting Test Harness (Console Mode)...");
            File.WriteAllText("test_harness.log", $"[{DateTime.Now}] Starting Test Harness...{Environment.NewLine}");

            Console.WriteLine($"Loading MorphemeEngine from: {dicDir}");
            using var morph = new MorphemeEngine(baseDir);
            morph.Initialize();

            Console.WriteLine("Initializing TTSEngine...");
            using var engine = new TTSEngine(baseDir);
            await engine.EnsureModelExistsAsync(ModelType.Multilingual, (msg, pct) => {
                Console.WriteLine($"  [Download] {msg} ({pct:F1}%)");
            });

            Console.WriteLine($"Loading Tokenizer from: {modelsDir}");
            var tokenizer = new Tokenizer(Path.Combine(modelsDir, "tokenizer_mtl.json"));

            Console.WriteLine("Loading TTSEngine models...");
            engine.LoadModel(ModelType.Multilingual, (msg, pct) => {
                Console.WriteLine($"  [Load] {msg} ({pct:F1}%)");
                File.AppendAllText("test_harness.log", $"  [Load] {msg} ({pct:F1}%){Environment.NewLine}");
            });

            // ==================== 1. 日本語テスト ====================
            {
                string textJa = "せかいはかくのほのおにつつまれた。だがじんるいはぜつめつしてはいなかった。";
                Console.WriteLine($"[JA Test] Input: {textJa}");
                File.AppendAllText("test_harness.log", $"[JA Test] Input: {textJa}{Environment.NewLine}");

                long langTokenJa = 723;
                Console.WriteLine("[JA Test] Generating audio...");
                File.AppendAllText("test_harness.log", $"[JA Test] Generating audio...{Environment.NewLine}");
                float[] wavJa = await engine.GenerateBatchAsync(textJa, voicePath, 0.5f,
                    morph, tokenizer, langTokenJa, msg => Console.WriteLine($"  [JA Status] {msg}"));

                Console.WriteLine($"[JA Test] Generated! Samples: {wavJa.Length}");
                File.AppendAllText("test_harness.log", $"[JA Test] Generated! Samples: {wavJa.Length}{Environment.NewLine}");

                string outPathJa = Path.Combine(baseDir, "test_harness_japanese_out.wav");
                using (var audio = new AudioEngine())
                {
                    audio.SaveWav(wavJa, outPathJa);
                }
                Console.WriteLine($"[JA Test] Saved to: {outPathJa}");
                File.AppendAllText("test_harness.log", $"[JA Test] Saved to: {outPathJa}{Environment.NewLine}");
            }

            // ==================== 2. 英語テスト ====================
            {
                string textEn = "The quick brown fox jumps over the lazy dog. Voice synthesis is working beautifully.";
                Console.WriteLine($"[EN Test] Input: {textEn}");
                File.AppendAllText("test_harness.log", $"[EN Test] Input: {textEn}{Environment.NewLine}");

                long langTokenEn = 1007;
                Console.WriteLine("[EN Test] Generating audio...");
                File.AppendAllText("test_harness.log", $"[EN Test] Generating audio...{Environment.NewLine}");
                float[] wavEn = await engine.GenerateBatchAsync(textEn, voicePath, 0.5f,
                    morph, tokenizer, langTokenEn, msg => Console.WriteLine($"  [EN Status] {msg}"));

                Console.WriteLine($"[EN Test] Generated! Samples: {wavEn.Length}");
                File.AppendAllText("test_harness.log", $"[EN Test] Generated! Samples: {wavEn.Length}{Environment.NewLine}");

                string outPathEn = Path.Combine(baseDir, "test_harness_english_out.wav");
                using (var audio = new AudioEngine())
                {
                    audio.SaveWav(wavEn, outPathEn);
                }
                Console.WriteLine($"[EN Test] Saved to: {outPathEn}");
                File.AppendAllText("test_harness.log", $"[EN Test] Saved to: {outPathEn}{Environment.NewLine}");
            }
        }
    }
}
