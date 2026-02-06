using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using TranslationToolUI.Models;
using TranslationToolUI.Services.Audio;

namespace TranslationToolUI.Services
{
    public class SpeechTranslationService
    {
        private AzureSpeechConfig _config;
        private TranslationRecognizer? _recognizer;
        private bool _isTranslating; private string _currentSessionFilePath = string.Empty;

        private string _currentRunStamp = "";
        private string? _currentAudioWavPath;
        private string? _currentAudioMp3Path;

        private AudioConfig? _audioConfig;
        private PushAudioInputStream? _pushStream;
        private WasapiPcm16AudioSource? _naudioSource;

        private WavChunkRecorder? _wavRecorder;
        private Task? _pendingTranscodeTask;

        private readonly object _subtitleLock = new();
        private StreamWriter? _srtWriter;
        private StreamWriter? _vttWriter;
        private int _subtitleIndex = 1;
        private DateTime _sessionStartUtc;
        private TimeSpan _lastSubtitleEnd = TimeSpan.Zero;

        private readonly SemaphoreSlim _restartLock = new(1, 1);
        private CancellationTokenSource? _noResponseMonitorCts;
        private Task? _noResponseMonitorTask;
        private DateTime _lastRecognitionUtc = DateTime.MinValue;
        private DateTime _lastAudioActivityUtc = DateTime.MinValue;
        private double _smoothedAudioLevel;

        public event EventHandler<TranslationItem>? OnRealtimeTranslationReceived;
        public event EventHandler<TranslationItem>? OnFinalTranslationReceived;
        public event EventHandler<string>? OnStatusChanged;
        public event EventHandler<string>? OnReconnectTriggered;
        public event EventHandler<double>? OnAudioLevelUpdated;
        public SpeechTranslationService(AzureSpeechConfig config)
        {
            _config = config;
            _isTranslating = false;
            InitializeSessionFile();
        }

        private void InitializeSessionFile()
        {
            try
            {

                var sessionsPath = PathManager.Instance.SessionsPath;

                if (!Directory.Exists(sessionsPath))
                {
                    Directory.CreateDirectory(sessionsPath);
                }

                _currentSessionFilePath = PathManager.Instance.GetSessionFile(
                    $"Session_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                using (var fileStream = new FileStream(_currentSessionFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(fileStream))
                {
                    writer.WriteLine($"=== 实时翻译会话记录 - {DateTime.Now} ===");
                    writer.WriteLine();
                    writer.Flush();
                }

                OnStatusChanged?.Invoke(this, $"会话文件已创建: {_currentSessionFilePath}");
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke(this, $"创建会话文件失败: {ex.Message}");
            }
        }
        public async Task StartTranslationAsync()
        {
            if (_isTranslating)
                return;

            try
            {
                _currentRunStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _sessionStartUtc = DateTime.UtcNow;
                _audioConfig = CreateAudioConfigAndStartSource();
                InitializeSubtitleWriters();
                var speechConfig = CreateSpeechConfig();
                _recognizer = new TranslationRecognizer(speechConfig, _audioConfig);

                _recognizer.Recognized += OnRecognized;
                _recognizer.Recognizing += OnRecognizing;
                _recognizer.Canceled += OnCanceled;
                _recognizer.SessionStarted += OnSessionStarted;
                _recognizer.SessionStopped += OnSessionStopped;

                await _recognizer.StartContinuousRecognitionAsync();
                _isTranslating = true;
                _lastRecognitionUtc = DateTime.UtcNow;
                _lastAudioActivityUtc = DateTime.UtcNow;
                StartNoResponseMonitor();

                var inputName = GetInputDisplayName();
                var statusMessage = _config.FilterModalParticles
                    ? $"正在监听：{inputName}... (已启用语气助词过滤)"
                    : $"正在监听：{inputName}...";
                OnStatusChanged?.Invoke(this, statusMessage);
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke(this, $"启动翻译失败: {ex.Message}");
                await CleanupAudioAsync().ConfigureAwait(false);
            }
        }

        public async Task StopTranslationAsync()
        {
            if (!_isTranslating || _recognizer == null)
                return;

            try
            {
                StopNoResponseMonitor();
                await _recognizer.StopContinuousRecognitionAsync();
                _recognizer.Recognized -= OnRecognized;
                _recognizer.Recognizing -= OnRecognizing;
                _recognizer.Canceled -= OnCanceled;
                _recognizer.SessionStarted -= OnSessionStarted;
                _recognizer.SessionStopped -= OnSessionStopped;

                _recognizer.Dispose();
                _recognizer = null;
                _isTranslating = false;
                PublishAudioLevel(0);

                DisposeSubtitleWriters();

                await CleanupAudioAsync().ConfigureAwait(false);

                OnStatusChanged?.Invoke(this, "翻译已停止");
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke(this, $"停止翻译失败: {ex.Message}");
            }
        }

        private void OnRecognized(object? sender, TranslationRecognitionEventArgs e)
        {
            if (e.Result.Reason == ResultReason.TranslatedSpeech)
            {
                _lastRecognitionUtc = DateTime.UtcNow;
                string originalText = e.Result.Text;
                string translatedText = e.Result.Translations.ContainsKey(_config.TargetLanguage)
                    ? e.Result.Translations[_config.TargetLanguage]
                    : "";

                if (_config.FilterModalParticles)
                {
                    originalText = FilterModalParticles(originalText);
                }
                var translationItem = new TranslationItem
                {
                    Timestamp = DateTime.Now,
                    OriginalText = originalText,
                    TranslatedText = translatedText
                };

                SaveTranslationToFile(translationItem);
                WriteSubtitleEntry(e.Result, translatedText);
                OnFinalTranslationReceived?.Invoke(this, translationItem);

                OnStatusChanged?.Invoke(this, "收到最终识别结果");
            }
        }

        private void OnRecognizing(object? sender, TranslationRecognitionEventArgs e)
        {
            if (e.Result.Reason == ResultReason.TranslatingSpeech)
            {
                _lastRecognitionUtc = DateTime.UtcNow;
                string originalText = e.Result.Text;
                string translatedText = e.Result.Translations.ContainsKey(_config.TargetLanguage)
                    ? e.Result.Translations[_config.TargetLanguage]
                    : "";

                if (_config.FilterModalParticles)
                {
                    originalText = FilterModalParticles(originalText);
                }

                var translationItem = new TranslationItem
                {
                    Timestamp = DateTime.Now,
                    OriginalText = originalText,
                    TranslatedText = translatedText
                };
                OnRealtimeTranslationReceived?.Invoke(this, translationItem);

                OnStatusChanged?.Invoke(this, "实时更新 (中间结果)");
            }
        }

        private void OnCanceled(object? sender, TranslationRecognitionCanceledEventArgs e)
        {
            if (e.Reason == CancellationReason.Error)
            {
                OnStatusChanged?.Invoke(this, $"翻译错误: {e.ErrorCode}, {e.ErrorDetails}");
            }
        }

        private void OnSessionStarted(object? sender, SessionEventArgs e)
        {
            var inputName = GetInputDisplayName();
            var statusMessage = _config.FilterModalParticles
                ? $"正在监听：{inputName}... (已启用语气助词过滤) (点击停止退出)"
                : $"正在监听：{inputName}... (点击停止退出)";

            OnStatusChanged?.Invoke(this, statusMessage);
        }

        private void OnSessionStopped(object? sender, SessionEventArgs e)
        {
            OnStatusChanged?.Invoke(this, "翻译会话已结束");
        }

        private string FilterModalParticles(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            string[] fillers = { 
                "啊", "呀", "吧", "啦", "嘛", "呢", "哦", "呐", "哈", "呵", "嗯", "唉", "哎",
                "那个", "这个", "就是", "然后", "就是说", "怎么说", "你知道", "对吧", "是吧",
                "呃", "额", "嗯嗯", "啊啊", "哦哦"
            };

            string result = text;

            foreach (string filler in fillers)
            {
                result = System.Text.RegularExpressions.Regex.Replace(result, $@"^{System.Text.RegularExpressions.Regex.Escape(filler)}[，,]?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                result = System.Text.RegularExpressions.Regex.Replace(result, $@"\s+{System.Text.RegularExpressions.Regex.Escape(filler)}\s+", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                result = System.Text.RegularExpressions.Regex.Replace(result, $@"{System.Text.RegularExpressions.Regex.Escape(filler)}([。！？，,]?)$", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                result = result.Replace($"，{filler}", "，")
                               .Replace($"。{filler}", "。")
                               .Replace($"？{filler}", "？")
                               .Replace($"！{filler}", "！");
            }

            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"[，,]\s*[，,]", "，");
            result = result.Trim();

            return result;
        }

        private void SaveTranslationToFile(TranslationItem item)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentSessionFilePath))
                    return;

                using (var fileStream = new FileStream(_currentSessionFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(fileStream))
                {
                    writer.WriteLine($"[{item.Timestamp:HH:mm:ss}]");
                    writer.WriteLine($"原文: {item.OriginalText}");
                    writer.WriteLine($"译文: {item.TranslatedText}");
                    writer.WriteLine();
                    writer.Flush();
                }

                item.HasBeenWrittenToFile = true;
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke(this, $"保存翻译到文件失败: {ex.Message}");
            }
        }

        private void InitializeSubtitleWriters()
        {
            DisposeSubtitleWriters();

            _subtitleIndex = 1;
            _lastSubtitleEnd = TimeSpan.Zero;

            if (!_config.ExportSrtSubtitles && !_config.ExportVttSubtitles)
            {
                return;
            }

            var sessionsPath = PathManager.Instance.SessionsPath;
            Directory.CreateDirectory(sessionsPath);
            var baseName = GetSubtitleBaseName();

            if (_config.ExportSrtSubtitles)
            {
                var srtPath = PathManager.Instance.GetSessionFile($"{baseName}.srt");
                _srtWriter = new StreamWriter(new FileStream(srtPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
            }

            if (_config.ExportVttSubtitles)
            {
                var vttPath = PathManager.Instance.GetSessionFile($"{baseName}.vtt");
                _vttWriter = new StreamWriter(new FileStream(vttPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
                _vttWriter.WriteLine("WEBVTT");
                _vttWriter.WriteLine();
                _vttWriter.Flush();
            }
        }

        private string GetSubtitleBaseName()
        {
            var audioPath = _currentAudioMp3Path ?? _currentAudioWavPath;
            if (!string.IsNullOrWhiteSpace(audioPath))
            {
                return $"{Path.GetFileNameWithoutExtension(audioPath)}.speech";
            }

            return $"Audio_{_currentRunStamp}.speech";
        }

        private void DisposeSubtitleWriters()
        {
            lock (_subtitleLock)
            {
                try
                {
                    _srtWriter?.Flush();
                    _srtWriter?.Dispose();
                }
                catch
                {
                    // ignore
                }

                try
                {
                    _vttWriter?.Flush();
                    _vttWriter?.Dispose();
                }
                catch
                {
                    // ignore
                }

                _srtWriter = null;
                _vttWriter = null;
            }
        }

        private void WriteSubtitleEntry(TranslationRecognitionResult result, string translatedText)
        {
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                return;
            }

            if (_srtWriter == null && _vttWriter == null)
            {
                return;
            }

            if (!TryGetSubtitleTiming(result, out var start, out var end))
            {
                var fallbackEnd = DateTime.UtcNow - _sessionStartUtc;
                if (fallbackEnd < _lastSubtitleEnd + TimeSpan.FromMilliseconds(200))
                {
                    fallbackEnd = _lastSubtitleEnd + TimeSpan.FromMilliseconds(200);
                }
                start = _lastSubtitleEnd;
                end = fallbackEnd;
            }

            if (end <= start)
            {
                end = start + TimeSpan.FromMilliseconds(300);
            }

            lock (_subtitleLock)
            {
                if (_srtWriter != null)
                {
                    _srtWriter.WriteLine(_subtitleIndex);
                    _srtWriter.WriteLine($"{FormatSrtTime(start)} --> {FormatSrtTime(end)}");
                    _srtWriter.WriteLine(translatedText);
                    _srtWriter.WriteLine();
                    _srtWriter.Flush();
                }

                if (_vttWriter != null)
                {
                    _vttWriter.WriteLine($"{FormatVttTime(start)} --> {FormatVttTime(end)}");
                    _vttWriter.WriteLine(translatedText);
                    _vttWriter.WriteLine();
                    _vttWriter.Flush();
                }

                _subtitleIndex++;
            }

            _lastSubtitleEnd = end;
        }

        private static string FormatSrtTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
            {
                time = TimeSpan.Zero;
            }

            return string.Format("{0:00}:{1:00}:{2:00},{3:000}",
                (int)time.TotalHours,
                time.Minutes,
                time.Seconds,
                time.Milliseconds);
        }

        private static string FormatVttTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
            {
                time = TimeSpan.Zero;
            }

            return string.Format("{0:00}:{1:00}:{2:00}.{3:000}",
                (int)time.TotalHours,
                time.Minutes,
                time.Seconds,
                time.Milliseconds);
        }

        private static bool TryGetSubtitleTiming(TranslationRecognitionResult result, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            var json = result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!TryReadOffsetDuration(doc.RootElement, out var offset, out var duration))
                {
                    return false;
                }

                start = TimeSpan.FromTicks(Math.Max(0, offset));
                end = start + TimeSpan.FromTicks(Math.Max(0, duration));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadOffsetDuration(JsonElement root, out long offset, out long duration)
        {
            offset = 0;
            duration = 0;

            if (root.TryGetProperty("Offset", out var offsetElement) &&
                root.TryGetProperty("Duration", out var durationElement) &&
                offsetElement.TryGetInt64(out offset) &&
                durationElement.TryGetInt64(out duration))
            {
                return true;
            }

            if (root.TryGetProperty("NBest", out var nbest) &&
                nbest.ValueKind == JsonValueKind.Array &&
                nbest.GetArrayLength() > 0)
            {
                var first = nbest[0];
                if (first.TryGetProperty("Offset", out var nbOffset) &&
                    first.TryGetProperty("Duration", out var nbDuration) &&
                    nbOffset.TryGetInt64(out offset) &&
                    nbDuration.TryGetInt64(out duration))
                {
                    return true;
                }
            }

            return false;
        }
        public void UpdateConfig(AzureSpeechConfig newConfig)
        {
            bool wasTranslating = _isTranslating;

            if (wasTranslating)
            {
                OnStatusChanged?.Invoke(this, "配置已更改，正在重新连接...");
                StopTranslationAsync().Wait();
            }

            _config = newConfig;

            if (wasTranslating && _config.IsValid())
            {
                StartTranslationAsync().Wait();
                OnStatusChanged?.Invoke(this, "配置更新完成，翻译已重新开始");
            }
            else if (wasTranslating && !_config.IsValid())
            {
                OnStatusChanged?.Invoke(this, "配置无效，翻译已停止");
            }
        }

        private AudioConfig CreateAudioConfigAndStartSource()
        {
            if (!OperatingSystem.IsWindows())
            {
                if (_config.EnableRecording)
                {
                    OnStatusChanged?.Invoke(this, "当前平台不支持 NAudio 录音/设备枚举；识别将回退默认麦克风，且不会本地录音。");
                }
                _pushStream = null;
                _naudioSource = null;
                return AudioConfig.FromDefaultMicrophoneInput();
            }

            var streamFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
            _pushStream = AudioInputStream.CreatePushStream(streamFormat);

            var mode = _config.AudioSourceMode;
            var deviceId = mode == AudioSourceMode.DefaultMic ? null : _config.SelectedAudioDeviceId;

            _naudioSource = new WasapiPcm16AudioSource(mode == AudioSourceMode.DefaultMic ? AudioSourceMode.CaptureDevice : mode, deviceId, _config.ChunkDurationMs);
            _naudioSource.Pcm16ChunkReady += OnPcm16ChunkReady;

            try
            {
                _naudioSource.StartAsync().GetAwaiter().GetResult();

                if (_config.EnableRecording)
                {
                    _currentAudioWavPath = PathManager.Instance.GetSessionFile($"Audio_{_currentRunStamp}.wav");
                    _currentAudioMp3Path = PathManager.Instance.GetSessionFile($"Audio_{_currentRunStamp}.mp3");

                    _wavRecorder = new WavChunkRecorder(_currentAudioWavPath, _naudioSource.OutputWaveFormat);
                    _wavRecorder.Start();
                    OnStatusChanged?.Invoke(this, $"录音已开始: {_currentAudioWavPath}");
                }
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke(this, $"启动音频采集失败，已回退默认麦克风: {ex.Message}");
                _naudioSource.Pcm16ChunkReady -= OnPcm16ChunkReady;
                _naudioSource.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _naudioSource = null;

                _wavRecorder?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _wavRecorder = null;
                _currentAudioWavPath = null;
                _currentAudioMp3Path = null;

                _pushStream.Dispose();
                _pushStream = null;

                return AudioConfig.FromDefaultMicrophoneInput();
            }

            return AudioConfig.FromStreamInput(_pushStream);
        }

        private SpeechTranslationConfig CreateSpeechConfig()
        {
            var speechConfig = SpeechTranslationConfig.FromSubscription(_config.SubscriptionKey, _config.ServiceRegion);
            speechConfig.SpeechRecognitionLanguage = _config.SourceLanguage;
            speechConfig.AddTargetLanguage(_config.TargetLanguage);
            speechConfig.OutputFormat = OutputFormat.Detailed;

            if (_config.EnableAutoTimeout)
            {
                var initialSeconds = Math.Clamp(_config.InitialSilenceTimeoutSeconds, 1, 300);
                var endSeconds = Math.Clamp(_config.EndSilenceTimeoutSeconds, 1, 30);
                speechConfig.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs,
                    (initialSeconds * 1000).ToString());
                speechConfig.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs,
                    (endSeconds * 1000).ToString());
            }
            else
            {
                speechConfig.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "300000");
                speechConfig.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "300000");
            }

            if (_config.FilterModalParticles)
            {
                OnStatusChanged?.Invoke(this, "已启用语气助词过滤功能");
                speechConfig.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");
                speechConfig.SetProperty(PropertyId.SpeechServiceResponse_ProfanityOption, "Raw");
            }
            else
            {
                OnStatusChanged?.Invoke(this, "未启用语气助词过滤功能");
            }

            return speechConfig;
        }

        private void OnPcm16ChunkReady(byte[] chunk)
        {
            try
            {
                if (HasAudioActivity(chunk, _config.AudioActivityThreshold))
                {
                    _lastAudioActivityUtc = DateTime.UtcNow;
                }
                UpdateAudioLevel(chunk);
                _pushStream?.Write(chunk);
                _wavRecorder?.TryEnqueue(chunk);
            }
            catch
            {
                // ignore: can happen during shutdown
            }
        }

        private void UpdateAudioLevel(byte[] chunk)
        {
            var peak = GetPeakLevel(chunk);
            _smoothedAudioLevel = (_smoothedAudioLevel * 0.8) + (peak * 0.2);
            var gain = Math.Max(0.1, _config.AudioLevelGain);
            PublishAudioLevel(Math.Clamp(_smoothedAudioLevel * gain, 0, 1));
        }

        private static double GetPeakLevel(byte[] chunk)
        {
            var max = 0;
            for (var i = 0; i + 1 < chunk.Length; i += 2)
            {
                var sample = (short)(chunk[i] | (chunk[i + 1] << 8));
                var abs = Math.Abs(sample);
                if (abs > max)
                {
                    max = abs;
                }
            }

            return Math.Clamp(max / 32768d, 0, 1);
        }

        private void PublishAudioLevel(double level)
        {
            OnAudioLevelUpdated?.Invoke(this, Math.Clamp(level, 0, 1));
        }

        private static bool HasAudioActivity(byte[] chunk, int threshold)
        {
            var sampleThreshold = (short)Math.Clamp(Math.Abs(threshold), 50, 8000);
            for (var i = 0; i + 1 < chunk.Length; i += 2)
            {
                var sample = (short)(chunk[i] | (chunk[i + 1] << 8));
                if (sample >= sampleThreshold || sample <= -sampleThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        private void StartNoResponseMonitor()
        {
            if (!_config.EnableNoResponseRestart)
            {
                return;
            }

            _noResponseMonitorCts?.Cancel();
            _noResponseMonitorCts = new CancellationTokenSource();
            var token = _noResponseMonitorCts.Token;

            _noResponseMonitorTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (!_isTranslating || _recognizer == null || !_config.EnableNoResponseRestart)
                    {
                        continue;
                    }

                    var thresholdSeconds = Math.Max(1, _config.NoResponseRestartSeconds);
                    var threshold = TimeSpan.FromSeconds(thresholdSeconds);
                    var now = DateTime.UtcNow;

                    if (now - _lastRecognitionUtc < threshold)
                    {
                        continue;
                    }

                    if (now - _lastAudioActivityUtc > threshold)
                    {
                        continue;
                    }

                    await RestartRecognitionAsync($"无回显超过 {thresholdSeconds} 秒").ConfigureAwait(false);
                }
            }, token);
        }

        private void StopNoResponseMonitor()
        {
            _noResponseMonitorCts?.Cancel();
            _noResponseMonitorCts?.Dispose();
            _noResponseMonitorCts = null;
            _noResponseMonitorTask = null;
        }

        private async Task RestartRecognitionAsync(string reason)
        {
            await _restartLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_isTranslating || _recognizer == null || _audioConfig == null)
                {
                    return;
                }

                OnReconnectTriggered?.Invoke(this, reason);
                OnStatusChanged?.Invoke(this, $"{reason}，正在重连...");

                try
                {
                    await _recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignore stop failures
                }

                _recognizer.Recognized -= OnRecognized;
                _recognizer.Recognizing -= OnRecognizing;
                _recognizer.Canceled -= OnCanceled;
                _recognizer.SessionStarted -= OnSessionStarted;
                _recognizer.SessionStopped -= OnSessionStopped;

                _recognizer.Dispose();
                _recognizer = null;

                var speechConfig = CreateSpeechConfig();
                _recognizer = new TranslationRecognizer(speechConfig, _audioConfig);

                _recognizer.Recognized += OnRecognized;
                _recognizer.Recognizing += OnRecognizing;
                _recognizer.Canceled += OnCanceled;
                _recognizer.SessionStarted += OnSessionStarted;
                _recognizer.SessionStopped += OnSessionStopped;

                await _recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
                _lastRecognitionUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke(this, $"重连失败: {ex.Message}");
            }
            finally
            {
                _restartLock.Release();
            }
        }

        private string GetInputDisplayName()
        {
            return _config.AudioSourceMode switch
            {
                AudioSourceMode.DefaultMic => "默认麦克风",
                AudioSourceMode.Loopback => "系统输出回环",
                AudioSourceMode.CaptureDevice => "选择输入设备",
                _ => "音频输入"
            };
        }

        private async Task CleanupAudioAsync()
        {
            if (_wavRecorder != null)
            {
                try
                {
                    await _wavRecorder.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }

            var wavPath = _currentAudioWavPath;
            var mp3Path = _currentAudioMp3Path;
            _wavRecorder = null;
            _currentAudioWavPath = null;
            _currentAudioMp3Path = null;

            if (_naudioSource != null)
            {
                try
                {
                    _naudioSource.Pcm16ChunkReady -= OnPcm16ChunkReady;
                    await _naudioSource.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    _naudioSource = null;
                }
            }

            if (_pushStream != null)
            {
                try
                {
                    _pushStream.Close();
                    _pushStream.Dispose();
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    _pushStream = null;
                }
            }

            if (_audioConfig != null)
            {
                try
                {
                    _audioConfig.Dispose();
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    _audioConfig = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(wavPath) && _config.EnableRecording)
            {
                if (string.IsNullOrWhiteSpace(mp3Path))
                {
                    mp3Path = PathManager.Instance.GetSessionFile($"Audio_{DateTime.Now:yyyyMMdd_HHmmss}.mp3");
                }

                var bitrate = _config.RecordingMp3BitrateKbps;
                var deleteWav = _config.DeleteWavAfterMp3;

                _pendingTranscodeTask = WavToMp3Transcoder
                    .TranscodeToMp3AndOptionallyDeleteWavAsync(wavPath, mp3Path, bitrate, deleteWav)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            OnStatusChanged?.Invoke(this, $"MP3 转码失败（WAV 已保留）: {t.Exception?.GetBaseException().Message}");
                        }
                        else if (t.IsCanceled)
                        {
                            OnStatusChanged?.Invoke(this, "MP3 转码已取消（WAV 已保留）");
                        }
                        else
                        {
                            OnStatusChanged?.Invoke(this, $"MP3 已生成: {mp3Path}");
                        }
                    });

                OnStatusChanged?.Invoke(this, "正在后台转码为 MP3...");
            }
        }
    }
}

