using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.ViewModels;

public class PayoutRow
{
    public PayoutRecord Payout { get; set; } = new();
    public string RoomNumber => Payout.RoomNumber;
    public string Manager => Payout.Manager;
    public string UnitType => string.IsNullOrEmpty(Payout.UnitType) ? "-" : Payout.UnitType;
    public string PaymentMethod => Payout.PaymentMethod;
    public string Rent => Payout.MonthlyRent.HasValue ? $"{Payout.MonthlyRent.Value} 元/月" : $"{(Payout.AnnualRent / 12)} 元/月";
    public int DueDay => Payout.RentDueDay;
    public string LeaseRange => $"{Payout.LeaseStartDate:yyyy-MM-dd} ~ {Payout.LeaseEndDate:yyyy-MM-dd}";
}

public partial class PayoutViewModel : ViewModelBase
{
    public ObservableCollection<PayoutRow> Rows { get; } = new();
    public ObservableCollection<string> StatusFilter { get; } = new() { "全部", "月付", "季付", "半年付", "年付" };

    [ObservableProperty]
    private string selectedFilter = "全部";
    [ObservableProperty]
    private string statText = "";

    public PayoutViewModel()
    {
        AppDb.DataChanged += Refresh;
        Refresh();
    }

    partial void OnSelectedFilterChanged(string value) => Refresh();

    public void Refresh()
    {
        Rows.Clear();
        var list = AppDb.GetPayouts();
        var filtered = SelectedFilter == "全部" ? list : list.Where(p => p.PaymentMethod == SelectedFilter);
        foreach (var p in filtered.OrderBy(p => p.RentDueDay))
            Rows.Add(new PayoutRow { Payout = p });
        var monthOut = list.Sum(p => (decimal)(p.MonthlyRent ?? p.AnnualRent / 12));
        StatText = $"包租合同 {list.Count} 份 ｜ 月付租支出 {monthOut:0.##} 元 ｜ 显示 {Rows.Count} 份";
        OnPropertyChanged(nameof(StatText));
    }

    public void DeletePayout(PayoutRecord p)
    {
        AppDb.SoftDeletePayout(p.Id);
        AppDb.AddLog("包租", "删除", "成功", $"{p.RoomNumber} 移入回收站", p.RoomNumber);
        AppDb.NotifyDataChanged();
    }
}
