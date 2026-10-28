using System;
using System.Collections.Generic;
using System.Linq;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.ViewModels;

namespace ApartmentManagerWin.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private MainViewModel? Main => Nav.Current;

    private void OnAddHouse(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Main?.GoTo("houses");

    private void OnAddDeal(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Main?.GoTo("deals");

    private void OnGoRent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Main?.GoTo("rent");

    private void OnGoPayout(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Main?.GoTo("payout");

    private void OnGoTodos(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Main?.GoTo("todos");

    // v1.0：快捷收租（对齐 iOS 工作台快捷收租）——把本期（本月）到期未收的租金标记已收
    private Window? GetWindow(Avalonia.StyledElement? c)
    {
        while (c != null) { if (c is Window w) return w; c = c.Parent; }
        return null;
    }

    private async void OnQuickCollectToday(object? sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        var props = AppDb.GetProperties().Where(p => !p.IsTerminated && p.RentDueDay > 0).ToList();
        var pending = new List<(Property prop, RentMonthRecord rec)>();
        foreach (var p in props)
        {
            var recs = AppDb.GetRentRecords(p.Id);
            var cur = recs.FirstOrDefault(r => r.Year == now.Year && r.Month == now.Month);
            if (cur != null && !cur.IsPaid) pending.Add((p, cur));
        }
        if (pending.Count == 0) { NotifyService.Push("快捷收租", "本期没有待收记录", NotifyKind.Info); return; }
        var names = string.Join("、", pending.Select(x => $"{x.prop.RoomNumber}({x.prop.Rent}元)"));
        var win = GetWindow(this);
        var ok = await ConfirmDialog.ShowAsync(win, "一键收租", $"将以下 {pending.Count} 套本期租金标记为已收：\n{names}\n\n确认？");
        if (!ok) return;
        foreach (var (prop, rec) in pending)
        {
            rec.IsPaid = true;
            rec.PaidDate = now;
            AppDb.SaveRentRecord(rec);
            AppDb.AddLog("收租", "快捷收租", "成功", $"{prop.RoomNumber} 本期已收 {prop.Rent}元", prop.RoomNumber);
        }
        NotifyService.Push("快捷收租", $"已标记 {pending.Count} 套本期租金已收", NotifyKind.Success);
        if (DataContext is DashboardViewModel dv) dv.Refresh();
    }

    private void OnEditUserCard(object? sender, RoutedEventArgs e)
    {
        var dlg = new UserCardDialog(DataContext as DashboardViewModel).ShowDialog(GetWindow(this));
    }

    private void OnAddIncome(object? sender, RoutedEventArgs e) => new MiscEditDialog(true).ShowDialog(GetWindow(this));
    private void OnAddExpense(object? sender, RoutedEventArgs e) => new MiscEditDialog(false).ShowDialog(GetWindow(this));
    private void OnAddPayout(object? sender, RoutedEventArgs e) => new PayoutEditDialog().ShowDialog(GetWindow(this));
    private void OnAddTodo(object? sender, RoutedEventArgs e) => new TodoEditDialog().ShowDialog(GetWindow(this));
}
