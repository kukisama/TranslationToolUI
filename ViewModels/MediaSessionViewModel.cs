using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows.Input;
using Avalonia.Threading;
using TranslationToolUI.Models;
using TranslationToolUI.Services;

namespace TranslationToolUI.ViewModels
{
    /// <summary>
    /// 单会话 ViewModel — 聊天交互+任务管理
    /// </summary>
    public class MediaSessionViewModel : ViewModelBase
    {
        private readonly AiConfig _aiConfig;
        private readonly MediaGenConfig _genConfig;
        private readonly AiImageGenService _imageService;
        private readonly AiVideoGenService _videoService;
        private readonly Action _onTaskCountChanged;
        private readonly Action<MediaSessionViewModel>? _onRequestSave;
        private CancellationTokenSource _cts = new();

        public string SessionId { get; }

        private string _sessionName;
        public string SessionName
        {
            get => _sessionName;
            set => SetProperty(ref _sessionName, value);
        }

        public string SessionDirectory { get; }

        // --- 聊天记录 ---
        public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

        // --- 进行中的任务 ---
        public ObservableCollection<MediaGenTask> RunningTasks { get; } = new();

        /// <summary>
        /// 所有任务历史（含已完成、失败、取消的）
        /// </summary>
        public List<MediaGenTask> TaskHistory { get; } = new();

        public int RunningTaskCount => RunningTasks.Count;

        public bool IsBusy => RunningTasks.Count > 0;

        public bool HasBadge => RunningTasks.Count > 0;

        // --- 输入区 ---
        private string _promptText = "";
        public string PromptText
        {
            get => _promptText;
            set
            {
                if (SetProperty(ref _promptText, value))
                    ((RelayCommand)GenerateCommand).RaiseCanExecuteChanged();
            }
        }

        private MediaGenType _selectedType = MediaGenType.Image;
        public MediaGenType SelectedType
        {
            get => _selectedType;
            set
            {
                if (SetProperty(ref _selectedType, value))
                {
                    OnPropertyChanged(nameof(IsImageMode));
                    OnPropertyChanged(nameof(IsVideoMode));
                }
            }
        }

        public bool IsImageMode
        {
            get => SelectedType == MediaGenType.Image;
            set { if (value) SelectedType = MediaGenType.Image; }
        }

        public bool IsVideoMode
        {
            get => SelectedType == MediaGenType.Video;
            set { if (value) SelectedType = MediaGenType.Video; }
        }

        // 图片参数覆盖
        private string? _overrideImageSize;
        public string? OverrideImageSize
        {
            get => _overrideImageSize;
            set => SetProperty(ref _overrideImageSize, value);
        }

        private string? _overrideImageQuality;
        public string? OverrideImageQuality
        {
            get => _overrideImageQuality;
            set => SetProperty(ref _overrideImageQuality, value);
        }

        private string? _overrideImageFormat;
        public string? OverrideImageFormat
        {
            get => _overrideImageFormat;
            set => SetProperty(ref _overrideImageFormat, value);
        }

        private int? _overrideImageCount;
        public int? OverrideImageCount
        {
            get => _overrideImageCount;
            set => SetProperty(ref _overrideImageCount, value);
        }

        // 视频参数覆盖
        private string _selectedVideoAspectRatio = "16:9";
        public string SelectedVideoAspectRatio
        {
            get => _selectedVideoAspectRatio;
            set => SetProperty(ref _selectedVideoAspectRatio, value);
        }

        private string _selectedVideoResolution = "480p";
        public string SelectedVideoResolution
        {
            get => _selectedVideoResolution;
            set => SetProperty(ref _selectedVideoResolution, value);
        }

        private int? _overrideVideoSeconds;
        public int? OverrideVideoSeconds
        {
            get => _overrideVideoSeconds;
            set => SetProperty(ref _overrideVideoSeconds, value);
        }

        private int? _overrideVideoVariants;
        public int? OverrideVideoVariants
        {
            get => _overrideVideoVariants;
            set => SetProperty(ref _overrideVideoVariants, value);
        }

        // --- 状态指示 ---
        private string _statusText = "就绪";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (SetProperty(ref _isGenerating, value))
                {
                    ((RelayCommand)GenerateCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
                }
            }
        }

        // --- 命令 ---
        public ICommand GenerateCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand OpenFileCommand { get; }

        // --- 参数选项 ---
        public List<string> ImageSizeOptions { get; } = new()
        {
            "1024x1024", "1024x1536", "1536x1024"
        };

        public List<string> ImageQualityOptions { get; } = new()
        {
            "low", "medium", "high"
        };

        public List<string> ImageFormatOptions { get; } = new()
        {
            "png", "jpeg"
        };

        public List<int> ImageCountOptions { get; } = new()
        {
            1, 2, 3, 4, 5
        };

        public List<string> VideoAspectRatioOptions { get; } = new()
        {
            "1:1", "16:9", "9:16"
        };

        public List<string> VideoResolutionOptions { get; } = new()
        {
            "480p", "720p", "1080p"
        };

        public List<int> VideoDurationOptions { get; } = new()
        {
            5, 10, 15, 20
        };

        public List<int> VideoCountOptions { get; } = new()
        {
            1, 2
        };

        public MediaSessionViewModel(
            string sessionId,
            string sessionName,
            string sessionDirectory,
            AiConfig aiConfig,
            MediaGenConfig genConfig,
            AiImageGenService imageService,
            AiVideoGenService videoService,
            Action onTaskCountChanged,
            Action<MediaSessionViewModel>? onRequestSave = null)
        {
            SessionId = sessionId;
            _sessionName = sessionName;
            SessionDirectory = sessionDirectory;
            _aiConfig = aiConfig;
            _genConfig = genConfig;
            _imageService = imageService;
            _videoService = videoService;
            _onTaskCountChanged = onTaskCountChanged;
            _onRequestSave = onRequestSave;

            GenerateCommand = new RelayCommand(
                _ => Generate(),
                _ => !IsGenerating && !string.IsNullOrWhiteSpace(PromptText));

            CancelCommand = new RelayCommand(
                _ => CancelAll(),
                _ => IsGenerating);

            OpenFileCommand = new RelayCommand(
                param => OpenFile(param as string));

            OverrideImageSize = "1024x1024";
            OverrideImageQuality = "medium";
            OverrideImageFormat = "png";
            OverrideImageCount = 1;

            SelectedVideoAspectRatio = "16:9";
            SelectedVideoResolution = "480p";
            OverrideVideoSeconds = 5;
            OverrideVideoVariants = 1;

            RunningTasks.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(RunningTaskCount));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(HasBadge));
                _onTaskCountChanged?.Invoke();
            };
        }

        public void Generate()
        {
            if (string.IsNullOrWhiteSpace(PromptText)) return;

            var prompt = PromptText.Trim();
            PromptText = "";

            // 添加用户消息
            Messages.Add(new ChatMessageViewModel(new MediaChatMessage
            {
                Role = "user",
                Text = prompt,
                Timestamp = DateTime.Now
            }));

            if (SelectedType == MediaGenType.Image)
            {
                GenerateImage(prompt);
            }
            else
            {
                GenerateVideo(prompt);
            }
        }

        private void GenerateImage(string prompt)
        {
            var task = new MediaGenTask
            {
                Type = MediaGenType.Image,
                Status = MediaGenStatus.Running,
                Prompt = prompt
            };

            RunningTasks.Add(task);
            TaskHistory.Add(task);
            IsGenerating = true;
            StatusText = "生成图片中...";

            var loadingMessage = new ChatMessageViewModel(new MediaChatMessage
            {
                Role = "assistant",
                Text = "已提交提示词，生成中...",
                Timestamp = DateTime.Now
            })
            { IsLoading = true };
            Messages.Add(loadingMessage);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            // 启动计时器显示
            var timer = new System.Threading.Timer(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (loadingMessage.IsLoading)
                    {
                        var elapsed = stopwatch.Elapsed;
                        loadingMessage.Text = $"生成中... 已耗时 {elapsed.TotalSeconds:F0} 秒";
                    }
                });
            }, null, 1000, 1000);

            // 构建有效的配置（覆盖参数优先）
            var effectiveConfig = new MediaGenConfig
            {
                ImageModel = _genConfig.ImageModel,
                ImageSize = OverrideImageSize ?? _genConfig.ImageSize,
                ImageQuality = OverrideImageQuality ?? _genConfig.ImageQuality,
                ImageFormat = OverrideImageFormat ?? _genConfig.ImageFormat,
                ImageCount = OverrideImageCount ?? _genConfig.ImageCount
            };

            var imageConfig = BuildImageAiConfig();

            var ct = _cts.Token;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await _imageService.GenerateAndSaveImagesAsync(
                        imageConfig, prompt, effectiveConfig, SessionDirectory, ct,
                        p => Dispatcher.UIThread.Post(() =>
                        {
                            task.Progress = p;
                            var elapsed = stopwatch.Elapsed.TotalSeconds;
                            var phaseText = p switch
                            {
                                < 50 => $"等待服务端生成... 已耗时 {elapsed:F0}s",
                                < 96 => $"下载图片数据中... {p}%  已耗时 {elapsed:F0}s",
                                < 100 => $"解析并保存中... 已耗时 {elapsed:F0}s",
                                _ => "图片生成完成"
                            };
                            StatusText = phaseText;
                            // 同步到聊天气泡
                            if (loadingMessage.IsLoading)
                                loadingMessage.Text = phaseText;
                        }));

                    var totalSeconds = stopwatch.Elapsed.TotalSeconds;
                    stopwatch.Stop();
                    timer.Dispose();

                    Dispatcher.UIThread.Post(() =>
                    {
                        task.Status = MediaGenStatus.Completed;
                        task.ResultFilePath = result.FilePaths.FirstOrDefault();

                        loadingMessage.IsLoading = false;
                        loadingMessage.GenerateSeconds = result.GenerateSeconds;
                        loadingMessage.DownloadSeconds = result.DownloadSeconds;
                        loadingMessage.Text = $"已生成 {result.FilePaths.Count} 张图片 (生成 {result.GenerateSeconds:F1}s + 下载 {result.DownloadSeconds:F1}s = 总计 {totalSeconds:F1}s)";
                        loadingMessage.MediaPaths.Clear();
                        foreach (var path in result.FilePaths)
                        {
                            loadingMessage.MediaPaths.Add(path);
                        }

                        RunningTasks.Remove(task);
                        UpdateGeneratingState();
                        _onRequestSave?.Invoke(this);
                    });
                }
                catch (OperationCanceledException)
                {
                    stopwatch.Stop();
                    timer.Dispose();
                    Dispatcher.UIThread.Post(() =>
                    {
                        task.Status = MediaGenStatus.Cancelled;
                        loadingMessage.IsLoading = false;
                        loadingMessage.Text = "已取消生成";
                        RunningTasks.Remove(task);
                        UpdateGeneratingState();
                        StatusText = "已取消";
                        _onRequestSave?.Invoke(this);
                    });
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    timer.Dispose();
                    var elapsedSec = stopwatch.Elapsed.TotalSeconds;
                    Dispatcher.UIThread.Post(() =>
                    {
                        task.Status = MediaGenStatus.Failed;
                        task.ErrorMessage = ex.Message;

                        loadingMessage.IsLoading = false;
                        loadingMessage.Text = $"❌ 图片生成失败 (耗时 {elapsedSec:F1}秒): {ex.Message}";

                        RunningTasks.Remove(task);
                        UpdateGeneratingState();
                        _onRequestSave?.Invoke(this);
                    });
                }
            }, ct);
        }

        private void GenerateVideo(string prompt)
        {
            var task = new MediaGenTask
            {
                Type = MediaGenType.Video,
                Status = MediaGenStatus.Running,
                Prompt = prompt
            };

            RunningTasks.Add(task);
            TaskHistory.Add(task);
            IsGenerating = true;
            StatusText = "创建视频任务中...";

            var loadingMessage = new ChatMessageViewModel(new MediaChatMessage
            {
                Role = "assistant",
                Text = "已提交提示词，生成中...",
                Timestamp = DateTime.Now
            })
            { IsLoading = true };
            Messages.Add(loadingMessage);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var timer = new System.Threading.Timer(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (loadingMessage.IsLoading)
                    {
                        var elapsed = stopwatch.Elapsed;
                        loadingMessage.Text = $"生成中... 已耗时 {elapsed.TotalSeconds:F0} 秒";
                    }
                });
            }, null, 1000, 1000);

            var (videoWidth, videoHeight) = GetVideoDimensions(
                SelectedVideoAspectRatio,
                SelectedVideoResolution);

            var effectiveConfig = new MediaGenConfig
            {
                VideoModel = _genConfig.VideoModel,
                VideoWidth = videoWidth,
                VideoHeight = videoHeight,
                VideoSeconds = OverrideVideoSeconds ?? _genConfig.VideoSeconds,
                VideoVariants = OverrideVideoVariants ?? _genConfig.VideoVariants,
                VideoPollIntervalMs = _genConfig.VideoPollIntervalMs
            };

            var videoConfig = BuildVideoAiConfig();

            var randomId = Guid.NewGuid().ToString("N")[..8];
            var outputPath = Path.Combine(SessionDirectory, $"vid_001_{randomId}.mp4");

            var ct = _cts.Token;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await _videoService.GenerateVideoAsync(
                        videoConfig, prompt, effectiveConfig, outputPath, ct,
                        p => Dispatcher.UIThread.Post(() =>
                        {
                            task.Progress = p;
                            StatusText = p < 100 ? $"生成视频中... {p}%" : "视频生成完成";
                        }),
                        videoId => Dispatcher.UIThread.Post(() =>
                        {
                            task.RemoteVideoId = videoId;
                            StatusText = $"视频任务已创建，等待生成... (ID: {videoId})";
                            _onRequestSave?.Invoke(this);
                        }));

                    Dispatcher.UIThread.Post(() =>
                    {
                        task.Status = MediaGenStatus.Completed;
                        task.ResultFilePath = outputPath;

                        var elapsedSec = stopwatch.Elapsed.TotalSeconds;
                        stopwatch.Stop();
                        timer.Dispose();

                        loadingMessage.IsLoading = false;
                        loadingMessage.Text = $"✅ 视频已生成，耗时 {elapsedSec:F1} 秒";
                        loadingMessage.MediaPaths.Clear();
                        loadingMessage.MediaPaths.Add(outputPath);

                        RunningTasks.Remove(task);
                        UpdateGeneratingState();
                        _onRequestSave?.Invoke(this);
                    });
                }
                catch (OperationCanceledException)
                {
                    stopwatch.Stop();
                    timer.Dispose();
                    Dispatcher.UIThread.Post(() =>
                    {
                        task.Status = MediaGenStatus.Cancelled;
                        loadingMessage.IsLoading = false;
                        loadingMessage.Text = "已取消生成";
                        RunningTasks.Remove(task);
                        UpdateGeneratingState();
                        StatusText = "已取消";
                        _onRequestSave?.Invoke(this);
                    });
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    timer.Dispose();
                    var elapsedSec = stopwatch.Elapsed.TotalSeconds;
                    Dispatcher.UIThread.Post(() =>
                    {
                        task.Status = MediaGenStatus.Failed;
                        task.ErrorMessage = ex.Message;

                        loadingMessage.IsLoading = false;
                        loadingMessage.Text = $"❌ 视频生成失败 (耗时 {elapsedSec:F1}秒): {ex.Message}";

                        RunningTasks.Remove(task);
                        UpdateGeneratingState();
                        _onRequestSave?.Invoke(this);
                    });
                }
            }, ct);
        }

        /// <summary>
        /// 恢复一个中断的视频任务（基于 RemoteVideoId 继续轮询+下载）
        /// </summary>
        public void ResumeVideoTask(MediaGenTask task)
        {
            if (string.IsNullOrEmpty(task.RemoteVideoId))
            {
                task.Status = MediaGenStatus.Failed;
                task.ErrorMessage = "无法恢复：缺少 RemoteVideoId";
                return;
            }

            task.Status = MediaGenStatus.Running;
            RunningTasks.Add(task);
            // 如果 TaskHistory 已包含此 task（从持久化恢复的情况），不重复添加
            if (!TaskHistory.Contains(task))
                TaskHistory.Add(task);
            IsGenerating = true;
            StatusText = $"恢复视频任务... (ID: {task.RemoteVideoId})";

            var videoConfig = BuildVideoAiConfig();
            var randomId = Guid.NewGuid().ToString("N")[..8];
            var outputPath = task.ResultFilePath
                ?? Path.Combine(SessionDirectory, $"vid_resume_{randomId}.mp4");

            var effectiveConfig = new MediaGenConfig
            {
                VideoPollIntervalMs = _genConfig.VideoPollIntervalMs
            };

            // 添加恢复中的提示消息
            var loadingMessage = new ChatMessageViewModel(new MediaChatMessage
            {
                Role = "assistant",
                Text = $"恢复视频生成... (ID: {task.RemoteVideoId})",
                Timestamp = DateTime.Now
            })
            { IsLoading = true };
            Messages.Add(loadingMessage);

            var ct = _cts.Token;
            var videoId = task.RemoteVideoId;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    // 直接从轮询开始（跳过创建步骤）
                    var retryCount = 0;
                    const int maxRetries = 3;

                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            var (status, progress) = await _videoService.PollStatusAsync(
                                videoConfig, videoId, ct);
                            Dispatcher.UIThread.Post(() =>
                            {
                                task.Progress = progress;
                                StatusText = progress < 100
                                    ? $"恢复视频中... {progress}%"
                                    : "视频生成完成";
                            });
                            retryCount = 0;

                            if (status == "completed") break;
                            if (status == "failed")
                                throw new InvalidOperationException("视频生成失败");

                            await System.Threading.Tasks.Task.Delay(
                                effectiveConfig.VideoPollIntervalMs, ct);
                        }
                        catch (HttpRequestException) when (retryCount < maxRetries)
                        {
                            retryCount++;
                            await System.Threading.Tasks.Task.Delay(
                                effectiveConfig.VideoPollIntervalMs, ct);
                        }
                    }

                    ct.ThrowIfCancellationRequested();

                    await _videoService.DownloadVideoAsync(videoConfig, videoId, outputPath, ct);

                    Dispatcher.UIThread.Post(() =>
                    {
                        task.Status = MediaGenStatus.Completed;
                        task.ResultFilePath = outputPath;

                        loadingMessage.IsLoading = false;
                        loadingMessage.Text = "✅ 视频已恢复生成";
                        loadingMessage.MediaPaths.Clear();
                        loadingMessage.MediaPaths.Add(outputPath);

                        RunningTasks.Remove(task);
                        UpdateGeneratingState();
                        _onRequestSave?.Invoke(this);
                    });
                }
                catch (OperationCanceledException)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        task.Status = MediaGenStatus.Cancelled;
                        loadingMessage.IsLoading = false;
                        loadingMessage.Text = "已取消恢复";
                        RunningTasks.Remove(task);
                        UpdateGeneratingState();
                        _onRequestSave?.Invoke(this);
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        task.Status = MediaGenStatus.Failed;
                        task.ErrorMessage = ex.Message;
                        loadingMessage.IsLoading = false;
                        loadingMessage.Text = $"❌ 视频恢复失败: {ex.Message}";
                        RunningTasks.Remove(task);
                        UpdateGeneratingState();
                        _onRequestSave?.Invoke(this);
                    });
                }
            }, ct);
        }

        private void UpdateGeneratingState()
        {
            IsGenerating = RunningTasks.Count > 0;
            if (!IsGenerating)
                StatusText = "就绪";
        }

        public void CancelAll()
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();

            foreach (var t in RunningTasks.ToList())
            {
                t.Status = MediaGenStatus.Cancelled;
            }

            RunningTasks.Clear();
            IsGenerating = false;
            StatusText = "已取消所有任务";
        }

        private void OpenFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"打开文件失败: {ex.Message}");
            }
        }

        private AiConfig BuildImageAiConfig()
        {
            var useFallback = string.IsNullOrWhiteSpace(_genConfig.ImageApiEndpoint);

            return new AiConfig
            {
                ProviderType = useFallback ? _aiConfig.ProviderType : _genConfig.ImageProviderType,
                ApiEndpoint = useFallback ? _aiConfig.ApiEndpoint : _genConfig.ImageApiEndpoint,
                ApiKey = useFallback ? _aiConfig.ApiKey : _genConfig.ImageApiKey,
                DeploymentName = _genConfig.ImageModel,
                ModelName = _genConfig.ImageModel,
                ApiVersion = string.IsNullOrWhiteSpace(_aiConfig.ApiVersion)
                    ? "2024-02-01"
                    : _aiConfig.ApiVersion,
                AzureAuthMode = useFallback ? _aiConfig.AzureAuthMode : _genConfig.ImageAzureAuthMode,
                AzureTenantId = useFallback ? _aiConfig.AzureTenantId : _genConfig.ImageAzureTenantId,
                AzureClientId = useFallback ? _aiConfig.AzureClientId : _genConfig.ImageAzureClientId
            };
        }

        private AiConfig BuildVideoAiConfig()
        {
            AiConfig config;

            if (_genConfig.VideoUseImageEndpoint)
            {
                config = BuildImageAiConfig();
            }
            else
            {
                var useFallback = string.IsNullOrWhiteSpace(_genConfig.VideoApiEndpoint);

                config = new AiConfig
                {
                    ProviderType = useFallback ? _aiConfig.ProviderType : _genConfig.VideoProviderType,
                    ApiEndpoint = useFallback ? _aiConfig.ApiEndpoint : _genConfig.VideoApiEndpoint,
                    ApiKey = useFallback ? _aiConfig.ApiKey : _genConfig.VideoApiKey,
                    ApiVersion = string.IsNullOrWhiteSpace(_aiConfig.ApiVersion)
                        ? "2024-02-01"
                        : _aiConfig.ApiVersion,
                    AzureAuthMode = useFallback ? _aiConfig.AzureAuthMode : _genConfig.VideoAzureAuthMode,
                    AzureTenantId = useFallback ? _aiConfig.AzureTenantId : _genConfig.VideoAzureTenantId,
                    AzureClientId = useFallback ? _aiConfig.AzureClientId : _genConfig.VideoAzureClientId
                };
            }

            config.DeploymentName = _genConfig.VideoModel;
            config.ModelName = _genConfig.VideoModel;
            return config;
        }

        private static (int Width, int Height) GetVideoDimensions(string aspectRatio, string resolution)
        {
            var baseSize = resolution switch
            {
                "1080p" => 1080,
                "720p" => 720,
                _ => 480
            };

            return aspectRatio switch
            {
                "1:1" => (baseSize, baseSize),
                "9:16" => (baseSize, (int)Math.Ceiling(baseSize * 16.0 / 9.0)),
                _ => ((int)Math.Ceiling(baseSize * 16.0 / 9.0), baseSize)
            };
        }
    }

    /// <summary>
    /// 聊天消息 ViewModel
    /// </summary>
    public class ChatMessageViewModel : ViewModelBase
    {
        public string Role { get; }

        private string _text;
        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        public ObservableCollection<string> MediaPaths { get; }

        public DateTime Timestamp { get; }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        /// <summary>服务端生成耗时（秒）</summary>
        public double? GenerateSeconds { get; set; }
        /// <summary>下载传输耗时（秒）</summary>
        public double? DownloadSeconds { get; set; }

        public bool IsUser => Role == "user";
        public bool IsAssistant => Role != "user";
        public bool HasMedia => MediaPaths.Count > 0;

        public string TimestampText => Timestamp.ToString("HH:mm:ss");

        public ChatMessageViewModel(MediaChatMessage message)
        {
            Role = message.Role;
            _text = message.Text;
            MediaPaths = new ObservableCollection<string>(message.MediaPaths);
            Timestamp = message.Timestamp;
            GenerateSeconds = message.GenerateSeconds;
            DownloadSeconds = message.DownloadSeconds;

            MediaPaths.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasMedia));
            };
        }
    }
}
