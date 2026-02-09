using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using TranslationToolUI.Services;

namespace TranslationToolUI.Models
{
    public enum BatchLogLevel
    {
        Off,
        FailuresOnly,
        SuccessAndFailure
    }

    public enum AutoGainPreset
    {
        Off,
        Low,
        Medium,
        High
    }

    public class AzureSubscription
    {
        public string Name { get; set; } = "";
        public string SubscriptionKey { get; set; } = "";
        public string ServiceRegion { get; set; } = "southeastasia";

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(SubscriptionKey) &&
                   !string.IsNullOrEmpty(ServiceRegion);
        }
    }

    public class AzureSpeechConfig
    {
        public List<AzureSubscription> Subscriptions { get; set; } = new();
        public int ActiveSubscriptionIndex { get; set; } = 0;

        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "zh-CN";

        public bool FilterModalParticles { get; set; } = true;
        public int MaxHistoryItems { get; set; } = 15;
        public int RealtimeMaxLength { get; set; } = 150;
        public bool EnableAutoTimeout { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 5;
        public int InitialSilenceTimeoutSeconds { get; set; } = 25;
        public int EndSilenceTimeoutSeconds { get; set; } = 1;

        public bool EnableNoResponseRestart { get; set; } = false;
        public int NoResponseRestartSeconds { get; set; } = 3;
        public bool ShowReconnectMarkerInSubtitle { get; set; } = true;
        public int AudioActivityThreshold { get; set; } = 600;
        public double AudioLevelGain { get; set; } = 2.0;
        public bool AutoGainEnabled { get; set; } = false;
        public AutoGainPreset AutoGainPreset { get; set; } = AutoGainPreset.Off;
        public double AutoGainTargetRms { get; set; } = 0.12;
        public double AutoGainMinGain { get; set; } = 0.5;
        public double AutoGainMaxGain { get; set; } = 6.0;
        public double AutoGainSmoothing { get; set; } = 0.08;

        public AudioSourceMode AudioSourceMode { get; set; } = AudioSourceMode.DefaultMic;
        public string SelectedAudioDeviceId { get; set; } = "";
        public string SelectedOutputDeviceId { get; set; } = "";
        public RecordingMode RecordingMode { get; set; } = RecordingMode.LoopbackWithMic;
        public bool UseInputForRecognition { get; set; } = true;
        public bool UseOutputForRecognition { get; set; } = false;

        public int ChunkDurationMs { get; set; } = 200;

        public bool EnableRecording { get; set; } = true;
        public int RecordingMp3BitrateKbps { get; set; } = 256;
        public bool DeleteWavAfterMp3 { get; set; } = true;

        public bool ExportSrtSubtitles { get; set; } = false;
        public bool ExportVttSubtitles { get; set; } = false;

        public string? SessionDirectoryOverride { get; set; }

        public const string DefaultBatchAudioContainerName = "translationtoolui-audio";
        public const string DefaultBatchResultContainerName = "translationtoolui-results";

        public string BatchStorageConnectionString { get; set; } = "";
        public bool BatchStorageIsValid { get; set; } = false;
        public string BatchAudioContainerName { get; set; } = DefaultBatchAudioContainerName;
        public string BatchResultContainerName { get; set; } = DefaultBatchResultContainerName;
        public bool UseSpeechSubtitleForReview { get; set; } = true;
        public BatchLogLevel BatchLogLevel { get; set; } = BatchLogLevel.Off;

        public AiConfig? AiConfig { get; set; }

        [JsonIgnore]
        public string SessionDirectory => string.IsNullOrWhiteSpace(SessionDirectoryOverride)
            ? PathManager.Instance.SessionsPath
            : SessionDirectoryOverride;

        public AzureSpeechConfig()
        {
        }

        public AzureSubscription? GetActiveSubscription()
        {
            if (Subscriptions.Count == 0) return null;

            if (ActiveSubscriptionIndex < 0 || ActiveSubscriptionIndex >= Subscriptions.Count)
            {
                ActiveSubscriptionIndex = 0;
            }

            return Subscriptions[ActiveSubscriptionIndex];
        }

        [JsonIgnore]
        public string SubscriptionKey
        {
            get => GetActiveSubscription()?.SubscriptionKey ?? "";
        }

        [JsonIgnore]
        public string ServiceRegion
        {
            get => GetActiveSubscription()?.ServiceRegion ?? "southeastasia";
        }

        public bool IsValid()
        {
            var activeSubscription = GetActiveSubscription();
            return activeSubscription?.IsValid() == true &&
                   !string.IsNullOrEmpty(SourceLanguage) &&
                   !string.IsNullOrEmpty(TargetLanguage);
        }
    }
}
