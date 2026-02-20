using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services
{
    /// <summary>
    /// 图片/视频生成服务的共享基类（HttpClient、认证、URL 构建）
    /// </summary>
    public abstract class AiMediaServiceBase
    {
        protected static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        protected AzureTokenProvider? TokenProvider { get; set; }

        public void SetTokenProvider(AzureTokenProvider? provider)
        {
            TokenProvider = provider;
        }

        /// <summary>
        /// 构建 API 基础 URL（去掉末尾的 /v1 等）
        /// </summary>
        protected static string BuildBaseUrl(AiConfig config)
        {
            var baseUrl = config.ApiEndpoint.TrimEnd('/');
            if (baseUrl.EndsWith("/v1"))
                baseUrl = baseUrl[..^3];
            return baseUrl;
        }

        /// <summary>
        /// 设置认证头（支持 OpenAI Compatible、Azure OpenAI api-key 和 AAD Bearer）
        /// </summary>
        protected async Task SetAuthHeadersAsync(HttpRequestMessage request, AiConfig config, CancellationToken ct = default)
        {
            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                if (config.AzureAuthMode == AzureAuthMode.AAD && TokenProvider?.IsLoggedIn == true)
                {
                    var token = await TokenProvider.GetTokenAsync(ct);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                else
                {
                    request.Headers.Add("api-key", config.ApiKey);
                }
            }
            else
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", config.ApiKey);
            }
        }

        /// <summary>
        /// 设置认证头（同步版本，用于不需要 AAD 的场景）
        /// </summary>
        protected static void SetAuthHeaders(HttpRequestMessage request, AiConfig config)
        {
            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                request.Headers.Add("api-key", config.ApiKey);
            }
            else
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", config.ApiKey);
            }
        }

        /// <summary>
        /// 构建 Images API URL（生成）
        /// </summary>
        protected static string BuildImageUrl(AiConfig config)
        {
            var baseUrl = config.ApiEndpoint.TrimEnd('/');

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                // Azure OpenAI: /openai/deployments/{deployment}/images/generations?api-version=...
                return $"{baseUrl}/openai/deployments/{config.DeploymentName}/images/generations?api-version={config.ApiVersion}";
            }

            // OpenAI Compatible
            if (baseUrl.EndsWith("/v1"))
                return $"{baseUrl}/images/generations";
            return $"{baseUrl}/v1/images/generations";
        }

        /// <summary>
        /// 构建 Images Edits API URL（编辑/参考图）
        /// gpt-image-1.5 附加参考图时使用 /images/edits 终结点，且需要 multipart/form-data。
        /// </summary>
        protected static string BuildImageEditUrl(AiConfig config)
        {
            var baseUrl = config.ApiEndpoint.TrimEnd('/');

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                // 旧方式（传统 Azure 部署路径，/images/edits 返回 404）：
                // return $"{baseUrl}/openai/deployments/{config.DeploymentName}/images/edits?api-version={config.ApiVersion}";

                // 新方式：走 OpenAI 兼容路径
                return $"{baseUrl}/openai/v1/images/edits";
            }

            // OpenAI Compatible
            if (baseUrl.EndsWith("/v1"))
                return $"{baseUrl}/images/edits";
            return $"{baseUrl}/v1/images/edits";
        }

        /// <summary>
        /// 构建 Videos API URL（创建）
        /// </summary>
        protected static string BuildVideoCreateUrl(AiConfig config)
        {
            return BuildVideoCreateUrl(config, VideoApiMode.SoraJobs);
        }

        /// <summary>
        /// 构建 Videos API URL（创建）
        /// 
        /// - Azure OpenAI:
        ///   - SoraJobs: /openai/v1/video/generations/jobs?api-version=preview
        ///   - Videos:   /openai/v1/videos(?api-version=preview)
        /// - OpenAI Compatible: /v1/videos
        /// </summary>
        protected static string BuildVideoCreateUrl(AiConfig config, VideoApiMode apiMode)
        {
            var baseUrl = config.ApiEndpoint.TrimEnd('/');

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                if (apiMode == VideoApiMode.Videos)
                {
                    // 注：部分示例不带 api-version，但 Azure 通常要求。这里默认带上，必要时由调用方回退到不带参数。
                    return $"{baseUrl}/openai/v1/videos?api-version=preview";
                }

                return $"{baseUrl}/openai/v1/video/generations/jobs?api-version=preview";
            }

            if (baseUrl.EndsWith("/v1"))
                return $"{baseUrl}/videos";
            return $"{baseUrl}/v1/videos";
        }

        /// <summary>
        /// 构建 Videos API URL（轮询状态）
        /// </summary>
        protected static string BuildVideoPollUrl(AiConfig config, string videoId)
        {
            return BuildVideoPollUrl(config, videoId, VideoApiMode.SoraJobs);
        }

        /// <summary>
        /// 构建 Videos API URL（轮询状态）
        /// </summary>
        protected static string BuildVideoPollUrl(AiConfig config, string videoId, VideoApiMode apiMode)
        {
            var baseUrl = config.ApiEndpoint.TrimEnd('/');

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                if (apiMode == VideoApiMode.Videos)
                {
                    return $"{baseUrl}/openai/v1/videos/{videoId}?api-version=preview";
                }

                return $"{baseUrl}/openai/v1/video/generations/jobs/{videoId}?api-version=preview";
            }

            if (baseUrl.EndsWith("/v1"))
                return $"{baseUrl}/videos/{videoId}";
            return $"{baseUrl}/v1/videos/{videoId}";
        }

        /// <summary>
        /// 构建 Videos API URL（下载内容）
        /// </summary>
        protected static string BuildVideoDownloadUrl(AiConfig config, string videoId)
        {
            return BuildVideoDownloadUrl(config, videoId, VideoApiMode.SoraJobs);
        }

        /// <summary>
        /// 构建 Videos API URL（下载内容）
        /// </summary>
        protected static string BuildVideoDownloadUrl(AiConfig config, string videoId, VideoApiMode apiMode)
        {
            var baseUrl = config.ApiEndpoint.TrimEnd('/');

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                if (apiMode == VideoApiMode.Videos)
                {
                    return $"{baseUrl}/openai/v1/videos/{videoId}/content?api-version=preview";
                }

                return $"{baseUrl}/openai/v1/video/generations/jobs/{videoId}/content?api-version=preview";
            }

            if (baseUrl.EndsWith("/v1"))
                return $"{baseUrl}/videos/{videoId}/content";
            return $"{baseUrl}/v1/videos/{videoId}/content";
        }

        /// <summary>
        /// 构建 Videos API URL（下载内容备用路径）。
        /// 
        /// 备注：部分实现可能使用 /content/video。
        /// </summary>
        protected static string BuildVideoDownloadUrlAlt(AiConfig config, string videoId, VideoApiMode apiMode)
        {
            var baseUrl = config.ApiEndpoint.TrimEnd('/');

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                if (apiMode == VideoApiMode.Videos)
                {
                    return $"{baseUrl}/openai/v1/videos/{videoId}/content/video?api-version=preview";
                }

                return $"{baseUrl}/openai/v1/video/generations/jobs/{videoId}/content?api-version=preview";
            }

            if (baseUrl.EndsWith("/v1"))
                return $"{baseUrl}/videos/{videoId}/content";
            return $"{baseUrl}/v1/videos/{videoId}/content";
        }

        /// <summary>
        /// 构建 Videos API URL（下载内容，使用 generationId）。
        /// 
        /// 说明：Azure 视频任务轮询返回的 generations[].id（gen_...）在部分实现中才是内容下载的标识。
        /// 若 jobs/{taskId}/content 返回 404，可回退尝试此路径。
        /// </summary>
        protected static string BuildVideoGenerationDownloadUrl(AiConfig config, string generationId)
        {
            var baseUrl = config.ApiEndpoint.TrimEnd('/');

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                // 官方示例：/openai/v1/video/generations/{generationId}/content/video?api-version=preview
                return $"{baseUrl}/openai/v1/video/generations/{generationId}/content/video?api-version=preview";
            }

            // OpenAI Compatible：目前项目只实现 /videos/{id}/content；如后续 provider 返回 generationId，可在此扩展。
            if (baseUrl.EndsWith("/v1"))
                return $"{baseUrl}/videos/{generationId}/content";
            return $"{baseUrl}/v1/videos/{generationId}/content";
        }

        /// <summary>
        /// 构建 Videos API URL（下载内容，使用 generationId，非 /video 子路径）。
        /// 
        /// 备注：不同后端/版本可能会把内容端点暴露为 /content 或 /content/video。
        /// </summary>
        protected static string BuildVideoGenerationDownloadUrlAlt(AiConfig config, string generationId)
        {
            var baseUrl = config.ApiEndpoint.TrimEnd('/');

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                return $"{baseUrl}/openai/v1/video/generations/{generationId}/content?api-version=preview";
            }

            if (baseUrl.EndsWith("/v1"))
                return $"{baseUrl}/videos/{generationId}/content";
            return $"{baseUrl}/v1/videos/{generationId}/content";
        }
    }
}
