using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using TranslationToolUI.Models;
using TranslationToolUI.Services;
using TranslationToolUI.ViewModels;

namespace TranslationToolUI.Views
{
    public partial class MediaStudioWindow : Window
    {
        private readonly MediaStudioViewModel _viewModel;

        public MediaStudioWindow()
        {
            InitializeComponent();
            // 设计器用，不应在运行时调用
            _viewModel = null!;
        }

        public MediaStudioWindow(AiConfig aiConfig, MediaGenConfig genConfig)
        {
            InitializeComponent();

            try
            {
                var icon = AppIconProvider.WindowIcon;
                if (icon != null) Icon = icon;
            }
            catch { /* ignore icon failures */ }

            _viewModel = new MediaStudioViewModel(aiConfig, genConfig);
            DataContext = _viewModel;

            // 监听消息列表变化，自动滚动到底部
            if (_viewModel.CurrentSession != null)
            {
                _viewModel.CurrentSession.Messages.CollectionChanged += (_, _) => ScrollToBottom();
            }

            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MediaStudioViewModel.CurrentSession) && _viewModel.CurrentSession != null)
                {
                    _viewModel.CurrentSession.Messages.CollectionChanged += (_, _) => ScrollToBottom();
                }
            };
        }

        private void ScrollToBottom()
        {
            var scrollViewer = this.FindControl<ScrollViewer>("ChatScrollViewer");
            scrollViewer?.ScrollToEnd();
        }

        private void PromptTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (_viewModel?.CurrentSession?.GenerateCommand.CanExecute(null) == true)
                {
                    _viewModel.CurrentSession.GenerateCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _viewModel?.Dispose();
            base.OnClosing(e);
        }

        private void MediaThumbnail_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // 单击缩略图打开大图预览
            if (e.ClickCount >= 1 && sender is Border border)
            {
                var filePath = border.DataContext as string;
                if (border.Tag is ChatMessageViewModel message)
                {
                    OpenImagePreview(message.MediaPaths, filePath);
                    e.Handled = true;
                }
                else if (!string.IsNullOrWhiteSpace(filePath))
                {
                    OpenImagePreview(new[] { filePath }, filePath);
                    e.Handled = true;
                }
            }
        }

        private void PreviewLargeImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is string filePath)
            {
                if (btn.Tag is ChatMessageViewModel message)
                {
                    OpenImagePreview(message.MediaPaths, filePath);
                }
                else
                {
                    OpenImagePreview(new[] { filePath }, filePath);
                }
            }
        }

        private void OpenImagePreview(System.Collections.Generic.IReadOnlyList<string> mediaPaths, string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            var imagePaths = mediaPaths
                .Where(IsImageFile)
                .ToList();

            if (imagePaths.Count == 0)
            {
                OpenExternalFile(filePath);
                return;
            }

            var index = imagePaths.IndexOf(filePath);
            if (index < 0)
            {
                index = 0;
            }

            var previewWindow = new ImagePreviewWindow(imagePaths, index);
            previewWindow.Show();
        }

        private static bool IsImageFile(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif";
        }

        private static void OpenExternalFile(string filePath)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
