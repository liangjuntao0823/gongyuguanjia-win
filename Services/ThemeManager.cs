using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ApartmentManagerWin.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace ApartmentManagerWin.Services;

/// <summary>
/// 外观系统（v1.0）：外观模式（白天/黑夜/跟随系统）× 配色（默认蓝/暖金/墨绿/暗夜紫）+ 自定义背景图片
/// </summary>
public static class ThemeManager
{
    public static string CurrentMode { get; private set; } = "system";
    public static string CurrentPalette { get; private set; } = "默认蓝";

    public static IReadOnlyList<string> Palettes { get; } = new[] { "默认蓝", "暖金", "墨绿", "暗夜紫" };

    public static string SavedMode
    {
        get => Settings.Get("themeMode", "system");
        set => Settings.Set("themeMode", value);
    }

    public static string SavedPalette
    {
        get => Settings.Get("themePalette", "默认蓝");
        set => Settings.Set("themePalette", value);
    }

    /// 自定义背景图片路径（空 = 不启用）
    public static string BackgroundImagePath
    {
        get => Settings.Get("themeBackground", "");
        set => Settings.Set("themeBackground", value);
    }

    public static void Apply(string mode, string palette)
    {
        CurrentMode = mode;
        CurrentPalette = palette;
        var app = Application.Current;
        if (app == null) return;
        app.RequestedThemeVariant = mode switch
        {
            "day" => ThemeVariant.Light,
            "night" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        ApplyPalette(app, palette);
        ApplyBackground(app);
    }

    public static void ApplySaved() => Apply(SavedMode, SavedPalette);

    /// 用户选择新背景图片（返回是否成功）
    public static bool SetBackgroundImage(string path)
    {
        if (!File.Exists(path)) return false;
        BackgroundImagePath = path;
        ApplyBackground(Application.Current);
        return true;
    }

    public static void ClearBackground()
    {
        BackgroundImagePath = "";
        ApplyBackground(Application.Current);
    }

    private static void ApplyBackground(Avalonia.Application? app)
    {
        if (app == null) return;
        var path = BackgroundImagePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            // 恢复纯色背景（由 ApplyPalette 覆盖回默认）
            app.Resources.Remove("PageBackground");
            if (!app.Resources.ContainsKey("PageBackground"))
            {
                var isDark = app.RequestedThemeVariant == ThemeVariant.Dark
                    || (app.RequestedThemeVariant == ThemeVariant.Default
                        && app.ActualThemeVariant == ThemeVariant.Dark);
                app.Resources["PageBackground"] = new SolidColorBrush(isDark
                    ? Color.Parse("#17181C") : Color.Parse("#F4F5F9"));
            }
            return;
        }
        try
        {
            using var fs = File.OpenRead(path);
            var bitmap = new Bitmap(fs);
            app.Resources["PageBackground"] = new ImageBrush(bitmap)
            {
                Stretch = Stretch.UniformToFill,
                Opacity = 0.88,
            };
        }
        catch
        {
            app.Resources.Remove("PageBackground");
            app.Resources["PageBackground"] = new SolidColorBrush(Color.Parse("#F4F5F9"));
        }
    }

    /// 按配色覆写主题资源（Light/Dark 各自色值）
    private static void ApplyPalette(Avalonia.Application app, string palette)
    {
        var isDark = app.RequestedThemeVariant == ThemeVariant.Dark
            || (app.RequestedThemeVariant == ThemeVariant.Default
                && app.ActualThemeVariant == ThemeVariant.Dark);
        (string key, Color light, Color dark)[] colors = palette switch
        {
            "暖金" => new[]
            {
                ("Accent", Color.Parse("#C07A1B"), Color.Parse("#E39A3A")),
                ("SidebarActive", Color.Parse("#C07A1B"), Color.Parse("#E39A3A")),
                ("AccentSoft", Color.Parse("#FBF0DE"), Color.Parse("#4A3A20")),
                ("SidebarBackground", Color.Parse("#F7F3EA"), Color.Parse("#221E16")),
                ("SidebarHover", Color.Parse("#EFE6D2"), Color.Parse("#2E2820")),
            },
            "墨绿" => new[]
            {
                ("Accent", Color.Parse("#1E7F6B"), Color.Parse("#35A48C")),
                ("SidebarActive", Color.Parse("#1E7F6B"), Color.Parse("#35A48C")),
                ("AccentSoft", Color.Parse("#E4F3EE"), Color.Parse("#1E3A33")),
                ("SidebarBackground", Color.Parse("#ECF4F1"), Color.Parse("#182421")),
                ("SidebarHover", Color.Parse("#D9EBE4"), Color.Parse("#223430")),
            },
            "暗夜紫" => new[]
            {
                ("Accent", Color.Parse("#6C4BBF"), Color.Parse("#8B6FE0")),
                ("SidebarActive", Color.Parse("#6C4BBF"), Color.Parse("#8B6FE0")),
                ("AccentSoft", Color.Parse("#EFEAFA"), Color.Parse("#322A4A")),
                ("SidebarBackground", Color.Parse("#F0EDF8"), Color.Parse("#1E1A28")),
                ("SidebarHover", Color.Parse("#E2DBF3"), Color.Parse("#2A2438")),
            },
            _ => new[]
            {
                ("Accent", Color.Parse("#2F6BFF"), Color.Parse("#3D7BFF")),
                ("SidebarActive", Color.Parse("#2F6BFF"), Color.Parse("#3D7BFF")),
                ("AccentSoft", Color.Parse("#EAF0FF"), Color.Parse("#2A3A5E")),
                ("SidebarBackground", Color.Parse("#EEF0F6"), Color.Parse("#1E2025")),
                ("SidebarHover", Color.Parse("#E2E6F0"), Color.Parse("#2A2D35")),
            },
        };
        foreach (var (key, light, dark) in colors)
        {
            var color = isDark ? dark : light;
            app.Resources[key] = new SolidColorBrush(color);
        }
    }
}
