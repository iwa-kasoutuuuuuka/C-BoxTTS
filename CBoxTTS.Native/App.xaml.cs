using System;
using System.Windows;

namespace CBoxTTS.Native
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string baseDir = AppContext.BaseDirectory;
            string[] cmdArgs = Environment.GetCommandLineArgs();
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(baseDir, "app_start.log"), $"[{DateTime.Now}] App.OnStartup triggered. Args: {string.Join(" ", cmdArgs)}\r\n"); } catch { }

            if (cmdArgs.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase)) || (e.Args != null && e.Args.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase))))
            {
                RunTestAndExit();
                return;
            }

            // Normal Startup: Programmatically instantiate and show MainWindow
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private async void RunTestAndExit()
        {
            string baseDir = AppContext.BaseDirectory;
            string logPath = System.IO.Path.Combine(baseDir, "test_result.log");
            Action<string> log = msg =>
            {
                Console.WriteLine(msg);
                try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n"); } catch { }
            };

            try { System.IO.File.WriteAllText(logPath, "=== Running C-BoxTTS EN CLI Test Runner ===\r\n"); } catch { }

            try
            {
                string jsonPath = System.IO.Path.Combine(baseDir, "models", "english", "tokenizer.json");
                var tokenizer = new Tokenizer(jsonPath);
                string text = "Hello world! This is an automated test for clear native American English speech.";
                string normalized = EnglishNormalizer.Normalize(text);
                long[] tokens = tokenizer.Encode(normalized, 1);
                log($"[1/4] Tokenizer OK! Tokens: {tokens.Length}, Starts with {tokens[0]}");

                var engine = new TTSEngine(baseDir);
                var morph = new MorphemeEngine(baseDir);
                log("[2/4] Loading TTSEngine Model...");
                engine.LoadModel(ModelType.English, (msg, pct) => log($"Load Progress: [{pct:F0}%] {msg}"));
                log($"[2/4] Model Loaded! Active Backend: {engine.ActiveBackend}");

                string voicePath = System.IO.Path.Combine(baseDir, "models", "default_voice_en.wav");
                log($"[3/4] Generating speech with prompt: {voicePath}");
                var samples = await engine.GenerateBatchAsync(text, voicePath, 0.30f, 0.50f, morph, tokenizer, 1, 0.40f, 1.35f, msg => log($"Gen: {msg}"));
                log($"[3/4] Generated {samples.Length} samples ({samples.Length / 24000.0:F2} seconds)");

                var audio = new AudioEngine();
                string outWav = System.IO.Path.Combine(baseDir, "verify_en_cuda_out.wav");
                audio.SaveWav(samples, outWav, 1.0f);
                log($"[3/4] Saved WAV: {outWav}");

                string outSlow80 = System.IO.Path.Combine(baseDir, "verify_en_cuda_slow80.wav");
                audio.SaveWav(samples, outSlow80, 0.8f);
                log($"[4/4] WSOLA Slow 0.8x WAV Saved: {outSlow80}");

                log("=== ALL CLI VERIFICATION TESTS PASSED SUCCESSFULLY ===");
            }
            catch (Exception ex)
            {
                log($"FATAL TEST ERROR: {ex}");
            }
            finally
            {
                Shutdown(0);
            }
        }
    }
}
