using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services
{
    public static class BlobStorageService
    {
        public static async Task<(BlobContainerClient Audio, BlobContainerClient Result)> GetBatchContainersAsync(
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

        public static string NormalizeContainerName(string? name, string fallback)
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

        public static async Task<BlobClient> UploadAudioToBlobAsync(
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

        public static Uri CreateBlobReadSasUri(BlobClient blobClient, TimeSpan validFor)
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

        public static string GetAudioContentType(string audioPath)
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

        public static async Task UploadTextToBlobAsync(
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

        public static void WriteVttFile(string outputPath, List<SubtitleCue> cues)
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

        public static string FormatVttTime(TimeSpan time)
        {
            return time.ToString(@"hh\:mm\:ss\.fff");
        }
    }
}
