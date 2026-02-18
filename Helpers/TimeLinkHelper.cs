using System;

namespace TranslationToolUI.Helpers
{
    public static class TimeLinkHelper
    {
        public static string InjectTimeLinks(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return markdown;
            }

            var regex = new System.Text.RegularExpressions.Regex(@"\[(?<time>(\d{1,2}:)?\d{2}:\d{2})\](?!\()",
                System.Text.RegularExpressions.RegexOptions.Compiled);

            return regex.Replace(markdown, match =>
            {
                var timeText = match.Groups["time"].Value;
                return $"[{timeText}](tt://{timeText})";
            });
        }

        public static bool TryParseTimeUrl(string url, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (url.StartsWith("tt://", StringComparison.OrdinalIgnoreCase))
            {
                var text = url.Substring("tt://".Length);
                return TryParseTimestamp(text, out time);
            }

            if (url.StartsWith("time://", StringComparison.OrdinalIgnoreCase))
            {
                var text = url.Substring("time://".Length);
                return TryParseTimestamp(text, out time);
            }

            return false;
        }

        public static bool TryParseTimestamp(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var parts = text.Split(':');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var mm)
                && int.TryParse(parts[1], out var ss))
            {
                time = new TimeSpan(0, mm, ss);
                return true;
            }

            if (parts.Length == 3
                && int.TryParse(parts[0], out var hh)
                && int.TryParse(parts[1], out var mm2)
                && int.TryParse(parts[2], out var ss2))
            {
                time = new TimeSpan(hh, mm2, ss2);
                return true;
            }

            return false;
        }
    }
}
