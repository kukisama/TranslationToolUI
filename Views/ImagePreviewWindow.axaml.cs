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
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TrueFluentPro.Views
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
            if (_filePaths.Count == 0) return;
            var filePath = _filePaths[_currentIndex];
            if (!File.Exists(filePath)) return;

            if (OperatingSystem.IsWindows())
            {
                if (TryCopyImageToWindowsClipboard(filePath))
                    return;
            }

            // 非 Windows 或图片复制失败时，回退到复制文件路径
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(filePath);
        }

        // ========== Windows 剪贴板图片复制（GDI+ → CF_DIB） ==========

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("gdiplus.dll")]
        private static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);

        [DllImport("gdiplus.dll")]
        private static extern void GdiplusShutdown(IntPtr token);

        [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
        private static extern int GdipCreateBitmapFromFile(string filename, out IntPtr bitmap);

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

        private const uint CF_DIB = 8;
        private const uint GMEM_MOVEABLE = 0x0002;

        /// <summary>
        /// 使用 GDI+ 加载图片 → 保存为 BMP → 去掉 14 字节文件头 → 以 CF_DIB 写入剪贴板。
        /// CF_DIB 是 Word/PPT/画图 等应用粘贴图片最可靠的格式。
        /// </summary>
        private static bool TryCopyImageToWindowsClipboard(string filePath)
        {
            IntPtr gdipToken = IntPtr.Zero;
            IntPtr gdipBitmap = IntPtr.Zero;
            string? tempBmpPath = null;

            try
            {
                // 初始化 GDI+
                var startupInput = new GdiplusStartupInput { GdiplusVersion = 1 };
                if (GdiplusStartup(out gdipToken, ref startupInput, IntPtr.Zero) != 0)
                    return false;

                // 用 GDI+ 加载图片文件（支持 PNG/JPG/BMP/GIF/WEBP 等）
                if (GdipCreateBitmapFromFile(filePath, out gdipBitmap) != 0)
                    return false;

                // 保存为临时 BMP 文件（GDI+ BMP Encoder 保证输出合法的未压缩 BMP）
                tempBmpPath = Path.Combine(Path.GetTempPath(), $"clipboard_{Guid.NewGuid():N}.bmp");
                var bmpEncoderClsid = new Guid("557cf400-1a04-11d3-9a73-0000f81ef32e");
                if (GdipSaveImageToFile(gdipBitmap, tempBmpPath, ref bmpEncoderClsid, IntPtr.Zero) != 0)
                    return false;

                // 释放 GDI+ 资源（不再需要）
                GdipDisposeImage(gdipBitmap);
                gdipBitmap = IntPtr.Zero;
                GdiplusShutdown(gdipToken);
                gdipToken = IntPtr.Zero;

                // 读取 BMP 文件
                var bmpData = File.ReadAllBytes(tempBmpPath);
                if (bmpData.Length <= 54)
                    return false;

                // BMP 文件结构: [14 字节 BITMAPFILEHEADER] [BITMAPINFOHEADER + 像素数据]
                // CF_DIB 格式 = BITMAPFILEHEADER 之后的全部内容
                const int bmpFileHeaderSize = 14;
                var dibLength = bmpData.Length - bmpFileHeaderSize;

                var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)dibLength);
                if (hMem == IntPtr.Zero)
                    return false;

                var pMem = GlobalLock(hMem);
                if (pMem == IntPtr.Zero)
                    return false;

                try
                {
                    Marshal.Copy(bmpData, bmpFileHeaderSize, pMem, dibLength);
                }
                finally
                {
                    GlobalUnlock(hMem);
                }

                if (!OpenClipboard(IntPtr.Zero))
                    return false;

                try
                {
                    EmptyClipboard();
                    if (SetClipboardData(CF_DIB, hMem) != IntPtr.Zero)
                    {
                        // 剪贴板接管了内存所有权，不需要 GlobalFree
                        return true;
                    }
                    return false;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"剪贴板图片复制失败: {ex.Message}");
                return false;
            }
            finally
            {
                if (gdipBitmap != IntPtr.Zero) GdipDisposeImage(gdipBitmap);
                if (gdipToken != IntPtr.Zero) GdiplusShutdown(gdipToken);
                if (tempBmpPath != null)
                {
                    try { File.Delete(tempBmpPath); } catch { }
                }
            }
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
