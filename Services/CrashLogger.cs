using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TrueFluentPro.Services;

public static class CrashLogger
{
    private static int _initialized;
    private static string? _logDir;

    public static string? LogDirectory => _logDir;

    public static void Init()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        _logDir = TryCreateLogDir(Path.Combine(AppContext.BaseDirectory, "logs"))
            ?? TryCreateLogDir(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TrueFluentPro",
                "logs"));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Write(
                source: "AppDomain.CurrentDomain.UnhandledException",
                exception: e.ExceptionObject as Exception,
                isTerminating: e.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(
                source: "TaskScheduler.UnobservedTaskException",
                exception: e.Exception,
                isTerminating: false);

            e.SetObserved();
        };

        try
        {
            Trace.AutoFlush = true;

            if (_logDir != null)
            {
                var tracePath = Path.Combine(_logDir, "trace.log");
                Trace.Listeners.Add(new TextWriterTraceListener(tracePath));
            }
        }
        catch
        {
            // ignore
        }
    }

    public static void WriteMessage(string source, string message)
    {
        Write(source, new Exception(message), isTerminating: false);
    }

    public static void HookAvaloniaUiThread()
    {
        try
        {
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Write(
                    source: "Avalonia.Dispatcher.UIThread.UnhandledException",
                    exception: e.Exception,
                    isTerminating: false);

                // Keep default behavior (crash) unless we explicitly decide otherwise.
                e.Handled = false;
            };
        }
        catch
        {
            // ignore
        }
    }

    public static void Write(string source, Exception? exception, bool isTerminating)
    {
        try
        {
            if (_initialized == 0)
            {
                Init();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"IsTerminating: {isTerminating}");
            sb.AppendLine($"Process: {Environment.ProcessPath}");
            sb.AppendLine($"BaseDirectory: {AppContext.BaseDirectory}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"Framework: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            sb.AppendLine();

            if (exception != null)
            {
                sb.AppendLine(exception.ToString());
            }
            else
            {
                sb.AppendLine("(no Exception object)");
            }

            if (_logDir != null)
            {
                var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss.fff");
                var crashPath = Path.Combine(_logDir, $"crash-{stamp}.log");
                File.WriteAllText(crashPath, sb.ToString(), Encoding.UTF8);

                try
                {
                    var lastPath = Path.Combine(_logDir, "last-crash.log");
                    File.Copy(crashPath, lastPath, overwrite: true);
                }
                catch
                {
                    // ignore
                }

                TryWriteToStderr($"[CrashLogger] wrote: {crashPath}");
            }
            else
            {
                TryWriteToStderr("[CrashLogger] log dir unavailable");
            }

            TryWriteToStderr(sb.ToString());
            Debug.WriteLine(sb.ToString());
        }
        catch
        {
            // Absolutely never throw from crash logging.
        }
    }

    private static void TryWriteToStderr(string text)
    {
        try
        {
            Console.Error.WriteLine(text);
        }
        catch
        {
            // ignore
        }
    }

    private static string? TryCreateLogDir(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            // Touch test to ensure it's writable.
            var testFile = Path.Combine(path, ".write-test");
            File.WriteAllText(testFile, DateTimeOffset.Now.ToString("O"), Encoding.UTF8);
            File.Delete(testFile);

            return path;
        }
        catch
        {
            return null;
        }
    }
}
