using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.ViewModels;

public class NavItem
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Glyph { get; set; } = "";
}

public partial class MainViewModel : ViewModelBase
{
    private readonly SyncService _sync = new();

    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new NavItem { Key = "dashboard", Title = "工作台", Glyph = "◉" },
        new NavItem { Key = "houses", Title = "房源", Glyph = "⌂" },
        new NavItem { Key = "deals", Title = "成交", Glyph = "✓" },
        new NavItem { Key = "rent", Title = "收租", Glyph = "¥" },
        new NavItem { Key = "payout", Title = "包租", Glyph = "¤" },
        new NavItem { Key = "summary", Title = "汇总", Glyph = "Σ" },
        new NavItem { Key = "todos", Title = "待办", Glyph = "☐" },
        new NavItem { Key = "settings", Title = "设置", Glyph = "⚙" },
    };

    [ObservableProperty]
    private string currentNavKey = "dashboard";

    [ObservableProperty]
    private object? currentPage;

    [ObservableProperty]
    private string syncStatusText = "未同步";

    [ObservableProperty]
    private double syncProgress = 0;

    public bool IsSyncingVisible => SyncProgress > 0 && SyncProgress < 100;

    [ObservableProperty]
    private bool isCloudConfigured = CosConfig.Load().IsValid;

    public MainViewModel()
    {
        Nav.Current = this;
        CurrentPage = new DashboardViewModel();
        _sync.StatusChanged += s => SyncStatusText = s;
        _sync.ProgressChanged += p =>
        {
            SyncProgress = p;
            OnPropertyChanged(nameof(IsSyncingVisible));
        };
        AppDb.DataChanged += () => IsCloudConfigured = CosConfig.Load().IsValid;
        NotifyService.CheckReminders();
    }

    /// 页面切换：懒加载创建、切走丢弃（避免 Mac ZStack 常驻卡顿问题）
    partial void OnCurrentNavKeyChanged(string value)
    {
        CurrentPage = value switch
        {
            "houses" => new HousesViewModel(),
            "deals" => new DealsViewModel(),
            "rent" => new RentViewModel(),
            "payout" => new PayoutViewModel(),
            "summary" => new SummaryViewModel(),
            "todos" => new TodosViewModel(),
            "settings" => new SettingsViewModel(),
            _ => new DashboardViewModel(),
        };
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (_sync.IsRunning) return;
        await _sync.SyncAsync();
    }

    [RelayCommand]
    private void NavigateTo(string key) => CurrentNavKey = key;

    /// 供子视图快捷跳转
    public void GoTo(string key) => CurrentNavKey = key;
}
