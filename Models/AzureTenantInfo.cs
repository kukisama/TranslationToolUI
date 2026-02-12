namespace TranslationToolUI.Models
{
    /// <summary>
    /// AAD / Entra 租户信息（用于 UI 展示与选择）。
    /// </summary>
    public class AzureTenantInfo
    {
        public string TenantId { get; set; } = "";

        /// <summary>
        /// 租户显示名（可能为空）。
        /// </summary>
        public string DisplayName { get; set; } = "";

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(DisplayName)
                ? TenantId
                : $"{DisplayName} ({TenantId})";
        }
    }
}
