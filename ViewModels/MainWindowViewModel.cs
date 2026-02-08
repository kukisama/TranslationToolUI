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

    public class MainWindowViewModel : ViewModelBase
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

        public ObservableCollection<AudioDeviceInfo> AudioDevices => _audioDevices;

        public ObservableCollection<MediaFileItem> AudioFiles => _audioFiles;

        public ObservableCollection<MediaFileItem> SubtitleFiles => _subtitleFiles;

        public ObservableCollection<SubtitleCue> SubtitleCues => _subtitleCues;

        public ObservableCollection<BatchTaskItem> BatchTasks => _batchTasks;

        public ObservableCollection<BatchQueueItem> BatchQueueItems => _batchQueueItems;

        public int BatchConcurrencyLimit
        {
            get => _batchConcurrencyLimit;
            set => SetProperty(ref _batchConcurrencyLimit, Math.Clamp(value, 1, 10));
        }

        public bool IsBatchRunning
        {
            get => _isBatchRunning;
            private set
            {
                if (SetProperty(ref _isBatchRunning, value))
                {
                    if (StartBatchCommand is RelayCommand startCmd)
                    {
                        startCmd.RaiseCanExecuteChanged();
                    }

                    if (StopBatchCommand is RelayCommand stopCmd)
                    {
                        stopCmd.RaiseCanExecuteChanged();
                    }
                }
            }
        }

        public string BatchStatusMessage
        {
            get => _batchStatusMessage;
            set => SetProperty(ref _batchStatusMessage, value);
        }

        public string BatchQueueStatusText
        {
            get => _batchQueueStatusText;
            private set => SetProperty(ref _batchQueueStatusText, value);
        }

        public bool IsSpeechSubtitleGenerating
        {
            get => _isSpeechSubtitleGenerating;
            private set
            {
                if (SetProperty(ref _isSpeechSubtitleGenerating, value))
                {
                    if (GenerateSpeechSubtitleCommand is RelayCommand cmd)
                    {
                        cmd.RaiseCanExecuteChanged();
                    }
                    if (GenerateBatchSpeechSubtitleCommand is RelayCommand batchCmd)
                    {
                        batchCmd.RaiseCanExecuteChanged();
                    }
                    if (CancelSpeechSubtitleCommand is RelayCommand cancelCmd)
                    {
                        cancelCmd.RaiseCanExecuteChanged();
                    }
                }
            }
        }

        public string SpeechSubtitleStatusMessage
        {
            get => _speechSubtitleStatusMessage;
            private set => SetProperty(ref _speechSubtitleStatusMessage, value);
        }

        public IBrush ReviewSummaryLampFill
        {
            get
            {
                if (IsReviewSummaryLoading)
                {
                    return Brushes.Orange;
                }

                return string.IsNullOrWhiteSpace(ReviewSummaryMarkdown)
                    ? Brushes.Gray
                    : Brushes.LimeGreen;
            }
        }

        public IBrush ReviewSummaryLampStroke
        {
            get
            {
                if (IsReviewSummaryLoading)
                {
                    return Brushes.DarkOrange;
                }

                return string.IsNullOrWhiteSpace(ReviewSummaryMarkdown)
                    ? Brushes.DarkGray
                    : Brushes.Green;
            }
        }

        public double ReviewSummaryLampOpacity => IsReviewSummaryLoading
            ? (_reviewLampBlinkOn ? 1.0 : 0.35)
            : 1.0;

        public double SubtitleListHeight
        {
            get => _subtitleListHeight;
            private set => SetProperty(ref _subtitleListHeight, value);
        }

        public ObservableCollection<double> AudioLevelHistory => _audioLevelHistory;

        public double AudioLevel
        {
            get => _audioLevel;
            private set => SetProperty(ref _audioLevel, value);
        }

        public MediaFileItem? SelectedAudioFile
        {
            get => _selectedAudioFile;
            set
            {
                if (!SetProperty(ref _selectedAudioFile, value))
                {
                    return;
                }

                CancelAllReviewSheetGeneration();
                LoadSubtitleFilesForAudio(value);
                LoadAudioForPlayback(value);
                LoadReviewSheetForAudio(value, SelectedReviewSheet);
                ((RelayCommand)GenerateReviewSummaryCommand).RaiseCanExecuteChanged();
                ((RelayCommand)GenerateAllReviewSheetsCommand).RaiseCanExecuteChanged();
                if (GenerateSpeechSubtitleCommand is RelayCommand speechCmd)
                {
                    speechCmd.RaiseCanExecuteChanged();
                }
                if (GenerateBatchSpeechSubtitleCommand is RelayCommand batchCmd)
                {
                    batchCmd.RaiseCanExecuteChanged();
                }
            }
        }

        public MediaFileItem? SelectedSubtitleFile
        {
            get => _selectedSubtitleFile;
            set
            {
                if (!SetProperty(ref _selectedSubtitleFile, value))
                {
                    return;
                }

                LoadSubtitleCues(value);
            }
        }

        public SubtitleCue? SelectedSubtitleCue
        {
            get => _selectedSubtitleCue;
            set
            {
                if (!SetProperty(ref _selectedSubtitleCue, value))
                {
                    return;
                }

                if (_suppressSubtitleSeek)
                {
                    return;
                }

                if (value != null)
                {
                    SeekToTime(value.Start);
                }
            }
        }

        public bool IsPlayEnabled => _isPlaybackReady && !_isPlaying;

        public bool IsPauseEnabled => _isPlaybackReady && _isPlaying;

        public bool IsStopEnabled => _isPlaybackReady;

        public string PlaybackTimeText => $"{FormatTime(_playbackPosition)} / {FormatTime(_playbackDuration)}";

        public double PlaybackProgress
        {
            get => _playbackProgress;
            set
            {
                if (!SetProperty(ref _playbackProgress, value))
                {
                    return;
                }

                if (!_suppressSeek)
                {
                    SeekToProgress(value);
                }
            }
        }

        public AudioDeviceInfo? SelectedAudioDevice
        {
            get => _selectedAudioDevice;
            set => SetSelectedAudioDevice(value, persistSelection: true);
        }

        public bool IsAudioSourceSelectionEnabled => OperatingSystem.IsWindows() && !IsTranslating;

        public bool IsAudioDeviceSelectionEnabled
        {
            get => _isAudioDeviceSelectionEnabled;
            private set => SetProperty(ref _isAudioDeviceSelectionEnabled, value);
        }

        public bool IsAudioDeviceRefreshEnabled
        {
            get => _isAudioDeviceRefreshEnabled;
            private set => SetProperty(ref _isAudioDeviceRefreshEnabled, value);
        }

        private void SetSelectedAudioDevice(AudioDeviceInfo? device, bool persistSelection)
        {
            if (!SetProperty(ref _selectedAudioDevice, device))
            {
                return;
            }

            var deviceId = device?.DeviceId ?? "";
            if (_config.SelectedAudioDeviceId != deviceId)
            {
                _config.SelectedAudioDeviceId = deviceId;

                if (_translationService != null)
                {
                    _translationService.UpdateConfig(_config);
                }

                if (persistSelection)
                {
                    _ = Task.Run(async () => await _configService.SaveConfigAsync(_config));
                }
            }
        }

        private void RefreshAudioDevices(bool persistSelection)
        {
            if (IsTranslating)
            {
                IsAudioDeviceSelectionEnabled = false;
                IsAudioDeviceRefreshEnabled = false;
                return;
            }

            _audioDevices.Clear();

            if (!OperatingSystem.IsWindows())
            {
                IsAudioDeviceSelectionEnabled = false;
                IsAudioDeviceRefreshEnabled = false;
                SetSelectedAudioDevice(null, persistSelection: false);
                return;
            }

            var mode = _config.AudioSourceMode;
            if (mode == AudioSourceMode.DefaultMic)
            {
                IsAudioDeviceSelectionEnabled = false;
                IsAudioDeviceRefreshEnabled = false;
                SetSelectedAudioDevice(null, persistSelection: false);
                return;
            }

            var deviceType = mode == AudioSourceMode.Loopback ? AudioDeviceType.Render : AudioDeviceType.Capture;
            var devices = AudioDeviceEnumerator.GetActiveDevices(deviceType);
            foreach (var device in devices)
            {
                _audioDevices.Add(device);
            }

            IsAudioDeviceRefreshEnabled = true;
            IsAudioDeviceSelectionEnabled = _audioDevices.Count > 0;

            AudioDeviceInfo? selection = null;
            var targetDeviceId = _config.SelectedAudioDeviceId;

            if (string.IsNullOrWhiteSpace(targetDeviceId))
            {
                targetDeviceId = AudioDeviceEnumerator.GetDefaultDeviceId(deviceType) ?? "";
            }

            if (!string.IsNullOrWhiteSpace(targetDeviceId))
            {
                selection = _audioDevices.FirstOrDefault(d => d.DeviceId == targetDeviceId);
            }

            selection ??= _audioDevices.FirstOrDefault();
            SetSelectedAudioDevice(selection, persistSelection);
        }

        private void RefreshAudioLibrary()
        {
            _audioFiles.Clear();
            _subtitleFiles.Clear();
            _subtitleCues.Clear();
            foreach (var sheet in _reviewSheets)
            {
                sheet.Markdown = "";
                sheet.StatusMessage = "";
            }

            var sessionsPath = PathManager.Instance.SessionsPath;
            if (!Directory.Exists(sessionsPath))
            {
                return;
            }

            var files = Directory.GetFiles(sessionsPath, "*.mp3")
                .Concat(Directory.GetFiles(sessionsPath, "*.wav"))
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path));

            foreach (var file in files)
            {
                _audioFiles.Add(new MediaFileItem
                {
                    Name = Path.GetFileName(file),
                    FullPath = file
                });
            }

            if (_selectedAudioFile != null && !_audioFiles.Any(item => item.FullPath == _selectedAudioFile.FullPath))
            {
                SelectedAudioFile = null;
            }
        }

        private void LoadBatchTasksFromLibrary()
        {
            if (_audioFiles.Count == 0)
            {
                RefreshAudioLibrary();
            }

            var batchSheets = GetBatchReviewSheets();
            _batchTasks.Clear();
            foreach (var audio in _audioFiles)
            {
                var totalSheets = batchSheets.Count;
                var completedSheets = 0;
                foreach (var sheet in batchSheets)
                {
                    if (File.Exists(GetReviewSheetPath(audio.FullPath, sheet.FileTag)))
                    {
                        completedSheets++;
                    }
                }
                var hasAiSummary = totalSheets > 0 && completedSheets >= totalSheets;
                var hasAiSubtitle = HasAiSubtitle(audio.FullPath);
                var subtitlePath = GetPreferredSubtitlePath(audio.FullPath);
                var pendingSheets = Math.Max(totalSheets - completedSheets, 0);

                _batchTasks.Add(new BatchTaskItem
                {
                    FileName = audio.Name,
                    FullPath = audio.FullPath,
                    Status = BatchTaskStatus.Pending,
                    Progress = 0,
                    HasAiSubtitle = hasAiSubtitle,
                    HasAiSummary = hasAiSummary,
                    StatusMessage = string.IsNullOrWhiteSpace(subtitlePath) ? "缺少字幕" : "待处理",
                    ReviewTotal = totalSheets,
                    ReviewCompleted = completedSheets,
                    ReviewFailed = 0,
                    ReviewPending = pendingSheets,
                    ReviewStatusText = totalSheets == 0
                        ? "复盘:未勾选"
                        : $"复盘 {completedSheets}/{totalSheets}"
                });
            }

            StatusMessage = _batchTasks.Count == 0
                ? "未找到可批处理的音频文件"
                : $"已载入 {_batchTasks.Count} 条批处理任务";
            BatchStatusMessage = "";
        }

        private List<ReviewSheetPreset> GetBatchReviewSheets()
        {
            var sheets = _config.AiConfig?.ReviewSheets
                ?.Where(s => s.IncludeInBatch)
                .ToList();

            return sheets ?? new List<ReviewSheetPreset>();
        }

        private void UpdateBatchQueueStatusText()
        {
            var total = _batchQueueItems.Count;
            var completed = _batchQueueItems.Count(item => item.Status == BatchTaskStatus.Completed);
            var running = _batchQueueItems.Count(item => item.Status == BatchTaskStatus.Running);
            var failed = _batchQueueItems.Count(item => item.Status == BatchTaskStatus.Failed);
            var pending = Math.Max(total - completed - running - failed, 0);

            BatchQueueStatusText = total == 0
                ? "队列为空"
                : $"队列 {completed}/{total} 完成，运行 {running}，等待 {pending}，失败 {failed}";
        }

        private void EnqueueReviewSheetsForAudio(MediaFileItem audioFile, IEnumerable<ReviewSheetState> sheets)
        {
            var subtitlePath = GetPreferredSubtitlePath(audioFile.FullPath);
            var hasSubtitle = !string.IsNullOrWhiteSpace(subtitlePath) && File.Exists(subtitlePath);
            if (!hasSubtitle)
            {
                foreach (var sheet in sheets)
                {
                    sheet.StatusMessage = "缺少字幕";
                }
                return;
            }

            foreach (var sheet in sheets)
            {
                var sheetPath = GetReviewSheetPath(audioFile.FullPath, sheet.FileTag);
                if (File.Exists(sheetPath))
                {
                    continue;
                }

                var existsInQueue = _batchQueueItems.Any(item =>
                    string.Equals(item.FullPath, audioFile.FullPath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.SheetTag, sheet.FileTag, StringComparison.OrdinalIgnoreCase)
                    && item.Status is BatchTaskStatus.Pending or BatchTaskStatus.Running);

                if (existsInQueue)
                {
                    continue;
                }

                _batchQueueItems.Add(new BatchQueueItem
                {
                    FileName = audioFile.Name,
                    FullPath = audioFile.FullPath,
                    SheetName = sheet.Name,
                    SheetTag = sheet.FileTag,
                    Prompt = sheet.Prompt,
                    Status = BatchTaskStatus.Pending,
                    Progress = 0,
                    StatusMessage = "待处理"
                });

                sheet.StatusMessage = "已加入队列";
            }

            UpdateBatchQueueStatusText();
        }

        private void StartBatchQueueRunner(string statusMessage)
        {
            if (_batchQueueRunnerTask != null && !_batchQueueRunnerTask.IsCompleted)
            {
                return;
            }

            if (_batchQueueItems.Count == 0)
            {
                return;
            }

            _batchCts?.Cancel();
            _batchCts = new CancellationTokenSource();
            var token = _batchCts.Token;

            IsBatchRunning = true;
            BatchStatusMessage = statusMessage;

            _batchQueueRunnerTask = Task.Run(async () =>
            {
                var running = new List<Task>();
                var cueCache = new Dictionary<string, List<SubtitleCue>>();
                var cueLock = new object();

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        running.RemoveAll(t => t.IsCompleted);

                        while (running.Count < BatchConcurrencyLimit)
                        {
                            var next = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                                _batchQueueItems.FirstOrDefault(item => item.Status == BatchTaskStatus.Pending));

                            if (next == null)
                            {
                                break;
                            }

                            var parent = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                                BatchTasks.FirstOrDefault(x => x.FullPath == next.FullPath));

                            running.Add(Task.Run(() =>
                                ProcessBatchQueueItem(next, parent, token, cueCache, cueLock), token));
                        }

                        if (running.Count == 0)
                        {
                            break;
                        }

                        var finished = await Task.WhenAny(running);
                        running.Remove(finished);
                    }
                }
                catch (OperationCanceledException)
                {
                    // ignore cancellation
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    IsBatchRunning = false;
                    BatchStatusMessage = token.IsCancellationRequested
                        ? "批处理已停止"
                        : "批处理完成";
                });
            }, token);
        }

        private void ClearBatchTasks()
        {
            _batchTasks.Clear();
            StatusMessage = "批处理任务已清空";
            BatchStatusMessage = "";
        }

        private bool CanStartBatchProcessing()
        {
            return !IsBatchRunning && BatchTasks.Count > 0 && IsAiConfigured;
        }

        private void StartBatchProcessing()
        {
            if (_config.AiConfig == null || !_config.AiConfig.IsValid)
            {
                BatchStatusMessage = "AI 配置无效，请先配置 AI 服务";
                return;
            }

            if (BatchTasks.Count == 0)
            {
                BatchStatusMessage = "没有可处理的任务";
                return;
            }

            var batchSheets = GetBatchReviewSheets();
            if (batchSheets.Count == 0)
            {
                BatchStatusMessage = "未勾选批处理复盘模板";
                return;
            }

            _batchCts?.Cancel();
            IsBatchRunning = true;
            BatchStatusMessage = "批处理已开始";

            _batchQueueItems.Clear();
            var batchItemLookup = BatchTasks.ToDictionary(item => item.FullPath, item => item);
            foreach (var batchItem in BatchTasks)
            {
                var subtitlePath = GetPreferredSubtitlePath(batchItem.FullPath);
                var hasSubtitle = !string.IsNullOrWhiteSpace(subtitlePath) && File.Exists(subtitlePath);
                if (!hasSubtitle)
                {
                    UpdateBatchItem(batchItem, BatchTaskStatus.Failed, 0, "缺少字幕");
                    batchItem.ReviewTotal = batchSheets.Count;
                    batchItem.ReviewCompleted = 0;
                    batchItem.ReviewFailed = 0;
                    batchItem.ReviewPending = batchSheets.Count;
                    batchItem.ReviewStatusText = "复盘:缺少字幕";
                    continue;
                }

                var completed = 0;
                foreach (var sheet in batchSheets)
                {
                    if (File.Exists(GetReviewSheetPath(batchItem.FullPath, sheet.FileTag)))
                    {
                        completed++;
                    }
                }

                var pending = Math.Max(batchSheets.Count - completed, 0);
                batchItem.ReviewTotal = batchSheets.Count;
                batchItem.ReviewCompleted = completed;
                batchItem.ReviewFailed = 0;
                batchItem.ReviewPending = pending;
                batchItem.ReviewStatusText = batchSheets.Count == 0
                    ? "复盘:未勾选"
                    : $"复盘 {completed}/{batchSheets.Count}";

                if (pending == 0)
                {
                    UpdateBatchItem(batchItem, BatchTaskStatus.Completed, 1, "已存在");
                    batchItem.HasAiSummary = true;
                    continue;
                }

                foreach (var sheet in batchSheets)
                {
                    if (File.Exists(GetReviewSheetPath(batchItem.FullPath, sheet.FileTag)))
                    {
                        continue;
                    }

                    _batchQueueItems.Add(new BatchQueueItem
                    {
                        FileName = batchItem.FileName,
                        FullPath = batchItem.FullPath,
                        SheetName = sheet.Name,
                        SheetTag = sheet.FileTag,
                        Prompt = sheet.Prompt,
                        Status = BatchTaskStatus.Pending,
                        Progress = 0,
                        StatusMessage = "待处理"
                    });
                }
            }

            StartBatchQueueRunner("批处理已开始");
        }

        private void StopBatchProcessing()
        {
            _batchCts?.Cancel();
            foreach (var item in _batchQueueItems)
            {
                item.Cts?.Cancel();
            }
            BatchStatusMessage = "正在停止批处理...";
        }

        private async Task ProcessBatchQueueItem(
            BatchQueueItem queueItem,
            BatchTaskItem? parentItem,
            CancellationToken token,
            Dictionary<string, List<SubtitleCue>> cueCache,
            object cueLock)
        {
            if (!_batchQueueItems.Contains(queueItem))
            {
                return;
            }

            queueItem.Cts?.Cancel();
            queueItem.Cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var localToken = queueItem.Cts.Token;

            UpdateQueueItem(queueItem, BatchTaskStatus.Running, 0.1, "生成中");
            if (parentItem != null)
            {
                UpdateBatchItem(parentItem, BatchTaskStatus.Running, parentItem.Progress, "生成中");
            }

            var cues = GetBatchCues(queueItem.FullPath, cueCache, cueLock);
            if (cues.Count == 0)
            {
                UpdateQueueItem(queueItem, BatchTaskStatus.Failed, 0, "字幕为空");
                if (parentItem != null)
                {
                    UpdateBatchReviewProgress(parentItem, BatchTaskStatus.Failed);
                }
                return;
            }

            var systemPrompt = "你是一个会议复盘助手。根据字幕内容生成结构化 Markdown 总结。请输出包含关键结论、行动项和风险点，并在引用内容时标注时间戳，格式为 [HH:MM:SS]。";
            var prompt = string.IsNullOrWhiteSpace(queueItem.Prompt)
                ? "请生成复盘总结。"
                : queueItem.Prompt.Trim();
            var userPrompt = $"以下是会议字幕内容:\n\n{FormatSubtitleForSummary(cues)}\n\n---\n\n{prompt}";

            try
            {
                var sb = new System.Text.StringBuilder();
                AiRequestOutcome? outcome = null;
                await _aiInsightService.StreamChatAsync(
                    _config.AiConfig!,
                    systemPrompt,
                    userPrompt,
                    chunk =>
                    {
                        sb.Append(chunk);
                    },
                    localToken,
                    AiChatProfile.Summary,
                    enableReasoning: _config.AiConfig!.SummaryEnableReasoning,
                    onOutcome: o => outcome = o);

                var markdown = InjectTimeLinks(sb.ToString());
                var summaryPath = GetReviewSheetPath(queueItem.FullPath, queueItem.SheetTag);
                File.WriteAllText(summaryPath, markdown);
                var note = "完成";
                if (outcome?.UsedFallback == true)
                {
                    note = "完成(非思考,已降级)";
                }
                else if (outcome?.UsedReasoning == true)
                {
                    note = "完成(思考)";
                }
                else if (_config.AiConfig!.SummaryEnableReasoning)
                {
                    note = "完成(非思考)";
                }

                UpdateQueueItem(queueItem, BatchTaskStatus.Completed, 1, note);
                if (parentItem != null)
                {
                    UpdateBatchReviewProgress(parentItem, BatchTaskStatus.Completed);
                }
            }
            catch (OperationCanceledException)
            {
                UpdateQueueItem(queueItem, BatchTaskStatus.Failed, 0, "已取消");
                if (parentItem != null)
                {
                    UpdateBatchReviewProgress(parentItem, BatchTaskStatus.Failed);
                }
            }
            catch (Exception ex)
            {
                UpdateQueueItem(queueItem, BatchTaskStatus.Failed, 0, $"失败: {ex.Message}");
                if (parentItem != null)
                {
                    UpdateBatchReviewProgress(parentItem, BatchTaskStatus.Failed);
                }
            }
        }

        private List<SubtitleCue> GetBatchCues(
            string audioPath,
            Dictionary<string, List<SubtitleCue>> cueCache,
            object cueLock)
        {
            lock (cueLock)
            {
                if (cueCache.TryGetValue(audioPath, out var cached))
                {
                    return cached;
                }
            }

            var subtitlePath = GetPreferredSubtitlePath(audioPath);
            if (string.IsNullOrWhiteSpace(subtitlePath) || !File.Exists(subtitlePath))
            {
                return new List<SubtitleCue>();
            }

            var cues = ParseSubtitleFileToCues(subtitlePath);
            lock (cueLock)
            {
                cueCache[audioPath] = cues;
            }

            return cues;
        }

        private void UpdateBatchReviewProgress(BatchTaskItem item, BatchTaskStatus sheetStatus)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (sheetStatus == BatchTaskStatus.Completed)
                {
                    item.ReviewCompleted++;
                }
                else if (sheetStatus == BatchTaskStatus.Failed)
                {
                    item.ReviewFailed++;
                }

                item.ReviewPending = Math.Max(item.ReviewTotal - item.ReviewCompleted - item.ReviewFailed, 0);
                if (item.ReviewTotal > 0)
                {
                    item.ReviewStatusText = $"复盘 {item.ReviewCompleted}/{item.ReviewTotal}";
                }
                else
                {
                    item.ReviewStatusText = "复盘:未勾选";
                }

                var progress = item.ReviewTotal == 0
                    ? 1
                    : (double)(item.ReviewCompleted + item.ReviewFailed) / item.ReviewTotal;
                var finished = item.ReviewPending == 0;
                var status = finished
                    ? (item.ReviewFailed > 0 ? BatchTaskStatus.Failed : BatchTaskStatus.Completed)
                    : BatchTaskStatus.Running;
                var statusMessage = finished
                    ? (item.ReviewFailed > 0 ? "完成(含失败)" : "完成")
                    : "生成中";

                item.HasAiSummary = item.ReviewTotal > 0 && item.ReviewCompleted >= item.ReviewTotal;
                UpdateBatchItem(item, status, progress, statusMessage);
            });
        }

        private void UpdateQueueItem(BatchQueueItem item, BatchTaskStatus status, double progress, string message)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!_batchQueueItems.Contains(item))
                {
                    return;
                }

                item.Status = status;
                item.Progress = progress;
                item.StatusMessage = message;
                UpdateBatchQueueStatusText();
            });
        }

        private void CancelBatchQueueItem(BatchQueueItem? item)
        {
            if (item == null)
            {
                return;
            }

            item.Cts?.Cancel();
            UpdateQueueItem(item, BatchTaskStatus.Failed, item.Progress, "已取消");

            var parent = BatchTasks.FirstOrDefault(x => x.FullPath == item.FullPath);
            if (parent != null)
            {
                UpdateBatchReviewProgress(parent, BatchTaskStatus.Failed);
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _batchQueueItems.Remove(item);
            });
        }

        private void UpdateBatchItem(BatchTaskItem item, BatchTaskStatus status, double progress, string message)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                item.Status = status;
                item.Progress = progress;
                item.StatusMessage = message;
            });
        }

        private static string? GetPreferredSubtitlePath(string audioFilePath)
        {
            var directory = Path.GetDirectoryName(audioFilePath) ?? PathManager.Instance.SessionsPath;
            var baseName = Path.GetFileNameWithoutExtension(audioFilePath);

            var candidates = new[]
            {
                Path.Combine(directory, baseName + ".speech.vtt"),
                Path.Combine(directory, baseName + ".ai.srt"),
                Path.Combine(directory, baseName + ".ai.vtt"),
                Path.Combine(directory, baseName + ".srt"),
                Path.Combine(directory, baseName + ".vtt")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static bool HasAiSubtitle(string audioFilePath)
        {
            var directory = Path.GetDirectoryName(audioFilePath) ?? PathManager.Instance.SessionsPath;
            var baseName = Path.GetFileNameWithoutExtension(audioFilePath);
            var aiSrt = Path.Combine(directory, baseName + ".ai.srt");
            var aiVtt = Path.Combine(directory, baseName + ".ai.vtt");
            return File.Exists(aiSrt) || File.Exists(aiVtt);
        }

        private void RebuildReviewSheets()
        {
            var currentTag = SelectedReviewSheet?.FileTag;
            _reviewSheets.Clear();

            var presets = _config.AiConfig?.ReviewSheets;
            if (presets == null || presets.Count == 0)
            {
                presets = new AiConfig().ReviewSheets;
            }

            foreach (var preset in presets)
            {
                _reviewSheets.Add(ReviewSheetState.FromPreset(preset));
            }

            SelectedReviewSheet = _reviewSheets.FirstOrDefault(s => s.FileTag == currentTag)
                                   ?? _reviewSheets.FirstOrDefault();
        }

        private ReviewSheetState? GetPrimaryReviewSheet()
        {
            return _reviewSheets.FirstOrDefault(s => string.Equals(s.FileTag, "summary", StringComparison.OrdinalIgnoreCase))
                   ?? _reviewSheets.FirstOrDefault();
        }

        private void OnSelectedReviewSheetPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ReviewSummaryMarkdown));
            OnPropertyChanged(nameof(ReviewSummaryStatusMessage));
            OnPropertyChanged(nameof(IsReviewSummaryLoading));
            OnPropertyChanged(nameof(IsReviewSummaryEmpty));
            OnPropertyChanged(nameof(ReviewSummaryLampFill));
            OnPropertyChanged(nameof(ReviewSummaryLampStroke));
            OnPropertyChanged(nameof(ReviewSummaryLampOpacity));
                if (GenerateReviewSummaryCommand is RelayCommand genCmd)
                {
                    genCmd.RaiseCanExecuteChanged();
                }
                if (GenerateAllReviewSheetsCommand is RelayCommand allCmd)
                {
                    allCmd.RaiseCanExecuteChanged();
                }
        }

        private void LoadSubtitleFilesForAudio(MediaFileItem? audioFile)
        {
            _subtitleFiles.Clear();
            _subtitleCues.Clear();
            SelectedSubtitleFile = null;

            if (audioFile == null || string.IsNullOrWhiteSpace(audioFile.FullPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(audioFile.FullPath) ?? PathManager.Instance.SessionsPath;
            var baseName = Path.GetFileNameWithoutExtension(audioFile.FullPath);
            var candidateBases = new[] { baseName };

            foreach (var candidate in candidateBases)
            {
                var speechVtt = Path.Combine(directory, candidate + ".speech.vtt");
                if (File.Exists(speechVtt))
                {
                    _subtitleFiles.Add(new MediaFileItem
                    {
                        Name = Path.GetFileName(speechVtt),
                        FullPath = speechVtt
                    });
                }

                var srtPath = Path.Combine(directory, candidate + ".srt");
                if (File.Exists(srtPath))
                {
                    _subtitleFiles.Add(new MediaFileItem
                    {
                        Name = Path.GetFileName(srtPath),
                        FullPath = srtPath
                    });
                }

                var vttPath = Path.Combine(directory, candidate + ".vtt");
                if (File.Exists(vttPath))
                {
                    _subtitleFiles.Add(new MediaFileItem
                    {
                        Name = Path.GetFileName(vttPath),
                        FullPath = vttPath
                    });
                }
            }

            if (_subtitleFiles.Count > 0)
            {
                SelectedSubtitleFile = _subtitleFiles[0];
            }
        }

        private void LoadSubtitleCues(MediaFileItem? subtitleFile)
        {
            _subtitleCues.Clear();
            SelectedSubtitleCue = null;

            if (subtitleFile == null || string.IsNullOrWhiteSpace(subtitleFile.FullPath))
            {
                return;
            }

            if (!File.Exists(subtitleFile.FullPath))
            {
                return;
            }

            var extension = Path.GetExtension(subtitleFile.FullPath).ToLowerInvariant();
            if (extension == ".srt")
            {
                ParseSrt(subtitleFile.FullPath);
            }
            else if (extension == ".vtt")
            {
                ParseVtt(subtitleFile.FullPath);
            }

            UpdateSubtitleListHeight();
            ((RelayCommand)GenerateReviewSummaryCommand).RaiseCanExecuteChanged();
            ((RelayCommand)GenerateAllReviewSheetsCommand).RaiseCanExecuteChanged();
        }

        private void LoadReviewSheetForAudio(MediaFileItem? audioFile, ReviewSheetState? sheet)
        {
            if (sheet != null)
            {
                sheet.Markdown = "";
                sheet.StatusMessage = "";
                sheet.IsLoading = false;
            }

            if (audioFile == null || string.IsNullOrWhiteSpace(audioFile.FullPath) || sheet == null)
            {
                return;
            }

            var sheetPath = GetReviewSheetPath(audioFile.FullPath, sheet.FileTag);
            if (File.Exists(sheetPath))
            {
                sheet.Markdown = InjectTimeLinks(File.ReadAllText(sheetPath));
                sheet.StatusMessage = $"已加载: {Path.GetFileName(sheetPath)}";
            }
            else
            {
                sheet.StatusMessage = "未找到复盘内容，可生成。";
            }

            ((RelayCommand)GenerateReviewSummaryCommand).RaiseCanExecuteChanged();
            ((RelayCommand)GenerateAllReviewSheetsCommand).RaiseCanExecuteChanged();
        }

        private static string GetReviewSheetPath(string audioFilePath, string fileTag)
        {
            var directory = Path.GetDirectoryName(audioFilePath) ?? PathManager.Instance.SessionsPath;
            var baseName = Path.GetFileNameWithoutExtension(audioFilePath);
            var tag = string.IsNullOrWhiteSpace(fileTag) ? "summary" : fileTag.Trim();
            return Path.Combine(directory, baseName + $".ai.{tag}.md");
        }

        private void CancelAllReviewSheetGeneration()
        {
            foreach (var sheet in _reviewSheets)
            {
                sheet.Cts?.Cancel();
                sheet.Cts = null;
                sheet.IsLoading = false;
            }
        }

        private bool CanGenerateReviewSummary()
        {
            return IsAiConfigured
                   && SelectedAudioFile != null
                   && SelectedReviewSheet != null
                   && SubtitleCues.Count > 0
                   && !IsReviewSummaryLoading;
        }

        private async void GenerateReviewSummary()
        {
            if (SelectedReviewSheet == null || SelectedAudioFile == null)
            {
                return;
            }

            var sheet = SelectedReviewSheet;
            var audioFile = SelectedAudioFile;
            var cues = SubtitleCues.ToList();
            await GenerateReviewSheetAsync(sheet, audioFile, cues);
        }

        private bool CanGenerateAllReviewSheets()
        {
            return IsAiConfigured
                   && SelectedAudioFile != null
                   && SubtitleCues.Count > 0
                   && _reviewSheets.Count > 0
                   && _reviewSheets.Any(sheet => !sheet.IsLoading);
        }

        private async void GenerateAllReviewSheets()
        {
            if (SelectedAudioFile == null || SubtitleCues.Count == 0)
            {
                return;
            }

            EnqueueReviewSheetsForAudio(SelectedAudioFile, _reviewSheets);
            StartBatchQueueRunner("复盘已加入队列");
        }

        private bool CanGenerateSpeechSubtitle()
        {
            if (IsSpeechSubtitleGenerating)
            {
                return false;
            }

            if (SelectedAudioFile == null || string.IsNullOrWhiteSpace(SelectedAudioFile.FullPath))
            {
                return false;
            }

            if (!File.Exists(SelectedAudioFile.FullPath))
            {
                return false;
            }

            var subscription = _config.GetActiveSubscription();
            return subscription?.IsValid() == true && !string.IsNullOrWhiteSpace(_config.SourceLanguage);
        }

        private async void GenerateSpeechSubtitle()
        {
            if (!CanGenerateSpeechSubtitle())
            {
                SpeechSubtitleStatusMessage = "订阅或音频不可用";
                return;
            }

            var audioFile = SelectedAudioFile;
            if (audioFile == null)
            {
                return;
            }

            _speechSubtitleCts?.Cancel();
            _speechSubtitleCts = new CancellationTokenSource();
            var token = _speechSubtitleCts.Token;

            IsSpeechSubtitleGenerating = true;
            SpeechSubtitleStatusMessage = "正在转写...";

            try
            {
                var cues = await TranscribeSpeechToCuesAsync(audioFile.FullPath, token);
                if (cues.Count == 0)
                {
                    SpeechSubtitleStatusMessage = "未识别到有效文本";
                    return;
                }

                var outputPath = GetSpeechSubtitlePath(audioFile.FullPath);
                WriteVttFile(outputPath, cues);

                SpeechSubtitleStatusMessage = $"speech 字幕已生成: {Path.GetFileName(outputPath)}";
                LoadSubtitleFilesForAudio(audioFile);
            }
            catch (OperationCanceledException)
            {
                SpeechSubtitleStatusMessage = "转写已取消";
            }
            catch (Exception ex)
            {
                SpeechSubtitleStatusMessage = $"转写失败: {ex.Message}";
            }
            finally
            {
                IsSpeechSubtitleGenerating = false;
                _speechSubtitleCts?.Dispose();
                _speechSubtitleCts = null;
            }
        }

        private bool CanGenerateBatchSpeechSubtitle()
        {
            if (IsSpeechSubtitleGenerating)
            {
                return false;
            }

            if (SelectedAudioFile == null || string.IsNullOrWhiteSpace(SelectedAudioFile.FullPath))
            {
                return false;
            }

            if (!File.Exists(SelectedAudioFile.FullPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_config.BatchStorageConnectionString))
            {
                return false;
            }

            var subscription = _config.GetActiveSubscription();
            return subscription?.IsValid() == true && !string.IsNullOrWhiteSpace(_config.SourceLanguage);
        }

        private async void GenerateBatchSpeechSubtitle()
        {
            if (!CanGenerateBatchSpeechSubtitle())
            {
                SpeechSubtitleStatusMessage = "请配置批量转写的存储连接字符串";
                return;
            }

            var audioFile = SelectedAudioFile;
            if (audioFile == null)
            {
                return;
            }

            _speechSubtitleCts?.Cancel();
            _speechSubtitleCts = new CancellationTokenSource();
            var token = _speechSubtitleCts.Token;

            IsSpeechSubtitleGenerating = true;
            SpeechSubtitleStatusMessage = "批量转写：上传音频...";

            BlobClient? uploadedBlob = null;
            BlobContainerClient? resultContainer = null;

            try
            {
                var (audioContainer, outputContainer) = await GetBatchContainersAsync(
                    _config.BatchStorageConnectionString,
                    _config.BatchAudioContainerName,
                    _config.BatchResultContainerName,
                    token);

                resultContainer = outputContainer;

                uploadedBlob = await UploadAudioToBlobAsync(
                    audioFile.FullPath,
                    audioContainer,
                    token);

                var contentUrl = CreateBlobReadSasUri(uploadedBlob, TimeSpan.FromHours(24));

                SpeechSubtitleStatusMessage = "批量转写：提交任务...";

                var subscription = _config.GetActiveSubscription();
                if (subscription == null)
                {
                    throw new InvalidOperationException("语音订阅未配置");
                }

                var (cues, transcriptionJson) = await BatchTranscribeSpeechToCuesAsync(
                    contentUrl,
                    _config.SourceLanguage,
                    subscription,
                    token,
                    status => SpeechSubtitleStatusMessage = status);

                if (cues.Count == 0)
                {
                    SpeechSubtitleStatusMessage = "未识别到有效文本";
                    return;
                }

                var outputPath = GetSpeechSubtitlePath(audioFile.FullPath);
                WriteVttFile(outputPath, cues);

                if (resultContainer != null)
                {
                    var baseName = Path.GetFileNameWithoutExtension(audioFile.FullPath);
                    await UploadTextToBlobAsync(resultContainer, baseName + ".speech.vtt", File.ReadAllText(outputPath), "text/vtt", token);
                    await UploadTextToBlobAsync(resultContainer, baseName + ".speech.json", transcriptionJson, "application/json", token);
                }

                SpeechSubtitleStatusMessage = $"speech 字幕已生成: {Path.GetFileName(outputPath)}";
                LoadSubtitleFilesForAudio(audioFile);
            }
            catch (OperationCanceledException)
            {
                SpeechSubtitleStatusMessage = "批量转写已取消";
            }
            catch (Exception ex)
            {
                SpeechSubtitleStatusMessage = $"批量转写失败: {ex.Message}";
            }
            finally
            {
                IsSpeechSubtitleGenerating = false;
                _speechSubtitleCts?.Dispose();
                _speechSubtitleCts = null;
            }
        }

        private static async Task<(BlobContainerClient Audio, BlobContainerClient Result)> GetBatchContainersAsync(
            string connectionString,
            string audioContainerName,
            string resultContainerName,
            CancellationToken token)
        {
            var serviceClient = new BlobServiceClient(connectionString);
            var normalizedAudio = NormalizeContainerName(audioContainerName, AzureSpeechConfig.DefaultBatchAudioContainerName);
            var normalizedResult = NormalizeContainerName(resultContainerName, AzureSpeechConfig.DefaultBatchResultContainerName);

            var audioContainer = serviceClient.GetBlobContainerClient(normalizedAudio);
            await audioContainer.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: token);

            var resultContainer = serviceClient.GetBlobContainerClient(normalizedResult);
            await resultContainer.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: token);

            return (audioContainer, resultContainer);
        }

        private static string NormalizeContainerName(string? name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return fallback;
            }

            var normalized = new string(name.Trim().ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray());

            normalized = normalized.Trim('-');
            if (normalized.Length < 3)
            {
                return fallback;
            }

            if (normalized.Length > 63)
            {
                normalized = normalized.Substring(0, 63).Trim('-');
            }

            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }

        private static async Task<BlobClient> UploadAudioToBlobAsync(
            string audioPath,
            BlobContainerClient container,
            CancellationToken token)
        {
            var fileName = Path.GetFileName(audioPath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var blobName = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}{Path.GetExtension(fileName)}";

            var blobClient = container.GetBlobClient(blobName);
            using var stream = File.OpenRead(audioPath);
            await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: token);
            await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
            {
                ContentType = GetAudioContentType(audioPath)
            }, cancellationToken: token);
            return blobClient;
        }

        private static Uri CreateBlobReadSasUri(BlobClient blobClient, TimeSpan validFor)
        {
            if (!blobClient.CanGenerateSasUri)
            {
                throw new InvalidOperationException("无法生成 SAS URL，请确保使用存储账号连接字符串");
            }

            var builder = new BlobSasBuilder
            {
                BlobContainerName = blobClient.BlobContainerName,
                BlobName = blobClient.Name,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.Add(validFor)
            };

            builder.SetPermissions(BlobSasPermissions.Read);
            return blobClient.GenerateSasUri(builder);
        }

        private static string GetAudioContentType(string audioPath)
        {
            var extension = Path.GetExtension(audioPath).ToLowerInvariant();
            return extension switch
            {
                ".wav" => "audio/wav",
                ".mp3" => "audio/mpeg",
                ".m4a" => "audio/mp4",
                _ => "application/octet-stream"
            };
        }

        private static async Task UploadTextToBlobAsync(
            BlobContainerClient container,
            string blobName,
            string content,
            string contentType,
            CancellationToken token)
        {
            var blobClient = container.GetBlobClient(blobName);
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: token);
            await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
            {
                ContentType = contentType
            }, cancellationToken: token);
        }

        private static async Task<(List<SubtitleCue> Cues, string TranscriptionJson)> BatchTranscribeSpeechToCuesAsync(
            Uri contentUrl,
            string locale,
            AzureSubscription subscription,
            CancellationToken token,
            Action<string> onStatus)
        {
            var endpoint = $"https://{subscription.ServiceRegion}.api.cognitive.microsoft.com/speechtotext/v3.1/transcriptions";
            var requestBody = new
            {
                displayName = $"Batch-{DateTime.Now:yyyyMMdd_HHmmss}",
                locale = locale,
                contentUrls = new[] { contentUrl.ToString() },
                properties = new
                {
                    diarizationEnabled = true,
                    wordLevelTimestampsEnabled = true,
                    punctuationMode = "DictatedAndAutomatic",
                    profanityFilterMode = "Masked"
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscription.SubscriptionKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");

            using var response = await SpeechBatchHttpClient.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(token);
                throw new InvalidOperationException($"创建批量转写失败: {response.StatusCode} {detail}");
            }

            var statusUrl = response.Headers.Location?.ToString();
            if (string.IsNullOrWhiteSpace(statusUrl))
            {
                var body = await response.Content.ReadAsStringAsync(token);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("self", out var selfElement))
                {
                    statusUrl = selfElement.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(statusUrl))
            {
                throw new InvalidOperationException("未获取到批量转写状态地址");
            }

            string? lastStatusJson = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                using var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUrl);
                statusRequest.Headers.Add("Ocp-Apim-Subscription-Key", subscription.SubscriptionKey);

                using var statusResponse = await SpeechBatchHttpClient.SendAsync(statusRequest, token);
                var statusBody = await statusResponse.Content.ReadAsStringAsync(token);
                lastStatusJson = statusBody;

                if (!statusResponse.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"查询批量转写状态失败: {statusResponse.StatusCode} {statusBody}");
                }

                using var statusDoc = JsonDocument.Parse(statusBody);
                var status = statusDoc.RootElement.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString()
                    : "";

                if (string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    onStatus("批量转写：已完成，整理字幕...");
                    break;
                }

                if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    var errorMessage = "批量转写失败";
                    if (statusDoc.RootElement.TryGetProperty("error", out var errorElement) &&
                        errorElement.TryGetProperty("message", out var messageElement))
                    {
                        errorMessage = messageElement.GetString() ?? errorMessage;
                    }

                    throw new InvalidOperationException(errorMessage);
                }

                onStatus($"批量转写：{status}...");
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }

            var filesUrl = statusUrl.TrimEnd('/') + "/files";
            if (!string.IsNullOrWhiteSpace(lastStatusJson))
            {
                using var statusDoc = JsonDocument.Parse(lastStatusJson);
                if (statusDoc.RootElement.TryGetProperty("links", out var linksElement) &&
                    linksElement.TryGetProperty("files", out var filesElement))
                {
                    filesUrl = filesElement.GetString() ?? filesUrl;
                }
            }

            using var filesRequest = new HttpRequestMessage(HttpMethod.Get, filesUrl);
            filesRequest.Headers.Add("Ocp-Apim-Subscription-Key", subscription.SubscriptionKey);

            using var filesResponse = await SpeechBatchHttpClient.SendAsync(filesRequest, token);
            var filesBody = await filesResponse.Content.ReadAsStringAsync(token);
            if (!filesResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"获取批量转写文件列表失败: {filesResponse.StatusCode} {filesBody}");
            }

            var transcriptionUrl = ExtractTranscriptionContentUrl(filesBody);
            if (string.IsNullOrWhiteSpace(transcriptionUrl))
            {
                throw new InvalidOperationException("未找到批量转写结果文件");
            }

            var transcriptionJson = await SpeechBatchHttpClient.GetStringAsync(transcriptionUrl, token);
            var cues = ParseBatchTranscriptionToCues(transcriptionJson);
            return (cues, transcriptionJson);
        }

        private static string? ExtractTranscriptionContentUrl(string filesJson)
        {
            using var doc = JsonDocument.Parse(filesJson);
            if (!doc.RootElement.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in values.EnumerateArray())
            {
                var kind = item.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() : "";
                if (!string.Equals(kind, "Transcription", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (item.TryGetProperty("links", out var linksElement) &&
                    linksElement.TryGetProperty("contentUrl", out var contentElement))
                {
                    return contentElement.GetString();
                }
            }

            return null;
        }

        private static List<SubtitleCue> ParseBatchTranscriptionToCues(string transcriptionJson)
        {
            var list = new List<SubtitleCue>();
            using var doc = JsonDocument.Parse(transcriptionJson);
            if (!doc.RootElement.TryGetProperty("recognizedPhrases", out var phrases) || phrases.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var phrase in phrases.EnumerateArray())
            {
                if (!TryParseBatchOffsetDuration(phrase, out var start, out var end))
                {
                    continue;
                }

                var text = ExtractPhraseText(phrase);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var speaker = phrase.TryGetProperty("speaker", out var speakerElement)
                    ? speakerElement.ToString()
                    : "";
                var speakerLabel = string.IsNullOrWhiteSpace(speaker) ? "Speaker" : $"Speaker {speaker}";

                list.Add(new SubtitleCue
                {
                    Start = start,
                    End = end,
                    Text = $"{speakerLabel}: {text}"
                });
            }

            return list.OrderBy(c => c.Start).ToList();
        }

        private static string ExtractPhraseText(JsonElement phrase)
        {
            if (phrase.TryGetProperty("nBest", out var nbest) && nbest.ValueKind == JsonValueKind.Array && nbest.GetArrayLength() > 0)
            {
                var first = nbest[0];
                if (first.TryGetProperty("display", out var displayElement))
                {
                    return displayElement.GetString() ?? "";
                }
                if (first.TryGetProperty("lexical", out var lexicalElement))
                {
                    return lexicalElement.GetString() ?? "";
                }
            }

            if (phrase.TryGetProperty("display", out var directDisplay))
            {
                return directDisplay.GetString() ?? "";
            }

            return "";
        }

        private static bool TryParseBatchOffsetDuration(JsonElement phrase, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            if (TryGetTimeValue(phrase, "offsetInTicks", out var offsetTicks) &&
                TryGetTimeValue(phrase, "durationInTicks", out var durationTicks))
            {
                start = offsetTicks;
                end = start + durationTicks;
                return true;
            }

            if (TryGetTimeValue(phrase, "offset", out var offset) &&
                TryGetTimeValue(phrase, "duration", out var duration))
            {
                start = offset;
                end = start + duration;
                return true;
            }

            return false;
        }

        private static bool TryGetTimeValue(JsonElement element, string propertyName, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (!element.TryGetProperty(propertyName, out var prop))
            {
                return false;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var ticks))
            {
                value = TimeSpan.FromTicks(Math.Max(0, ticks));
                return true;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                var text = prop.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                if (text.StartsWith("PT", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        value = XmlConvert.ToTimeSpan(text);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                if (TimeSpan.TryParse(text, out var parsed))
                {
                    value = parsed;
                    return true;
                }

                if (long.TryParse(text, out var parsedTicks))
                {
                    value = TimeSpan.FromTicks(Math.Max(0, parsedTicks));
                    return true;
                }
            }

            return false;
        }

        private void CancelSpeechSubtitle()
        {
            _speechSubtitleCts?.Cancel();
        }

        private static string GetSpeechSubtitlePath(string audioFilePath)
        {
            var directory = Path.GetDirectoryName(audioFilePath) ?? PathManager.Instance.SessionsPath;
            var baseName = Path.GetFileNameWithoutExtension(audioFilePath);
            return Path.Combine(directory, baseName + ".speech.vtt");
        }

        private async Task<List<SubtitleCue>> TranscribeSpeechToCuesAsync(string audioPath, CancellationToken token)
        {
            var subscription = _config.GetActiveSubscription();
            if (subscription == null || !subscription.IsValid())
            {
                throw new InvalidOperationException("语音订阅未配置");
            }

            if (!File.Exists(audioPath))
            {
                throw new FileNotFoundException("未找到音频文件", audioPath);
            }

            var speechConfig = SpeechConfig.FromSubscription(subscription.SubscriptionKey, subscription.ServiceRegion);
            speechConfig.SpeechRecognitionLanguage = _config.SourceLanguage;

            var cues = new List<SubtitleCue>();
            var cueLock = new object();
            var fallbackCursor = TimeSpan.Zero;
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var audioConfig = CreateTranscriptionAudioConfig(audioPath, token, out var feedTask);
            using var transcriber = new ConversationTranscriber(speechConfig, audioConfig);

            transcriber.Transcribed += (_, e) =>
            {
                if (e.Result.Reason != ResultReason.RecognizedSpeech)
                {
                    return;
                }

                var text = e.Result.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                var speakerId = string.IsNullOrWhiteSpace(e.Result.SpeakerId)
                    ? "Speaker"
                    : $"Speaker {e.Result.SpeakerId}";
                TimeSpan start;
                TimeSpan end;
                if (!TryGetTranscriptionTiming(e.Result, out start, out end))
                {
                    lock (cueLock)
                    {
                        start = fallbackCursor;
                        end = start + TimeSpan.FromSeconds(2);
                        fallbackCursor = end;
                    }
                }

                var cue = new SubtitleCue
                {
                    Start = start,
                    End = end,
                    Text = $"{speakerId}: {text}"
                };

                lock (cueLock)
                {
                    cues.Add(cue);
                }
            };

            transcriber.Canceled += (_, e) =>
            {
                completed.TrySetException(new InvalidOperationException($"转写取消: {e.Reason}, {e.ErrorDetails}"));
            };

            transcriber.SessionStopped += (_, _) => completed.TrySetResult(true);

            token.Register(() => completed.TrySetCanceled(token));

            await transcriber.StartTranscribingAsync();
            if (feedTask != null)
            {
                await feedTask;
            }

            try
            {
                await completed.Task;
            }
            finally
            {
                await transcriber.StopTranscribingAsync();
            }

            lock (cueLock)
            {
                return cues.OrderBy(c => c.Start).ToList();
            }
        }

        private static bool TryGetTranscriptionTiming(ConversationTranscriptionResult result, out TimeSpan start, out TimeSpan end)
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
                using var doc = System.Text.Json.JsonDocument.Parse(json);
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

        private static bool TryReadOffsetDuration(System.Text.Json.JsonElement root, out long offset, out long duration)
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
                nbest.ValueKind == System.Text.Json.JsonValueKind.Array &&
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

        private static AudioConfig CreateTranscriptionAudioConfig(string audioPath, CancellationToken token, out Task? feedTask)
        {
            var streamFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
            var pushStream = AudioInputStream.CreatePushStream(streamFormat);
            var audioConfig = AudioConfig.FromStreamInput(pushStream);

            feedTask = Task.Run(() =>
            {
                try
                {
                    using var reader = new AudioFileReader(audioPath);
                    using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1))
                    {
                        ResamplerQuality = 60
                    };

                    var buffer = new byte[3200];
                    int read;
                    while (!token.IsCancellationRequested && (read = resampler.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        pushStream.Write(buffer, read);
                    }
                }
                finally
                {
                    pushStream.Close();
                }
            });

            return audioConfig;
        }

        private static void WriteVttFile(string outputPath, List<SubtitleCue> cues)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var writer = new StreamWriter(outputPath, false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine("WEBVTT");
            writer.WriteLine();

            var index = 1;
            foreach (var cue in cues)
            {
                writer.WriteLine(index++);
                writer.WriteLine($"{FormatVttTime(cue.Start)} --> {FormatVttTime(cue.End)}");
                writer.WriteLine(cue.Text);
                writer.WriteLine();
            }
        }

        private static string FormatVttTime(TimeSpan time)
        {
            return time.ToString(@"hh\:mm\:ss\.fff");
        }

        private async Task GenerateReviewSheetAsync(ReviewSheetState sheet, MediaFileItem audioFile, List<SubtitleCue> cues)
        {
            if (_config.AiConfig == null || !_config.AiConfig.IsValid)
            {
                sheet.StatusMessage = "AI 配置无效，请先配置 AI 服务";
                return;
            }

            if (audioFile == null || string.IsNullOrWhiteSpace(audioFile.FullPath))
            {
                sheet.StatusMessage = "未选择音频文件";
                return;
            }

            if (cues.Count == 0)
            {
                sheet.StatusMessage = "未加载字幕，无法生成总结";
                return;
            }

            sheet.Cts?.Cancel();
            var localCts = new CancellationTokenSource();
            sheet.Cts = localCts;
            var token = localCts.Token;

            sheet.IsLoading = true;
            sheet.Markdown = "";
            sheet.StatusMessage = "正在生成复盘内容...";
            if (GenerateAllReviewSheetsCommand is RelayCommand allStartCmd)
            {
                allStartCmd.RaiseCanExecuteChanged();
            }

            var systemPrompt = "你是一个会议复盘助手。根据字幕内容生成结构化 Markdown 总结。请输出包含关键结论、行动项和风险点，并在引用内容时标注时间戳，格式为 [HH:MM:SS]。";
            var subtitlesText = FormatSubtitleForSummary(cues);
            var prompt = string.IsNullOrWhiteSpace(sheet.Prompt)
                ? "请生成复盘总结。"
                : sheet.Prompt.Trim();
            var userPrompt = $"以下是会议字幕内容:\n\n{subtitlesText}\n\n---\n\n{prompt}";

            try
            {
                var sb = new System.Text.StringBuilder();
                AiRequestOutcome? outcome = null;
                await _aiInsightService.StreamChatAsync(
                    _config.AiConfig,
                    systemPrompt,
                    userPrompt,
                    chunk =>
                    {
                        if (!ReferenceEquals(sheet.Cts, localCts))
                        {
                            return;
                        }

                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            sb.Append(chunk);
                            sheet.Markdown = InjectTimeLinks(sb.ToString());
                        });
                    },
                    token,
                    AiChatProfile.Summary,
                    enableReasoning: _config.AiConfig.SummaryEnableReasoning,
                    onOutcome: o => outcome = o);

                if (!ReferenceEquals(sheet.Cts, localCts))
                {
                    return;
                }

                var summaryPath = GetReviewSheetPath(audioFile.FullPath, sheet.FileTag);
                File.WriteAllText(summaryPath, sheet.Markdown);
                var reasoningNote = "";
                if (outcome?.UsedFallback == true)
                {
                    reasoningNote = " (已降级为非思考)";
                }
                else if (outcome?.UsedReasoning == true)
                {
                    reasoningNote = " (思考已启用)";
                }
                else if (_config.AiConfig.SummaryEnableReasoning)
                {
                    reasoningNote = " (未启用思考)";
                }

                sheet.StatusMessage = $"复盘内容已保存: {Path.GetFileName(summaryPath)}{reasoningNote}";
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(sheet.Cts, localCts))
                {
                    sheet.StatusMessage = "复盘内容已取消";
                }
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(sheet.Cts, localCts))
                {
                    sheet.StatusMessage = $"生成失败: {ex.Message}";
                }
            }
            finally
            {
                if (ReferenceEquals(sheet.Cts, localCts))
                {
                    sheet.IsLoading = false;
                    sheet.Cts = null;
                }

                if (GenerateAllReviewSheetsCommand is RelayCommand allCmd)
                {
                    allCmd.RaiseCanExecuteChanged();
                }
            }
        }

        private string FormatSubtitleForSummary()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var cue in SubtitleCues)
            {
                var time = cue.Start.ToString(@"hh\:mm\:ss");
                sb.AppendLine($"[{time}] {cue.Text}");
            }
            return sb.ToString();
        }

        private static string FormatSubtitleForSummary(List<SubtitleCue> cues)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var cue in cues)
            {
                var time = cue.Start.ToString(@"hh\:mm\:ss");
                sb.AppendLine($"[{time}] {cue.Text}");
            }
            return sb.ToString();
        }

        private void OnReviewMarkdownLink(object? param)
        {
            if (param is not string url || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            if (TryParseTimeUrl(url, out var time))
            {
                SeekToTime(time);
            }
        }

        private static bool TryParseTimeUrl(string url, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (url.StartsWith("tt://", StringComparison.OrdinalIgnoreCase))
            {
                var text = url.Substring("tt://".Length);
                return TryParseTimestamp(text, out time);
            }

            if (url.StartsWith("time://", StringComparison.OrdinalIgnoreCase))
            {
                var text = url.Substring("time://".Length);
                return TryParseTimestamp(text, out time);
            }

            return false;
        }

        private static bool TryParseTimestamp(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var parts = text.Split(':');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var mm)
                && int.TryParse(parts[1], out var ss))
            {
                time = new TimeSpan(0, mm, ss);
                return true;
            }

            if (parts.Length == 3
                && int.TryParse(parts[0], out var hh)
                && int.TryParse(parts[1], out var mm2)
                && int.TryParse(parts[2], out var ss2))
            {
                time = new TimeSpan(hh, mm2, ss2);
                return true;
            }

            return false;
        }

        private static string InjectTimeLinks(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return markdown;
            }

            var regex = new Regex(@"\[(?<time>(\d{1,2}:)?\d{2}:\d{2})\](?!\()",
                RegexOptions.Compiled);

            return regex.Replace(markdown, match =>
            {
                var timeText = match.Groups["time"].Value;
                return $"[{timeText}](tt://{timeText})";
            });
        }


        private void ParseSrt(string path)
        {
            var lines = File.ReadAllLines(path);
            ParseSubtitleLines(lines, expectsHeader: false);
        }

        private void ParseVtt(string path)
        {
            var lines = File.ReadAllLines(path);
            ParseSubtitleLines(lines, expectsHeader: true);
        }

        private void ParseSubtitleLines(string[] lines, bool expectsHeader)
        {
            var index = 0;
            if (expectsHeader && index < lines.Length && lines[index].StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            {
                index++;
            }

            while (index < lines.Length)
            {
                var line = lines[index].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    index++;
                    continue;
                }

                if (int.TryParse(line, out _))
                {
                    index++;
                    if (index >= lines.Length)
                    {
                        break;
                    }
                    line = lines[index].Trim();
                }

                if (!TryParseTimeRange(line, out var start, out var end))
                {
                    index++;
                    continue;
                }

                index++;
                var textLines = new List<string>();
                while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
                {
                    textLines.Add(lines[index].Trim());
                    index++;
                }

                var text = string.Join(" ", textLines).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _subtitleCues.Add(new SubtitleCue
                    {
                        Start = start,
                        End = end,
                        Text = text
                    });
                }
            }

            UpdateSubtitleListHeight();
        }

        private static List<SubtitleCue> ParseSubtitleFileToCues(string path)
        {
            if (!File.Exists(path))
            {
                return new List<SubtitleCue>();
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var lines = File.ReadAllLines(path);
            var expectsHeader = extension == ".vtt";
            return ParseSubtitleLinesToList(lines, expectsHeader);
        }

        private static List<SubtitleCue> ParseSubtitleLinesToList(string[] lines, bool expectsHeader)
        {
            var list = new List<SubtitleCue>();
            var index = 0;
            if (expectsHeader && index < lines.Length && lines[index].StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            {
                index++;
            }

            while (index < lines.Length)
            {
                var line = lines[index].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    index++;
                    continue;
                }

                if (int.TryParse(line, out _))
                {
                    index++;
                    if (index >= lines.Length)
                    {
                        break;
                    }
                    line = lines[index].Trim();
                }

                if (!TryParseTimeRange(line, out var start, out var end))
                {
                    index++;
                    continue;
                }

                index++;
                var textLines = new List<string>();
                while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
                {
                    textLines.Add(lines[index].Trim());
                    index++;
                }

                var text = string.Join(" ", textLines).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    list.Add(new SubtitleCue
                    {
                        Start = start,
                        End = end,
                        Text = text
                    });
                }
            }

            return list;
        }

        private void UpdateSubtitleListHeight()
        {
            var visible = Math.Min(_subtitleCues.Count, 6);
            SubtitleListHeight = visible * SubtitleCueRowHeight;
        }

        private static bool TryParseTimeRange(string line, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            var match = Regex.Match(line, @"(?<start>\d{2}:\d{2}:\d{2}[\.,]\d{3})\s*-->\s*(?<end>\d{2}:\d{2}:\d{2}[\.,]\d{3})");
            if (!match.Success)
            {
                return false;
            }

            if (!TryParseTimecode(match.Groups["start"].Value, out start))
            {
                return false;
            }

            if (!TryParseTimecode(match.Groups["end"].Value, out end))
            {
                return false;
            }

            return true;
        }

        private static bool TryParseTimecode(string value, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            var normalized = value.Replace(',', '.');
            if (TimeSpan.TryParseExact(normalized, @"hh\:mm\:ss\.fff", null, out time))
            {
                return true;
            }

            return false;
        }

        private void LoadAudioForPlayback(MediaFileItem? audioFile)
        {
            StopPlaybackInternal();

            _playbackDuration = TimeSpan.Zero;
            _playbackPosition = TimeSpan.Zero;
            PlaybackProgress = 0;

            if (audioFile == null || string.IsNullOrWhiteSpace(audioFile.FullPath))
            {
                UpdatePlaybackState(false, false);
                return;
            }

            if (!File.Exists(audioFile.FullPath))
            {
                UpdatePlaybackState(false, false);
                return;
            }

            try
            {
                _playbackReader = new AudioFileReader(audioFile.FullPath);
                _playbackOutput = new WaveOutEvent();
                _playbackOutput.Init(_playbackReader);
                _playbackOutput.PlaybackStopped += OnPlaybackStopped;
                _playbackDuration = _playbackReader.TotalTime;
                _playbackPosition = TimeSpan.Zero;
                PlaybackProgress = 0;
                UpdatePlaybackState(true, false);
                _playbackTimer.Start();
            }
            catch (Exception ex)
            {
                UpdatePlaybackState(false, false);
                StatusMessage = $"加载音频失败: {ex.Message}";
            }
        }

        private void PlayAudio()
        {
            if (_playbackOutput == null)
            {
                return;
            }

            _playbackOutput.Play();
            UpdatePlaybackState(true, true);
        }

        private void PauseAudio()
        {
            if (_playbackOutput == null)
            {
                return;
            }

            _playbackOutput.Pause();
            UpdatePlaybackState(true, false);
        }

        private void StopAudio()
        {
            if (_playbackOutput == null)
            {
                return;
            }

            _playbackOutput.Stop();
            SeekToTime(TimeSpan.Zero);
            UpdatePlaybackState(true, false);
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            UpdatePlaybackProgressFromReader();
            UpdatePlaybackState(_playbackReader != null, false);
        }

        private void SeekToTime(TimeSpan time)
        {
            if (_playbackReader == null)
            {
                return;
            }

            var safe = time;
            if (safe < TimeSpan.Zero)
            {
                safe = TimeSpan.Zero;
            }
            if (_playbackDuration > TimeSpan.Zero && safe > _playbackDuration)
            {
                safe = _playbackDuration;
            }

            _playbackReader.CurrentTime = safe;
            _playbackPosition = safe;
            UpdatePlaybackProgressFromReader();
        }

        private void SeekToProgress(double progress)
        {
            if (_playbackReader == null || _playbackDuration <= TimeSpan.Zero)
            {
                return;
            }

            var clamped = Math.Clamp(progress, 0, 1);
            var target = TimeSpan.FromMilliseconds(_playbackDuration.TotalMilliseconds * clamped);
            SeekToTime(target);
        }

        private void UpdatePlaybackProgressFromReader()
        {
            if (_playbackReader == null)
            {
                return;
            }

            _suppressSeek = true;
            _playbackPosition = _playbackReader.CurrentTime;
            _playbackDuration = _playbackReader.TotalTime;
            _playbackProgress = _playbackDuration > TimeSpan.Zero
                ? _playbackPosition.TotalMilliseconds / _playbackDuration.TotalMilliseconds
                : 0;
            OnPropertyChanged(nameof(PlaybackProgress));
            OnPropertyChanged(nameof(PlaybackTimeText));
            UpdateCurrentSubtitleCue(_playbackPosition);
            _suppressSeek = false;
        }

        public void PlayFromSubtitleCue(SubtitleCue? cue)
        {
            if (cue == null || _playbackReader == null || _playbackOutput == null)
            {
                return;
            }

            SeekToTime(cue.Start);
            PlayAudio();
        }

        private void UpdateCurrentSubtitleCue(TimeSpan position)
        {
            if (_subtitleCues.Count == 0)
            {
                return;
            }

            if (_selectedSubtitleCue != null
                && position >= _selectedSubtitleCue.Start
                && position <= _selectedSubtitleCue.End)
            {
                return;
            }

            var match = _subtitleCues.FirstOrDefault(cue => position >= cue.Start && position <= cue.End);
            if (ReferenceEquals(match, _selectedSubtitleCue))
            {
                return;
            }

            _suppressSubtitleSeek = true;
            SelectedSubtitleCue = match;
            _suppressSubtitleSeek = false;
        }

        private void UpdatePlaybackState(bool ready, bool playing)
        {
            _isPlaybackReady = ready;
            _isPlaying = playing;
            OnPropertyChanged(nameof(IsPlayEnabled));
            OnPropertyChanged(nameof(IsPauseEnabled));
            OnPropertyChanged(nameof(IsStopEnabled));
            OnPropertyChanged(nameof(PlaybackTimeText));
            ((RelayCommand)PlayAudioCommand).RaiseCanExecuteChanged();
            ((RelayCommand)PauseAudioCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StopAudioCommand).RaiseCanExecuteChanged();
        }

        private void StopPlaybackInternal()
        {
            try
            {
                if (_playbackOutput != null)
                {
                    _playbackOutput.PlaybackStopped -= OnPlaybackStopped;
                    _playbackOutput.Stop();
                    _playbackOutput.Dispose();
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _playbackOutput = null;
            }

            try
            {
                _playbackReader?.Dispose();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _playbackReader = null;
            }

            _playbackTimer.Stop();
        }

        private static string FormatTime(TimeSpan time)
        {
            return time.TotalHours >= 1
                ? time.ToString(@"hh\:mm\:ss")
                : time.ToString(@"mm\:ss");
        }
        private void UpdateSubscriptionNames()
        {
            _subscriptionNames.Clear();

            foreach (var subscription in _config.Subscriptions)
            {
                var displayName = $"{subscription.Name} ({subscription.ServiceRegion})";
                _subscriptionNames.Add(displayName);
            }
        }

        public AzureSpeechConfig Config
        {
            get => _config;
            set => SetProperty(ref _config, value);
        }        
        public string CurrentOriginal
        {
            get => _currentOriginal;
            set
            {
                if (SetProperty(ref _currentOriginal, value))
                {
                    OnPropertyChanged(nameof(DisplayedText));
                }
            }
        }

        public string CurrentTranslated
        {
            get => _currentTranslated;
            set
            {
                if (SetProperty(ref _currentTranslated, value))
                {
                    OnPropertyChanged(nameof(DisplayedText));
                }
            }
        }

        public EditorDisplayMode EditorDisplayMode
        {
            get => _editorDisplayMode;
            set
            {
                if (!SetProperty(ref _editorDisplayMode, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(IsOriginalView));
                OnPropertyChanged(nameof(IsTranslatedView));
                OnPropertyChanged(nameof(IsBilingualView));
                OnPropertyChanged(nameof(IsSingleView));
                OnPropertyChanged(nameof(DisplayedText));
                OnPropertyChanged(nameof(DisplayPlaceholder));
            }
        }

        public bool IsOriginalView
        {
            get => _editorDisplayMode == EditorDisplayMode.Original;
            set
            {
                if (value)
                {
                    EditorDisplayMode = EditorDisplayMode.Original;
                }
            }
        }

        public bool IsTranslatedView
        {
            get => _editorDisplayMode == EditorDisplayMode.Translated;
            set
            {
                if (value)
                {
                    EditorDisplayMode = EditorDisplayMode.Translated;
                }
            }
        }

        public bool IsBilingualView
        {
            get => _editorDisplayMode == EditorDisplayMode.Bilingual;
            set
            {
                if (value)
                {
                    EditorDisplayMode = EditorDisplayMode.Bilingual;
                }
            }
        }

        public bool IsSingleView => _editorDisplayMode != EditorDisplayMode.Bilingual;

        public string DisplayedText
        {
            get => _editorDisplayMode == EditorDisplayMode.Original ? CurrentOriginal : CurrentTranslated;
            set
            {
                if (_editorDisplayMode == EditorDisplayMode.Original)
                {
                    CurrentOriginal = value;
                }
                else if (_editorDisplayMode == EditorDisplayMode.Translated)
                {
                    CurrentTranslated = value;
                }
            }
        }

        public string DisplayPlaceholder => _editorDisplayMode == EditorDisplayMode.Original
            ? "原文将在这里显示..."
            : _editorDisplayMode == EditorDisplayMode.Translated
                ? "译文将在这里显示..."
                : "双语将在这里显示...";

        public bool IsFloatingSubtitleOpen
        {
            get => _isFloatingSubtitleOpen;
            private set
            {
                if (!SetProperty(ref _isFloatingSubtitleOpen, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(FloatingSubtitleButtonBackground));
                OnPropertyChanged(nameof(FloatingSubtitleButtonForeground));
            }
        }

        public object? FloatingSubtitleButtonBackground => IsFloatingSubtitleOpen
            ? new SolidColorBrush(Color.Parse("#FF10B981"))
            : AvaloniaProperty.UnsetValue;

        public object? FloatingSubtitleButtonForeground => IsFloatingSubtitleOpen
            ? Brushes.White
            : AvaloniaProperty.UnsetValue;

        public bool IsTranslating
        {
            get => _isTranslating;
            set
            {
                if (!SetProperty(ref _isTranslating, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(TranslationToggleButtonText));
                OnPropertyChanged(nameof(TranslationToggleButtonBackground));
                OnPropertyChanged(nameof(TranslationToggleButtonForeground));
                OnPropertyChanged(nameof(IsAudioSourceSelectionEnabled));

                if (value)
                {
                    IsAudioDeviceSelectionEnabled = false;
                    IsAudioDeviceRefreshEnabled = false;
                }
                else
                {
                    RefreshAudioDevices(persistSelection: false);
                }

                ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
                ((RelayCommand)StopTranslationCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ToggleTranslationCommand).RaiseCanExecuteChanged();
            }
        }

        public string TranslationToggleButtonText => IsTranslating ? "停止翻译" : "开始翻译";

        public IBrush TranslationToggleButtonBackground => IsTranslating ? Brushes.Red : Brushes.Green;

        public IBrush TranslationToggleButtonForeground => Brushes.White;

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

        public SubscriptionValidationState SubscriptionValidationState
        {
            get => _subscriptionValidationState;
            private set
            {
                if (SetProperty(ref _subscriptionValidationState, value))
                {
                    OnPropertyChanged(nameof(SubscriptionLampFill));
                    OnPropertyChanged(nameof(SubscriptionLampStroke));
                    OnPropertyChanged(nameof(SubscriptionLampOpacity));
                }
            }
        }

        public string SubscriptionValidationStatusMessage
        {
            get => _subscriptionValidationStatusMessage;
            private set => SetProperty(ref _subscriptionValidationStatusMessage, value);
        }

        public IBrush SubscriptionLampFill
        {
            get
            {
                return _subscriptionValidationState switch
                {
                    SubscriptionValidationState.Valid => Brushes.LimeGreen,
                    SubscriptionValidationState.Invalid => Brushes.Red,
                    _ => Brushes.Transparent
                };
            }
        }

        public IBrush SubscriptionLampStroke
        {
            get
            {
                return _subscriptionValidationState switch
                {
                    SubscriptionValidationState.Valid => Brushes.LimeGreen,
                    SubscriptionValidationState.Invalid => Brushes.Red,
                    SubscriptionValidationState.Validating => Brushes.Gray,
                    _ => Brushes.Gray
                };
            }
        }

        public double SubscriptionLampOpacity
        {
            get
            {
                if (_subscriptionValidationState != SubscriptionValidationState.Validating)
                {
                    return 1;
                }

                return _subscriptionLampBlinkOn ? 1 : 0.3;
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

        public ObservableCollection<ReviewSheetState> ReviewSheets => _reviewSheets;

        public ReviewSheetState? SelectedReviewSheet
        {
            get => _selectedReviewSheet;
            set
            {
                if (ReferenceEquals(_selectedReviewSheet, value))
                {
                    return;
                }

                if (_selectedReviewSheet != null)
                {
                    _selectedReviewSheet.PropertyChanged -= OnSelectedReviewSheetPropertyChanged;
                }

                _selectedReviewSheet = value;
                OnPropertyChanged(nameof(SelectedReviewSheet));

                OnPropertyChanged(nameof(ReviewSummaryMarkdown));
                OnPropertyChanged(nameof(ReviewSummaryStatusMessage));
                OnPropertyChanged(nameof(IsReviewSummaryLoading));
                OnPropertyChanged(nameof(IsReviewSummaryEmpty));
                OnPropertyChanged(nameof(ReviewSummaryLampFill));
                OnPropertyChanged(nameof(ReviewSummaryLampStroke));
                OnPropertyChanged(nameof(ReviewSummaryLampOpacity));

                if (_selectedReviewSheet != null)
                {
                    _selectedReviewSheet.PropertyChanged += OnSelectedReviewSheetPropertyChanged;
                }

                LoadReviewSheetForAudio(SelectedAudioFile, _selectedReviewSheet);
            }
        }

        public string ReviewSummaryMarkdown => SelectedReviewSheet?.Markdown ?? "";

        public string ReviewSummaryStatusMessage => SelectedReviewSheet?.StatusMessage ?? "";

        public bool IsReviewSummaryLoading => SelectedReviewSheet?.IsLoading ?? false;

        public bool IsReviewSummaryEmpty => string.IsNullOrWhiteSpace(ReviewSummaryMarkdown) && !IsReviewSummaryLoading;

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
        private async void StartTranslation()
        {
            if (_translationService == null)
            {
                _translationService = new SpeechTranslationService(_config);
                _translationService.OnRealtimeTranslationReceived += OnRealtimeTranslationReceived;
                _translationService.OnFinalTranslationReceived += OnFinalTranslationReceived;
                _translationService.OnStatusChanged += OnStatusChanged;
                _translationService.OnReconnectTriggered += OnReconnectTriggered;
                _translationService.OnAudioLevelUpdated += OnAudioLevelUpdated;
            }

            await _translationService.StartTranslationAsync();
            IsTranslating = true;
            IsConfigurationEnabled = false;
            StatusMessage = "正在翻译...";

            ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ToggleTranslationCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StopTranslationCommand).RaiseCanExecuteChanged();
        }        private async void StopTranslation()
        {
            if (_translationService != null)
            {
                await _translationService.StopTranslationAsync();
            }

            IsTranslating = false;
            IsConfigurationEnabled = true;
            StatusMessage = "已停止";
            AudioLevel = 0;
            ResetAudioLevelHistory();

            if (_floatingSubtitleManager?.IsWindowOpen == true)
            {
                _floatingSubtitleManager.CloseWindow();
                StatusMessage = "已停止，浮动字幕窗口已关闭";
            }

            ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StopTranslationCommand).RaiseCanExecuteChanged();
        }

        private void OnReconnectTriggered(object? sender, string reason)
        {
            if (!_config.ShowReconnectMarkerInSubtitle)
            {
                return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var marker = "*触发重连*";
                if (string.IsNullOrWhiteSpace(CurrentTranslated))
                {
                    CurrentTranslated = marker;
                }
                else if (!CurrentTranslated.Contains(marker, StringComparison.Ordinal))
                {
                    CurrentTranslated = $"{CurrentTranslated} {marker}";
                }

                if (_floatingSubtitleManager?.IsWindowOpen == true)
                {
                    _floatingSubtitleManager.UpdateSubtitle(CurrentTranslated);
                }
            });
        }
        private void ClearHistory()
        {
            History.Clear();

            CurrentOriginal = "";
            CurrentTranslated = "";

            ((RelayCommand)ClearHistoryCommand).RaiseCanExecuteChanged();
        }
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

        private void ShowFloatingSubtitles()
        {
            try
            {
                if (_floatingSubtitleManager == null)
                {
                    _floatingSubtitleManager = new FloatingSubtitleManager();
                    _floatingSubtitleManager.WindowStateChanged += (_, isOpen) => IsFloatingSubtitleOpen = isOpen;
                }

                _floatingSubtitleManager.ToggleWindow();
                IsFloatingSubtitleOpen = _floatingSubtitleManager.IsWindowOpen;

                if (_floatingSubtitleManager.IsWindowOpen)
                {
                    StatusMessage = "浮动字幕窗口已打开";
                    
                    if (!string.IsNullOrEmpty(CurrentTranslated))
                    {
                        _floatingSubtitleManager.UpdateSubtitle(CurrentTranslated);
                    }
                }
                else
                {
                    StatusMessage = "浮动字幕窗口已关闭";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"浮动字幕窗口操作失败: {ex.Message}";
            }
        }

        private void ToggleEditorType()
        {
            EditorType = EditorType == TextEditorType.Simple 
                ? TextEditorType.Advanced 
                : TextEditorType.Simple;
            
            StatusMessage = $"已切换到 {(EditorType == TextEditorType.Simple ? "简单" : "高级")} 编辑器";
        }        private void OnRealtimeTranslationReceived(object? sender, TranslationItem item)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CurrentOriginal = item.OriginalText ?? "";
                CurrentTranslated = item.TranslatedText ?? "";
                
                if (_floatingSubtitleManager?.IsWindowOpen == true && !string.IsNullOrEmpty(CurrentTranslated))
                {
                    _floatingSubtitleManager.UpdateSubtitle(CurrentTranslated);
                }
            });
        }

        private void OnFinalTranslationReceived(object? sender, TranslationItem item)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                History.Insert(0, item);

                while (History.Count > _config.MaxHistoryItems)
                {
                    History.RemoveAt(History.Count - 1);
                }

                ((RelayCommand)ClearHistoryCommand).RaiseCanExecuteChanged();

                OnNewDataAutoInsight();
            });
        }
        private void OnStatusChanged(object? sender, string message)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = message;
            });
        }

        private void OnAudioLevelUpdated(object? sender, double level)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AudioLevel = level;
                PushAudioLevelSample(level);
            });
        }

        private void PushAudioLevelSample(double level)
        {
            if (_audioLevelHistory.Count == 0)
            {
                return;
            }

            _audioLevelHistory.RemoveAt(0);
            _audioLevelHistory.Add(level);
        }

        private void ResetAudioLevelHistory()
        {
            _audioLevelHistory.Clear();
            for (var i = 0; i < 24; i++)
            {
                _audioLevelHistory.Add(0);
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

        private async void SendInsight(string userQuestion)
        {
            if (_config.AiConfig == null || !_config.AiConfig.IsValid)
                return;

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
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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
                return "(暂无翻译记录)";

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
                return;

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
                return;

            if (History.Count == 0)
                return;

            SendInsight(AutoInsightPrompt);
        }

        private void OnNewDataAutoInsight()
        {
            if (!IsAutoInsightEnabled || AutoInsightModeIndex != 1)
                return;

            if (IsInsightLoading)
                return;

            if (History.Count <= _lastAutoInsightHistoryCount)
                return;

            _lastAutoInsightHistoryCount = History.Count;
            SendInsight(AutoInsightPrompt);
        }

        private void TriggerSubscriptionValidation()
        {
            var subscription = _config.GetActiveSubscription();
            if (subscription == null || !subscription.IsValid())
            {
                SubscriptionValidationState = SubscriptionValidationState.Unknown;
                SubscriptionValidationStatusMessage = "未配置有效订阅";
                return;
            }

            var version = Interlocked.Increment(ref _subscriptionValidationVersion);
            _subscriptionValidationCts?.Cancel();
            _subscriptionValidationCts?.Dispose();
            _subscriptionValidationCts = new CancellationTokenSource();
            var token = _subscriptionValidationCts.Token;

            SubscriptionValidationState = SubscriptionValidationState.Validating;
            SubscriptionValidationStatusMessage = $"正在验证订阅：{subscription.Name} ({subscription.ServiceRegion}) ...";

            _ = Task.Run(async () =>
            {
                var result = await _subscriptionValidator.ValidateAsync(subscription, token).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (version != _subscriptionValidationVersion)
                    {
                        return;
                    }

                    SubscriptionValidationState = result.IsValid
                        ? SubscriptionValidationState.Valid
                        : SubscriptionValidationState.Invalid;

                    SubscriptionValidationStatusMessage = result.IsValid
                        ? $"✓ 订阅可用：{subscription.Name} ({subscription.ServiceRegion})"
                        : $"✗ 订阅不可用：{subscription.Name} ({subscription.ServiceRegion}) - {result.Message}";
                });
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
        }        public void Dispose()
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


