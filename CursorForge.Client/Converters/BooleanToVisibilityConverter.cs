using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CursorForge.Client.Converters;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolVal = value is bool b && b;
        if (Inverse) boolVal = !boolVal;
        return boolVal ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility v)
        {
            bool res = v == Visibility.Visible;
            return Inverse ? !res : res;
        }
        return false;
    }
}
