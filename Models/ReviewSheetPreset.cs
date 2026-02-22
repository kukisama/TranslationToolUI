namespace TrueFluentPro.Models
{
    public class ReviewSheetPreset
    {
        public string Name { get; set; } = "";
        public string FileTag { get; set; } = "";
        public string Prompt { get; set; } = "";
        public bool IncludeInBatch { get; set; } = true;
    }
}
