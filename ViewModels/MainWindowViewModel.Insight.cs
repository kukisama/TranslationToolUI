using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using TranslationToolUI.Models;
using TranslationToolUI.Services;

namespace TranslationToolUI.ViewModels
{
    public partial class MainWindowViewModel
    {
        private readonly AiInsightService _aiInsightService = new();
        private string _insightMarkdown = "";
        private string _insightUserInput = "";
        private bool _isInsightLoading;
        private CancellationTokenSource? _insightCts;

        private bool _isAutoInsightEnabled;
        private int _autoInsightIntervalSeconds = 30;
        private int _autoInsightModeIndex; // 0=定时, 1=新数据触发
        private string _autoInsightPrompt = "请对以上翻译记录进行会议摘要。总结会议的主要议题、关键讨论内容和结论。";
        private DispatcherTimer? _autoInsightTimer;
        private int _lastAutoInsightHistoryCount;

        public string InsightMarkdown
        {
            get => _insightMarkdown;
            set
            {
                if (SetProperty(ref _insightMarkdown, value))
                {
                    OnPropertyChanged(nameof(IsInsightEmpty));
                }
            }
        }

        public string InsightUserInput
        {
            get => _insightUserInput;
            set
            {
                if (SetProperty(ref _insightUserInput, value))
                {
                    ((RelayCommand)SendInsightCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsInsightLoading
        {
            get => _isInsightLoading;
            set
            {
                if (SetProperty(ref _isInsightLoading, value))
                {
                    OnPropertyChanged(nameof(IsInsightEmpty));
                    ((RelayCommand)SendInsightCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)StopInsightCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)SendPresetInsightCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAiConfigured => _config.AiConfig?.IsValid == true;

        public List<InsightPresetButton> InsightPresetButtons =>
            _config.AiConfig?.PresetButtons ?? new List<InsightPresetButton>();

        public bool IsInsightEmpty => string.IsNullOrEmpty(InsightMarkdown) && !IsInsightLoading;

        public bool IsAutoInsightEnabled
        {
            get => _isAutoInsightEnabled;
            private set
            {
                if (SetProperty(ref _isAutoInsightEnabled, value))
                {
                    OnPropertyChanged(nameof(AutoInsightToggleText));
                    ((RelayCommand)ToggleAutoInsightCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string AutoInsightToggleText => IsAutoInsightEnabled ? "停止自动洞察" : "启动自动洞察";

        public int AutoInsightIntervalSeconds
        {
            get => _autoInsightIntervalSeconds;
            set => SetProperty(ref _autoInsightIntervalSeconds, Math.Max(10, value));
        }

        public int AutoInsightModeIndex
        {
            get => _autoInsightModeIndex;
            set => SetProperty(ref _autoInsightModeIndex, value);
        }

        public string AutoInsightPrompt
        {
            get => _autoInsightPrompt;
            set => SetProperty(ref _autoInsightPrompt, value);
        }

        private async void SendInsight(string userQuestion)
        {
            if (_config.AiConfig == null || !_config.AiConfig.IsValid)
            {
                return;
            }

            _insightCts?.Cancel();
            _insightCts = new CancellationTokenSource();
            var token = _insightCts.Token;

            IsInsightLoading = true;
            InsightMarkdown = "";

            var systemPrompt = "你是一个专业的会议/翻译分析助手。用户会提供实时翻译的历史记录，请根据用户的问题对内容进行分析。请用 Markdown 格式输出分析结果。";
            var historyText = FormatHistoryForAi();
            var fullUserContent = $"以下是翻译历史记录：\n\n{historyText}\n\n---\n\n用户问题：{userQuestion}";

            try
            {
                var sb = new System.Text.StringBuilder();
                await _aiInsightService.StreamChatAsync(
                    _config.AiConfig,
                    systemPrompt,
                    fullUserContent,
                    chunk =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            sb.Append(chunk);
                            InsightMarkdown = sb.ToString();
                        });
                    },
                    token,
                    AiChatProfile.Quick,
                    enableReasoning: false);
            }
            catch (OperationCanceledException)
            {
                // user stopped
            }
            catch (HttpRequestException ex)
            {
                InsightMarkdown += $"\n\n---\n**错误**: {ex.Message}";
            }
            catch (Exception ex)
            {
                InsightMarkdown += $"\n\n---\n**错误**: {ex.Message}";
            }
            finally
            {
                IsInsightLoading = false;
            }
        }

        private void StopInsight()
        {
            _insightCts?.Cancel();
        }

        private string FormatHistoryForAi()
        {
            if (History.Count == 0)
            {
                return "(暂无翻译记录)";
            }

            var sb = new System.Text.StringBuilder();
            var ordered = History.Reverse().ToList();
            foreach (var item in ordered)
            {
                sb.AppendLine($"[{item.Timestamp:HH:mm:ss}]");
                sb.AppendLine($"  原文: {item.OriginalText}");
                sb.AppendLine($"  译文: {item.TranslatedText}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private async Task ShowAiConfig()
        {
            await ShowConfig();
        }

        private void ToggleAutoInsight()
        {
            if (IsAutoInsightEnabled)
            {
                StopAutoInsight();
                return;
            }

            if (string.IsNullOrWhiteSpace(AutoInsightPrompt))
            {
                return;
            }

            IsAutoInsightEnabled = true;
            _lastAutoInsightHistoryCount = History.Count;

            if (AutoInsightModeIndex == 0)
            {
                _autoInsightTimer = new DispatcherTimer(
                    TimeSpan.FromSeconds(AutoInsightIntervalSeconds),
                    DispatcherPriority.Background,
                    (_, _) => AutoInsightTick());
                _autoInsightTimer.Start();
                StatusMessage = $"自动洞察已启动，每 {AutoInsightIntervalSeconds} 秒分析一次";
            }
            else
            {
                StatusMessage = "自动洞察已启动，每收到新翻译数据时自动分析";
            }
        }

        private void StopAutoInsight()
        {
            IsAutoInsightEnabled = false;
            _autoInsightTimer?.Stop();
            _autoInsightTimer = null;
            StatusMessage = "自动洞察已停止";
        }

        private void AutoInsightTick()
        {
            if (!IsAutoInsightEnabled || IsInsightLoading)
            {
                return;
            }

            if (History.Count == 0)
            {
                return;
            }

            SendInsight(AutoInsightPrompt);
        }

        private void OnNewDataAutoInsight()
        {
            if (!IsAutoInsightEnabled || AutoInsightModeIndex != 1)
            {
                return;
            }

            if (IsInsightLoading)
            {
                return;
            }

            if (History.Count <= _lastAutoInsightHistoryCount)
            {
                return;
            }

            _lastAutoInsightHistoryCount = History.Count;
            SendInsight(AutoInsightPrompt);
        }
    }
}
