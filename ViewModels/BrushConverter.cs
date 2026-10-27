using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ApartmentManagerWin.ViewModels;

/// <summary>收入绿/支出红的简单转换器</summary>
public class IncomeBrushConverter : IValueConverter
{
    public static readonly IncomeBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b
            ? new SolidColorBrush(Color.Parse("#22A06B"))
            : new SolidColorBrush(Color.Parse("#D64545"));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// 兼容 x:Static 引用
public static class BrushConverter
{
    public static readonly IncomeBrushConverter Income = IncomeBrushConverter.Instance;
}
