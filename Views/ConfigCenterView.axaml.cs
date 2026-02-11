using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
            BatchStorageValidateButton.Click += BatchStorageValidateButton_Click;
            SubscriptionListBox.SelectionChanged += SubscriptionListBox_SelectionChanged;
            SubscriptionListBox.DoubleTapped += SubscriptionListBox_DoubleTapped;
            SubscriptionListBox.PointerPressed += SubscriptionListBox_PointerPressed;

            EnableRecordingCheckBox.IsCheckedChanged += EnableRecordingCheckBox_IsCheckedChanged;
            EnableAutoTimeoutCheckBox.IsCheckedChanged += EnableAutoTimeoutCheckBox_IsCheckedChanged;
            EnableNoResponseRestartCheckBox.IsCheckedChanged += EnableNoResponseRestartCheckBox_IsCheckedChanged;

            ProviderTypeComboBox.SelectionChanged += (_, _) => UpdateProviderFieldsVisibility();
            AiTestButton.Click += AiTestButton_Click;
            AddPresetButton.Click += AddPresetButton_Click;
            AddReviewSheetButton.Click += AddReviewSheetButton_Click;

            Opened += ConfigCenterView_Opened;
        }

        private void LoadDefaultConfig()
        {
            SubscriptionListBox.ItemsSource = _subscriptions;

            RegionComboBox.SelectedIndex = 0;
            FilterModalParticlesCheckBox.IsChecked = true;
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

            for (var i = 0; i < RegionComboBox.Items.Count; i++)
            {
                if (RegionComboBox.Items[i] is ComboBoxItem item &&
                    item.Tag?.ToString() == subscription.ServiceRegion)
                {
                    RegionComboBox.SelectedIndex = i;
                    break;
                }
            }
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
                var region = (RegionComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "southeastasia";

                var (isValid, message) = await ValidateAzureSubscriptionAsync(subscriptionKey, region);

                if (!isValid)
                {
                    ShowMessage($"✗ {message}");
                    return;
                }

                var newSubscription = new AzureSubscription
                {
                    Name = SubscriptionNameTextBox.Text.Trim(),
                    SubscriptionKey = subscriptionKey,
                    ServiceRegion = region
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
                var region = (RegionComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "southeastasia";

                var (isValid, message) = await ValidateAzureSubscriptionAsync(subscriptionKey, region);

                if (!isValid)
                {
                    ShowMessage($"✗ {message}");
                    return;
                }
                _selectedSubscription.Name = SubscriptionNameTextBox.Text.Trim();
                _selectedSubscription.SubscriptionKey = subscriptionKey;
                _selectedSubscription.ServiceRegion = region;

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
                var region = (RegionComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "southeastasia";
                var subscriptionKey = SubscriptionKeyTextBox.Text.Trim();

                var (isValid, message) = await ValidateAzureSubscriptionAsync(subscriptionKey, region);

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

        private async Task<(bool IsValid, string Message)> ValidateAzureSubscriptionAsync(string subscriptionKey, string region)
        {
            var subscription = new AzureSubscription
            {
                Name = "(test)",
                SubscriptionKey = subscriptionKey?.Trim() ?? "",
                ServiceRegion = region?.Trim() ?? ""
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
            RegionComboBox.SelectedIndex = 0;
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
                    var (isValid, _) = await ValidateAzureSubscriptionAsync(subscription.SubscriptionKey, subscription.ServiceRegion);
                    if (isValid)
                    {
                        validSubscriptions.Add(subscription);
                    }
                    else
                    {
                        invalidSubscriptions += $"• {subscription.Name} ({subscription.ServiceRegion})\n";
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
    }
}
