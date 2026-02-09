using Avalonia.Controls;

namespace TranslationToolUI.ViewModels
{
    public partial class MainWindowViewModel
    {
        public void SetMainWindow(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void Dispose()
        {
            if (_translationService != null)
            {
                _translationService.OnRealtimeTranslationReceived -= OnRealtimeTranslationReceived;
                _translationService.OnFinalTranslationReceived -= OnFinalTranslationReceived;
                _translationService.OnStatusChanged -= OnStatusChanged;
                _translationService.OnReconnectTriggered -= OnReconnectTriggered;
                _translationService.OnAudioLevelUpdated -= OnAudioLevelUpdated;
            }

            _floatingSubtitleManager?.Dispose();

            _insightCts?.Cancel();
            _insightCts?.Dispose();
            _autoInsightTimer?.Stop();
        }
    }
}
