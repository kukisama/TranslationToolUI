using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;

namespace TranslationToolUI;

public partial class App : Application
{
    public override void Initialize()
    {
        Console.WriteLine("App.Initialize() called");
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Console.WriteLine("App.OnFrameworkInitializationCompleted() called");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Console.WriteLine("Creating MainWindow...");
            desktop.MainWindow = new MainWindow();
            Console.WriteLine("MainWindow created and assigned");
        }
        else
        {
            Console.WriteLine("ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime");
        }

        base.OnFrameworkInitializationCompleted();
        Console.WriteLine("Framework initialization completed");
    }
}

