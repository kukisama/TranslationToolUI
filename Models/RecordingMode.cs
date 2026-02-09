using System.Text.Json.Serialization;

namespace TranslationToolUI.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecordingMode
    {
        LoopbackOnly = 0,
        LoopbackWithMic = 1
    }
}
