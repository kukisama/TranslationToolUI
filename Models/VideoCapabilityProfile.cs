using System;
using System.Collections.Generic;

namespace TrueFluentPro.Models
{
    /// <summary>
    /// 单个视频能力配置档（按 API 模式 + 模型匹配）。
    /// 所有视频分辨率/比例/时长/数量能力均由此处定义，避免业务层写死映射。
    /// </summary>
    public sealed class VideoCapabilityProfile
    {
        public string Name { get; init; } = string.Empty;

        public VideoApiMode ApiMode { get; init; }

        /// <summary>
        /// 模型匹配规则。为空表示该模式下的通用兜底档。
        /// </summary>
        public Func<string, bool>? ModelMatcher { get; init; }

        public List<string> AspectRatioOptions { get; init; } = new();

        public List<string> ResolutionOptions { get; init; } = new();

        public List<int> DurationOptions { get; init; } = new();

        public List<int> CountOptions { get; init; } = new();

        /// <summary>
        /// key: "{aspect}|{resolution}", value: (width,height)
        /// </summary>
        public Dictionary<string, (int Width, int Height)> SizeMatrix { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TryResolveSize(string aspectRatio, string resolution, out int width, out int height)
        {
            if (SizeMatrix.TryGetValue(BuildMatrixKey(aspectRatio, resolution), out var size))
            {
                width = size.Width;
                height = size.Height;
                return true;
            }

            width = 0;
            height = 0;
            return false;
        }

        public static string BuildMatrixKey(string aspectRatio, string resolution)
            => $"{aspectRatio?.Trim()}|{resolution?.Trim()}";
    }
}
