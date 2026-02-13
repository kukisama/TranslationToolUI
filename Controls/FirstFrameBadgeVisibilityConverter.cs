using System;
using System.Globalization;
using Avalonia.Data.Converters;
using TranslationToolUI.Services;

namespace TranslationToolUI.Controls
{
    public sealed class FirstFrameBadgeVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var path = value as string;
            return VideoFrameExtractorService.IsFirstFrameImagePath(path);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
