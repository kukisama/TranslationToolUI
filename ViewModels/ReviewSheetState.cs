using System.Threading;
using TrueFluentPro.Models;

namespace TrueFluentPro.ViewModels
{
    public class ReviewSheetState : ViewModelBase
    {
        private string _name = "";
        private string _fileTag = "";
        private string _prompt = "";
        private string _markdown = "";
        private string _statusMessage = "";
        private bool _isLoading;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string FileTag
        {
            get => _fileTag;
            set => SetProperty(ref _fileTag, value);
        }

        public string Prompt
        {
            get => _prompt;
            set => SetProperty(ref _prompt, value);
        }

        public string Markdown
        {
            get => _markdown;
            set
            {
                if (SetProperty(ref _markdown, value))
                {
                    OnPropertyChanged(nameof(IsEmpty));
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(IsEmpty));
                }
            }
        }

        public bool IsEmpty => string.IsNullOrWhiteSpace(Markdown) && !IsLoading;

        public CancellationTokenSource? Cts { get; set; }

        public static ReviewSheetState FromPreset(ReviewSheetPreset preset)
        {
            return new ReviewSheetState
            {
                Name = preset.Name,
                FileTag = preset.FileTag,
                Prompt = preset.Prompt
            };
        }
    }
}
