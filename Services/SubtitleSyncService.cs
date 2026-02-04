using System;
using System.ComponentModel;

namespace TranslationToolUI.Services
{
    public class SubtitleSyncService
    {
        public event Action<string>? SubtitleUpdated;
        private string _currentSubtitle = "";

        public string CurrentSubtitle
        {
            get => _currentSubtitle;
            private set
            {
                if (_currentSubtitle != value)
                {
                    _currentSubtitle = value;
                    SubtitleUpdated?.Invoke(value);
                }
            }
        }

        public void UpdateSubtitle(string subtitle)
        {
            CurrentSubtitle = subtitle ?? "";
        }

        public void ClearSubtitle()
        {
            CurrentSubtitle = "";
        }
    }
}

