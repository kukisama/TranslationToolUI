using System;
using System.Collections.Generic;
using Avalonia.Data.Converters;

namespace TrueFluentPro.Controls
{
    public class ProgressToOffsetConverter : IMultiValueConverter
    {
        public double Padding { get; set; } = 2;

        public object? Convert(IList<object?> values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || values.Count < 3)
            {
                return 0d;
            }

            var progress = GetDouble(values[0]);
            var trackWidth = GetDouble(values[1]);
            var pillWidth = GetDouble(values[2]);

            if (double.IsNaN(trackWidth) || double.IsInfinity(trackWidth) || trackWidth <= 0)
            {
                return 0d;
            }

            if (double.IsNaN(pillWidth) || double.IsInfinity(pillWidth) || pillWidth < 0)
            {
                pillWidth = 0;
            }

            progress = Math.Clamp(progress, 0, 1);
            var usable = Math.Max(0, trackWidth - pillWidth - Padding * 2);
            return Padding + usable * progress;
        }

        public object? ConvertBack(IList<object?> values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        private static double GetDouble(object? value)
        {
            if (value is double number)
            {
                return number;
            }

            return 0d;
        }
    }
}
