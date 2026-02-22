using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml;
using TrueFluentPro.Models;

namespace TrueFluentPro.Services
{
    public sealed class BatchSubtitleSplitOptions
    {
        public bool EnableSentenceSplit { get; set; }
        public bool SplitOnComma { get; set; }
        public int MaxChars { get; set; }
        public double MaxDurationSeconds { get; set; }
        public int PauseSplitMs { get; set; }
    }

    public static class BatchTranscriptionParser
    {
        public static List<SubtitleCue> ParseBatchTranscriptionToCues(string transcriptionJson, BatchSubtitleSplitOptions splitOptions)
        {
            var list = new List<SubtitleCue>();
            using var doc = JsonDocument.Parse(transcriptionJson);
            if (!doc.RootElement.TryGetProperty("recognizedPhrases", out var phrases) || phrases.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var phrase in phrases.EnumerateArray())
            {
                var text = ExtractPhraseText(phrase);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var speaker = phrase.TryGetProperty("speaker", out var speakerElement)
                    ? speakerElement.ToString()
                    : "";
                var speakerLabel = string.IsNullOrWhiteSpace(speaker) ? "Speaker" : $"Speaker {speaker}";

                if (splitOptions.EnableSentenceSplit && TryGetPhraseWords(phrase, out var words) && words.Count > 0)
                {
                    list.AddRange(SplitPhraseToCues(words, text, speakerLabel, splitOptions));
                    continue;
                }

                if (!TryParseBatchOffsetDuration(phrase, out var start, out var end))
                {
                    continue;
                }

                list.Add(new SubtitleCue
                {
                    Start = start,
                    End = end,
                    Text = $"{speakerLabel}: {text}"
                });
            }

            return list.OrderBy(c => c.Start).ToList();
        }

        private sealed class BatchWordInfo
        {
            public required string Text { get; init; }
            public required TimeSpan Start { get; init; }
            public required TimeSpan End { get; init; }
        }

        private static bool TryGetPhraseWords(JsonElement phrase, out List<BatchWordInfo> words)
        {
            words = new List<BatchWordInfo>();
            if (!phrase.TryGetProperty("nBest", out var nbest) || nbest.ValueKind != JsonValueKind.Array || nbest.GetArrayLength() == 0)
            {
                return false;
            }

            var best = nbest[0];
            if (!best.TryGetProperty("words", out var wordsElement) || wordsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var wordElement in wordsElement.EnumerateArray())
            {
                if (!wordElement.TryGetProperty("word", out var wordTextElement))
                {
                    continue;
                }

                var wordText = wordTextElement.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(wordText))
                {
                    continue;
                }

                if (!TryGetWordTiming(wordElement, out var start, out var end))
                {
                    continue;
                }

                words.Add(new BatchWordInfo
                {
                    Text = wordText,
                    Start = start,
                    End = end
                });
            }

            return words.Count > 0;
        }

        private static bool TryGetWordTiming(JsonElement wordElement, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;
            if (!TryGetTimeValue(wordElement, "offset", out start) && !TryGetTimeValue(wordElement, "offsetInTicks", out start))
            {
                return false;
            }

            if (!TryGetTimeValue(wordElement, "duration", out var duration) && !TryGetTimeValue(wordElement, "durationInTicks", out duration))
            {
                duration = TimeSpan.Zero;
            }

            end = start + duration;
            return true;
        }

        private static List<SubtitleCue> SplitPhraseToCues(
            List<BatchWordInfo> words,
            string displayText,
            string speakerLabel,
            BatchSubtitleSplitOptions splitOptions)
        {
            var cues = new List<SubtitleCue>();
            if (words.Count == 0)
            {
                return cues;
            }

            var breakIndices = GetPunctuationBreakIndices(displayText, words, splitOptions.SplitOnComma);
            var segmentStartIndex = 0;
            var segmentStartCharIndex = 0;
            var segmentCharCount = 0;
            var segmentStartTime = words[0].Start;
            var currentCharIndex = 0;

            for (var i = 0; i < words.Count; i++)
            {
                var word = words[i];
                var wordLength = GetWordLength(word.Text);
                segmentCharCount += wordLength;
                currentCharIndex += wordLength;

                var durationSeconds = (word.End - segmentStartTime).TotalSeconds;
                var nextGapMs = i + 1 < words.Count
                    ? (words[i + 1].Start - word.End).TotalMilliseconds
                    : 0;

                var shouldSplit = false;
                if (breakIndices.Contains(i))
                {
                    shouldSplit = true;
                }
                else if (splitOptions.PauseSplitMs > 0 && nextGapMs >= splitOptions.PauseSplitMs)
                {
                    shouldSplit = true;
                }
                else if (splitOptions.MaxChars > 0 && segmentCharCount >= splitOptions.MaxChars)
                {
                    shouldSplit = true;
                }
                else if (splitOptions.MaxDurationSeconds > 0 && durationSeconds >= splitOptions.MaxDurationSeconds)
                {
                    shouldSplit = true;
                }

                if (!shouldSplit && i < words.Count - 1)
                {
                    continue;
                }

                var segmentEndIndex = i;
                var segmentEndCharIndex = currentCharIndex;
                var segmentText = TrySliceDisplaySegment(displayText, segmentStartCharIndex, segmentEndCharIndex)
                    ?? string.Concat(words.Skip(segmentStartIndex).Take(segmentEndIndex - segmentStartIndex + 1)
                        .Select(w => w.Text));

                segmentText = NormalizeSubtitleText(segmentText);
                if (!string.IsNullOrWhiteSpace(segmentText))
                {
                    cues.Add(new SubtitleCue
                    {
                        Start = segmentStartTime,
                        End = word.End,
                        Text = $"{speakerLabel}: {segmentText}"
                    });
                }

                segmentStartIndex = i + 1;
                segmentStartCharIndex = segmentEndCharIndex;
                segmentCharCount = 0;
                if (segmentStartIndex < words.Count)
                {
                    segmentStartTime = words[segmentStartIndex].Start;
                }
            }

            return cues;
        }

        private static HashSet<int> GetPunctuationBreakIndices(
            string displayText,
            List<BatchWordInfo> words,
            bool splitOnComma)
        {
            var breakIndices = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(displayText) || words.Count == 0)
            {
                return breakIndices;
            }

            var wordEndOffsets = new List<int>(words.Count);
            var running = 0;
            foreach (var word in words)
            {
                running += GetWordLength(word.Text);
                wordEndOffsets.Add(running);
            }

            var charCount = 0;
            foreach (var ch in displayText)
            {
                if (char.IsWhiteSpace(ch))
                {
                    continue;
                }

                if (IsSentenceBreakPunctuation(ch, splitOnComma))
                {
                    if (charCount <= 0)
                    {
                        continue;
                    }

                    var idx = wordEndOffsets.FindIndex(end => end >= charCount);
                    if (idx >= 0)
                    {
                        breakIndices.Add(idx);
                    }
                    continue;
                }

                if (IsSkippableDisplayChar(ch))
                {
                    continue;
                }

                charCount++;
            }

            return breakIndices;
        }

        private static string? TrySliceDisplaySegment(string displayText, int startCharIndex, int endCharIndex)
        {
            if (string.IsNullOrWhiteSpace(displayText))
            {
                return null;
            }

            var charMap = new List<int>();
            for (var i = 0; i < displayText.Length; i++)
            {
                var ch = displayText[i];
                if (char.IsWhiteSpace(ch) || IsSkippableDisplayChar(ch))
                {
                    continue;
                }

                charMap.Add(i);
            }

            if (charMap.Count == 0)
            {
                return displayText.Trim();
            }

            var safeStart = Math.Clamp(startCharIndex, 0, charMap.Count - 1);
            var safeEnd = Math.Clamp(endCharIndex, safeStart + 1, charMap.Count);
            var startIndex = charMap[safeStart];
            var endIndex = charMap[safeEnd - 1];

            while (endIndex + 1 < displayText.Length)
            {
                var ch = displayText[endIndex + 1];
                if (char.IsWhiteSpace(ch) || IsSentenceBreakPunctuation(ch, splitOnComma: true))
                {
                    endIndex++;
                    continue;
                }

                break;
            }

            var segment = displayText.Substring(startIndex, endIndex - startIndex + 1);
            return segment.Trim();
        }

        private static int GetWordLength(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? 0 : text.Replace(" ", "").Length;
        }

        private static bool IsSkippableDisplayChar(char ch)
        {
            return "。！？!?；;，,、：:".IndexOf(ch) >= 0;
        }

        private static bool IsSentenceBreakPunctuation(char ch, bool splitOnComma)
        {
            if ("。！？!?；;".IndexOf(ch) >= 0)
            {
                return true;
            }

            if (splitOnComma && "，,".IndexOf(ch) >= 0)
            {
                return true;
            }

            return false;
        }

        private static string NormalizeSubtitleText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            return System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
        }

        private static string ExtractPhraseText(JsonElement phrase)
        {
            if (phrase.TryGetProperty("nBest", out var nbest) && nbest.ValueKind == JsonValueKind.Array && nbest.GetArrayLength() > 0)
            {
                var first = nbest[0];
                if (first.TryGetProperty("display", out var displayElement))
                {
                    return displayElement.GetString() ?? "";
                }
                if (first.TryGetProperty("lexical", out var lexicalElement))
                {
                    return lexicalElement.GetString() ?? "";
                }
            }

            if (phrase.TryGetProperty("display", out var directDisplay))
            {
                return directDisplay.GetString() ?? "";
            }

            return "";
        }

        private static bool TryParseBatchOffsetDuration(JsonElement phrase, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            if (TryGetTimeValue(phrase, "offsetInTicks", out var offsetTicks) &&
                TryGetTimeValue(phrase, "durationInTicks", out var durationTicks))
            {
                start = offsetTicks;
                end = start + durationTicks;
                return true;
            }

            if (TryGetTimeValue(phrase, "offset", out var offset) &&
                TryGetTimeValue(phrase, "duration", out var duration))
            {
                start = offset;
                end = start + duration;
                return true;
            }

            return false;
        }

        internal static bool TryGetTimeValue(JsonElement element, string propertyName, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (!element.TryGetProperty(propertyName, out var prop))
            {
                return false;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var ticks))
            {
                value = TimeSpan.FromTicks(Math.Max(0, ticks));
                return true;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                var text = prop.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                if (text.StartsWith("PT", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        value = XmlConvert.ToTimeSpan(text);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                if (TimeSpan.TryParse(text, out var parsed))
                {
                    value = parsed;
                    return true;
                }

                if (long.TryParse(text, out var parsedTicks))
                {
                    value = TimeSpan.FromTicks(Math.Max(0, parsedTicks));
                    return true;
                }
            }

            return false;
        }
    }
}
