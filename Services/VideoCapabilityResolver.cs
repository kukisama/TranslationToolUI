using System;
using System.Collections.Generic;
using System.Linq;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services
{
    /// <summary>
    /// 视频能力解析器：从统一能力表解析“可选项 + 目标像素尺寸”。
    /// </summary>
    public static class VideoCapabilityResolver
    {
        private static readonly IReadOnlyList<VideoCapabilityProfile> Profiles = BuildProfiles();

        public static VideoCapabilityProfile ResolveProfile(VideoApiMode apiMode, string? model)
        {
            var normalizedModel = (model ?? string.Empty).Trim();

            var matched = Profiles.FirstOrDefault(p =>
                p.ApiMode == apiMode
                && p.ModelMatcher != null
                && p.ModelMatcher(normalizedModel));

            if (matched != null)
                return matched;

            var fallback = Profiles.FirstOrDefault(p => p.ApiMode == apiMode && p.ModelMatcher == null);
            if (fallback != null)
                return fallback;

            return Profiles.First();
        }

        public static bool TryResolveSize(
            VideoApiMode apiMode,
            string? model,
            string aspectRatio,
            string resolution,
            out int width,
            out int height)
        {
            var profile = ResolveProfile(apiMode, model);
            return profile.TryResolveSize(aspectRatio, resolution, out width, out height);
        }

        private static IReadOnlyList<VideoCapabilityProfile> BuildProfiles()
        {
            static Dictionary<string, (int Width, int Height)> BuildMatrix(IEnumerable<(string Aspect, string Resolution, int W, int H)> rows)
            {
                var dict = new Dictionary<string, (int Width, int Height)>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                {
                    dict[VideoCapabilityProfile.BuildMatrixKey(row.Aspect, row.Resolution)] = (row.W, row.H);
                }
                return dict;
            }

            var sora2Videos = new VideoCapabilityProfile
            {
                Name = "sora-2-videos",
                ApiMode = VideoApiMode.Videos,
                ModelMatcher = model => model.Contains("sora-2", StringComparison.OrdinalIgnoreCase),
                AspectRatioOptions = new List<string> { "16:9", "9:16" },
                ResolutionOptions = new List<string> { "720p" },
                DurationOptions = new List<int> { 4, 8, 12 },
                CountOptions = new List<int> { 1 },
                SizeMatrix = BuildMatrix(new[]
                {
                    ("16:9", "720p", 1280, 720),
                    ("9:16", "720p", 720, 1280)
                })
            };

            var videosFallback = new VideoCapabilityProfile
            {
                Name = "videos-fallback",
                ApiMode = VideoApiMode.Videos,
                AspectRatioOptions = new List<string> { "16:9", "9:16" },
                ResolutionOptions = new List<string> { "720p" },
                DurationOptions = new List<int> { 4, 8, 12 },
                CountOptions = new List<int> { 1 },
                SizeMatrix = BuildMatrix(new[]
                {
                    ("16:9", "720p", 1280, 720),
                    ("9:16", "720p", 720, 1280)
                })
            };

            var soraJobsDefault = new VideoCapabilityProfile
            {
                Name = "sora-jobs-default",
                ApiMode = VideoApiMode.SoraJobs,
                AspectRatioOptions = new List<string> { "1:1", "16:9", "9:16" },
                ResolutionOptions = new List<string> { "480p", "720p", "1080p" },
                DurationOptions = new List<int> { 5, 10, 15, 20 },
                CountOptions = new List<int> { 1, 2 },
                SizeMatrix = BuildMatrix(new[]
                {
                    ("1:1", "480p", 480, 480),
                    ("16:9", "480p", 854, 480),
                    ("9:16", "480p", 480, 854),

                    ("1:1", "720p", 720, 720),
                    ("16:9", "720p", 1280, 720),
                    ("9:16", "720p", 720, 1280),

                    ("1:1", "1080p", 1080, 1080),
                    ("16:9", "1080p", 1920, 1080),
                    ("9:16", "1080p", 1080, 1920)
                })
            };

            return new List<VideoCapabilityProfile>
            {
                sora2Videos,
                videosFallback,
                soraJobsDefault
            };
        }
    }
}
