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

        public void Play(float[] audioData, float speed = 1.0f)
        {
            Stop();
            var normalized = Normalize(audioData);
            var processedData = StretchAudio(normalized, speed);

            // float配列をSampleProviderとして扱う
            var sampleProvider = new ISampleProvider[] { new RawSampleProvider(processedData, _sampleRate) };
            _player = new WaveOutEvent();
            _player.Init(sampleProvider[0]);
            _player.Play();
        }

        public void Stop()
        {
            if (_player != null)
            {
                _player.Stop();
                _player.Dispose();
                _player = null;
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
            int hopSynthesis = (int)(hopAnalysis / speed);
            if (hopSynthesis <= 0) hopSynthesis = 1;

            int maxDelay = 256; // 探索窓サイズ
            int inputLen = input.Length;

            var output = new List<float>();
            var outputWeights = new List<float>();

            // ハニング窓の作成
            float[] window = new float[frameSize];
            for (int i = 0; i < frameSize; i++)
            {
                window[i] = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * i / (frameSize - 1)));
            }

            int inputPtr = 0;
            int outputPtr = 0;

            // 最初のフレームを配置
            if (inputLen >= frameSize)
            {
                for (int i = 0; i < frameSize; i++)
                {
                    output.Add(input[i]);
                    outputWeights.Add(1.0f);
                }
                inputPtr += hopAnalysis;
                outputPtr += hopSynthesis;
            }
            else
            {
                return input;
            }

            while (inputPtr + frameSize <= inputLen)
            {
                int bestDelay = 0;
                double maxCorrelation = double.NegativeInfinity;

                // 重ね合わせ領域での最適な位置を探索 (SOLA)
                int overlapRegion = frameSize - hopSynthesis;
                if (overlapRegion > 0 && output.Count >= outputPtr + overlapRegion)
                {
                    for (int delay = -maxDelay / 2; delay < maxDelay / 2; delay++)
                    {
                        int targetOutPtr = outputPtr + delay;
                        if (targetOutPtr < 0 || targetOutPtr + overlapRegion > output.Count)
                            continue;

                        double num = 0;
                        double denInput = 0;
                        double denOutput = 0;

                        for (int i = 0; i < overlapRegion; i++)
                        {
                            float inVal = input[inputPtr + i];
                            float outVal = output[targetOutPtr + i];
                            num += inVal * outVal;
                            denInput += inVal * inVal;
                            denOutput += outVal * outVal;
                        }

                        double correlation = 0;
                        double den = Math.Sqrt(denInput * denOutput);
                        if (den > 1e-6)
                        {
                            correlation = num / den;
                        }
                        else
                        {
                            correlation = num;
                        }

                        if (correlation > maxCorrelation)
                        {
                            maxCorrelation = correlation;
                            bestDelay = delay;
                        }
                    }
                }

                int actualOutPtr = outputPtr + bestDelay;

                while (output.Count < actualOutPtr + frameSize)
                {
                    output.Add(0f);
                    outputWeights.Add(0f);
                }

                // クロスフェード重ね合わせ
                for (int i = 0; i < frameSize; i++)
                {
                    float w = window[i];
                    float inputVal = input[inputPtr + i];
                    int outIdx = actualOutPtr + i;

                    output[outIdx] += inputVal * w;
                    outputWeights[outIdx] += w;
                }

                inputPtr += hopAnalysis;
                outputPtr = actualOutPtr + hopSynthesis;
            }

            float[] result = new float[output.Count];
            for (int i = 0; i < output.Count; i++)
            {
                if (outputWeights[i] > 1e-4f)
                {
                    result[i] = output[i] / outputWeights[i];
                }
                else
                {
                    result[i] = output[i];
                }
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
