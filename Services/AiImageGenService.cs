using System;
using System.Collections.Generic;
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
    /// 图片生成结果（含耗时信息）
    /// </summary>
    public class ImageGenerationResult
    {
        public List<byte[]> Images { get; set; } = new();
        /// <summary>服务端生成耗时（秒）：从发送请求到 headers 返回</summary>
        public double GenerateSeconds { get; set; }
        /// <summary>下载传输耗时（秒）：从 headers 返回到 body 读取完毕</summary>
        public double DownloadSeconds { get; set; }
    }

    /// <summary>
    /// 图片生成服务（OpenAI Compatible Images API）
    /// </summary>
    public class AiImageGenService : AiMediaServiceBase
    {
        /// <summary>
        /// 生成图片，返回 base64 解码后的字节数组列表及耗时信息。
        /// onProgress: 0-49 = 等待服务端生成, 50-95 = 下载响应体, 100 = 完成
        /// </summary>
        public async Task<ImageGenerationResult> GenerateImagesAsync(
            AiConfig config,
            string prompt,
            MediaGenConfig genConfig,
            CancellationToken ct,
            Action<int>? onProgress = null)
        {
            onProgress?.Invoke(0);
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();

            var url = BuildImageUrl(config);

            var bodyObj = new Dictionary<string, object>
            {
                ["prompt"] = prompt,
                ["n"] = genConfig.ImageCount,
                ["size"] = genConfig.ImageSize,
                ["quality"] = genConfig.ImageQuality,
                ["output_format"] = genConfig.ImageFormat
            };

            // Azure OpenAI 不需要 model 字段（通过 deployment 指定）
            if (config.ProviderType != AiProviderType.AzureOpenAi)
            {
                bodyObj["model"] = genConfig.ImageModel;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(
                JsonSerializer.Serialize(bodyObj),
                Encoding.UTF8,
                "application/json");

            await SetAuthHeadersAsync(request, config, ct);

            // ResponseHeadersRead: headers 到达即返回控制权，body 可分块读取
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"图片生成失败: {(int)response.StatusCode} {response.ReasonPhrase}. {errorText}");
            }

            // 服务端已返回 header → 生成完毕，进入下载阶段
            var generateSeconds = totalStopwatch.Elapsed.TotalSeconds;
            onProgress?.Invoke(50);

            // 分块读取响应体，追踪下载进度
            var contentLength = response.Content.Headers.ContentLength; // 可能为 null
            using var responseStream = await response.Content.ReadAsStreamAsync(ct);

            using var ms = new MemoryStream();
            var buffer = new byte[81920]; // 80KB 每块
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await responseStream.ReadAsync(buffer, ct)) > 0)
            {
                ms.Write(buffer, 0, bytesRead);
                totalRead += bytesRead;

                if (contentLength is > 0)
                {
                    // 下载进度映射到 50-95
                    var downloadPercent = (int)(totalRead * 45 / contentLength.Value);
                    onProgress?.Invoke(50 + Math.Min(downloadPercent, 45));
                }
            }

            onProgress?.Invoke(96); // 下载完毕，开始解析
            var downloadSeconds = totalStopwatch.Elapsed.TotalSeconds - generateSeconds;

            var json = Encoding.UTF8.GetString(ms.ToArray());
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var results = new List<byte[]>();

            if (root.TryGetProperty("data", out var dataArray))
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    if (item.TryGetProperty("b64_json", out var b64Elem))
                    {
                        var b64 = b64Elem.GetString();
                        if (!string.IsNullOrEmpty(b64))
                        {
                            results.Add(Convert.FromBase64String(b64));
                        }
                    }
                    else if (item.TryGetProperty("url", out var urlElem))
                    {
                        var imageUrl = urlElem.GetString();
                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl, ct);
                            results.Add(imageBytes);
                        }
                    }
                }
            }

            onProgress?.Invoke(100);
            return new ImageGenerationResult
            {
                Images = results,
                GenerateSeconds = generateSeconds,
                DownloadSeconds = downloadSeconds
            };
        }

    /// <summary>
    /// 图片生成+保存结果（含文件路径和耗时）
    /// </summary>
    public class ImageSaveResult
    {
        public List<string> FilePaths { get; set; } = new();
        public double GenerateSeconds { get; set; }
        public double DownloadSeconds { get; set; }
    }

        /// <summary>
        /// 生成图片并保存到指定目录，返回文件路径列表及耗时信息
        /// </summary>
        public async Task<ImageSaveResult> GenerateAndSaveImagesAsync(
            AiConfig config,
            string prompt,
            MediaGenConfig genConfig,
            string outputDirectory,
            CancellationToken ct,
            Action<int>? onProgress = null)
        {
            var genResult = await GenerateImagesAsync(config, prompt, genConfig, ct, onProgress);
            Directory.CreateDirectory(outputDirectory);

            var filePaths = new List<string>();
            var seq = 1;
            foreach (var data in genResult.Images)
            {
                var randomId = Guid.NewGuid().ToString("N")[..8];
                var ext = genConfig.ImageFormat;
                var fileName = $"img_{seq:D3}_{randomId}.{ext}";
                var filePath = Path.Combine(outputDirectory, fileName);

                await File.WriteAllBytesAsync(filePath, data, ct);
                filePaths.Add(filePath);
                seq++;
            }

            return new ImageSaveResult
            {
                FilePaths = filePaths,
                GenerateSeconds = genResult.GenerateSeconds,
                DownloadSeconds = genResult.DownloadSeconds
            };
        }
    }
}
