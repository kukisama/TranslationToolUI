using System;
using TranslationToolUI.Views;
using TranslationToolUI.ViewModels;
using TranslationToolUI.Services;

namespace TranslationToolUI.Services
{
    public class FloatingSubtitleManager
    {
        private FloatingSubtitleWindow? _window;
        private FloatingSubtitleViewModel? _viewModel;
        private SubtitleSyncService? _syncService;
        private bool _isWindowOpen = false;

        public bool IsWindowOpen => _isWindowOpen;

        public void ToggleWindow()
        {
            if (_isWindowOpen)
            {
                CloseWindow();
            }
            else
            {
                OpenWindow();
            }
        }

        public void OpenWindow()
        {
            if (_isWindowOpen) return;

            try
            {
                _syncService = new SubtitleSyncService();

                _viewModel = new FloatingSubtitleViewModel(_syncService);

                _window = new FloatingSubtitleWindow(_viewModel);

                _window.Closed += OnWindowClosed;

                _window.Show();
                _isWindowOpen = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open floating subtitle window: {ex.Message}");
            }
        }

        public void CloseWindow()
        {
            if (!_isWindowOpen || _window == null) return;

            try
            {
                _window.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to close floating subtitle window: {ex.Message}");
            }
        }

        public void UpdateSubtitle(string subtitle)
        {
            if (_isWindowOpen && _syncService != null)
            {
                _syncService.UpdateSubtitle(subtitle);
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _isWindowOpen = false;

            if (_window != null)
            {
                _window.Closed -= OnWindowClosed;
                _window = null;
            }

            _viewModel = null;
            _syncService = null;
        }

        public void Dispose()
        {
            CloseWindow();
        }
    }
}

