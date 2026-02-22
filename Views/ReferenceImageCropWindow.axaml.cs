using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TrueFluentPro.Services;
using TrueFluentPro.ViewModels;

namespace TrueFluentPro.Views
{
    public partial class ReferenceImageCropWindow : Window
    {
        private readonly ReferenceImageCropViewModel _vm;
        private Bitmap? _previewBitmap;
        private Bitmap? _originalBitmap;
        private bool _isRendering;
        private bool _renderPending;
        private bool _isDragging;
        private Point _lastPointerPosition;
        private Rect _imageDisplayRect;

        private static readonly SolidColorBrush IdleFrameBorderBrush = new(Color.FromArgb(255, 30, 136, 229));
        private static readonly SolidColorBrush IdleFrameBackgroundBrush = new(Color.FromArgb(56, 30, 136, 229));
        private static readonly SolidColorBrush IdleGuideBrush = new(Color.FromArgb(195, 30, 136, 229));

        private static readonly SolidColorBrush ActiveFrameBorderBrush = new(Color.FromArgb(255, 21, 101, 192));
        private static readonly SolidColorBrush ActiveFrameBackgroundBrush = new(Color.FromArgb(80, 21, 101, 192));
        private static readonly SolidColorBrush ActiveGuideBrush = new(Color.FromArgb(220, 21, 101, 192));

        public bool CropApplied { get; private set; }

        public ReferenceImageCropWindow()
        {
            InitializeComponent();
            _vm = new ReferenceImageCropViewModel(string.Empty, 1280, 720);
            DataContext = _vm;
        }

        public ReferenceImageCropWindow(string sourcePath, int targetWidth, int targetHeight)
        {
            InitializeComponent();

            _vm = new ReferenceImageCropViewModel(sourcePath, targetWidth, targetHeight);
            DataContext = _vm;

            _vm.PropertyChanged += ViewModel_PropertyChanged;
            Opened += OnOpened;
        }

        private async void OnOpened(object? sender, EventArgs e)
        {
            try
            {
                _originalBitmap = new Bitmap(_vm.SourcePath);
                OriginalImage.Source = _originalBitmap;
                UpdateOverlayVisualState(isActive: false);
                RefreshCropFrameOverlay();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"加载原图失败：{ex.Message}";
            }

            await RequestPreviewRenderAsync();
        }

        private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ReferenceImageCropViewModel.CenterX)
                or nameof(ReferenceImageCropViewModel.CenterY)
                or nameof(ReferenceImageCropViewModel.Zoom))
            {
                RefreshCropFrameOverlay();
                await RequestPreviewRenderAsync();
            }
        }

        private async Task RequestPreviewRenderAsync()
        {
            if (_isRendering)
            {
                _renderPending = true;
                return;
            }

            do
            {
                _renderPending = false;
                await RenderPreviewCoreAsync();
            }
            while (_renderPending);
        }

        private async Task RenderPreviewCoreAsync()
        {
            if (_originalBitmap == null)
                return;

            _isRendering = true;
            try
            {
                var bytes = await Task.Run(() => ImageCropService.BuildPreviewPng(
                    _vm.SourcePath,
                    _vm.TargetWidth,
                    _vm.TargetHeight,
                    _vm.CenterX,
                    _vm.CenterY,
                    _vm.Zoom));

                await using var ms = new MemoryStream(bytes);
                var bitmap = new Bitmap(ms);

                _previewBitmap?.Dispose();
                _previewBitmap = bitmap;
                PreviewImage.Source = _previewBitmap;
                StatusText.Text = string.Empty;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"预览失败：{ex.Message}";
            }
            finally
            {
                _isRendering = false;
            }
        }

        private void SelectorHost_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            RefreshCropFrameOverlay();
        }

        private void SelectorHost_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_originalBitmap == null)
                return;

            if (!e.GetCurrentPoint(SelectorHost).Properties.IsLeftButtonPressed)
                return;

            var current = e.GetPosition(SelectorHost);
            if (!_imageDisplayRect.Contains(current))
                return;

            _isDragging = true;
            _lastPointerPosition = current;
            e.Pointer.Capture(SelectorHost);
            UpdateOverlayVisualState(isActive: true);
            e.Handled = true;
        }

        private async void SelectorHost_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging)
                return;

            var current = e.GetPosition(SelectorHost);
            var dx = current.X - _lastPointerPosition.X;
            var dy = current.Y - _lastPointerPosition.Y;
            _lastPointerPosition = current;

            if (_imageDisplayRect.Width <= 1 || _imageDisplayRect.Height <= 1)
                return;

            _vm.CenterX = Clamp(_vm.CenterX + dx / _imageDisplayRect.Width, 0d, 1d);
            _vm.CenterY = Clamp(_vm.CenterY + dy / _imageDisplayRect.Height, 0d, 1d);

            RefreshCropFrameOverlay();
            await RequestPreviewRenderAsync();
            e.Handled = true;
        }

        private void SelectorHost_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDragging)
                return;

            _isDragging = false;
            e.Pointer.Capture(null);
            UpdateOverlayVisualState(isActive: false);
            e.Handled = true;
        }

        private void SelectorHost_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (!_isDragging)
                return;

            _isDragging = false;
            UpdateOverlayVisualState(isActive: false);
        }

        private async void SelectorHost_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            var delta = e.Delta.Y;
            if (Math.Abs(delta) < 0.001)
                return;

            _vm.Zoom = Clamp(_vm.Zoom + delta * 0.15, 1d, 6d);
            RefreshCropFrameOverlay();
            await RequestPreviewRenderAsync();
            e.Handled = true;
        }

        private void RefreshCropFrameOverlay()
        {
            if (_originalBitmap == null)
                return;

            var hostWidth = SelectorHost.Bounds.Width;
            var hostHeight = SelectorHost.Bounds.Height;
            if (hostWidth <= 1 || hostHeight <= 1)
                return;

            SelectorCanvas.Width = hostWidth;
            SelectorCanvas.Height = hostHeight;

            var sourceWidth = _originalBitmap.PixelSize.Width;
            var sourceHeight = _originalBitmap.PixelSize.Height;

            _imageDisplayRect = CalculateDisplayRect(hostWidth, hostHeight, sourceWidth, sourceHeight);

            Canvas.SetLeft(OriginalImage, _imageDisplayRect.X);
            Canvas.SetTop(OriginalImage, _imageDisplayRect.Y);
            OriginalImage.Width = _imageDisplayRect.Width;
            OriginalImage.Height = _imageDisplayRect.Height;

            var cropInSource = CalculateCropRectInSource(sourceWidth, sourceHeight);

            var cropLeft = _imageDisplayRect.X + (cropInSource.X / sourceWidth) * _imageDisplayRect.Width;
            var cropTop = _imageDisplayRect.Y + (cropInSource.Y / sourceHeight) * _imageDisplayRect.Height;
            var cropWidth = (cropInSource.Width / sourceWidth) * _imageDisplayRect.Width;
            var cropHeight = (cropInSource.Height / sourceHeight) * _imageDisplayRect.Height;

            Canvas.SetLeft(CropFrame, cropLeft);
            Canvas.SetTop(CropFrame, cropTop);
            CropFrame.Width = Math.Max(2, cropWidth);
            CropFrame.Height = Math.Max(2, cropHeight);
        }

        private Rect CalculateCropRectInSource(int sourceWidth, int sourceHeight)
        {
            var targetRatio = _vm.TargetWidth / (double)_vm.TargetHeight;
            var sourceRatio = sourceWidth / (double)sourceHeight;

            double baseCropWidth;
            double baseCropHeight;

            if (sourceRatio > targetRatio)
            {
                baseCropHeight = sourceHeight;
                baseCropWidth = baseCropHeight * targetRatio;
            }
            else
            {
                baseCropWidth = sourceWidth;
                baseCropHeight = baseCropWidth / targetRatio;
            }

            var cropWidth = baseCropWidth / _vm.Zoom;
            var cropHeight = baseCropHeight / _vm.Zoom;

            var centerX = _vm.CenterX * sourceWidth;
            var centerY = _vm.CenterY * sourceHeight;

            var left = Clamp(centerX - cropWidth / 2d, 0d, sourceWidth - cropWidth);
            var top = Clamp(centerY - cropHeight / 2d, 0d, sourceHeight - cropHeight);

            return new Rect(left, top, cropWidth, cropHeight);
        }

        private static Rect CalculateDisplayRect(double hostWidth, double hostHeight, int sourceWidth, int sourceHeight)
        {
            var sourceRatio = sourceWidth / (double)sourceHeight;
            var hostRatio = hostWidth / hostHeight;

            double drawWidth;
            double drawHeight;
            if (sourceRatio > hostRatio)
            {
                drawWidth = hostWidth;
                drawHeight = hostWidth / sourceRatio;
            }
            else
            {
                drawHeight = hostHeight;
                drawWidth = hostHeight * sourceRatio;
            }

            var left = (hostWidth - drawWidth) / 2d;
            var top = (hostHeight - drawHeight) / 2d;
            return new Rect(left, top, drawWidth, drawHeight);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void UpdateOverlayVisualState(bool isActive)
        {
            CropFrame.BorderBrush = isActive ? ActiveFrameBorderBrush : IdleFrameBorderBrush;
            CropFrame.Background = isActive ? ActiveFrameBackgroundBrush : IdleFrameBackgroundBrush;

            var guideBrush = isActive ? ActiveGuideBrush : IdleGuideBrush;
            GuideV1.BorderBrush = guideBrush;
            GuideV2.BorderBrush = guideBrush;
            GuideH1.BorderBrush = guideBrush;
            GuideH2.BorderBrush = guideBrush;
        }

        private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            CropApplied = false;
            Close(false);
        }

        private async void Crop_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                await ImageCropService.CropAndResizeToFileAsync(
                    _vm.SourcePath,
                    _vm.SourcePath,
                    _vm.TargetWidth,
                    _vm.TargetHeight,
                    _vm.CenterX,
                    _vm.CenterY,
                    _vm.Zoom);

                CropApplied = true;
                Close(true);
            }
            catch (Exception ex)
            {
                CropApplied = false;
                StatusText.Text = $"裁切失败：{ex.Message}";
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _vm.PropertyChanged -= ViewModel_PropertyChanged;
            Opened -= OnOpened;
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            _originalBitmap?.Dispose();
            _originalBitmap = null;
            base.OnClosing(e);
        }
    }
}
