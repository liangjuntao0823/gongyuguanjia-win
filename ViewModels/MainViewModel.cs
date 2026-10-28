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
    public bool IsSelected { get; set; }
}

public partial class MainViewModel : ViewModelBase
{
    private readonly SyncService _sync = new();
    /// v1.0：供子视图做同步预览（对齐 iOS applySyncPreview）
    public SyncService SyncService => _sync;

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

    /// 全局提示中心（根层级横幅，任何界面可见可点，iOS v1.76 防卡死等价实现）
    public ObservableCollection<NotifyItem> NotifyItems { get; } = new();

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

    /// 版本号（v1.0 起，设置页与关于同步展示）
    public string VersionText => "v1.0.1";

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
        _sync.SyncLog += s => PushNotify("同步", s, NotifyKind.Info);
        _sync.Completed += () => PushNotify("同步完成", "房源与媒体同步完成", NotifyKind.Success);
        AppDb.DataChanged += () => IsCloudConfigured = CosConfig.Load().IsValid;
        // 全局横幅：订阅通知（任何页面可点）
        NotifyService.NotifyRaised += n =>
        {
            NotifyItems.Add(n);
            while (NotifyItems.Count > 6) NotifyItems.RemoveAt(0);
        };
        NotifyService.CheckReminders();
    }

    public void PushNotify(string title, string message, NotifyKind kind)
        => NotifyService.Push(title, message, kind);

    [RelayCommand]
    private void DismissNotify(NotifyItem item) => NotifyItems.Remove(item);

    /// 页面切换：懒加载创建、切走丢弃（避免常驻卡顿）
    partial void OnCurrentNavKeyChanged(string value)
    {
        foreach (var n in NavItems) n.IsSelected = n.Key == value;
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
        // 同步完成后刷新各板块
        var ok = await _sync.SyncAsync();
        if (ok) AppDb.NotifyDataChanged();
    }

    [RelayCommand]
    private void NavigateTo(string key) => CurrentNavKey = key;

    /// 供子视图快捷跳转
    public void GoTo(string key) => CurrentNavKey = key;
}
