using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Avalonia.Media;
using Avalonia.Threading;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Transcription;
using NAudio.Wave;
using TranslationToolUI.Models;
using TranslationToolUI.Services;

namespace TranslationToolUI.ViewModels
{
    public partial class MainWindowViewModel
    {
        private const double SubtitleCueRowHeight = 56;

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
                    if (GenerateReviewSummaryCommand is RelayCommand genCmd)
                    {
                        genCmd.RaiseCanExecuteChanged();
                    }
                    if (GenerateAllReviewSheetsCommand is RelayCommand allCmd)
                    {
                        allCmd.RaiseCanExecuteChanged();
                    }
                }
            }
        }

        public string SpeechSubtitleStatusMessage
        {
            get => _speechSubtitleStatusMessage;
            private set => SetProperty(ref _speechSubtitleStatusMessage, value);
        }

        public bool IsSpeechSubtitleOptionEnabled => _config.BatchStorageIsValid
            && !string.IsNullOrWhiteSpace(_config.BatchStorageConnectionString);

        public bool UseSpeechSubtitleForReview
        {
            get => _config.UseSpeechSubtitleForReview;
            set
            {
                if (_config.UseSpeechSubtitleForReview == value)
                {
                    return;
                }

                _config.UseSpeechSubtitleForReview = value;
                OnPropertyChanged(nameof(UseSpeechSubtitleForReview));
                OnPropertyChanged(nameof(BatchStartButtonText));
                if (GenerateReviewSummaryCommand is RelayCommand genCmd)
                {
                    genCmd.RaiseCanExecuteChanged();
                }
                if (GenerateAllReviewSheetsCommand is RelayCommand allCmd)
                {
                    allCmd.RaiseCanExecuteChanged();
                }
                if (StartBatchCommand is RelayCommand startCmd)
                {
                    startCmd.RaiseCanExecuteChanged();
                }
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
        }

        public string SpeechSubtitleOptionStatusText => IsSpeechSubtitleOptionEnabled
            ? "存储账号已验证，允许生成 speech 字幕"
            : "未验证存储账号，已禁用该选项";

        public string BatchStartButtonText => GetBatchStartButtonText();

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
                var requireSpeech = ShouldGenerateSpeechSubtitleForReview;
                var hasSpeechSubtitle = HasSpeechSubtitle(audio.FullPath);
                var subtitlePath = requireSpeech
                    ? (hasSpeechSubtitle ? GetSpeechSubtitlePath(audio.FullPath) : "")
                    : GetPreferredSubtitlePath(audio.FullPath);
                var hasSubtitle = requireSpeech
                    ? hasSpeechSubtitle
                    : !string.IsNullOrWhiteSpace(subtitlePath);
                var hasAiSubtitle = HasAiSubtitle(audio.FullPath);
                var pendingSheets = Math.Max(totalSheets - completedSheets, 0);
                var statusMessage = hasSubtitle
                    ? "待处理"
                    : (requireSpeech ? "待生成 speech 字幕" : "缺少字幕");
                var reviewStatusText = totalSheets == 0
                    ? "复盘:未勾选"
                    : $"复盘 {completedSheets}/{totalSheets}";
                if (requireSpeech && !hasSubtitle && totalSheets > 0)
                {
                    reviewStatusText = "复盘:等待字幕";
                }

                _batchTasks.Add(new BatchTaskItem
                {
                    FileName = audio.Name,
                    FullPath = audio.FullPath,
                    Status = BatchTaskStatus.Pending,
                    Progress = 0,
                    HasAiSubtitle = hasAiSubtitle,
                    HasAiSummary = hasAiSummary,
                    StatusMessage = statusMessage,
                    ReviewTotal = totalSheets,
                    ReviewCompleted = completedSheets,
                    ReviewFailed = 0,
                    ReviewPending = pendingSheets,
                    ReviewStatusText = reviewStatusText
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

        private void NormalizeSpeechSubtitleOption()
        {
            if (!IsSpeechSubtitleOptionEnabled && _config.UseSpeechSubtitleForReview)
            {
                _config.UseSpeechSubtitleForReview = false;
            }
        }

        private bool ShouldGenerateSpeechSubtitleForReview => IsSpeechSubtitleOptionEnabled
            && _config.UseSpeechSubtitleForReview;

        private bool CanGenerateSpeechSubtitleFromStorage()
        {
            if (!IsSpeechSubtitleOptionEnabled)
            {
                return false;
            }

            var subscription = _config.GetActiveSubscription();
            return subscription?.IsValid() == true && !string.IsNullOrWhiteSpace(_config.SourceLanguage);
        }

        private void EnqueueReviewSheetsForAudio(MediaFileItem audioFile, IEnumerable<ReviewSheetState> sheets)
        {
            var requireSpeech = ShouldGenerateSpeechSubtitleForReview;
            var subtitlePath = requireSpeech
                ? GetSpeechSubtitlePath(audioFile.FullPath)
                : GetPreferredSubtitlePath(audioFile.FullPath);
            var hasSubtitle = requireSpeech
                ? File.Exists(subtitlePath)
                : !string.IsNullOrWhiteSpace(subtitlePath) && File.Exists(subtitlePath);
            if (!hasSubtitle)
            {
                foreach (var sheet in sheets)
                {
                    sheet.StatusMessage = requireSpeech ? "缺少 speech 字幕" : "缺少字幕";
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
                    QueueType = BatchQueueItemType.ReviewSheet,
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
                            {
                                var item = _batchQueueItems.FirstOrDefault(i => i.Status == BatchTaskStatus.Pending);
                                if (item != null)
                                {
                                    item.Status = BatchTaskStatus.Running;
                                    item.StatusMessage = "调度中";
                                }
                                return item;
                            });

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
                    if (!token.IsCancellationRequested && ShouldWriteBatchLogSuccess)
                    {
                        AppendBatchLog("BatchComplete", "-", "Success", "批处理完成");
                    }
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
            if (IsBatchRunning || BatchTasks.Count == 0)
            {
                return false;
            }

            var batchSheets = GetBatchReviewSheets();
            var enableReview = IsAiConfigured && batchSheets.Count > 0;
            var enableSpeech = ShouldGenerateSpeechSubtitleForReview;

            if (!enableReview && !enableSpeech)
            {
                return false;
            }

            var needsSpeech = enableSpeech && BatchTasks.Any(task => !HasSpeechSubtitle(task.FullPath));
            if (needsSpeech && !CanGenerateSpeechSubtitleFromStorage())
            {
                return false;
            }

            return true;
        }

        private bool ShouldWriteBatchLogSuccess => _config.BatchLogLevel == BatchLogLevel.SuccessAndFailure;

        private bool ShouldWriteBatchLogFailure => _config.BatchLogLevel is BatchLogLevel.FailuresOnly or BatchLogLevel.SuccessAndFailure;

        private void EnsureBatchLogFile()
        {
            if (_config.BatchLogLevel == BatchLogLevel.Off)
            {
                _batchLogFilePath = null;
                return;
            }

            if (!string.IsNullOrWhiteSpace(_batchLogFilePath))
            {
                return;
            }

            var sessionsPath = PathManager.Instance.SessionsPath;
            var logsRoot = Directory.GetParent(sessionsPath)?.FullName ?? sessionsPath;
            var logsPath = Path.Combine(logsRoot, "Logs");
            Directory.CreateDirectory(logsPath);
            var fileName = $"batch_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            _batchLogFilePath = Path.Combine(logsPath, fileName);
        }

        private void AppendBatchLog(string eventName, string fileName, string status, string message)
        {
            if (_config.BatchLogLevel == BatchLogLevel.Off)
            {
                return;
            }

            EnsureBatchLogFile();
            if (string.IsNullOrWhiteSpace(_batchLogFilePath))
            {
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"{timestamp} | {eventName} | {fileName} | {status} | {message}";

            lock (_batchLogLock)
            {
                File.AppendAllText(_batchLogFilePath, line + Environment.NewLine, new System.Text.UTF8Encoding(false));
            }
        }

        private void AppendBatchDebugLog(string eventName, string message)
        {
            if (!_config.EnableAuditLog)
            {
                return;
            }

            var sessionsPath = PathManager.Instance.SessionsPath;
            var logsRoot = Directory.GetParent(sessionsPath)?.FullName ?? sessionsPath;
            var logsPath = Path.Combine(logsRoot, "Logs");
            Directory.CreateDirectory(logsPath);
            var auditPath = Path.Combine(logsPath, "Audit.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"{timestamp} | {eventName} | {message}";

            lock (_batchLogLock)
            {
                File.AppendAllText(auditPath, line + Environment.NewLine, new System.Text.UTF8Encoding(false));
            }
        }

        public void AuditUiEvent(string eventName, string message)
        {
            AppendBatchDebugLog(eventName, message);
        }

        public void EnqueueSubtitleAndReviewFromLibraryUi(MediaFileItem? audioFile)
        {
            EnqueueSubtitleAndReviewFromLibrary(audioFile);
        }

        private static string FormatBatchExceptionForLog(Exception ex)
        {
            var sb = new System.Text.StringBuilder(ex.ToString());
            if (ex.Data.Contains("SpeechBatchError"))
            {
                var detail = ex.Data["SpeechBatchError"]?.ToString();
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    sb.AppendLine();
                    sb.Append("SpeechBatchError: ");
                    sb.Append(detail);
                }
            }

            return sb.ToString();
        }

        private string GetBatchStartButtonText()
        {
            var batchSheets = GetBatchReviewSheets();
            var enableReview = IsAiConfigured && batchSheets.Count > 0;
            var enableSpeech = ShouldGenerateSpeechSubtitleForReview;

            if (enableSpeech && enableReview)
            {
                return "开始生成字幕+复盘";
            }

            if (enableSpeech)
            {
                return "开始生成字幕";
            }

            if (enableReview)
            {
                return "开始生成复盘";
            }

            return "开始处理";
        }

        private void StartBatchProcessing()
        {
            if (BatchTasks.Count == 0)
            {
                BatchStatusMessage = "没有可处理的任务";
                return;
            }

            var batchSheets = GetBatchReviewSheets();
            var enableReview = IsAiConfigured && batchSheets.Count > 0;
            var enableSpeech = ShouldGenerateSpeechSubtitleForReview;

            if (!enableReview && !enableSpeech)
            {
                BatchStatusMessage = "未启用 speech 字幕或复盘生成";
                return;
            }

            var needsSpeech = enableSpeech && BatchTasks.Any(task => !HasSpeechSubtitle(task.FullPath));
            if (needsSpeech && !CanGenerateSpeechSubtitleFromStorage())
            {
                BatchStatusMessage = "speech 字幕需要有效的存储账号与语音订阅";
                return;
            }

            _batchCts?.Cancel();
            IsBatchRunning = true;
            BatchStatusMessage = "批处理已开始";
            _batchLogFilePath = null;
            EnsureBatchLogFile();
            if (ShouldWriteBatchLogSuccess)
            {
                AppendBatchLog("BatchStart", "-", "Success", "批处理开始");
            }

            _batchQueueItems.Clear();
            _batchReviewSheetSnapshot = batchSheets.ToList();
            foreach (var batchItem in BatchTasks)
            {
                PrepareAndEnqueueSingleItem(batchItem, batchSheets, enableSpeech, enableReview,
                    _config.BatchForceRegeneration);
            }

            if (_batchQueueItems.Count == 0)
            {
                IsBatchRunning = false;
                BatchStatusMessage = "批处理完成";
                if (ShouldWriteBatchLogSuccess)
                {
                    AppendBatchLog("BatchComplete", "-", "Success", "无待处理任务");
                }
                return;
            }

            StartBatchQueueRunner("批处理已开始");
        }

        private void EnqueueSpeechSubtitleForBatch(BatchTaskItem batchItem)
        {
            var existsInQueue = _batchQueueItems.Any(item =>
                item.QueueType == BatchQueueItemType.SpeechSubtitle
                && string.Equals(item.FullPath, batchItem.FullPath, StringComparison.OrdinalIgnoreCase)
                && item.Status is BatchTaskStatus.Pending or BatchTaskStatus.Running);

            if (existsInQueue)
            {
                return;
            }

            _batchQueueItems.Add(new BatchQueueItem
            {
                FileName = batchItem.FileName,
                FullPath = batchItem.FullPath,
                SheetName = "speech 字幕",
                SheetTag = "speech",
                Prompt = "",
                QueueType = BatchQueueItemType.SpeechSubtitle,
                Status = BatchTaskStatus.Pending,
                Progress = 0,
                StatusMessage = "待处理"
            });
            if (ShouldWriteBatchLogSuccess)
            {
                AppendBatchLog("SpeechEnqueue", batchItem.FileName, "Success", "入队 speech 字幕");
            }
        }

        private int EnqueueReviewQueueItemsForAudioInternal(
            BatchTaskItem parentItem,
            IEnumerable<ReviewSheetPreset> sheets,
            bool ignoreExistingFiles = false)
        {
            var added = 0;
            foreach (var sheet in sheets)
            {
                if (!ignoreExistingFiles && File.Exists(GetReviewSheetPath(parentItem.FullPath, sheet.FileTag)))
                {
                    continue;
                }

                var existsInQueue = _batchQueueItems.Any(item =>
                    item.QueueType == BatchQueueItemType.ReviewSheet
                    && string.Equals(item.FullPath, parentItem.FullPath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.SheetTag, sheet.FileTag, StringComparison.OrdinalIgnoreCase)
                    && item.Status is BatchTaskStatus.Pending or BatchTaskStatus.Running);

                if (existsInQueue)
                {
                    continue;
                }

                _batchQueueItems.Add(new BatchQueueItem
                {
                    FileName = parentItem.FileName,
                    FullPath = parentItem.FullPath,
                    SheetName = sheet.Name,
                    SheetTag = sheet.FileTag,
                    Prompt = sheet.Prompt,
                    QueueType = BatchQueueItemType.ReviewSheet,
                    Status = BatchTaskStatus.Pending,
                    Progress = 0,
                    StatusMessage = "待处理"
                });
                added++;
                if (ShouldWriteBatchLogSuccess)
                {
                    AppendBatchLog("ReviewEnqueue", parentItem.FileName, "Success", $"入队 {sheet.Name}");
                }
            }

            return added;
        }

        /// <summary>
        /// 单个 BatchTaskItem 的决策与入队逻辑（共享核心）。
        /// 批处理按钮和右键菜单共同调用此方法。
        /// </summary>
        /// <param name="forceRegeneration">true: 强制重新生成字幕和复盘（忽略已有文件）</param>
        /// <returns>添加到队列的项数</returns>
        private int PrepareAndEnqueueSingleItem(
            BatchTaskItem batchItem,
            List<ReviewSheetPreset> reviewSheets,
            bool enableSpeech,
            bool enableReview,
            bool forceRegeneration)
        {
            var speechExists = HasSpeechSubtitle(batchItem.FullPath);
            batchItem.HasAiSubtitle = enableSpeech ? speechExists : HasAiSubtitle(batchItem.FullPath);

            // ── 需要生成 speech 字幕 ──
            if (enableSpeech && (forceRegeneration || !speechExists))
            {
                batchItem.HasAiSubtitle = false;
                batchItem.ForceReviewRegeneration = forceRegeneration && enableReview;
                batchItem.ReviewTotal = enableReview ? reviewSheets.Count : 0;
                batchItem.ReviewCompleted = 0;
                batchItem.ReviewFailed = 0;
                batchItem.ReviewPending = enableReview ? reviewSheets.Count : 0;
                batchItem.ReviewStatusText = enableReview ? "复盘:等待字幕" : "复盘:未启用";
                batchItem.HasAiSummary = false;
                UpdateBatchItem(batchItem, BatchTaskStatus.Pending, 0, "待生成 speech 字幕");
                EnqueueSpeechSubtitleForBatch(batchItem);
                return 1;
            }

            // ── 字幕已有 / 未启用，且无需复盘 ──
            if (!enableReview)
            {
                batchItem.ReviewTotal = 0;
                batchItem.ReviewCompleted = 0;
                batchItem.ReviewFailed = 0;
                batchItem.ReviewPending = 0;
                batchItem.ReviewStatusText = "复盘:未启用";
                batchItem.HasAiSubtitle = speechExists || HasAiSubtitle(batchItem.FullPath);
                UpdateBatchItem(batchItem, BatchTaskStatus.Completed, 1, "字幕已存在");
                if (ShouldWriteBatchLogSuccess)
                {
                    AppendBatchLog("SpeechSkip", batchItem.FileName, "Success", "speech 字幕已存在");
                }
                return 0;
            }

            // ── 未启用 speech，检查是否有字幕可供复盘 ──
            if (!enableSpeech)
            {
                var subtitlePath = GetPreferredSubtitlePath(batchItem.FullPath);
                if (string.IsNullOrWhiteSpace(subtitlePath) || !File.Exists(subtitlePath))
                {
                    batchItem.ReviewTotal = reviewSheets.Count;
                    batchItem.ReviewCompleted = 0;
                    batchItem.ReviewFailed = 0;
                    batchItem.ReviewPending = reviewSheets.Count;
                    batchItem.ReviewStatusText = "复盘:缺少字幕";
                    UpdateBatchItem(batchItem, BatchTaskStatus.Failed, 0, "缺少字幕");
                    if (ShouldWriteBatchLogFailure)
                    {
                        AppendBatchLog("ReviewSkip", batchItem.FileName, "Failed", "缺少字幕");
                    }
                    return 0;
                }
            }

            // ── 入队复盘项 ──
            var completed = forceRegeneration ? 0 : reviewSheets.Count(s =>
                File.Exists(GetReviewSheetPath(batchItem.FullPath, s.FileTag)));
            var pending = Math.Max(reviewSheets.Count - completed, 0);

            batchItem.ReviewTotal = reviewSheets.Count;
            batchItem.ReviewCompleted = completed;
            batchItem.ReviewFailed = 0;
            batchItem.ReviewPending = pending;
            batchItem.ReviewStatusText = reviewSheets.Count == 0
                ? "复盘:未勾选"
                : $"复盘 {completed}/{reviewSheets.Count}";
            batchItem.HasAiSummary = false;
            batchItem.ForceReviewRegeneration = forceRegeneration;

            if (pending == 0)
            {
                batchItem.HasAiSummary = true;
                UpdateBatchItem(batchItem, BatchTaskStatus.Completed, 1, "已存在");
                if (ShouldWriteBatchLogSuccess)
                {
                    AppendBatchLog("ReviewSkip", batchItem.FileName, "Success", "复盘已存在");
                }
                return 0;
            }

            UpdateBatchItem(batchItem, BatchTaskStatus.Pending, 0, "待处理");
            var added = EnqueueReviewQueueItemsForAudioInternal(batchItem, reviewSheets, forceRegeneration);
            batchItem.ForceReviewRegeneration = false;
            return added;
        }

        private void StopBatchProcessing()
        {
            _batchCts?.Cancel();
            foreach (var item in _batchQueueItems)
            {
                item.Cts?.Cancel();
            }
            BatchStatusMessage = "正在停止批处理...";
            if (ShouldWriteBatchLogFailure)
            {
                AppendBatchLog("BatchStop", "-", "Failed", "批处理停止");
            }
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
            if (ShouldWriteBatchLogSuccess)
            {
                var eventName = queueItem.QueueType == BatchQueueItemType.SpeechSubtitle
                    ? "SpeechStart"
                    : "ReviewStart";
                AppendBatchLog(eventName, queueItem.FileName, "Success", queueItem.SheetName);
            }
            if (parentItem != null)
            {
                UpdateBatchItem(parentItem, BatchTaskStatus.Running, parentItem.Progress, "生成中");
            }

            if (queueItem.QueueType == BatchQueueItemType.SpeechSubtitle)
            {
                await ProcessSpeechSubtitleQueueItem(queueItem, parentItem, localToken, cueCache, cueLock);
                return;
            }

            var cues = GetBatchCues(queueItem.FullPath, cueCache, cueLock);
            if (cues.Count == 0)
            {
                UpdateQueueItem(queueItem, BatchTaskStatus.Failed, 0, "字幕为空");
                if (ShouldWriteBatchLogFailure)
                {
                    AppendBatchLog("ReviewFailed", queueItem.FileName, "Failed", "字幕为空");
                }
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
                if (ShouldWriteBatchLogSuccess)
                {
                    AppendBatchLog("ReviewSuccess", queueItem.FileName, "Success", queueItem.SheetName);
                }
                if (parentItem != null)
                {
                    UpdateBatchReviewProgress(parentItem, BatchTaskStatus.Completed);
                }
            }
            catch (OperationCanceledException)
            {
                UpdateQueueItem(queueItem, BatchTaskStatus.Failed, 0, "已取消");
                if (ShouldWriteBatchLogFailure)
                {
                    AppendBatchLog("ReviewCanceled", queueItem.FileName, "Failed", "已取消");
                }
                if (parentItem != null)
                {
                    UpdateBatchReviewProgress(parentItem, BatchTaskStatus.Failed);
                }
            }
            catch (Exception ex)
            {
                UpdateQueueItem(queueItem, BatchTaskStatus.Failed, 0, $"失败: {ex.Message}");
                if (ShouldWriteBatchLogFailure)
                {
                    AppendBatchLog("ReviewFailed", queueItem.FileName, "Failed", ex.ToString());
                }
                if (parentItem != null)
                {
                    UpdateBatchReviewProgress(parentItem, BatchTaskStatus.Failed);
                }
            }
        }

        private async Task ProcessSpeechSubtitleQueueItem(
            BatchQueueItem queueItem,
            BatchTaskItem? parentItem,
            CancellationToken token,
            Dictionary<string, List<SubtitleCue>> cueCache,
            object cueLock)
        {
            try
            {
                var success = await GenerateBatchSpeechSubtitleForFileAsync(
                    queueItem.FullPath,
                    token,
                    status => UpdateQueueItem(queueItem, BatchTaskStatus.Running, 0.2, status));

                if (!success)
                {
                    UpdateQueueItem(queueItem, BatchTaskStatus.Failed, 0, "未识别到有效文本");
                    if (ShouldWriteBatchLogFailure)
                    {
                        AppendBatchLog("SpeechFailed", queueItem.FileName, "Failed", "未识别到有效文本");
                    }
                    if (parentItem != null)
                    {
                        parentItem.ReviewStatusText = "复盘:字幕失败";
                        UpdateBatchItem(parentItem, BatchTaskStatus.Failed, 0, "字幕失败");
                    }
                    return;
                }

                UpdateQueueItem(queueItem, BatchTaskStatus.Completed, 1, "完成");
                if (ShouldWriteBatchLogSuccess)
                {
                    AppendBatchLog("SpeechSuccess", queueItem.FileName, "Success", "speech 字幕完成");
                }

                lock (cueLock)
                {
                    cueCache.Remove(queueItem.FullPath);
                }

                if (parentItem != null)
                {
                    parentItem.HasAiSubtitle = true;
                }

                var enableReview = IsAiConfigured && _batchReviewSheetSnapshot.Count > 0;
                if (!enableReview)
                {
                    if (parentItem != null)
                    {
                        UpdateBatchItem(parentItem, BatchTaskStatus.Completed, 1, "字幕完成");
                    }
                    return;
                }

                if (parentItem != null)
                {
                    var completed = _batchReviewSheetSnapshot.Count(sheet =>
                        File.Exists(GetReviewSheetPath(parentItem.FullPath, sheet.FileTag)));
                    parentItem.ReviewTotal = _batchReviewSheetSnapshot.Count;
                    parentItem.ReviewCompleted = completed;
                    parentItem.ReviewFailed = 0;
                    parentItem.ReviewPending = Math.Max(parentItem.ReviewTotal - completed, 0);
                    parentItem.ReviewStatusText = parentItem.ReviewTotal == 0
                        ? "复盘:未勾选"
                        : $"复盘 {completed}/{parentItem.ReviewTotal}";

                    if (parentItem.ReviewPending == 0)
                    {
                        parentItem.HasAiSummary = true;
                        UpdateBatchItem(parentItem, BatchTaskStatus.Completed, 1, "已存在");
                        return;
                    }
                }

                var added = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    parentItem == null
                        ? 0
                        : EnqueueReviewQueueItemsForAudioInternal(
                            parentItem,
                            _batchReviewSheetSnapshot,
                            parentItem.ForceReviewRegeneration));

                if (parentItem != null)
                {
                    parentItem.ForceReviewRegeneration = false;
                }

                if (parentItem != null)
                {
                    var statusMessage = added > 0 ? "字幕完成，待复盘" : "字幕完成";
                    UpdateBatchItem(parentItem, BatchTaskStatus.Running, parentItem.Progress, statusMessage);
                }
            }
            catch (OperationCanceledException)
            {
                UpdateQueueItem(queueItem, BatchTaskStatus.Failed, 0, "已取消");
                if (ShouldWriteBatchLogFailure)
                {
                    AppendBatchLog("SpeechCanceled", queueItem.FileName, "Failed", "已取消");
                }
                if (parentItem != null)
                {
                    UpdateBatchItem(parentItem, BatchTaskStatus.Failed, 0, "字幕已取消");
                }
            }
            catch (Exception ex)
            {
                UpdateQueueItem(queueItem, BatchTaskStatus.Failed, 0, $"失败: {ex.Message}");
                if (ShouldWriteBatchLogFailure)
                {
                    AppendBatchLog("SpeechFailed", queueItem.FileName, "Failed", FormatBatchExceptionForLog(ex));
                }
                if (parentItem != null)
                {
                    UpdateBatchItem(parentItem, BatchTaskStatus.Failed, 0, "字幕失败");
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

            var subtitlePath = ShouldGenerateSpeechSubtitleForReview
                ? GetSpeechSubtitlePath(audioPath)
                : GetPreferredSubtitlePath(audioPath);
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
            if (ShouldWriteBatchLogFailure)
            {
                var eventName = item.QueueType == BatchQueueItemType.SpeechSubtitle
                    ? "SpeechCanceled"
                    : "ReviewCanceled";
                AppendBatchLog(eventName, item.FileName, "Failed", "已取消");
            }

            var parent = BatchTasks.FirstOrDefault(x => x.FullPath == item.FullPath);
            if (parent != null)
            {
                if (item.QueueType == BatchQueueItemType.SpeechSubtitle)
                {
                    parent.ReviewStatusText = "复盘:字幕取消";
                    UpdateBatchItem(parent, BatchTaskStatus.Failed, 0, "字幕已取消");
                }
                else
                {
                    UpdateBatchReviewProgress(parent, BatchTaskStatus.Failed);
                }
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
            var speechVtt = Path.Combine(directory, baseName + ".speech.vtt");
            var aiSrt = Path.Combine(directory, baseName + ".ai.srt");
            var aiVtt = Path.Combine(directory, baseName + ".ai.vtt");
            return File.Exists(speechVtt) || File.Exists(aiSrt) || File.Exists(aiVtt);
        }

        private static bool HasSpeechSubtitle(string audioFilePath)
        {
            var speechPath = GetSpeechSubtitlePath(audioFilePath);
            return File.Exists(speechPath);
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
                var speechPath = GetSpeechSubtitlePath(audioFile.FullPath);
                var speechFile = _subtitleFiles.FirstOrDefault(item =>
                    string.Equals(item.FullPath, speechPath, StringComparison.OrdinalIgnoreCase));
                SelectedSubtitleFile = ShouldGenerateSpeechSubtitleForReview && speechFile != null
                    ? speechFile
                    : _subtitleFiles[0];
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
            var hasCues = SubtitleCues.Count > 0;
            var allowSpeechGeneration = ShouldGenerateSpeechSubtitleForReview
                && SelectedAudioFile != null
                && !IsSpeechSubtitleGenerating
                && (HasSpeechSubtitle(SelectedAudioFile.FullPath) || CanGenerateSpeechSubtitleFromStorage());

            return IsAiConfigured
                   && SelectedAudioFile != null
                   && SelectedReviewSheet != null
                   && (hasCues || allowSpeechGeneration)
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
            if (!await EnsureSpeechSubtitleForReviewAsync(audioFile))
            {
                return;
            }

            var cues = SubtitleCues.ToList();
            await GenerateReviewSheetAsync(sheet, audioFile, cues);
        }

        private bool CanGenerateAllReviewSheets()
        {
            var hasCues = SubtitleCues.Count > 0;
            var allowSpeechGeneration = ShouldGenerateSpeechSubtitleForReview
                && SelectedAudioFile != null
                && !IsSpeechSubtitleGenerating
                && (HasSpeechSubtitle(SelectedAudioFile.FullPath) || CanGenerateSpeechSubtitleFromStorage());

            return IsAiConfigured
                   && SelectedAudioFile != null
                   && (hasCues || allowSpeechGeneration)
                   && _reviewSheets.Count > 0
                   && _reviewSheets.Any(sheet => !sheet.IsLoading);
        }

        private async Task<bool> EnsureSpeechSubtitleForReviewAsync(MediaFileItem audioFile)
        {
            if (!ShouldGenerateSpeechSubtitleForReview)
            {
                return true;
            }

            var speechPath = GetSpeechSubtitlePath(audioFile.FullPath);
            if (File.Exists(speechPath))
            {
                LoadSubtitleFilesForAudio(audioFile);
                var speechFile = _subtitleFiles.FirstOrDefault(item =>
                    string.Equals(item.FullPath, speechPath, StringComparison.OrdinalIgnoreCase));
                if (speechFile != null)
                {
                    SelectedSubtitleFile = speechFile;
                }
                return true;
            }

            if (!CanGenerateSpeechSubtitleFromStorage())
            {
                SpeechSubtitleStatusMessage = "缺少有效的存储账号或语音订阅，无法生成 speech 字幕";
                return false;
            }

            _speechSubtitleCts?.Cancel();
            _speechSubtitleCts = new CancellationTokenSource();
            var token = _speechSubtitleCts.Token;

            IsSpeechSubtitleGenerating = true;
            SpeechSubtitleStatusMessage = "speech 字幕生成中...";

            try
            {
                var success = await GenerateBatchSpeechSubtitleForFileAsync(
                    audioFile.FullPath,
                    token,
                    status => SpeechSubtitleStatusMessage = status);
                if (!success)
                {
                    SpeechSubtitleStatusMessage = "未识别到有效文本";
                    return false;
                }

                SpeechSubtitleStatusMessage = $"speech 字幕已生成: {Path.GetFileName(speechPath)}";
                LoadSubtitleFilesForAudio(audioFile);
                var speechFile = _subtitleFiles.FirstOrDefault(item =>
                    string.Equals(item.FullPath, speechPath, StringComparison.OrdinalIgnoreCase));
                if (speechFile != null)
                {
                    SelectedSubtitleFile = speechFile;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                SpeechSubtitleStatusMessage = "speech 字幕生成已取消";
                return false;
            }
            catch (Exception ex)
            {
                SpeechSubtitleStatusMessage = $"speech 字幕生成失败: {ex.Message}";
                return false;
            }
            finally
            {
                IsSpeechSubtitleGenerating = false;
                _speechSubtitleCts?.Dispose();
                _speechSubtitleCts = null;
            }
        }

        private async void GenerateAllReviewSheets()
        {
            if (SelectedAudioFile == null)
            {
                return;
            }

            if (!await EnsureSpeechSubtitleForReviewAsync(SelectedAudioFile))
            {
                return;
            }

            EnqueueReviewSheetsForAudio(SelectedAudioFile, _reviewSheets);
            StartBatchQueueRunner("复盘已加入队列");
        }

        private bool CanEnqueueSubtitleAndReviewFromLibrary(MediaFileItem? audioFile)
        {
            var target = audioFile ?? SelectedAudioFile;
            var canExecute = target != null && !string.IsNullOrWhiteSpace(target.FullPath);
            if (_config.EnableAuditLog)
            {
                var reason = canExecute
                    ? "ok"
                    : (target == null ? "target-null" : "fullpath-empty");
                var selectedPath = SelectedAudioFile?.FullPath ?? "";
                var paramPath = audioFile?.FullPath ?? "";
                AppendBatchDebugLog(
                    "EnqueueSubtitleReview.CanExecute",
                    $"result={canExecute} reason={reason} selected={selectedPath} param={paramPath}");
            }

            return canExecute;
        }

        private void EnqueueSubtitleAndReviewFromLibrary(MediaFileItem? audioFile)
        {
            var target = audioFile ?? SelectedAudioFile;
            if (target == null || string.IsNullOrWhiteSpace(target.FullPath))
            {
                BatchStatusMessage = "未选择音频文件";
                return;
            }

            if (!File.Exists(target.FullPath))
            {
                BatchStatusMessage = "音频文件不存在";
                return;
            }

            var batchSheets = GetBatchReviewSheets();
            var enableReview = IsAiConfigured && batchSheets.Count > 0;
            var enableSpeech = ShouldGenerateSpeechSubtitleForReview;

            if (!enableReview && !enableSpeech)
            {
                BatchStatusMessage = "未启用 speech 字幕或复盘生成";
                return;
            }

            if (enableSpeech && !CanGenerateSpeechSubtitleFromStorage())
            {
                BatchStatusMessage = "speech 字幕需要有效的存储账号与语音订阅";
                return;
            }

            if (!enableSpeech)
            {
                var subtitlePath = GetPreferredSubtitlePath(target.FullPath);
                var hasSubtitle = !string.IsNullOrWhiteSpace(subtitlePath) && File.Exists(subtitlePath);
                if (!hasSubtitle)
                {
                    BatchStatusMessage = "缺少字幕";
                    return;
                }
            }

            var batchItem = _batchTasks.FirstOrDefault(item =>
                string.Equals(item.FullPath, target.FullPath, StringComparison.OrdinalIgnoreCase));
            if (batchItem == null)
            {
                batchItem = new BatchTaskItem
                {
                    FileName = target.Name,
                    FullPath = target.FullPath,
                    Status = BatchTaskStatus.Pending,
                    Progress = 0
                };
                _batchTasks.Add(batchItem);
            }

            var reviewSheets = _batchReviewSheetSnapshot.Count > 0
                ? _batchReviewSheetSnapshot
                : batchSheets.ToList();
            if (_batchReviewSheetSnapshot.Count == 0 && reviewSheets.Count > 0)
            {
                _batchReviewSheetSnapshot = reviewSheets.ToList();
            }

            EnsureBatchLogFile();

            PrepareAndEnqueueSingleItem(batchItem, reviewSheets, enableSpeech, enableReview,
                _config.ContextMenuForceRegeneration);

            if (ShouldWriteBatchLogSuccess)
            {
                AppendBatchLog("QueueStart", batchItem.FileName, "Success", "右键队列启动");
            }

            UpdateBatchQueueStatusText();
            StartBatchQueueRunner("已加入队列");
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

            if (!IsSpeechSubtitleOptionEnabled)
            {
                return false;
            }

            return CanGenerateSpeechSubtitleFromStorage();
        }

        private async void GenerateBatchSpeechSubtitle()
        {
            if (!CanGenerateBatchSpeechSubtitle())
            {
                SpeechSubtitleStatusMessage = "请先验证存储账号与语音订阅";
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
            try
            {
                var success = await GenerateBatchSpeechSubtitleForFileAsync(
                    audioFile.FullPath,
                    token,
                    status => SpeechSubtitleStatusMessage = status);

                if (!success)
                {
                    SpeechSubtitleStatusMessage = "未识别到有效文本";
                    return;
                }

                var outputPath = GetSpeechSubtitlePath(audioFile.FullPath);
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

        private async Task<bool> GenerateBatchSpeechSubtitleForFileAsync(
            string audioPath,
            CancellationToken token,
            Action<string>? onStatus)
        {
            if (string.IsNullOrWhiteSpace(audioPath))
            {
                return false;
            }

            if (!File.Exists(audioPath))
            {
                throw new FileNotFoundException("未找到音频文件", audioPath);
            }

            var subscription = _config.GetActiveSubscription();
            if (subscription == null || !subscription.IsValid())
            {
                throw new InvalidOperationException("语音订阅未配置");
            }

            if (!IsSpeechSubtitleOptionEnabled)
            {
                throw new InvalidOperationException("存储账号未验证");
            }

            var uploadPath = audioPath;
            string? tempUploadPath = null;
            var converted = false;

            try
            {
                uploadPath = PrepareBatchUploadAudioPath(
                    audioPath,
                    onStatus,
                    token,
                    out tempUploadPath,
                    out converted);

                var audioFileName = Path.GetFileName(audioPath);
                if (converted)
                {
                    if (ShouldWriteBatchLogSuccess)
                    {
                        AppendBatchLog("AudioConvert", audioFileName, "Success",
                            $"pcm16k16bitmono temp={Path.GetFileName(uploadPath)}");
                    }
                }
                else
                {
                    if (ShouldWriteBatchLogSuccess)
                    {
                        AppendBatchLog("AudioReuse", audioFileName, "Success",
                            $"pcm16k16bitmono file={Path.GetFileName(uploadPath)}");
                    }
                }

                onStatus?.Invoke("批量转写：上传音频...");

                var (audioContainer, outputContainer) = await GetBatchContainersAsync(
                    _config.BatchStorageConnectionString,
                    _config.BatchAudioContainerName,
                    _config.BatchResultContainerName,
                    token);

                BlobClient uploadedBlob;
                try
                {
                    uploadedBlob = await UploadAudioToBlobAsync(
                        uploadPath,
                        audioContainer,
                        token);

                    if (ShouldWriteBatchLogSuccess)
                    {
                        var blobProps = await uploadedBlob.GetPropertiesAsync(cancellationToken: token);
                        var eTag = blobProps.Value.ETag.ToString();
                        var requestId = blobProps.GetRawResponse().Headers.TryGetValue("x-ms-request-id", out var rid)
                            ? rid
                            : "";
                        AppendBatchLog("BlobUpload", audioFileName, "Success",
                            $"container={audioContainer.Name} blob={uploadedBlob.Name} etag={eTag} requestId={requestId}");
                    }
                }
                catch (Exception ex)
                {
                    if (ShouldWriteBatchLogFailure)
                    {
                        AppendBatchLog("BlobUpload", audioFileName, "Failed",
                            $"container={audioContainer.Name} file={Path.GetFileName(uploadPath)} error={ex.Message}");
                    }
                    throw;
                }

                if (!string.IsNullOrWhiteSpace(tempUploadPath))
                {
                    try
                    {
                        File.Delete(tempUploadPath);
                    }
                    catch
                    {
                        // ignore temp cleanup failures
                    }
                    tempUploadPath = null;
                }

                var contentUrl = CreateBlobReadSasUri(uploadedBlob, TimeSpan.FromHours(24));

                onStatus?.Invoke("批量转写：提交任务...");

                var splitOptions = GetBatchSubtitleSplitOptions();
                Action<string, string>? batchLog = ShouldWriteBatchLogSuccess
                    ? (evt, msg) => AppendBatchLog(evt, audioFileName, "Success", msg)
                    : null;
                var (cues, transcriptionJson) = await BatchTranscribeSpeechToCuesAsync(
                    contentUrl,
                    _config.SourceLanguage,
                    subscription,
                    token,
                    status => onStatus?.Invoke(status),
                    splitOptions,
                    batchLog);

                if (ShouldWriteBatchLogSuccess)
                {
                    AppendBatchLog("TranscribeResult", audioFileName, "Success", $"cues={cues.Count}");
                }

                if (cues.Count == 0)
                {
                    return false;
                }

                var outputPath = GetSpeechSubtitlePath(audioPath);
                WriteVttFile(outputPath, cues);

                var baseName = Path.GetFileNameWithoutExtension(audioPath);
                await UploadTextToBlobAsync(outputContainer, baseName + ".speech.vtt", File.ReadAllText(outputPath), "text/vtt", token);
                await UploadTextToBlobAsync(outputContainer, baseName + ".speech.json", transcriptionJson, "application/json", token);

                return true;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempUploadPath))
                {
                    try
                    {
                        File.Delete(tempUploadPath);
                    }
                    catch
                    {
                        // ignore temp cleanup failures
                    }
                }
            }
        }

        private static string PrepareBatchUploadAudioPath(
            string audioPath,
            Action<string>? onStatus,
            CancellationToken token,
            out string? tempUploadPath,
            out bool converted)
        {
            tempUploadPath = null;
            converted = false;

            if (IsPcm16kMonoWav(audioPath))
            {
                return audioPath;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "TranslationToolUI", "BatchAudio");
            Directory.CreateDirectory(tempDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var unique = Guid.NewGuid().ToString("N");
            tempUploadPath = Path.Combine(
                tempDir,
                $"{Path.GetFileNameWithoutExtension(audioPath)}_{stamp}_{unique}_pcm16k_mono.wav");

            onStatus?.Invoke("批量转写：转换 WAV(16kHz/16-bit/mono)...");

            try
            {
                ConvertToPcm16kMonoWav(audioPath, tempUploadPath, token);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"音频转换失败: {ex.Message}", ex);
            }

            converted = true;

            return tempUploadPath;
        }

        private static bool IsPcm16kMonoWav(string audioPath)
        {
            if (!string.Equals(Path.GetExtension(audioPath), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using var reader = new WaveFileReader(audioPath);
                var format = reader.WaveFormat;
                return format.Encoding == WaveFormatEncoding.Pcm
                       && format.SampleRate == 16000
                       && format.BitsPerSample == 16
                       && format.Channels == 1;
            }
            catch
            {
                return false;
            }
        }

        private static void ConvertToPcm16kMonoWav(string sourcePath, string outputPath, CancellationToken token)
        {
            using var reader = new AudioFileReader(sourcePath);
            using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1))
            {
                ResamplerQuality = 60
            };
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            using var writer = new WaveFileWriter(outputPath, resampler.WaveFormat);
            var buffer = new byte[resampler.WaveFormat.AverageBytesPerSecond];
            int read;
            while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                writer.Write(buffer, 0, read);
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

        private BatchSubtitleSplitOptions GetBatchSubtitleSplitOptions()
        {
            return new BatchSubtitleSplitOptions
            {
                EnableSentenceSplit = _config.EnableBatchSubtitleSentenceSplit,
                SplitOnComma = _config.BatchSubtitleSplitOnComma,
                MaxChars = Math.Clamp(_config.BatchSubtitleMaxChars, 6, 80),
                MaxDurationSeconds = Math.Clamp(_config.BatchSubtitleMaxDurationSeconds, 1, 15),
                PauseSplitMs = Math.Clamp(_config.BatchSubtitlePauseSplitMs, 100, 2000)
            };
        }

        private sealed class BatchSubtitleSplitOptions
        {
            public bool EnableSentenceSplit { get; set; }
            public bool SplitOnComma { get; set; }
            public int MaxChars { get; set; }
            public double MaxDurationSeconds { get; set; }
            public int PauseSplitMs { get; set; }
        }

        private static async Task<(List<SubtitleCue> Cues, string TranscriptionJson)> BatchTranscribeSpeechToCuesAsync(
            Uri contentUrl,
            string locale,
            AzureSubscription subscription,
            CancellationToken token,
            Action<string> onStatus,
            BatchSubtitleSplitOptions splitOptions,
            Action<string, string>? onBatchLog = null)
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

            onBatchLog?.Invoke("TranscribeSubmit", $"url={statusUrl}");

            string? lastStatusJson = null;
            string? lastPolledStatus = null;
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

                if (!string.Equals(status, lastPolledStatus, StringComparison.OrdinalIgnoreCase))
                {
                    onBatchLog?.Invoke("TranscribePoll", $"status={status}");
                    lastPolledStatus = status;
                }

                if (string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    onStatus("批量转写：已完成，整理字幕...");
                    break;
                }

                if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    var errorSummary = BuildSpeechBatchFailureSummary(statusDoc) ?? "批量转写失败";
                    var ex = new InvalidOperationException(errorSummary);
                    ex.Data["SpeechBatchError"] = statusBody;
                    throw ex;
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
            var cues = ParseBatchTranscriptionToCues(transcriptionJson, splitOptions);
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

        private static string? BuildSpeechBatchFailureSummary(JsonDocument statusDoc)
        {
            if (!statusDoc.RootElement.TryGetProperty("error", out var errorElement))
            {
                return null;
            }

            var code = errorElement.TryGetProperty("code", out var codeElement)
                ? codeElement.GetString()
                : null;
            var message = errorElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;

            var detailMessages = new List<string>();
            if (errorElement.TryGetProperty("details", out var detailsElement) &&
                detailsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in detailsElement.EnumerateArray())
                {
                    var detailMessage = detail.TryGetProperty("message", out var detailMessageElement)
                        ? detailMessageElement.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(detailMessage))
                    {
                        detailMessages.Add(detailMessage);
                    }
                }
            }

            var summaryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(code))
            {
                summaryParts.Add(code);
            }
            if (!string.IsNullOrWhiteSpace(message))
            {
                summaryParts.Add(message);
            }
            if (detailMessages.Count > 0)
            {
                summaryParts.Add(string.Join("; ", detailMessages));
            }

            if (summaryParts.Count == 0)
            {
                return null;
            }

            return "批量转写失败: " + string.Join(" | ", summaryParts);
        }

        private static List<SubtitleCue> ParseBatchTranscriptionToCues(string transcriptionJson, BatchSubtitleSplitOptions splitOptions)
        {
            var list = new List<SubtitleCue>();
            using var doc = JsonDocument.Parse(transcriptionJson);
            if (!doc.RootElement.TryGetProperty("recognizedPhrases", out var phrases) || phrases.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var phrase in phrases.EnumerateArray())
            {
                var text = ExtractPhraseText(phrase);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var speaker = phrase.TryGetProperty("speaker", out var speakerElement)
                    ? speakerElement.ToString()
                    : "";
                var speakerLabel = string.IsNullOrWhiteSpace(speaker) ? "Speaker" : $"Speaker {speaker}";

                if (splitOptions.EnableSentenceSplit && TryGetPhraseWords(phrase, out var words) && words.Count > 0)
                {
                    list.AddRange(SplitPhraseToCues(words, text, speakerLabel, splitOptions));
                    continue;
                }

                if (!TryParseBatchOffsetDuration(phrase, out var start, out var end))
                {
                    continue;
                }

                list.Add(new SubtitleCue
                {
                    Start = start,
                    End = end,
                    Text = $"{speakerLabel}: {text}"
                });
            }

            return list.OrderBy(c => c.Start).ToList();
        }

        private sealed class BatchWordInfo
        {
            public required string Text { get; init; }
            public required TimeSpan Start { get; init; }
            public required TimeSpan End { get; init; }
        }

        private static bool TryGetPhraseWords(JsonElement phrase, out List<BatchWordInfo> words)
        {
            words = new List<BatchWordInfo>();
            if (!phrase.TryGetProperty("nBest", out var nbest) || nbest.ValueKind != JsonValueKind.Array || nbest.GetArrayLength() == 0)
            {
                return false;
            }

            var best = nbest[0];
            if (!best.TryGetProperty("words", out var wordsElement) || wordsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var wordElement in wordsElement.EnumerateArray())
            {
                if (!wordElement.TryGetProperty("word", out var wordTextElement))
                {
                    continue;
                }

                var wordText = wordTextElement.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(wordText))
                {
                    continue;
                }

                if (!TryGetWordTiming(wordElement, out var start, out var end))
                {
                    continue;
                }

                words.Add(new BatchWordInfo
                {
                    Text = wordText,
                    Start = start,
                    End = end
                });
            }

            return words.Count > 0;
        }

        private static bool TryGetWordTiming(JsonElement wordElement, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;
            if (!TryGetTimeValue(wordElement, "offset", out start) && !TryGetTimeValue(wordElement, "offsetInTicks", out start))
            {
                return false;
            }

            if (!TryGetTimeValue(wordElement, "duration", out var duration) && !TryGetTimeValue(wordElement, "durationInTicks", out duration))
            {
                duration = TimeSpan.Zero;
            }

            end = start + duration;
            return true;
        }

        private static List<SubtitleCue> SplitPhraseToCues(
            List<BatchWordInfo> words,
            string displayText,
            string speakerLabel,
            BatchSubtitleSplitOptions splitOptions)
        {
            var cues = new List<SubtitleCue>();
            if (words.Count == 0)
            {
                return cues;
            }

            var breakIndices = GetPunctuationBreakIndices(displayText, words, splitOptions.SplitOnComma);
            var segmentStartIndex = 0;
            var segmentStartCharIndex = 0;
            var segmentCharCount = 0;
            var segmentStartTime = words[0].Start;
            var currentCharIndex = 0;

            for (var i = 0; i < words.Count; i++)
            {
                var word = words[i];
                var wordLength = GetWordLength(word.Text);
                segmentCharCount += wordLength;
                currentCharIndex += wordLength;

                var durationSeconds = (word.End - segmentStartTime).TotalSeconds;
                var nextGapMs = i + 1 < words.Count
                    ? (words[i + 1].Start - word.End).TotalMilliseconds
                    : 0;

                var shouldSplit = false;
                if (breakIndices.Contains(i))
                {
                    shouldSplit = true;
                }
                else if (splitOptions.PauseSplitMs > 0 && nextGapMs >= splitOptions.PauseSplitMs)
                {
                    shouldSplit = true;
                }
                else if (splitOptions.MaxChars > 0 && segmentCharCount >= splitOptions.MaxChars)
                {
                    shouldSplit = true;
                }
                else if (splitOptions.MaxDurationSeconds > 0 && durationSeconds >= splitOptions.MaxDurationSeconds)
                {
                    shouldSplit = true;
                }

                if (!shouldSplit && i < words.Count - 1)
                {
                    continue;
                }

                var segmentEndIndex = i;
                var segmentEndCharIndex = currentCharIndex;
                var segmentText = TrySliceDisplaySegment(displayText, segmentStartCharIndex, segmentEndCharIndex)
                    ?? string.Concat(words.Skip(segmentStartIndex).Take(segmentEndIndex - segmentStartIndex + 1)
                        .Select(w => w.Text));

                segmentText = NormalizeSubtitleText(segmentText);
                if (!string.IsNullOrWhiteSpace(segmentText))
                {
                    cues.Add(new SubtitleCue
                    {
                        Start = segmentStartTime,
                        End = word.End,
                        Text = $"{speakerLabel}: {segmentText}"
                    });
                }

                segmentStartIndex = i + 1;
                segmentStartCharIndex = segmentEndCharIndex;
                segmentCharCount = 0;
                if (segmentStartIndex < words.Count)
                {
                    segmentStartTime = words[segmentStartIndex].Start;
                }
            }

            return cues;
        }

        private static HashSet<int> GetPunctuationBreakIndices(
            string displayText,
            List<BatchWordInfo> words,
            bool splitOnComma)
        {
            var breakIndices = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(displayText) || words.Count == 0)
            {
                return breakIndices;
            }

            var wordEndOffsets = new List<int>(words.Count);
            var running = 0;
            foreach (var word in words)
            {
                running += GetWordLength(word.Text);
                wordEndOffsets.Add(running);
            }

            var charCount = 0;
            foreach (var ch in displayText)
            {
                if (char.IsWhiteSpace(ch))
                {
                    continue;
                }

                if (IsSentenceBreakPunctuation(ch, splitOnComma))
                {
                    if (charCount <= 0)
                    {
                        continue;
                    }

                    var idx = wordEndOffsets.FindIndex(end => end >= charCount);
                    if (idx >= 0)
                    {
                        breakIndices.Add(idx);
                    }
                    continue;
                }

                if (IsSkippableDisplayChar(ch))
                {
                    continue;
                }

                charCount++;
            }

            return breakIndices;
        }

        private static string? TrySliceDisplaySegment(string displayText, int startCharIndex, int endCharIndex)
        {
            if (string.IsNullOrWhiteSpace(displayText))
            {
                return null;
            }

            var charMap = new List<int>();
            for (var i = 0; i < displayText.Length; i++)
            {
                var ch = displayText[i];
                if (char.IsWhiteSpace(ch) || IsSkippableDisplayChar(ch))
                {
                    continue;
                }

                charMap.Add(i);
            }

            if (charMap.Count == 0)
            {
                return displayText.Trim();
            }

            var safeStart = Math.Clamp(startCharIndex, 0, charMap.Count - 1);
            var safeEnd = Math.Clamp(endCharIndex, safeStart + 1, charMap.Count);
            var startIndex = charMap[safeStart];
            var endIndex = charMap[safeEnd - 1];

            while (endIndex + 1 < displayText.Length)
            {
                var ch = displayText[endIndex + 1];
                if (char.IsWhiteSpace(ch) || IsSentenceBreakPunctuation(ch, splitOnComma: true))
                {
                    endIndex++;
                    continue;
                }

                break;
            }

            var segment = displayText.Substring(startIndex, endIndex - startIndex + 1);
            return segment.Trim();
        }

        private static int GetWordLength(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? 0 : text.Replace(" ", "").Length;
        }

        private static bool IsSkippableDisplayChar(char ch)
        {
            return "。！？!?；;，,、：:".IndexOf(ch) >= 0;
        }

        private static bool IsSentenceBreakPunctuation(char ch, bool splitOnComma)
        {
            if ("。！？!?；;".IndexOf(ch) >= 0)
            {
                return true;
            }

            if (splitOnComma && "，,".IndexOf(ch) >= 0)
            {
                return true;
            }

            return false;
        }

        private static string NormalizeSubtitleText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            return System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
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

        private string GetTranscriptionSourceLanguage()
        {
            if (string.Equals(_config.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-CN";
            }

            return _config.SourceLanguage;
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
            speechConfig.SpeechRecognitionLanguage = GetTranscriptionSourceLanguage();

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

            var regex = new System.Text.RegularExpressions.Regex(@"\[(?<time>(\d{1,2}:)?\d{2}:\d{2})\](?!\()",
                System.Text.RegularExpressions.RegexOptions.Compiled);

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

            var match = System.Text.RegularExpressions.Regex.Match(line, @"(?<start>\d{2}:\d{2}:\d{2}[\.,]\d{3})\s*-->\s*(?<end>\d{2}:\d{2}:\d{2}[\.,]\d{3})");
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
    }
}
