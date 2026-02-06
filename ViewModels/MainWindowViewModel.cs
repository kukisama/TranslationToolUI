using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using TranslationToolUI.Models;
using TranslationToolUI.Services;
using TranslationToolUI.Views;
using System.Threading.Tasks;
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
using System.Diagnostics;

namespace TranslationToolUI.ViewModels
{
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

        private int _audioSourceModeIndex;
        private readonly ObservableCollection<AudioDeviceInfo> _audioDevices;
        private AudioDeviceInfo? _selectedAudioDevice;
        private bool _isAudioDeviceSelectionEnabled;
        private bool _isAudioDeviceRefreshEnabled;
        private double _audioLevel;
        private readonly ObservableCollection<double> _audioLevelHistory;

        private readonly AzureSubscriptionValidator _subscriptionValidator = new();
        private SubscriptionValidationState _subscriptionValidationState = SubscriptionValidationState.Unknown;
        private string _subscriptionValidationStatusMessage = "";
        private CancellationTokenSource? _subscriptionValidationCts;
        private int _subscriptionValidationVersion;
        private bool _subscriptionLampBlinkOn = true;
        private readonly DispatcherTimer _subscriptionLampTimer;

        private FloatingSubtitleManager? _floatingSubtitleManager;

        private readonly string[] _sourceLanguages = { "zh-CN", "en-US", "ja-JP", "ko-KR", "fr-FR", "de-DE", "es-ES" };
        private readonly string[] _targetLanguages = { "en", "zh-CN", "ja-JP", "ko-KR", "fr-FR", "de-DE", "es-ES" };        public MainWindowViewModel()
        {
            _configService = new ConfigurationService();
            _config = new AzureSpeechConfig();
            _history = new ObservableCollection<TranslationItem>();
            _subscriptionNames = new ObservableCollection<string>();
            _audioDevices = new ObservableCollection<AudioDeviceInfo>();
            _audioLevelHistory = new ObservableCollection<double>(Enumerable.Repeat(0d, 24));

            _subscriptionLampTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) =>
            {
                if (_subscriptionValidationState == SubscriptionValidationState.Validating)
                {
                    _subscriptionLampBlinkOn = !_subscriptionLampBlinkOn;
                    OnPropertyChanged(nameof(SubscriptionLampOpacity));
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
        }

        public ICommand ToggleTranslationCommand { get; }

        public ICommand RefreshAudioDevicesCommand { get; }
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

                    ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ToggleTranslationCommand).RaiseCanExecuteChanged();

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

        public ObservableCollection<double> AudioLevelHistory => _audioLevelHistory;

        public double AudioLevel
        {
            get => _audioLevel;
            private set => SetProperty(ref _audioLevel, value);
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
            set => SetProperty(ref _currentOriginal, value);
        }

        public string CurrentTranslated
        {
            get => _currentTranslated;
            set => SetProperty(ref _currentTranslated, value);
        }

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

            var configView = new ConfigView(_config);

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
                }

                _floatingSubtitleManager.ToggleWindow();

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

            ForceUpdateComboBoxSelection();
            ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();

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
        }
    }
}


