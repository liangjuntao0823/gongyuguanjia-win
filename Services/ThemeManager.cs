using Avalonia;
using Avalonia.Styling;

namespace ApartmentManagerWin.Services;

/// <summary>
/// 外观模式：白天 / 黑夜 / 跟随系统（对齐 Mac 黑夜模式需求）
/// </summary>
public static class ThemeManager
{
    public static string CurrentMode { get; private set; } = "system";

    public static void Apply(string mode)
    {
        CurrentMode = mode;
        var app = Application.Current;
        if (app == null) return;
        app.RequestedThemeVariant = mode switch
        {
            "day" => ThemeVariant.Light,
            "night" => ThemeVariant.Dark,
            _ => ThemeVariant.Default, // 跟随系统
        };
    }
}
