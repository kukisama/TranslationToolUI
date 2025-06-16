using Avalonia;
using System;
using System.Diagnostics;

namespace TranslationToolUI;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("Starting Avalonia application...");
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting application: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.ReadKey();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}


