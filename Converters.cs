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


/// 导航栏状态转换器（选中态）
public class NavBgConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => (v is bool b && b)
            ? (object)new SolidColorBrush(Color.Parse("#2F6BFF"))
            : new SolidColorBrush(Color.Parse("Transparent"));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public class NavFgConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => (v is bool b && b)
            ? (object)new SolidColorBrush(Color.Parse("#FFFFFF"))
            : new SolidColorBrush(Color.Parse("#9BA3B2"));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public class NavIconBgConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => (v is bool b && b)
            ? (object)new SolidColorBrush(Color.Parse("#FFFFFF22"))
            : new SolidColorBrush(Color.Parse("#FFFFFF0A"));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public class NavBarConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => (v is bool b && b)
            ? (object)new SolidColorBrush(Color.Parse("#FFFFFF"))
            : new SolidColorBrush(Color.Parse("Transparent"));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public class NavWeightConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => (v is bool b && b) ? (object)FontWeight.SemiBold : FontWeight.Normal;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
