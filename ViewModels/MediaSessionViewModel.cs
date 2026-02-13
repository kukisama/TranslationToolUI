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
        private readonly SemaphoreSlim _videoFrameBackfillLock = new(1, 1);
        private const int MaxReferenceImageCount = 8;

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

        public ObservableCollection<string> ReferenceImagePaths { get; } = new();

        public string? ReferenceImagePath => ReferenceImagePaths.FirstOrDefault();

        public int ReferenceImageCount => ReferenceImagePaths.Count;

        public bool HasReferenceImage => ReferenceImagePaths.Count > 0;

        public bool CanAddMoreReferenceImages => ReferenceImagePaths.Count < MaxReferenceImageCount;

        public bool IsVideoReferenceLimitExceeded => IsVideoMode && ReferenceImagePaths.Count > 1;

        public string VideoReferenceLimitHint => $"Sora 当前仅支持 1 张参考图，已选择 {ReferenceImagePaths.Count} 张";

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
                    OnPropertyChanged(nameof(IsVideoReferenceLimitExceeded));
                    OnPropertyChanged(nameof(VideoReferenceLimitHint));
                    ((RelayCommand)GenerateCommand).RaiseCanExecuteChanged();
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
        public ICommand DeleteMessageCommand { get; }
        public ICommand RemoveReferenceImageCommand { get; }

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

        public List<string> VideoAspectRatioOptions { get; private set; } = new()
        {
            "1:1", "16:9", "9:16"
        };

        public List<string> VideoResolutionOptions { get; private set; } = new()
        {
            "480p", "720p", "1080p"
        };

        public List<int> VideoDurationOptions { get; private set; } = new()
        {
            5, 10, 15, 20
        };

        public List<int> VideoCountOptions { get; private set; } = new()
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
                _ => CanGenerateNow());

            CancelCommand = new RelayCommand(
                _ => CancelAll(),
                _ => IsGenerating);

            OpenFileCommand = new RelayCommand(
                param => OpenFile(param as string));

            DeleteMessageCommand = new RelayCommand(
                param => DeleteMessage(param as ChatMessageViewModel),
                param => param is ChatMessageViewModel m && !m.IsLoading);

            RemoveReferenceImageCommand = new RelayCommand(
                p => RemoveReferenceImage(p as string),
                _ => HasReferenceImage);

            OverrideImageSize = "1024x1024";
            OverrideImageQuality = "medium";
            OverrideImageFormat = "png";
            OverrideImageCount = 1;

            // 根据当前 API 模式初始化视频参数选项
            RefreshVideoParameterOptions();

            // 设置视频参数默认值
            OverrideVideoSeconds = VideoDurationOptions[0];
            OverrideVideoVariants = VideoCountOptions[0];

            RunningTasks.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(RunningTaskCount));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(HasBadge));
                _onTaskCountChanged?.Invoke();
            };

            ReferenceImagePaths.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(ReferenceImagePath));
                OnPropertyChanged(nameof(ReferenceImageCount));
                OnPropertyChanged(nameof(HasReferenceImage));
                OnPropertyChanged(nameof(CanAddMoreReferenceImages));
                OnPropertyChanged(nameof(IsVideoReferenceLimitExceeded));
                OnPropertyChanged(nameof(VideoReferenceLimitHint));
                (RemoveReferenceImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
                ((RelayCommand)GenerateCommand).RaiseCanExecuteChanged();
            };
        }

        private bool CanGenerateNow()
        {
            if (IsGenerating || string.IsNullOrWhiteSpace(PromptText))
                return false;

            if (IsVideoMode && ReferenceImagePaths.Count > 1)
                return false;

            return true;
        }

        /// <summary>
        /// 删除一条聊天记录（只删除记录，不删除磁盘上的媒体文件）。
        /// </summary>
        public void DeleteMessage(ChatMessageViewModel? message)
        {
            if (message == null)
                return;
            if (message.IsLoading)
                return;

            if (Messages.Contains(message))
            {
                Messages.Remove(message);
                _onRequestSave?.Invoke(this);
            }
        }

        /// <summary>
        /// 根据当前 VideoApiMode（sora / sora-2）刷新视频参数选项。
        /// sora: 全参数（1:1/16:9/9:16，480p/720p/1080p，5/10/15/20秒，1/2数量）
        /// sora-2: 仅 16:9/9:16，720p，4/8/12秒，无数量选择
        /// </summary>
        public void RefreshVideoParameterOptions()
        {
            var isSora2 = _genConfig.VideoApiMode == VideoApiMode.Videos;

            if (isSora2)
            {
                VideoAspectRatioOptions = new List<string> { "16:9", "9:16" };
                VideoResolutionOptions = new List<string> { "720p" };
                VideoDurationOptions = new List<int> { 4, 8, 12 };
                VideoCountOptions = new List<int> { 1 };
            }
            else
            {
                VideoAspectRatioOptions = new List<string> { "1:1", "16:9", "9:16" };
                VideoResolutionOptions = new List<string> { "480p", "720p", "1080p" };
                VideoDurationOptions = new List<int> { 5, 10, 15, 20 };
                VideoCountOptions = new List<int> { 1, 2 };
            }

            OnPropertyChanged(nameof(VideoAspectRatioOptions));
            OnPropertyChanged(nameof(VideoResolutionOptions));
            OnPropertyChanged(nameof(VideoDurationOptions));
            OnPropertyChanged(nameof(VideoCountOptions));

            // 确保当前选中值在新选项中有效
            if (!VideoAspectRatioOptions.Contains(SelectedVideoAspectRatio))
                SelectedVideoAspectRatio = VideoAspectRatioOptions[0];
            if (!VideoResolutionOptions.Contains(SelectedVideoResolution))
                SelectedVideoResolution = VideoResolutionOptions[0];
            if (OverrideVideoSeconds.HasValue && !VideoDurationOptions.Contains(OverrideVideoSeconds.Value))
                OverrideVideoSeconds = VideoDurationOptions[0];
            if (OverrideVideoVariants.HasValue && !VideoCountOptions.Contains(OverrideVideoVariants.Value))
                OverrideVideoVariants = VideoCountOptions[0];
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
                        ReferenceImagePaths.ToList(),
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

                        // 图片生成完成后清空参考图（避免下一次误用）
                        ClearReferenceImage(silent: true);

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
                Text = "生成中 0秒",
                Timestamp = DateTime.Now
            })
            { IsLoading = true };
            Messages.Add(loadingMessage);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string currentApiStatus = ""; // 记录当前 API 返回的状态
            bool isDownloading = false;   // 标记是否已进入下载阶段
            double recordedGenerateSeconds = 0;
            var downloadStopwatch = new System.Diagnostics.Stopwatch();
            var timer = new System.Threading.Timer(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (loadingMessage.IsLoading)
                    {
                        if (isDownloading)
                        {
                            // 阶段3：succeeded 后下载中
                            loadingMessage.Text = $"生成进度 succeeded 耗时{recordedGenerateSeconds:F0}秒，下载中 {downloadStopwatch.Elapsed.TotalSeconds:F0}秒";
                        }
                        else if (!string.IsNullOrEmpty(currentApiStatus))
                        {
                            // 阶段2：有状态了
                            loadingMessage.Text = $"生成进度 {currentApiStatus} {stopwatch.Elapsed.TotalSeconds:F0}秒";
                        }
                        else
                        {
                            // 阶段1：刚提交
                            loadingMessage.Text = $"生成中 {stopwatch.Elapsed.TotalSeconds:F0}秒";
                        }
                    }
                });
            }, null, 1000, 1000);

            var (videoWidth, videoHeight) = GetVideoDimensions(
                SelectedVideoAspectRatio,
                SelectedVideoResolution);

            var effectiveConfig = new MediaGenConfig
            {
                VideoModel = _genConfig.VideoModel,
                VideoApiMode = _genConfig.VideoApiMode,
                VideoWidth = videoWidth,
                VideoHeight = videoHeight,
                VideoSeconds = OverrideVideoSeconds ?? _genConfig.VideoSeconds,
                VideoVariants = OverrideVideoVariants ?? _genConfig.VideoVariants,
                VideoPollIntervalMs = _genConfig.VideoPollIntervalMs
            };

            // 记录该任务创建时的模式，方便重启/恢复时走同一路径。
            task.RemoteVideoApiMode = effectiveConfig.VideoApiMode;

            var videoConfig = BuildVideoAiConfig();

            var randomId = Guid.NewGuid().ToString("N")[..8];
            var outputPath = Path.Combine(SessionDirectory, $"vid_001_{randomId}.mp4");

            var ct = _cts.Token;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var (_, generateSec, downloadSec, downloadUrl) = await _videoService.GenerateVideoAsync(
                        videoConfig, prompt, effectiveConfig, outputPath, ct,
                        ReferenceImagePath,
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
                        }),
                        status => Dispatcher.UIThread.Post(() =>
                        {
                            currentApiStatus = status;
                            StatusText = $"视频状态: {status}";
                        }),
                        genId => Dispatcher.UIThread.Post(() =>
                        {
                            task.RemoteGenerationId = genId;

                            // 拿到 generationId 就可以提前构造并写入下载 URL（不等到真正下载完成）
                            if (!string.IsNullOrWhiteSpace(task.RemoteVideoId))
                            {
                                var candidates = _videoService.BuildDownloadCandidateUrls(
                                    videoConfig,
                                    task.RemoteVideoId,
                                    genId,
                                    effectiveConfig.VideoApiMode);
                                var preferred = candidates.Count > 0 ? candidates[0] : null;
                                if (!string.IsNullOrWhiteSpace(preferred))
                                {
                                    task.RemoteDownloadUrl = preferred;
                                }
                            }
                            _onRequestSave?.Invoke(this);
                        }),
                        genSeconds => Dispatcher.UIThread.Post(() =>
                        {
                            // succeeded → 记录生成耗时，切换到下载阶段
                            recordedGenerateSeconds = genSeconds;
                            task.GenerateSeconds = genSeconds;
                            isDownloading = true;
                            downloadStopwatch.Start();
                            _onRequestSave?.Invoke(this);
                        }));

                    var frameResult = await VideoFrameExtractorService.TryExtractFirstAndLastFrameAsync(outputPath, ct);

                    Dispatcher.UIThread.Post(() =>
                    {
                        task.Status = MediaGenStatus.Completed;
                        task.ResultFilePath = outputPath;
                        task.GenerateSeconds = generateSec;
                        task.DownloadSeconds = downloadSec;
                        task.RemoteDownloadUrl = downloadUrl;

                        stopwatch.Stop();
                        timer.Dispose();

                        loadingMessage.IsLoading = false;
                        loadingMessage.GenerateSeconds = generateSec;
                        loadingMessage.DownloadSeconds = downloadSec;
                        var totalSec = generateSec + downloadSec;
                        loadingMessage.Text = $"✅ 视频已生成（AI生成 {generateSec:F1}s + 下载 {downloadSec:F1}s = 总计 {totalSec:F1}s）";
                        loadingMessage.MediaPaths.Clear();
                        if (!string.IsNullOrWhiteSpace(frameResult.FirstFramePath))
                        {
                            loadingMessage.MediaPaths.Add(frameResult.FirstFramePath);
                        }
                        if (!string.IsNullOrWhiteSpace(frameResult.LastFramePath))
                        {
                            loadingMessage.MediaPaths.Add(frameResult.LastFramePath);
                        }
                        if (loadingMessage.MediaPaths.Count == 0)
                        {
                            loadingMessage.MediaPaths.Add(outputPath);
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
        /// 若 RemoteGenerationId 已存在，则跳过轮询直接下载。
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

            var apiMode = task.RemoteVideoApiMode ?? _genConfig.VideoApiMode;

            // 添加恢复中的提示消息
            var loadingMessage = new ChatMessageViewModel(new MediaChatMessage
            {
                Role = "assistant",
                Text = $"生成中 0秒 (恢复 ID: {task.RemoteVideoId})",
                Timestamp = DateTime.Now
            })
            { IsLoading = true };
            Messages.Add(loadingMessage);


            var ct = _cts.Token;
            var videoId = task.RemoteVideoId;
            var existingGenId = task.RemoteGenerationId;

            // 如果已知 generationId，提前写入 RemoteDownloadUrl（不等到真正下载完成）
            if (!string.IsNullOrEmpty(existingGenId))
            {
                var candidates = _videoService.BuildDownloadCandidateUrls(
                    videoConfig,
                    videoId,
                    existingGenId,
                    apiMode);
                var preferred = candidates.Count > 0 ? candidates[0] : null;
                if (!string.IsNullOrWhiteSpace(preferred))
                {
                    task.RemoteDownloadUrl = preferred;
                    _onRequestSave?.Invoke(this);
                }
            }

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var generateSw = System.Diagnostics.Stopwatch.StartNew();
                var downloadSw = new System.Diagnostics.Stopwatch();
                bool isDownloadPhase = false;
                double recordedGenSec = 0;
                try
                {
                    string? generationId = existingGenId;
                    // 用 Timer 定时刷新显示文字（生成+下载阶段都可用）
                    string currentStatus = "";
                    using var pollTimer = new System.Threading.Timer(_ =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (loadingMessage.IsLoading)
                            {
                                if (isDownloadPhase)
                                {
                                    loadingMessage.Text = $"生成进度 succeeded 耗时{recordedGenSec:F0}秒，下载中 {downloadSw.Elapsed.TotalSeconds:F0}秒";
                                }
                                else if (!string.IsNullOrEmpty(currentStatus))
                                {
                                    loadingMessage.Text = $"生成进度 {currentStatus} {generateSw.Elapsed.TotalSeconds:F0}秒";
                                }
                                else
                                {
                                    loadingMessage.Text = $"生成中 {generateSw.Elapsed.TotalSeconds:F0}秒";
                                }
                            }
                        });
                    }, null, 1000, 1000);

                    // 如果已有 generationId，跳过轮询直接下载
                    if (!string.IsNullOrEmpty(generationId))
                    {
                        generateSw.Stop(); // 无需生成等待
                        isDownloadPhase = true;
                        downloadSw.Start();
                        Dispatcher.UIThread.Post(() =>
                        {
                            loadingMessage.Text = $"生成进度 succeeded 耗时0秒，下载中 0秒";
                            StatusText = "跳过轮询，直接下载视频...";
                        });
                    }
                    else
                    {
                        var retryCount = 0;
                        const int maxRetries = 3;
                        while (!ct.IsCancellationRequested)
                        {
                            try
                            {
                                var (status, progress, genId, failureReason) = await _videoService.PollStatusDetailsAsync(
                                    videoConfig, videoId, ct, apiMode);
                                Dispatcher.UIThread.Post(() =>
                                {
                                    task.Progress = progress;
                                    currentStatus = status;
                                    StatusText = $"视频状态: {status}";
                                });
                                retryCount = 0;

                                if (!string.IsNullOrWhiteSpace(genId))
                                {
                                    generationId = genId;
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        task.RemoteGenerationId = genId;

                                        // 解析到 generationId 就立刻构造并写入下载 URL，便于恢复
                                        var candidates = _videoService.BuildDownloadCandidateUrls(
                                            videoConfig,
                                            videoId,
                                            genId,
                                            apiMode);
                                        var preferred = candidates.Count > 0 ? candidates[0] : null;
                                        if (!string.IsNullOrWhiteSpace(preferred))
                                        {
                                            task.RemoteDownloadUrl = preferred;
                                        }
                                        _onRequestSave?.Invoke(this);
                                    });
                                }

                                if (status is "succeeded" or "completed" or "success")
                                {
                                    generateSw.Stop();
                                    recordedGenSec = generateSw.Elapsed.TotalSeconds;
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        task.GenerateSeconds = recordedGenSec;
                                        _onRequestSave?.Invoke(this);
                                    });
                                    break;
                                }
                                if (status is "failed" or "error" or "cancelled" or "canceled")
                                {
                                    var detail = string.IsNullOrWhiteSpace(failureReason)
                                        ? status
                                        : $"{status} ({failureReason})";
                                    throw new InvalidOperationException($"视频生成失败: {detail}");
                                }

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
                        // 进入下载阶段
                        isDownloadPhase = true;
                        downloadSw.Start();
                        Dispatcher.UIThread.Post(() =>
                        {
                            loadingMessage.Text = $"生成进度 succeeded 耗时{recordedGenSec:F0}秒，下载中 0秒";
                            StatusText = "下载视频中...";
                        });
                    }

                    var dlUrl = await _videoService.DownloadVideoAsync(videoConfig, videoId, outputPath, ct,
                        generationId, apiMode);
                    downloadSw.Stop();

                    var frameResult = await VideoFrameExtractorService.TryExtractFirstAndLastFrameAsync(outputPath, ct);

                    var genSec = recordedGenSec > 0 ? recordedGenSec : generateSw.Elapsed.TotalSeconds;
                    var dlSec = downloadSw.Elapsed.TotalSeconds;

                    Dispatcher.UIThread.Post(() =>
                    {
                        task.Status = MediaGenStatus.Completed;
                        task.ResultFilePath = outputPath;
                        task.GenerateSeconds = genSec;
                        task.DownloadSeconds = dlSec;
                        task.RemoteDownloadUrl = dlUrl;

                        loadingMessage.IsLoading = false;
                        loadingMessage.GenerateSeconds = genSec;
                        loadingMessage.DownloadSeconds = dlSec;
                        var totalSec = genSec + dlSec;
                        loadingMessage.Text = $"✅ 视频已恢复（等待 {genSec:F1}s + 下载 {dlSec:F1}s = 总计 {totalSec:F1}s）";
                        loadingMessage.MediaPaths.Clear();
                        if (!string.IsNullOrWhiteSpace(frameResult.FirstFramePath))
                        {
                            loadingMessage.MediaPaths.Add(frameResult.FirstFramePath);
                        }
                        if (!string.IsNullOrWhiteSpace(frameResult.LastFramePath))
                        {
                            loadingMessage.MediaPaths.Add(frameResult.LastFramePath);
                        }
                        if (loadingMessage.MediaPaths.Count == 0)
                        {
                            loadingMessage.MediaPaths.Add(outputPath);
                        }

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

        public async System.Threading.Tasks.Task<bool> SetReferenceImageFromFileAsync(string sourcePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return false;
            }

            if (!CanAddMoreReferenceImages)
            {
                StatusText = $"最多仅支持 {MaxReferenceImageCount} 张参考图";
                return false;
            }

            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".png";
            }

            var refsDir = Path.Combine(SessionDirectory, "refs");
            Directory.CreateDirectory(refsDir);

            var targetPath = Path.Combine(refsDir, $"reference_{Guid.NewGuid():N}{ext}");
            await using (var source = File.OpenRead(sourcePath))
            await using (var target = File.Create(targetPath))
            {
                await source.CopyToAsync(target, ct);
            }

            AddReferenceImagePath(targetPath);
            StatusText = $"已添加参考图（{ReferenceImagePaths.Count}/{MaxReferenceImageCount}）";
            _onRequestSave?.Invoke(this);
            return true;
        }

        public async System.Threading.Tasks.Task<bool> SetReferenceImageFromBytesAsync(byte[] bytes, string extension = ".png", CancellationToken ct = default)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            if (!CanAddMoreReferenceImages)
            {
                StatusText = $"最多仅支持 {MaxReferenceImageCount} 张参考图";
                return false;
            }

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            if (!extension.StartsWith('.'))
            {
                extension = "." + extension;
            }

            var refsDir = Path.Combine(SessionDirectory, "refs");
            Directory.CreateDirectory(refsDir);

            var targetPath = Path.Combine(refsDir, $"reference_{Guid.NewGuid():N}{extension}");
            await File.WriteAllBytesAsync(targetPath, bytes, ct);

            AddReferenceImagePath(targetPath);
            StatusText = $"已添加参考图（{ReferenceImagePaths.Count}/{MaxReferenceImageCount}）";
            _onRequestSave?.Invoke(this);
            return true;
        }

        public void ClearReferenceImage(bool silent = false)
        {
            DeleteReferenceImageFiles();
            ReferenceImagePaths.Clear();
            if (!silent)
            {
                StatusText = "已移除参考图";
            }
            _onRequestSave?.Invoke(this);
            (RemoveReferenceImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void RemoveReferenceImage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ClearReferenceImage();
                return;
            }

            var existing = ReferenceImagePaths.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                return;

            DeleteReferenceImageFile(existing);
            ReferenceImagePaths.Remove(existing);
            StatusText = "已移除参考图";
            _onRequestSave?.Invoke(this);
            (RemoveReferenceImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void AddReferenceImagePath(string path)
        {
            if (ReferenceImagePaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
                return;

            ReferenceImagePaths.Add(path);
            (RemoveReferenceImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void DeleteReferenceImageFiles()
        {
            foreach (var path in ReferenceImagePaths.ToList())
            {
                DeleteReferenceImageFile(path);
            }
        }

        private static void DeleteReferenceImageFile(string? path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        /// <summary>
        /// 会话激活时，自动为历史“视频已生成/视频已恢复”消息补齐首帧和尾帧。
        /// </summary>
        public async System.Threading.Tasks.Task BackfillVideoFramesForExistingMessagesAsync(CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            if (!await _videoFrameBackfillLock.WaitAsync(0, cancellationToken))
            {
                return;
            }

            try
            {
                var candidates = Messages
                    .Where(IsTargetVideoMessage)
                    .ToList();

                var changed = false;
                foreach (var message in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var mediaPaths = message.MediaPaths.ToList();
                    if (mediaPaths.Count == 0)
                    {
                        continue;
                    }

                    var hasFirst = mediaPaths.Any(VideoFrameExtractorService.IsFirstFrameImagePath);
                    var hasLast = mediaPaths.Any(VideoFrameExtractorService.IsLastFrameImagePath);
                    if (hasFirst && hasLast)
                    {
                        continue;
                    }

                    var videoPath = ResolveVideoPathForMessage(mediaPaths);
                    if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
                    {
                        continue;
                    }

                    var frameResult = await VideoFrameExtractorService
                        .TryExtractFirstAndLastFrameAsync(videoPath, cancellationToken)
                        .ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(frameResult.FirstFramePath)
                        && string.IsNullOrWhiteSpace(frameResult.LastFramePath))
                    {
                        continue;
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        message.MediaPaths.Clear();
                        if (!string.IsNullOrWhiteSpace(frameResult.FirstFramePath))
                        {
                            message.MediaPaths.Add(frameResult.FirstFramePath);
                        }

                        if (!string.IsNullOrWhiteSpace(frameResult.LastFramePath))
                        {
                            message.MediaPaths.Add(frameResult.LastFramePath);
                        }

                        if (message.MediaPaths.Count == 0)
                        {
                            message.MediaPaths.Add(videoPath);
                        }
                    });

                    changed = true;
                }

                if (changed)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => _onRequestSave?.Invoke(this));
                }
            }
            catch (OperationCanceledException)
            {
                // 忽略取消
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"历史视频补帧失败: {ex.Message}");
            }
            finally
            {
                _videoFrameBackfillLock.Release();
            }
        }

        private static bool IsTargetVideoMessage(ChatMessageViewModel message)
        {
            if (message.Role == "user" || message.IsLoading)
            {
                return false;
            }

            if (message.MediaPaths.Count == 0)
            {
                return false;
            }

            var text = message.Text ?? string.Empty;
            return text.Contains("视频已生成", StringComparison.Ordinal)
                || text.Contains("视频已恢复", StringComparison.Ordinal);
        }

        private static string? ResolveVideoPathForMessage(IReadOnlyList<string> mediaPaths)
        {
            foreach (var path in mediaPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(path))
                {
                    return path;
                }

                if (VideoFrameExtractorService.TryResolveVideoPathFromFirstFrame(path, out var fromFirst)
                    && File.Exists(fromFirst))
                {
                    return fromFirst;
                }
            }

            return null;
        }

        private void OpenFile(string? filePath)
        {
            if (VideoFrameExtractorService.TryResolveVideoPathFromFirstFrame(filePath, out var videoPath))
            {
                filePath = videoPath;
            }

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
