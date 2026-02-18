using System;
using System.IO;
using System.Text;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services
{
    public class BatchLogService
    {
        private string? _logFilePath;
        private readonly object _logLock = new();
        private readonly Func<BatchLogLevel> _getLogLevel;
        private readonly Func<bool> _getEnableAudit;

        public BatchLogService(Func<BatchLogLevel> getLogLevel, Func<bool> getEnableAudit)
        {
            _getLogLevel = getLogLevel;
            _getEnableAudit = getEnableAudit;
        }

        public bool ShouldWriteBatchLogSuccess => _getLogLevel() == BatchLogLevel.SuccessAndFailure;

        public void ResetLogFile() => _logFilePath = null;

        public bool ShouldWriteBatchLogFailure => _getLogLevel() is BatchLogLevel.FailuresOnly or BatchLogLevel.SuccessAndFailure;

        public void EnsureBatchLogFile()
        {
            if (_getLogLevel() == BatchLogLevel.Off)
            {
                _logFilePath = null;
                return;
            }

            if (!string.IsNullOrWhiteSpace(_logFilePath))
            {
                return;
            }

            var sessionsPath = PathManager.Instance.SessionsPath;
            var logsRoot = Directory.GetParent(sessionsPath)?.FullName ?? sessionsPath;
            var logsPath = Path.Combine(logsRoot, "Logs");
            Directory.CreateDirectory(logsPath);
            var fileName = $"batch_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            _logFilePath = Path.Combine(logsPath, fileName);
        }

        public void AppendBatchLog(string eventName, string fileName, string status, string message)
        {
            if (_getLogLevel() == BatchLogLevel.Off)
            {
                return;
            }

            EnsureBatchLogFile();
            if (string.IsNullOrWhiteSpace(_logFilePath))
            {
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"{timestamp} | {eventName} | {fileName} | {status} | {message}";

            lock (_logLock)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }

        public void AppendBatchDebugLog(string eventName, string message)
        {
            if (!_getEnableAudit())
            {
                return;
            }

            var sessionsPath = PathManager.Instance.SessionsPath;
            var logsRoot = Directory.GetParent(sessionsPath)?.FullName ?? sessionsPath;
            var logsPath = Path.Combine(logsRoot, "Logs");
            Directory.CreateDirectory(logsPath);
            var auditPath = Path.Combine(logsPath, "Audit.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"{timestamp} | {eventName} | {message}";

            lock (_logLock)
            {
                File.AppendAllText(auditPath, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }

        public static string FormatBatchExceptionForLog(Exception ex)
        {
            var sb = new StringBuilder(ex.ToString());
            if (ex.Data.Contains("SpeechBatchError"))
            {
                var detail = ex.Data["SpeechBatchError"]?.ToString();
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    sb.AppendLine();
                    sb.Append("SpeechBatchError: ");
                    sb.Append(detail);
                }
            }

            return sb.ToString();
        }
    }
}
