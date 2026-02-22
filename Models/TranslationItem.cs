
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrueFluentPro.Models
{
    public class TranslationItem : INotifyPropertyChanged
    {
        private DateTime _timestamp;
        private string _originalText = "";
        private string _translatedText = "";
        private bool _hasBeenWrittenToFile = false;

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (_timestamp != value)
                {
                    _timestamp = value;
                    OnPropertyChanged();
                }
            }
        }

        public string OriginalText
        {
            get => _originalText;
            set
            {
                if (_originalText != value)
                {
                    _originalText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string TranslatedText
        {
            get => _translatedText;
            set
            {
                if (_translatedText != value)
                {
                    _translatedText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasBeenWrittenToFile
        {
            get => _hasBeenWrittenToFile;
            set
            {
                if (_hasBeenWrittenToFile != value)
                {
                    _hasBeenWrittenToFile = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}


