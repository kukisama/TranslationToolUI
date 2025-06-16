using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services
{
    public class SpeechTranslationService
    {
        private AzureSpeechConfig _config;
        private TranslationRecognizer? _recognizer;
        private bool _isTranslating; private string _currentSessionFilePath = string.Empty;

        public event EventHandler<TranslationItem>? OnRealtimeTranslationReceived;
        public event EventHandler<TranslationItem>? OnFinalTranslationReceived;
        public event EventHandler<string>? OnStatusChanged;
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
                var speechConfig = SpeechTranslationConfig.FromSubscription(_config.SubscriptionKey, _config.ServiceRegion);
                speechConfig.SpeechRecognitionLanguage = _config.SourceLanguage;
                speechConfig.AddTargetLanguage(_config.TargetLanguage);
                speechConfig.OutputFormat = OutputFormat.Detailed;

                if (_config.EnableAutoTimeout)
                {
                    speechConfig.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs,
                        (_config.TimeoutSeconds * 5000).ToString());
                    speechConfig.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "1000");
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

                var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
                _recognizer = new TranslationRecognizer(speechConfig, audioConfig);

                _recognizer.Recognized += OnRecognized;
                _recognizer.Recognizing += OnRecognizing;
                _recognizer.Canceled += OnCanceled;
                _recognizer.SessionStarted += OnSessionStarted;
                _recognizer.SessionStopped += OnSessionStopped;

                await _recognizer.StartContinuousRecognitionAsync();
                _isTranslating = true;

                var statusMessage = _config.FilterModalParticles
                    ? "正在监听麦克风... (已启用语气助词过滤)"
                    : "正在监听麦克风...";
                OnStatusChanged?.Invoke(this, statusMessage);
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke(this, $"启动翻译失败: {ex.Message}");
            }
        }

        public async Task StopTranslationAsync()
        {
            if (!_isTranslating || _recognizer == null)
                return;

            try
            {
                await _recognizer.StopContinuousRecognitionAsync();
                _recognizer.Recognized -= OnRecognized;
                _recognizer.Recognizing -= OnRecognizing;
                _recognizer.Canceled -= OnCanceled;
                _recognizer.SessionStarted -= OnSessionStarted;
                _recognizer.SessionStopped -= OnSessionStopped;

                _recognizer.Dispose();
                _recognizer = null;
                _isTranslating = false;

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
                OnFinalTranslationReceived?.Invoke(this, translationItem);

                OnStatusChanged?.Invoke(this, "收到最终识别结果");
            }
        }

        private void OnRecognizing(object? sender, TranslationRecognitionEventArgs e)
        {
            if (e.Result.Reason == ResultReason.TranslatingSpeech)
            {
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
            var statusMessage = _config.FilterModalParticles
                ? "正在监听麦克风... (已启用语气助词过滤) (点击停止退出)"
                : "正在监听麦克风... (点击停止退出)";

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
    }
}

