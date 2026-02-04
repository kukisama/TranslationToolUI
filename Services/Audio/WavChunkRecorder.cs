using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.Wave;

namespace TranslationToolUI.Services.Audio
{
    public sealed class WavChunkRecorder : IAsyncDisposable
    {
        private readonly string _wavPath;
        private readonly WaveFormat _waveFormat;
        private readonly Channel<byte[]> _channel;

        private WaveFileWriter? _writer;
        private Task? _writerTask;

        public string WavPath => _wavPath;

        public WavChunkRecorder(string wavPath, WaveFormat waveFormat, int maxBufferedChunks = 200)
        {
            _wavPath = wavPath;
            _waveFormat = waveFormat;

            _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(Math.Max(10, maxBufferedChunks))
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });
        }

        public void Start()
        {
            if (_writerTask != null)
            {
                throw new InvalidOperationException("Recorder already started.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_wavPath) ?? ".");
            _writer = new WaveFileWriter(_wavPath, _waveFormat);
            _writerTask = Task.Run(WriterLoop, CancellationToken.None);
        }

        public bool TryEnqueue(byte[] chunk)
        {
            return _channel.Writer.TryWrite(chunk);
        }

        public async Task StopAsync()
        {
            if (_writerTask == null)
            {
                return;
            }

            _channel.Writer.TryComplete();

            try
            {
                await _writerTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
            finally
            {
                _writerTask = null;

                try
                {
                    _writer?.Dispose();
                }
                catch
                {
                    // ignore
                }
                _writer = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        private async Task WriterLoop()
        {
            if (_writer == null)
            {
                return;
            }

            while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var chunk))
                {
                    _writer.Write(chunk, 0, chunk.Length);
                }

                _writer.Flush();
            }
        }
    }
}
