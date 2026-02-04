using System.Text.Json.Serialization;

namespace TranslationToolUI.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AudioSourceMode
    {
        DefaultMic = 0,
        CaptureDevice = 1,
        Loopback = 2
    }
}
