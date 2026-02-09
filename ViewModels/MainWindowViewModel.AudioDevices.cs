using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using TranslationToolUI.Models;
using TranslationToolUI.Services.Audio;

namespace TranslationToolUI.ViewModels
{
    public partial class MainWindowViewModel
    {
        private static int AudioSourceModeToIndex(AudioSourceMode mode)
        {
            return mode switch
            {
                AudioSourceMode.DefaultMic => 0,
                AudioSourceMode.CaptureDevice => 1,
                AudioSourceMode.Loopback => 2,
                _ => 0
            };
        }

        private static AudioSourceMode IndexToAudioSourceMode(int index)
        {
            return index switch
            {
                1 => AudioSourceMode.CaptureDevice,
                2 => AudioSourceMode.Loopback,
                _ => AudioSourceMode.DefaultMic
            };
        }

        public int AudioSourceModeIndex
        {
            get => _audioSourceModeIndex;
            set
            {
                if (!SetProperty(ref _audioSourceModeIndex, value))
                {
                    return;
                }

                if (!OperatingSystem.IsWindows())
                {
                    if (_config.AudioSourceMode != AudioSourceMode.DefaultMic)
                    {
                        _config.AudioSourceMode = AudioSourceMode.DefaultMic;
                        _audioSourceModeIndex = 0;
                        OnPropertyChanged(nameof(AudioSourceModeIndex));
                    }

                    RefreshAudioDevices(persistSelection: false);
                    return;
                }

                var mode = IndexToAudioSourceMode(value);
                if (_config.AudioSourceMode != mode)
                {
                    _config.AudioSourceMode = mode;

                    if (_translationService != null)
                    {
                        _translationService.UpdateConfig(_config);
                    }

                    _ = Task.Run(async () => await _configService.SaveConfigAsync(_config));
                }

                RefreshAudioDevices(persistSelection: true);
            }
        }

        public ObservableCollection<AudioDeviceInfo> AudioDevices => _audioDevices;

        public ObservableCollection<AudioDeviceInfo> OutputDevices => _outputDevices;

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

        public AudioDeviceInfo? SelectedOutputDevice
        {
            get => _selectedOutputDevice;
            set => SetSelectedOutputDevice(value, persistSelection: true);
        }

        public bool IsAudioSourceSelectionEnabled => OperatingSystem.IsWindows() && !IsTranslating;

        public bool IsAudioDeviceSelectionEnabled
        {
            get => _isAudioDeviceSelectionEnabled;
            private set => SetProperty(ref _isAudioDeviceSelectionEnabled, value);
        }

        public bool IsOutputDeviceSelectionEnabled
        {
            get => _isOutputDeviceSelectionEnabled;
            private set => SetProperty(ref _isOutputDeviceSelectionEnabled, value);
        }

        public bool IsInputRecognitionEnabled
        {
            get => _config.UseInputForRecognition;
            set => SetInputRecognitionEnabled(value);
        }

        public bool IsOutputRecognitionEnabled
        {
            get => _config.UseOutputForRecognition;
            set => SetOutputRecognitionEnabled(value);
        }

        public bool IsInputDeviceUiEnabled => IsAudioDeviceSelectionEnabled && IsInputRecognitionEnabled;

        public bool IsOutputDeviceUiEnabled => IsOutputDeviceSelectionEnabled && IsOutputRecognitionEnabled;

        public bool IsAudioDeviceRefreshEnabled
        {
            get => _isAudioDeviceRefreshEnabled;
            private set => SetProperty(ref _isAudioDeviceRefreshEnabled, value);
        }

        public bool IsRecordingLoopbackOnly
        {
            get => _config.RecordingMode == RecordingMode.LoopbackOnly;
            set
            {
                if (value)
                {
                    SetRecordingMode(RecordingMode.LoopbackOnly);
                }
            }
        }

        public bool IsRecordingLoopbackMix
        {
            get => _config.RecordingMode == RecordingMode.LoopbackWithMic;
            set
            {
                if (value)
                {
                    SetRecordingMode(RecordingMode.LoopbackWithMic);
                }
            }
        }

        private void SetSelectedAudioDevice(AudioDeviceInfo? device, bool persistSelection)
        {
            AppendBatchDebugLog("InputSelectChange",
                $"device='{device?.DisplayName ?? ""}' id='{device?.DeviceId ?? ""}' persist={persistSelection} suppress={_suppressDeviceSelectionPersistence}");

            if (_suppressDeviceSelectionPersistence && device == null)
            {
                return;
            }

            if (device == null && _audioDevices.Count > 0)
            {
                return;
            }

            if (!SetProperty(ref _selectedAudioDevice, device))
            {
                return;
            }

            if (!persistSelection || _suppressDeviceSelectionPersistence)
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
                    QueueConfigSave("InputDeviceSelected");
                }
            }
        }

        private void SetSelectedOutputDevice(AudioDeviceInfo? device, bool persistSelection)
        {
            AppendBatchDebugLog("OutputSelectChange",
                $"device='{device?.DisplayName ?? ""}' id='{device?.DeviceId ?? ""}' persist={persistSelection} suppress={_suppressDeviceSelectionPersistence}");

            if (_suppressDeviceSelectionPersistence && device == null)
            {
                return;
            }

            if (device == null && _outputDevices.Count > 0)
            {
                return;
            }

            if (!SetProperty(ref _selectedOutputDevice, device))
            {
                return;
            }

            if (!persistSelection || _suppressDeviceSelectionPersistence)
            {
                return;
            }

            var deviceId = device?.DeviceId ?? "";
            if (_config.SelectedOutputDeviceId != deviceId)
            {
                _config.SelectedOutputDeviceId = deviceId;

                if (_translationService != null)
                {
                    _translationService.UpdateConfig(_config);
                }

                QueueConfigSave("OutputDeviceSelected");
            }
        }

        private void SetRecordingMode(RecordingMode mode)
        {
            if (_config.RecordingMode == mode)
            {
                return;
            }

            _config.RecordingMode = mode;

            if (_translationService != null)
            {
                _translationService.UpdateConfig(_config);
            }

            _ = Task.Run(async () => await _configService.SaveConfigAsync(_config));
            OnPropertyChanged(nameof(IsRecordingLoopbackOnly));
            OnPropertyChanged(nameof(IsRecordingLoopbackMix));
        }

        private void SetInputRecognitionEnabled(bool enabled)
        {
            if (_config.UseInputForRecognition == enabled)
            {
                return;
            }

            if (!enabled && !_config.UseOutputForRecognition)
            {
                StatusMessage = "输入与输出不能同时关闭";
                OnPropertyChanged(nameof(IsInputRecognitionEnabled));
                return;
            }

            _config.UseInputForRecognition = enabled;
            UpdateRecognitionModeFromToggles();
            QueueConfigSave("InputRecognitionToggle");
            OnPropertyChanged(nameof(IsInputRecognitionEnabled));
            OnPropertyChanged(nameof(IsInputDeviceUiEnabled));
            OnPropertyChanged(nameof(IsOutputDeviceUiEnabled));
        }

        private void SetOutputRecognitionEnabled(bool enabled)
        {
            if (_config.UseOutputForRecognition == enabled)
            {
                return;
            }

            if (!enabled && !_config.UseInputForRecognition)
            {
                StatusMessage = "输入与输出不能同时关闭";
                OnPropertyChanged(nameof(IsOutputRecognitionEnabled));
                return;
            }

            _config.UseOutputForRecognition = enabled;
            UpdateRecognitionModeFromToggles();
            QueueConfigSave("OutputRecognitionToggle");
            OnPropertyChanged(nameof(IsOutputRecognitionEnabled));
            OnPropertyChanged(nameof(IsInputDeviceUiEnabled));
            OnPropertyChanged(nameof(IsOutputDeviceUiEnabled));
        }

        private void UpdateRecognitionModeFromToggles()
        {
            if (_config.UseOutputForRecognition && !_config.UseInputForRecognition)
            {
                _config.AudioSourceMode = AudioSourceMode.Loopback;
            }
            else
            {
                _config.AudioSourceMode = AudioSourceMode.CaptureDevice;
            }

            _audioSourceModeIndex = AudioSourceModeToIndex(_config.AudioSourceMode);

            if (_translationService != null)
            {
                _translationService.UpdateConfig(_config);
            }

            OnPropertyChanged(nameof(AudioSourceModeIndex));
        }

        private void NormalizeRecognitionToggles()
        {
            if (!_config.UseInputForRecognition && !_config.UseOutputForRecognition)
            {
                _config.UseInputForRecognition = true;
            }

            UpdateRecognitionModeFromToggles();
        }

        private void RefreshAudioDevices(bool persistSelection)
        {
            if (IsTranslating)
            {
                IsAudioDeviceSelectionEnabled = false;
                IsAudioDeviceRefreshEnabled = false;
                IsOutputDeviceSelectionEnabled = false;
                return;
            }

            var currentInputId = _selectedAudioDevice?.DeviceId ?? _config.SelectedAudioDeviceId;
            var currentOutputId = _selectedOutputDevice?.DeviceId ?? _config.SelectedOutputDeviceId;

            AppendBatchDebugLog("DeviceRefreshStart",
                $"persist={persistSelection} inputId='{currentInputId}' outputId='{currentOutputId}' " +
                $"inputName='{_selectedAudioDevice?.DisplayName ?? ""}' outputName='{_selectedOutputDevice?.DisplayName ?? ""}'");

            _suppressDeviceSelectionPersistence = true;
            try
            {
                _audioDevices.Clear();
                _outputDevices.Clear();

                if (!OperatingSystem.IsWindows())
                {
                    IsAudioDeviceSelectionEnabled = false;
                    IsAudioDeviceRefreshEnabled = false;
                    IsOutputDeviceSelectionEnabled = false;
                    SetSelectedAudioDevice(null, persistSelection: false);
                    SetSelectedOutputDevice(null, persistSelection: false);
                    return;
                }

                var inputDevices = AudioDeviceEnumerator.GetActiveDevices(AudioDeviceType.Capture);
                foreach (var device in inputDevices)
                {
                    _audioDevices.Add(device);
                }

                var outputDevices = AudioDeviceEnumerator.GetActiveDevices(AudioDeviceType.Render);
                foreach (var device in outputDevices)
                {
                    _outputDevices.Add(device);
                }

                var inputPreview = string.Join(" | ", _audioDevices.Take(5)
                    .Select(d => $"{d.DisplayName} ({d.DeviceId})"));
                var outputPreview = string.Join(" | ", _outputDevices.Take(5)
                    .Select(d => $"{d.DisplayName} ({d.DeviceId})"));
                AppendBatchDebugLog("DeviceRefreshList",
                    $"inputs={_audioDevices.Count} outputs={_outputDevices.Count} " +
                    $"inputPreview='{inputPreview}' outputPreview='{outputPreview}'");

                IsAudioDeviceRefreshEnabled = true;
                IsAudioDeviceSelectionEnabled = _audioDevices.Count > 0;
                IsOutputDeviceSelectionEnabled = _outputDevices.Count > 0;
                OnPropertyChanged(nameof(IsInputDeviceUiEnabled));
                OnPropertyChanged(nameof(IsOutputDeviceUiEnabled));

                var targetInputId = currentInputId;
                if (string.IsNullOrWhiteSpace(targetInputId)
                    || _audioDevices.All(d => d.DeviceId != targetInputId))
                {
                    targetInputId = AudioDeviceEnumerator.GetDefaultDeviceId(AudioDeviceType.Capture) ?? "";
                    if (string.IsNullOrWhiteSpace(targetInputId))
                    {
                        targetInputId = _audioDevices.FirstOrDefault()?.DeviceId ?? "";
                    }
                }

                var inputSelection = _audioDevices.FirstOrDefault(d => d.DeviceId == targetInputId)
                    ?? _audioDevices.FirstOrDefault();
                SetSelectedAudioDevice(inputSelection, persistSelection: false);

                var targetOutputId = currentOutputId;
                if (string.IsNullOrWhiteSpace(targetOutputId)
                    || _outputDevices.All(d => d.DeviceId != targetOutputId))
                {
                    targetOutputId = AudioDeviceEnumerator.GetDefaultDeviceId(AudioDeviceType.Render) ?? "";
                    if (string.IsNullOrWhiteSpace(targetOutputId))
                    {
                        targetOutputId = _outputDevices.FirstOrDefault()?.DeviceId ?? "";
                    }
                }

                var outputSelection = _outputDevices.FirstOrDefault(d => d.DeviceId == targetOutputId)
                    ?? _outputDevices.FirstOrDefault();
                SetSelectedOutputDevice(outputSelection, persistSelection: false);

                if (string.IsNullOrWhiteSpace(_config.SelectedAudioDeviceId)
                    && _selectedAudioDevice != null)
                {
                    _config.SelectedAudioDeviceId = _selectedAudioDevice.DeviceId;
                    QueueConfigSave("InputDevicePersistDefault");
                    AppendBatchDebugLog("DevicePersist",
                        $"inputId='{_config.SelectedAudioDeviceId}' inputName='{_selectedAudioDevice.DisplayName}'");
                }

                if (string.IsNullOrWhiteSpace(_config.SelectedOutputDeviceId)
                    && _selectedOutputDevice != null)
                {
                    _config.SelectedOutputDeviceId = _selectedOutputDevice.DeviceId;
                    QueueConfigSave("OutputDevicePersistDefault");
                    AppendBatchDebugLog("DevicePersist",
                        $"outputId='{_config.SelectedOutputDeviceId}' outputName='{_selectedOutputDevice.DisplayName}'");
                }

                AppendBatchDebugLog("DeviceRefreshSelect",
                    $"inputSelected='{_selectedAudioDevice?.DisplayName ?? ""}' ({_selectedAudioDevice?.DeviceId ?? ""}) " +
                    $"outputSelected='{_selectedOutputDevice?.DisplayName ?? ""}' ({_selectedOutputDevice?.DeviceId ?? ""})");

                ForceUpdateDeviceComboBoxSelection();
            }
            finally
            {
                _suppressDeviceSelectionPersistence = false;
            }
        }

        private void ForceUpdateDeviceComboBoxSelection()
        {
            if (_mainWindow == null)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                _suppressDeviceSelectionPersistence = true;
                try
                {
                    var inputCombo = _mainWindow.FindControl<ComboBox>("InputDeviceComboBox");
                    if (inputCombo != null)
                    {
                        if (_selectedAudioDevice != null)
                        {
                            inputCombo.SelectedItem = _selectedAudioDevice;
                        }
                        else if (_audioDevices.Count > 0)
                        {
                            inputCombo.SelectedItem = _audioDevices[0];
                        }
                    }

                    var outputCombo = _mainWindow.FindControl<ComboBox>("OutputDeviceComboBox");
                    if (outputCombo != null)
                    {
                        if (_selectedOutputDevice != null)
                        {
                            outputCombo.SelectedItem = _selectedOutputDevice;
                        }
                        else if (_outputDevices.Count > 0)
                        {
                            outputCombo.SelectedItem = _outputDevices[0];
                        }
                    }
                }
                finally
                {
                    _suppressDeviceSelectionPersistence = false;
                }
            });
        }

        private void OnAudioLevelUpdated(object? sender, double level)
        {
            Dispatcher.UIThread.Post(() =>
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
