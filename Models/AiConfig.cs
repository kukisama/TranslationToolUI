using System.Text.Json.Serialization;

namespace TranslationToolUI.Models
{
    public enum AiProviderType
    {
        OpenAiCompatible,
        AzureOpenAi
    }

    public class AiConfig
    {
        public AiProviderType ProviderType { get; set; } = AiProviderType.OpenAiCompatible;

        public string ApiEndpoint { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string ModelName { get; set; } = "gpt-4o-mini";

        public string DeploymentName { get; set; } = "";
        public string ApiVersion { get; set; } = "2024-02-01";

        [JsonIgnore]
        public bool IsValid => !string.IsNullOrWhiteSpace(ApiEndpoint)
                            && !string.IsNullOrWhiteSpace(ApiKey)
                            && (ProviderType == AiProviderType.OpenAiCompatible
                                ? !string.IsNullOrWhiteSpace(ModelName)
                                : !string.IsNullOrWhiteSpace(DeploymentName));
    }
}
