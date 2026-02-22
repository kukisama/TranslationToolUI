using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Transcription;
using NAudio.Wave;
using TrueFluentPro.Models;

namespace TrueFluentPro.Services
{
    public static class RealtimeSpeechTranscriber
    {
        public static string GetSpeechSubtitlePath(string audioFilePath)
        {
            var directory = Path.GetDirectoryName(audioFilePath) ?? PathManager.Instance.SessionsPath;
            var baseName = Path.GetFileNameWithoutExtension(audioFilePath);
            return Path.Combine(directory, baseName + ".speech.vtt");
        }

        public static string GetTranscriptionSourceLanguage(string sourceLanguage)
        {
            if (string.Equals(sourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-CN";
            }

            return sourceLanguage;
        }

        public static async Task<List<SubtitleCue>> TranscribeSpeechToCuesAsync(
            string audioPath,
            AzureSubscription subscription,
            string sourceLanguage,
            CancellationToken token)
        {
            if (subscription == null || !subscription.IsValid())
            {
                throw new InvalidOperationException("语音订阅未配置");
            }

            if (!File.Exists(audioPath))
            {
                throw new FileNotFoundException("未找到音频文件", audioPath);
            }

            SpeechConfig speechConfig;
            if (subscription.IsChinaEndpoint)
            {
                var host = new Uri(subscription.GetCognitiveServicesHost());
                speechConfig = SpeechConfig.FromHost(host, subscription.SubscriptionKey);
            }
            else
            {
                speechConfig = SpeechConfig.FromSubscription(subscription.SubscriptionKey, subscription.ServiceRegion);
            }
            speechConfig.SpeechRecognitionLanguage = GetTranscriptionSourceLanguage(sourceLanguage);

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
    }
}
