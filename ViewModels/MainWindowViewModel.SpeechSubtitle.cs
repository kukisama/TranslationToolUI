using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using TranslationToolUI.Models;
using TranslationToolUI.Services;
using TranslationToolUI.Views;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.ComponentModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using TranslationToolUI.Services.Audio;
using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using NAudio.Wave;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Transcription;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using System.Xml;

namespace TranslationToolUI.ViewModels
{
    public partial class MainWindowViewModel
    {
        public bool IsSpeechSubtitleGenerating
        {
            get => _isSpeechSubtitleGenerating;
            private set
            {
                if (SetProperty(ref _isSpeechSubtitleGenerating, value))
                {
                    if (GenerateSpeechSubtitleCommand is RelayCommand cmd)
                    {
                        cmd.RaiseCanExecuteChanged();
                    }
                    if (GenerateBatchSpeechSubtitleCommand is RelayCommand batchCmd)
                    {
                        batchCmd.RaiseCanExecuteChanged();
                    }
                    if (CancelSpeechSubtitleCommand is RelayCommand cancelCmd)
                    {
                        cancelCmd.RaiseCanExecuteChanged();
                    }
                    if (GenerateReviewSummaryCommand is RelayCommand genCmd)
                    {
                        genCmd.RaiseCanExecuteChanged();
                    }
                    if (GenerateAllReviewSheetsCommand is RelayCommand allCmd)
                    {
                        allCmd.RaiseCanExecuteChanged();
                    }
                }
            }
        }

        public string SpeechSubtitleStatusMessage
        {
            get => _speechSubtitleStatusMessage;
            private set => SetProperty(ref _speechSubtitleStatusMessage, value);
        }

        public bool IsSpeechSubtitleOptionEnabled => _config.BatchStorageIsValid
            && !string.IsNullOrWhiteSpace(_config.BatchStorageConnectionString);

        public bool UseSpeechSubtitleForReview
        {
            get => _config.UseSpeechSubtitleForReview;
            set
            {
                if (_config.UseSpeechSubtitleForReview == value)
                {
                    return;
                }

                _config.UseSpeechSubtitleForReview = value;
                OnPropertyChanged(nameof(UseSpeechSubtitleForReview));
                OnPropertyChanged(nameof(BatchStartButtonText));
                if (GenerateReviewSummaryCommand is RelayCommand genCmd)
                {
                    genCmd.RaiseCanExecuteChanged();
                }
                if (GenerateAllReviewSheetsCommand is RelayCommand allCmd)
                {
                    allCmd.RaiseCanExecuteChanged();
                }
                if (StartBatchCommand is RelayCommand startCmd)
                {
                    startCmd.RaiseCanExecuteChanged();
                }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _configService.SaveConfigAsync(_config);
                    }
                    catch
                    {
                    }
                });
            }
        }

        public string SpeechSubtitleOptionStatusText => IsSpeechSubtitleOptionEnabled
            ? "存储账号已验证，允许生成 speech 字幕"
            : "未验证存储账号，已禁用该选项";

        private void NormalizeSpeechSubtitleOption()
        {
            if (!IsSpeechSubtitleOptionEnabled && _config.UseSpeechSubtitleForReview)
            {
                _config.UseSpeechSubtitleForReview = false;
            }
        }

        private bool ShouldGenerateSpeechSubtitleForReview => IsSpeechSubtitleOptionEnabled
            && _config.UseSpeechSubtitleForReview;

        private bool CanGenerateSpeechSubtitleFromStorage()
        {
            if (!IsSpeechSubtitleOptionEnabled)
            {
                return false;
            }

            var subscription = _config.GetActiveSubscription();
            return subscription?.IsValid() == true && !string.IsNullOrWhiteSpace(_config.SourceLanguage);
        }

        private async Task<bool> EnsureSpeechSubtitleForReviewAsync(MediaFileItem audioFile)
        {
            if (!ShouldGenerateSpeechSubtitleForReview)
            {
                return true;
            }

            var speechPath = GetSpeechSubtitlePath(audioFile.FullPath);
            if (File.Exists(speechPath))
            {
                LoadSubtitleFilesForAudio(audioFile);
                var speechFile = _subtitleFiles.FirstOrDefault(item =>
                    string.Equals(item.FullPath, speechPath, StringComparison.OrdinalIgnoreCase));
                if (speechFile != null)
                {
                    SelectedSubtitleFile = speechFile;
                }
                return true;
            }

            if (!CanGenerateSpeechSubtitleFromStorage())
            {
                SpeechSubtitleStatusMessage = "缺少有效的存储账号或语音订阅，无法生成 speech 字幕";
                return false;
            }

            _speechSubtitleCts?.Cancel();
            _speechSubtitleCts = new CancellationTokenSource();
            var token = _speechSubtitleCts.Token;

            IsSpeechSubtitleGenerating = true;
            SpeechSubtitleStatusMessage = "speech 字幕生成中...";

            try
            {
                var success = await GenerateBatchSpeechSubtitleForFileAsync(
                    audioFile.FullPath,
                    token,
                    status => SpeechSubtitleStatusMessage = status);
                if (!success)
                {
                    SpeechSubtitleStatusMessage = "未识别到有效文本";
                    return false;
                }

                SpeechSubtitleStatusMessage = $"speech 字幕已生成: {Path.GetFileName(speechPath)}";
                LoadSubtitleFilesForAudio(audioFile);
                var speechFile = _subtitleFiles.FirstOrDefault(item =>
                    string.Equals(item.FullPath, speechPath, StringComparison.OrdinalIgnoreCase));
                if (speechFile != null)
                {
                    SelectedSubtitleFile = speechFile;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                SpeechSubtitleStatusMessage = "speech 字幕生成已取消";
                return false;
            }
            catch (Exception ex)
            {
                SpeechSubtitleStatusMessage = $"speech 字幕生成失败: {ex.Message}";
                return false;
            }
            finally
            {
                IsSpeechSubtitleGenerating = false;
                _speechSubtitleCts?.Dispose();
                _speechSubtitleCts = null;
            }
        }

        private bool CanGenerateSpeechSubtitle()
        {
            if (IsSpeechSubtitleGenerating)
            {
                return false;
            }

            if (SelectedAudioFile == null || string.IsNullOrWhiteSpace(SelectedAudioFile.FullPath))
            {
                return false;
            }

            if (!File.Exists(SelectedAudioFile.FullPath))
            {
                return false;
            }

            var subscription = _config.GetActiveSubscription();
            return subscription?.IsValid() == true && !string.IsNullOrWhiteSpace(_config.SourceLanguage);
        }

        private async void GenerateSpeechSubtitle()
        {
            if (!CanGenerateSpeechSubtitle())
            {
                SpeechSubtitleStatusMessage = "订阅或音频不可用";
                return;
            }

            var audioFile = SelectedAudioFile;
            if (audioFile == null)
            {
                return;
            }

            _speechSubtitleCts?.Cancel();
            _speechSubtitleCts = new CancellationTokenSource();
            var token = _speechSubtitleCts.Token;

            IsSpeechSubtitleGenerating = true;
            SpeechSubtitleStatusMessage = "正在转写...";

            try
            {
                var cues = await TranscribeSpeechToCuesAsync(audioFile.FullPath, token);
                if (cues.Count == 0)
                {
                    SpeechSubtitleStatusMessage = "未识别到有效文本";
                    return;
                }

                var outputPath = GetSpeechSubtitlePath(audioFile.FullPath);
                WriteVttFile(outputPath, cues);

                SpeechSubtitleStatusMessage = $"speech 字幕已生成: {Path.GetFileName(outputPath)}";
                LoadSubtitleFilesForAudio(audioFile);
            }
            catch (OperationCanceledException)
            {
                SpeechSubtitleStatusMessage = "转写已取消";
            }
            catch (Exception ex)
            {
                SpeechSubtitleStatusMessage = $"转写失败: {ex.Message}";
            }
            finally
            {
                IsSpeechSubtitleGenerating = false;
                _speechSubtitleCts?.Dispose();
                _speechSubtitleCts = null;
            }
        }

        private bool CanGenerateBatchSpeechSubtitle()
        {
            if (IsSpeechSubtitleGenerating)
            {
                return false;
            }

            if (SelectedAudioFile == null || string.IsNullOrWhiteSpace(SelectedAudioFile.FullPath))
            {
                return false;
            }

            if (!File.Exists(SelectedAudioFile.FullPath))
            {
                return false;
            }

            if (!IsSpeechSubtitleOptionEnabled)
            {
                return false;
            }

            return CanGenerateSpeechSubtitleFromStorage();
        }

        private async void GenerateBatchSpeechSubtitle()
        {
            if (!CanGenerateBatchSpeechSubtitle())
            {
                SpeechSubtitleStatusMessage = "请先验证存储账号与语音订阅";
                return;
            }

            var audioFile = SelectedAudioFile;
            if (audioFile == null)
            {
                return;
            }

            _speechSubtitleCts?.Cancel();
            _speechSubtitleCts = new CancellationTokenSource();
            var token = _speechSubtitleCts.Token;

            IsSpeechSubtitleGenerating = true;
            try
            {
                var success = await GenerateBatchSpeechSubtitleForFileAsync(
                    audioFile.FullPath,
                    token,
                    status => SpeechSubtitleStatusMessage = status);

                if (!success)
                {
                    SpeechSubtitleStatusMessage = "未识别到有效文本";
                    return;
                }

                var outputPath = GetSpeechSubtitlePath(audioFile.FullPath);
                SpeechSubtitleStatusMessage = $"speech 字幕已生成: {Path.GetFileName(outputPath)}";
                LoadSubtitleFilesForAudio(audioFile);
            }
            catch (OperationCanceledException)
            {
                SpeechSubtitleStatusMessage = "批量转写已取消";
            }
            catch (Exception ex)
            {
                SpeechSubtitleStatusMessage = $"批量转写失败: {ex.Message}";
            }
            finally
            {
                IsSpeechSubtitleGenerating = false;
                _speechSubtitleCts?.Dispose();
                _speechSubtitleCts = null;
            }
        }

        private async Task<bool> GenerateBatchSpeechSubtitleForFileAsync(
            string audioPath,
            CancellationToken token,
            Action<string>? onStatus)
        {
            if (string.IsNullOrWhiteSpace(audioPath))
            {
                return false;
            }

            if (!File.Exists(audioPath))
            {
                throw new FileNotFoundException("未找到音频文件", audioPath);
            }

            var subscription = _config.GetActiveSubscription();
            if (subscription == null || !subscription.IsValid())
            {
                throw new InvalidOperationException("语音订阅未配置");
            }

            if (!IsSpeechSubtitleOptionEnabled)
            {
                throw new InvalidOperationException("存储账号未验证");
            }

            onStatus?.Invoke("批量转写：上传音频...");

            var (audioContainer, outputContainer) = await GetBatchContainersAsync(
                _config.BatchStorageConnectionString,
                _config.BatchAudioContainerName,
                _config.BatchResultContainerName,
                token);

            var uploadedBlob = await UploadAudioToBlobAsync(
                audioPath,
                audioContainer,
                token);

            var contentUrl = CreateBlobReadSasUri(uploadedBlob, TimeSpan.FromHours(24));

            onStatus?.Invoke("批量转写：提交任务...");

            var (cues, transcriptionJson) = await BatchTranscribeSpeechToCuesAsync(
                contentUrl,
                _config.SourceLanguage,
                subscription,
                token,
                status => onStatus?.Invoke(status));

            if (cues.Count == 0)
            {
                return false;
            }

            var outputPath = GetSpeechSubtitlePath(audioPath);
            WriteVttFile(outputPath, cues);

            var baseName = Path.GetFileNameWithoutExtension(audioPath);
            await UploadTextToBlobAsync(outputContainer, baseName + ".speech.vtt", File.ReadAllText(outputPath), "text/vtt", token);
            await UploadTextToBlobAsync(outputContainer, baseName + ".speech.json", transcriptionJson, "application/json", token);

            return true;
        }

        private static async Task<(BlobContainerClient Audio, BlobContainerClient Result)> GetBatchContainersAsync(
            string connectionString,
            string audioContainerName,
            string resultContainerName,
            CancellationToken token)
        {
            var serviceClient = new BlobServiceClient(connectionString);
            var normalizedAudio = NormalizeContainerName(audioContainerName, AzureSpeechConfig.DefaultBatchAudioContainerName);
            var normalizedResult = NormalizeContainerName(resultContainerName, AzureSpeechConfig.DefaultBatchResultContainerName);

            var audioContainer = serviceClient.GetBlobContainerClient(normalizedAudio);
            await audioContainer.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: token);

            var resultContainer = serviceClient.GetBlobContainerClient(normalizedResult);
            await resultContainer.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: token);

            return (audioContainer, resultContainer);
        }

        private static string NormalizeContainerName(string? name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return fallback;
            }

            var normalized = new string(name.Trim().ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray());

            normalized = normalized.Trim('-');
            if (normalized.Length < 3)
            {
                return fallback;
            }

            if (normalized.Length > 63)
            {
                normalized = normalized.Substring(0, 63).Trim('-');
            }

            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }

        private static async Task<BlobClient> UploadAudioToBlobAsync(
            string audioPath,
            BlobContainerClient container,
            CancellationToken token)
        {
            var fileName = Path.GetFileName(audioPath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var blobName = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}{Path.GetExtension(fileName)}";

            var blobClient = container.GetBlobClient(blobName);
            using var stream = File.OpenRead(audioPath);
            await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: token);
            await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
            {
                ContentType = GetAudioContentType(audioPath)
            }, cancellationToken: token);
            return blobClient;
        }

        private static Uri CreateBlobReadSasUri(BlobClient blobClient, TimeSpan validFor)
        {
            if (!blobClient.CanGenerateSasUri)
            {
                throw new InvalidOperationException("无法生成 SAS URL，请确保使用存储账号连接字符串");
            }

            var builder = new BlobSasBuilder
            {
                BlobContainerName = blobClient.BlobContainerName,
                BlobName = blobClient.Name,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.Add(validFor)
            };

            builder.SetPermissions(BlobSasPermissions.Read);
            return blobClient.GenerateSasUri(builder);
        }

        private static string GetAudioContentType(string audioPath)
        {
            var extension = Path.GetExtension(audioPath).ToLowerInvariant();
            return extension switch
            {
                ".wav" => "audio/wav",
                ".mp3" => "audio/mpeg",
                ".m4a" => "audio/mp4",
                _ => "application/octet-stream"
            };
        }

        private static async Task UploadTextToBlobAsync(
            BlobContainerClient container,
            string blobName,
            string content,
            string contentType,
            CancellationToken token)
        {
            var blobClient = container.GetBlobClient(blobName);
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: token);
            await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
            {
                ContentType = contentType
            }, cancellationToken: token);
        }

        private static async Task<(List<SubtitleCue> Cues, string TranscriptionJson)> BatchTranscribeSpeechToCuesAsync(
            Uri contentUrl,
            string locale,
            AzureSubscription subscription,
            CancellationToken token,
            Action<string> onStatus)
        {
            var endpoint = $"https://{subscription.ServiceRegion}.api.cognitive.microsoft.com/speechtotext/v3.1/transcriptions";
            var requestBody = new
            {
                displayName = $"Batch-{DateTime.Now:yyyyMMdd_HHmmss}",
                locale = locale,
                contentUrls = new[] { contentUrl.ToString() },
                properties = new
                {
                    diarizationEnabled = true,
                    wordLevelTimestampsEnabled = true,
                    punctuationMode = "DictatedAndAutomatic",
                    profanityFilterMode = "Masked"
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscription.SubscriptionKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");

            using var response = await SpeechBatchHttpClient.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(token);
                throw new InvalidOperationException($"创建批量转写失败: {response.StatusCode} {detail}");
            }

            var statusUrl = response.Headers.Location?.ToString();
            if (string.IsNullOrWhiteSpace(statusUrl))
            {
                var body = await response.Content.ReadAsStringAsync(token);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("self", out var selfElement))
                {
                    statusUrl = selfElement.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(statusUrl))
            {
                throw new InvalidOperationException("未获取到批量转写状态地址");
            }

            string? lastStatusJson = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                using var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUrl);
                statusRequest.Headers.Add("Ocp-Apim-Subscription-Key", subscription.SubscriptionKey);

                using var statusResponse = await SpeechBatchHttpClient.SendAsync(statusRequest, token);
                var statusBody = await statusResponse.Content.ReadAsStringAsync(token);
                lastStatusJson = statusBody;

                if (!statusResponse.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"查询批量转写状态失败: {statusResponse.StatusCode} {statusBody}");
                }

                using var statusDoc = JsonDocument.Parse(statusBody);
                var status = statusDoc.RootElement.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString()
                    : "";

                if (string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    onStatus("批量转写：已完成，整理字幕...");
                    break;
                }

                if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    var errorSummary = BuildSpeechBatchFailureSummary(statusDoc) ?? "批量转写失败";
                    var ex = new InvalidOperationException(errorSummary);
                    ex.Data["SpeechBatchError"] = statusBody;
                    throw ex;
                }

                onStatus($"批量转写：{status}...");
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }

            var filesUrl = statusUrl.TrimEnd('/') + "/files";
            if (!string.IsNullOrWhiteSpace(lastStatusJson))
            {
                using var statusDoc = JsonDocument.Parse(lastStatusJson);
                if (statusDoc.RootElement.TryGetProperty("links", out var linksElement) &&
                    linksElement.TryGetProperty("files", out var filesElement))
                {
                    filesUrl = filesElement.GetString() ?? filesUrl;
                }
            }

            using var filesRequest = new HttpRequestMessage(HttpMethod.Get, filesUrl);
            filesRequest.Headers.Add("Ocp-Apim-Subscription-Key", subscription.SubscriptionKey);

            using var filesResponse = await SpeechBatchHttpClient.SendAsync(filesRequest, token);
            var filesBody = await filesResponse.Content.ReadAsStringAsync(token);
            if (!filesResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"获取批量转写文件列表失败: {filesResponse.StatusCode} {filesBody}");
            }

            var transcriptionUrl = ExtractTranscriptionContentUrl(filesBody);
            if (string.IsNullOrWhiteSpace(transcriptionUrl))
            {
                throw new InvalidOperationException("未找到批量转写结果文件");
            }

            var transcriptionJson = await SpeechBatchHttpClient.GetStringAsync(transcriptionUrl, token);
            var cues = ParseBatchTranscriptionToCues(transcriptionJson);
            return (cues, transcriptionJson);
        }

        private static string? ExtractTranscriptionContentUrl(string filesJson)
        {
            using var doc = JsonDocument.Parse(filesJson);
            if (!doc.RootElement.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in values.EnumerateArray())
            {
                var kind = item.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() : "";
                if (!string.Equals(kind, "Transcription", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (item.TryGetProperty("links", out var linksElement) &&
                    linksElement.TryGetProperty("contentUrl", out var contentElement))
                {
                    return contentElement.GetString();
                }
            }

            return null;
        }

        private static string? BuildSpeechBatchFailureSummary(JsonDocument statusDoc)
        {
            if (!statusDoc.RootElement.TryGetProperty("error", out var errorElement))
            {
                return null;
            }

            var code = errorElement.TryGetProperty("code", out var codeElement)
                ? codeElement.GetString()
                : null;
            var message = errorElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;

            var detailMessages = new List<string>();
            if (errorElement.TryGetProperty("details", out var detailsElement) &&
                detailsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in detailsElement.EnumerateArray())
                {
                    var detailMessage = detail.TryGetProperty("message", out var detailMessageElement)
                        ? detailMessageElement.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(detailMessage))
                    {
                        detailMessages.Add(detailMessage);
                    }
                }
            }

            var summaryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(code))
            {
                summaryParts.Add(code);
            }
            if (!string.IsNullOrWhiteSpace(message))
            {
                summaryParts.Add(message);
            }
            if (detailMessages.Count > 0)
            {
                summaryParts.Add(string.Join("; ", detailMessages));
            }

            if (summaryParts.Count == 0)
            {
                return null;
            }

            return "批量转写失败: " + string.Join(" | ", summaryParts);
        }

        private static List<SubtitleCue> ParseBatchTranscriptionToCues(string transcriptionJson)
        {
            var list = new List<SubtitleCue>();
            using var doc = JsonDocument.Parse(transcriptionJson);
            if (!doc.RootElement.TryGetProperty("recognizedPhrases", out var phrases) || phrases.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var phrase in phrases.EnumerateArray())
            {
                if (!TryParseBatchOffsetDuration(phrase, out var start, out var end))
                {
                    continue;
                }

                var text = ExtractPhraseText(phrase);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var speaker = phrase.TryGetProperty("speaker", out var speakerElement)
                    ? speakerElement.ToString()
                    : "";
                var speakerLabel = string.IsNullOrWhiteSpace(speaker) ? "Speaker" : $"Speaker {speaker}";

                list.Add(new SubtitleCue
                {
                    Start = start,
                    End = end,
                    Text = $"{speakerLabel}: {text}"
                });
            }

            return list.OrderBy(c => c.Start).ToList();
        }

        private static string ExtractPhraseText(JsonElement phrase)
        {
            if (phrase.TryGetProperty("nBest", out var nbest) && nbest.ValueKind == JsonValueKind.Array && nbest.GetArrayLength() > 0)
            {
                var first = nbest[0];
                if (first.TryGetProperty("display", out var displayElement))
                {
                    return displayElement.GetString() ?? "";
                }
                if (first.TryGetProperty("lexical", out var lexicalElement))
                {
                    return lexicalElement.GetString() ?? "";
                }
            }

            if (phrase.TryGetProperty("display", out var directDisplay))
            {
                return directDisplay.GetString() ?? "";
            }

            return "";
        }

        private static bool TryParseBatchOffsetDuration(JsonElement phrase, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            if (TryGetTimeValue(phrase, "offsetInTicks", out var offsetTicks) &&
                TryGetTimeValue(phrase, "durationInTicks", out var durationTicks))
            {
                start = offsetTicks;
                end = start + durationTicks;
                return true;
            }

            if (TryGetTimeValue(phrase, "offset", out var offset) &&
                TryGetTimeValue(phrase, "duration", out var duration))
            {
                start = offset;
                end = start + duration;
                return true;
            }

            return false;
        }

        private static bool TryGetTimeValue(JsonElement element, string propertyName, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (!element.TryGetProperty(propertyName, out var prop))
            {
                return false;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var ticks))
            {
                value = TimeSpan.FromTicks(Math.Max(0, ticks));
                return true;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                var text = prop.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                if (text.StartsWith("PT", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        value = XmlConvert.ToTimeSpan(text);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                if (TimeSpan.TryParse(text, out var parsed))
                {
                    value = parsed;
                    return true;
                }

                if (long.TryParse(text, out var parsedTicks))
                {
                    value = TimeSpan.FromTicks(Math.Max(0, parsedTicks));
                    return true;
                }
            }

            return false;
        }

        private void CancelSpeechSubtitle()
        {
            _speechSubtitleCts?.Cancel();
        }

        private static string GetSpeechSubtitlePath(string audioFilePath)
        {
            var directory = Path.GetDirectoryName(audioFilePath) ?? PathManager.Instance.SessionsPath;
            var baseName = Path.GetFileNameWithoutExtension(audioFilePath);
            return Path.Combine(directory, baseName + ".speech.vtt");
        }

        private async Task<List<SubtitleCue>> TranscribeSpeechToCuesAsync(string audioPath, CancellationToken token)
        {
            var subscription = _config.GetActiveSubscription();
            if (subscription == null || !subscription.IsValid())
            {
                throw new InvalidOperationException("语音订阅未配置");
            }

            if (!File.Exists(audioPath))
            {
                throw new FileNotFoundException("未找到音频文件", audioPath);
            }

            var speechConfig = SpeechConfig.FromSubscription(subscription.SubscriptionKey, subscription.ServiceRegion);
            speechConfig.SpeechRecognitionLanguage = _config.SourceLanguage;

            var cues = new List<SubtitleCue>();
            var cueLock = new object();
            var fallbackCursor = TimeSpan.Zero;
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var audioConfig = CreateTranscriptionAudioConfig(audioPath, token, out var feedTask);
            using var transcriber = new ConversationTranscriber(speechConfig, audioConfig);

            transcriber.Transcribed += (_, e) =>
            {
                if (e.Result.Reason != ResultReason.RecognizedSpeech)
                {
                    return;
                }

                var text = e.Result.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                var speakerId = string.IsNullOrWhiteSpace(e.Result.SpeakerId)
                    ? "Speaker"
                    : $"Speaker {e.Result.SpeakerId}";
                TimeSpan start;
                TimeSpan end;
                if (!TryGetTranscriptionTiming(e.Result, out start, out end))
                {
                    lock (cueLock)
                    {
                        start = fallbackCursor;
                        end = start + TimeSpan.FromSeconds(2);
                        fallbackCursor = end;
                    }
                }

                var cue = new SubtitleCue
                {
                    Start = start,
                    End = end,
                    Text = $"{speakerId}: {text}"
                };

                lock (cueLock)
                {
                    cues.Add(cue);
                }
            };

            transcriber.Canceled += (_, e) =>
            {
                completed.TrySetException(new InvalidOperationException($"转写取消: {e.Reason}, {e.ErrorDetails}"));
            };

            transcriber.SessionStopped += (_, _) => completed.TrySetResult(true);

            token.Register(() => completed.TrySetCanceled(token));

            await transcriber.StartTranscribingAsync();
            if (feedTask != null)
            {
                await feedTask;
            }

            try
            {
                await completed.Task;
            }
            finally
            {
                await transcriber.StopTranscribingAsync();
            }

            lock (cueLock)
            {
                return cues.OrderBy(c => c.Start).ToList();
            }
        }

        private static bool TryGetTranscriptionTiming(ConversationTranscriptionResult result, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            var json = result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (!TryReadOffsetDuration(doc.RootElement, out var offset, out var duration))
                {
                    return false;
                }

                start = TimeSpan.FromTicks(Math.Max(0, offset));
                end = start + TimeSpan.FromTicks(Math.Max(0, duration));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadOffsetDuration(System.Text.Json.JsonElement root, out long offset, out long duration)
        {
            offset = 0;
            duration = 0;

            if (root.TryGetProperty("Offset", out var offsetElement) &&
                root.TryGetProperty("Duration", out var durationElement) &&
                offsetElement.TryGetInt64(out offset) &&
                durationElement.TryGetInt64(out duration))
            {
                return true;
            }

            if (root.TryGetProperty("NBest", out var nbest) &&
                nbest.ValueKind == System.Text.Json.JsonValueKind.Array &&
                nbest.GetArrayLength() > 0)
            {
                var first = nbest[0];
                if (first.TryGetProperty("Offset", out var nbOffset) &&
                    first.TryGetProperty("Duration", out var nbDuration) &&
                    nbOffset.TryGetInt64(out offset) &&
                    nbDuration.TryGetInt64(out duration))
                {
                    return true;
                }
            }

            return false;
        }

        private static AudioConfig CreateTranscriptionAudioConfig(string audioPath, CancellationToken token, out Task? feedTask)
        {
            var streamFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
            var pushStream = AudioInputStream.CreatePushStream(streamFormat);
            var audioConfig = AudioConfig.FromStreamInput(pushStream);

            feedTask = Task.Run(() =>
            {
                try
                {
                    using var reader = new AudioFileReader(audioPath);
                    using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1))
                    {
                        ResamplerQuality = 60
                    };

                    var buffer = new byte[3200];
                    int read;
                    while (!token.IsCancellationRequested && (read = resampler.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        pushStream.Write(buffer, read);
                    }
                }
                finally
                {
                    pushStream.Close();
                }
            });

            return audioConfig;
        }

        private static void WriteVttFile(string outputPath, List<SubtitleCue> cues)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var writer = new StreamWriter(outputPath, false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine("WEBVTT");
            writer.WriteLine();

            var index = 1;
            foreach (var cue in cues)
            {
                writer.WriteLine(index++);
                writer.WriteLine($"{FormatVttTime(cue.Start)} --> {FormatVttTime(cue.End)}");
                writer.WriteLine(cue.Text);
                writer.WriteLine();
            }
        }

        private static string FormatVttTime(TimeSpan time)
        {
            return time.ToString(@"hh\:mm\:ss\.fff");
        }
    }
}
