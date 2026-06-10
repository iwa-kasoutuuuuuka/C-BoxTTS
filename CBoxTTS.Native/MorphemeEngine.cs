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
        private const string DicUrl = "https://github.com/shogo82148/mecab-ipadic-neologd/releases/download/v20200910/mecab-ipadic-neologd-20200910.tar.gz"; // 代替案が必要な場合あり

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
                
                // 標準的な初期化方式に戻す（SingleFile解除により動作可能）
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
                // BOS/EOS (文頭・文末の仮想ノード) を除外
                if (node.Stat == MeCabNodeStat.Nor || node.Stat == MeCabNodeStat.Unk)
                {
                    // IPADICのFeature形式: 品詞,品詞細分類1,...,読み,発音
                    var features = node.Feature.Split(',');
                    // 読みが「*」（アスタリスク）の場合は、安全のため元の Surface を使用する
                    string reading = (features.Length > 7 && features[7] != "*") ? features[7] : node.Surface;
                    
                    // カタカナをひらがなに変換 (簡易実装)
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
