using ApartmentManagerWin.ViewModels;

namespace ApartmentManagerWin.Services;

/// <summary>全局导航引用（快捷操作跨页跳转）</summary>
public static class Nav
{
    public static MainViewModel? Current { get; set; }
}
