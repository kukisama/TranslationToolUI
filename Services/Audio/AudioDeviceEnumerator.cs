using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services.Audio
{
    public static class AudioDeviceEnumerator
    {
        public static IReadOnlyList<AudioDeviceInfo> GetActiveDevices(AudioDeviceType deviceType)
        {
            if (!OperatingSystem.IsWindows())
            {
                return Array.Empty<AudioDeviceInfo>();
            }

            var dataFlow = deviceType == AudioDeviceType.Render ? DataFlow.Render : DataFlow.Capture;
            var list = new List<AudioDeviceInfo>();

            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active))
            {
                list.Add(new AudioDeviceInfo
                {
                    DeviceId = device.ID,
                    DisplayName = device.FriendlyName,
                    DeviceType = deviceType
                });
            }

            return list;
        }

        public static string? GetDefaultDeviceId(AudioDeviceType deviceType)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var dataFlow = deviceType == AudioDeviceType.Render ? DataFlow.Render : DataFlow.Capture;

            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
            return device?.ID;
        }

        public static string GetDisplayName(string deviceId)
        {
            if (!OperatingSystem.IsWindows())
            {
                return "";
            }

            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(deviceId);
            return device?.FriendlyName ?? "";
        }
    }
}
