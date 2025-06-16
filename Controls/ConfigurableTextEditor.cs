using Avalonia;
using Avalonia.Controls;
using TranslationToolUI.Models;
using System;
using System.ComponentModel;

namespace TranslationToolUI.Controls
{
    public class ConfigurableTextEditor : UserControl
    {
        private Control? _currentEditor;

        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<ConfigurableTextEditor, string>(nameof(Text), "");

        public static readonly StyledProperty<string> PlaceholderProperty =
            AvaloniaProperty.Register<ConfigurableTextEditor, string>(nameof(Placeholder), "");

        public static readonly StyledProperty<TextEditorType> EditorTypeProperty =
            AvaloniaProperty.Register<ConfigurableTextEditor, TextEditorType>(nameof(EditorType), TextEditorType.Simple);

        public ConfigurableTextEditor()
        {
            InitializeEditor();
        }

        private void InitializeEditor()
        {
            UpdateEditor();
        }

        private void UpdateEditor()
        {
            var currentText = Text;
            var currentPlaceholder = Placeholder;

            if (_currentEditor != null)
            {
                Content = null;
                _currentEditor = null;
            }

            switch (EditorType)
            {
                case TextEditorType.Simple:
                    System.Diagnostics.Debug.WriteLine("创建简单编辑器");
                    _currentEditor = new SimpleRichTextBox();
                    break;
                    
                case TextEditorType.Advanced:
                    System.Diagnostics.Debug.WriteLine("创建高级编辑器（黄色背景版本）");
                    _currentEditor = new AdvancedRichTextBox();
                    break;
                    
                default:
                    _currentEditor = new SimpleRichTextBox();
                    break;
            }

            if (_currentEditor != null)
            {
                Content = _currentEditor;
                
                Text = currentText;
                Placeholder = currentPlaceholder;
                UpdateEditorText();
                UpdateEditorPlaceholder();
                
                System.Diagnostics.Debug.WriteLine($"编辑器切换完成: {EditorType}");
            }
        }

        private void UpdateEditorText()
        {
            if (_currentEditor == null) return;

            var text = Text;

            if (_currentEditor is SimpleRichTextBox simpleEditor)
            {
                simpleEditor.Text = text;
                simpleEditor.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SimpleRichTextBox.Text))
                    {
                        if (Text != simpleEditor.Text)
                        {
                            SetValue(TextProperty, simpleEditor.Text);
                        }
                    }
                };
            }
            else if (_currentEditor is AdvancedRichTextBox advancedEditor)
            {
                advancedEditor.Text = text;
                advancedEditor.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(AdvancedRichTextBox.Text))
                    {
                        if (Text != advancedEditor.Text)
                        {
                            SetValue(TextProperty, advancedEditor.Text);
                        }
                    }
                };
            }
        }

        private void UpdateEditorPlaceholder()
        {
            if (_currentEditor == null) return;

            var placeholder = Placeholder;

            if (_currentEditor is SimpleRichTextBox simpleEditor)
            {
                simpleEditor.Placeholder = placeholder;
            }
            else if (_currentEditor is AdvancedRichTextBox advancedEditor)
            {
                advancedEditor.Placeholder = placeholder;
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == TextProperty)
            {
                UpdateEditorText();
            }
            else if (change.Property == PlaceholderProperty)
            {
                UpdateEditorPlaceholder();
            }
            else if (change.Property == EditorTypeProperty)
            {
                UpdateEditor();
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

        public TextEditorType EditorType
        {
            get => GetValue(EditorTypeProperty);
            set => SetValue(EditorTypeProperty, value);
        }
    }
}
