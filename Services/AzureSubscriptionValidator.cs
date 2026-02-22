using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TrueFluentPro.Models;

namespace TrueFluentPro.Services
{
    public sealed class AzureSubscriptionValidator
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public async Task<(bool IsValid, string Message)> ValidateAsync(AzureSubscription subscription, CancellationToken cancellationToken)
        {
            if (subscription == null)
            {
                return (false, "订阅为空");
            }

            if (string.IsNullOrWhiteSpace(subscription.SubscriptionKey) || subscription.SubscriptionKey.Length < 32)
            {
                return (false, "订阅密钥为空或长度不正确");
            }

            var region = subscription.GetEffectiveRegion();
            if (string.IsNullOrWhiteSpace(region))
            {
                return (false, "无法从终结点解析服务区域");
            }

            var tokenEndpoint = subscription.GetTokenEndpoint();

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = new StringContent("")
            };

            request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", subscription.SubscriptionKey);

            try
            {
                using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var token = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        return (true, "订阅验证成功");
                    }

                    return (false, "订阅验证失败：未获取到 token");
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return (false, $"订阅验证失败：鉴权失败 ({(int)response.StatusCode})");
                }

                return (false, $"订阅验证失败：HTTP {(int)response.StatusCode}");
            }
            catch (OperationCanceledException)
            {
                return (false, "订阅验证已取消");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"订阅验证失败：网络错误（{ex.Message}）");
            }
            catch (Exception ex)
            {
                return (false, $"订阅验证失败：{ex.Message}");
            }
        }
    }
}
