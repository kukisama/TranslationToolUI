using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Translation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TranslationToolUI.Models;
using TranslationToolUI.Services;

namespace TranslationToolUI.Views
{
    public partial class ConfigCenterView : Window
    {
        private readonly AzureSpeechConfig _config;
        private readonly ObservableCollection<AzureSubscription> _subscriptions;
        private AzureSubscription? _selectedSubscription;

        private readonly AzureSubscriptionValidator _subscriptionValidator = new();

        private AiConfig _aiConfig;
        private ObservableCollection<InsightPresetButton> _presetButtons = new();
        private ObservableCollection<ReviewSheetPreset> _reviewSheets = new();

        public event EventHandler<AzureSpeechConfig>? ConfigurationUpdated;

        public AzureSpeechConfig Config => _config;

        public AiConfig AiConfig => _aiConfig;

        public ConfigCenterView()
        {
            InitializeComponent();
            _config = new AzureSpeechConfig();
            _subscriptions = new ObservableCollection<AzureSubscription>();
            _aiConfig = BuildAiConfig(null);
            LoadDefaultConfig();
            LoadAiConfigValues();
            SetupEventHandlers();
        }

        public ConfigCenterView(AzureSpeechConfig config)
        {
            InitializeComponent();
            _config = config;
            _subscriptions = new ObservableCollection<AzureSubscription>(_config.Subscriptions);
            _aiConfig = BuildAiConfig(_config.AiConfig);
            LoadConfigValues();
            LoadAiConfigValues();
            SetupEventHandlers();
        }

        private static AiConfig BuildAiConfig(AiConfig? existingConfig)
        {
            return existingConfig != null
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
                    AzureAuthMode = existingConfig.AzureAuthMode,
                    AzureTenantId = existingConfig.AzureTenantId,
                    AzureClientId = existingConfig.AzureClientId,
                    SummaryEnableReasoning = existingConfig.SummaryEnableReasoning,
                    InsightSystemPrompt = existingConfig.InsightSystemPrompt,
                    ReviewSystemPrompt = existingConfig.ReviewSystemPrompt,
                    InsightUserContentTemplate = existingConfig.InsightUserContentTemplate,
                    ReviewUserContentTemplate = existingConfig.ReviewUserContentTemplate,
                    AutoInsightBufferOutput = existingConfig.AutoInsightBufferOutput,
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
        }

        private void SetupEventHandlers()
        {
            SaveButton.Click += SaveButton_Click;
            CancelButton.Click += (_, _) => Close(false);
            BrowseButton.Click += BrowseButton_Click;
            AddButton.Click += AddButton_Click;
            UpdateButton.Click += UpdateButton_Click;
            DeleteButton.Click += DeleteButton_Click;
            SpeechTestButton.Click += SpeechTestButton_Click;
            TestAllSubscriptionsButton.Click += TestAllSubscriptionsButton_Click;
            BatchStorageValidateButton.Click += BatchStorageValidateButton_Click;
            SubscriptionListBox.SelectionChanged += SubscriptionListBox_SelectionChanged;
            SubscriptionListBox.DoubleTapped += SubscriptionListBox_DoubleTapped;
            SubscriptionListBox.PointerPressed += SubscriptionListBox_PointerPressed;
            EndpointTextBox.TextChanged += (_, _) => UpdateEndpointParsedRegionDisplay();

            EnableRecordingCheckBox.IsCheckedChanged += EnableRecordingCheckBox_IsCheckedChanged;
            EnableAutoTimeoutCheckBox.IsCheckedChanged += EnableAutoTimeoutCheckBox_IsCheckedChanged;
            EnableNoResponseRestartCheckBox.IsCheckedChanged += EnableNoResponseRestartCheckBox_IsCheckedChanged;

            ProviderTypeComboBox.SelectionChanged += (_, _) => UpdateProviderFieldsVisibility();
            AzureAuthModeComboBox.SelectionChanged += (_, _) => UpdateAadFieldsVisibility();
            AiTestButton.Click += AiTestButton_Click;
            AddPresetButton.Click += AddPresetButton_Click;
            AddReviewSheetButton.Click += AddReviewSheetButton_Click;

            AadLoginButton.Click += AadLoginButton_Click;
            AadLogoutButton.Click += AadLogoutButton_Click;
            AadCopyCodeButton.Click += AadCopyCodeButton_Click;
            AadOpenLinkButton.Click += AadOpenLinkButton_Click;

            MediaBrowseOutputDirButton.Click += MediaBrowseOutputDirButton_Click;

            MediaImageProviderTypeComboBox.SelectionChanged += (_, _) => UpdateMediaImageProviderFieldsVisibility();
            MediaImageAzureAuthModeComboBox.SelectionChanged += (_, _) => UpdateMediaImageAadFieldsVisibility();
            MediaVideoProviderTypeComboBox.SelectionChanged += (_, _) => UpdateMediaVideoProviderFieldsVisibility();
            MediaVideoAzureAuthModeComboBox.SelectionChanged += (_, _) => UpdateMediaVideoAadFieldsVisibility();
            MediaVideoUseImageEndpointCheckBox.IsCheckedChanged += (_, _) => UpdateMediaVideoEndpointSyncState();
            MediaVideoApiModeComboBox.SelectionChanged += (_, _) => UpdateMediaVideoApiModeVisibility();

            MediaImageAadLoginButton.Click += MediaImageAadLoginButton_Click;
            MediaImageAadLogoutButton.Click += MediaImageAadLogoutButton_Click;
            MediaImageAadCopyCodeButton.Click += MediaImageAadCopyCodeButton_Click;
            MediaImageAadOpenLinkButton.Click += MediaImageAadOpenLinkButton_Click;

            MediaVideoAadLoginButton.Click += MediaVideoAadLoginButton_Click;
            MediaVideoAadLogoutButton.Click += MediaVideoAadLogoutButton_Click;
            MediaVideoAadCopyCodeButton.Click += MediaVideoAadCopyCodeButton_Click;
            MediaVideoAadOpenLinkButton.Click += MediaVideoAadOpenLinkButton_Click;

            Opened += ConfigCenterView_Opened;
        }

        private void LoadDefaultConfig()
        {
            SubscriptionListBox.ItemsSource = _subscriptions;

            EndpointTextBox.Text = "";
            MaxHistoryItemsNumeric.Value = 15;
            RealtimeMaxLengthNumeric.Value = 150;
            ChunkDurationMsNumeric.Value = 200;

            EnableAutoTimeoutCheckBox.IsChecked = true;
            InitialSilenceTimeoutSecondsNumeric.Value = 25;
            EndSilenceTimeoutSecondsNumeric.Value = 1;
            UpdateTimeoutUiEnabledState();

            EnableNoResponseRestartCheckBox.IsChecked = false;
            NoResponseRestartSecondsNumeric.Value = 3;
            AudioActivityThresholdNumeric.Value = 600;
            AudioLevelGainNumeric.Value = 2.0m;
            AutoGainPresetComboBox.SelectedIndex = 0;
            ShowReconnectMarkerCheckBox.IsChecked = true;
            UpdateNoResponseUiEnabledState();

            EnableRecordingCheckBox.IsChecked = true;
            RecordingMp3BitrateNumeric.Value = 256;
            UpdateRecordingUiEnabledState();

            ExportSrtCheckBox.IsChecked = false;
            ExportVttCheckBox.IsChecked = false;

            // 默认字号
            SelectFontSizeComboBox(38);

            SessionDirectoryTextBox.Text = PathManager.Instance.SessionsPath;
            BatchStorageConnectionStringTextBox.Text = "";
            BatchStorageValidateStatusTextBlock.Text = "";
            BatchLogLevelComboBox.SelectedIndex = 0;
            EnableAuditLogCheckBox.IsChecked = false;
            BatchForceRegenerationCheckBox.IsChecked = false;
            ContextMenuForceRegenerationCheckBox.IsChecked = true;
            EnableBatchSentenceSplitCheckBox.IsChecked = true;
            BatchSplitOnCommaCheckBox.IsChecked = false;
            BatchMaxCharsNumeric.Value = 24;
            BatchMaxDurationNumeric.Value = 6;
            BatchPauseSplitNumeric.Value = 500;

            UseSpeechSubtitleForReviewCheckBox.IsChecked = false;
            UseSpeechSubtitleForReviewCheckBox.IsEnabled = false;

            // Media Studio defaults
            LoadMediaGenConfigValues();
        }

        private void LoadConfigValues()
        {
            SubscriptionListBox.ItemsSource = _subscriptions;

            FilterModalParticlesCheckBox.IsChecked = _config.FilterModalParticles;
            MaxHistoryItemsNumeric.Value = _config.MaxHistoryItems;
            RealtimeMaxLengthNumeric.Value = _config.RealtimeMaxLength;
            ChunkDurationMsNumeric.Value = _config.ChunkDurationMs;

            EnableAutoTimeoutCheckBox.IsChecked = _config.EnableAutoTimeout;
            InitialSilenceTimeoutSecondsNumeric.Value = _config.InitialSilenceTimeoutSeconds;
            EndSilenceTimeoutSecondsNumeric.Value = _config.EndSilenceTimeoutSeconds;
            UpdateTimeoutUiEnabledState();

            EnableNoResponseRestartCheckBox.IsChecked = _config.EnableNoResponseRestart;
            NoResponseRestartSecondsNumeric.Value = _config.NoResponseRestartSeconds;
            AudioActivityThresholdNumeric.Value = _config.AudioActivityThreshold;
            AudioLevelGainNumeric.Value = (decimal)_config.AudioLevelGain;
            AutoGainPresetComboBox.SelectedIndex = _config.AutoGainEnabled ? (int)_config.AutoGainPreset : 0;
            ShowReconnectMarkerCheckBox.IsChecked = _config.ShowReconnectMarkerInSubtitle;
            UpdateNoResponseUiEnabledState();

            EnableRecordingCheckBox.IsChecked = _config.EnableRecording;
            RecordingMp3BitrateNumeric.Value = _config.RecordingMp3BitrateKbps;
            UpdateRecordingUiEnabledState();

            ExportSrtCheckBox.IsChecked = _config.ExportSrtSubtitles;
            ExportVttCheckBox.IsChecked = _config.ExportVttSubtitles;

            // 默认字号
            SelectFontSizeComboBox(_config.DefaultFontSize);

            SessionDirectoryTextBox.Text = _config.SessionDirectory;
            BatchStorageConnectionStringTextBox.Text = _config.BatchStorageConnectionString;
            BatchStorageValidateStatusTextBlock.Text = _config.BatchStorageIsValid
                ? "已验证存储账号"
                : "";
            BatchLogLevelComboBox.SelectedIndex = _config.BatchLogLevel switch
            {
                BatchLogLevel.FailuresOnly => 1,
                BatchLogLevel.SuccessAndFailure => 2,
                _ => 0
            };
            EnableAuditLogCheckBox.IsChecked = _config.EnableAuditLog;
            BatchForceRegenerationCheckBox.IsChecked = _config.BatchForceRegeneration;
            ContextMenuForceRegenerationCheckBox.IsChecked = _config.ContextMenuForceRegeneration;
            EnableBatchSentenceSplitCheckBox.IsChecked = _config.EnableBatchSubtitleSentenceSplit;
            BatchSplitOnCommaCheckBox.IsChecked = _config.BatchSubtitleSplitOnComma;
            BatchMaxCharsNumeric.Value = _config.BatchSubtitleMaxChars;
            BatchMaxDurationNumeric.Value = (decimal)_config.BatchSubtitleMaxDurationSeconds;
            BatchPauseSplitNumeric.Value = _config.BatchSubtitlePauseSplitMs;

            UseSpeechSubtitleForReviewCheckBox.IsChecked = _config.UseSpeechSubtitleForReview;
            UseSpeechSubtitleForReviewCheckBox.IsEnabled = _config.BatchStorageIsValid;

            // Media Studio config
            LoadMediaGenConfigValues();
        }

        private void LoadMediaGenConfigValues()
        {
            var mc = _config.MediaGenConfig;
            MediaImageModelTextBox.Text = mc.ImageModel;
            MediaImageProviderTypeComboBox.SelectedIndex = mc.ImageProviderType == AiProviderType.AzureOpenAi ? 1 : 0;
            MediaImageEndpointTextBox.Text = mc.ImageApiEndpoint;
            MediaImageApiKeyTextBox.Text = mc.ImageApiKey;
            MediaImageAzureAuthModeComboBox.SelectedIndex = mc.ImageAzureAuthMode == AzureAuthMode.AAD ? 1 : 0;
            MediaImageAzureTenantIdTextBox.Text = mc.ImageAzureTenantId ?? "";
            MediaImageAzureClientIdTextBox.Text = mc.ImageAzureClientId ?? "";

            MediaVideoUseImageEndpointCheckBox.IsChecked = mc.VideoUseImageEndpoint;
            MediaVideoModelTextBox.Text = mc.VideoModel;
            MediaVideoApiModeComboBox.SelectedIndex = mc.VideoApiMode == VideoApiMode.Videos ? 1 : 0;
            MediaVideoProviderTypeComboBox.SelectedIndex = mc.VideoProviderType == AiProviderType.AzureOpenAi ? 1 : 0;
            MediaVideoEndpointTextBox.Text = mc.VideoApiEndpoint;
            MediaVideoApiKeyTextBox.Text = mc.VideoApiKey;
            MediaVideoAzureAuthModeComboBox.SelectedIndex = mc.VideoAzureAuthMode == AzureAuthMode.AAD ? 1 : 0;
            MediaVideoAzureTenantIdTextBox.Text = mc.VideoAzureTenantId ?? "";
            MediaVideoAzureClientIdTextBox.Text = mc.VideoAzureClientId ?? "";
            MediaOutputDirTextBox.Text = mc.OutputDirectory;

            UpdateMediaImageProviderFieldsVisibility();
            UpdateMediaImageAadFieldsVisibility();
            UpdateMediaVideoProviderFieldsVisibility();
            UpdateMediaVideoAadFieldsVisibility();
            UpdateMediaVideoEndpointSyncState();

            UpdateMediaVideoApiModeVisibility();
        }

        private void LoadAiConfigValues()
        {
            ProviderTypeComboBox.SelectedIndex =
                _aiConfig.ProviderType == AiProviderType.AzureOpenAi ? 1 : 0;
            ApiEndpointTextBox.Text = _aiConfig.ApiEndpoint;
            ApiKeyTextBox.Text = _aiConfig.ApiKey;
            QuickModelNameTextBox.Text = string.IsNullOrWhiteSpace(_aiConfig.QuickModelName)
                ? _aiConfig.ModelName
                : _aiConfig.QuickModelName;
            SummaryModelNameTextBox.Text = string.IsNullOrWhiteSpace(_aiConfig.SummaryModelName)
                ? _aiConfig.ModelName
                : _aiConfig.SummaryModelName;
            QuickDeploymentNameTextBox.Text = string.IsNullOrWhiteSpace(_aiConfig.QuickDeploymentName)
                ? _aiConfig.DeploymentName
                : _aiConfig.QuickDeploymentName;
            SummaryDeploymentNameTextBox.Text = string.IsNullOrWhiteSpace(_aiConfig.SummaryDeploymentName)
                ? _aiConfig.DeploymentName
                : _aiConfig.SummaryDeploymentName;
            ApiVersionTextBox.Text = string.IsNullOrWhiteSpace(_aiConfig.ApiVersion)
                ? "2024-02-01"
                : _aiConfig.ApiVersion;
            SummaryReasoningCheckBox.IsChecked = _aiConfig.SummaryEnableReasoning;
            UpdateProviderFieldsVisibility();

            // AAD auth fields
            AzureAuthModeComboBox.SelectedIndex = _aiConfig.AzureAuthMode == AzureAuthMode.AAD ? 1 : 0;
            AzureTenantIdTextBox.Text = _aiConfig.AzureTenantId ?? "";
            AzureClientIdTextBox.Text = _aiConfig.AzureClientId ?? "";
            UpdateAadFieldsVisibility();

            InsightSystemPromptTextBox.Text = _aiConfig.InsightSystemPrompt;
            ReviewSystemPromptTextBox.Text = _aiConfig.ReviewSystemPrompt;
            InsightUserContentTemplateTextBox.Text = _aiConfig.InsightUserContentTemplate;
            ReviewUserContentTemplateTextBox.Text = _aiConfig.ReviewUserContentTemplate;
            AutoInsightBufferCheckBox.IsChecked = _aiConfig.AutoInsightBufferOutput;

            _presetButtons = new ObservableCollection<InsightPresetButton>(_aiConfig.PresetButtons);
            PresetButtonsItemsControl.ItemsSource = _presetButtons;

            _reviewSheets = new ObservableCollection<ReviewSheetPreset>(_aiConfig.ReviewSheets
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

            // Provider 切换时，也同步刷新 AAD/Key 相关可见性（尤其是 API Key 面板）
            UpdateAadFieldsVisibility();
        }

        private void UpdateAadFieldsVisibility()
        {
            var isAad = AzureAuthModeComboBox.SelectedIndex == 1;
            AadFieldsPanel.IsVisible = isAad;

            // AAD 模式下不需要 API Key；仅在 Key 模式（或非 Azure Provider）显示。
            var providerIsAzure = ProviderTypeComboBox.SelectedIndex == 1;
            ApiKeyPanel.IsVisible = !providerIsAzure || !isAad;
        }

        private void UpdateMediaImageProviderFieldsVisibility()
        {
            var isAzure = MediaImageProviderTypeComboBox.SelectedIndex == 1;
            MediaImageAzureFieldsPanel.IsVisible = isAzure;

            // Provider 切换时也刷新 key 面板可见性
            UpdateMediaImageAadFieldsVisibility();

            // 视频若复用图片终结点，则API模式提示应跟随图片 Provider
            UpdateMediaVideoApiModeVisibility();
        }

        private void UpdateMediaImageAadFieldsVisibility()
        {
            var isAad = MediaImageAzureAuthModeComboBox.SelectedIndex == 1;
            MediaImageAadFieldsPanel.IsVisible = isAad;

            var providerIsAzure = MediaImageProviderTypeComboBox.SelectedIndex == 1;
            MediaImageApiKeyPanel.IsVisible = !providerIsAzure || !isAad;
        }

        private void UpdateMediaVideoProviderFieldsVisibility()
        {
            var isAzure = MediaVideoProviderTypeComboBox.SelectedIndex == 1;
            MediaVideoAzureFieldsPanel.IsVisible = isAzure;

            // Provider 切换时也刷新 key 面板/API模式提示
            UpdateMediaVideoAadFieldsVisibility();
            UpdateMediaVideoApiModeVisibility();
        }

        private void UpdateMediaVideoAadFieldsVisibility()
        {
            var isAad = MediaVideoAzureAuthModeComboBox.SelectedIndex == 1;
            MediaVideoAadFieldsPanel.IsVisible = isAad;

            var providerIsAzure = MediaVideoProviderTypeComboBox.SelectedIndex == 1;
            MediaVideoApiKeyPanel.IsVisible = !providerIsAzure || !isAad;
        }

        private void UpdateMediaVideoEndpointSyncState()
        {
            var shared = MediaVideoUseImageEndpointCheckBox.IsChecked ?? true;
            MediaVideoEndpointPanel.IsVisible = !shared;
            MediaVideoEndpointPanel.IsEnabled = !shared;

            UpdateMediaVideoApiModeVisibility();
        }

        private void UpdateMediaVideoApiModeVisibility()
        {
            // “底层模型 must be sora” 仅对 Azure OpenAI 视频有效。
            // 若视频终结点与图片一致，则以图片 Provider 为准。
            var shared = MediaVideoUseImageEndpointCheckBox.IsChecked ?? true;
            var effectiveProviderIsAzure = shared
                ? (MediaImageProviderTypeComboBox.SelectedIndex == 1)
                : (MediaVideoProviderTypeComboBox.SelectedIndex == 1);

            MediaVideoApiModePanel.IsVisible = effectiveProviderIsAzure;

            var apiMode = MediaVideoApiModeComboBox.SelectedIndex == 1
                ? VideoApiMode.Videos
                : VideoApiMode.SoraJobs;

            if (effectiveProviderIsAzure)
            {
                if (apiMode == VideoApiMode.SoraJobs)
                {
                    MediaVideoApiModeHintTextBlock.Text = "使用 /openai/v1/video/generations/jobs?api-version=preview。轮询返回 job + generations[].id（用于下载）。";
                }
                else
                {
                    MediaVideoApiModeHintTextBlock.Text = "使用 /openai/v1/videos。不同后端返回体可能不同，程序会自动适配并在调试日志中记录原始响应。";
                }
            }
            else
            {
                MediaVideoApiModeHintTextBlock.Text = "";
            }
        }

        private string? _lastDeviceCodeUrl;
        private string? _lastMediaImageDeviceCodeUrl;
        private string? _lastMediaVideoDeviceCodeUrl;

        private static Avalonia.Media.SolidColorBrush BrushFromHex(string hex)
        {
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));
        }

        private static void SetStatus(TextBlock tb, string text, string colorHex)
        {
            tb.Text = text;
            tb.Foreground = BrushFromHex(colorHex);
        }

        private async Task RunAadLoginWithOptionalTenantPickAsync(
            Button loginButton,
            TextBlock statusTextBlock,
            Control deviceCodePanel,
            TextBlock deviceCodeMessage,
            TextBox tenantIdTextBox,
            TextBox clientIdTextBox,
            Action<string?> setLastDeviceCodeUrl,
            string profileKey)
        {
            loginButton.IsEnabled = false;
            SetStatus(statusTextBlock, "正在启动登录…", "#6a737d");
            deviceCodePanel.IsVisible = false;
            setLastDeviceCodeUrl(null);

            var provider = new AzureTokenProvider(profileKey);
            var tenantId = tenantIdTextBox.Text?.Trim();
            var clientId = clientIdTextBox.Text?.Trim();

            Action<string> onDeviceCode = message =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    deviceCodePanel.IsVisible = true;
                    deviceCodeMessage.Text = message;

                    // 尝试从消息中提取 URL
                    var urlMatch = System.Text.RegularExpressions.Regex.Match(message, @"https?://\S+");
                    if (urlMatch.Success)
                        setLastDeviceCodeUrl(urlMatch.Value);
                });
            };

            try
            {
                var success = await provider.LoginAutoAsync(tenantId, clientId, onDeviceCode);
                if (!success)
                {
                    SetStatus(statusTextBlock, "✗ 登录失败", "#cb2431");
                    return;
                }

                SetStatus(statusTextBlock, $"✓ 已登录: {provider.Username ?? "已认证"}", "#22863a");
                deviceCodePanel.IsVisible = false;

                // 若用户未指定 tenantId，则在首次登录后尝试拉取租户列表并让用户选择。
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    SetStatus(statusTextBlock, $"✓ 已登录: {provider.Username ?? "已认证"}（正在获取租户列表…）", "#22863a");
                    IReadOnlyList<AzureTenantInfo> tenants;
                    try
                    {
                        tenants = await provider.GetAvailableTenantsAsync();
                    }
                    catch
                    {
                        tenants = Array.Empty<AzureTenantInfo>();
                    }

                    if (tenants.Count == 1)
                    {
                        tenantIdTextBox.Text = tenants[0].TenantId;
                        SetStatus(statusTextBlock, $"✓ 已登录: {provider.Username ?? "已认证"}（租户: {tenants[0].TenantId}）", "#22863a");
                    }
                    else if (tenants.Count > 1)
                    {
                        var picked = await TenantSelectionView.ShowAsync(this, tenants);
                        if (picked != null && !string.IsNullOrWhiteSpace(picked.TenantId))
                        {
                            tenantIdTextBox.Text = picked.TenantId;

                            // 切换到用户选择的租户：优先尝试静默（利用 AuthRecord），必要时会再次给出设备码。
                            SetStatus(statusTextBlock, $"正在切换到租户: {picked.DisplayName ?? picked.TenantId}…", "#6a737d");
                            var switched = await provider.LoginAutoAsync(picked.TenantId, clientId, onDeviceCode);
                            if (switched)
                            {
                                SetStatus(statusTextBlock, $"✓ 已登录: {provider.Username ?? "已认证"}（租户: {picked.TenantId}）", "#22863a");
                                deviceCodePanel.IsVisible = false;
                            }
                            else
                            {
                                SetStatus(statusTextBlock, "✗ 切换租户失败（请检查权限/管理员同意/条件访问策略）", "#cb2431");
                            }
                        }
                        else
                        {
                            // 用户取消选择，不做强制切换。
                            SetStatus(statusTextBlock, $"✓ 已登录: {provider.Username ?? "已认证"}（未选择租户）", "#22863a");
                        }
                    }
                    else
                    {
                        // 获取不到租户列表时不阻塞使用：用户仍可手动填写 tenantId。
                        SetStatus(statusTextBlock, $"✓ 已登录: {provider.Username ?? "已认证"}（未能获取租户列表，可手动填写租户 ID）", "#22863a");
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus(statusTextBlock, $"✗ 错误: {ex.Message}", "#cb2431");
            }
            finally
            {
                loginButton.IsEnabled = true;
            }
        }

        private async void AadLoginButton_Click(object? sender, RoutedEventArgs e)
        {
            await RunAadLoginWithOptionalTenantPickAsync(
                AadLoginButton,
                AadStatusTextBlock,
                AadDeviceCodePanel,
                AadDeviceCodeMessage,
                AzureTenantIdTextBox,
                AzureClientIdTextBox,
                url => _lastDeviceCodeUrl = url,
                profileKey: "ai");
        }

        private async void MediaImageAadLoginButton_Click(object? sender, RoutedEventArgs e)
        {
            await RunAadLoginWithOptionalTenantPickAsync(
                MediaImageAadLoginButton,
                MediaImageAadStatusTextBlock,
                MediaImageAadDeviceCodePanel,
                MediaImageAadDeviceCodeMessage,
                MediaImageAzureTenantIdTextBox,
                MediaImageAzureClientIdTextBox,
                url => _lastMediaImageDeviceCodeUrl = url,
                profileKey: "media-image");
        }

        private void MediaImageAadLogoutButton_Click(object? sender, RoutedEventArgs e)
        {
            var provider = new AzureTokenProvider("media-image");
            provider.Logout();
            MediaImageAadStatusTextBlock.Text = "已注销";
            MediaImageAadStatusTextBlock.Foreground = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse("#6a737d"));
            MediaImageAadDeviceCodePanel.IsVisible = false;
        }

        private async void MediaImageAadCopyCodeButton_Click(object? sender, RoutedEventArgs e)
        {
            var text = MediaImageAadDeviceCodeMessage.Text ?? "";
            var codeMatch = System.Text.RegularExpressions.Regex.Match(text, @"\b[A-Z0-9]{8,}\b");
            if (codeMatch.Success)
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(codeMatch.Value);
            }
        }

        private void MediaImageAadOpenLinkButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastMediaImageDeviceCodeUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _lastMediaImageDeviceCodeUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private async void MediaVideoAadLoginButton_Click(object? sender, RoutedEventArgs e)
        {
            await RunAadLoginWithOptionalTenantPickAsync(
                MediaVideoAadLoginButton,
                MediaVideoAadStatusTextBlock,
                MediaVideoAadDeviceCodePanel,
                MediaVideoAadDeviceCodeMessage,
                MediaVideoAzureTenantIdTextBox,
                MediaVideoAzureClientIdTextBox,
                url => _lastMediaVideoDeviceCodeUrl = url,
                profileKey: "media-video");
        }

        private void MediaVideoAadLogoutButton_Click(object? sender, RoutedEventArgs e)
        {
            var provider = new AzureTokenProvider("media-video");
            provider.Logout();
            MediaVideoAadStatusTextBlock.Text = "已注销";
            MediaVideoAadStatusTextBlock.Foreground = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse("#6a737d"));
            MediaVideoAadDeviceCodePanel.IsVisible = false;
        }

        private async void MediaVideoAadCopyCodeButton_Click(object? sender, RoutedEventArgs e)
        {
            var text = MediaVideoAadDeviceCodeMessage.Text ?? "";
            var codeMatch = System.Text.RegularExpressions.Regex.Match(text, @"\b[A-Z0-9]{8,}\b");
            if (codeMatch.Success)
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(codeMatch.Value);
            }
        }

        private void MediaVideoAadOpenLinkButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastMediaVideoDeviceCodeUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _lastMediaVideoDeviceCodeUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void AadLogoutButton_Click(object? sender, RoutedEventArgs e)
        {
            var provider = new AzureTokenProvider("ai");
            provider.Logout();
            AadStatusTextBlock.Text = "已注销";
            AadStatusTextBlock.Foreground = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse("#6a737d"));
            AadDeviceCodePanel.IsVisible = false;
        }

        private async void AadCopyCodeButton_Click(object? sender, RoutedEventArgs e)
        {
            var text = AadDeviceCodeMessage.Text ?? "";
            // 提取设备代码（通常是大写字母数字组合）
            var codeMatch = System.Text.RegularExpressions.Regex.Match(text, @"\b[A-Z0-9]{8,}\b");
            if (codeMatch.Success)
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(codeMatch.Value);
            }
        }

        private void AadOpenLinkButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastDeviceCodeUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _lastDeviceCodeUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void EnableRecordingCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            UpdateRecordingUiEnabledState();
        }

        private void EnableAutoTimeoutCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            UpdateTimeoutUiEnabledState();
        }

        private void EnableNoResponseRestartCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            UpdateNoResponseUiEnabledState();
        }


        private void UpdateRecordingUiEnabledState()
        {
            var enabled = EnableRecordingCheckBox.IsChecked ?? true;
            RecordingMp3BitrateNumeric.IsEnabled = enabled;
        }

        private void UpdateTimeoutUiEnabledState()
        {
            var enabled = EnableAutoTimeoutCheckBox.IsChecked ?? true;
            InitialSilenceTimeoutSecondsNumeric.IsEnabled = enabled;
            EndSilenceTimeoutSecondsNumeric.IsEnabled = enabled;
        }

        private void UpdateNoResponseUiEnabledState()
        {
            var enabled = EnableNoResponseRestartCheckBox.IsChecked ?? false;
            NoResponseRestartSecondsNumeric.IsEnabled = enabled;
            AudioActivityThresholdNumeric.IsEnabled = enabled;
            ShowReconnectMarkerCheckBox.IsEnabled = enabled;
        }


        private void ForceUpdateListBoxSelection(int targetIndex)
        {
            if (targetIndex >= 0 && targetIndex < _subscriptions.Count)
            {
                SubscriptionListBox.Focus();
                SubscriptionListBox.SelectedIndex = -1;
                SubscriptionListBox.SelectedIndex = targetIndex;

                if (SubscriptionListBox.SelectedItem != null)
                {
                    SubscriptionListBox.ScrollIntoView(SubscriptionListBox.SelectedItem);
                }
            }
        }

        private void EnsureSelectionWhenSingleItem()
        {
            if (_subscriptions.Count == 1 && SubscriptionListBox.SelectedIndex == -1)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ForceUpdateListBoxSelection(0);
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
            else if (_subscriptions.Count == 1 && SubscriptionListBox.SelectedIndex == 0)
            {
                if (SubscriptionListBox.SelectedItem is AzureSubscription subscription)
                {
                    LoadSubscriptionToEditor(subscription);
                }
            }
        }

        private async void ConfigCenterView_Opened(object? sender, EventArgs e)
        {
            await Task.Delay(200);

            if (_subscriptions.Count == 1)
            {
                ForceUpdateListBoxSelection(0);
            }
            else if (_subscriptions.Count > 1 && _config.ActiveSubscriptionIndex >= 0 && _config.ActiveSubscriptionIndex < _subscriptions.Count)
            {
                ForceUpdateListBoxSelection(_config.ActiveSubscriptionIndex);
            }

            // 刷新 AAD 登录状态展示：关闭窗口不会“丢登录”，这里只是把已保存的认证记录显示出来。
            // 注意：这里不主动拿 Token（避免触发交互式登录），只读取已保存的 AuthenticationRecord。
            await RefreshSavedAadStatusIndicatorsAsync();
        }

        private static async Task<string?> TryReadSavedAadUsernameAsync(string profileKey, CancellationToken ct = default)
        {
            try
            {
                var path = Path.Combine(PathManager.Instance.AppDataPath, $"azure_auth_record_{profileKey}.json");
                if (!File.Exists(path))
                    return null;

                await using var stream = File.OpenRead(path);
                var record = await AuthenticationRecord.DeserializeAsync(stream, ct);
                return string.IsNullOrWhiteSpace(record?.Username) ? null : record.Username;
            }
            catch
            {
                return null;
            }
        }

        private async Task RefreshSavedAadStatusIndicatorsAsync()
        {
            // --- AI 聊天 ---
            if (ProviderTypeComboBox.SelectedIndex == 1 && AzureAuthModeComboBox.SelectedIndex == 1)
            {
                var username = await TryReadSavedAadUsernameAsync("ai");
                SetStatus(AadStatusTextBlock,
                    username != null ? $"✓ 已保存登录: {username}" : "未登录（无保存记录）",
                    username != null ? "#22863a" : "#6a737d");
            }
            else
            {
                AadStatusTextBlock.Text = "";
            }

            // --- Media 图片 ---
            if (MediaImageProviderTypeComboBox.SelectedIndex == 1 && MediaImageAzureAuthModeComboBox.SelectedIndex == 1)
            {
                var username = await TryReadSavedAadUsernameAsync("media-image");
                SetStatus(MediaImageAadStatusTextBlock,
                    username != null ? $"✓ 已保存登录: {username}" : "未登录（无保存记录）",
                    username != null ? "#22863a" : "#6a737d");
            }
            else
            {
                MediaImageAadStatusTextBlock.Text = "";
            }

            // --- Media 视频 ---
            // 若视频终结点与图片一致，则实际会复用图片的登录状态；这里仍显示 media-video 的保存状态（若存在）供排查。
            var videoUsesImageEndpoint = MediaVideoUseImageEndpointCheckBox.IsChecked ?? true;
            var videoProviderIsAzure = videoUsesImageEndpoint
                ? MediaImageProviderTypeComboBox.SelectedIndex == 1
                : MediaVideoProviderTypeComboBox.SelectedIndex == 1;
            var videoAuthIsAad = videoUsesImageEndpoint
                ? MediaImageAzureAuthModeComboBox.SelectedIndex == 1
                : MediaVideoAzureAuthModeComboBox.SelectedIndex == 1;

            if (videoProviderIsAzure && videoAuthIsAad)
            {
                var username = await TryReadSavedAadUsernameAsync("media-video");

                // 共享终结点时更推荐看 media-image 的登录状态
                if (videoUsesImageEndpoint)
                {
                    SetStatus(MediaVideoAadStatusTextBlock,
                        "使用图片终结点时将复用图片登录状态（如需单独视频登录，请取消勾选共享终结点）",
                        "#6a737d");
                }
                else
                {
                    SetStatus(MediaVideoAadStatusTextBlock,
                        username != null ? $"✓ 已保存登录: {username}" : "未登录（无保存记录）",
                        username != null ? "#22863a" : "#6a737d");
                }
            }
            else
            {
                MediaVideoAadStatusTextBlock.Text = "";
            }
        }

        private void SubscriptionListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (SubscriptionListBox.SelectedItem is AzureSubscription subscription)
            {
                _selectedSubscription = subscription;
                LoadSubscriptionToEditor(subscription);
            }
        }

        private void SubscriptionListBox_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            if (_subscriptions.Count == 1 && SubscriptionListBox.SelectedIndex != 0)
            {
                ForceUpdateListBoxSelection(0);
            }
            else if (SubscriptionListBox.SelectedIndex >= 0)
            {
                if (SubscriptionListBox.SelectedItem is AzureSubscription subscription)
                {
                    LoadSubscriptionToEditor(subscription);
                }
            }
        }

        private void SubscriptionListBox_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(SubscriptionListBox).Properties.IsLeftButtonPressed)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_subscriptions.Count == 1 && SubscriptionListBox.SelectedIndex != 0)
                    {
                        ForceUpdateListBoxSelection(0);
                    }
                    else if (_subscriptions.Count > 1 && SubscriptionListBox.SelectedIndex >= 0)
                    {
                        var currentIndex = SubscriptionListBox.SelectedIndex;
                        ForceUpdateListBoxSelection(currentIndex);
                    }
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
        }

        private void LoadSubscriptionToEditor(AzureSubscription subscription)
        {
            SubscriptionNameTextBox.Text = subscription.Name;
            SubscriptionKeyTextBox.Text = subscription.SubscriptionKey;

            EndpointTextBox.Text = subscription.GetEffectiveEndpoint();
            UpdateEndpointParsedRegionDisplay();
        }

        private async void AddButton_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SubscriptionNameTextBox.Text) ||
                string.IsNullOrWhiteSpace(SubscriptionKeyTextBox.Text))
            {
                ShowMessage("请填写订阅名称和密钥");
                return;
            }

            AddButton.IsEnabled = false;
            AddButton.Content = "验证中...";

            try
            {
                var subscriptionKey = SubscriptionKeyTextBox.Text.Trim();
                var endpoint = EndpointTextBox.Text?.Trim() ?? "";
                var region = AzureSubscription.ParseRegionFromEndpoint(endpoint);
                if (string.IsNullOrWhiteSpace(region))
                {
                    ShowMessage("无法从终结点解析区域，请检查格式。\n示例: https://southeastasia.api.cognitive.microsoft.com/");
                    return;
                }

                var (isValid, message) = await ValidateAzureSubscriptionAsync(subscriptionKey, region, endpoint);

                if (!isValid)
                {
                    ShowMessage($"✗ {message}");
                    return;
                }

                var newSubscription = new AzureSubscription
                {
                    Name = SubscriptionNameTextBox.Text.Trim(),
                    SubscriptionKey = subscriptionKey,
                    ServiceRegion = region,
                    Endpoint = endpoint
                };
                _subscriptions.Add(newSubscription);
                ForceUpdateListBoxSelection(_subscriptions.Count - 1);

                ClearEditor();

                EnsureSelectionWhenSingleItem();

                ShowMessage("✓ 订阅添加成功！");
            }
            finally
            {
                AddButton.IsEnabled = true;
                AddButton.Content = "添加";
            }
        }

        private async void UpdateButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedSubscription == null)
            {
                ShowMessage("请先选择要更新的订阅");
                return;
            }

            if (string.IsNullOrWhiteSpace(SubscriptionNameTextBox.Text) ||
                string.IsNullOrWhiteSpace(SubscriptionKeyTextBox.Text))
            {
                ShowMessage("请填写订阅名称和密钥");
                return;
            }

            UpdateButton.IsEnabled = false;
            UpdateButton.Content = "验证中...";

            try
            {
                var subscriptionKey = SubscriptionKeyTextBox.Text.Trim();
                var endpoint = EndpointTextBox.Text?.Trim() ?? "";
                var region = AzureSubscription.ParseRegionFromEndpoint(endpoint);
                if (string.IsNullOrWhiteSpace(region))
                {
                    ShowMessage("无法从终结点解析区域，请检查格式。");
                    return;
                }

                var (isValid, message) = await ValidateAzureSubscriptionAsync(subscriptionKey, region, endpoint);

                if (!isValid)
                {
                    ShowMessage($"✗ {message}");
                    return;
                }
                _selectedSubscription.Name = SubscriptionNameTextBox.Text.Trim();
                _selectedSubscription.SubscriptionKey = subscriptionKey;
                _selectedSubscription.ServiceRegion = region;
                _selectedSubscription.Endpoint = endpoint;

                var selectedIndex = SubscriptionListBox.SelectedIndex;
                SubscriptionListBox.ItemsSource = null;
                SubscriptionListBox.ItemsSource = _subscriptions;
                ForceUpdateListBoxSelection(selectedIndex);

                ShowMessage("✓ 订阅更新成功！");
            }
            finally
            {
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = "更新";
            }
        }

        private void DeleteButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedSubscription == null)
            {
                ShowMessage("请先选择要删除的订阅");
                return;
            }

            _subscriptions.Remove(_selectedSubscription);
            ClearEditor();
            _selectedSubscription = null;

            ShowMessage("✓ 订阅删除成功！");
        }

        private async void SpeechTestButton_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SubscriptionKeyTextBox.Text))
            {
                ShowMessage("请输入订阅密钥");
                return;
            }

            SpeechTestButton.IsEnabled = false;
            SpeechTestButton.Content = "测试中...";

            try
            {
                var endpoint = EndpointTextBox.Text?.Trim() ?? "";
                var region = AzureSubscription.ParseRegionFromEndpoint(endpoint);
                if (string.IsNullOrWhiteSpace(region))
                {
                    ShowMessage("无法从终结点解析区域，请检查格式。");
                    return;
                }
                var subscriptionKey = SubscriptionKeyTextBox.Text.Trim();

                var (isValid, message) = await ValidateAzureSubscriptionAsync(subscriptionKey, region, endpoint);

                if (isValid)
                {
                    ShowMessage($"✓ {message}");
                }
                else
                {
                    ShowMessage($"✗ {message}");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"✗ 测试失败: {ex.Message}");
            }
            finally
            {
                SpeechTestButton.IsEnabled = true;
                SpeechTestButton.Content = "测试";
            }
        }

        private async void TestAllSubscriptionsButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_subscriptions.Count == 0)
            {
                ShowMessage("订阅列表为空，请先添加订阅");
                return;
            }

            TestAllSubscriptionsButton.IsEnabled = false;
            TestAllSubscriptionsButton.Content = "测试中...";
            TestAllResultTextBlock.Text = "正在测试所有订阅...";
            TestAllResultTextBlock.IsVisible = true;

            try
            {
                var results = new List<(string Name, string Region, bool IsValid, long ElapsedMs, string Message)>();

                foreach (var sub in _subscriptions)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var (isValid, message) = await _subscriptionValidator.ValidateAsync(sub, CancellationToken.None);
                    sw.Stop();
                    results.Add((sub.Name, sub.ServiceRegion, isValid, sw.ElapsedMilliseconds, message));
                }

                // Sort by speed (valid first, then by elapsed time)
                results.Sort((a, b) =>
                {
                    if (a.IsValid != b.IsValid) return a.IsValid ? -1 : 1;
                    return a.ElapsedMs.CompareTo(b.ElapsedMs);
                });

                var resultText = "测试结果（按速度排序）：\n";
                foreach (var (name, region, isValid, elapsedMs, message) in results)
                {
                    var icon = isValid ? "✓" : "✗";
                    resultText += $"{icon} {name} ({region}) — {elapsedMs}ms";
                    if (!isValid) resultText += $" [{message}]";
                    resultText += "\n";
                }

                if (results.Any(r => r.IsValid))
                {
                    var fastest = results.First(r => r.IsValid);
                    resultText += $"\n🏆 最快: {fastest.Name} ({fastest.Region}) — {fastest.ElapsedMs}ms";
                }

                TestAllResultTextBlock.Text = resultText.TrimEnd();
            }
            catch (Exception ex)
            {
                TestAllResultTextBlock.Text = $"测试失败: {ex.Message}";
            }
            finally
            {
                TestAllSubscriptionsButton.IsEnabled = true;
                TestAllSubscriptionsButton.Content = "全部测试速度";
            }
        }

        private async Task<(bool IsValid, string Message)> ValidateAzureSubscriptionAsync(string subscriptionKey, string region, string endpoint = "")
        {
            var subscription = new AzureSubscription
            {
                Name = "(test)",
                SubscriptionKey = subscriptionKey?.Trim() ?? "",
                ServiceRegion = region?.Trim() ?? "",
                Endpoint = endpoint?.Trim() ?? ""
            };

            return await _subscriptionValidator.ValidateAsync(subscription, CancellationToken.None);
        }

        private async void BatchStorageValidateButton_Click(object? sender, RoutedEventArgs e)
        {
            BatchStorageValidateButton.IsEnabled = false;
            BatchStorageValidateButton.Content = "验证中...";

            try
            {
                var connectionString = BatchStorageConnectionStringTextBox.Text?.Trim() ?? "";
                var (isValid, message) = await ValidateBatchStorageAsync(connectionString, CancellationToken.None);
                BatchStorageValidateStatusTextBlock.Text = message;
                _config.BatchStorageIsValid = isValid;
                UseSpeechSubtitleForReviewCheckBox.IsEnabled = isValid;
                if (!isValid)
                {
                    UseSpeechSubtitleForReviewCheckBox.IsChecked = false;
                }
            }
            catch (Exception ex)
            {
                BatchStorageValidateStatusTextBlock.Text = $"验证失败: {ex.Message}";
                _config.BatchStorageIsValid = false;
                UseSpeechSubtitleForReviewCheckBox.IsEnabled = false;
                UseSpeechSubtitleForReviewCheckBox.IsChecked = false;
            }
            finally
            {
                BatchStorageValidateButton.IsEnabled = true;
                BatchStorageValidateButton.Content = "验证";
            }
        }

        private static async Task<(bool IsValid, string Message)> ValidateBatchStorageAsync(
            string connectionString,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return (false, "请填写存储账号连接字符串");
            }

            try
            {
                var serviceClient = new BlobServiceClient(connectionString);
                await serviceClient.GetAccountInfoAsync(token);

                var audioContainer = serviceClient.GetBlobContainerClient(AzureSpeechConfig.DefaultBatchAudioContainerName);
                await audioContainer.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: token);

                var resultContainer = serviceClient.GetBlobContainerClient(AzureSpeechConfig.DefaultBatchResultContainerName);
                await resultContainer.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: token);

                return (true, "存储账号验证成功，可用");
            }
            catch (Exception ex)
            {
                return (false, $"存储账号验证失败: {ex.Message}");
            }
        }

        private void ClearEditor()
        {
            SubscriptionNameTextBox.Text = "";
            SubscriptionKeyTextBox.Text = "";
            EndpointTextBox.Text = "";
            EndpointParsedRegionTextBlock.Text = "";
        }

        private void ShowMessage(string message)
        {
            var messageBox = new Window
            {
                Title = "提示",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Spacing = 20,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        new Button { Content = "确定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                    }
                }
            };

            if (messageBox.Content is StackPanel panel && panel.Children.LastOrDefault() is Button okButton)
            {
                okButton.Click += (_, _) => messageBox.Close();
            }

            messageBox.ShowDialog(this);
        }

        private void BrowseButton_Click(object? sender, RoutedEventArgs e)
        {
            _ = BrowseSessionDirectoryAsync();
        }

        private async Task BrowseSessionDirectoryAsync()
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择会话目录",
                AllowMultiple = false
            });

            var folder = folders.FirstOrDefault();
            if (folder == null)
            {
                return;
            }

            var path = folder.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowMessage("无法读取所选目录路径");
                return;
            }

            SessionDirectoryTextBox.Text = path;
        }

        private async void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_subscriptions.Count == 0)
            {
                ShowMessage("请至少添加一个Azure订阅后再保存配置。");
                return;
            }

            SaveButton.IsEnabled = false;
            SaveButton.Content = "验证中...";

            try
            {
                var validSubscriptions = new List<AzureSubscription>();
                var invalidSubscriptions = "";

                foreach (var subscription in _subscriptions)
                {
                    var (isValid, _) = await ValidateAzureSubscriptionAsync(
                        subscription.SubscriptionKey, subscription.GetEffectiveRegion(), subscription.Endpoint);
                    if (isValid)
                    {
                        validSubscriptions.Add(subscription);
                    }
                    else
                    {
                        invalidSubscriptions += $"• {subscription.Name} ({subscription.GetEffectiveRegion()})\n";
                    }
                }

                if (validSubscriptions.Count == 0)
                {
                    ShowMessage("所有订阅验证都失败了，请检查订阅密钥和区域设置后重试。\n\n无法保存配置。");
                    return;
                }

                if (!string.IsNullOrEmpty(invalidSubscriptions))
                {
                    ShowMessage($"以下订阅验证失败，将不会被保存：\n\n{invalidSubscriptions}\n只有验证成功的订阅会被保存。");
                }
                _config.Subscriptions.Clear();
                foreach (var subscription in validSubscriptions)
                {
                    _config.Subscriptions.Add(subscription);
                }

                string? selectedSubscriptionName = null;
                if (SubscriptionListBox.SelectedItem is AzureSubscription selectedSubscription)
                {
                    selectedSubscriptionName = selectedSubscription.Name;
                }

                if (!string.IsNullOrEmpty(selectedSubscriptionName))
                {
                    var matchIndex = validSubscriptions.FindIndex(s => s.Name == selectedSubscriptionName);
                    if (matchIndex >= 0)
                    {
                        _config.ActiveSubscriptionIndex = matchIndex;
                    }
                    else
                    {
                        _config.ActiveSubscriptionIndex = 0;
                    }
                }
                else
                {
                    _config.ActiveSubscriptionIndex = 0;
                }

                _config.FilterModalParticles = FilterModalParticlesCheckBox.IsChecked ?? true;
                _config.MaxHistoryItems = (int)(MaxHistoryItemsNumeric.Value ?? 15);
                _config.RealtimeMaxLength = (int)(RealtimeMaxLengthNumeric.Value ?? 150);
                _config.ChunkDurationMs = (int)(ChunkDurationMsNumeric.Value ?? 200);

                _config.EnableAutoTimeout = EnableAutoTimeoutCheckBox.IsChecked ?? true;
                _config.InitialSilenceTimeoutSeconds = (int)(InitialSilenceTimeoutSecondsNumeric.Value ?? 25);
                _config.EndSilenceTimeoutSeconds = (int)(EndSilenceTimeoutSecondsNumeric.Value ?? 1);

                _config.EnableNoResponseRestart = EnableNoResponseRestartCheckBox.IsChecked ?? false;
                _config.NoResponseRestartSeconds = (int)(NoResponseRestartSecondsNumeric.Value ?? 3);
                _config.AudioActivityThreshold = (int)(AudioActivityThresholdNumeric.Value ?? 600);
                _config.AudioLevelGain = (double)(AudioLevelGainNumeric.Value ?? 2.0m);
                var presetIndex = Math.Clamp(AutoGainPresetComboBox.SelectedIndex, 0, 3);
                _config.AutoGainEnabled = presetIndex > 0;
                _config.AutoGainPreset = (AutoGainPreset)presetIndex;
                _config.ShowReconnectMarkerInSubtitle = ShowReconnectMarkerCheckBox.IsChecked ?? true;

                _config.EnableRecording = EnableRecordingCheckBox.IsChecked ?? true;
                _config.RecordingMp3BitrateKbps = (int)(RecordingMp3BitrateNumeric.Value ?? 256);

                _config.ExportSrtSubtitles = ExportSrtCheckBox.IsChecked ?? false;
                _config.ExportVttSubtitles = ExportVttCheckBox.IsChecked ?? false;

                _config.DefaultFontSize = GetSelectedFontSize();
                Controls.AdvancedRichTextBox.DefaultFontSizeValue = _config.DefaultFontSize;

                _config.UseSpeechSubtitleForReview = UseSpeechSubtitleForReviewCheckBox.IsChecked ?? false;

                _config.BatchStorageConnectionString = BatchStorageConnectionStringTextBox.Text?.Trim() ?? "";
                _config.BatchAudioContainerName = AzureSpeechConfig.DefaultBatchAudioContainerName;
                _config.BatchResultContainerName = AzureSpeechConfig.DefaultBatchResultContainerName;
                _config.BatchLogLevel = BatchLogLevelComboBox.SelectedIndex switch
                {
                    1 => BatchLogLevel.FailuresOnly,
                    2 => BatchLogLevel.SuccessAndFailure,
                    _ => BatchLogLevel.Off
                };
                _config.EnableAuditLog = EnableAuditLogCheckBox.IsChecked ?? false;
                _config.BatchForceRegeneration = BatchForceRegenerationCheckBox.IsChecked ?? false;
                _config.ContextMenuForceRegeneration = ContextMenuForceRegenerationCheckBox.IsChecked ?? true;
                _config.EnableBatchSubtitleSentenceSplit = EnableBatchSentenceSplitCheckBox.IsChecked ?? true;
                _config.BatchSubtitleSplitOnComma = BatchSplitOnCommaCheckBox.IsChecked ?? false;
                _config.BatchSubtitleMaxChars = (int)(BatchMaxCharsNumeric.Value ?? 24);
                _config.BatchSubtitleMaxDurationSeconds = (double)(BatchMaxDurationNumeric.Value ?? 6m);
                _config.BatchSubtitlePauseSplitMs = (int)(BatchPauseSplitNumeric.Value ?? 500);

                if (string.IsNullOrWhiteSpace(_config.BatchStorageConnectionString))
                {
                    _config.BatchStorageIsValid = false;
                    _config.UseSpeechSubtitleForReview = false;
                    BatchStorageValidateStatusTextBlock.Text = "";
                }
                else
                {
                    var (storageValid, storageMessage) = await ValidateBatchStorageAsync(
                        _config.BatchStorageConnectionString,
                        CancellationToken.None);
                    _config.BatchStorageIsValid = storageValid;
                    BatchStorageValidateStatusTextBlock.Text = storageMessage;
                    if (!storageValid)
                    {
                        _config.UseSpeechSubtitleForReview = false;
                    }
                }

                var sessionDir = SessionDirectoryTextBox.Text?.Trim() ?? "";
                var defaultDir = PathManager.Instance.DefaultSessionsPath;
                _config.SessionDirectoryOverride = string.IsNullOrWhiteSpace(sessionDir)
                    ? null
                    : (string.Equals(sessionDir, defaultDir, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : sessionDir);
                PathManager.Instance.SetSessionsPath(_config.SessionDirectoryOverride);

                UpdateAiConfigFromUi();
                _config.AiConfig = _aiConfig;
                UpdateMediaGenConfigFromUi();

                ConfigurationUpdated?.Invoke(this, _config);

                ShowMessage($"✓ 配置保存成功！共保存了 {validSubscriptions.Count} 个有效订阅。\n{BatchStorageValidateStatusTextBlock.Text}");
                Close(true);
            }
            finally
            {
                SaveButton.IsEnabled = true;
                SaveButton.Content = "保存";
            }
        }

        private void UpdateAiConfigFromUi()
        {
            _aiConfig.ProviderType = ProviderTypeComboBox.SelectedIndex == 1
                ? AiProviderType.AzureOpenAi
                : AiProviderType.OpenAiCompatible;
            _aiConfig.ApiEndpoint = ApiEndpointTextBox.Text?.Trim() ?? "";
            _aiConfig.ApiKey = ApiKeyTextBox.Text?.Trim() ?? "";
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
                quickModel = _aiConfig.ModelName;
            }
            if (string.IsNullOrWhiteSpace(summaryModel))
            {
                summaryModel = quickModel;
            }

            _aiConfig.QuickModelName = quickModel;
            _aiConfig.SummaryModelName = summaryModel;
            _aiConfig.ModelName = quickModel;

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
                quickDeployment = _aiConfig.DeploymentName;
            }
            if (string.IsNullOrWhiteSpace(summaryDeployment))
            {
                summaryDeployment = quickDeployment;
            }

            _aiConfig.QuickDeploymentName = quickDeployment;
            _aiConfig.SummaryDeploymentName = summaryDeployment;
            _aiConfig.DeploymentName = quickDeployment;
            _aiConfig.ApiVersion = ApiVersionTextBox.Text?.Trim() ?? "2024-02-01";
            _aiConfig.AzureAuthMode = AzureAuthModeComboBox.SelectedIndex == 1
                ? AzureAuthMode.AAD
                : AzureAuthMode.ApiKey;
            _aiConfig.AzureTenantId = AzureTenantIdTextBox.Text?.Trim() ?? "";
            _aiConfig.AzureClientId = AzureClientIdTextBox.Text?.Trim() ?? "";
            _aiConfig.SummaryEnableReasoning = SummaryReasoningCheckBox.IsChecked == true;
            _aiConfig.InsightSystemPrompt = InsightSystemPromptTextBox.Text?.Trim() ?? "";
            _aiConfig.ReviewSystemPrompt = ReviewSystemPromptTextBox.Text?.Trim() ?? "";
            _aiConfig.InsightUserContentTemplate = InsightUserContentTemplateTextBox.Text?.Trim() ?? "";
            _aiConfig.ReviewUserContentTemplate = ReviewUserContentTemplateTextBox.Text?.Trim() ?? "";
            _aiConfig.AutoInsightBufferOutput = AutoInsightBufferCheckBox.IsChecked == true;
            _aiConfig.PresetButtons = _presetButtons
                .Where(b => !string.IsNullOrWhiteSpace(b.Name))
                .ToList();
            _aiConfig.ReviewSheets = _reviewSheets
                .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .Select(s => new ReviewSheetPreset
                {
                    Name = s.Name.Trim(),
                    FileTag = string.IsNullOrWhiteSpace(s.FileTag) ? "summary" : s.FileTag.Trim(),
                    Prompt = s.Prompt?.Trim() ?? "",
                    IncludeInBatch = s.IncludeInBatch
                })
                .ToList();
        }

        private void UpdateMediaGenConfigFromUi()
        {
            var mc = _config.MediaGenConfig;
            mc.ImageModel = MediaImageModelTextBox.Text?.Trim() ?? "gpt-image-1";
            mc.ImageProviderType = MediaImageProviderTypeComboBox.SelectedIndex == 1
                ? AiProviderType.AzureOpenAi
                : AiProviderType.OpenAiCompatible;
            mc.ImageApiEndpoint = MediaImageEndpointTextBox.Text?.Trim() ?? "";
            mc.ImageApiKey = MediaImageApiKeyTextBox.Text?.Trim() ?? "";
            mc.ImageAzureAuthMode = MediaImageAzureAuthModeComboBox.SelectedIndex == 1
                ? AzureAuthMode.AAD
                : AzureAuthMode.ApiKey;
            mc.ImageAzureTenantId = MediaImageAzureTenantIdTextBox.Text?.Trim() ?? "";
            mc.ImageAzureClientId = MediaImageAzureClientIdTextBox.Text?.Trim() ?? "";

            mc.VideoUseImageEndpoint = MediaVideoUseImageEndpointCheckBox.IsChecked ?? true;

            // 视频模型字段：
            // - OpenAI 兼容：通常填写模型名（例如 sora-2）
            // - Azure OpenAI：该字段会作为“部署名 / model 参数”使用，且后端要求部署的模型为 'sora'
            var videoUsesImageEndpoint = mc.VideoUseImageEndpoint;
            var effectiveProviderIsAzure = videoUsesImageEndpoint
                ? (MediaImageProviderTypeComboBox.SelectedIndex == 1)
                : (MediaVideoProviderTypeComboBox.SelectedIndex == 1);

            var rawVideoModel = MediaVideoModelTextBox.Text?.Trim();
            mc.VideoModel = string.IsNullOrWhiteSpace(rawVideoModel)
                ? "sora-2"
                : rawVideoModel;

            mc.VideoApiMode = MediaVideoApiModeComboBox.SelectedIndex == 1
                ? VideoApiMode.Videos
                : VideoApiMode.SoraJobs;
            mc.VideoProviderType = MediaVideoProviderTypeComboBox.SelectedIndex == 1
                ? AiProviderType.AzureOpenAi
                : AiProviderType.OpenAiCompatible;
            mc.VideoApiEndpoint = MediaVideoEndpointTextBox.Text?.Trim() ?? "";
            mc.VideoApiKey = MediaVideoApiKeyTextBox.Text?.Trim() ?? "";
            mc.VideoAzureAuthMode = MediaVideoAzureAuthModeComboBox.SelectedIndex == 1
                ? AzureAuthMode.AAD
                : AzureAuthMode.ApiKey;
            mc.VideoAzureTenantId = MediaVideoAzureTenantIdTextBox.Text?.Trim() ?? "";
            mc.VideoAzureClientId = MediaVideoAzureClientIdTextBox.Text?.Trim() ?? "";
            mc.OutputDirectory = MediaOutputDirTextBox.Text?.Trim() ?? "";
        }

        private async void MediaBrowseOutputDirButton_Click(object? sender, RoutedEventArgs e)
        {
            var storageProvider = StorageProvider;
            var result = await storageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "选择 Media Studio 输出目录",
                AllowMultiple = false
            });
            if (result.Count > 0)
            {
                MediaOutputDirTextBox.Text = result[0].Path.LocalPath;
            }
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

        private async void AiTestButton_Click(object? sender, RoutedEventArgs e)
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

            AiTestButton.IsEnabled = false;
            AiTestButton.Content = "测试中...";
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
                AiTestButton.IsEnabled = true;
                AiTestButton.Content = "测试连接";
            }
        }

        private void SelectFontSizeComboBox(int fontSize)
        {
            for (var i = 0; i < DefaultFontSizeComboBox.Items.Count; i++)
            {
                if (DefaultFontSizeComboBox.Items[i] is ComboBoxItem item &&
                    item.Tag?.ToString() == fontSize.ToString())
                {
                    DefaultFontSizeComboBox.SelectedIndex = i;
                    return;
                }
            }
            // Fallback to 38 (index 7)
            DefaultFontSizeComboBox.SelectedIndex = 7;
        }

        private int GetSelectedFontSize()
        {
            if (DefaultFontSizeComboBox.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out var size))
            {
                return size;
            }
            return 38;
        }

        private void UpdateEndpointParsedRegionDisplay()
        {
            var endpoint = EndpointTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                EndpointParsedRegionTextBlock.Text = "";
                return;
            }

            var region = AzureSubscription.ParseRegionFromEndpoint(endpoint);
            if (!string.IsNullOrWhiteSpace(region))
            {
                var type = endpoint.Contains(".azure.cn", StringComparison.OrdinalIgnoreCase) ? "中国区" : "国际版";
                EndpointParsedRegionTextBlock.Text = $"✓ 已识别区域: {region} ({type})";
                EndpointParsedRegionTextBlock.Foreground = Avalonia.Media.Brushes.DarkGreen;
            }
            else
            {
                EndpointParsedRegionTextBlock.Text = "✗ 无法识别区域，请检查终结点格式";
                EndpointParsedRegionTextBlock.Foreground = Avalonia.Media.Brushes.Red;
            }
        }
    }
}
