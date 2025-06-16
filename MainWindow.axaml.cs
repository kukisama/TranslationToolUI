using Avalonia.Controls;
using TranslationToolUI.ViewModels;
using System;
using Avalonia.Interactivity;

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
}

