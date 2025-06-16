﻿using Avalonia.Controls;
using Avalonia.Interactivity;
using TranslationToolUI.Models;
using TranslationToolUI.Services;
using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Translation;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Platform.Storage;
using System.Net.Http;

namespace TranslationToolUI.Views
{
    public partial class ConfigView : Window
    {
        private readonly AzureSpeechConfig _config;
        private readonly ObservableCollection<AzureSubscription> _subscriptions;
        private AzureSubscription? _selectedSubscription;

        public event EventHandler<AzureSpeechConfig>? ConfigurationUpdated;

        public AzureSpeechConfig Config => _config;

        public ConfigView()
        {
            InitializeComponent();
            _config = new AzureSpeechConfig();
            _subscriptions = new ObservableCollection<AzureSubscription>();
            LoadDefaultConfig();
            SetupEventHandlers();
        }

        public ConfigView(AzureSpeechConfig config)
        {
            InitializeComponent();
            _config = config;
            _subscriptions = new ObservableCollection<AzureSubscription>(_config.Subscriptions);
            LoadConfigValues();
            SetupEventHandlers();
        }
        private void SetupEventHandlers()
        {
            SaveButton.Click += SaveButton_Click;
            CancelButton.Click += CancelButton_Click;
            BrowseButton.Click += BrowseButton_Click;
            AddButton.Click += AddButton_Click;
            UpdateButton.Click += UpdateButton_Click;
            DeleteButton.Click += DeleteButton_Click;
            TestButton.Click += TestButton_Click;
            SubscriptionListBox.SelectionChanged += SubscriptionListBox_SelectionChanged;
            SubscriptionListBox.DoubleTapped += SubscriptionListBox_DoubleTapped;
            SubscriptionListBox.PointerPressed += SubscriptionListBox_PointerPressed;

            this.Opened += ConfigView_Opened;
        }
        private void LoadDefaultConfig()
        {
            SubscriptionListBox.ItemsSource = _subscriptions;

            RegionComboBox.SelectedIndex = 0;
            FilterModalParticlesCheckBox.IsChecked = true;
            MaxHistoryItemsNumeric.Value = 15;
            RealtimeMaxLengthNumeric.Value = 150;

            SessionDirectoryTextBox.Text = PathManager.Instance.SessionsPath;

        }
        private void LoadConfigValues()
        {
            SubscriptionListBox.ItemsSource = _subscriptions;

            if (_config.ActiveSubscriptionIndex >= 0 && _config.ActiveSubscriptionIndex < _subscriptions.Count)
            {
            }            FilterModalParticlesCheckBox.IsChecked = _config.FilterModalParticles;
            MaxHistoryItemsNumeric.Value = _config.MaxHistoryItems;
            RealtimeMaxLengthNumeric.Value = _config.RealtimeMaxLength;
            
            SessionDirectoryTextBox.Text = _config.SessionDirectory;

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

        private async void ConfigView_Opened(object? sender, EventArgs e)
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

            for (int i = 0; i < RegionComboBox.Items.Count; i++)
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

                var isValid = await ValidateAzureSubscriptionAsync(subscriptionKey, region);

                if (!isValid)
                {
                    ShowMessage("✗ 订阅密钥或区域配置无效，请检查后重试。");
                    return;
                }

                var newSubscription = new AzureSubscription
                {
                    Name = SubscriptionNameTextBox.Text.Trim(),
                    SubscriptionKey = subscriptionKey,
                    ServiceRegion = region
                }; _subscriptions.Add(newSubscription);
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

                var isValid = await ValidateAzureSubscriptionAsync(subscriptionKey, region);

                if (!isValid)
                {
                    ShowMessage("✗ 订阅密钥或区域配置无效，请检查后重试。");
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
        private async void TestButton_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SubscriptionKeyTextBox.Text))
            {
                ShowMessage("请输入订阅密钥");
                return;
            }

            TestButton.IsEnabled = false;
            TestButton.Content = "测试中...";

            try
            {
                var region = (RegionComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "southeastasia";
                var subscriptionKey = SubscriptionKeyTextBox.Text.Trim();

                var isValid = await ValidateAzureSubscriptionAsync(subscriptionKey, region);

                if (isValid)
                {
                    ShowMessage("✓ 订阅密钥和区域配置正确，可以正常使用！");
                }
                else
                {
                    ShowMessage("✗ 订阅密钥或区域配置错误，请检查后重试。");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"✗ 测试失败: {ex.Message}");
            }
            finally
            {
                TestButton.IsEnabled = true;
                TestButton.Content = "测试";
            }
        }
        private async Task<bool> ValidateAzureSubscriptionAsync(string subscriptionKey, string region)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(subscriptionKey) || subscriptionKey.Length < 32)
                {
                    return false;
                }

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var tokenEndpoint = $"https://{region}.api.cognitive.microsoft.com/sts/v1.0/issueToken";

                try
                {
                    var response = await httpClient.PostAsync(tokenEndpoint, new StringContent(""));

                    if (response.IsSuccessStatusCode)
                    {
                        var token = await response.Content.ReadAsStringAsync();
                        return !string.IsNullOrEmpty(token);
                    }
                    else
                    {
                        return response.StatusCode != System.Net.HttpStatusCode.Unauthorized &&
                               response.StatusCode != System.Net.HttpStatusCode.Forbidden &&
                               response.StatusCode != System.Net.HttpStatusCode.BadRequest;
                    }
                }
                catch (HttpRequestException)
                {
                    return false;
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                var errorMsg = ex.Message.ToLower();
                bool isAuthError = errorMsg.Contains("401") ||
                                  errorMsg.Contains("403") ||
                                  errorMsg.Contains("unauthorized") ||
                                  errorMsg.Contains("forbidden") ||
                                  errorMsg.Contains("authentication") ||
                                  errorMsg.Contains("invalid") ||
                                  errorMsg.Contains("subscription") ||
                                  errorMsg.Contains("key");

                return !isAuthError;
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
                okButton.Click += (s, e) => messageBox.Close();
            }

            messageBox.ShowDialog(this);
        }        private void BrowseButton_Click(object? sender, RoutedEventArgs e)
        {
            ShowMessage("会话目录由系统自动管理，无需手动设置。\n\n当前目录：" + PathManager.Instance.SessionsPath);
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
                string invalidSubscriptions = "";

                foreach (var subscription in _subscriptions)
                {
                    bool isValid = await ValidateAzureSubscriptionAsync(subscription.SubscriptionKey, subscription.ServiceRegion);
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

                ConfigurationUpdated?.Invoke(this, _config);

                ShowMessage($"✓ 配置保存成功！共保存了 {validSubscriptions.Count} 个有效订阅。");
                Close(true);
            }
            finally
            {
                SaveButton.IsEnabled = true;
                SaveButton.Content = "保存";
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}


