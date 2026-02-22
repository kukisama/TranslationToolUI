using System;

namespace TrueFluentPro.Models
{
    public class SubtitleCue
    {
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public string Text { get; set; } = "";

        public string RangeText => $"{Start:hh\\:mm\\:ss\\.fff} - {End:hh\\:mm\\:ss\\.fff}";
    }
}
