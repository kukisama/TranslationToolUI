using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services
{
    /// <summary>
    /// 视频生成服务（Sora 异步轮询模式）
    /// </summary>
    public class AiVideoGenService : AiMediaServiceBase
    {
        private string? _lastSuccessfulDownloadUrl;

        /// <summary>
        /// 构造「下载内容」的候选 URL 列表（不发起网络请求）。
        /// 
        /// 用途：当 UI 已经拿到 generationId（如 gen_...）时，可立即把一个可用的下载 URL（通常是第一个候选）写入
        /// <see cref="Models.MediaGenTask.RemoteDownloadUrl"/> 并持久化，从而支持“恢复/断点续传”与更清晰的状态展示。
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<string> BuildDownloadCandidateUrls(
            AiConfig config,
            string videoId,
            string? generationId,
            VideoApiMode apiMode = VideoApiMode.SoraJobs)
        {
            var urlsToTry = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddUrl(string? u)
            {
                if (string.IsNullOrWhiteSpace(u))
                    return;
                if (seen.Add(u))
                    urlsToTry.Add(u);
            }

            // 首选下载路径（注意：不同模式含义不同）
            var primaryUrl = BuildVideoDownloadUrl(config, videoId, apiMode);
            var primaryAltUrl = BuildVideoDownloadUrlAlt(config, videoId, apiMode);

            // Azure /openai/v1/videos 示例可能不接受 api-version=preview（返回 404），对这种情况准备无参数回退。
            var primaryUrlNoApiVersion = (config.ProviderType == AiProviderType.AzureOpenAi && apiMode == VideoApiMode.Videos)
                ? RemovePreviewApiVersion(primaryUrl)
                : null;
            var primaryAltUrlNoApiVersion = (config.ProviderType == AiProviderType.AzureOpenAi && apiMode == VideoApiMode.Videos)
                ? RemovePreviewApiVersion(primaryAltUrl)
                : null;

            string? fallbackUrl = null;
            string? fallbackAltUrl = null;
            if (!string.IsNullOrWhiteSpace(generationId))
            {
                fallbackUrl = BuildVideoGenerationDownloadUrl(config, generationId);
                fallbackAltUrl = BuildVideoGenerationDownloadUrlAlt(config, generationId);
            }

            // 组装候选 URL 列表，顺序与 DownloadVideoAsync 中一致
            if (config.ProviderType == AiProviderType.AzureOpenAi && apiMode == VideoApiMode.SoraJobs)
            {
                // generationId 下载优先
                AddUrl(fallbackUrl);
                AddUrl(fallbackAltUrl);
                // jobs 内容作为兜底
                AddUrl(primaryUrl);
                AddUrl(primaryAltUrl);
            }
            else
            {
                AddUrl(primaryUrl);
                AddUrl(primaryAltUrl);
                AddUrl(primaryUrlNoApiVersion);
                AddUrl(primaryAltUrlNoApiVersion);
                AddUrl(fallbackUrl);
                AddUrl(fallbackAltUrl);
            }

            return urlsToTry;
        }

#if DEBUG
        private static readonly SemaphoreSlim _pollLogLock = new(1, 1);

        private static string GetPollDebugLogPath()
        {
            // %APPDATA%\TranslationToolUI\Logs\video_poll_debug.log
            var dir = PathManager.Instance.LogsPath;
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "video_poll_debug.log");
        }

        private static async Task AppendHttpDebugLogAsync(
            string action,
            string videoId,
            string url,
            HttpResponseMessage response,
            string responseText,
            CancellationToken ct)
        {
            try
            {
                var path = GetPollDebugLogPath();
                var now = DateTimeOffset.Now;

                var sb = new StringBuilder();
                sb.AppendLine(new string('=', 88));
                sb.AppendLine($"[{now:yyyy-MM-dd HH:mm:ss.fff zzz}] {action} videoId={videoId}");
                sb.AppendLine($"URL: {url}");
                sb.AppendLine($"HTTP: {(int)response.StatusCode} {response.ReasonPhrase}");

                // 尽量记录 Content-Type，帮助判断是否为 JSON/二进制
                var ctHeader = response.Content?.Headers?.ContentType?.ToString();
                if (!string.IsNullOrWhiteSpace(ctHeader))
                {
                    sb.AppendLine($"Content-Type: {ctHeader}");
                }

                sb.AppendLine("Body:");
                sb.AppendLine(responseText);

                await _pollLogLock.WaitAsync(ct);
                try
                {
                    await File.AppendAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
                }
                finally
                {
                    _pollLogLock.Release();
                }
            }
            catch
            {
                // Debug 日志失败不影响主流程
            }
        }

        private static Task AppendPollDebugLogAsync(
            string videoId,
            string url,
            HttpResponseMessage response,
            string responseText,
            CancellationToken ct)
            => AppendHttpDebugLogAsync("Poll", videoId, url, response, responseText, ct);

        private static Task AppendDownloadDebugLogAsync(
            string videoId,
            string url,
            HttpResponseMessage response,
            string responseText,
            CancellationToken ct)
            => AppendHttpDebugLogAsync("Download", videoId, url, response, responseText, ct);

        private static Task AppendCreateDebugLogAsync(
            string videoId,
            string url,
            HttpResponseMessage response,
            string responseText,
            CancellationToken ct)
            => AppendHttpDebugLogAsync("Create", videoId, url, response, responseText, ct);
#endif

        private static string RemovePreviewApiVersion(string url)
            => url.Replace("?api-version=preview", "", StringComparison.OrdinalIgnoreCase);

        private static bool IsTerminalSuccessStatus(string status)
        {
            // Azure video job 实测返回: succeeded
            // 兼容其他可能返回: completed / success
            return status is "succeeded" or "completed" or "success";
        }

        private static bool IsTerminalFailureStatus(string status)
        {
            // 兼容常见失败/取消状态
            return status is "failed" or "error" or "cancelled" or "canceled";
        }

        public async Task<(string status, int progress, string? generationId, string? failureReason)> PollStatusDetailsAsync(
            AiConfig config, string videoId, CancellationToken ct, VideoApiMode apiMode)
        {
            var url = BuildVideoPollUrl(config, videoId, apiMode);
            var altUrl = (config.ProviderType == AiProviderType.AzureOpenAi && apiMode == VideoApiMode.Videos)
                ? RemovePreviewApiVersion(url)
                : null;

            async Task<(HttpResponseMessage response, string body, string urlUsed)> SendOnceAsync(string u)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, u);
                await SetAuthHeadersAsync(req, config, ct);
                var resp = await _httpClient.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                return (resp, body, u);
            }

            HttpResponseMessage response;
            string json;
            string urlUsed;

            (response, json, urlUsed) = await SendOnceAsync(url);

            try
            {
                // 某些示例/后端可能不接受 api-version=preview，且会返回 404；对该情况做一次回退。
                if (!response.IsSuccessStatusCode
                    && (int)response.StatusCode == 404
                    && !string.IsNullOrWhiteSpace(altUrl)
                    && !string.Equals(url, altUrl, StringComparison.OrdinalIgnoreCase))
                {
                    response.Dispose();
                    (response, json, urlUsed) = await SendOnceAsync(altUrl);
                }

#if DEBUG
            await AppendPollDebugLogAsync(videoId, urlUsed, response, json, ct);
#endif

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"视频状态查询失败: {(int)response.StatusCode} {response.ReasonPhrase}. {json}");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var statusElem)
                ? statusElem.GetString() ?? "unknown"
                : "unknown";

            status = status.Trim().ToLowerInvariant();

            var progress = 0;
            if (root.TryGetProperty("progress", out var progressElem))
            {
                if (progressElem.ValueKind == JsonValueKind.Number)
                    progress = progressElem.GetInt32();
            }

            string? failureReason = null;
            if (root.TryGetProperty("failure_reason", out var frElem)
                && frElem.ValueKind == JsonValueKind.String)
            {
                var fr = frElem.GetString();
                if (!string.IsNullOrWhiteSpace(fr))
                    failureReason = fr.Trim();
            }

            string? generationId = null;
            // Jobs 模式常见：generations[].id (gen_...)
            if (root.TryGetProperty("generations", out var gensElem) && gensElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in gensElem.EnumerateArray())
                {
                    if (g.ValueKind != JsonValueKind.Object)
                        continue;
                    if (g.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.String)
                    {
                        var id = idElem.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            generationId = id.Trim();
                            break;
                        }
                    }
                }
            }

            // Videos/OpenAI Compatible 可能使用 data[].id
            if (string.IsNullOrWhiteSpace(generationId)
                && root.TryGetProperty("data", out var dataElem)
                && dataElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in dataElem.EnumerateArray())
                {
                    if (g.ValueKind != JsonValueKind.Object)
                        continue;
                    if (g.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.String)
                    {
                        var id = idElem.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            generationId = id.Trim();
                            break;
                        }
                    }
                }
            }

            // 有些实现不会返回 progress，但会返回终态 succeeded/completed。
            if (progress == 0 && IsTerminalSuccessStatus(status))
                progress = 100;

            return (status, progress, generationId, failureReason);
            }
            finally
            {
                response.Dispose();
            }
        }

        /// <summary>
        /// 创建视频生成任务，返回 video_id
        /// </summary>
        public async Task<string> CreateVideoAsync(
            AiConfig config,
            string prompt,
            MediaGenConfig genConfig,
            CancellationToken ct)
        {
            var url = BuildVideoCreateUrl(config, genConfig.VideoApiMode);
            var altUrl = (config.ProviderType == AiProviderType.AzureOpenAi && genConfig.VideoApiMode == VideoApiMode.Videos)
                ? RemovePreviewApiVersion(url)
                : null;

            object bodyObj;
            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                if (genConfig.VideoApiMode == VideoApiMode.Videos)
                {
                    // /openai/v1/videos 示例：size + seconds
                    var size = $"{genConfig.VideoWidth}x{genConfig.VideoHeight}";
                    var dict = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["model"] = genConfig.VideoModel,
                        ["prompt"] = prompt,
                        ["size"] = size,
                        ["seconds"] = genConfig.VideoSeconds.ToString()
                    };

                    // 某些后端可能支持 n_variants；为兼容起见，仅在 >1 时发送
                    if (genConfig.VideoVariants > 1)
                        dict["n_variants"] = genConfig.VideoVariants;

                    bodyObj = dict;
                }
                else
                {
                    bodyObj = new
                    {
                        model = genConfig.VideoModel,
                        prompt = prompt,
                        height = genConfig.VideoHeight,
                        width = genConfig.VideoWidth,
                        n_seconds = genConfig.VideoSeconds,
                        n_variants = genConfig.VideoVariants
                    };
                }
            }
            else
            {
                var size = $"{genConfig.VideoWidth}x{genConfig.VideoHeight}";
                bodyObj = new
                {
                    model = genConfig.VideoModel,
                    prompt = prompt,
                    size = size,
                    seconds = genConfig.VideoSeconds.ToString(),
                    n_variants = genConfig.VideoVariants
                };
            }

            var payload = JsonSerializer.Serialize(bodyObj);

            async Task<(HttpResponseMessage response, string body, string urlUsed)> SendCreateOnceAsync(string u)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, u);
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                await SetAuthHeadersAsync(req, config, ct);
                var resp = await _httpClient.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                return (resp, body, u);
            }

            HttpResponseMessage response;
            string json;
            string urlUsed;

            (response, json, urlUsed) = await SendCreateOnceAsync(url);

            try
            {
                if (!response.IsSuccessStatusCode
                    && (int)response.StatusCode == 404
                    && !string.IsNullOrWhiteSpace(altUrl)
                    && !string.Equals(url, altUrl, StringComparison.OrdinalIgnoreCase))
                {
                    response.Dispose();
                    (response, json, urlUsed) = await SendCreateOnceAsync(altUrl);
                }

#if DEBUG
            await AppendCreateDebugLogAsync("(create)", urlUsed, response, json, ct);
#endif

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"视频创建失败: {(int)response.StatusCode} {response.ReasonPhrase}. {json}");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idElem))
            {
                return idElem.GetString() ?? throw new InvalidOperationException("视频 ID 为空");
            }

            throw new InvalidOperationException($"无法解析视频 ID，响应: {json}");
            }
            finally
            {
                response.Dispose();
            }
        }

        /// <summary>
        /// 轮询视频状态
        /// </summary>
        public async Task<(string status, int progress, string? failureReason)> PollStatusAsync(
            AiConfig config, string videoId, CancellationToken ct, VideoApiMode apiMode = VideoApiMode.SoraJobs)
        {
            var (status, progress, _, failureReason) = await PollStatusDetailsAsync(config, videoId, ct, apiMode);
            return (status, progress, failureReason);
        }

        /// <summary>
        /// 下载视频到本地文件
        /// </summary>
        public async Task<string?> DownloadVideoAsync(
            AiConfig config,
            string videoId,
            string localPath,
            CancellationToken ct,
            string? generationId = null,
            VideoApiMode apiMode = VideoApiMode.SoraJobs)
        {
            // Azure 实测：轮询是 jobs/{taskId}，但下载 content 可能需要使用 generations[].id（gen_...）。
            // 因此下载采用“多 URL 尝试 + 对 404 做短暂重试”的策略。

            string? resolvedGenId = generationId;

            // 首选下载路径（注意：不同模式含义不同）
            var primaryUrl = BuildVideoDownloadUrl(config, videoId, apiMode);
            var primaryAltUrl = BuildVideoDownloadUrlAlt(config, videoId, apiMode);

            // Azure /openai/v1/videos 示例可能不接受 api-version=preview（返回 404），对这种情况准备无参数回退。
            var primaryUrlNoApiVersion = (config.ProviderType == AiProviderType.AzureOpenAi && apiMode == VideoApiMode.Videos)
                ? RemovePreviewApiVersion(primaryUrl)
                : null;
            var primaryAltUrlNoApiVersion = (config.ProviderType == AiProviderType.AzureOpenAi && apiMode == VideoApiMode.Videos)
                ? RemovePreviewApiVersion(primaryAltUrl)
                : null;

            // 如果未提供 generationId，先尝试从轮询响应解析出来。
            // 备注：即使 status 不是终态，部分后端也可能已经返回 generations[].id。
            if (config.ProviderType == AiProviderType.AzureOpenAi
                && apiMode == VideoApiMode.SoraJobs
                && string.IsNullOrWhiteSpace(resolvedGenId))
            {
                try
                {
                    var (st, _, genId, _) = await PollStatusDetailsAsync(config, videoId, ct, apiMode);
                    if (!string.IsNullOrWhiteSpace(genId))
                        resolvedGenId = genId;
                }
                catch
                {
                    // 解析失败不阻断后续下载尝试
                }
            }

            string? fallbackUrl = null;
            string? fallbackAltUrl = null;
            if (!string.IsNullOrWhiteSpace(resolvedGenId))
            {
                fallbackUrl = BuildVideoGenerationDownloadUrl(config, resolvedGenId);
                fallbackAltUrl = BuildVideoGenerationDownloadUrlAlt(config, resolvedGenId);
            }

            async Task<bool> TryDownloadOnceAsync(string url)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                await SetAuthHeadersAsync(request, config, ct);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync(ct);

#if DEBUG
                    await AppendDownloadDebugLogAsync(videoId, url, response, errorText, ct);
#endif

                    if ((int)response.StatusCode == 404)
                        return false;

                    throw new HttpRequestException(
                        $"视频下载失败: {(int)response.StatusCode} {response.ReasonPhrase}. {errorText}");
                }

                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = File.Create(localPath);
                await stream.CopyToAsync(fileStream, ct);
                _lastSuccessfulDownloadUrl = url;
                return true;
            }

            _lastSuccessfulDownloadUrl = null;

            // 组装待尝试 URL 列表：
            // - Azure + SoraJobs：优先尝试 generations/{genId}/content/video（更可靠）；jobs/{taskId}/content 作为兜底。
            // - 其他：按 primary → alt → 无 api-version 回退的顺序。
            var urlsToTry = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddUrl(string? u)
            {
                if (string.IsNullOrWhiteSpace(u))
                    return;
                if (seen.Add(u))
                    urlsToTry.Add(u);
            }

            if (config.ProviderType == AiProviderType.AzureOpenAi && apiMode == VideoApiMode.SoraJobs)
            {
                // generationId 下载优先
                AddUrl(fallbackUrl);
                AddUrl(fallbackAltUrl);
                // jobs 内容作为兜底（部分环境会一直 404）
                AddUrl(primaryUrl);
                AddUrl(primaryAltUrl);
            }
            else
            {
                AddUrl(primaryUrl);
                AddUrl(primaryAltUrl);
                AddUrl(primaryUrlNoApiVersion);
                AddUrl(primaryAltUrlNoApiVersion);
                AddUrl(fallbackUrl);
                AddUrl(fallbackAltUrl);
            }

            // 对每个 URL 做少量重试（content 可能延迟可用）
            foreach (var url in urlsToTry)
            {
                for (var i = 0; i < 3; i++)
                {
                    if (await TryDownloadOnceAsync(url))
                        return _lastSuccessfulDownloadUrl;
                    await Task.Delay(2000, ct);
                }
            }

            throw new HttpRequestException(
                "视频下载失败: 404 Resource Not Found（已尝试 jobs/{taskId}/content，并回退尝试 generations/{genId}/content/video 与 generations/{genId}/content）");
        }

        /// <summary>
        /// 完整流程：创建 → 轮询 → 下载
        /// </summary>
        public async Task<(string filePath, double generateSeconds, double downloadSeconds, string? downloadUrl)> GenerateVideoAsync(
            AiConfig config,
            string prompt,
            MediaGenConfig genConfig,
            string outputPath,
            CancellationToken ct,
            Action<int>? onProgress = null,
            Action<string>? onVideoIdCreated = null,
            Action<string>? onStatusChanged = null,
            Action<string>? onGenerationIdResolved = null,
            Action<double>? onSucceeded = null)
        {
            var totalSw = System.Diagnostics.Stopwatch.StartNew();

            var videoId = await CreateVideoAsync(config, prompt, genConfig, ct);
            onVideoIdCreated?.Invoke(videoId);
            onProgress?.Invoke(0);

            var retryCount = 0;
            const int maxRetries = 3;

            string? generationId = null;
            string? failureReason = null;
            string lastStatus = "";
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var (status, progress, genId, fr) = await PollStatusDetailsAsync(
                        config, videoId, ct, genConfig.VideoApiMode);
                    onProgress?.Invoke(progress);
                    retryCount = 0; // 成功后重置重试计数

                    if (!string.IsNullOrWhiteSpace(genId) && generationId == null)
                    {
                        generationId = genId;
                        onGenerationIdResolved?.Invoke(genId);
                    }
                    else if (!string.IsNullOrWhiteSpace(genId))
                    {
                        generationId = genId;
                    }
                    if (!string.IsNullOrWhiteSpace(fr))
                        failureReason = fr;

                    // 状态变化时通知 UI
                    if (!string.IsNullOrWhiteSpace(status) && status != lastStatus)
                    {
                        lastStatus = status;
                        onStatusChanged?.Invoke(status);
                    }

                    if (IsTerminalSuccessStatus(status))
                        break;

                    if (IsTerminalFailureStatus(status))
                    {
                        var detail = string.IsNullOrWhiteSpace(failureReason)
                            ? status
                            : $"{status} ({failureReason})";
                        throw new InvalidOperationException($"视频生成失败: {detail}");
                    }

                    await Task.Delay(genConfig.VideoPollIntervalMs, ct);
                }
                catch (HttpRequestException) when (retryCount < maxRetries)
                {
                    retryCount++;
                    await Task.Delay(genConfig.VideoPollIntervalMs, ct);
                }
            }

            ct.ThrowIfCancellationRequested();

            var generateSeconds = totalSw.Elapsed.TotalSeconds;

            // 通知 UI：succeeded，开始下载
            onSucceeded?.Invoke(generateSeconds);

            var downloadSw = System.Diagnostics.Stopwatch.StartNew();
            var downloadUrl = await DownloadVideoAsync(config, videoId, outputPath, ct, generationId, genConfig.VideoApiMode);
            downloadSw.Stop();
            onProgress?.Invoke(100);

            return (outputPath, generateSeconds, downloadSw.Elapsed.TotalSeconds, downloadUrl);
        }
    }
}
