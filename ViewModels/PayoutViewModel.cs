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
    public System.Collections.ObjectModel.ObservableCollection<PayoutPackageRow> PackageRows { get; } = new();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string packageStatText = "";

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

    public void RefreshPackages()
    {
        PackageRows.Clear();
        foreach (var r in AppDb.GetPayoutPackages())
            PackageRows.Add(new PayoutPackageRow { Record = r });
        var total = AppDb.GetPayoutPackages().Sum(p => p.NetProfit);
        PackageStatText = total >= 0 ? $"包租合计净赚 {total:0.##} 元（共 {PackageRows.Count} 套核算）" : $"包租合计亏损 {Math.Abs(total):0.##} 元（共 {PackageRows.Count} 套核算）";
        OnPropertyChanged(nameof(PackageStatText));
    }

    public void Refresh()
    {
        RefreshPackages();
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

// ============ v1.0：包租核算（对齐 iOS PayoutPackageRecord） ============
public class PayoutPackageRow
{
    public PayoutPackageRecord Record { get; set; } = new();
    public string RoomNumber => Record.RoomNumber;
    public string PackagePeriod => $"{Record.PackageStart:yyyy-MM-dd} ~ {Record.PackageEnd:yyyy-MM-dd}";
    public string PackageAmountText => $"包租总价 {Record.TotalPackageAmount:0.##} 元";
    public string DailyCostText => $"日均 {Record.DailyCost:0.##} 元";
    public string VacancyCostText => $"空置损失 {Record.VacancyCost:0.##} 元（{Record.VacancyDays}天）";
    public string RentalIncomeText => $"租客收入 {Record.RentalIncome:0.##} 元";
    public string NetProfitText => Record.NetProfit >= 0 ? $"净赚 {Record.NetProfit:0.##} 元" : $"亏损 {Math.Abs(Record.NetProfit):0.##} 元";
    public string Notes => Record.Notes;
    public Avalonia.Media.IBrush NetProfitBrush => Record.NetProfit >= 0
        ? Avalonia.Media.Brushes.Green
        : Avalonia.Media.Brushes.Red;
}
}
