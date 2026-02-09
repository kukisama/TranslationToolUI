using Avalonia.Controls;

namespace TranslationToolUI.Views;

public partial class ReviewModeView : UserControl
{
    public ReviewModeView()
    {
        InitializeComponent();
    }

    private void SubtitleCueListBox_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is not TranslationToolUI.ViewModels.MainWindowViewModel vm)
            return;
        if (sender is ListBox listBox && listBox.SelectedItem is TranslationToolUI.Models.SubtitleCue cue)
            vm.PlayFromSubtitleCue(cue);
    }
}
