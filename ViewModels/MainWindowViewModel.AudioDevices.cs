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
                    LogAudioModeSnapshot("音源模式已切换");

                    if (_translationService != null)
                    {
                        _ = _translationService.UpdateConfigAsync(_config);
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

        public bool IsInputDeviceUiEnabled => IsAudioDeviceSelectionEnabled;

        public bool IsOutputDeviceUiEnabled => IsOutputDeviceSelectionEnabled;

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

            var nextEnabled = device != null && !string.IsNullOrWhiteSpace(device.DeviceId);

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

            var deviceId = nextEnabled ? (device?.DeviceId ?? "") : "";
            var recognitionChanged = _config.UseInputForRecognition != nextEnabled;
            var deviceChanged = _config.SelectedAudioDeviceId != deviceId;

            if (recognitionChanged || deviceChanged)
            {
                _config.UseInputForRecognition = nextEnabled;
                _config.SelectedAudioDeviceId = deviceId;
                UpdateRecognitionModeFromToggles();

                QueueConfigSave("InputDeviceSelected");
            }
        }

        private void SetSelectedOutputDevice(AudioDeviceInfo? device, bool persistSelection)
        {
            AppendBatchDebugLog("OutputSelectChange",
                $"device='{device?.DisplayName ?? ""}' id='{device?.DeviceId ?? ""}' persist={persistSelection} suppress={_suppressDeviceSelectionPersistence}");

            var nextEnabled = device != null && !string.IsNullOrWhiteSpace(device.DeviceId);

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

            var deviceId = nextEnabled ? (device?.DeviceId ?? "") : "";
            var recognitionChanged = _config.UseOutputForRecognition != nextEnabled;
            var deviceChanged = _config.SelectedOutputDeviceId != deviceId;

            if (recognitionChanged || deviceChanged)
            {
                _config.UseOutputForRecognition = nextEnabled;
                _config.SelectedOutputDeviceId = deviceId;
                UpdateRecognitionModeFromToggles();

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
            LogAudioModeSnapshot("录制模式已切换");

            if (_translationService != null && !IsTranslating)
            {
                _ = _translationService.UpdateConfigAsync(_config);
            }
            else if (_translationService != null && IsTranslating)
            {
                var applied = _translationService.TryApplyLiveAudioRoutingFromCurrentConfig();
                StatusMessage = applied
                    ? $"录制模式已切换为{(mode == RecordingMode.LoopbackOnly ? "仅环回" : "环回+麦克风")}（已实时生效）"
                    : $"录制模式已切换为{(mode == RecordingMode.LoopbackOnly ? "仅环回" : "环回+麦克风")}，将在下次启动翻译时生效";
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

            _config.UseInputForRecognition = enabled;
            UpdateRecognitionModeFromToggles();
            LogAudioModeSnapshot("输入识别开关");
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

            _config.UseOutputForRecognition = enabled;
            UpdateRecognitionModeFromToggles();
            LogAudioModeSnapshot("输出识别开关");
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
                if (IsTranslating)
                {
                    _translationService.TryApplyLiveAudioRoutingFromCurrentConfig();
                }
                else
                {
                    _ = _translationService.UpdateConfigAsync(_config);
                }
            }

            LogAudioModeSnapshot("识别路由已更新");

            OnPropertyChanged(nameof(AudioSourceModeIndex));
        }

        private void LogAudioModeSnapshot(string eventName)
        {
            AppendBatchDebugLog(eventName,
                $"音源模式={FormatAudioSourceMode(_config.AudioSourceMode)} 输入识别={(_config.UseInputForRecognition ? "开" : "关")} 输出识别={(_config.UseOutputForRecognition ? "开" : "关")} " +
                $"录制模式={FormatRecordingMode(_config.RecordingMode)} 翻译中={IsTranslating} " +
                $"输入设备ID='{_config.SelectedAudioDeviceId}' 输出设备ID='{_config.SelectedOutputDeviceId}'");
        }

        private static string FormatAudioSourceMode(AudioSourceMode mode)
        {
            return mode switch
            {
                AudioSourceMode.DefaultMic => "默认麦克风",
                AudioSourceMode.CaptureDevice => "选择输入设备",
                AudioSourceMode.Loopback => "系统回环",
                _ => mode.ToString()
            };
        }

        private static string FormatRecordingMode(RecordingMode mode)
        {
            return mode switch
            {
                RecordingMode.LoopbackOnly => "仅环回",
                RecordingMode.LoopbackWithMic => "环回+麦克风",
                _ => mode.ToString()
            };
        }

        private void NormalizeRecognitionToggles()
        {
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
                _audioDevices.Add(new AudioDeviceInfo
                {
                    DeviceId = "",
                    DisplayName = "无",
                    DeviceType = AudioDeviceType.Capture
                });
                foreach (var device in inputDevices)
                {
                    _audioDevices.Add(device);
                }

                var outputDevices = AudioDeviceEnumerator.GetActiveDevices(AudioDeviceType.Render);
                _outputDevices.Add(new AudioDeviceInfo
                {
                    DeviceId = "",
                    DisplayName = "无",
                    DeviceType = AudioDeviceType.Render
                });
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
                if (!string.IsNullOrWhiteSpace(targetInputId)
                    && _audioDevices.All(d => d.DeviceId != targetInputId))
                {
                    targetInputId = AudioDeviceEnumerator.GetDefaultDeviceId(AudioDeviceType.Capture) ?? "";
                    if (string.IsNullOrWhiteSpace(targetInputId))
                    {
                        targetInputId = _audioDevices.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.DeviceId))?.DeviceId ?? "";
                    }
                }

                var inputSelection = _audioDevices.FirstOrDefault(d => d.DeviceId == targetInputId)
                    ?? _audioDevices.FirstOrDefault();
                SetSelectedAudioDevice(inputSelection, persistSelection: false);

                var targetOutputId = currentOutputId;
                if (!string.IsNullOrWhiteSpace(targetOutputId)
                    && _outputDevices.All(d => d.DeviceId != targetOutputId))
                {
                    targetOutputId = AudioDeviceEnumerator.GetDefaultDeviceId(AudioDeviceType.Render) ?? "";
                    if (string.IsNullOrWhiteSpace(targetOutputId))
                    {
                        targetOutputId = _outputDevices.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.DeviceId))?.DeviceId ?? "";
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
