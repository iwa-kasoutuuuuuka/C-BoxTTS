using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CBoxTTS.Native
{
    public class AudioEngine : IDisposable
    {
        private IWavePlayer? _player;
        private readonly int _sampleRate = 24000;

        public event Action? PlaybackStopped;

        public void Play(float[] audioData, float speed = 1.0f)
        {
            Stop();
            var normalized = Normalize(audioData);
            var processedData = StretchAudio(normalized, speed);

            // float配列をSampleProviderとして扱う
            var sampleProvider = new ISampleProvider[] { new RawSampleProvider(processedData, _sampleRate) };
            var player = new WaveOutEvent();
            player.PlaybackStopped += (s, e) =>
            {
                PlaybackStopped?.Invoke();
            };
            _player = player;
            _player.Init(sampleProvider[0]);
            _player.Play();
        }

        public void Stop()
        {
            if (_player != null)
            {
                var playerTemp = _player;
                _player = null; // 再入や競合を防ぐため先に null 代入
                playerTemp.Stop();
                playerTemp.Dispose();
            }
        }

        public void SaveWav(float[] audioData, string filePath, float speed = 1.0f)
        {
            var normalized = Normalize(audioData);
            var processedData = StretchAudio(normalized, speed);
            using (var writer = new WaveFileWriter(filePath, new WaveFormat(_sampleRate, 16, 1)))
            {
                // float (-1.0 to 1.0) を short (PCM 16bit) に変換して書き込み
                foreach (var sample in processedData)
                {
                    writer.WriteSample(sample);
                }
            }
        }

        private float[] Normalize(float[]? input)
        {
            if (input == null || input.Length == 0) return Array.Empty<float>();
            
            float maxAbs = 0f;
            foreach (var sample in input)
            {
                float abs = Math.Abs(sample);
                if (abs > maxAbs) maxAbs = abs;
            }

            if (maxAbs < 1e-6f) return input; // 全て無音の場合は何もしない

            float targetPeak = 0.95f;
            float factor = targetPeak / maxAbs;

            float[] output = new float[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                output[i] = input[i] * factor;
            }
            return output;
        }

        private float[] StretchAudio(float[] input, float speed)
        {
            if (Math.Abs(speed - 1.0f) < 0.01f) return input;

            int frameSize = 1024;
            int hopAnalysis = 256;
            int hopSynthesis = Math.Max(1, (int)Math.Round((double)hopAnalysis / speed));
            int maxSearchOffset = 128; // ピッチ同期探索範囲 ±128サンプル
            int inputLen = input.Length;
            if (inputLen < frameSize) return input;

            // ハニング窓の作成
            float[] window = new float[frameSize];
            for (int i = 0; i < frameSize; i++)
            {
                window[i] = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * i / (frameSize - 1)));
            }

            // フレーム数と出力長を事前計算してバッファを確保（List動的拡張よりも高速）
            int numFrames = (inputLen - frameSize) / hopAnalysis + 1;
            int outputLen = (numFrames - 1) * hopSynthesis + frameSize + maxSearchOffset * 2;
            float[] output = new float[outputLen];
            float[] outputWeights = new float[outputLen];

            for (int frame = 0; frame < numFrames; frame++)
            {
                int inputStart = frame * hopAnalysis;
                if (inputStart + frameSize > inputLen) break;

                // 出力配置の基準位置: フレーム番号 × 合成ホップサイズ（ドリフトしない）
                int nominalOutputStart = frame * hopSynthesis;
                int bestOffset = 0;

                // 2フレーム目以降: SOLA位相同期探索
                if (frame > 0)
                {
                    int overlapLen = Math.Max(128, frameSize - hopSynthesis);
                    if (overlapLen > frameSize) overlapLen = frameSize;

                    double bestCorr = double.NegativeInfinity;

                    for (int offset = -maxSearchOffset; offset <= maxSearchOffset; offset++)
                    {
                        int candidateStart = nominalOutputStart + offset;
                        if (candidateStart < 0 || candidateStart + overlapLen > outputLen) continue;

                        // 既存コンテンツがない領域はスキップ
                        if (outputWeights[candidateStart] < 1e-4f) continue;

                        double num = 0, denA = 0, denB = 0;
                        for (int i = 0; i < overlapLen; i++)
                        {
                            float a = input[inputStart + i];
                            // 正規化された出力値（重みで割る）と比較 — 生の累積値ではなく実際の音声波形
                            float b = outputWeights[candidateStart + i] > 1e-4f
                                ? output[candidateStart + i] / outputWeights[candidateStart + i]
                                : 0f;
                            num += a * b;
                            denA += a * a;
                            denB += b * b;
                        }
                        double den = Math.Sqrt(denA * denB);
                        double corr = (den > 1e-6) ? num / den : 0;

                        if (corr > bestCorr)
                        {
                            bestCorr = corr;
                            bestOffset = offset;
                        }
                    }
                }

                int actualStart = nominalOutputStart + bestOffset;
                if (actualStart < 0) actualStart = 0;

                // 全フレーム（最初のフレーム含む）をハニング窓付きでオーバーラップ加算
                for (int i = 0; i < frameSize; i++)
                {
                    int idx = actualStart + i;
                    if (idx >= outputLen) break;
                    output[idx] += input[inputStart + i] * window[i];
                    outputWeights[idx] += window[i];
                }
            }

            // 出力の実効長を検出（末尾の無音を除外）
            int actualLen = 0;
            for (int i = outputLen - 1; i >= 0; i--)
            {
                if (outputWeights[i] > 1e-4f) { actualLen = i + 1; break; }
            }

            // 重みで正規化して最終出力を生成
            float[] result = new float[actualLen];
            for (int i = 0; i < actualLen; i++)
            {
                result[i] = (outputWeights[i] > 1e-4f) ? output[i] / outputWeights[i] : 0f;
            }

            return result;
        }

        public void Dispose()
        {
            Stop();
        }

        private class RawSampleProvider : ISampleProvider
        {
            private readonly float[] _data;
            private int _offset;
            public WaveFormat WaveFormat { get; }

            public RawSampleProvider(float[] data, int sampleRate)
            {
                _data = data;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int available = _data.Length - _offset;
                int toCopy = Math.Min(available, count);
                Array.Copy(_data, _offset, buffer, offset, toCopy);
                _offset += toCopy;
                return toCopy;
            }
        }
    }
}
