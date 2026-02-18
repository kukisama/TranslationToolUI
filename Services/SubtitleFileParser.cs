using System;
using System.Collections.Generic;
using System.IO;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services
{
    public static class SubtitleFileParser
    {
        public static List<SubtitleCue> ParseSubtitleFileToCues(string path)
        {
            if (!File.Exists(path))
            {
                return new List<SubtitleCue>();
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var lines = File.ReadAllLines(path);
            var expectsHeader = extension == ".vtt";
            return ParseSubtitleLinesToList(lines, expectsHeader);
        }

        public static List<SubtitleCue> ParseSubtitleLinesToList(string[] lines, bool expectsHeader)
        {
            var list = new List<SubtitleCue>();
            var index = 0;
            if (expectsHeader && index < lines.Length && lines[index].StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            {
                index++;
            }

            while (index < lines.Length)
            {
                var line = lines[index].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    index++;
                    continue;
                }

                if (int.TryParse(line, out _))
                {
                    index++;
                    if (index >= lines.Length)
                    {
                        break;
                    }
                    line = lines[index].Trim();
                }

                if (!TryParseTimeRange(line, out var start, out var end))
                {
                    index++;
                    continue;
                }

                index++;
                var textLines = new List<string>();
                while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
                {
                    textLines.Add(lines[index].Trim());
                    index++;
                }

                var text = string.Join(" ", textLines).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    list.Add(new SubtitleCue
                    {
                        Start = start,
                        End = end,
                        Text = text
                    });
                }
            }

            return list;
        }

        public static bool TryParseTimeRange(string line, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            var match = System.Text.RegularExpressions.Regex.Match(line, @"(?<start>\d{2}:\d{2}:\d{2}[\.,]\d{3})\s*-->\s*(?<end>\d{2}:\d{2}:\d{2}[\.,]\d{3})");
            if (!match.Success)
            {
                return false;
            }

            if (!TryParseTimecode(match.Groups["start"].Value, out start))
            {
                return false;
            }

            if (!TryParseTimecode(match.Groups["end"].Value, out end))
            {
                return false;
            }

            return true;
        }

        public static bool TryParseTimecode(string value, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            var normalized = value.Replace(',', '.');
            if (TimeSpan.TryParseExact(normalized, @"hh\:mm\:ss\.fff", null, out time))
            {
                return true;
            }

            return false;
        }
    }
}
