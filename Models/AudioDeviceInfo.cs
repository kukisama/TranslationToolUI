namespace TrueFluentPro.Models
{
    public class AudioDeviceInfo
    {
        public string DeviceId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public AudioDeviceType DeviceType { get; set; } = AudioDeviceType.Capture;

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
