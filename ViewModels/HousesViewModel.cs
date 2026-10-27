using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.ViewModels;

public class LayoutChip
{
    public string Name { get; set; } = "";
    public bool IsSelected { get; set; }
}

public class HouseRow
{    public House House { get; set; } = new();
    public long Id => House.Id;
    public string RoomNo => House.RoomNo;
    public string DisplayRoomNo => House.RoomNo;
    public string Layout => string.IsNullOrEmpty(House.Layout) ? "-" : House.Layout;
    public string Landlord => House.Landlord;
    public string Price => House.Price > 0 ? $"{House.Price} 元/月" : "-";
    public string Remark => string.IsNullOrEmpty(House.Remark) ? "-" : House.Remark;
    public string Status => House.IsRented ? "已租" : (House.IsListed ? "待租" : "未上架");
    public bool HasMedia => House.HasMedia;
    public string CloudBadge => House.CloudStatus switch
    {
        "both" => "双端",
        "cloud" => "云端",
        _ => "本地",
    };
    public bool IsCloudBadge => !string.IsNullOrEmpty(CosConfig.Load().Bucket);
}

public partial class HousesViewModel : ViewModelBase
{
    public ObservableCollection<HouseRow> Rows { get; } = new();

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private string statusFilter = "待租";   // 待租(默认)/已租/全部

    public ObservableCollection<string> StatusOptions { get; } = new() { "待租", "已租", "全部" };

    public ObservableCollection<LayoutChip> LayoutChips { get; } = new();

    [ObservableProperty]
    private string statText = "";

    [ObservableProperty]
    private bool isDetailVisible;

    public House? SelectedHouse { get; private set; }

    public HousesViewModel()
    {
        RefreshLayouts();
        AppDb.HousesChanged += Refresh;
        AppDb.DataChanged += RefreshLayouts;
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnStatusFilterChanged(string value) => Refresh();

    public void ToggleLayout(LayoutChip chip)
    {
        chip.IsSelected = !chip.IsSelected;
        Refresh();
    }

    public void RefreshLayouts()
    {
        var layouts = AppDb.GetHouses().Select(h => h.Layout).Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l).ToList();
        var keep = LayoutChips.Where(c => c.IsSelected && layouts.Contains(c.Name)).Select(c => c.Name).ToList();
        LayoutChips.Clear();
        foreach (var l in layouts)
            LayoutChips.Add(new LayoutChip { Name = l, IsSelected = keep.Contains(l) });
    }

    public void Refresh()
    {
        Rows.Clear();
        var list = AppDb.GetHouses();
        // 默认显示待租（未出租：已上架+未上架均算待租）
        var filtered = StatusFilter switch
        {
            "待租" => list.Where(h => !h.IsRented),
            "已租" => list.Where(h => h.IsRented),
            _ => list,
        };
        if (LayoutChips.Count(c => c.IsSelected) > 0)
        {
            var sel = LayoutChips.Where(c => c.IsSelected).Select(c => c.Name).ToHashSet();
            filtered = filtered.Where(h => sel.Contains(h.Layout));
        }
        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(h => h.RoomNo.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                                       || h.Landlord.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var h in filtered.OrderBy(h => h.Tower).ThenBy(h => h.RoomNo))
            Rows.Add(new HouseRow { House = h });

        var all = AppDb.GetHouses();
        var t1 = all.Count(h => h.Tower == 1);
        var t2 = all.Count(h => h.Tower == 2);
        var withMedia = all.Count(h => h.HasMedia);
        StatText = $"目前已收录房源总计：1座 {t1} 套，2座 {t2} 套 ｜ 带图 {withMedia} 套 ｜ 当前显示 {Rows.Count} 套";
        OnPropertyChanged(nameof(StatText));
    }

    public void OpenDetail(House h)
    {
        SelectedHouse = h;
        IsDetailVisible = true;
    }

    public void CloseDetail() => IsDetailVisible = false;

    public void DeleteHouse(House h)
    {
        // 软删进回收站 + 本地账本镜像清理
        AppDb.SoftDeleteHouse(h.Id);
        CosService.DeleteLocalLedger(h.MediaRoomKey);
        AppDb.AddLog("房源", "删除", "成功", $"{h.RoomNo} 移入回收站", h.RoomNo);
        AppDb.NotifyHousesChanged();
    }

    /// 一键保存全部媒体（复制媒体目录到目标）
    public string? SaveAllMedia(House h, string targetDir)
    {
        var src = DataRoot.RoomMediaDirectory(h.MediaRoomKey);
        if (!Directory.Exists(src)) return null;
        var dst = Path.Combine(targetDir, h.RoomNo + "_媒体");
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src))
        {
            var name = Path.GetFileName(f);
            if (name == "info.json") continue;
            File.Copy(f, Path.Combine(dst, name), true);
        }
        return dst;
    }
}
