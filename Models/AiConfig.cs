using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TranslationToolUI.Models
{
    public enum AiProviderType
    {
        OpenAiCompatible,
        AzureOpenAi
    }

    public enum AiChatProfile
    {
        Quick,
        Summary
    }

    public class InsightPresetButton
    {
        public string Name { get; set; } = "";
        public string Prompt { get; set; } = "";
    }

    public class AiConfig
    {
        public AiProviderType ProviderType { get; set; } = AiProviderType.OpenAiCompatible;

        public string ApiEndpoint { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string ModelName { get; set; } = "gpt-4o-mini";
        public string SummaryModelName { get; set; } = "";
        public string QuickModelName { get; set; } = "";

        public string DeploymentName { get; set; } = "";
        public string SummaryDeploymentName { get; set; } = "";
        public string QuickDeploymentName { get; set; } = "";
        public string ApiVersion { get; set; } = "2024-02-01";

        public bool SummaryEnableReasoning { get; set; } = false;

        public List<InsightPresetButton> PresetButtons { get; set; } = new()
        {
            new() { Name = "会议摘要", Prompt = "请对以上翻译记录进行会议摘要。总结会议的主要议题、关键讨论内容和结论。" },
            new() { Name = "知识点提取", Prompt = "请从以上翻译记录中提取核心知识点和专业术语，按主题分类整理。" },
            new() { Name = "客户投诉识别", Prompt = "请识别以上翻译记录中是否存在客户投诉、不满或负面反馈，列出具体内容和建议的应对方式。" },
            new() { Name = "行动项提取", Prompt = "请从以上翻译记录中提取所有行动项(Action Items)，包括待办事项、分工安排、承诺和截止时间。" },
            new() { Name = "情绪分析", Prompt = "请对以上翻译记录进行情绪分析，判断对话中各参与者的整体情绪倾向，标注情绪变化的关键节点。" },
        };

        [JsonIgnore]
        public bool IsValid => !string.IsNullOrWhiteSpace(ApiEndpoint)
                            && !string.IsNullOrWhiteSpace(ApiKey)
                            && (ProviderType == AiProviderType.OpenAiCompatible
                                ? !string.IsNullOrWhiteSpace(ModelName)
                                    || !string.IsNullOrWhiteSpace(SummaryModelName)
                                    || !string.IsNullOrWhiteSpace(QuickModelName)
                                : !string.IsNullOrWhiteSpace(DeploymentName)
                                    || !string.IsNullOrWhiteSpace(SummaryDeploymentName)
                                    || !string.IsNullOrWhiteSpace(QuickDeploymentName));

        public string GetModelName(AiChatProfile profile)
        {
            if (profile == AiChatProfile.Summary)
            {
                if (!string.IsNullOrWhiteSpace(SummaryModelName))
                    return SummaryModelName;
                if (!string.IsNullOrWhiteSpace(QuickModelName))
                    return QuickModelName;
                return ModelName;
            }

            if (!string.IsNullOrWhiteSpace(QuickModelName))
                return QuickModelName;
            if (!string.IsNullOrWhiteSpace(SummaryModelName))
                return SummaryModelName;
            return ModelName;
        }

        public string GetDeploymentName(AiChatProfile profile)
        {
            if (profile == AiChatProfile.Summary)
            {
                if (!string.IsNullOrWhiteSpace(SummaryDeploymentName))
                    return SummaryDeploymentName;
                if (!string.IsNullOrWhiteSpace(QuickDeploymentName))
                    return QuickDeploymentName;
                return DeploymentName;
            }

            if (!string.IsNullOrWhiteSpace(QuickDeploymentName))
                return QuickDeploymentName;
            if (!string.IsNullOrWhiteSpace(SummaryDeploymentName))
                return SummaryDeploymentName;
            return DeploymentName;
        }
    }
}
