using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CBoxTTS.Native;

class Program
{
    static async Task Main(string[] args)
    {
        string modelsDir = @"e:\app\C-BoxTTS-C\CBoxTTS.Native\Release_Portable\models";
        string dicDir = @"e:\app\C-BoxTTS-C\CBoxTTS.Native\Release_Portable\dic";
        string voicePath = Path.Combine(modelsDir, "default_voice.wav");

        Console.WriteLine("Initializing MorphemeEngine...");
        using var morph = new MorphemeEngine(dicDir);
        morph.Initialize();

        Console.WriteLine("Initializing Tokenizer...");
        var tokenizer = new Tokenizer(Path.Combine(modelsDir, "tokenizer_mtl.json"));

        Console.WriteLine("Initializing TTSEngine...");
        using var engine = new TTSEngine(Path.GetDirectoryName(modelsDir) ?? "");
        engine.LoadModel(ModelType.Multilingual, (msg, pct) => {
            Console.WriteLine($"  {msg} ({pct:F1}%)");
        });

        // 英文での動作検証
        string text = "The quick brown fox jumps over the lazy dog.";
        Console.WriteLine($"Generating audio for: {text}");

        long langToken = 1007; // 英語トークン
        float[] wav = await engine.GenerateBatchAsync(text, voicePath, 0.5f,
            morph, tokenizer, langToken, msg => Console.WriteLine($"  [Status] {msg}"));

        Console.WriteLine($"Waveform generated! Samples: {wav.Length}");
        
        string outPath = "test_english_out.wav";
        using (var audio = new AudioEngine())
        {
            audio.SaveWav(wav, outPath);
        }
        Console.WriteLine($"Saved C# wav to: {outPath}");
    }
}
