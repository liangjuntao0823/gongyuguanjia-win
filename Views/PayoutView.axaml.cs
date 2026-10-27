using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;
using ApartmentManagerWin.ViewModels;

namespace ApartmentManagerWin.Views;

public partial class PayoutView : UserControl
{
    private PayoutViewModel? Vm => DataContext as PayoutViewModel;

    public PayoutView() => InitializeComponent();

    private void OnAddPayout(object? sender, RoutedEventArgs e)
        => new PayoutEditDialog().ShowDialog(GetWindow(this));

    private void OnViewDetail(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is PayoutRecord p)
            new PayoutDetailDialog(p).ShowDialog(GetWindow(this));
    }

    private void OnEditPayout(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is PayoutRecord p)
            new PayoutEditDialog(p).ShowDialog(GetWindow(this));
    }

    private void OnDeletePayout(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is PayoutRecord p)
        {
            Vm?.DeletePayout(p);
            NotifyService.Push("包租删除", $"{p.RoomNumber} 已移入回收站", NotifyKind.Info);
        }
    }

    private void OnExportExcel(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var path = ExportService.ExportPayoutsExcel(Vm.Rows.Select(r => r.Payout).ToList());
        NotifyService.Push("导出成功", path, NotifyKind.Success);
    }

    private void OnExportImage(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var rows = Vm.Rows.Select(r => (r.RoomNumber, $"{r.Manager} {r.Rent} {r.PaymentMethod} 每{r.DueDay}日")).ToList();
        var data = ExportService.RenderRecordsImage("包租合同", rows);
        var path = ExportService.SavePng(data, "包租合同");
        NotifyService.Push("导出成功", path, NotifyKind.Success);
    }

    private Window? GetWindow(Avalonia.StyledElement? c)
    {
        while (c != null) { if (c is Window w) return w; c = c.Parent; }
        return null;
    }
}
