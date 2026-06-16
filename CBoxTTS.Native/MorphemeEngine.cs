#if EN_BUILD
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CBoxTTS.Native
{
    public class MorphemeEngine : IDisposable
    {
        public MorphemeEngine(string baseDir)
        {
        }

        public Task EnsureDictionaryExistsAsync()
        {
            return Task.CompletedTask;
        }

        public void Initialize()
        {
        }

        public List<(string Surface, string Reading)> Analyze(string text)
        {
            // 英語版ビルドではMeCabによる形態素解析を行わないため、空のリストを返す
            return new List<(string Surface, string Reading)>();
        }

        public void Dispose()
        {
        }
    }
}
#else
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MeCab;

namespace CBoxTTS.Native
{
    public class MorphemeEngine : IDisposable
    {
        private MeCabTagger? _tagger;
        private readonly string _dicPath;
        private const string DicUrl = "https://github.com/shogo82148/mecab-ipadic-neologd/releases/download/v20200910/mecab-ipadic-neologd-20200910.tar.gz";

        public MorphemeEngine(string baseDir)
        {
            _dicPath = Path.Combine(baseDir, "dic");
        }

        public async Task EnsureDictionaryExistsAsync()
        {
            if (!Directory.Exists(_dicPath))
            {
                Console.WriteLine("辞書フォルダが見つかりません。作成します...");
                Directory.CreateDirectory(_dicPath);
            }
            await Task.CompletedTask;
        }

        public void Initialize()
        {
            if (_tagger != null) return;
            try
            {
                string path = _dicPath;
                if (!Directory.Exists(path))
                {
                    string exeDir = AppContext.BaseDirectory;
                    path = Path.Combine(exeDir, "dic");
                }

                if (!Directory.Exists(path) || !File.Exists(Path.Combine(path, "sys.dic")))
                {
                    throw new FileNotFoundException($"MeCab辞書が見つかりません。パス: {path}");
                }

                Log($"MeCab初期化開始: {path}");
                
                var param = new MeCabParam { DicDir = path };
                _tagger = MeCabTagger.Create(param);
                
                Log("MeCab初期化成功");
            }
            catch (Exception ex)
            {
                Log($"FATAL: MeCab初期化に失敗しました: {ex}");
                throw;
            }
        }

        private void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "debug.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        public List<(string Surface, string Reading)> Analyze(string text)
        {
            if (_tagger == null) throw new InvalidOperationException("MorphemeEngineが初期化されていません。");
            
            var nodes = _tagger.ParseToNodes(text);
            var result = new List<(string Surface, string Reading)>();
            
            foreach (var node in nodes)
            {
                if (node.Stat == MeCabNodeStat.Nor || node.Stat == MeCabNodeStat.Unk)
                {
                    var features = node.Feature.Split(',');
                    string reading = (features.Length > 7 && features[7] != "*") ? features[7] : node.Surface;
                    
                    reading = ToHiragana(reading);
                    result.Add((node.Surface, reading));
                }
            }
            return result;
        }

        private string ToHiragana(string katakana)
        {
            return string.Concat(katakana.Select(c => 
                (c >= '\u30A1' && c <= '\u30F6') ? (char)(c - 0x60) : c));
        }

        public void Dispose()
        {
            _tagger?.Dispose();
        }
    }
}
#endif
