using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using TranslationToolUI.Models;
using TranslationToolUI.Services;

namespace TranslationToolUI.ViewModels
{
    /// <summary>
    /// Media Studio 主 ViewModel — 管理会话列表、全局状态
    /// </summary>
    public class MediaStudioViewModel : ViewModelBase, IDisposable
    {
        private readonly AiConfig _aiConfig;
        private readonly MediaGenConfig _genConfig;
        private readonly AiImageGenService _imageService = new();
        private readonly AiVideoGenService _videoService = new();
        private readonly AzureTokenProvider _imageTokenProvider = new();
        private readonly AzureTokenProvider _videoTokenProvider;
        private readonly string _studioDirectory;

        // --- 会话管理 ---
        public ObservableCollection<MediaSessionViewModel> Sessions { get; } = new();

        private MediaSessionViewModel? _currentSession;
        public MediaSessionViewModel? CurrentSession
        {
            get => _currentSession;
            set
            {
                if (SetProperty(ref _currentSession, value))
                {
                    OnPropertyChanged(nameof(HasCurrentSession));
                }
            }
        }

        public bool HasCurrentSession => CurrentSession != null;

        // --- 全局状态 ---
        private int _activeTaskCount;
        public int ActiveTaskCount
        {
            get => _activeTaskCount;
            set
            {
                if (SetProperty(ref _activeTaskCount, value))
                    OnPropertyChanged(nameof(HasActiveTasks));
            }
        }

        public bool HasActiveTasks => ActiveTaskCount > 0;

        private string _statusText = "就绪";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // --- 命令 ---
        public ICommand NewSessionCommand { get; }
        public ICommand DeleteSessionCommand { get; }
        public ICommand RenameSessionCommand { get; }

        public MediaStudioViewModel(AiConfig aiConfig, MediaGenConfig genConfig)
        {
            _aiConfig = aiConfig;
            _genConfig = genConfig;
            _videoTokenProvider = _genConfig.VideoUseImageEndpoint
                ? _imageTokenProvider
                : new AzureTokenProvider();

            _imageService.SetTokenProvider(_imageTokenProvider);
            _videoService.SetTokenProvider(_videoTokenProvider);

            var sessionsPath = PathManager.Instance.SessionsPath;
            _studioDirectory = Path.Combine(sessionsPath, "media-studio");
            Directory.CreateDirectory(_studioDirectory);

            NewSessionCommand = new RelayCommand(_ => CreateNewSession());
            DeleteSessionCommand = new RelayCommand(
                _ => DeleteCurrentSession(),
                _ => CurrentSession != null);
            RenameSessionCommand = new RelayCommand(
                _ => { },  // 由 View 处理弹窗逻辑
                _ => CurrentSession != null);

            // 加载现有会话
            LoadSessions();

            // 若无会话，自动创建一个
            if (Sessions.Count == 0)
            {
                CreateNewSession();
            }

            _ = TrySilentLoginForMediaAsync();

            // 恢复中断的视频任务
            ResumeInterruptedVideoTasks();
        }

        /// <summary>
        /// 遍历所有会话，恢复 Running 状态且有 RemoteVideoId 的视频任务
        /// </summary>
        private void ResumeInterruptedVideoTasks()
        {
            foreach (var session in Sessions)
            {
                var tasksToResume = session.TaskHistory
                    .Where(t => t.Type == MediaGenType.Video
                        && t.Status == MediaGenStatus.Running
                        && !string.IsNullOrEmpty(t.RemoteVideoId))
                    .ToList();

                foreach (var task in tasksToResume)
                {
                    Debug.WriteLine($"恢复视频任务: {task.Id}, VideoId: {task.RemoteVideoId}");
                    session.ResumeVideoTask(task);
                }
            }
        }

        private async Task TrySilentLoginForMediaAsync()
        {
            if (_genConfig.ImageProviderType == AiProviderType.AzureOpenAi
                && _genConfig.ImageAzureAuthMode == AzureAuthMode.AAD)
            {
                await _imageTokenProvider.TrySilentLoginAsync(
                    _genConfig.ImageAzureTenantId,
                    _genConfig.ImageAzureClientId);
            }

            if (!_genConfig.VideoUseImageEndpoint
                && _genConfig.VideoProviderType == AiProviderType.AzureOpenAi
                && _genConfig.VideoAzureAuthMode == AzureAuthMode.AAD)
            {
                await _videoTokenProvider.TrySilentLoginAsync(
                    _genConfig.VideoAzureTenantId,
                    _genConfig.VideoAzureClientId);
            }
        }

        public void CreateNewSession()
        {
            var sessionId = Guid.NewGuid().ToString("N")[..8];
            var sessionDir = Path.Combine(_studioDirectory, $"session_{sessionId}");
            Directory.CreateDirectory(sessionDir);

            var session = new MediaSessionViewModel(
                sessionId, $"会话 {Sessions.Count + 1}",
                sessionDir, _aiConfig, _genConfig,
                _imageService, _videoService,
                OnSessionTaskCountChanged,
                s => SaveSessionMeta(s));

            Sessions.Add(session);
            CurrentSession = session;
            UpdateActiveTaskCount();
            SaveSessionMeta(session);
        }

        public void DeleteCurrentSession()
        {
            if (CurrentSession == null) return;

            var session = CurrentSession;
            session.CancelAll();

            // 标记 session.json 为已删除，下次不再加载
            MarkSessionDeleted(session);

            var idx = Sessions.IndexOf(session);
            Sessions.Remove(session);

            if (Sessions.Count > 0)
            {
                CurrentSession = Sessions[Math.Min(idx, Sessions.Count - 1)];
            }
            else
            {
                CurrentSession = null;
            }

            UpdateActiveTaskCount();
        }

        private void MarkSessionDeleted(MediaSessionViewModel session)
        {
            try
            {
                var metaPath = Path.Combine(session.SessionDirectory, "session.json");
                if (File.Exists(metaPath))
                {
                    var json = File.ReadAllText(metaPath);
                    var sessionData = JsonSerializer.Deserialize<MediaGenSession>(json);
                    if (sessionData != null)
                    {
                        sessionData.IsDeleted = true;
                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        File.WriteAllText(metaPath, JsonSerializer.Serialize(sessionData, options));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"标记会话删除失败: {ex.Message}");
            }
        }

        public void RenameCurrentSession(string newName)
        {
            if (CurrentSession == null) return;
            CurrentSession.SessionName = newName;
            SaveSessionMeta(CurrentSession);
        }

        public void CancelAll()
        {
            foreach (var session in Sessions)
            {
                session.CancelAll();
            }
        }

        private void OnSessionTaskCountChanged()
        {
            Dispatcher.UIThread.Post(UpdateActiveTaskCount);
        }

        private void UpdateActiveTaskCount()
        {
            ActiveTaskCount = Sessions.Sum(s => s.RunningTaskCount);
            StatusText = ActiveTaskCount > 0
                ? $"活跃任务: {ActiveTaskCount}"
                : "就绪";
        }

        // --- 持久化 ---

        private void LoadSessions()
        {
            try
            {
                if (!Directory.Exists(_studioDirectory)) return;

                foreach (var dir in Directory.GetDirectories(_studioDirectory, "session_*"))
                {
                    var metaPath = Path.Combine(dir, "session.json");
                    if (!File.Exists(metaPath)) continue;

                    try
                    {
                        var json = File.ReadAllText(metaPath);
                        var sessionData = JsonSerializer.Deserialize<MediaGenSession>(json);
                        if (sessionData == null) continue;

                        // 跳过已删除的会话
                        if (sessionData.IsDeleted) continue;

                        var session = new MediaSessionViewModel(
                            sessionData.Id, sessionData.Name,
                            dir, _aiConfig, _genConfig,
                            _imageService, _videoService,
                            OnSessionTaskCountChanged,
                            s => SaveSessionMeta(s));

                        // 恢复聊天记录
                        foreach (var msg in sessionData.Messages)
                        {
                            session.Messages.Add(new ChatMessageViewModel(msg));
                        }

                        // 恢复任务历史
                        if (sessionData.Tasks != null)
                        {
                            foreach (var task in sessionData.Tasks)
                            {
                                session.TaskHistory.Add(task);
                            }
                        }

                        Sessions.Add(session);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"加载会话失败 {dir}: {ex.Message}");
                    }
                }

                if (Sessions.Count > 0)
                {
                    CurrentSession = Sessions[0];
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载会话列表失败: {ex.Message}");
            }
        }

        public void SaveSessionMeta(MediaSessionViewModel session)
        {
            try
            {
                var metaPath = Path.Combine(session.SessionDirectory, "session.json");
                var data = new MediaGenSession
                {
                    Id = session.SessionId,
                    Name = session.SessionName,
                    Messages = session.Messages.Select(m => new MediaChatMessage
                    {
                        Role = m.Role,
                        Text = m.Text,
                        MediaPaths = m.MediaPaths.ToList(),
                        Timestamp = m.Timestamp,
                        GenerateSeconds = m.GenerateSeconds,
                        DownloadSeconds = m.DownloadSeconds
                    }).ToList(),
                    Tasks = session.TaskHistory.Select(t => new MediaGenTask
                    {
                        Id = t.Id,
                        Type = t.Type,
                        Status = t.Status,
                        Prompt = t.Prompt,
                        Progress = t.Progress,
                        ResultFilePath = t.ResultFilePath,
                        ErrorMessage = t.ErrorMessage,
                        CreatedAt = t.CreatedAt,
                        RemoteVideoId = t.RemoteVideoId
                    }).ToList()
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                File.WriteAllText(metaPath, JsonSerializer.Serialize(data, options));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存会话元数据失败: {ex.Message}");
            }
        }

        public void SaveAllSessions()
        {
            foreach (var session in Sessions)
            {
                SaveSessionMeta(session);
            }
        }

        public void Dispose()
        {
            SaveAllSessions();
            CancelAll();
        }
    }
}
