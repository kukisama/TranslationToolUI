﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using TranslationToolUI.Services;

namespace TranslationToolUI.ViewModels
{    public class FloatingSubtitleViewModel : INotifyPropertyChanged
    {
        private string _subtitleText = "等待字幕内容...";
        private int _backgroundMode = 0;
        private readonly SubtitleSyncService _syncService;

        public FloatingSubtitleViewModel(SubtitleSyncService syncService)
        {
            _syncService = syncService;
            _syncService.SubtitleUpdated += OnSubtitleUpdated;
        }        public string SubtitleText
        {
            get => _subtitleText;
            set
            {
                if (_subtitleText != value)
                {
                    _subtitleText = value;
                    OnPropertyChanged();
                }
            }
        }

        public int BackgroundMode
        {
            get => _backgroundMode;
            set
            {
                if (_backgroundMode != value)
                {
                    _backgroundMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BackgroundBrush));
                    OnPropertyChanged(nameof(TextBrush));
                }
            }
        }

        public IBrush BackgroundBrush
        {
            get
            {
                return _backgroundMode switch
                {
                    0 => new SolidColorBrush(Colors.Transparent),
                    1 => new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                    2 => new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    _ => new SolidColorBrush(Colors.Transparent)
                };
            }
        }        public IBrush TextBrush
        {
            get
            {
                return _backgroundMode switch
                {
                    0 => new SolidColorBrush(Color.FromRgb(255, 20, 147)),
                    1 => new SolidColorBrush(Colors.White),
                    2 => new SolidColorBrush(Colors.Black),
                    _ => new SolidColorBrush(Color.FromRgb(255, 20, 147))
                };
            }
        }

        public void ToggleTransparency()
        {
            BackgroundMode = (BackgroundMode + 1) % 3;
        }        private void OnSubtitleUpdated(string newSubtitle)
        {
            var processedText = ProcessSubtitleText(newSubtitle);
            
            if (!string.IsNullOrEmpty(processedText) && processedText.Length > 40)
            {
                SubtitleText = processedText.Substring(processedText.Length - 40);
            }
            else
            {
                SubtitleText = processedText;
            }
        }        private string ProcessSubtitleText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "等待字幕内容...";

            text = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty);
            text = text.Trim().Replace('\n', ' ').Replace('\r', ' ');

            if (string.IsNullOrEmpty(text))
                return "等待字幕内容...";

            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

            return text;
        }

        public void OnWindowClosed()
        {
            if (_syncService != null)
            {
                _syncService.SubtitleUpdated -= OnSubtitleUpdated;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

