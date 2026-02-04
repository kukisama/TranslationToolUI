using Avalonia.Controls;
using TranslationToolUI.Services;

namespace TranslationToolUI.Views;

public partial class AboutView : Window
{
    public AboutView()
    {
        InitializeComponent();

        try
        {
            var icon = AppIconProvider.WindowIcon;
            if (icon != null)
            {
                Icon = icon;
            }

            var bitmap = AppIconProvider.LogoBitmap;
            if (bitmap != null)
            {
                LogoImage.Source = bitmap;
            }

            AboutMarkdown.Markdown = MarkdownContentLoader.LoadMarkdown(
                fileName: "About.md",
                assetPath: "Assets/About.md");
        }
        catch
        {
            // ignore icon failures
        }

        CloseButton.Click += (_, _) => Close();
    }
}
