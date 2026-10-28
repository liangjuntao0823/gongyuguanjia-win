using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.ViewModels;

public class MonthlyDetailRow
{
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public bool IsIncome { get; set; }
}

public partial class SummaryViewModel : ViewModelBase
{
    public ObservableCollection<int> Years { get; } = new();
    public ObservableCollection<string> Months { get; } = new();
    public ObservableCollection<MonthlyDetailRow> DetailRows { get; } = new();

    [ObservableProperty]
    private int selectedYear;
    [ObservableProperty]
    private string selectedMonth = "全部";
    [ObservableProperty]
    private string statText = "";
    [ObservableProperty]
    private decimal totalIncome;
    [ObservableProperty]
    private decimal totalExpense;
    [ObservableProperty]
    private decimal profit;

    // v1.0：对齐 Mac 汇总页 5 KPI（成交单数/中介费/杂项收入/杂项支出/净总收入）
    [ObservableProperty]
    private int dealCount;
    [ObservableProperty]
    private decimal agentFee;
    [ObservableProperty]
    private decimal miscIncome;
    [ObservableProperty]
    private decimal miscExpense;
    [ObservableProperty]
    private decimal netIncome;

    public string DealCountText => $"{DealCount}单";
    public string AgentFeeText => $"¥{AgentFee:0}";
    public string MiscIncomeText => $"¥{MiscIncome:0}";
    public string MiscExpenseText => $"¥{MiscExpense:0}";
    public string NetIncomeText => $"¥{NetIncome:0}";
    [ObservableProperty]
    private bool hideData;   // v1.0：对齐 iOS 汇总页隐藏数据开关 "**"

    public SummaryViewModel()
    {
        var now = DateTime.Now;
        SelectedYear = now.Year;
        Months.Add("全部");
        for (int m = 1; m <= 12; m++) Months.Add(m + "月");
        Years.Add(now.Year);
        Years.Add(now.Year - 1);
        AppDb.DataChanged += Refresh;
        Refresh();
    }

    partial void OnSelectedYearChanged(int value) => Refresh();
    partial void OnSelectedMonthChanged(string value) => Refresh();
    partial void OnHideDataChanged(bool value) => Refresh();

    public void Refresh()
    {
        DetailRows.Clear();
        var now = DateTime.Now;
        var dealList = AppDb.GetDeals().Where(d => d.Date.Year == SelectedYear);
        var incomeList = AppDb.GetMiscIncomes().Where(m => m.Date.Year == SelectedYear);
        var expenseList = AppDb.GetMiscExpenses().Where(m => m.Date.Year == SelectedYear);
        if (SelectedMonth != "全部")
        {
            var m = int.Parse(SelectedMonth.Replace("月", ""));
            dealList = dealList.Where(d => d.Date.Month == m);
            incomeList = incomeList.Where(m2 => m2.Date.Month == m);
            expenseList = expenseList.Where(m2 => m2.Date.Month == m);
        }
        var deals = dealList.ToList();
        var incomes = incomeList.ToList();
        var expenses = expenseList.ToList();

        // 收租已收（当月已收月租）
        var rentPaid = 0m;
        var props = AppDb.GetProperties().Where(p => !p.IsTerminated).ToList();
        foreach (var p in props)
        {
            var recs = AppDb.GetRentRecords(p.Id);
            var m = SelectedMonth == "全部" ? 0 : int.Parse(SelectedMonth.Replace("月", ""));
            if (m == 0) rentPaid += recs.Where(r => r.Year == SelectedYear && r.IsPaid).Sum(r => r.Amount);
            else rentPaid += recs.Where(r => r.Year == SelectedYear && r.Month == m && r.IsPaid).Sum(r => r.Amount);
        }

        var dealAmount = deals.Sum(d => d.Amount);
        var agentFee = deals.Sum(d => d.AgentFee);
        var miscIncome = incomes.Sum(m => m.Amount);

        // 包租已付
        var payoutPaid = 0m;
        foreach (var p in AppDb.GetPayouts())
        {
            var recs = AppDb.GetMonthlyPayouts(p.Id);
            var m = SelectedMonth == "全部" ? 0 : int.Parse(SelectedMonth.Replace("月", ""));
            if (m == 0) payoutPaid += recs.Where(r => (r.Month / 100) == SelectedYear && r.IsPaid).Sum(r => r.Amount);
            else payoutPaid += recs.Where(r => (r.Month / 100) == SelectedYear && (r.Month % 100) == m && r.IsPaid).Sum(r => r.Amount);
        }
        var miscExpense = expenses.Sum(m => m.Amount);

        DetailRows.Add(new MonthlyDetailRow { Label = "收租已收", Amount = rentPaid, IsIncome = true });
        DetailRows.Add(new MonthlyDetailRow { Label = "成交金额", Amount = dealAmount, IsIncome = true });
        DetailRows.Add(new MonthlyDetailRow { Label = "中介费", Amount = agentFee, IsIncome = true });
        DetailRows.Add(new MonthlyDetailRow { Label = "杂项收入", Amount = miscIncome, IsIncome = true });
        DetailRows.Add(new MonthlyDetailRow { Label = "包租已付", Amount = payoutPaid, IsIncome = false });
        DetailRows.Add(new MonthlyDetailRow { Label = "杂项支出", Amount = miscExpense, IsIncome = false });

        TotalIncome = rentPaid + dealAmount + agentFee + miscIncome;
        TotalExpense = payoutPaid + miscExpense;
        Profit = TotalIncome - TotalExpense;
        // Mac 口径 5 KPI
        DealCount = deals.Count;
        AgentFee = agentFee;
        MiscIncome = miscIncome;
        MiscExpense = miscExpense;
        NetIncome = AgentFee + MiscIncome - MiscExpense;
        StatText = $"{SelectedYear}{(SelectedMonth == "全部" ? "" : SelectedMonth)} 月度明细";
        OnPropertyChanged(nameof(StatText));
        OnPropertyChanged(nameof(TotalIncomeText));
        OnPropertyChanged(nameof(DealCountText));
        OnPropertyChanged(nameof(AgentFeeText));
        OnPropertyChanged(nameof(MiscIncomeText));
        OnPropertyChanged(nameof(MiscExpenseText));
        OnPropertyChanged(nameof(NetIncomeText));
        OnPropertyChanged(nameof(TotalExpenseText));
        OnPropertyChanged(nameof(ProfitText));
    }

    // v1.0：隐藏数据（对齐 iOS hideData "**"）
    public string TotalIncomeText => HideData ? "**" : TotalIncome.ToString("0.##") + " 元";
    public string TotalExpenseText => HideData ? "**" : TotalExpense.ToString("0.##") + " 元";
    public string ProfitText => HideData ? "**" : Profit.ToString("0.##") + " 元";
}
