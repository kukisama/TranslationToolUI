using Avalonia;
using System;
using TrueFluentPro.Services;

namespace TrueFluentPro;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CrashLogger.Init();
        try
        {
            Environment.ExitCode = 0;
            var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Environment.ExitCode = exitCode;

            if (exitCode != 0)
            {
                CrashLogger.WriteMessage("Avalonia.StartWithClassicDesktopLifetime", $"exitCode={exitCode}");
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write(source: "Program.Main", exception: ex, isTerminating: true);
            Environment.Exit(1);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}


