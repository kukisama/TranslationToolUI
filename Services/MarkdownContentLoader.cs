using Avalonia.Platform;
using System;
using System.IO;
using System.Text;

namespace TrueFluentPro.Services;

public static class MarkdownContentLoader
{
    public static string LoadMarkdown(string fileName, string assetPath)
    {
        // 1) Prefer external file next to the executable for easy updates.
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var externalPath = Path.Combine(baseDir, fileName);
            if (File.Exists(externalPath))
            {
                return File.ReadAllText(externalPath, Encoding.UTF8);
            }
        }
        catch
        {
            // ignore and fallback
        }

        // 2) Fallback: embedded Avalonia asset.
        try
        {
            var uri = new Uri($"avares://TrueFluentPro/{assetPath}");
            if (!AssetLoader.Exists(uri))
            {
                return $"（未找到内置资源：{assetPath}）";
            }

            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return $"（加载失败：{ex.Message}）";
        }
    }
}
