using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private static string FormatResponseHeaders(HttpResponseMessage response)
        {
            var sb = new StringBuilder();

            foreach (var h in response.Headers)
            {
                sb.Append(h.Key);
                sb.Append('=').AppendLine(string.Join(",", h.Value));
            }

            if (response.Content != null)
            {
                foreach (var h in response.Content.Headers)
                {
                    sb.Append(h.Key);
                    sb.Append('=').AppendLine(string.Join(",", h.Value));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 生成图片，返回 base64 解码后的字节数组列表及耗时信息。
        /// onProgress: 0-49 = 等待服务端生成, 50-95 = 下载响应体, 100 = 完成
        /// </summary>
        public async Task<ImageGenerationResult> GenerateImagesAsync(
            AiConfig config,
            string prompt,
            MediaGenConfig genConfig,
            CancellationToken ct,
            IReadOnlyList<string>? referenceImagePaths = null,
            Action<int>? onProgress = null)
        {
            onProgress?.Invoke(0);
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();

            HttpResponseMessage? response = null;

            var validReferenceImages = (referenceImagePaths ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .ToList();

            await AppLogService.Instance.LogHttpDebugAsync(
                "image",
                "ImageRequest.Start",
                $"Mode={(validReferenceImages.Count == 0 ? "Generate" : "Edit")}\n" +
                $"ProviderType={config.ProviderType}\n" +
                $"ApiEndpoint={config.ApiEndpoint}\n" +
                $"DeploymentName={config.DeploymentName}\n" +
                $"ApiVersion={config.ApiVersion}\n" +
                $"ImageModel={genConfig.ImageModel}\n" +
                $"ImageSize={genConfig.ImageSize}\n" +
                $"ImageQuality={genConfig.ImageQuality}\n" +
                $"ImageFormat={genConfig.ImageFormat}\n" +
                $"ImageCount={genConfig.ImageCount}\n" +
                $"ReferenceImageCount={validReferenceImages.Count}\n" +
                $"ReferenceImages={string.Join(";", validReferenceImages)}",
                ct);

            if (validReferenceImages.Count > 0)
            {
                // ── 有参考图：优先尝试 /images/edits + multipart/form-data（Azure OpenAI 官方方式） ──
                var editUrl = BuildImageEditUrl(config);

                // 1) 尝试 /images/edits（multipart/form-data）
                //    严格按照官方 curl：只发 image + prompt（+ model/size/quality 可选）
                //    注意：output_format / output_compression 是 /generations 专有参数，edits 不支持
                using (var formContent = new MultipartFormDataContent())
                {
                    var imageNames = new List<string>();
                    foreach (var imagePath in validReferenceImages)
                    {
                        var imageBytes = await File.ReadAllBytesAsync(imagePath, ct);
                        var ext = Path.GetExtension(imagePath).ToLowerInvariant();
                        var mimeType = ext switch
                        {
                            ".jpg" or ".jpeg" => "image/jpeg",
                            ".webp" => "image/webp",
                            ".gif" => "image/gif",
                            ".bmp" => "image/bmp",
                            _ => "image/png"
                        };
                        var fileName = Path.GetFileName(imagePath);
                        imageNames.Add(fileName);

                        var imageContent = new ByteArrayContent(imageBytes);
                        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
                        formContent.Add(imageContent, "image", fileName);
                    }

                    formContent.Add(new StringContent(prompt), "prompt");
                    formContent.Add(new StringContent(genConfig.ImageModel), "model");

                    using var editRequest = new HttpRequestMessage(HttpMethod.Post, editUrl);
                    editRequest.Content = formContent;
                    await SetAuthHeadersAsync(editRequest, config, ct);
                    editRequest.Headers.Accept.ParseAdd("application/json");
                    editRequest.Headers.ExpectContinue = false;

                    System.Diagnostics.Debug.WriteLine($"[ImageEdit] POST {editUrl}");
                    System.Diagnostics.Debug.WriteLine($"[ImageEdit] images={validReferenceImages.Count}, model={genConfig.ImageModel}, size={genConfig.ImageSize}, quality={genConfig.ImageQuality}");

                    var authModeText = config.ProviderType == AiProviderType.AzureOpenAi
                        ? $"Azure/{config.AzureAuthMode}"
                        : "OpenAICompatible/Bearer";
                    await AppLogService.Instance.LogHttpDebugAsync(
                        "image",
                        "ImageEdit.Request",
                        $"URL={editUrl}\n" +
                        $"AuthMode={authModeText}\n" +
                        $"FormFields=prompt,model,image\n" +
                        $"ImageCount={validReferenceImages.Count}\n" +
                        $"ImageFiles={string.Join(",", imageNames)}",
                        ct);

                    response = await _httpClient.SendAsync(editRequest, HttpCompletionOption.ResponseHeadersRead, ct);

                    System.Diagnostics.Debug.WriteLine($"[ImageEdit] Response: {(int)response.StatusCode} {response.ReasonPhrase}");

                    await AppLogService.Instance.LogHttpDebugAsync(
                        "image",
                        "ImageEdit.Response",
                        $"URL={editUrl}\n" +
                        $"HTTP={(int)response.StatusCode} {response.ReasonPhrase}\n" +
                        $"Headers:\n{FormatResponseHeaders(response)}",
                        ct);
                }

                // 不回退，直接暴露错误
                if (response == null)
                {
                    throw new HttpRequestException("图片编辑失败: 未发送请求");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync(ct);
                    var sc = response.StatusCode; var rp = response.ReasonPhrase;
                    var oneapiRequestId = response.Headers.TryGetValues("X-Oneapi-Request-Id", out var ridVals)
                        ? string.Join(",", ridVals)
                        : string.Empty;
                    await AppLogService.Instance.LogHttpDebugAsync(
                        "image",
                        "ImageEdit.Error",
                        $"HTTP={(int)sc} {rp}\n" +
                        $"URL={editUrl}\n" +
                        $"Headers:\n{FormatResponseHeaders(response)}\n" +
                        $"Body={errorText}",
                        ct);
                    response.Dispose();
                    throw new HttpRequestException(
                        $"图片编辑失败: {(int)sc} {rp}. " +
                        (string.IsNullOrWhiteSpace(oneapiRequestId)
                            ? string.Empty
                            : $"[X-Oneapi-Request-Id: {oneapiRequestId}] ") +
                        $"{errorText}");
                }
            }
            else
            {
                // ── 无参考图：使用 /images/generations 终结点 + JSON ──
                var url = BuildImageUrl(config);

                var bodyObj = new Dictionary<string, object>
                {
                    ["prompt"] = prompt,
                    ["model"] = genConfig.ImageModel,
                    ["n"] = genConfig.ImageCount,
                    ["size"] = genConfig.ImageSize,
                    ["quality"] = genConfig.ImageQuality,
                    ["output_format"] = genConfig.ImageFormat
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(bodyObj),
                    Encoding.UTF8,
                    "application/json");

                await SetAuthHeadersAsync(request, config, ct);
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.ExpectContinue = false;

                var authModeText = config.ProviderType == AiProviderType.AzureOpenAi
                    ? $"Azure/{config.AzureAuthMode}"
                    : "OpenAICompatible/Bearer";
                await AppLogService.Instance.LogHttpDebugAsync(
                    "image",
                    "ImageGenerate.Request",
                    $"URL={url}\n" +
                    $"AuthMode={authModeText}\n" +
                    $"Json={JsonSerializer.Serialize(bodyObj)}",
                    ct);

                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                await AppLogService.Instance.LogHttpDebugAsync(
                    "image",
                    "ImageGenerate.Response",
                    $"URL={url}\n" +
                    $"HTTP={(int)response.StatusCode} {response.ReasonPhrase}\n" +
                    $"Headers:\n{FormatResponseHeaders(response)}",
                    ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync(ct);
                    var statusCode = response.StatusCode;
                    var reasonPhrase = response.ReasonPhrase;
                    var oneapiRequestId = response.Headers.TryGetValues("X-Oneapi-Request-Id", out var ridVals)
                        ? string.Join(",", ridVals)
                        : string.Empty;
                    await AppLogService.Instance.LogHttpDebugAsync(
                        "image",
                        "ImageGenerate.Error",
                        $"HTTP={(int)statusCode} {reasonPhrase}\n" +
                        $"URL={url}\n" +
                        $"Headers:\n{FormatResponseHeaders(response)}\n" +
                        $"Body={errorText}",
                        ct);
                    response.Dispose();
                    throw new HttpRequestException(
                        $"图片生成失败: {(int)statusCode} {reasonPhrase}. " +
                        (string.IsNullOrWhiteSpace(oneapiRequestId)
                            ? string.Empty
                            : $"[X-Oneapi-Request-Id: {oneapiRequestId}] ") +
                        $"{errorText}");
                }
            }
            using (response)
            {
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
            IReadOnlyList<string>? referenceImagePaths = null,
            Action<int>? onProgress = null)
        {
            var genResult = await GenerateImagesAsync(config, prompt, genConfig, ct, referenceImagePaths, onProgress);
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

        private static string? BuildImageDataUrl(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                return null;

            var bytes = File.ReadAllBytes(imagePath);
            var ext = Path.GetExtension(imagePath).ToLowerInvariant();
            var mime = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                _ => "image/png"
            };
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }

    }
}
