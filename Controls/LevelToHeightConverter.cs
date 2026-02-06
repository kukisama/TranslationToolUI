using System;
using Avalonia.Data.Converters;

namespace TranslationToolUI.Controls
{
    public class LevelToHeightConverter : IValueConverter
    {
        public double MaxHeight { get; set; } = 16;

        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double level)
            {
                return Math.Clamp(level, 0, 1) * MaxHeight;
            }

            return 0d;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }
    }
}
