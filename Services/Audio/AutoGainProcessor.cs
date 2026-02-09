using System;
using NAudio.Wave;

namespace TranslationToolUI.Services.Audio
{
    public sealed class AutoGainProcessor
    {
        private readonly double _targetRms;
        private readonly double _minGain;
        private readonly double _maxGain;
        private readonly double _smoothing;
        private double _currentGain = 1.0;

        public AutoGainProcessor(double targetRms = 0.12, double minGain = 0.5, double maxGain = 6.0, double smoothing = 0.08)
        {
            _targetRms = Math.Clamp(targetRms, 0.02, 0.4);
            _minGain = Math.Clamp(minGain, 0.1, 2.0);
            _maxGain = Math.Clamp(maxGain, 1.0, 12.0);
            _smoothing = Math.Clamp(smoothing, 0.01, 0.5);
        }

        public void ProcessInPlace(byte[] buffer, int length)
        {
            if (length < 2)
            {
                return;
            }

            var rms = ComputeRmsPcm16(buffer, length);
            var targetGain = _targetRms / Math.Max(rms, 1e-6);
            targetGain = Math.Clamp(targetGain, _minGain, _maxGain);
            _currentGain = (_currentGain * (1 - _smoothing)) + (targetGain * _smoothing);

            for (var i = 0; i + 1 < length; i += 2)
            {
                var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                var scaled = (int)Math.Round(sample * _currentGain);
                if (scaled > short.MaxValue)
                {
                    scaled = short.MaxValue;
                }
                else if (scaled < short.MinValue)
                {
                    scaled = short.MinValue;
                }

                buffer[i] = (byte)(scaled & 0xFF);
                buffer[i + 1] = (byte)((scaled >> 8) & 0xFF);
            }
        }

        private static double ComputeRmsPcm16(byte[] buffer, int length)
        {
            long sumSquares = 0;
            var samples = 0;
            for (var i = 0; i + 1 < length; i += 2)
            {
                var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                sumSquares += (long)sample * sample;
                samples++;
            }

            if (samples == 0)
            {
                return 0;
            }

            var mean = sumSquares / (double)samples;
            var rms = Math.Sqrt(mean) / short.MaxValue;
            return Math.Clamp(rms, 0, 1);
        }
    }

    public sealed class AutoGainSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly double _targetRms;
        private readonly double _minGain;
        private readonly double _maxGain;
        private readonly double _smoothing;
        private double _currentGain = 1.0;

        public AutoGainSampleProvider(ISampleProvider source, double targetRms, double minGain, double maxGain, double smoothing)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _targetRms = Math.Clamp(targetRms, 0.02, 0.4);
            _minGain = Math.Clamp(minGain, 0.1, 2.0);
            _maxGain = Math.Clamp(maxGain, 1.0, 12.0);
            _smoothing = Math.Clamp(smoothing, 0.01, 0.5);
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read <= 0)
            {
                return read;
            }

            var rms = ComputeRms(buffer, offset, read);
            var targetGain = ComputeTargetGain(rms);
            ApplyGain(buffer, offset, read, targetGain);
            return read;
        }

        private double ComputeTargetGain(double rms)
        {
            var targetGain = _targetRms / Math.Max(rms, 1e-6);
            targetGain = Math.Clamp(targetGain, _minGain, _maxGain);
            _currentGain = (_currentGain * (1 - _smoothing)) + (targetGain * _smoothing);
            return _currentGain;
        }

        private static double ComputeRms(float[] buffer, int offset, int count)
        {
            double sumSquares = 0;
            for (var i = 0; i < count; i++)
            {
                var sample = buffer[offset + i];
                sumSquares += sample * sample;
            }

            if (count == 0)
            {
                return 0;
            }

            var rms = Math.Sqrt(sumSquares / count);
            return Math.Clamp(rms, 0, 1);
        }

        private static void ApplyGain(float[] buffer, int offset, int count, double gain)
        {
            var g = (float)gain;
            for (var i = 0; i < count; i++)
            {
                var sample = buffer[offset + i] * g;
                if (sample > 1f)
                {
                    sample = 1f;
                }
                else if (sample < -1f)
                {
                    sample = -1f;
                }

                buffer[offset + i] = sample;
            }
        }
    }
}
