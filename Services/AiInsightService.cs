using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services
{
    public class AiRequestOutcome
    {
        public bool UsedReasoning { get; set; }
        public bool UsedFallback { get; set; }
    }

    public class AiInsightService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public async Task StreamChatAsync(
            AiConfig config,
            string systemPrompt,
            string userContent,
            Action<string> onChunk,
            CancellationToken cancellationToken,
            AiChatProfile profile = AiChatProfile.Quick,
            bool enableReasoning = false,
            Action<AiRequestOutcome>? onOutcome = null)
        {
            using var response = await SendRequestAsync(
                config,
                systemPrompt,
                userContent,
                profile,
                enableReasoning,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);

                if (enableReasoning
                    && config.ProviderType == AiProviderType.OpenAiCompatible
                    && (int)response.StatusCode is >= 400 and < 500)
                {
                    using var fallbackResponse = await SendRequestAsync(
                        config,
                        systemPrompt,
                        userContent,
                        profile,
                        enableReasoning: false,
                        cancellationToken);

                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        onOutcome?.Invoke(new AiRequestOutcome
                        {
                            UsedReasoning = false,
                            UsedFallback = true
                        });
                        await StreamResponseAsync(fallbackResponse, onChunk, cancellationToken);
                        return;
                    }

                    var fallbackText = await fallbackResponse.Content.ReadAsStringAsync(cancellationToken);
                    throw new HttpRequestException($"Request failed: {(int)fallbackResponse.StatusCode} {fallbackResponse.ReasonPhrase}. {fallbackText}");
                }

                throw new HttpRequestException($"Request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {errorText}");
            }

            onOutcome?.Invoke(new AiRequestOutcome
            {
                UsedReasoning = enableReasoning
                                && profile == AiChatProfile.Summary
                                && config.SummaryEnableReasoning
                                && config.ProviderType == AiProviderType.OpenAiCompatible,
                UsedFallback = false
            });

            await StreamResponseAsync(response, onChunk, cancellationToken);
        }

        private static async Task<HttpResponseMessage> SendRequestAsync(
            AiConfig config,
            string systemPrompt,
            string userContent,
            AiChatProfile profile,
            bool enableReasoning,
            CancellationToken cancellationToken)
        {
            var url = BuildUrl(config, profile);
            var body = BuildRequestBody(config, systemPrompt, userContent, profile, enableReasoning);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                request.Headers.Add("api-key", config.ApiKey);
            }
            else
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", config.ApiKey);
            }

            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }

        private static async Task StreamResponseAsync(
            HttpResponseMessage response,
            Action<string> onChunk,
            CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;
                if (!line.StartsWith("data: ")) continue;

                var data = line.Substring(6);
                if (data == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("choices", out var choices)
                        || choices.GetArrayLength() == 0)
                        continue;

                    var firstChoice = choices[0];
                    if (!firstChoice.TryGetProperty("delta", out var delta))
                        continue;

                    if (delta.TryGetProperty("content", out var contentElem))
                    {
                        var text = contentElem.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            onChunk(text);
                        }
                    }
                }
                catch (JsonException)
                {
                    // skip unparseable lines
                }
            }
        }

        private static string BuildUrl(AiConfig config, AiChatProfile profile)
        {
            var baseUrl = config.ApiEndpoint.TrimEnd('/');

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                var deploymentName = config.GetDeploymentName(profile);
                return $"{baseUrl}/openai/deployments/{deploymentName}/chat/completions?api-version={config.ApiVersion}";
            }

            if (baseUrl.EndsWith("/v1"))
                return $"{baseUrl}/chat/completions";
            return $"{baseUrl}/v1/chat/completions";
        }

        private static object BuildRequestBody(
            AiConfig config, string systemPrompt, string userContent, AiChatProfile profile, bool enableReasoning)
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            };

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                return new { messages, stream = true };
            }

            var body = new Dictionary<string, object>
            {
                ["model"] = config.GetModelName(profile),
                ["messages"] = messages,
                ["stream"] = true
            };

            if (enableReasoning && profile == AiChatProfile.Summary && config.SummaryEnableReasoning)
            {
                body["reasoning"] = new { effort = "medium" };
            }

            return body;
        }
    }
}
