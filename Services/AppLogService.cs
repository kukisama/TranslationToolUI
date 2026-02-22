using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TrueFluentPro.Models;

namespace TrueFluentPro.Services
{
    /// <summary>
    /// 统一诊断日志服务。
    /// 所有日志（批处理 / 审计 / HTTP 调试）均由配置中单一的
    /// <see cref="BatchLogLevel"/> 开关控制：
    /// <list type="bullet">
    ///   <item>Off — 不写入任何日志</item>
    ///   <item>FailuresOnly — 仅记录失败事件</item>
    ///   <item>SuccessAndFailure — 记录所有事件（成功 + 失败）</item>
    /// </list>
    /// </summary>
    public sealed class AppLogService
    {
        private static AppLogService? _instance;

        /// <summary>
        /// 单例实例。首次访问前必须调用 <see cref="Initialize"/>。
        /// </summary>
        public static AppLogService Instance =>
            _instance ?? throw new InvalidOperationException(
                "AppLogService not initialized. Call AppLogService.Initialize() first.");

        /// <summary>
        /// 当 Instance 尚未初始化时返回 false，已初始化返回 true。
        /// 供非 ViewModel 代码在不确定初始化时机时安全调用。
        /// </summary>
        public static bool IsInitialized => _instance != null;

        private readonly Func<BatchLogLevel> _getLevel;
        private string? _batchLogPath;
        private readonly object _batchLock = new();
        private readonly SemaphoreSlim _auditLock = new(1, 1);
        private readonly SemaphoreSlim _httpDebugLock = new(1, 1);
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private AppLogService(Func<BatchLogLevel> getLevel)
        {
            _getLevel = getLevel;
        }

        /// <summary>
        /// 初始化单例，传入日志级别委托。
        /// 通常在 MainWindowViewModel 构造函数中调用。
        /// </summary>
        public static void Initialize(Func<BatchLogLevel> getLevel)
        {
            _instance = new AppLogService(getLevel);
        }

        // ─── 级别判断 ────────────────────────────────────────────────────

        /// <summary>当前级别是否应记录成功事件。</summary>
        public bool ShouldLogSuccess =>
            _getLevel() == BatchLogLevel.SuccessAndFailure;

        /// <summary>当前级别是否应记录失败事件。</summary>
        public bool ShouldLogFailure =>
            _getLevel() is BatchLogLevel.FailuresOnly or BatchLogLevel.SuccessAndFailure;

        /// <summary>根据成功/失败标志判断是否应记录。</summary>
        public bool ShouldLog(bool isSuccess) =>
            isSuccess ? ShouldLogSuccess : ShouldLogFailure;

        // ─── Batch Log ─── batch_{timestamp}.log ─────────────────────────

        /// <summary>重置批处理日志文件路径，下次写入时创建新文件。</summary>
        public void ResetBatchFile() => _batchLogPath = null;

        /// <summary>确保批处理日志文件已创建（如果级别非 Off）。</summary>
        public void EnsureBatchFile()
        {
            if (_getLevel() == BatchLogLevel.Off)
            {
                _batchLogPath = null;
                return;
            }

            if (!string.IsNullOrWhiteSpace(_batchLogPath)) return;

            var logsPath = PathManager.Instance.LogsPath;
            Directory.CreateDirectory(logsPath);
            _batchLogPath = Path.Combine(logsPath,
                $"batch_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        }

        /// <summary>
        /// 写入批处理事件。
        /// 格式：timestamp | eventName | fileName | status | message
        /// </summary>
        public void LogBatch(string eventName, string fileName, string status, string message)
        {
            bool isSuccess = string.Equals(status, "Success", StringComparison.Ordinal);
            if (!ShouldLog(isSuccess)) return;

            EnsureBatchFile();
            if (string.IsNullOrWhiteSpace(_batchLogPath)) return;

            var line = $"{Timestamp()} | {eventName} | {fileName} | {status} | {message}";
            lock (_batchLock)
            {
                File.AppendAllText(_batchLogPath, line + Environment.NewLine, Utf8NoBom);
            }
        }

        // ─── Audit Log ─── Audit.log ─────────────────────────────────────

        /// <summary>
        /// 写入诊断/审计事件（同步）。
        /// 格式：timestamp | eventName | message
        /// </summary>
        public void LogAudit(string eventName, string message, bool isSuccess = true)
        {
            if (!ShouldLog(isSuccess)) return;
            WriteAuditLine($"{Timestamp()} | {eventName} | {message}");
        }

        /// <summary>
        /// 写入诊断/审计事件（异步，适用于 View 层代码）。
        /// 格式：[timestamp] [category] [trace=N] eventName | message
        /// </summary>
        public async Task LogAuditAsync(string category, int traceId,
            string eventName, string message, bool isSuccess = true)
        {
            if (!ShouldLog(isSuccess)) return;
            var line = $"[{Timestamp()}] [{category}] [trace={traceId}] {eventName} | {message}";
            await WriteAuditLineAsync(line);
        }

        private void WriteAuditLine(string line)
        {
            var path = GetAuditPath();
            _auditLock.Wait();
            try
            {
                File.AppendAllText(path, line + Environment.NewLine, Utf8NoBom);
            }
            finally
            {
                _auditLock.Release();
            }
        }

        private async Task WriteAuditLineAsync(string line)
        {
            try
            {
                var path = GetAuditPath();
                await _auditLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await File.AppendAllTextAsync(path, line + Environment.NewLine, Utf8NoBom).ConfigureAwait(false);
                }
                finally
                {
                    _auditLock.Release();
                }
            }
            catch
            {
                // 审计日志失败不影响主流程
            }
        }

        // ─── HTTP Debug Log ─── {prefix}_http_debug.log ──────────────────

        /// <summary>
        /// 写入 HTTP 调试详情日志（异步）。
        /// 仅在 <see cref="BatchLogLevel.SuccessAndFailure"/> 级别写入。
        /// 文件名：{filePrefix}_http_debug.log
        /// </summary>
        public async Task LogHttpDebugAsync(string filePrefix, string title,
            string content, CancellationToken ct = default)
        {
            // HTTP 调试属于 "成功" 级别（信息性），仅 All 级别写入
            if (!ShouldLogSuccess) return;

            try
            {
                var logsPath = PathManager.Instance.LogsPath;
                Directory.CreateDirectory(logsPath);
                var path = Path.Combine(logsPath, $"{filePrefix}_http_debug.log");

                var sb = new StringBuilder();
                sb.AppendLine(new string('=', 88));
                sb.AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {title}");
                sb.AppendLine(content);

                await _httpDebugLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await File.AppendAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
                }
                finally
                {
                    _httpDebugLock.Release();
                }
            }
            catch
            {
                // Debug 日志失败不影响主流程
            }
        }

        // ─── Utilities ───────────────────────────────────────────────────

        private static string GetAuditPath()
        {
            var logsPath = PathManager.Instance.LogsPath;
            Directory.CreateDirectory(logsPath);
            return Path.Combine(logsPath, "Audit.log");
        }

        private static string Timestamp() =>
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        /// <summary>格式化异常用于日志，包含 SpeechBatchError 附加数据。</summary>
        public static string FormatException(Exception ex)
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
