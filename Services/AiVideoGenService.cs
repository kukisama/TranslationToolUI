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
        /// <summary>
        /// 创建视频生成任务，返回 video_id
        /// </summary>
        public async Task<string> CreateVideoAsync(
            AiConfig config,
            string prompt,
            MediaGenConfig genConfig,
            CancellationToken ct)
        {
            var url = BuildVideoCreateUrl(config);

            object bodyObj;
            if (config.ProviderType == AiProviderType.AzureOpenAi)
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
            else
            {
                var size = $"{genConfig.VideoWidth}x{genConfig.VideoHeight}";
                bodyObj = new
                {
                    model = genConfig.VideoModel,
                    prompt = prompt,
                    size = size,
                    seconds = genConfig.VideoSeconds,
                    n_variants = genConfig.VideoVariants
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(
                JsonSerializer.Serialize(bodyObj),
                Encoding.UTF8,
                "application/json");

            await SetAuthHeadersAsync(request, config, ct);

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"视频创建失败: {(int)response.StatusCode} {response.ReasonPhrase}. {errorText}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idElem))
            {
                return idElem.GetString() ?? throw new InvalidOperationException("视频 ID 为空");
            }

            throw new InvalidOperationException($"无法解析视频 ID，响应: {json}");
        }

        /// <summary>
        /// 轮询视频状态
        /// </summary>
        public async Task<(string status, int progress)> PollStatusAsync(
            AiConfig config, string videoId, CancellationToken ct)
        {
            var url = BuildVideoPollUrl(config, videoId);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            await SetAuthHeadersAsync(request, config, ct);

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"视频状态查询失败: {(int)response.StatusCode} {response.ReasonPhrase}. {errorText}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var statusElem)
                ? statusElem.GetString() ?? "unknown"
                : "unknown";

            var progress = 0;
            if (root.TryGetProperty("progress", out var progressElem))
            {
                if (progressElem.ValueKind == JsonValueKind.Number)
                    progress = progressElem.GetInt32();
            }

            return (status, progress);
        }

        /// <summary>
        /// 下载视频到本地文件
        /// </summary>
        public async Task DownloadVideoAsync(
            AiConfig config, string videoId, string localPath, CancellationToken ct)
        {
            var url = BuildVideoDownloadUrl(config, videoId);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            await SetAuthHeadersAsync(request, config, ct);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"视频下载失败: {(int)response.StatusCode} {response.ReasonPhrase}. {errorText}");
            }

            var dir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = File.Create(localPath);
            await stream.CopyToAsync(fileStream, ct);
        }

        /// <summary>
        /// 完整流程：创建 → 轮询 → 下载
        /// </summary>
        public async Task<string> GenerateVideoAsync(
            AiConfig config,
            string prompt,
            MediaGenConfig genConfig,
            string outputPath,
            CancellationToken ct,
            Action<int>? onProgress = null,
            Action<string>? onVideoIdCreated = null)
        {
            var videoId = await CreateVideoAsync(config, prompt, genConfig, ct);
            onVideoIdCreated?.Invoke(videoId);
            onProgress?.Invoke(0);

            var retryCount = 0;
            const int maxRetries = 3;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var (status, progress) = await PollStatusAsync(config, videoId, ct);
                    onProgress?.Invoke(progress);
                    retryCount = 0; // 成功后重置重试计数

                    if (status == "completed")
                        break;

                    if (status == "failed")
                        throw new InvalidOperationException("视频生成失败");

                    await Task.Delay(genConfig.VideoPollIntervalMs, ct);
                }
                catch (HttpRequestException) when (retryCount < maxRetries)
                {
                    retryCount++;
                    await Task.Delay(genConfig.VideoPollIntervalMs, ct);
                }
            }

            ct.ThrowIfCancellationRequested();

            await DownloadVideoAsync(config, videoId, outputPath, ct);
            onProgress?.Invoke(100);
            return outputPath;
        }
    }
}
