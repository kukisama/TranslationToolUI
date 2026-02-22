using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.MediaFoundation;

namespace TrueFluentPro.Services.Audio
{
    public static class WavToMp3Transcoder
    {
        private static int _mfStarted;
        private static readonly object StartupLock = new();

        public static Task TranscodeToMp3AndOptionallyDeleteWavAsync(
            string wavPath,
            string mp3Path,
            int bitrateKbps,
            bool deleteWavAfter,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("MP3 转码依赖 Windows Media Foundation。");
                }

                EnsureMediaFoundationStarted();

                if (!File.Exists(wavPath))
                {
                    throw new FileNotFoundException("WAV 文件不存在", wavPath);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(mp3Path) ?? ".");

                using (var reader = new WaveFileReader(wavPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var bitrate = Math.Clamp(bitrateKbps, 32, 320) * 1000;
                    MediaFoundationEncoder.EncodeToMp3(reader, mp3Path, bitrate);
                }

                if (deleteWavAfter)
                {
                    try
                    {
                        File.Delete(wavPath);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }, cancellationToken);
        }

        private static void EnsureMediaFoundationStarted()
        {
            if (Volatile.Read(ref _mfStarted) == 1)
            {
                return;
            }

            lock (StartupLock)
            {
                if (_mfStarted == 1)
                {
                    return;
                }

                MediaFoundationApi.Startup();
                _mfStarted = 1;
            }
        }
    }
}
