using System;
using System.IO;
using System.Threading;
using NAudio.Wave;

namespace TrueFluentPro.Services
{
    public static class AudioFormatConverter
    {
        public static string PrepareBatchUploadAudioPath(
            string audioPath,
            Action<string>? onStatus,
            CancellationToken token,
            out string? tempUploadPath,
            out bool converted)
        {
            tempUploadPath = null;
            converted = false;

            if (IsPcm16kMonoWav(audioPath))
            {
                return audioPath;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "TrueFluentPro", "BatchAudio");
            Directory.CreateDirectory(tempDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var unique = Guid.NewGuid().ToString("N");
            tempUploadPath = Path.Combine(
                tempDir,
                $"{Path.GetFileNameWithoutExtension(audioPath)}_{stamp}_{unique}_pcm16k_mono.wav");

            onStatus?.Invoke("批量转写：转换 WAV(16kHz/16-bit/mono)...");

            try
            {
                ConvertToPcm16kMonoWav(audioPath, tempUploadPath, token);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"音频转换失败: {ex.Message}", ex);
            }

            converted = true;

            return tempUploadPath;
        }

        public static bool IsPcm16kMonoWav(string audioPath)
        {
            if (!string.Equals(Path.GetExtension(audioPath), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using var reader = new WaveFileReader(audioPath);
                var format = reader.WaveFormat;
                return format.Encoding == WaveFormatEncoding.Pcm
                       && format.SampleRate == 16000
                       && format.BitsPerSample == 16
                       && format.Channels == 1;
            }
            catch
            {
                return false;
            }
        }

        public static void ConvertToPcm16kMonoWav(string sourcePath, string outputPath, CancellationToken token)
        {
            using var reader = new AudioFileReader(sourcePath);
            using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1))
            {
                ResamplerQuality = 60
            };
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            using var writer = new WaveFileWriter(outputPath, resampler.WaveFormat);
            var buffer = new byte[resampler.WaveFormat.AverageBytesPerSecond];
            int read;
            while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                writer.Write(buffer, 0, read);
            }
        }
    }
}
