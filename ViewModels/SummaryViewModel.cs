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
        StatText = $"{SelectedYear}{(SelectedMonth == "全部" ? "" : SelectedMonth)} 月度明细";
        OnPropertyChanged(nameof(StatText));
    }
}
