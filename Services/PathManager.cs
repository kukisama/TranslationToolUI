using System;
using System.IO;

namespace TranslationToolUI.Services
{
    public class PathManager
    {
        private static readonly Lazy<PathManager> _instance = new(() => new PathManager());
        public static PathManager Instance => _instance.Value;
        
        private readonly string _appName = "TranslationToolUI";
        
        #region 🎯 主要路径属性 - 外部调用这些
        
        public string AppDataPath { get; }
        
        public string SessionsPath { get; }
        
        public string ConfigFilePath { get; }
        
        public string LogsPath { get; }
        
        public string CachePath { get; }
        
        #endregion
        
        private PathManager()
        {
            AppDataPath = GetPlatformAppDataPath();
            SessionsPath = Path.Combine(AppDataPath, "Sessions");
            ConfigFilePath = Path.Combine(AppDataPath, "config.json");
            LogsPath = Path.Combine(AppDataPath, "Logs");
            CachePath = Path.Combine(AppDataPath, "Cache");
            
            EnsureDirectoriesExist();
        }
        
        #region 🎯 便利方法 - 用于路径拼接
        
        public string GetConfigFile(string fileName) => Path.Combine(AppDataPath, fileName);
        
        public string GetSessionFile(string fileName) => Path.Combine(SessionsPath, fileName);
        
        public string GetLogFile(string fileName) => Path.Combine(LogsPath, fileName);
        
        public string GetCacheFile(string fileName) => Path.Combine(CachePath, fileName);
        
        #endregion
        
        #region 🎯 平台检测和路径获取
        
        private string GetPlatformAppDataPath()
        {
            if (OperatingSystem.IsAndroid())
            {
                return GetAndroidPath();
            }
            if (OperatingSystem.IsIOS())
            {
                return GetIOSPath();
            }
            
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                return GetDesktopPath();
            }
            
            throw new PlatformNotSupportedException($"不支持的平台: {Environment.OSVersion}");
        }
        
        private string GetDesktopPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                _appName
            );
        }
        
        private string GetAndroidPath()
        {
            throw new NotImplementedException("Android 平台支持待实现");
        }
        
        private string GetIOSPath()
        {
            throw new NotImplementedException("iOS 平台支持待实现");
        }
        
        #endregion
        
        #region 🎯 辅助方法
        
        private void EnsureDirectoriesExist()
        {
            try
            {
                Directory.CreateDirectory(AppDataPath);
                Directory.CreateDirectory(SessionsPath);
                Directory.CreateDirectory(LogsPath);
                Directory.CreateDirectory(CachePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"创建目录失败: {ex.Message}");
            }
        }
        
        public string GetPlatformInfo()
        {
            if (OperatingSystem.IsWindows()) return "Windows";
            if (OperatingSystem.IsMacOS()) return "macOS";
            if (OperatingSystem.IsLinux()) return "Linux";
            if (OperatingSystem.IsAndroid()) return "Android";
            if (OperatingSystem.IsIOS()) return "iOS";
            return "Unknown";
        }
        
        #endregion
    }
}
