using Avalonia.Controls;
using Avalonia.Interactivity;
using TrueFluentPro.Helpers;
using TrueFluentPro.Models;
using TrueFluentPro.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace TrueFluentPro.Views
{
    public partial class AiConfigView : Window
    {
        public AiConfig Config { get; private set; }
        private ObservableCollection<InsightPresetButton> _presetButtons = new();
        private ObservableCollection<ReviewSheetPreset> _reviewSheets = new();

        public AiConfigView()
        {
            InitializeComponent();
            Config = new AiConfig();
            LoadConfigValues();
            SetupEventHandlers();
        }

        public AiConfigView(AiConfig? existingConfig)
        {
            InitializeComponent();
            Config = existingConfig != null
                ? new AiConfig
                {
                    ProviderType = existingConfig.ProviderType,
                    ApiEndpoint = existingConfig.ApiEndpoint,
                    ApiKey = existingConfig.ApiKey,
                    ModelName = existingConfig.ModelName,
                    SummaryModelName = existingConfig.SummaryModelName,
                    QuickModelName = existingConfig.QuickModelName,
                    DeploymentName = existingConfig.DeploymentName,
                    SummaryDeploymentName = existingConfig.SummaryDeploymentName,
                    QuickDeploymentName = existingConfig.QuickDeploymentName,
                    ApiVersion = existingConfig.ApiVersion,
                    SummaryEnableReasoning = existingConfig.SummaryEnableReasoning,
                    PresetButtons = existingConfig.PresetButtons
                        .Select(b => new InsightPresetButton { Name = b.Name, Prompt = b.Prompt })
                        .ToList(),
                    ReviewSheets = existingConfig.ReviewSheets
                        .Select(s => new ReviewSheetPreset
                        {
                            Name = s.Name,
                            FileTag = s.FileTag,
                            Prompt = s.Prompt,
                            IncludeInBatch = s.IncludeInBatch
                        })
                        .ToList()
                }
                : new AiConfig();
            LoadConfigValues();
            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            ProviderTypeComboBox.SelectionChanged += (_, _) => UpdateProviderFieldsVisibility();
            SaveButton.Click += SaveButton_Click;
            CancelButton.Click += (_, _) => Close(false);
            TestButton.Click += TestButton_Click;
        }

        private void LoadConfigValues()
        {
            ProviderTypeComboBox.SelectedIndex =
                Config.ProviderType == AiProviderType.AzureOpenAi ? 1 : 0;
            ApiEndpointTextBox.Text = Config.ApiEndpoint;
            ApiKeyTextBox.Text = Config.ApiKey;
            QuickModelNameTextBox.Text = string.IsNullOrWhiteSpace(Config.QuickModelName)
                ? Config.ModelName
                : Config.QuickModelName;
            SummaryModelNameTextBox.Text = string.IsNullOrWhiteSpace(Config.SummaryModelName)
                ? Config.ModelName
                : Config.SummaryModelName;
            QuickDeploymentNameTextBox.Text = string.IsNullOrWhiteSpace(Config.QuickDeploymentName)
                ? Config.DeploymentName
                : Config.QuickDeploymentName;
            SummaryDeploymentNameTextBox.Text = string.IsNullOrWhiteSpace(Config.SummaryDeploymentName)
                ? Config.DeploymentName
                : Config.SummaryDeploymentName;
            ApiVersionTextBox.Text = string.IsNullOrWhiteSpace(Config.ApiVersion)
                ? "2024-02-01"
                : Config.ApiVersion;
            SummaryReasoningCheckBox.IsChecked = Config.SummaryEnableReasoning;
            UpdateProviderFieldsVisibility();

            _presetButtons = new ObservableCollection<InsightPresetButton>(Config.PresetButtons);
            PresetButtonsEditorControl.Items = _presetButtons;

            _reviewSheets = new ObservableCollection<ReviewSheetPreset>(Config.ReviewSheets
                .Select(s => new ReviewSheetPreset
                {
                    Name = s.Name,
                    FileTag = s.FileTag,
                    Prompt = s.Prompt,
                    IncludeInBatch = s.IncludeInBatch
                }));
            ReviewSheetsEditorControl.Items = _reviewSheets;
        }

        private void UpdateProviderFieldsVisibility()
        {
            var isAzure = ProviderTypeComboBox.SelectedIndex == 1;
            AzureFieldsPanel.IsVisible = isAzure;
            ModelNamePanel.IsVisible = !isAzure;
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            Config.ProviderType = ProviderTypeComboBox.SelectedIndex == 1
                ? AiProviderType.AzureOpenAi
                : AiProviderType.OpenAiCompatible;
            Config.ApiEndpoint = ApiEndpointTextBox.Text?.Trim() ?? "";
            Config.ApiKey = ApiKeyTextBox.Text?.Trim() ?? "";
            var existingModel = Config.ModelName;
            var existingDeployment = Config.DeploymentName;
            Config.QuickModelName = QuickModelNameTextBox.Text?.Trim() ?? "";
            Config.SummaryModelName = SummaryModelNameTextBox.Text?.Trim() ?? "";
            Config.QuickDeploymentName = QuickDeploymentNameTextBox.Text?.Trim() ?? "";
            Config.SummaryDeploymentName = SummaryDeploymentNameTextBox.Text?.Trim() ?? "";
            ConfigViewHelper.ApplyModelDeploymentFallbacks(Config, existingModel, existingDeployment);
            Config.ApiVersion = ApiVersionTextBox.Text?.Trim() ?? "2024-02-01";
            Config.SummaryEnableReasoning = SummaryReasoningCheckBox.IsChecked == true;
            Config.PresetButtons = _presetButtons
                .Where(b => !string.IsNullOrWhiteSpace(b.Name))
                .ToList();
            Config.ReviewSheets = _reviewSheets
                .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .Select(s => new ReviewSheetPreset
                {
                    Name = s.Name.Trim(),
                    FileTag = string.IsNullOrWhiteSpace(s.FileTag) ? "summary" : s.FileTag.Trim(),
                    Prompt = s.Prompt?.Trim() ?? "",
                    IncludeInBatch = s.IncludeInBatch
                })
                .ToList();
            Close(true);
        }

        private async void TestButton_Click(object? sender, RoutedEventArgs e)
        {
            var testConfig = new AiConfig
            {
                ProviderType = ProviderTypeComboBox.SelectedIndex == 1
                    ? AiProviderType.AzureOpenAi
                    : AiProviderType.OpenAiCompatible,
                ApiEndpoint = ApiEndpointTextBox.Text?.Trim() ?? "",
                ApiKey = ApiKeyTextBox.Text?.Trim() ?? "",
                QuickModelName = QuickModelNameTextBox.Text?.Trim() ?? "",
                SummaryModelName = SummaryModelNameTextBox.Text?.Trim() ?? "",
                QuickDeploymentName = QuickDeploymentNameTextBox.Text?.Trim() ?? "",
                SummaryDeploymentName = SummaryDeploymentNameTextBox.Text?.Trim() ?? "",
                ApiVersion = ApiVersionTextBox.Text?.Trim() ?? "2024-02-01",
                SummaryEnableReasoning = SummaryReasoningCheckBox.IsChecked == true
            };
            ConfigViewHelper.ApplyModelDeploymentFallbacks(testConfig);
            await ConfigViewHelper.RunAiConnectionTestAsync(testConfig, StatusTextBlock, ReasoningOutputTextBox, TestButton);
        }
    }
}
