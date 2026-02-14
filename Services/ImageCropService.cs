using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace TranslationToolUI.Services
{
    /// <summary>
    /// 参考图裁切与缩放服务（基于 SkiaSharp）。
    /// </summary>
    public static class ImageCropService
    {
        public static bool TryGetImageSize(string imagePath, out int width, out int height)
        {
            width = 0;
            height = 0;

            try
            {
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                    return false;

                using var codec = SKCodec.Create(imagePath);
                if (codec == null)
                    return false;

                width = codec.Info.Width;
                height = codec.Info.Height;
                return width > 0 && height > 0;
            }
            catch
            {
                return false;
            }
        }

        public static async Task CropAndResizeToFileAsync(
            string sourcePath,
            string outputPath,
            int targetWidth,
            int targetHeight,
            double centerX,
            double centerY,
            double zoom,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("源图片不存在", sourcePath);
            if (targetWidth <= 0 || targetHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetWidth), "目标宽高必须大于 0");

            centerX = Clamp(centerX, 0d, 1d);
            centerY = Clamp(centerY, 0d, 1d);
            zoom = Clamp(zoom, 1d, 8d);

            await using var sourceStream = File.OpenRead(sourcePath);
            using var sourceBitmap = SKBitmap.Decode(sourceStream)
                ?? throw new InvalidOperationException("无法解码源图片");

            var srcRect = CalculateCropRect(sourceBitmap.Width, sourceBitmap.Height, targetWidth, targetHeight, centerX, centerY, zoom);

            using var outputBitmap = new SKBitmap(targetWidth, targetHeight, sourceBitmap.ColorType, sourceBitmap.AlphaType);
            using (var canvas = new SKCanvas(outputBitmap))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(sourceBitmap, srcRect, new SKRect(0, 0, targetWidth, targetHeight),
                    new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true });
            }

            using var image = SKImage.FromBitmap(outputBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 95)
                ?? throw new InvalidOperationException("编码裁切图片失败");

            var outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outDir))
                Directory.CreateDirectory(outDir);

            var tempPath = outputPath + ".crop_tmp";
            await using (var fs = File.Create(tempPath))
            {
                data.SaveTo(fs);
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            File.Move(tempPath, outputPath);

            ct.ThrowIfCancellationRequested();
        }

        public static byte[] BuildPreviewPng(
            string sourcePath,
            int targetWidth,
            int targetHeight,
            double centerX,
            double centerY,
            double zoom,
            int previewMaxEdge = 460)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("源图片不存在", sourcePath);

            centerX = Clamp(centerX, 0d, 1d);
            centerY = Clamp(centerY, 0d, 1d);
            zoom = Clamp(zoom, 1d, 8d);

            using var sourceBitmap = SKBitmap.Decode(sourcePath)
                ?? throw new InvalidOperationException("无法解码源图片");

            var srcRect = CalculateCropRect(sourceBitmap.Width, sourceBitmap.Height, targetWidth, targetHeight, centerX, centerY, zoom);

            var scale = Math.Min(previewMaxEdge / (double)targetWidth, previewMaxEdge / (double)targetHeight);
            if (scale > 1d)
                scale = 1d;

            var previewWidth = Math.Max(1, (int)Math.Round(targetWidth * scale));
            var previewHeight = Math.Max(1, (int)Math.Round(targetHeight * scale));

            using var previewBitmap = new SKBitmap(previewWidth, previewHeight, sourceBitmap.ColorType, sourceBitmap.AlphaType);
            using (var canvas = new SKCanvas(previewBitmap))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(sourceBitmap, srcRect, new SKRect(0, 0, previewWidth, previewHeight),
                    new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true });
            }

            using var image = SKImage.FromBitmap(previewBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 95)
                ?? throw new InvalidOperationException("编码预览图失败");

            return data.ToArray();
        }

        private static SKRectI CalculateCropRect(
            int sourceWidth,
            int sourceHeight,
            int targetWidth,
            int targetHeight,
            double centerX,
            double centerY,
            double zoom)
        {
            var targetRatio = targetWidth / (double)targetHeight;
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

            var cropWidth = baseCropWidth / zoom;
            var cropHeight = baseCropHeight / zoom;

            var centerPx = centerX * sourceWidth;
            var centerPy = centerY * sourceHeight;

            var left = centerPx - cropWidth / 2d;
            var top = centerPy - cropHeight / 2d;

            left = Clamp(left, 0d, sourceWidth - cropWidth);
            top = Clamp(top, 0d, sourceHeight - cropHeight);

            var right = left + cropWidth;
            var bottom = top + cropHeight;

            var rect = new SKRectI(
                left: Math.Max(0, (int)Math.Round(left)),
                top: Math.Max(0, (int)Math.Round(top)),
                right: Math.Min(sourceWidth, (int)Math.Round(right)),
                bottom: Math.Min(sourceHeight, (int)Math.Round(bottom)));

            if (rect.Width <= 1 || rect.Height <= 1)
            {
                rect = new SKRectI(0, 0, sourceWidth, sourceHeight);
            }

            return rect;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
