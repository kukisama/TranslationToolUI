using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TrueFluentPro.Models;

namespace TrueFluentPro.Services.Audio
{
    public sealed class HighQualityRecorder : IAsyncDisposable
    {
        private readonly string? _loopbackDeviceId;
        private readonly string? _micDeviceId;
        private readonly string _mp3Path;
        private readonly int _mp3BitrateKbps;
        private readonly object _mixLock = new();
        private readonly bool _autoGainEnabled;
        private readonly double _autoGainTargetRms;
        private readonly double _autoGainMinGain;
        private readonly double _autoGainMaxGain;
        private readonly double _autoGainSmoothing;

        private WasapiLoopbackCapture? _loopbackCapture;
        private WasapiCapture? _micCapture;
        private BufferedWaveProvider? _loopbackBuffer;
        private BufferedWaveProvider? _micBuffer;
        private VolumeSampleProvider? _loopbackVolume;
        private VolumeSampleProvider? _micVolume;
        private float _loopbackCurrentVolume;
        private float _micCurrentVolume;
        private float _loopbackTargetVolume;
        private float _micTargetVolume;
        private bool _loopbackCaptureRunning;
        private bool _micCaptureRunning;
        private IWaveProvider? _waveProvider;
        private Stream? _writerStream;
        private CancellationTokenSource? _cts;
        private Task? _writerTask;
        private CancellationTokenSource? _fadeCts;
        private Task? _fadeTask;
        private readonly AutoResetEvent _dataAvailableEvent = new(false);
        private readonly Action<string>? _log;
        private long _loopbackBytes;
        private long _micBytes;
        private DateTime _lastStatsUtc = DateTime.MinValue;
        private double _lastPeak;
        private double _loopbackPeak;
        private double _micPeak;

        public HighQualityRecorder(string mp3Path, string? loopbackDeviceId, string? micDeviceId, bool enableLoopback, bool enableMic, int mp3BitrateKbps, bool autoGainEnabled, double autoGainTargetRms, double autoGainMinGain, double autoGainMaxGain, double autoGainSmoothing, Action<string>? log = null)
        {
            _mp3Path = mp3Path;
            _loopbackDeviceId = string.IsNullOrWhiteSpace(loopbackDeviceId) ? null : loopbackDeviceId;
            _micDeviceId = string.IsNullOrWhiteSpace(micDeviceId) ? null : micDeviceId;
            _loopbackCurrentVolume = enableLoopback ? 1f : 0f;
            _micCurrentVolume = enableMic ? 1f : 0f;
            _loopbackTargetVolume = _loopbackCurrentVolume;
            _micTargetVolume = _micCurrentVolume;
            _mp3BitrateKbps = Math.Clamp(mp3BitrateKbps, 32, 320);
            _autoGainEnabled = autoGainEnabled;
            _autoGainTargetRms = autoGainTargetRms;
            _autoGainMinGain = autoGainMinGain;
            _autoGainMaxGain = autoGainMaxGain;
            _autoGainSmoothing = autoGainSmoothing;
            _log = log;
        }

        public bool HasLoopbackCapture => _loopbackCapture != null;

        public bool HasMicCapture => _micCapture != null;

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
            var loopbackDevice = GetDevice(enumerator, _loopbackDeviceId, DataFlow.Render);
            var micDevice = GetDevice(enumerator, _micDeviceId, DataFlow.Capture);

            if (loopbackDevice == null && micDevice == null)
            {
                throw new InvalidOperationException("Unable to resolve recording device.");
            }

            if (loopbackDevice != null)
            {
                _loopbackCapture = new WasapiLoopbackCapture(loopbackDevice);
                _loopbackBuffer = new BufferedWaveProvider(_loopbackCapture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromSeconds(5),
                    ReadFully = true
                };

                _loopbackCapture.DataAvailable += OnLoopbackDataAvailable;
                _loopbackCapture.RecordingStopped += OnRecordingStopped;
            }

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
            }

            var targetFormat = _loopbackBuffer?.WaveFormat ?? _micBuffer?.WaveFormat
                ?? throw new InvalidOperationException("No audio source initialized.");

            var providers = new System.Collections.Generic.List<ISampleProvider>(2);

            if (_loopbackBuffer != null)
            {
                var loopbackSample = ConvertToMatch(_loopbackBuffer.ToSampleProvider(), targetFormat);
                _loopbackVolume = new VolumeSampleProvider(loopbackSample)
                {
                    Volume = _loopbackCurrentVolume
                };
                providers.Add(_loopbackVolume);
            }

            if (_micBuffer != null)
            {
                var micSample = ConvertToMatch(_micBuffer.ToSampleProvider(), targetFormat);
                _micVolume = new VolumeSampleProvider(micSample)
                {
                    Volume = _micCurrentVolume
                };
                providers.Add(_micVolume);
            }

            if (providers.Count == 0)
            {
                throw new InvalidOperationException("No audio source initialized.");
            }

            ISampleProvider mixedSample = new MixingSampleProvider(providers)
            {
                ReadFully = true
            };

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

            if (_loopbackCapture != null && _loopbackCurrentVolume > 0.0001f)
            {
                _loopbackCapture.StartRecording();
                _loopbackCaptureRunning = true;
            }

            if (_micCapture != null && _micCurrentVolume > 0.0001f)
            {
                _micCapture.StartRecording();
                _micCaptureRunning = true;
            }

            _log?.Invoke($"[录制流] 录制器启动 回环设备ID='{loopbackDevice?.ID ?? ""}' " +
                         $"回环格式='{_loopbackCapture?.WaveFormat}' 麦克风设备ID='{micDevice?.ID ?? ""}' " +
                         $"麦克风格式='{_micCapture?.WaveFormat}' 录回环={(_loopbackCurrentVolume > 0.5f)} 录麦={(_micCurrentVolume > 0.5f)} " +
                         $"输出MP3='{_mp3Path}' 比特率={_mp3BitrateKbps}");

            return Task.CompletedTask;
        }

        public void UpdateRouting(bool enableLoopback, bool enableMic, int fadeMilliseconds = 30)
        {
            lock (_mixLock)
            {
                _loopbackTargetVolume = enableLoopback ? 1f : 0f;
                _micTargetVolume = enableMic ? 1f : 0f;
            }

            TryStartCaptureIfNeeded();

            StartFadeTask(Math.Clamp(fadeMilliseconds, 10, 50));
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
            _fadeCts?.Cancel();

            if (_loopbackCapture != null)
            {
                try
                {
                    _loopbackCapture.StopRecording();
                    _loopbackCaptureRunning = false;
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
                    _micCaptureRunning = false;
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

            if (_fadeTask != null)
            {
                try
                {
                    await _fadeTask.ConfigureAwait(false);
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

              _log($"[录制流] 录制统计 回环字节={_loopbackBytes} 麦字节={_micBytes} 总峰值={_lastPeak:F4} " +
                  $"回环峰值={_loopbackPeak:F4} 麦峰值={_micPeak:F4}");
            _lastStatsUtc = now;
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (sender == _loopbackCapture)
            {
                _loopbackCaptureRunning = false;
            }
            else if (sender == _micCapture)
            {
                _micCaptureRunning = false;
            }

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

        private static MMDevice? GetDevice(MMDeviceEnumerator enumerator, string? deviceId, DataFlow flow)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(deviceId))
                {
                    try
                    {
                        return enumerator.GetDevice(deviceId);
                    }
                    catch
                    {
                        // fallback to default endpoint when persisted device id is stale/missing
                    }
                }

                return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            }
            catch
            {
                return null;
            }
        }

        private void StartFadeTask(int fadeMilliseconds)
        {
            _fadeCts?.Cancel();
            _fadeCts?.Dispose();
            _fadeCts = new CancellationTokenSource();
            var token = _fadeCts.Token;

            _fadeTask = Task.Run(async () =>
            {
                var steps = Math.Max(1, fadeMilliseconds / 10);
                for (var i = 0; i < steps && !token.IsCancellationRequested; i++)
                {
                    lock (_mixLock)
                    {
                        _loopbackCurrentVolume = StepVolume(_loopbackCurrentVolume, _loopbackTargetVolume, steps - i);
                        _micCurrentVolume = StepVolume(_micCurrentVolume, _micTargetVolume, steps - i);

                        if (_loopbackVolume != null)
                        {
                            _loopbackVolume.Volume = _loopbackCurrentVolume;
                        }

                        if (_micVolume != null)
                        {
                            _micVolume.Volume = _micCurrentVolume;
                        }
                    }

                    try
                    {
                        await Task.Delay(10, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                lock (_mixLock)
                {
                    _loopbackCurrentVolume = _loopbackTargetVolume;
                    _micCurrentVolume = _micTargetVolume;

                    if (_loopbackVolume != null)
                    {
                        _loopbackVolume.Volume = _loopbackCurrentVolume;
                    }

                    if (_micVolume != null)
                    {
                        _micVolume.Volume = _micCurrentVolume;
                    }
                }

                TryStopCaptureIfMuted();
            }, token);
        }

        private void TryStartCaptureIfNeeded()
        {
            if (_loopbackCapture != null && !_loopbackCaptureRunning && _loopbackTargetVolume > 0.0001f)
            {
                try
                {
                    _loopbackCapture.StartRecording();
                    _loopbackCaptureRunning = true;
                }
                catch
                {
                    // ignore
                }
            }

            if (_micCapture != null && !_micCaptureRunning && _micTargetVolume > 0.0001f)
            {
                try
                {
                    _micCapture.StartRecording();
                    _micCaptureRunning = true;
                }
                catch
                {
                    // ignore
                }
            }
        }

        private void TryStopCaptureIfMuted()
        {
            if (_loopbackCapture != null && _loopbackCaptureRunning && _loopbackTargetVolume <= 0.0001f)
            {
                try
                {
                    _loopbackCapture.StopRecording();
                    _loopbackCaptureRunning = false;
                }
                catch
                {
                    // ignore
                }
            }

            if (_micCapture != null && _micCaptureRunning && _micTargetVolume <= 0.0001f)
            {
                try
                {
                    _micCapture.StopRecording();
                    _micCaptureRunning = false;
                }
                catch
                {
                    // ignore
                }
            }
        }

        private static float StepVolume(float current, float target, int remainSteps)
        {
            if (remainSteps <= 1)
            {
                return target;
            }

            return current + ((target - current) / remainSteps);
        }

        private void Cleanup()
        {
            _cts?.Dispose();
            _cts = null;
            _fadeCts?.Dispose();
            _fadeCts = null;

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
            _fadeTask = null;
            _writerStream?.Dispose();
            _writerStream = null;
            _loopbackBuffer = null;
            _micBuffer = null;
            _loopbackVolume = null;
            _micVolume = null;
            _waveProvider = null;
        }
    }
}
