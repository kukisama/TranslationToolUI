using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using TrueFluentPro.Models;
using TrueFluentPro.Services;

namespace TrueFluentPro.Helpers
{
    public static class ConfigViewHelper
    {
        public static void ShowMessage(string message, Window owner)
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

            messageBox.ShowDialog(owner);
        }

        public static void ForceUpdateListBoxSelection(ListBox listBox, int targetIndex, int itemCount)
        {
            if (targetIndex >= 0 && targetIndex < itemCount)
            {
                listBox.Focus();
                listBox.SelectedIndex = -1;
                listBox.SelectedIndex = targetIndex;

                if (listBox.SelectedItem != null)
                {
                    listBox.ScrollIntoView(listBox.SelectedItem);
                }
            }
        }

        public static void ApplyModelDeploymentFallbacks(
            AiConfig config,
            string? existingModel = null,
            string? existingDeployment = null)
        {
            if (string.IsNullOrWhiteSpace(config.QuickModelName))
                config.QuickModelName = config.SummaryModelName;
            if (string.IsNullOrWhiteSpace(config.SummaryModelName))
                config.SummaryModelName = config.QuickModelName;
            if (string.IsNullOrWhiteSpace(config.QuickModelName))
                config.QuickModelName = existingModel ?? "";
            if (string.IsNullOrWhiteSpace(config.SummaryModelName))
                config.SummaryModelName = config.QuickModelName;
            config.ModelName = config.QuickModelName;

            if (string.IsNullOrWhiteSpace(config.QuickDeploymentName))
                config.QuickDeploymentName = config.SummaryDeploymentName;
            if (string.IsNullOrWhiteSpace(config.SummaryDeploymentName))
                config.SummaryDeploymentName = config.QuickDeploymentName;
            if (string.IsNullOrWhiteSpace(config.QuickDeploymentName))
                config.QuickDeploymentName = existingDeployment ?? "";
            if (string.IsNullOrWhiteSpace(config.SummaryDeploymentName))
                config.SummaryDeploymentName = config.QuickDeploymentName;
            config.DeploymentName = config.QuickDeploymentName;
        }

        public static async Task RunAiConnectionTestAsync(
            AiConfig testConfig,
            TextBlock statusTextBlock,
            TextBox? reasoningOutputTextBox,
            Button testButton)
        {
            if (!testConfig.IsValid)
            {
                statusTextBlock.Text = "请填写必要的配置信息";
                statusTextBlock.Foreground = Brushes.Orange;
                return;
            }

            testButton.IsEnabled = false;
            testButton.Content = "测试中...";
            statusTextBlock.Text = "正在连接...";
            statusTextBlock.Foreground = Brushes.Gray;
            if (reasoningOutputTextBox != null)
                reasoningOutputTextBox.Text = "";

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

                if (reasoningOutputTextBox != null)
                {
                    if (reasoningReceived)
                    {
                        reasoningOutputTextBox.Text = reasoningBuilder.ToString();
                    }
                    else if (testConfig.SummaryEnableReasoning)
                    {
                        reasoningOutputTextBox.Text = "未收到思考内容。";
                    }
                }

                if (received)
                {
                    statusTextBlock.Text = "连接成功！AI 服务可用。";
                    statusTextBlock.Foreground = Brushes.Green;
                }
                else
                {
                    statusTextBlock.Text = "连接成功但未收到响应，请检查模型配置。";
                    statusTextBlock.Foreground = Brushes.Orange;
                }
            }
            catch (OperationCanceledException)
            {
                statusTextBlock.Text = "连接超时，请检查 API 端点是否正确。";
                statusTextBlock.Foreground = Brushes.Red;
            }
            catch (Exception ex)
            {
                statusTextBlock.Text = $"连接失败: {ex.Message}";
                statusTextBlock.Foreground = Brushes.Red;
            }
            finally
            {
                testButton.IsEnabled = true;
                testButton.Content = "测试连接";
            }
        }
    }
}
