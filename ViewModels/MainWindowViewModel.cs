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
using NAudio.Wave;

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
            _subtitleCues.CollectionChanged += (_, _) => UpdateSubtitleListHeight();

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

                LoadSubtitleFilesForAudio(value);
                LoadAudioForPlayback(value);
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

            ForceUpdateComboBoxSelection();
            ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SendInsightCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SendPresetInsightCommand).RaiseCanExecuteChanged();

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
                    token);
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
            if (_mainWindow == null) return;

            var configView = new AiConfigView(_config.AiConfig);
            var result = await configView.ShowDialog<bool>(_mainWindow);

            if (result)
            {
                _config.AiConfig = configView.Config;
                await _configService.SaveConfigAsync(_config);
                OnPropertyChanged(nameof(IsAiConfigured));
                ((RelayCommand)SendInsightCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendPresetInsightCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ToggleAutoInsightCommand).RaiseCanExecuteChanged();
                StatusMessage = "AI 配置已保存";
            }
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


