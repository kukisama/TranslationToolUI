using Avalonia.Controls;
using TranslationToolUI.Services;
using TranslationToolUI.ViewModels;
using System;
using Avalonia.Interactivity;
using TranslationToolUI.Models;

namespace TranslationToolUI;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;    public MainWindow()
    {
        Console.WriteLine("MainWindow constructor called");
        try
        {
            InitializeComponent();
            Console.WriteLine("InitializeComponent completed");

            try
            {
                var icon = AppIconProvider.WindowIcon;
                if (icon != null)
                {
                    Icon = icon;
                }
            }
            catch
            {
                // ignore icon failures
            }
            
            _viewModel = new MainWindowViewModel();
            Console.WriteLine("MainWindowViewModel created");
            
            _viewModel.SetMainWindow(this);
            Console.WriteLine("SetMainWindow called");
            
            DataContext = _viewModel;
            Console.WriteLine("DataContext set");
            
            this.Show();
            Console.WriteLine("Window.Show() called");
        }        catch (Exception ex)
        {
            Console.WriteLine($"Error in MainWindow constructor: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel?.Dispose();
        base.OnClosed(e);
    }

    private void HelpButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            // ContextMenu is hosted in a separate popup tree and does not reliably inherit
            // DataContext. Assign it explicitly so MenuItem Command bindings work.
            button.ContextMenu.DataContext = DataContext;
            button.ContextMenu.Open(button);
        }
    }

    private void SubtitleCueListBox_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }

        if (sender is ListBox listBox && listBox.SelectedItem is SubtitleCue cue)
        {
            _viewModel.PlayFromSubtitleCue(cue);
        }
    }
}

