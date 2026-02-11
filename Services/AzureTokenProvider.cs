using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace TranslationToolUI.Services
{
    /// <summary>
    /// Azure Entra ID (AAD) Token 提供者。
    /// 仅在用户手动点击"登录"时触发认证，不自动弹窗。
    /// </summary>
    public class AzureTokenProvider
    {
        private TokenCredential? _credential;
        private readonly string _scope = "https://cognitiveservices.azure.com/.default";
        private AccessToken _cachedToken;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private static string AuthRecordPath =>
            Path.Combine(PathManager.Instance.AppDataPath, "azure_auth_record.json");

        /// <summary>
        /// 当前是否已登录（持有有效 credential）
        /// </summary>
        public bool IsLoggedIn => _credential != null;

        /// <summary>
        /// 已登录的用户名（如果有）
        /// </summary>
        public string? Username { get; private set; }

        /// <summary>
        /// Token 过期时间
        /// </summary>
        public DateTimeOffset? TokenExpiry => _cachedToken.ExpiresOn == default ? null : _cachedToken.ExpiresOn;

        /// <summary>
        /// 使用设备代码流登录。onDeviceCode 回调用于在 UI 上显示设备码。
        /// </summary>
        public async Task<bool> LoginAsync(
            string? tenantId,
            string? clientId,
            Action<string>? onDeviceCode = null,
            CancellationToken ct = default)
        {
            try
            {
                // 尝试从已保存的 AuthenticationRecord 静默恢复
                AuthenticationRecord? record = null;
                if (File.Exists(AuthRecordPath))
                {
                    try
                    {
                        using var stream = File.OpenRead(AuthRecordPath);
                        record = await AuthenticationRecord.DeserializeAsync(stream, ct);
                    }
                    catch
                    {
                        // 文件损坏，忽略
                        File.Delete(AuthRecordPath);
                    }
                }

                var options = new DeviceCodeCredentialOptions
                {
                    TokenCachePersistenceOptions = new TokenCachePersistenceOptions
                    {
                        Name = "TranslationToolUI"
                    },
                    DeviceCodeCallback = (code, cancellation) =>
                    {
                        onDeviceCode?.Invoke(code.Message);
                        return Task.CompletedTask;
                    }
                };

                if (!string.IsNullOrWhiteSpace(tenantId))
                    options.TenantId = tenantId;

                if (!string.IsNullOrWhiteSpace(clientId))
                    options.ClientId = clientId;

                if (record != null)
                    options.AuthenticationRecord = record;

                var credential = new DeviceCodeCredential(options);

                // 获取 Token（会触发设备代码流或从缓存恢复）
                _cachedToken = await credential.GetTokenAsync(
                    new TokenRequestContext(new[] { _scope }), ct);

                _credential = credential;

                // 保存 AuthenticationRecord 便于后续静默恢复
                try
                {
                    var newRecord = await credential.AuthenticateAsync(
                        new TokenRequestContext(new[] { _scope }), ct);
                    using var stream = File.Create(AuthRecordPath);
                    await newRecord.SerializeAsync(stream, ct);
                    Username = newRecord.Username;
                }
                catch
                {
                    // 保存失败不影响登录结果
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AAD 登录失败: {ex.Message}");
                _credential = null;
                return false;
            }
        }

        /// <summary>
        /// 尝试使用已保存的凭据静默获取 Token（不弹窗）
        /// </summary>
        public async Task<bool> TrySilentLoginAsync(
            string? tenantId,
            string? clientId,
            CancellationToken ct = default)
        {
            if (!File.Exists(AuthRecordPath))
                return false;

            try
            {
                using var stream = File.OpenRead(AuthRecordPath);
                var record = await AuthenticationRecord.DeserializeAsync(stream, ct);

                var options = new DeviceCodeCredentialOptions
                {
                    AuthenticationRecord = record,
                    TokenCachePersistenceOptions = new TokenCachePersistenceOptions
                    {
                        Name = "TranslationToolUI"
                    },
                    // 静默模式下不希望弹窗，但 DeviceCodeCredential 需要回调
                    DeviceCodeCallback = (_, _) => Task.CompletedTask
                };

                if (!string.IsNullOrWhiteSpace(tenantId))
                    options.TenantId = tenantId;
                if (!string.IsNullOrWhiteSpace(clientId))
                    options.ClientId = clientId;

                var credential = new DeviceCodeCredential(options);

                // 尝试静默获取
                _cachedToken = await credential.GetTokenAsync(
                    new TokenRequestContext(new[] { _scope }), ct);

                _credential = credential;
                Username = record.Username;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取当前有效的 Bearer Token
        /// </summary>
        public async Task<string> GetTokenAsync(CancellationToken ct = default)
        {
            if (_credential == null)
                throw new InvalidOperationException("未登录，请先调用 LoginAsync");

            // 若 Token 还未过期（提前 2 分钟刷新），直接返回
            if (_cachedToken.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
                return _cachedToken.Token;

            await _lock.WaitAsync(ct);
            try
            {
                // 双重检查
                if (_cachedToken.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
                    return _cachedToken.Token;

                _cachedToken = await _credential.GetTokenAsync(
                    new TokenRequestContext(new[] { _scope }), ct);

                return _cachedToken.Token;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 注销，清除缓存的凭据
        /// </summary>
        public void Logout()
        {
            try
            {
                if (File.Exists(AuthRecordPath))
                    File.Delete(AuthRecordPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"删除 AuthRecord 失败: {ex.Message}");
            }

            _credential = null;
            _cachedToken = default;
            Username = null;
        }
    }
}
