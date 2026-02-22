using System;

namespace TrueFluentPro.ViewModels
{
    /// <summary>
    /// 参考图裁切窗口 ViewModel。
    /// </summary>
    public class ReferenceImageCropViewModel : ViewModelBase
    {
        public string SourcePath { get; }

        public int TargetWidth { get; }

        public int TargetHeight { get; }

        public string TargetSizeText => $"{TargetWidth}×{TargetHeight}";

        public string TargetRatioText => $"{TargetWidth}:{TargetHeight}（固定比例）";

        private double _centerX = 0.5;
        public double CenterX
        {
            get => _centerX;
            set
            {
                if (SetProperty(ref _centerX, value))
                {
                    OnPropertyChanged(nameof(CenterXPercent));
                }
            }
        }

        private double _centerY = 0.5;
        public double CenterY
        {
            get => _centerY;
            set
            {
                if (SetProperty(ref _centerY, value))
                {
                    OnPropertyChanged(nameof(CenterYPercent));
                }
            }
        }

        private double _zoom = 1.0;
        public double Zoom
        {
            get => _zoom;
            set => SetProperty(ref _zoom, value);
        }

        public string CenterXPercent => $"{CenterX * 100:F0}%";

        public string CenterYPercent => $"{CenterY * 100:F0}%";

        public ReferenceImageCropViewModel(string sourcePath, int targetWidth, int targetHeight)
        {
            SourcePath = sourcePath;
            TargetWidth = targetWidth;
            TargetHeight = targetHeight;
        }
    }
}
