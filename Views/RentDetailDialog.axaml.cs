using System;
using Avalonia;
using Avalonia.Layout;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class RentDetailDialog : Window
{
    private Property _property;
    private readonly System.Collections.Generic.List<RentMonthRecord> _rents = new();
    private readonly System.Collections.Generic.List<UtilitySettlementRecord> _utils = new();

    public RentDetailDialog(Property p)
    {
        InitializeComponent();
        _property = p;
        TitleText.Text = $"收租详情 {p.RoomNumber}";
        RoomNoText.Text = p.RoomNumber;
        LandlordText.Text = string.IsNullOrEmpty(p.Landlord) ? "-" : p.Landlord;
        UnitText.Text = string.IsNullOrEmpty(p.UnitType) ? "-" : p.UnitType;
        TypeText.Text = p.Type;
        RentText.Text = $"{p.Rent} 元/月";
        DueDayText.Text = $"每月 {p.RentDueDay} 日";
        StartText.Text = string.IsNullOrEmpty(p.LeaseStart) ? "-" : p.LeaseStart;
        EndText.Text = string.IsNullOrEmpty(p.LeaseEnd) ? "-" : p.LeaseEnd;
        DepositText.Text = p.Deposit > 0 ? $"{p.Deposit} 元" : "-";
        PrepayText.Text = p.Prepayment > 0 ? $"{p.Prepayment} 元" : "-";
        NoteText.Text = string.IsNullOrEmpty(p.Notes) ? "" : $"备注：{p.Notes}";
        WaterText.Text = $"{p.WaterMeterBase:0.##} 吨";
        ElectricText.Text = $"{p.ElectricMeterBase:0.##} 度";
        if (p.IsTerminated) TerminateBtn.Content = "已退租";
        LoadLists();
    }

    private void LoadLists()
    {
        RentList.Items.Clear();
        _rents.Clear();
        foreach (var r in AppDb.GetRentRecords(_property.Id)) _rents.Add(r);
        var paidCount = _rents.Count(r => r.IsPaid);
        RentSummaryText.Text = $"共 {_rents.Count} 期 ｜ 已收 {paidCount} 期";
        foreach (var r in _rents)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 2) };
            var period = new TextBlock { Text = $"{r.Year}年{r.Month}月", Width = 90, VerticalAlignment = VerticalAlignment.Center, Foreground = (Avalonia.Media.Brush?)this.FindResource("TextPrimary") };
            var amount = new TextBlock { Text = $"{r.Amount:0.##} 元", Width = 100, VerticalAlignment = VerticalAlignment.Center, Foreground = (Avalonia.Media.Brush?)this.FindResource("TextPrimary") };
            var status = new TextBlock { Text = r.IsPaid ? "✓ 已收" : "未收", Width = 70, VerticalAlignment = VerticalAlignment.Center, Foreground = r.IsPaid ? (Avalonia.Media.Brush?)this.FindResource("SuccessBrush") : (Avalonia.Media.Brush?)this.FindResource("WarningBrush") };
            var btn = new Button { Content = r.IsPaid ? "撤销" : "标记已收", Tag = r };
            btn.Click += OnToggleRentPaid;
            Grid.SetColumn(period, 0); Grid.SetColumn(amount, 1); Grid.SetColumn(status, 2); Grid.SetColumn(btn, 3);
            row.Children.Add(period); row.Children.Add(amount); row.Children.Add(status); row.Children.Add(btn);
            RentList.Items.Add(row);
        }

        UtilList.Items.Clear();
        _utils.Clear();
        foreach (var u in AppDb.GetUtilityRecords(_property.Id)) _utils.Add(u);
        var settledCount = _utils.Count(u => u.IsSettled);
        UtilSummaryText.Text = $"共 {_utils.Count} 期 ｜ 已结算 {settledCount} 期";
        foreach (var u in _utils)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 2) };
            var period = new TextBlock { Text = $"{u.Year} 第{u.Period}期", Width = 100, VerticalAlignment = VerticalAlignment.Center, Foreground = (Avalonia.Media.Brush?)this.FindResource("TextPrimary") };
            var amount = new TextBlock { Text = $"水{u.WaterAmount:0.##} 电{u.ElectricAmount:0.##}", Width = 140, VerticalAlignment = VerticalAlignment.Center, Foreground = (Avalonia.Media.Brush?)this.FindResource("TextPrimary") };
            var status = new TextBlock { Text = u.IsSettled ? "✓ 已结算" : "未结算", Width = 80, VerticalAlignment = VerticalAlignment.Center, Foreground = u.IsSettled ? (Avalonia.Media.Brush?)this.FindResource("SuccessBrush") : (Avalonia.Media.Brush?)this.FindResource("WarningBrush") };
            var btn = new Button { Content = u.IsSettled ? "撤销" : "标记结算", Tag = u };
            btn.Click += OnToggleUtilSettled;
            Grid.SetColumn(period, 0); Grid.SetColumn(amount, 1); Grid.SetColumn(status, 2); Grid.SetColumn(btn, 3);
            row.Children.Add(period); row.Children.Add(amount); row.Children.Add(status); row.Children.Add(btn);
            UtilList.Items.Add(row);
        }
    }

    private void OnToggleRentPaid(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is RentMonthRecord r)
        {
            r.IsPaid = !r.IsPaid;
            r.PaidDate = r.IsPaid ? DateTime.Now : null;
            AppDb.SaveRentRecord(r);
            AppDb.AddLog("收租", r.IsPaid ? "标记已收" : "撤销已收", "成功", $"{_property.RoomNumber} {r.Year}-{r.Month}");
            AppDb.NotifyDataChanged();
            LoadLists();
        }
    }

    private void OnToggleUtilSettled(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is UtilitySettlementRecord u)
        {
            u.IsSettled = !u.IsSettled;
            u.SettledDate = u.IsSettled ? DateTime.Now : null;
            AppDb.SaveUtilityRecord(u);
            if (u.IsSettled)
                AppDb.AddUtilityHistory(new UtilitySettlementHistory { Date = DateTime.Now, RoomNumber = _property.RoomNumber, WaterAmount = u.WaterAmount, ElectricAmount = u.ElectricAmount });
            AppDb.AddLog("收租", u.IsSettled ? "水电结算" : "撤销结算", "成功", $"{_property.RoomNumber} {u.Year} 第{u.Period}期");
            AppDb.NotifyDataChanged();
            LoadLists();
        }
    }

    private void OnEdit(object? sender, RoutedEventArgs e)
    {
        var dlg = new RentEditDialog(_property);
        dlg.ShowDialog(this);
        LoadLists();
    }

    private async void OnTerminate(object? sender, RoutedEventArgs e)
    {
        if (_property.IsTerminated) return;
        var ok = await ConfirmDialog.ShowAsync(this, "确认退租", $"确定将 {_property.RoomNumber} 标记为已退租？\n（可在收租房源-已退租 中查看）");
        if (!ok) return;
        _property.IsTerminated = true;
        _property.TerminationDate = DateTime.Now;
        _property.TerminationReason = "退租";
        _property.DepositRefundStatus = "待退押金";
        AppDb.SaveProperty(_property);
        AppDb.AddLog("收租", "退租", "成功", $"{_property.RoomNumber}");
        AppDb.NotifyDataChanged();
        TerminateBtn.Content = "已退租";
        LoadLists();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
