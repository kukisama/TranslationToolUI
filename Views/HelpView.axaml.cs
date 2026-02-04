using Avalonia.Controls;
using TranslationToolUI.Services;

namespace TranslationToolUI.Views;

public partial class HelpView : Window
{
    public HelpView()
    {
        InitializeComponent();

        try
        {
            var icon = AppIconProvider.WindowIcon;
            if (icon != null)
            {
                Icon = icon;
            }

            HelpMarkdown.Markdown = MarkdownContentLoader.LoadMarkdown(
                fileName: "Help.md",
                assetPath: "Assets/Help.md");
        }
        catch
        {
            // ignore
        }

        CloseButton.Click += (_, _) => Close();
    }
}
