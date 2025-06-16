using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System.ComponentModel;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Search;
using AvaloniaEdit.Document;
using System;
using System.Linq;

namespace TranslationToolUI.Controls
{
    public class AdvancedRichTextBox : UserControl, INotifyPropertyChanged
    {
        private readonly string _editorName;
        private static int _instanceCounter = 0;
        private TextEditor _textEditor = null!;
        private StackPanel _toolbar = null!;
   
        private SearchPanel? _searchPanel;
        private ComboBox _fontSizeCombo = null!;
        private ToggleButton _lineNumbersButton = null!;
        private ToggleButton _wordWrapButton = null!;
       /* private Button _searchButton = null!; */
        private ComboBox _syntaxCombo = null!;

        public new event PropertyChangedEventHandler? PropertyChanged;

        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<AdvancedRichTextBox, string>(nameof(Text), "");

        public static readonly StyledProperty<string> PlaceholderProperty =
            AvaloniaProperty.Register<AdvancedRichTextBox, string>(nameof(Placeholder), "");
        public AdvancedRichTextBox()
        {
            _instanceCounter++;
            _editorName = $"AdvancedEditor_{_instanceCounter}";
            System.Diagnostics.Debug.WriteLine($"创建新的AdvancedRichTextBox实例: {_editorName}");

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            var dockPanel = new DockPanel();

            CreateToolbar();

            // 直接设置工具栏为顶部停靠，无需标题栏
            DockPanel.SetDock(_toolbar, Dock.Top);
            dockPanel.Children.Add(_toolbar);

            _textEditor = new TextEditor
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                FontFamily = new FontFamily("Consolas, 'Courier New', monospace"),
                FontSize = 14,
                ShowLineNumbers = false,
                WordWrap = true,
                MinHeight = 200,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            try
            {
                _textEditor.SyntaxHighlighting = null;
            }
            catch
            {
            }
 
            _textEditor.Document = new TextDocument();
            _textEditor.TextChanged += OnTextChanged;

            try
            {
                _searchPanel = SearchPanel.Install(_textEditor);
                System.Diagnostics.Debug.WriteLine($"搜索面板安装成功: {_editorName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"搜索面板安装失败 ({_editorName}): {ex.Message}");
            }

            dockPanel.Children.Add(_textEditor);
            Content = dockPanel;
        }
        private void OnTextChanged(object? sender, EventArgs e)
        {
            var newText = _textEditor.Text ?? "";
            if (Text != newText)
            {
                SetValue(TextProperty, newText);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }
        private void UpdateEditorText()
        {
            if (_textEditor != null && _textEditor.Text != Text)
            {
                _textEditor.Text = Text;
            }
        }
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == TextProperty)
            {
                UpdateEditorText();
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

        private void CreateToolbar()
        {
            _toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = new SolidColorBrush(Color.FromRgb(233, 236, 239)),
                Spacing = 5,
                Margin = new Thickness(8, 8, 8, 6)
            };

            _toolbar.Children.Add(new TextBlock
            {
                Text = "字号:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 2, 0)
            }); _fontSizeCombo = new ComboBox
            {
                Width = 60,
                Height = 25,
                VerticalAlignment = VerticalAlignment.Center,
                ItemsSource = new[] { "10", "12", "14", "16", "18", "20", "22", "24", "26", "28", "30", "32", "34", "36", "38" },
                SelectedItem = "14"
            };
            _fontSizeCombo.SelectionChanged += OnFontSizeChanged;
            _toolbar.Children.Add(_fontSizeCombo);

            _toolbar.Children.Add(new Border
            {
                Width = 1,
                Background = Brushes.Gray,
                Margin = new Thickness(5, 2)
            });
            _lineNumbersButton = new ToggleButton
            {
                Content = "#",
                Width = 30,
                Height = 25,
                IsChecked = false,
                Background = new SolidColorBrush(Color.FromRgb(108, 117, 125)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(108, 117, 125))
            };
            ToolTip.SetTip(_lineNumbersButton, "显示/隐藏行号");
            _lineNumbersButton.Click += OnLineNumbersButtonClick;
            _toolbar.Children.Add(_lineNumbersButton);

            _wordWrapButton = new ToggleButton
            {
                Content = "↩",
                Width = 30,
                Height = 25,
                IsChecked = true,
                Background = new SolidColorBrush(Color.FromRgb(108, 117, 125)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(108, 117, 125))
            };
            ToolTip.SetTip(_wordWrapButton, "自动换行");
            _wordWrapButton.Click += OnWordWrapButtonClick;
            _toolbar.Children.Add(_wordWrapButton);

            _toolbar.Children.Add(new Border
            {
                Width = 1,
                Background = Brushes.Gray,
                Margin = new Thickness(5, 2)
            });
            _toolbar.Children.Add(new TextBlock
            {
                Text = "语法:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 2, 0)
            });
            _syntaxCombo = new ComboBox
            {
                Width = 80,
                Height = 25,
                VerticalAlignment = VerticalAlignment.Center,
                ItemsSource = new[] { "无", "C#", "XML", "JSON", "JavaScript", "Python" },
                SelectedItem = "无"
            };
            _syntaxCombo.SelectionChanged += OnSyntaxChanged;
            _toolbar.Children.Add(_syntaxCombo);
            /*搜索按钮暂时不使用，等待修复
                        _toolbar.Children.Add(new Border
                        {
                            Width = 1,
                            Background = Brushes.Gray,
                            Margin = new Thickness(5, 2)
                        });
                        _searchButton = new Button
                        {
                            Content = "🔍",
                            Width = 30,
                            Height = 25,
                            FontSize = 16,
                            VerticalAlignment = VerticalAlignment.Center,
                            Background = new SolidColorBrush(Color.FromRgb(220, 221, 222)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(206, 212, 218))
                        };
                        ToolTip.SetTip(_searchButton, "搜索/替换 (Ctrl+F)");
                        _searchButton.Click += OnSearchButtonClick;
                        _toolbar.Children.Add(_searchButton);
                        */
        }

        #region 事件处理

        private void OnFontSizeChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_fontSizeCombo.SelectedItem?.ToString() is string sizeStr &&
                double.TryParse(sizeStr, out double size))
            {
                _textEditor.FontSize = size;
            }
        }

        private void OnLineNumbersButtonClick(object? sender, RoutedEventArgs e)
        {
            _textEditor.ShowLineNumbers = _lineNumbersButton.IsChecked == true;
        }

        private void OnWordWrapButtonClick(object? sender, RoutedEventArgs e)
        {
            _textEditor.WordWrap = _wordWrapButton.IsChecked == true;
        }

        private void OnSyntaxChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_syntaxCombo.SelectedItem?.ToString() is string syntax)
            {
                try
                {
                    _textEditor.SyntaxHighlighting = syntax switch
                    {
                        "C#" => HighlightingManager.Instance.GetDefinition("C#"),
                        "XML" => HighlightingManager.Instance.GetDefinition("XML"),
                        "JSON" => HighlightingManager.Instance.GetDefinition("JavaScript"),
                        "JavaScript" => HighlightingManager.Instance.GetDefinition("JavaScript"),
                        "Python" => HighlightingManager.Instance.GetDefinition("Python"),
                        _ => null
                    };
                }
                catch
                {
                    _textEditor.SyntaxHighlighting = null;
                }
            }
        }
        private void OnSearchButtonClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"搜索按钮被点击: {_editorName}");

                _textEditor.Focus();

                if (_searchPanel != null)
                {
                    System.Diagnostics.Debug.WriteLine($"打开搜索面板: {_editorName}");
                    _searchPanel.Open();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"搜索面板为null: {_editorName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"打开搜索面板时发生错误 ({_editorName}): {ex.Message}");
            }
        }

        #endregion
    }
}
