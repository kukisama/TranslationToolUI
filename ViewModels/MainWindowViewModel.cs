using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using TranslationToolUI.Models;
using TranslationToolUI.Services;
using TranslationToolUI.Views;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.ComponentModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using TranslationToolUI.Services.Audio;
using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using NAudio.Wave;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Transcription;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using System.Xml;

namespace TranslationToolUI.ViewModels
{
    public enum EditorDisplayMode
    {
        Original,
        Translated,
        Bilingual
    }

    public partial class MainWindowViewModel : ViewModelBase
    {
        private AzureSpeechConfig _config;
        private bool _isTranslating = false;
        private string _statusMessage = "就绪";
        private string _currentOriginal = "";
        private string _currentTranslated = "";
        private ObservableCollection<TranslationItem> _history;
        private SpeechTranslationService? _translationService;
        private Window? _mainWindow;
        private ConfigurationService _configService;
        private ObservableCollection<string> _subscriptionNames;
        private int _activeSubscriptionIndex;
        private string _sourceLanguage = "zh-CN";
        private string _targetLanguage = "en";
        private bool _isConfigurationEnabled = true;
        private TextEditorType _editorType = TextEditorType.Advanced;
        private EditorDisplayMode _editorDisplayMode = EditorDisplayMode.Translated;

        private int _audioSourceModeIndex;
        private readonly ObservableCollection<AudioDeviceInfo> _audioDevices;
        private AudioDeviceInfo? _selectedAudioDevice;
        private bool _isAudioDeviceSelectionEnabled;
        private bool _isAudioDeviceRefreshEnabled;
        private double _audioLevel;
        private readonly ObservableCollection<double> _audioLevelHistory;

        private readonly ObservableCollection<MediaFileItem> _audioFiles;
        private readonly ObservableCollection<MediaFileItem> _subtitleFiles;
        private readonly ObservableCollection<SubtitleCue> _subtitleCues;
        private readonly ObservableCollection<BatchTaskItem> _batchTasks;
        private readonly ObservableCollection<BatchQueueItem> _batchQueueItems;
        private int _batchConcurrencyLimit = 5;
        private bool _isBatchRunning;
        private CancellationTokenSource? _batchCts;
        private string _batchStatusMessage = "";
        private string _batchQueueStatusText = "";
        private Task? _batchQueueRunnerTask;
        private List<ReviewSheetPreset> _batchReviewSheetSnapshot = new();
        private string? _batchLogFilePath;
        private readonly object _batchLogLock = new();
        private MediaFileItem? _selectedAudioFile;
        private MediaFileItem? _selectedSubtitleFile;
        private SubtitleCue? _selectedSubtitleCue;
        private double _subtitleListHeight;

        private WaveOutEvent? _playbackOutput;
        private AudioFileReader? _playbackReader;
        private readonly DispatcherTimer _playbackTimer;
        private TimeSpan _playbackPosition = TimeSpan.Zero;
        private TimeSpan _playbackDuration = TimeSpan.Zero;
        private double _playbackProgress;
        private bool _isPlaybackReady;
        private bool _isPlaying;
        private bool _suppressSeek;
        private bool _suppressSubtitleSeek;
        private const double SubtitleCueRowHeight = 56;
        private bool _isFloatingSubtitleOpen;

        private readonly AzureSubscriptionValidator _subscriptionValidator = new();
        private SubscriptionValidationState _subscriptionValidationState = SubscriptionValidationState.Unknown;
        private string _subscriptionValidationStatusMessage = "";
        private CancellationTokenSource? _subscriptionValidationCts;
        private int _subscriptionValidationVersion;
        private bool _subscriptionLampBlinkOn = true;
        private readonly DispatcherTimer _subscriptionLampTimer;
        private bool _reviewLampBlinkOn = true;

        private FloatingSubtitleManager? _floatingSubtitleManager;

        private readonly AiInsightService _aiInsightService = new();
        private string _insightMarkdown = "";
        private string _insightUserInput = "";
        private bool _isInsightLoading = false;
        private CancellationTokenSource? _insightCts;

        private bool _isAutoInsightEnabled;
        private int _autoInsightIntervalSeconds = 30;
        private int _autoInsightModeIndex; // 0=定时, 1=新数据触发
        private string _autoInsightPrompt = "请对以上翻译记录进行会议摘要。总结会议的主要议题、关键讨论内容和结论。";
        private DispatcherTimer? _autoInsightTimer;
        private int _lastAutoInsightHistoryCount;

        private bool _isSpeechSubtitleGenerating;
        private string _speechSubtitleStatusMessage = "";
        private CancellationTokenSource? _speechSubtitleCts;

        private int _uiModeIndex;

        private readonly ObservableCollection<ReviewSheetState> _reviewSheets = new();
        private ReviewSheetState? _selectedReviewSheet;

        private static readonly HttpClient SpeechBatchHttpClient = new();

        private readonly string[] _sourceLanguages = { "zh-CN", "en-US", "ja-JP", "ko-KR", "fr-FR", "de-DE", "es-ES" };
        private readonly string[] _targetLanguages = { "en", "zh-CN", "ja-JP", "ko-KR", "fr-FR", "de-DE", "es-ES" };        public MainWindowViewModel()
        {
            _configService = new ConfigurationService();
            _config = new AzureSpeechConfig();
            _history = new ObservableCollection<TranslationItem>();
            _subscriptionNames = new ObservableCollection<string>();
            _audioDevices = new ObservableCollection<AudioDeviceInfo>();
            _audioLevelHistory = new ObservableCollection<double>(Enumerable.Repeat(0d, 24));
            _audioFiles = new ObservableCollection<MediaFileItem>();
            _subtitleFiles = new ObservableCollection<MediaFileItem>();
            _subtitleCues = new ObservableCollection<SubtitleCue>();
            _batchTasks = new ObservableCollection<BatchTaskItem>();
            _batchQueueItems = new ObservableCollection<BatchQueueItem>();
            _batchQueueStatusText = "队列为空";
            _subtitleCues.CollectionChanged += (_, _) => UpdateSubtitleListHeight();
            _batchTasks.CollectionChanged += (_, _) =>
            {
                if (ClearBatchTasksCommand is RelayCommand cmd)
                {
                    cmd.RaiseCanExecuteChanged();
                }
                if (StartBatchCommand is RelayCommand startCmd)
                {
                    startCmd.RaiseCanExecuteChanged();
                }
                OnPropertyChanged(nameof(BatchStartButtonText));
            };
            _batchQueueItems.CollectionChanged += (_, _) => UpdateBatchQueueStatusText();

            _playbackTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
            {
                UpdatePlaybackProgressFromReader();
            });

            _subscriptionLampTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) =>
            {
                if (_subscriptionValidationState == SubscriptionValidationState.Validating)
                {
                    _subscriptionLampBlinkOn = !_subscriptionLampBlinkOn;
                    OnPropertyChanged(nameof(SubscriptionLampOpacity));
                }
                if (IsReviewSummaryLoading)
                {
                    _reviewLampBlinkOn = !_reviewLampBlinkOn;
                    OnPropertyChanged(nameof(ReviewSummaryLampOpacity));
                }
                else if (ReviewSummaryLampOpacity != 1)
                {
                    _reviewLampBlinkOn = true;
                    OnPropertyChanged(nameof(ReviewSummaryLampOpacity));
                }
                else if (SubscriptionLampOpacity != 1)
                {
                    _subscriptionLampBlinkOn = true;
                    OnPropertyChanged(nameof(SubscriptionLampOpacity));
                }
            });
            _subscriptionLampTimer.Start();

            _ = LoadConfigAsync();

            StartTranslationCommand = new RelayCommand(
                execute: _ => StartTranslation(),
                canExecute: _ => !IsTranslating && _config.IsValid()
            );

            StopTranslationCommand = new RelayCommand(
                execute: _ => StopTranslation(),
                canExecute: _ => IsTranslating
            );

            ToggleTranslationCommand = new RelayCommand(
                execute: _ =>
                {
                    if (IsTranslating)
                    {
                        StopTranslation();
                        return;
                    }

                    StartTranslation();
                },
                canExecute: _ => IsTranslating || _config.IsValid()
            );

            ClearHistoryCommand = new RelayCommand(
                execute: _ => ClearHistory(),
                canExecute: _ => History.Count > 0
            );
            ShowConfigCommand = new RelayCommand(
                execute: async _ => await ShowConfig(),
                canExecute: _ => true
            );

            OpenHistoryFolderCommand = new RelayCommand(
                execute: _ => OpenHistoryFolder(),
                canExecute: _ => true
            );              ShowFloatingSubtitlesCommand = new RelayCommand(
                execute: _ => ShowFloatingSubtitles(),
                canExecute: _ => true
            );
            
            ToggleEditorTypeCommand = new RelayCommand(
                execute: _ => ToggleEditorType(),
                canExecute: _ => true
            );

            RefreshAudioDevicesCommand = new RelayCommand(
                execute: _ => RefreshAudioDevices(persistSelection: true),
                canExecute: _ => true
            );

            RefreshAudioLibraryCommand = new RelayCommand(
                execute: _ => RefreshAudioLibrary(),
                canExecute: _ => true
            );

            PlayAudioCommand = new RelayCommand(
                execute: _ => PlayAudio(),
                canExecute: _ => IsPlayEnabled
            );

            PauseAudioCommand = new RelayCommand(
                execute: _ => PauseAudio(),
                canExecute: _ => IsPauseEnabled
            );

            StopAudioCommand = new RelayCommand(
                execute: _ => StopAudio(),
                canExecute: _ => IsStopEnabled
            );

            OpenAzureSpeechPortalCommand = new RelayCommand(
                execute: _ => OpenUrl("https://portal.azure.com/#view/Microsoft_Azure_ProjectOxford/CognitiveServicesHub/~/SpeechServices"),
                canExecute: _ => true
            );

            OpenProjectGitHubCommand = new RelayCommand(
                execute: _ => OpenUrl("https://github.com/kukisama/TranslationToolUI"),
                canExecute: _ => true
            );

            ShowAboutCommand = new RelayCommand(
                execute: async _ => await ShowAbout(),
                canExecute: _ => true
            );

            ShowHelpCommand = new RelayCommand(
                execute: async _ => await ShowHelp(),
                canExecute: _ => true
            );

            SendInsightCommand = new RelayCommand(
                execute: _ => SendInsight(InsightUserInput),
                canExecute: _ => !IsInsightLoading && IsAiConfigured
                                 && !string.IsNullOrWhiteSpace(InsightUserInput)
            );

            StopInsightCommand = new RelayCommand(
                execute: _ => StopInsight(),
                canExecute: _ => IsInsightLoading
            );

            ClearInsightCommand = new RelayCommand(
                execute: _ => { InsightMarkdown = ""; InsightUserInput = ""; },
                canExecute: _ => true
            );

            ShowAiConfigCommand = new RelayCommand(
                execute: async _ => await ShowAiConfig(),
                canExecute: _ => true
            );

            SendPresetInsightCommand = new RelayCommand(
                execute: param => SendInsight(param?.ToString() ?? ""),
                canExecute: _ => !IsInsightLoading && IsAiConfigured
            );

            ToggleAutoInsightCommand = new RelayCommand(
                execute: _ => ToggleAutoInsight(),
                canExecute: _ => IsAiConfigured
            );

            GenerateReviewSummaryCommand = new RelayCommand(
                execute: _ => GenerateReviewSummary(),
                canExecute: _ => CanGenerateReviewSummary()
            );

            GenerateAllReviewSheetsCommand = new RelayCommand(
                execute: _ => GenerateAllReviewSheets(),
                canExecute: _ => CanGenerateAllReviewSheets()
            );

            ReviewMarkdownLinkCommand = new RelayCommand(
                execute: param => OnReviewMarkdownLink(param)
            );

            LoadBatchTasksCommand = new RelayCommand(
                execute: _ => LoadBatchTasksFromLibrary()
            );

            ClearBatchTasksCommand = new RelayCommand(
                execute: _ => ClearBatchTasks(),
                canExecute: _ => BatchTasks.Count > 0
            );

            StartBatchCommand = new RelayCommand(
                execute: _ => StartBatchProcessing(),
                canExecute: _ => CanStartBatchProcessing()
            );

            StopBatchCommand = new RelayCommand(
                execute: _ => StopBatchProcessing(),
                canExecute: _ => IsBatchRunning
            );

            CancelBatchQueueItemCommand = new RelayCommand(
                execute: param => CancelBatchQueueItem(param as BatchQueueItem)
            );

            GenerateSpeechSubtitleCommand = new RelayCommand(
                execute: _ => GenerateSpeechSubtitle(),
                canExecute: _ => CanGenerateSpeechSubtitle()
            );

            CancelSpeechSubtitleCommand = new RelayCommand(
                execute: _ => CancelSpeechSubtitle(),
                canExecute: _ => IsSpeechSubtitleGenerating
            );

            GenerateBatchSpeechSubtitleCommand = new RelayCommand(
                execute: _ => GenerateBatchSpeechSubtitle(),
                canExecute: _ => CanGenerateBatchSpeechSubtitle()
            );
        }

        public ICommand ToggleTranslationCommand { get; }

        public ICommand RefreshAudioDevicesCommand { get; }
        public ICommand RefreshAudioLibraryCommand { get; }
        public ICommand PlayAudioCommand { get; }
        public ICommand PauseAudioCommand { get; }
        public ICommand StopAudioCommand { get; }

        public void SetMainWindow(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }

        private async Task LoadConfigAsync()
        {
            try
            {
                _config = await _configService.LoadConfigAsync();

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateSubscriptionNames();

                    if (_config.Subscriptions.Count > 0 && _config.ActiveSubscriptionIndex >= _config.Subscriptions.Count)
                    {
                        _config.ActiveSubscriptionIndex = _config.Subscriptions.Count - 1;
                    }
                    else if (_config.Subscriptions.Count == 0 && _config.ActiveSubscriptionIndex != -1)
                    {
                        _config.ActiveSubscriptionIndex = -1;
                    }

                    _sourceLanguage = _config.SourceLanguage;
                    _targetLanguage = _config.TargetLanguage;

                    _activeSubscriptionIndex = _config.ActiveSubscriptionIndex;

                    _audioSourceModeIndex = AudioSourceModeToIndex(_config.AudioSourceMode);

                    OnPropertyChanged(nameof(Config));
                    OnPropertyChanged(nameof(SubscriptionNames));
                    OnPropertyChanged(nameof(SourceLanguage));
                    OnPropertyChanged(nameof(TargetLanguage));
                    OnPropertyChanged(nameof(SourceLanguageIndex));
                    OnPropertyChanged(nameof(TargetLanguageIndex));
                    OnPropertyChanged(nameof(ActiveSubscriptionStatus));
                    OnPropertyChanged(nameof(AudioSourceModeIndex));
                    OnPropertyChanged(nameof(AudioDevices));
                    OnPropertyChanged(nameof(SelectedAudioDevice));
                    OnPropertyChanged(nameof(IsAudioSourceSelectionEnabled));
                    OnPropertyChanged(nameof(IsAudioDeviceSelectionEnabled));
                    OnPropertyChanged(nameof(IsAudioDeviceRefreshEnabled));

                    ForceUpdateComboBoxSelection();

                    RefreshAudioDevices(persistSelection: false);
                    RefreshAudioLibrary();

                    ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ToggleTranslationCommand).RaiseCanExecuteChanged();

                    OnPropertyChanged(nameof(IsAiConfigured));
                    OnPropertyChanged(nameof(InsightPresetButtons));
                    NormalizeSpeechSubtitleOption();
                    OnPropertyChanged(nameof(IsSpeechSubtitleOptionEnabled));
                    OnPropertyChanged(nameof(UseSpeechSubtitleForReview));
                    OnPropertyChanged(nameof(SpeechSubtitleOptionStatusText));
                    OnPropertyChanged(nameof(BatchStartButtonText));
                    RebuildReviewSheets();
                    ((RelayCommand)SendInsightCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)SendPresetInsightCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ToggleAutoInsightCommand).RaiseCanExecuteChanged();
                    if (GenerateSpeechSubtitleCommand is RelayCommand speechCmd)
                    {
                        speechCmd.RaiseCanExecuteChanged();
                    }
                    if (GenerateBatchSpeechSubtitleCommand is RelayCommand batchCmd)
                    {
                        batchCmd.RaiseCanExecuteChanged();
                    }

                    StatusMessage = $"配置已加载，文件位置: {_configService.GetConfigFilePath()}";

                    TriggerSubscriptionValidation();
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"加载配置失败: {ex.Message}";
                });
            }
        }

        private static int AudioSourceModeToIndex(AudioSourceMode mode)
        {
            return mode switch
            {
                AudioSourceMode.DefaultMic => 0,
                AudioSourceMode.CaptureDevice => 1,
                AudioSourceMode.Loopback => 2,
                _ => 0
            };
        }

        private static AudioSourceMode IndexToAudioSourceMode(int index)
        {
            return index switch
            {
                1 => AudioSourceMode.CaptureDevice,
                2 => AudioSourceMode.Loopback,
                _ => AudioSourceMode.DefaultMic
            };
        }

        public int AudioSourceModeIndex
        {
            get => _audioSourceModeIndex;
            set
            {
                if (!SetProperty(ref _audioSourceModeIndex, value))
                {
                    return;
                }

                if (!OperatingSystem.IsWindows())
                {
                    if (_config.AudioSourceMode != AudioSourceMode.DefaultMic)
                    {
                        _config.AudioSourceMode = AudioSourceMode.DefaultMic;
                        _audioSourceModeIndex = 0;
                        OnPropertyChanged(nameof(AudioSourceModeIndex));
                    }

                    RefreshAudioDevices(persistSelection: false);
                    return;
                }

                var mode = IndexToAudioSourceMode(value);
                if (_config.AudioSourceMode != mode)
                {
                    _config.AudioSourceMode = mode;

                    if (_translationService != null)
                    {
                        _translationService.UpdateConfig(_config);
                    }

                    _ = Task.Run(async () => await _configService.SaveConfigAsync(_config));
                }

                RefreshAudioDevices(persistSelection: true);
            }
        }

        public AzureSpeechConfig Config
        {
            get => _config;
            set => SetProperty(ref _config, value);
        }        

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<TranslationItem> History
        {
            get => _history;
            set => SetProperty(ref _history, value);
        }

        public ObservableCollection<string> SubscriptionNames
        {
            get => _subscriptionNames;
            set => SetProperty(ref _subscriptionNames, value);
        }

        public int ActiveSubscriptionIndex
        {
            get
            {
                return _activeSubscriptionIndex;
            }
            set
            {
                if (value >= 0 && value < _config.Subscriptions.Count)
                {
                    if (SetProperty(ref _activeSubscriptionIndex, value))
                    {
                        _config.ActiveSubscriptionIndex = value;
                        OnPropertyChanged(nameof(ActiveSubscriptionStatus));
                        ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
                        ((RelayCommand)ToggleTranslationCommand).RaiseCanExecuteChanged();

                        if (_translationService != null)
                        {
                            _translationService.UpdateConfig(_config);
                        }

                        TriggerSubscriptionValidation();

                        _ = Task.Run(async () => await _configService.SaveConfigAsync(_config));
                    }
                }
                else if (value == -1)
                {
                    if (SetProperty(ref _activeSubscriptionIndex, value))
                    {
                        _config.ActiveSubscriptionIndex = value;
                        OnPropertyChanged(nameof(ActiveSubscriptionStatus));
                        ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
                        TriggerSubscriptionValidation();
                        _ = Task.Run(async () => await _configService.SaveConfigAsync(_config));
                    }
                }
            }
        }

        public string SourceLanguage
        {
            get => _sourceLanguage;
            set
            {
                if (SetProperty(ref _sourceLanguage, value))
                {
                    _config.SourceLanguage = value;
                    OnPropertyChanged(nameof(SourceLanguageIndex));
                    ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ToggleTranslationCommand).RaiseCanExecuteChanged();

                    if (_translationService != null)
                    {
                        _translationService.UpdateConfig(_config);
                    }

                    _ = Task.Run(async () => await _configService.SaveConfigAsync(_config));
                }
            }
        }

        public string TargetLanguage
        {
            get => _targetLanguage;
            set
            {
                if (SetProperty(ref _targetLanguage, value))
                {
                    _config.TargetLanguage = value;
                    OnPropertyChanged(nameof(TargetLanguageIndex));
                    ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
                        ((RelayCommand)ToggleTranslationCommand).RaiseCanExecuteChanged();

                    if (_translationService != null)
                    {
                        _translationService.UpdateConfig(_config);
                    }

                    _ = Task.Run(async () => await _configService.SaveConfigAsync(_config));
                }
            }
        }

        public string ActiveSubscriptionStatus
        {
            get
            {
                var subscription = _config.GetActiveSubscription();
                if (subscription != null && subscription.IsValid())
                {
                    return $"{subscription.Name} ({subscription.ServiceRegion})";
                }
                return "未配置";
            }
        }

        public bool IsConfigurationEnabled
        {
            get => _isConfigurationEnabled;
            set => SetProperty(ref _isConfigurationEnabled, value);
        }

        public int UiModeIndex
        {
            get => _uiModeIndex;
            set
            {
                if (SetProperty(ref _uiModeIndex, value))
                {
                    OnPropertyChanged(nameof(IsLiveMode));
                    OnPropertyChanged(nameof(IsReviewMode));
                    OnPropertyChanged(nameof(IsLiveModeSelected));
                    OnPropertyChanged(nameof(IsReviewModeSelected));
                }
            }
        }

        public bool IsLiveMode => UiModeIndex == 0;

        public bool IsReviewMode => UiModeIndex == 1;

        public bool IsLiveModeSelected
        {
            get => UiModeIndex == 0;
            set
            {
                if (value)
                {
                    UiModeIndex = 0;
                }
            }
        }

        public bool IsReviewModeSelected
        {
            get => UiModeIndex == 1;
            set
            {
                if (value)
                {
                    UiModeIndex = 1;
                }
            }
        }

        public TextEditorType EditorType
        {
            get => _editorType;
            set => SetProperty(ref _editorType, value);
        }

        public int SourceLanguageIndex
        {
            get
            {
                var index = Array.IndexOf(_sourceLanguages, _sourceLanguage);
                return index >= 0 ? index : 0;
            }
            set
            {
                if (value >= 0 && value < _sourceLanguages.Length)
                {
                    SourceLanguage = _sourceLanguages[value];
                }
            }
        }

        public int TargetLanguageIndex
        {
            get
            {
                var index = Array.IndexOf(_targetLanguages, _targetLanguage);
                return index >= 0 ? index : 0;
            }
            set
            {
                if (value >= 0 && value < _targetLanguages.Length)
                {
                    TargetLanguage = _targetLanguages[value];
                }
            }        
        }

        public ICommand StartTranslationCommand { get; }
        public ICommand StopTranslationCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand ShowConfigCommand { get; }
        public ICommand OpenHistoryFolderCommand { get; }
        public ICommand ShowFloatingSubtitlesCommand { get; }
        public ICommand ToggleEditorTypeCommand { get; }
        public ICommand OpenAzureSpeechPortalCommand { get; }
        public ICommand OpenProjectGitHubCommand { get; }
        public ICommand ShowAboutCommand { get; }
        public ICommand ShowHelpCommand { get; }
        public ICommand SendInsightCommand { get; }
        public ICommand StopInsightCommand { get; }
        public ICommand ClearInsightCommand { get; }
        public ICommand ShowAiConfigCommand { get; }
        public ICommand SendPresetInsightCommand { get; }
        public ICommand ToggleAutoInsightCommand { get; }
        public ICommand GenerateReviewSummaryCommand { get; }
        public ICommand GenerateAllReviewSheetsCommand { get; }
        public ICommand ReviewMarkdownLinkCommand { get; }
        public ICommand LoadBatchTasksCommand { get; }
        public ICommand ClearBatchTasksCommand { get; }
        public ICommand StartBatchCommand { get; }
        public ICommand StopBatchCommand { get; }
        public ICommand CancelBatchQueueItemCommand { get; }
        public ICommand GenerateSpeechSubtitleCommand { get; } = null!;
        public ICommand CancelSpeechSubtitleCommand { get; } = null!;
        public ICommand GenerateBatchSpeechSubtitleCommand { get; } = null!;

        private async Task ShowConfig()
        {
            if (_mainWindow == null)
                return;

            var configView = new ConfigCenterView(_config);

            configView.ConfigurationUpdated += OnConfigurationUpdated;

            var result = await configView.ShowDialog<bool>(_mainWindow);

            configView.ConfigurationUpdated -= OnConfigurationUpdated;

            if (result)
            {
                _config = configView.Config;

                try
                {
                    await _configService.SaveConfigAsync(_config);
                    StatusMessage = $"配置已保存到: {_configService.GetConfigFilePath()}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"保存配置失败: {ex.Message}";
                }

                if (_translationService != null)
                {
                    _translationService.UpdateConfig(_config);
                }

                OnPropertyChanged(nameof(IsAiConfigured));
                OnPropertyChanged(nameof(InsightPresetButtons));
                RebuildReviewSheets();
                ((RelayCommand)SendInsightCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendPresetInsightCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ToggleAutoInsightCommand).RaiseCanExecuteChanged();
                ((RelayCommand)GenerateReviewSummaryCommand).RaiseCanExecuteChanged();
                ((RelayCommand)GenerateAllReviewSheetsCommand).RaiseCanExecuteChanged();
                ((RelayCommand)StartBatchCommand).RaiseCanExecuteChanged();
                ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
            }
        }

        private void OpenHistoryFolder()
        {
            try
            {
                var historyDirectory = _config.SessionDirectory;

                if (!System.IO.Directory.Exists(historyDirectory))
                {
                    System.IO.Directory.CreateDirectory(historyDirectory);
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = historyDirectory,
                    UseShellExecute = true
                });

                StatusMessage = $"已打开历史记录文件夹: {historyDirectory}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"打开历史记录文件夹失败: {ex.Message}";
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                StatusMessage = $"已打开链接: {url}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"打开链接失败: {ex.Message}";
            }
        }

        private async Task ShowAbout()
        {
            try
            {
                var owner = _mainWindow
                    ?? (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                var about = new AboutView();
                if (owner != null)
                {
                    await about.ShowDialog(owner);
                }
                else
                {
                    about.Show();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                Console.Error.WriteLine(ex);
                StatusMessage = $"打开关于失败: {ex.GetType().Name}: {ex.Message}";
            }
        }

        private async Task ShowHelp()
        {
            try
            {
                var owner = _mainWindow
                    ?? (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                var help = new HelpView();
                if (owner != null)
                {
                    await help.ShowDialog(owner);
                }
                else
                {
                    help.Show();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                Console.Error.WriteLine(ex);
                StatusMessage = $"打开说明失败: {ex.GetType().Name}: {ex.Message}";
            }
        }

        private void OnConfigurationUpdated(object? sender, AzureSpeechConfig updatedConfig)
        {
            _config = updatedConfig;

            UpdateSubscriptionNames();

            if (_config.Subscriptions.Count > 0 && _config.ActiveSubscriptionIndex >= _config.Subscriptions.Count)
            {
                _config.ActiveSubscriptionIndex = _config.Subscriptions.Count - 1;
            }
            else if (_config.Subscriptions.Count == 0 && _config.ActiveSubscriptionIndex != -1)
            {
                _config.ActiveSubscriptionIndex = -1;
            }

            _activeSubscriptionIndex = _config.ActiveSubscriptionIndex;

            _audioSourceModeIndex = AudioSourceModeToIndex(_config.AudioSourceMode);
            RefreshAudioDevices(persistSelection: false);

            OnPropertyChanged(nameof(SubscriptionNames));
            OnPropertyChanged(nameof(ActiveSubscriptionStatus));
            OnPropertyChanged(nameof(AudioSourceModeIndex));
            OnPropertyChanged(nameof(AudioDevices));
            OnPropertyChanged(nameof(SelectedAudioDevice));
            OnPropertyChanged(nameof(IsAudioSourceSelectionEnabled));
            OnPropertyChanged(nameof(IsAudioDeviceSelectionEnabled));
            OnPropertyChanged(nameof(IsAudioDeviceRefreshEnabled));
            OnPropertyChanged(nameof(IsAiConfigured));
            NormalizeSpeechSubtitleOption();
            OnPropertyChanged(nameof(IsSpeechSubtitleOptionEnabled));
            OnPropertyChanged(nameof(UseSpeechSubtitleForReview));
            OnPropertyChanged(nameof(SpeechSubtitleOptionStatusText));
            OnPropertyChanged(nameof(BatchStartButtonText));
            RebuildReviewSheets();

            ForceUpdateComboBoxSelection();
            ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SendInsightCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SendPresetInsightCommand).RaiseCanExecuteChanged();
            ((RelayCommand)GenerateReviewSummaryCommand).RaiseCanExecuteChanged();
            ((RelayCommand)GenerateAllReviewSheetsCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StartBatchCommand).RaiseCanExecuteChanged();
            if (GenerateSpeechSubtitleCommand is RelayCommand speechCmd)
            {
                speechCmd.RaiseCanExecuteChanged();
            }
            if (GenerateBatchSpeechSubtitleCommand is RelayCommand batchCmd)
            {
                batchCmd.RaiseCanExecuteChanged();
            }

            TriggerSubscriptionValidation();

            _ = Task.Run(async () =>
            {
                try
                {
                    await _configService.SaveConfigAsync(_config);
                }
                catch
                {

                }
            });
        }

        public void ForceUpdateComboBoxSelection()
        {
            if (_mainWindow == null) return;

            var comboBox = _mainWindow.FindControl<ComboBox>("SubscriptionComboBox");
            if (comboBox != null && _activeSubscriptionIndex >= 0 && _activeSubscriptionIndex < _subscriptionNames.Count)
            {
                comboBox.SelectedIndex = -1;
                comboBox.SelectedIndex = _activeSubscriptionIndex;
            }
        }

        public void Dispose()
        {
            if (_translationService != null)
            {
                _translationService.OnRealtimeTranslationReceived -= OnRealtimeTranslationReceived;
                _translationService.OnFinalTranslationReceived -= OnFinalTranslationReceived;
                _translationService.OnStatusChanged -= OnStatusChanged;
                _translationService.OnReconnectTriggered -= OnReconnectTriggered;
                _translationService.OnAudioLevelUpdated -= OnAudioLevelUpdated;
            }

            _floatingSubtitleManager?.Dispose();

            _insightCts?.Cancel();
            _insightCts?.Dispose();
            _autoInsightTimer?.Stop();
        }
    }
}
