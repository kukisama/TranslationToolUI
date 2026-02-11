using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TranslationToolUI.Views
{
    public partial class ImagePreviewWindow : Window
    {
        private readonly List<string> _filePaths;
        private int _currentIndex;
        private Bitmap? _bitmap;

        public ImagePreviewWindow()
        {
            InitializeComponent();
            _filePaths = new List<string>();
            _currentIndex = 0;
        }

        public ImagePreviewWindow(string filePath) : this(new[] { filePath }, 0)
        {
        }

        public ImagePreviewWindow(IReadOnlyList<string> filePaths, int startIndex = 0)
        {
            InitializeComponent();

            _filePaths = filePaths
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .ToList();

            if (_filePaths.Count == 0)
            {
                _filePaths.Add(filePaths.FirstOrDefault() ?? "");
            }

            _currentIndex = Math.Clamp(startIndex, 0, Math.Max(0, _filePaths.Count - 1));

            LoadImage();
            SetupEventHandlers();
        }

        private void LoadImage()
        {
            try
            {
                if (_filePaths.Count == 0)
                {
                    FileInfoTextBlock.Text = "未找到图片";
                    return;
                }

                var filePath = _filePaths[_currentIndex];
                if (!File.Exists(filePath))
                {
                    FileInfoTextBlock.Text = "文件不存在";
                    return;
                }

                _bitmap?.Dispose();
                _bitmap = new Bitmap(filePath);
                PreviewImage.Source = _bitmap;

                Title = $"图片预览 — {Path.GetFileName(filePath)}";

                var fileInfo = new FileInfo(filePath);
                var sizeKb = fileInfo.Length / 1024.0;
                FileInfoTextBlock.Text = $"{_bitmap.PixelSize.Width}×{_bitmap.PixelSize.Height}  |  {sizeKb:F1} KB  |  {Path.GetFileName(filePath)}  |  {_currentIndex + 1}/{_filePaths.Count}";

                UpdateNavigationButtons();
            }
            catch (Exception ex)
            {
                FileInfoTextBlock.Text = $"加载失败: {ex.Message}";
            }
        }

        private void SetupEventHandlers()
        {
            PrevButton.Click += (_, _) => Navigate(-1);
            NextButton.Click += (_, _) => Navigate(1);
            DownloadButton.Click += async (_, _) => await DownloadAsync();
            OpenLocationButton.Click += (_, _) => OpenLocation();
            CopyButton.Click += async (_, _) => await CopyToClipboardAsync();

            KeyDown += OnWindowKeyDown;
        }

        private void Navigate(int offset)
        {
            if (_filePaths.Count <= 1)
                return;

            var nextIndex = _currentIndex + offset;
            if (nextIndex < 0 || nextIndex >= _filePaths.Count)
                return;

            _currentIndex = nextIndex;
            LoadImage();
        }

        private void UpdateNavigationButtons()
        {
            PrevButton.IsEnabled = _currentIndex > 0;
            NextButton.IsEnabled = _currentIndex < _filePaths.Count - 1;
        }

        /// <summary>
        /// 拖拽窗口时检查最小尺寸——以工具栏不被折叠为限
        /// </summary>
        private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // 确保宽度不小于 480，高度不小于 200（工具栏 ~50px + 图片区最小 150px）
            if (Width < 480) Width = 480;
            if (Height < 200) Height = 200;
        }

        private async Task DownloadAsync()
        {
            if (_filePaths.Count == 0)
                return;

            var filePath = _filePaths[_currentIndex];
            if (!File.Exists(filePath))
                return;

            var storageProvider = StorageProvider;
            var result = await storageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "保存图片",
                SuggestedFileName = Path.GetFileName(filePath)
            });

            if (result == null)
                return;

            await using var source = File.OpenRead(filePath);
            await using var target = await result.OpenWriteAsync();
            await source.CopyToAsync(target);
        }

        private void OpenLocation()
        {
            if (_filePaths.Count == 0)
                return;

            var filePath = _filePaths[_currentIndex];
            if (!File.Exists(filePath))
                return;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dir,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch { }
        }

        private async Task CopyToClipboardAsync()
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
                return;

            if (_bitmap != null)
            {
                if (!await TrySetImageOnClipboardAsync(clipboard, _bitmap))
                {
                    await clipboard.SetTextAsync(_filePaths[_currentIndex]);
                }
            }
            else if (_filePaths.Count > 0)
            {
                await clipboard.SetTextAsync(_filePaths[_currentIndex]);
            }
        }

        private static async Task<bool> TrySetImageOnClipboardAsync(object clipboard, Bitmap bitmap)
        {
            var clipboardType = clipboard.GetType();

            var setImageAsync = clipboardType.GetMethod("SetImageAsync");
            if (setImageAsync != null)
            {
                if (setImageAsync.Invoke(clipboard, new object?[] { bitmap }) is Task task)
                {
                    await task;
                    return true;
                }
            }

            return false;
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
                case Key.Left:
                    Navigate(-1);
                    e.Handled = true;
                    break;
                case Key.Right:
                    Navigate(1);
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            base.OnClosing(e);
        }
    }
}
