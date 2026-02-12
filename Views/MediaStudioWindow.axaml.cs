using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

        private void SessionListBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2)
            {
                _ = StartRenameSessionAsync();
                e.Handled = true;
            }
        }

        private void RenameSession_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _ = StartRenameSessionAsync();
        }

        private void ResumeVideoTasks_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_viewModel?.CurrentSession == null) return;

            var session = _viewModel.CurrentSession;
            var cancelledTasks = session.TaskHistory
                .Where(t => t.Type == MediaGenType.Video
                    && (t.Status == MediaGenStatus.Cancelled || t.Status == MediaGenStatus.Failed)
                    && !string.IsNullOrEmpty(t.RemoteVideoId))
                .ToList();

            if (cancelledTasks.Count == 0)
            {
                // 无可恢复的任务
                return;
            }

            foreach (var task in cancelledTasks)
            {
                session.ResumeVideoTask(task);
            }
        }

        private async Task StartRenameSessionAsync()
        {
            if (_viewModel?.CurrentSession == null) return;

            var currentName = _viewModel.CurrentSession.SessionName;

            // 使用简单的输入对话框
            var dialog = new Window
            {
                Title = "重命名会话",
                Width = 360,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false
            };

            var textBox = new TextBox
            {
                Text = currentName,
                Margin = new Thickness(16, 16, 16, 8),
                Watermark = "输入新名称..."
            };

            var okButton = new Button
            {
                Content = "确定",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Padding = new Thickness(20, 6),
                IsDefault = true
            };

            var cancelButton = new Button
            {
                Content = "取消",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Padding = new Thickness(20, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };

            string? result = null;
            okButton.Click += (_, _) =>
            {
                result = textBox.Text?.Trim();
                dialog.Close();
            };
            cancelButton.Click += (_, _) => dialog.Close();

            textBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    result = textBox.Text?.Trim();
                    dialog.Close();
                    e.Handled = true;
                }
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Thickness(16, 0, 16, 16),
                Spacing = 8
            };
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(okButton);

            var stack = new StackPanel();
            stack.Children.Add(textBox);
            stack.Children.Add(buttonPanel);
            dialog.Content = stack;

            dialog.Opened += (_, _) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };

            await dialog.ShowDialog(this);

            if (!string.IsNullOrWhiteSpace(result) && result != currentName)
            {
                _viewModel.RenameCurrentSession(result);
            }
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
