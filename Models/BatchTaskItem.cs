using System.ComponentModel;

namespace TranslationToolUI.Models
{
    public enum BatchTaskStatus
    {
        Pending,
        Running,
        Completed,
        Failed
    }

    public class BatchTaskItem : INotifyPropertyChanged
    {
        private string _fileName = "";
        private string _fullPath = "";
        private BatchTaskStatus _status = BatchTaskStatus.Pending;
        private double _progress;
        private bool _hasAiSubtitle;
        private bool _hasAiSummary;
        private string _statusMessage = "";

        public string FileName
        {
            get => _fileName;
            set => SetProperty(ref _fileName, value, nameof(FileName));
        }

        public string FullPath
        {
            get => _fullPath;
            set => SetProperty(ref _fullPath, value, nameof(FullPath));
        }

        public BatchTaskStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value, nameof(Status));
        }

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value, nameof(Progress));
        }

        public bool HasAiSubtitle
        {
            get => _hasAiSubtitle;
            set => SetProperty(ref _hasAiSubtitle, value, nameof(HasAiSubtitle));
        }

        public bool HasAiSummary
        {
            get => _hasAiSummary;
            set => SetProperty(ref _hasAiSummary, value, nameof(HasAiSummary));
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value, nameof(StatusMessage));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetProperty<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
