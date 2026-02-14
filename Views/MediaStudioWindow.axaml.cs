using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
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

            // 在隧道阶段订阅 KeyDown，确保在 TextBox 内部处理 Ctrl+V / Ctrl+Enter 之前拦截
            var promptTextBox = this.FindControl<TextBox>("PromptTextBox");
            promptTextBox?.AddHandler(
                InputElement.KeyDownEvent,
                PromptTextBox_KeyDown,
                Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        private void ScrollToBottom()
        {
            var scrollViewer = this.FindControl<ScrollViewer>("ChatScrollViewer");
            scrollViewer?.ScrollToEnd();
        }

        private async void PromptTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (await TryAttachReferenceImageFromClipboardAsync())
                {
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (_viewModel?.CurrentSession?.GenerateCommand.CanExecute(null) == true)
                {
                    _viewModel.CurrentSession.GenerateCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private async void AttachReferenceImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_viewModel?.CurrentSession == null)
            {
                return;
            }

            var provider = StorageProvider;
            if (provider == null)
            {
                return;
            }

            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择参考图",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("图片")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif" }
                    }
                }
            });

            if (files == null || files.Count == 0)
            {
                return;
            }

            foreach (var file in files)
            {
                var localPath = file.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(localPath))
                {
                    continue;
                }

                await _viewModel.CurrentSession.SetReferenceImageFromFileAsync(localPath);
            }
        }

        private async Task<bool> TryAttachReferenceImageFromClipboardAsync()
        {
            if (_viewModel?.CurrentSession == null)
            {
                return false;
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                return false;
            }

            // 最佳实践：优先尝试直接读取位图
            using (var bitmap = await clipboard.TryGetBitmapAsync())
            {
                if (bitmap != null)
                {
                    var tempPngPath = Path.Combine(Path.GetTempPath(), $"refclip_{Guid.NewGuid():N}.png");
                    try
                    {
                        bitmap.Save(tempPngPath, 100);
                        return await _viewModel.CurrentSession.SetReferenceImageFromFileAsync(tempPngPath);
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(tempPngPath))
                            {
                                File.Delete(tempPngPath);
                            }
                        }
                        catch
                        {
                            // ignore temp cleanup errors
                        }
                    }
                }
            }

            // 其次尝试文本（常见为文件路径）
            var text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var candidates = text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().Trim('"'))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var attachedAny = false;
                foreach (var candidate in candidates)
                {
                    if (!File.Exists(candidate) || !IsImageFile(candidate))
                    {
                        continue;
                    }

                    attachedAny |= await _viewModel.CurrentSession.SetReferenceImageFromFileAsync(candidate);
                }

                if (attachedAny)
                    return true;
            }

            if (OperatingSystem.IsWindows())
            {
                return await TryAttachReferenceImageFromWindowsClipboardBitmapAsync();
            }

            return false;
        }

        private async Task<bool> TryAttachReferenceImageFromWindowsClipboardBitmapAsync()
        {
            if (_viewModel?.CurrentSession == null)
            {
                return false;
            }

            IntPtr gdipToken = IntPtr.Zero;
            IntPtr gdipBitmap = IntPtr.Zero;
            string? tempPngPath = null;

            try
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    return false;
                }

                try
                {
                    var hBitmap = GetClipboardData(CF_BITMAP);
                    if (hBitmap == IntPtr.Zero)
                    {
                        return false;
                    }

                    var startupInput = new GdiplusStartupInput { GdiplusVersion = 1 };
                    if (GdiplusStartup(out gdipToken, ref startupInput, IntPtr.Zero) != 0)
                    {
                        return false;
                    }

                    if (GdipCreateBitmapFromHBITMAP(hBitmap, IntPtr.Zero, out gdipBitmap) != 0)
                    {
                        return false;
                    }

                    tempPngPath = Path.Combine(Path.GetTempPath(), $"refclip_{Guid.NewGuid():N}.png");
                    var pngEncoderClsid = new Guid("557cf406-1a04-11d3-9a73-0000f81ef32e");
                    if (GdipSaveImageToFile(gdipBitmap, tempPngPath, ref pngEncoderClsid, IntPtr.Zero) != 0)
                    {
                        return false;
                    }
                }
                finally
                {
                    CloseClipboard();
                }

                if (string.IsNullOrWhiteSpace(tempPngPath) || !File.Exists(tempPngPath))
                {
                    return false;
                }

                return await _viewModel.CurrentSession.SetReferenceImageFromFileAsync(tempPngPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"从 Windows 剪贴板读取图片失败: {ex.Message}");
                return false;
            }
            finally
            {
                if (gdipBitmap != IntPtr.Zero)
                {
                    GdipDisposeImage(gdipBitmap);
                }

                if (gdipToken != IntPtr.Zero)
                {
                    GdiplusShutdown(gdipToken);
                }

                if (!string.IsNullOrWhiteSpace(tempPngPath))
                {
                    try
                    {
                        File.Delete(tempPngPath);
                    }
                    catch
                    {
                        // ignore temp cleanup errors
                    }
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("gdiplus.dll")]
        private static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);

        [DllImport("gdiplus.dll")]
        private static extern void GdiplusShutdown(IntPtr token);

        [DllImport("gdiplus.dll")]
        private static extern int GdipCreateBitmapFromHBITMAP(IntPtr hbm, IntPtr hpal, out IntPtr bitmap);

        [DllImport("gdiplus.dll")]
        private static extern int GdipDisposeImage(IntPtr image);

        [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
        private static extern int GdipSaveImageToFile(IntPtr image, string filename, ref Guid clsidEncoder, IntPtr encoderParams);

        [StructLayout(LayoutKind.Sequential)]
        private struct GdiplusStartupInput
        {
            public uint GdiplusVersion;
            public IntPtr DebugEventCallback;
            public int SuppressBackgroundThread;
            public int SuppressExternalCodecs;
        }

        private const uint CF_BITMAP = 2;

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

        private void DeleteChatMessage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_viewModel?.CurrentSession == null) return;
            if (sender is not MenuItem mi) return;
            if (mi.Tag is not ChatMessageViewModel msg) return;

            var cmd = _viewModel.CurrentSession.DeleteMessageCommand;
            if (cmd.CanExecute(msg))
            {
                cmd.Execute(msg);
                e.Handled = true;
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

        private async void ReferenceImageThumbnail_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_viewModel?.CurrentSession == null)
                return;

            var session = _viewModel.CurrentSession;
            if (!session.IsVideoMode)
                return;

            if (sender is not Border border)
                return;

            if (border.DataContext is not string filePath || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            if (!session.TryGetCurrentVideoTargetSize(out var targetWidth, out var targetHeight))
            {
                session.StatusText = "当前视频参数组合无可用尺寸映射";
                return;
            }

            var cropWindow = new ReferenceImageCropWindow(filePath, targetWidth, targetHeight);
            var result = await cropWindow.ShowDialog<bool>(this);
            if (!result)
                return;

            session.NotifyReferenceImageUpdated(filePath);
            session.StatusText = $"已将参考图裁切为 {targetWidth}×{targetHeight}";
            e.Handled = true;
        }

        private void OpenImagePreview(System.Collections.Generic.IReadOnlyList<string> mediaPaths, string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            if (VideoFrameExtractorService.TryResolveVideoPathFromFirstFrame(filePath, out var videoPath))
            {
                OpenExternalFile(videoPath);
                return;
            }

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
            previewWindow.Show(this);
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
