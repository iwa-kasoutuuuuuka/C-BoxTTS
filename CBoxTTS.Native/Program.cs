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

            Console.WriteLine("Starting Extended Test Harness (Console Mode)...");
            File.WriteAllText("test_harness.log", $"[{DateTime.Now}] Starting Extended Test Harness...{Environment.NewLine}");

            Console.WriteLine($"Loading MorphemeEngine from: {dicDir}");
            using var morph = new MorphemeEngine(baseDir);
            morph.Initialize();

            Console.WriteLine("Initializing TTSEngine...");
            using var engine = new TTSEngine(baseDir);

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
                float[] wavJa = await engine.GenerateBatchAsync(textJa, voicePath, 0.5f,
                    morph, tokenizer, 723, msg => Console.WriteLine($"  [JA Status] {msg}"));
                
                string outPathJa = Path.Combine(baseDir, "test_harness_japanese_out.wav");
                using (var audio = new AudioEngine()) { audio.SaveWav(wavJa, outPathJa); }
                Console.WriteLine($"[JA Test] Saved to: {outPathJa}");

                // 英語
                string textEn = "It works beautifully on multilingual model!";
                Console.WriteLine($"[EN Test] Input: {textEn}");
                float[] wavEn = await engine.GenerateBatchAsync(textEn, voicePath, 0.5f,
                    morph, tokenizer, 708, msg => Console.WriteLine($"  [EN Status] {msg}"));

                string outPathEn = Path.Combine(baseDir, "test_harness_english_out.wav");
                using (var audio = new AudioEngine()) { audio.SaveWav(wavEn, outPathEn); }
                Console.WriteLine($"[EN Test] Saved to: {outPathEn}");
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
                float[] wavJa = await engine.GenerateBatchAsync(textJa, voicePath, 0.5f,
                    morph, tokenizer, 723, msg => Console.WriteLine($"  [Turbo JA Status] {msg}"));

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
                float[] wavEn = await engine.GenerateBatchAsync(textEn, voicePath, 0.5f,
                    morph, tokenizer, 1, msg => Console.WriteLine($"  [English EN Status] {msg}"));

                string outPathEn = Path.Combine(baseDir, "test_harness_english_exclusive_out.wav");
                using (var audio = new AudioEngine()) { audio.SaveWav(wavEn, outPathEn); }
                Console.WriteLine($"[English EN Test] Saved to: {outPathEn}");
            }

            Console.WriteLine("\n=== Extended Test Harness Finished Successfully ===");
            File.AppendAllText("test_harness.log", $"{Environment.NewLine}=== Finished Successfully ==={Environment.NewLine}");
        }
    }
}
