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
            CancellationToken cancellationToken)
        {
            var url = BuildUrl(config);
            var body = BuildRequestBody(config, systemPrompt, userContent);

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

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

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

        private static string BuildUrl(AiConfig config)
        {
            var baseUrl = config.ApiEndpoint.TrimEnd('/');

            if (config.ProviderType == AiProviderType.AzureOpenAi)
            {
                return $"{baseUrl}/openai/deployments/{config.DeploymentName}/chat/completions?api-version={config.ApiVersion}";
            }

            if (baseUrl.EndsWith("/v1"))
                return $"{baseUrl}/chat/completions";
            return $"{baseUrl}/v1/chat/completions";
        }

        private static object BuildRequestBody(
            AiConfig config, string systemPrompt, string userContent)
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

            return new { model = config.ModelName, messages, stream = true };
        }
    }
}
