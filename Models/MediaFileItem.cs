namespace TranslationToolUI.Models
{
    public class MediaFileItem
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";

        public override string ToString() => Name;
    }
}
