using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace TranslationToolUI.Controls
{
    public class FilePathToBitmapConverter : IValueConverter
    {
        private static readonly Dictionary<string, WeakReference<Bitmap>> _cache = new();
        private static int _hitCount;
        private static int _missCount;

        private static bool? _auditEnabled;
        private static bool IsAuditEnabled()
        {
            if (_auditEnabled.HasValue) return _auditEnabled.Value;
            try
            {
                var configPath = Services.PathManager.Instance.ConfigFilePath;
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("EnableAuditLog", out var v) && v.GetBoolean())
                    {
                        _auditEnabled = true;
                        return true;
                    }
                }
            }
            catch { }
            _auditEnabled = false;
            return false;
        }

        private static void AuditLog(string message)
        {
            if (!IsAuditEnabled()) return;
            try
            {
                var sessionsPath = Services.PathManager.Instance.SessionsPath;
                var logsRoot = Directory.GetParent(sessionsPath)?.FullName ?? sessionsPath;
                var logsPath = Path.Combine(logsRoot, "Logs");
                Directory.CreateDirectory(logsPath);
                var auditPath = Path.Combine(logsPath, "Audit.log");
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var line = $"{timestamp} | 图片缓存 | {message}";
                File.AppendAllText(auditPath, line + Environment.NewLine, new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string filePath)
                return null;

            if (!File.Exists(filePath))
                return null;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif"))
                return null;

            lock (_cache)
            {
                // 尝试命中缓存
                if (_cache.TryGetValue(filePath, out var weakRef) && weakRef.TryGetTarget(out var cached))
                {
                    _hitCount++;
                    AuditLog($"命中缓存 路径={Path.GetFileName(filePath)} 命中={_hitCount} 未命中={_missCount} 缓存数={_cache.Count}");
                    return cached;
                }

                // 缓存未命中，创建新 Bitmap
                try
                {
                    var bmp = new Bitmap(filePath);
                    _cache[filePath] = new WeakReference<Bitmap>(bmp);
                    _missCount++;
                    AuditLog($"新建加载 路径={Path.GetFileName(filePath)} 命中={_hitCount} 未命中={_missCount} 缓存数={_cache.Count}");
                    return bmp;
                }
                catch
                {
                    return null;
                }
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
