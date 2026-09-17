using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace RadiusXtractUI.Helpers;

public class BoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool bValue = (bool)value;
        return bValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool bValue = (bool)value;
        return !bValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class ProgressTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return $"{value}%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string hex = value as string ?? "#00000000";
        hex = hex.Replace("#", "");
        byte a = 255;
        byte r = 0, g = 0, b = 0;
        if (hex.Length == 8)
        {
            a = System.Convert.ToByte(hex.Substring(0, 2), 16);
            r = System.Convert.ToByte(hex.Substring(2, 2), 16);
            g = System.Convert.ToByte(hex.Substring(4, 2), 16);
            b = System.Convert.ToByte(hex.Substring(6, 2), 16);
        }
        else if (hex.Length == 6)
        {
            r = System.Convert.ToByte(hex.Substring(0, 2), 16);
            g = System.Convert.ToByte(hex.Substring(2, 2), 16);
            b = System.Convert.ToByte(hex.Substring(4, 2), 16);
        }
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
