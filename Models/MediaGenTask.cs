using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TranslationToolUI.Models
{
    public enum MediaGenType
    {
        Image,
        Video
    }

    public enum MediaGenStatus
    {
        Queued,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// 单个生成任务（图片或视频）
    /// </summary>
    public class MediaGenTask : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public MediaGenType Type { get; set; }

        private MediaGenStatus _status;
        public MediaGenStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string Prompt { get; set; } = "";

        private int _progress;
        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        private string? _resultFilePath;
        public string? ResultFilePath
        {
            get => _resultFilePath;
            set { _resultFilePath = value; OnPropertyChanged(); }
        }

        private string? _errorMessage;
        public string? ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // 视频专用
        public string? RemoteVideoId { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
