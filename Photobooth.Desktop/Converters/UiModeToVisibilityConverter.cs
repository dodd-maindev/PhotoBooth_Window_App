using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Photobooth.Desktop.Models;

namespace Photobooth.Desktop.Converters;

public class UiModeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is UiMode currentMode && parameter is string targetMode)
        {
            var isMatch = targetMode.ToLower() switch
            {
                "landscape" => currentMode == UiMode.Landscape,
                "portrait" => currentMode == UiMode.Portrait,
                _ => false
            };

            return isMatch ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
