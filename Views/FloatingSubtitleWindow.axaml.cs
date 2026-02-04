using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TranslationToolUI.ViewModels;

namespace TranslationToolUI.Views
{    public partial class FloatingSubtitleWindow : Window
    {
        public FloatingSubtitleWindow()
        {
            InitializeComponent();
            
            SetInitialPosition();
        }

        public FloatingSubtitleWindow(FloatingSubtitleViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }        private void SetInitialPosition()
        {
            if (Screens.Primary != null)
            {
                var screen = Screens.Primary.WorkingArea;
                var windowWidth = 800;
                var windowHeight = 120;
                
                Position = new PixelPoint(
                    (int)(screen.X + (screen.Width - windowWidth) / 2),
                    (int)(screen.Y + screen.Height - windowHeight - 50)
                );
            }
        }private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                this.BeginMoveDrag(e);
                e.Handled = true;
            }
            
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                var viewModel = DataContext as FloatingSubtitleViewModel;
                viewModel?.ToggleTransparency();
                e.Handled = true;
            }
        }private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            if (DataContext is FloatingSubtitleViewModel viewModel)
            {
                viewModel.OnWindowClosed();
            }
        }
    }
}

