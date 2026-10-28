using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace ApartmentManagerWin;

/// <summary>取反转换器（IsVisible 绑定用）</summary>
public class BoolInverseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

/// <summary>bool → 可见性（False 时 Collapsed）</summary>
public class BoolVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b;
}
