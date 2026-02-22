using System.Text.Json.Serialization;

namespace TrueFluentPro.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AudioDeviceType
    {
        Capture = 0,
        Render = 1
    }
}
