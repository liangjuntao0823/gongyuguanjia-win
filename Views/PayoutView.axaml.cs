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

    // ============ v1.0：包租核算 ============
    private void OnAddPackage(object? sender, RoutedEventArgs e)
    {
        var dlg = new PayoutPackageEditDialog(null, () => Vm?.RefreshPackages());
        var win = GetWindow(this);
        var host = new Window
        {
            Width = 520, Height = 460,
            Title = "新增包租核算",
            Content = dlg,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        host.ShowDialog(win);
    }

    private void OnEditPackage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is PayoutPackageRecord rec)
        {
            var dlg = new PayoutPackageEditDialog(rec, () => Vm?.RefreshPackages());
            var host = new Window
            {
                Width = 520, Height = 460,
                Title = "编辑包租核算",
                Content = dlg,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            host.ShowDialog(GetWindow(this));
        }
    }

    private void OnDeletePackage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is PayoutPackageRecord rec)
        {
            AppDb.SoftDeletePayoutPackage(rec.Id);
            Vm?.RefreshPackages();
            NotifyService.Push("核算删除", $"{rec.RoomNumber} 核算已移入回收站", NotifyKind.Info);
        }
    }

    private void OnExportPackageExcel(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.PackageRows.Count == 0) return;
        var rows = Vm.PackageRows.Select(r => new object?[]
        {
            r.RoomNumber, r.Record.TotalPackageAmount, r.Record.PackageStart.ToString("yyyy-MM-dd"),
            r.Record.PackageEnd.ToString("yyyy-MM-dd"), r.Record.VacancyDays, r.Record.TenantMonthlyRent,
            r.Record.RentStart.ToString("yyyy-MM-dd"), r.Record.RentEnd.ToString("yyyy-MM-dd"),
            Math.Round(r.Record.DailyCost, 2), Math.Round(r.Record.VacancyCost, 2),
            Math.Round(r.Record.RentalIncome, 2), Math.Round(r.Record.NetProfit, 2), r.Record.Notes,
        }).ToList();
        var path = ExportService.ExportGenericExcel("包租核算", new[] { "房号","包租总价","包租起","包租止","空置天数","租客月租","租客起","租客止","日均成本","空置损失","租客收入","净利润","备注" }, rows);
        NotifyService.Push("导出成功", path, NotifyKind.Success);
    }
}
