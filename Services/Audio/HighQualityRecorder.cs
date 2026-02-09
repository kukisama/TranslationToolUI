using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services.Audio
{
    public sealed class HighQualityRecorder : IAsyncDisposable
    {
        private readonly RecordingMode _mode;
        private readonly string? _loopbackDeviceId;
        private readonly string? _micDeviceId;
        private readonly string _mp3Path;
        private readonly int _mp3BitrateKbps;
        private readonly bool _autoGainEnabled;
        private readonly double _autoGainTargetRms;
        private readonly double _autoGainMinGain;
        private readonly double _autoGainMaxGain;
        private readonly double _autoGainSmoothing;

        private WasapiLoopbackCapture? _loopbackCapture;
        private WasapiCapture? _micCapture;
        private BufferedWaveProvider? _loopbackBuffer;
        private BufferedWaveProvider? _micBuffer;
        private IWaveProvider? _waveProvider;
        private Stream? _writerStream;
        private CancellationTokenSource? _cts;
        private Task? _writerTask;
        private readonly AutoResetEvent _dataAvailableEvent = new(false);
        private readonly Action<string>? _log;
        private long _loopbackBytes;
        private long _micBytes;
        private DateTime _lastStatsUtc = DateTime.MinValue;
        private double _lastPeak;
        private double _loopbackPeak;
        private double _micPeak;

        public HighQualityRecorder(string mp3Path, string? loopbackDeviceId, string? micDeviceId, RecordingMode mode, int mp3BitrateKbps, bool autoGainEnabled, double autoGainTargetRms, double autoGainMinGain, double autoGainMaxGain, double autoGainSmoothing, Action<string>? log = null)
        {
            _mp3Path = mp3Path;
            _loopbackDeviceId = string.IsNullOrWhiteSpace(loopbackDeviceId) ? null : loopbackDeviceId;
            _micDeviceId = string.IsNullOrWhiteSpace(micDeviceId) ? null : micDeviceId;
            _mode = mode;
            _mp3BitrateKbps = Math.Clamp(mp3BitrateKbps, 32, 320);
            _autoGainEnabled = autoGainEnabled;
            _autoGainTargetRms = autoGainTargetRms;
            _autoGainMinGain = autoGainMinGain;
            _autoGainMaxGain = autoGainMaxGain;
            _autoGainSmoothing = autoGainSmoothing;
            _log = log;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_writerTask != null)
            {
                throw new InvalidOperationException("Recorder already started.");
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("NAudio WASAPI is only supported on Windows.");
            }

            using var enumerator = new MMDeviceEnumerator();
            var loopbackDevice = _loopbackDeviceId != null
                ? enumerator.GetDevice(_loopbackDeviceId)
                : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            if (loopbackDevice == null)
            {
                throw new InvalidOperationException("Unable to resolve loopback device.");
            }

            _loopbackCapture = new WasapiLoopbackCapture(loopbackDevice);
            _loopbackBuffer = new BufferedWaveProvider(_loopbackCapture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(5),
                ReadFully = true
            };

            _loopbackCapture.DataAvailable += OnLoopbackDataAvailable;
            _loopbackCapture.RecordingStopped += OnRecordingStopped;

            ISampleProvider loopbackSample = _loopbackBuffer.ToSampleProvider();
            var targetFormat = loopbackSample.WaveFormat;

            ISampleProvider? mixedSample = loopbackSample;

            if (_mode == RecordingMode.LoopbackWithMic)
            {
                var micDevice = _micDeviceId != null
                    ? enumerator.GetDevice(_micDeviceId)
                    : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

                if (micDevice != null)
                {
                    _micCapture = new WasapiCapture(micDevice);
                    _micBuffer = new BufferedWaveProvider(_micCapture.WaveFormat)
                    {
                        DiscardOnBufferOverflow = true,
                        BufferDuration = TimeSpan.FromSeconds(5),
                        ReadFully = true
                    };

                    _micCapture.DataAvailable += OnMicDataAvailable;
                    _micCapture.RecordingStopped += OnRecordingStopped;

                    var micSample = ConvertToMatch(_micBuffer.ToSampleProvider(), targetFormat);
                    var mixer = new MixingSampleProvider(new[] { loopbackSample, micSample })
                    {
                        ReadFully = true
                    };
                    mixedSample = mixer;
                }
            }

            if (_autoGainEnabled)
            {
                mixedSample = new AutoGainSampleProvider(
                    mixedSample,
                    _autoGainTargetRms,
                    _autoGainMinGain,
                    _autoGainMaxGain,
                    _autoGainSmoothing);
            }

            _waveProvider = new SampleToWaveProvider16(mixedSample);
            _writerStream = new LameMP3FileWriter(_mp3Path, _waveProvider.WaveFormat, _mp3BitrateKbps);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _writerTask = Task.Run(() => WriterLoop(_cts.Token), CancellationToken.None);

            _loopbackCapture.StartRecording();
            _micCapture?.StartRecording();

            _log?.Invoke($"HqRecorder start mode={_mode} loopbackId='{loopbackDevice.ID}' " +
                         $"loopbackFmt='{_loopbackCapture.WaveFormat}' micId='{_micDeviceId ?? ""}' " +
                         $"micFmt='{_micCapture?.WaveFormat}' mp3='{_mp3Path}' bitrate={_mp3BitrateKbps}");

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            var cts = _cts;
            if (cts == null)
            {
                return;
            }

            cts.Cancel();
            _dataAvailableEvent.Set();

            if (_loopbackCapture != null)
            {
                try
                {
                    _loopbackCapture.StopRecording();
                }
                catch
                {
                    // ignore
                }
            }

            if (_micCapture != null)
            {
                try
                {
                    _micCapture.StopRecording();
                }
                catch
                {
                    // ignore
                }
            }

            if (_writerTask != null)
            {
                try
                {
                    await _writerTask.ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }

            Cleanup();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _dataAvailableEvent.Dispose();
        }

        private void OnLoopbackDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_loopbackBuffer == null)
            {
                return;
            }

            _loopbackBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            _loopbackBytes += e.BytesRecorded;
            _loopbackPeak = GetPeakLevelFloat(e.Buffer, e.BytesRecorded);
            MaybeLogStats();
            try
            {
                _dataAvailableEvent.Set();
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }
        }

        private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_micBuffer == null)
            {
                return;
            }

            _micBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            _micBytes += e.BytesRecorded;
            _micPeak = GetPeakLevelFloat(e.Buffer, e.BytesRecorded);
            MaybeLogStats();
            try
            {
                _dataAvailableEvent.Set();
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }
        }

        private void MaybeLogStats()
        {
            if (_log == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (_lastStatsUtc == DateTime.MinValue)
            {
                _lastStatsUtc = now;
                return;
            }

            if ((now - _lastStatsUtc).TotalSeconds < 2)
            {
                return;
            }

              _log($"HqRecorder bytes loopback={_loopbackBytes} mic={_micBytes} peak={_lastPeak:F4} " +
                  $"loopPeak={_loopbackPeak:F4} micPeak={_micPeak:F4}");
            _lastStatsUtc = now;
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            try
            {
                _dataAvailableEvent.Set();
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }
        }

        private async Task WriterLoop(CancellationToken token)
        {
            if (_waveProvider == null || _writerStream == null)
            {
                return;
            }
            var format = _waveProvider.WaveFormat;
            var frameMs = 500;
            var bytesPerFrame = Math.Max(format.BlockAlign, (format.AverageBytesPerSecond * frameMs) / 1000);
            bytesPerFrame -= bytesPerFrame % format.BlockAlign;

            var buffer = new byte[bytesPerFrame];
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var nextDue = TimeSpan.FromMilliseconds(frameMs);

            while (!token.IsCancellationRequested)
            {
                var frameDeadline = stopwatch.Elapsed + TimeSpan.FromMilliseconds(frameMs);
                var filled = 0;

                while (filled < buffer.Length && !token.IsCancellationRequested)
                {
                    var read = _waveProvider.Read(buffer, filled, buffer.Length - filled);
                    if (read > 0)
                    {
                        filled += read;
                        continue;
                    }

                    var remaining = frameDeadline - stopwatch.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    try
                    {
                        _dataAvailableEvent.WaitOne(remaining);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }

                if (filled < buffer.Length)
                {
                    Array.Clear(buffer, filled, buffer.Length - filled);
                }

                _lastPeak = GetPeakLevel(buffer, filled > 0 ? filled : buffer.Length);

                _writerStream.Write(buffer, 0, buffer.Length);

                var delay = nextDue - stopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                nextDue += TimeSpan.FromMilliseconds(frameMs);
            }

            try
            {
                _writerStream.Flush();
            }
            catch
            {
                // ignore
            }
        }

        private static double GetPeakLevel(byte[] buffer, int length)
        {
            var max = 0;
            var limit = Math.Max(0, Math.Min(length, buffer.Length));
            for (var i = 0; i + 1 < limit; i += 2)
            {
                var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                var abs = Math.Abs(sample);
                if (abs > max)
                {
                    max = abs;
                }
            }

            return Math.Clamp(max / 32768d, 0, 1);
        }

        private static double GetPeakLevelFloat(byte[] buffer, int length)
        {
            var limit = Math.Max(0, Math.Min(length, buffer.Length));
            if (limit < 4)
            {
                return 0;
            }

            var waveBuffer = new WaveBuffer(buffer);
            var samples = Math.Min(waveBuffer.FloatBuffer.Length, limit / 4);
            var max = 0f;
            for (var i = 0; i < samples; i++)
            {
                var abs = MathF.Abs(waveBuffer.FloatBuffer[i]);
                if (abs > max)
                {
                    max = abs;
                }
            }

            return Math.Clamp(max, 0, 1);
        }

        private static ISampleProvider ConvertToMatch(ISampleProvider input, WaveFormat target)
        {
            ISampleProvider sample = input;

            if (sample.WaveFormat.SampleRate != target.SampleRate)
            {
                sample = new WdlResamplingSampleProvider(sample, target.SampleRate);
            }

            if (sample.WaveFormat.Channels != target.Channels)
            {
                if (sample.WaveFormat.Channels == 1 && target.Channels == 2)
                {
                    sample = new MonoToStereoSampleProvider(sample);
                }
                else if (sample.WaveFormat.Channels == 2 && target.Channels == 1)
                {
                    sample = new StereoToMonoSampleProvider(sample)
                    {
                        LeftVolume = 0.5f,
                        RightVolume = 0.5f
                    };
                }
            }

            return sample;
        }

        private void Cleanup()
        {
            _cts?.Dispose();
            _cts = null;

            if (_loopbackCapture != null)
            {
                _loopbackCapture.DataAvailable -= OnLoopbackDataAvailable;
                _loopbackCapture.RecordingStopped -= OnRecordingStopped;
                _loopbackCapture.Dispose();
                _loopbackCapture = null;
            }

            if (_micCapture != null)
            {
                _micCapture.DataAvailable -= OnMicDataAvailable;
                _micCapture.RecordingStopped -= OnRecordingStopped;
                _micCapture.Dispose();
                _micCapture = null;
            }

            _writerTask = null;
            _writerStream?.Dispose();
            _writerStream = null;
            _loopbackBuffer = null;
            _micBuffer = null;
            _waveProvider = null;
        }
    }
}
