using Avalonia.Controls;
using Avalonia.Interactivity;
using TranslationToolUI.Models;
using TranslationToolUI.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace TranslationToolUI.Views
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
            AddPresetButton.Click += AddPresetButton_Click;
            AddReviewSheetButton.Click += AddReviewSheetButton_Click;
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
            PresetButtonsItemsControl.ItemsSource = _presetButtons;

            _reviewSheets = new ObservableCollection<ReviewSheetPreset>(Config.ReviewSheets
                .Select(s => new ReviewSheetPreset
                {
                    Name = s.Name,
                    FileTag = s.FileTag,
                    Prompt = s.Prompt,
                    IncludeInBatch = s.IncludeInBatch
                }));
            ReviewSheetsItemsControl.ItemsSource = _reviewSheets;
        }

        private void UpdateProviderFieldsVisibility()
        {
            var isAzure = ProviderTypeComboBox.SelectedIndex == 1;
            AzureFieldsPanel.IsVisible = isAzure;
            ModelNamePanel.IsVisible = !isAzure;
        }

        private void AddPresetButton_Click(object? sender, RoutedEventArgs e)
        {
            _presetButtons.Add(new InsightPresetButton { Name = "新按钮", Prompt = "" });
        }

        private void RemovePresetButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is InsightPresetButton item)
            {
                _presetButtons.Remove(item);
            }
        }

        private void AddReviewSheetButton_Click(object? sender, RoutedEventArgs e)
        {
            _reviewSheets.Add(new ReviewSheetPreset
            {
                Name = "新复盘",
                FileTag = "custom",
                Prompt = "",
                IncludeInBatch = true
            });
        }

        private void RemoveReviewSheet_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ReviewSheetPreset item)
            {
                _reviewSheets.Remove(item);
            }
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            Config.ProviderType = ProviderTypeComboBox.SelectedIndex == 1
                ? AiProviderType.AzureOpenAi
                : AiProviderType.OpenAiCompatible;
            Config.ApiEndpoint = ApiEndpointTextBox.Text?.Trim() ?? "";
            Config.ApiKey = ApiKeyTextBox.Text?.Trim() ?? "";
            var quickModel = QuickModelNameTextBox.Text?.Trim() ?? "";
            var summaryModel = SummaryModelNameTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(quickModel))
            {
                quickModel = summaryModel;
            }
            if (string.IsNullOrWhiteSpace(summaryModel))
            {
                summaryModel = quickModel;
            }
            if (string.IsNullOrWhiteSpace(quickModel))
            {
                quickModel = Config.ModelName;
            }
            if (string.IsNullOrWhiteSpace(summaryModel))
            {
                summaryModel = quickModel;
            }

            Config.QuickModelName = quickModel;
            Config.SummaryModelName = summaryModel;
            Config.ModelName = quickModel;

            var quickDeployment = QuickDeploymentNameTextBox.Text?.Trim() ?? "";
            var summaryDeployment = SummaryDeploymentNameTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(quickDeployment))
            {
                quickDeployment = summaryDeployment;
            }
            if (string.IsNullOrWhiteSpace(summaryDeployment))
            {
                summaryDeployment = quickDeployment;
            }
            if (string.IsNullOrWhiteSpace(quickDeployment))
            {
                quickDeployment = Config.DeploymentName;
            }
            if (string.IsNullOrWhiteSpace(summaryDeployment))
            {
                summaryDeployment = quickDeployment;
            }

            Config.QuickDeploymentName = quickDeployment;
            Config.SummaryDeploymentName = summaryDeployment;
            Config.DeploymentName = quickDeployment;
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
                ModelName = QuickModelNameTextBox.Text?.Trim() ?? "",
                QuickDeploymentName = QuickDeploymentNameTextBox.Text?.Trim() ?? "",
                SummaryDeploymentName = SummaryDeploymentNameTextBox.Text?.Trim() ?? "",
                DeploymentName = QuickDeploymentNameTextBox.Text?.Trim() ?? "",
                ApiVersion = ApiVersionTextBox.Text?.Trim() ?? "2024-02-01"
            };
            if (string.IsNullOrWhiteSpace(testConfig.QuickModelName))
            {
                testConfig.QuickModelName = testConfig.SummaryModelName;
                testConfig.ModelName = testConfig.QuickModelName;
            }
            if (string.IsNullOrWhiteSpace(testConfig.SummaryModelName))
            {
                testConfig.SummaryModelName = testConfig.QuickModelName;
            }
            if (string.IsNullOrWhiteSpace(testConfig.QuickDeploymentName))
            {
                testConfig.QuickDeploymentName = testConfig.SummaryDeploymentName;
                testConfig.DeploymentName = testConfig.QuickDeploymentName;
            }
            if (string.IsNullOrWhiteSpace(testConfig.SummaryDeploymentName))
            {
                testConfig.SummaryDeploymentName = testConfig.QuickDeploymentName;
            }
            testConfig.SummaryEnableReasoning = SummaryReasoningCheckBox.IsChecked == true;

            if (!testConfig.IsValid)
            {
                StatusTextBlock.Text = "请填写必要的配置信息";
                StatusTextBlock.Foreground = Avalonia.Media.Brushes.Orange;
                return;
            }

            TestButton.IsEnabled = false;
            TestButton.Content = "测试中...";
            StatusTextBlock.Text = "正在连接...";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.Gray;
            ReasoningOutputTextBox.Text = "";

            try
            {
                var service = new AiInsightService();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var received = false;
                var reasoningReceived = false;
                var reasoningBuilder = new System.Text.StringBuilder();

                await service.StreamChatAsync(
                    testConfig,
                    "You are a helpful assistant.",
                    "Provide one short answer and think step-by-step.",
                    chunk => { received = true; },
                    cts.Token,
                    AiChatProfile.Summary,
                    enableReasoning: testConfig.SummaryEnableReasoning,
                    onOutcome: null,
                    onReasoningChunk: chunk =>
                    {
                        reasoningReceived = true;
                        reasoningBuilder.Append(chunk);
                    });

                if (reasoningReceived)
                {
                    ReasoningOutputTextBox.Text = reasoningBuilder.ToString();
                }
                else if (testConfig.SummaryEnableReasoning)
                {
                    ReasoningOutputTextBox.Text = "未收到思考内容。";
                }

                if (received)
                {
                    StatusTextBlock.Text = "连接成功！AI 服务可用。";
                    StatusTextBlock.Foreground = Avalonia.Media.Brushes.Green;
                }
                else
                {
                    StatusTextBlock.Text = "连接成功但未收到响应，请检查模型配置。";
                    StatusTextBlock.Foreground = Avalonia.Media.Brushes.Orange;
                }
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "连接超时，请检查 API 端点是否正确。";
                StatusTextBlock.Foreground = Avalonia.Media.Brushes.Red;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"连接失败: {ex.Message}";
                StatusTextBlock.Foreground = Avalonia.Media.Brushes.Red;
            }
            finally
            {
                TestButton.IsEnabled = true;
                TestButton.Content = "测试连接";
            }
        }
    }
}
