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
                    DeploymentName = existingConfig.DeploymentName,
                    ApiVersion = existingConfig.ApiVersion,
                    PresetButtons = existingConfig.PresetButtons
                        .Select(b => new InsightPresetButton { Name = b.Name, Prompt = b.Prompt })
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
        }

        private void LoadConfigValues()
        {
            ProviderTypeComboBox.SelectedIndex =
                Config.ProviderType == AiProviderType.AzureOpenAi ? 1 : 0;
            ApiEndpointTextBox.Text = Config.ApiEndpoint;
            ApiKeyTextBox.Text = Config.ApiKey;
            ModelNameTextBox.Text = Config.ModelName;
            DeploymentNameTextBox.Text = Config.DeploymentName;
            ApiVersionTextBox.Text = string.IsNullOrWhiteSpace(Config.ApiVersion)
                ? "2024-02-01"
                : Config.ApiVersion;
            UpdateProviderFieldsVisibility();

            _presetButtons = new ObservableCollection<InsightPresetButton>(Config.PresetButtons);
            PresetButtonsItemsControl.ItemsSource = _presetButtons;
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

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            Config.ProviderType = ProviderTypeComboBox.SelectedIndex == 1
                ? AiProviderType.AzureOpenAi
                : AiProviderType.OpenAiCompatible;
            Config.ApiEndpoint = ApiEndpointTextBox.Text?.Trim() ?? "";
            Config.ApiKey = ApiKeyTextBox.Text?.Trim() ?? "";
            Config.ModelName = ModelNameTextBox.Text?.Trim() ?? "";
            Config.DeploymentName = DeploymentNameTextBox.Text?.Trim() ?? "";
            Config.ApiVersion = ApiVersionTextBox.Text?.Trim() ?? "2024-02-01";
            Config.PresetButtons = _presetButtons
                .Where(b => !string.IsNullOrWhiteSpace(b.Name))
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
                ModelName = ModelNameTextBox.Text?.Trim() ?? "",
                DeploymentName = DeploymentNameTextBox.Text?.Trim() ?? "",
                ApiVersion = ApiVersionTextBox.Text?.Trim() ?? "2024-02-01"
            };

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

            try
            {
                var service = new AiInsightService();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var received = false;

                await service.StreamChatAsync(
                    testConfig,
                    "You are a helpful assistant.",
                    "Say hi in one word.",
                    chunk => { received = true; },
                    cts.Token);

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
