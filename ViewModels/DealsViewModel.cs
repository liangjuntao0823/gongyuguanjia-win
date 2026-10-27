using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.ViewModels;

public class DealRow
{
    public DealRecord Deal { get; set; } = new();
    public string Date => Deal.Date.ToString("yyyy-MM-dd");
    public string RoomNumber => Deal.RoomNumber;
    public string Customer => Deal.Customer;
    public string Type => Deal.Type;
    public decimal Amount => Deal.Amount;
    public decimal AgentFee => Deal.AgentFee;
    public string Note => Deal.Note;
    public string Summary => $"{Type}｜{Amount:0.##}元｜中介费{AgentFee:0.##}元";
}

public partial class DealsViewModel : ViewModelBase
{
    public ObservableCollection<DealRow> Rows { get; } = new();
    public ObservableCollection<int> Years { get; } = new();
    public ObservableCollection<string> Months { get; } = new();

    [ObservableProperty]
    private int selectedYear;
    [ObservableProperty]
    private string selectedMonth = "全部";
    [ObservableProperty]
    private string statText = "";
    [ObservableProperty]
    private decimal totalAmount;
    [ObservableProperty]
    private decimal totalFee;

    public DealsViewModel()
    {
        var now = DateTime.Now;
        SelectedYear = now.Year;
        Months.Add("全部");
        for (int m = 1; m <= 12; m++) Months.Add(m + "月");
        // 年份：从数据取，兜底近5年
        var years = AppDb.GetDeals().Select(d => d.Date.Year).Distinct().OrderByDescending(y => y).ToList();
        if (years.Count == 0) years = Enumerable.Range(now.Year - 4, 5).OrderByDescending(y => y).ToList();
        foreach (var y in years) Years.Add(y);
        AppDb.DataChanged += Refresh;
        Refresh();
    }

    partial void OnSelectedYearChanged(int value) => Refresh();
    partial void OnSelectedMonthChanged(string value) => Refresh();

    public void Refresh()
    {
        Rows.Clear();
        var list = AppDb.GetDeals();
        var filtered = list.Where(d => d.Date.Year == SelectedYear);
        if (SelectedMonth != "全部")
        {
            var m = int.Parse(SelectedMonth.Replace("月", ""));
            filtered = filtered.Where(d => d.Date.Month == m);
        }
        var rows = filtered.OrderByDescending(d => d.Date).ToList();
        foreach (var d in rows) Rows.Add(new DealRow { Deal = d });
        TotalAmount = rows.Sum(r => r.Amount);
        TotalFee = rows.Sum(r => r.AgentFee);
        StatText = $"{SelectedYear}{(SelectedMonth == "全部" ? "" : SelectedMonth)} 共 {rows.Count} 条 ｜ 成交总额 {TotalAmount:0.##} 元 ｜ 中介费 {TotalFee:0.##} 元";
        OnPropertyChanged(nameof(StatText));
    }

    public void DeleteDeal(DealRecord d)
    {
        AppDb.SoftDeleteDeal(d.Id);
        AppDb.AddLog("成交", "删除", "成功", $"{d.RoomNumber} {d.Date:yyyy-MM-dd}");
        AppDb.NotifyDataChanged();
    }
}
