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
        public ObservableCollection<AudioDeviceInfo> AudioDevices => _audioDevices;

        public ObservableCollection<double> AudioLevelHistory => _audioLevelHistory;

        public double AudioLevel
        {
            get => _audioLevel;
            private set => SetProperty(ref _audioLevel, value);
        }

        public AudioDeviceInfo? SelectedAudioDevice
        {
            get => _selectedAudioDevice;
            set => SetSelectedAudioDevice(value, persistSelection: true);
        }

        public bool IsAudioSourceSelectionEnabled => OperatingSystem.IsWindows() && !IsTranslating;

        public bool IsAudioDeviceSelectionEnabled
        {
            get => _isAudioDeviceSelectionEnabled;
            private set => SetProperty(ref _isAudioDeviceSelectionEnabled, value);
        }

        public bool IsAudioDeviceRefreshEnabled
        {
            get => _isAudioDeviceRefreshEnabled;
            private set => SetProperty(ref _isAudioDeviceRefreshEnabled, value);
        }

        private void SetSelectedAudioDevice(AudioDeviceInfo? device, bool persistSelection)
        {
            if (!SetProperty(ref _selectedAudioDevice, device))
            {
                return;
            }

            var deviceId = device?.DeviceId ?? "";
            if (_config.SelectedAudioDeviceId != deviceId)
            {
                _config.SelectedAudioDeviceId = deviceId;

                if (_translationService != null)
                {
                    _translationService.UpdateConfig(_config);
                }

                if (persistSelection)
                {
                    _ = Task.Run(async () => await _configService.SaveConfigAsync(_config));
                }
            }
        }

        private void RefreshAudioDevices(bool persistSelection)
        {
            if (IsTranslating)
            {
                IsAudioDeviceSelectionEnabled = false;
                IsAudioDeviceRefreshEnabled = false;
                return;
            }

            _audioDevices.Clear();

            if (!OperatingSystem.IsWindows())
            {
                IsAudioDeviceSelectionEnabled = false;
                IsAudioDeviceRefreshEnabled = false;
                SetSelectedAudioDevice(null, persistSelection: false);
                return;
            }

            var mode = _config.AudioSourceMode;
            if (mode == AudioSourceMode.DefaultMic)
            {
                IsAudioDeviceSelectionEnabled = false;
                IsAudioDeviceRefreshEnabled = false;
                SetSelectedAudioDevice(null, persistSelection: false);
                return;
            }

            var deviceType = mode == AudioSourceMode.Loopback ? AudioDeviceType.Render : AudioDeviceType.Capture;
            var devices = AudioDeviceEnumerator.GetActiveDevices(deviceType);
            foreach (var device in devices)
            {
                _audioDevices.Add(device);
            }

            IsAudioDeviceRefreshEnabled = true;
            IsAudioDeviceSelectionEnabled = _audioDevices.Count > 0;

            AudioDeviceInfo? selection = null;
            var targetDeviceId = _config.SelectedAudioDeviceId;

            if (string.IsNullOrWhiteSpace(targetDeviceId))
            {
                targetDeviceId = AudioDeviceEnumerator.GetDefaultDeviceId(deviceType) ?? "";
            }

            if (!string.IsNullOrWhiteSpace(targetDeviceId))
            {
                selection = _audioDevices.FirstOrDefault(d => d.DeviceId == targetDeviceId);
            }

            selection ??= _audioDevices.FirstOrDefault();
            SetSelectedAudioDevice(selection, persistSelection);
        }

        private void OnAudioLevelUpdated(object? sender, double level)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AudioLevel = level;
                PushAudioLevelSample(level);
            });
        }

        private void PushAudioLevelSample(double level)
        {
            if (_audioLevelHistory.Count == 0)
            {
                return;
            }

            _audioLevelHistory.RemoveAt(0);
            _audioLevelHistory.Add(level);
        }

        private void ResetAudioLevelHistory()
        {
            _audioLevelHistory.Clear();
            for (var i = 0; i < 24; i++)
            {
                _audioLevelHistory.Add(0);
            }
        }
    }
}
