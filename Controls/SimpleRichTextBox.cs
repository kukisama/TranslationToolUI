using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.ComponentModel;

namespace TrueFluentPro.Controls
{
    public class SimpleRichTextBox : UserControl, INotifyPropertyChanged
    {
        private TextBox _textBox = null!;

        public new event PropertyChangedEventHandler? PropertyChanged;
 
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<SimpleRichTextBox, string>(nameof(Text), "");

        public static readonly StyledProperty<string> PlaceholderProperty =
            AvaloniaProperty.Register<SimpleRichTextBox, string>(nameof(Placeholder), "");

        public SimpleRichTextBox()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {            _textBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Background = Brushes.White,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2),
                MinHeight = 100,
                FontSize = 14
            };

            _textBox.Bind(TextBox.WatermarkProperty, this.GetObservable(PlaceholderProperty));
            _textBox.TextChanged += OnTextChanged;

            Content = _textBox;
        }        private void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            var newText = _textBox.Text ?? "";
            if (Text != newText)
            {
                SetValue(TextProperty, newText);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            
            if (change.Property == TextProperty)
            {
                var newText = change.NewValue?.ToString() ?? "";
                if (_textBox.Text != newText)
                {
                    _textBox.Text = newText;
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }

        public string Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public string Placeholder
        {
            get => GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }
    }
}

