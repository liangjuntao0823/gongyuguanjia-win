using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.ViewModels;

public class RentRow
{
    public Property Property { get; set; } = new();
    public string RoomNumber => Property.RoomNumber;
    public string Landlord => Property.Landlord;
    public string UnitType => string.IsNullOrEmpty(Property.UnitType) ? "-" : Property.UnitType;
    public string Rent => $"{Property.Rent} 元/月";
    public int DueDay => Property.RentDueDay;
    public string Type => Property.Type;
    public string Status => Property.IsTerminated ? "已退租" : "在租";
    public string Notes => Property.Notes;
}

public partial class RentViewModel : ViewModelBase
{
    public ObservableCollection<RentRow> Rows { get; } = new();
    public ObservableCollection<string> StatusFilter { get; } = new() { "在租", "已退租", "全部" };
    public ObservableCollection<string> DueDayFilter { get; } = new() { "全部" };

    [ObservableProperty]
    private string selectedDueDay = "全部";

    [ObservableProperty]
    private string selectedStatus = "在租";
    [ObservableProperty]
    private string statText = "";

    public RentViewModel()
    {
        AppDb.DataChanged += Refresh;
        Refresh();
    }

    partial void OnSelectedStatusChanged(string value) => Refresh();
    partial void OnSelectedDueDayChanged(string value) => Refresh();

    public void Refresh()
    {
        Rows.Clear();
        var list = AppDb.GetProperties();
        // 交租日筛选（对齐 Mac：全部/1号/13号/15号/20号/23号/25号）
        var allProps = AppDb.GetProperties().Where(p => !p.IsDeleted).ToList();
        var dueDays = allProps.Where(p => p.RentDueDay > 0).Select(p => p.RentDueDay).Distinct().OrderBy(d => d).ToList();
        var prev = SelectedDueDay;
        DueDayFilter.Clear();
        DueDayFilter.Add("全部");
        foreach (var d in dueDays) DueDayFilter.Add($"{d}号");
        if (!DueDayFilter.Contains(prev)) SelectedDueDay = "全部";
        var filtered = SelectedStatus switch
        {
            "在租" => list.Where(p => !p.IsTerminated),
            "已退租" => list.Where(p => p.IsTerminated),
            _ => list,
        };
        foreach (var p in filtered.OrderBy(p => p.RentDueDay))
            Rows.Add(new RentRow { Property = p });
        var total = list.Count(p => !p.IsTerminated);
        var monthIncome = list.Where(p => !p.IsTerminated).Sum(p => (decimal)p.Rent);
        var dueSel = SelectedDueDay;
        if (dueSel != "全部")
        {
            var dd = int.Parse(dueSel.Replace("号", ""));
            filtered = filtered.Where(p => p.RentDueDay == dd);
        }
        var rentedCount = allProps.Count(p => !p.IsTerminated);
        var retiredCount = allProps.Count(p => p.IsTerminated);
        StatText = $"在租 {rentedCount} · 退租 {retiredCount} ｜ 月收租合计 {monthIncome:0.##} 元 ｜ 显示 {Rows.Count} 套";
        OnPropertyChanged(nameof(StatText));
    }

    public void DeleteProperty(Property p)
    {
        AppDb.SoftDeleteProperty(p.Id);
        AppDb.AddLog("收租", "删除", "成功", $"{p.RoomNumber} 移入回收站", p.RoomNumber);
        AppDb.NotifyDataChanged();
    }
}
