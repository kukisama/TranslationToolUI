using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services.Audio
{
    public sealed class WasapiPcm16AudioSource : IAsyncDisposable
    {
        private readonly AudioSourceMode _mode;
        private readonly string? _deviceId;
        private readonly int _chunkDurationMs;

        private IWaveIn? _capture;
        private BufferedWaveProvider? _buffered;
        private IWaveProvider? _pcm16Provider;
        private CancellationTokenSource? _cts;
        private Task? _readerTask;
        private readonly AutoResetEvent _dataAvailableEvent = new(false);

        public WaveFormat OutputWaveFormat { get; } = new WaveFormat(16000, 16, 1);

        public event Action<byte[]>? Pcm16ChunkReady;

        public WasapiPcm16AudioSource(AudioSourceMode mode, string? deviceId, int chunkDurationMs)
        {
            _mode = mode;
            _deviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
            _chunkDurationMs = chunkDurationMs <= 0 ? 200 : chunkDurationMs;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_readerTask != null)
            {
                throw new InvalidOperationException("Audio source already started.");
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("NAudio WASAPI is only supported on Windows.");
            }

            var deviceType = _mode == AudioSourceMode.Loopback ? AudioDeviceType.Render : AudioDeviceType.Capture;
            using var enumerator = new MMDeviceEnumerator();
            var device = _deviceId != null
                ? enumerator.GetDevice(_deviceId)
                : enumerator.GetDefaultAudioEndpoint(deviceType == AudioDeviceType.Render ? DataFlow.Render : DataFlow.Capture, Role.Multimedia);

            if (device == null)
            {
                throw new InvalidOperationException("Unable to resolve audio device.");
            }

            _capture = _mode == AudioSourceMode.Loopback
                ? new WasapiLoopbackCapture(device)
                : new WasapiCapture(device);

            _buffered = new BufferedWaveProvider(_capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(5),
                ReadFully = false
            };

            var sampleProvider = _buffered.ToSampleProvider();
            var monoProvider = ToMono(sampleProvider);
            var resampled = new WdlResamplingSampleProvider(monoProvider, OutputWaveFormat.SampleRate);
            _pcm16Provider = new SampleToWaveProvider16(resampled);

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readerTask = Task.Run(() => ReaderLoop(_cts.Token), CancellationToken.None);

            _capture.StartRecording();
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

            if (_capture != null)
            {
                try
                {
                    _capture.StopRecording();
                }
                catch
                {
                    // ignore
                }
            }

            if (_readerTask != null)
            {
                try
                {
                    await _readerTask.ConfigureAwait(false);
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

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_buffered == null)
            {
                return;
            }

            _buffered.AddSamples(e.Buffer, 0, e.BytesRecorded);
            _dataAvailableEvent.Set();
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _dataAvailableEvent.Set();
        }

        private async Task ReaderLoop(CancellationToken token)
        {
            if (_pcm16Provider == null)
            {
                return;
            }

            var chunkBytes = GetChunkByteCount(_chunkDurationMs);
            var buffer = new byte[chunkBytes];

            while (!token.IsCancellationRequested)
            {
                var filled = 0;
                while (filled < buffer.Length && !token.IsCancellationRequested)
                {
                    var read = _pcm16Provider.Read(buffer, filled, buffer.Length - filled);
                    if (read <= 0)
                    {
                        _dataAvailableEvent.WaitOne(50);
                        continue;
                    }

                    filled += read;
                }

                if (filled == buffer.Length)
                {
                    var chunk = new byte[buffer.Length];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, buffer.Length);
                    Pcm16ChunkReady?.Invoke(chunk);
                }

                await Task.Yield();
            }
        }

        private int GetChunkByteCount(int chunkDurationMs)
        {
            var ms = Math.Clamp(chunkDurationMs, 20, 2000);
            var bytes = (OutputWaveFormat.AverageBytesPerSecond * ms) / 1000;
            var align = OutputWaveFormat.BlockAlign;
            if (align <= 0)
            {
                return bytes;
            }

            return Math.Max(align, bytes - (bytes % align));
        }

        private static ISampleProvider ToMono(ISampleProvider provider)
        {
            if (provider.WaveFormat.Channels == 1)
            {
                return provider;
            }

            if (provider.WaveFormat.Channels == 2)
            {
                return new StereoToMonoSampleProvider(provider)
                {
                    LeftVolume = 0.5f,
                    RightVolume = 0.5f
                };
            }

            var mux = new MultiplexingSampleProvider(new[] { provider }, 1);
            mux.ConnectInputToOutput(0, 0);
            return mux;
        }

        private void Cleanup()
        {
            _cts?.Dispose();
            _cts = null;

            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                _capture = null;
            }

            _readerTask = null;
            _buffered = null;
            _pcm16Provider = null;
        }
    }
}
