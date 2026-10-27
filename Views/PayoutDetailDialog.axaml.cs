using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Layout;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class PayoutDetailDialog : Window
{
    private PayoutRecord _payout;
    private readonly List<MonthlyPayout> _pays = new();
    private readonly List<MonthlyUtilityRecord> _utils = new();

    public PayoutDetailDialog(PayoutRecord p)
    {
        InitializeComponent();
        _payout = p;
        TitleText.Text = $"包租详情 {p.RoomNumber}";
        ManagerText.Text = string.IsNullOrEmpty(p.Manager) ? "-" : p.Manager;
        UnitText.Text = string.IsNullOrEmpty(p.UnitType) ? "-" : p.UnitType;
        PayText.Text = p.PaymentMethod;
        RentText.Text = p.MonthlyRent.HasValue ? $"{p.MonthlyRent.Value} 元/月" : $"{p.AnnualRent} 元/年";
        DepositText.Text = p.Deposit > 0 ? $"{p.Deposit} 元" : "-";
        FreeText.Text = p.RentFreeDays > 0 ? $"{p.RentFreeDays} 天" : "-";
        DueDayText.Text = $"每月 {p.RentDueDay} 日";
        DurationText.Text = string.IsNullOrEmpty(p.LeaseDuration) ? "-" : p.LeaseDuration;
        LeaseRangeText.Text = $"租期：{p.LeaseStartDate:yyyy-MM-dd} ~ {p.LeaseEndDate:yyyy-MM-dd}";
        WaterText.Text = $"{p.WaterMeterBase:0.##} 吨";
        ElectricText.Text = $"{p.ElectricMeterBase:0.##} 度";
        LoadLists();
    }

    private void LoadLists()
    {
        PayList.Items.Clear();
        _pays.Clear();
        foreach (var r in AppDb.GetMonthlyPayouts(_payout.Id)) _pays.Add(r);
        var paidCount = _pays.Count(r => r.IsPaid);
        PaySummaryText.Text = $"共 {_pays.Count} 期 ｜ 已付 {paidCount} 期";
        foreach (var r in _pays)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 2) };
            var period = new TextBlock { Text = $"{r.Month / 100}年{r.Month % 100}月", Width = 100, VerticalAlignment = VerticalAlignment.Center, Foreground = (Avalonia.Media.Brush?)this.FindResource("TextPrimary") };
            var amount = new TextBlock { Text = $"{r.Amount:0.##} 元", Width = 110, VerticalAlignment = VerticalAlignment.Center, Foreground = (Avalonia.Media.Brush?)this.FindResource("TextPrimary") };
            var status = new TextBlock { Text = r.IsPaid ? "✓ 已付" : "未付", Width = 70, VerticalAlignment = VerticalAlignment.Center, Foreground = r.IsPaid ? (Avalonia.Media.Brush?)this.FindResource("SuccessBrush") : (Avalonia.Media.Brush?)this.FindResource("WarningBrush") };
            var btn = new Button { Content = r.IsPaid ? "撤销" : "标记已付", Tag = r };
            btn.Click += OnTogglePay;
            Grid.SetColumn(period, 0); Grid.SetColumn(amount, 1); Grid.SetColumn(status, 2); Grid.SetColumn(btn, 3);
            row.Children.Add(period); row.Children.Add(amount); row.Children.Add(status); row.Children.Add(btn);
            PayList.Items.Add(row);
        }

        UtilList.Items.Clear();
        _utils.Clear();
        foreach (var u in AppDb.GetMonthlyUtilityRecords(_payout.Id)) _utils.Add(u);
        var settledCount = _utils.Count(u => u.IsSettled);
        UtilSummaryText.Text = $"共 {_utils.Count} 期 ｜ 已结算 {settledCount} 期";
        foreach (var u in _utils)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 2) };
            var period = new TextBlock { Text = u.Date.ToString("yyyy-MM"), Width = 100, VerticalAlignment = VerticalAlignment.Center, Foreground = (Avalonia.Media.Brush?)this.FindResource("TextPrimary") };
            var amount = new TextBlock { Text = $"水{u.WaterAmount:0.##} 电{u.ElectricAmount:0.##}", Width = 140, VerticalAlignment = VerticalAlignment.Center, Foreground = (Avalonia.Media.Brush?)this.FindResource("TextPrimary") };
            var status = new TextBlock { Text = u.IsSettled ? "✓ 已结算" : "未结算", Width = 80, VerticalAlignment = VerticalAlignment.Center, Foreground = u.IsSettled ? (Avalonia.Media.Brush?)this.FindResource("SuccessBrush") : (Avalonia.Media.Brush?)this.FindResource("WarningBrush") };
            var btn = new Button { Content = u.IsSettled ? "撤销" : "标记结算", Tag = u };
            btn.Click += OnToggleUtil;
            Grid.SetColumn(period, 0); Grid.SetColumn(amount, 1); Grid.SetColumn(status, 2); Grid.SetColumn(btn, 3);
            row.Children.Add(period); row.Children.Add(amount); row.Children.Add(status); row.Children.Add(btn);
            UtilList.Items.Add(row);
        }
    }

    private void OnTogglePay(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is MonthlyPayout r)
        {
            r.IsPaid = !r.IsPaid;
            r.PaidDate = r.IsPaid ? DateTime.Now : null;
            AppDb.SaveMonthlyPayout(r);
            AppDb.AddLog("包租", r.IsPaid ? "标记已付" : "撤销已付", "成功", $"{_payout.RoomNumber} {r.Month / 100}-{r.Month % 100}");
            AppDb.NotifyDataChanged();
            LoadLists();
        }
    }

    private void OnToggleUtil(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is MonthlyUtilityRecord u)
        {
            u.IsSettled = !u.IsSettled;
            AppDb.SaveMonthlyUtilityRecord(u);
            if (u.IsSettled)
                AppDb.AddUtilityHistory(new UtilitySettlementHistory { Date = DateTime.Now, RoomNumber = _payout.RoomNumber, WaterAmount = u.WaterAmount, ElectricAmount = u.ElectricAmount });
            AppDb.AddLog("包租", u.IsSettled ? "水电结算" : "撤销结算", "成功", $"{_payout.RoomNumber}");
            AppDb.NotifyDataChanged();
            LoadLists();
        }
    }

    private void OnEdit(object? sender, RoutedEventArgs e)
    {
        var dlg = new PayoutEditDialog(_payout);
        dlg.ShowDialog(this);
        LoadLists();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
