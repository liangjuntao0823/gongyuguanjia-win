using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;
using ApartmentManagerWin.ViewModels;

namespace ApartmentManagerWin.Views;

public partial class RentView : UserControl
{
    private RentViewModel? Vm => DataContext as RentViewModel;

    public RentView() => InitializeComponent();

    private void OnAddProperty(object? sender, RoutedEventArgs e)
        => new RentEditDialog().ShowDialog(GetWindow(this));

    private void OnViewDetail(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is Property p)
            new RentDetailDialog(p).ShowDialog(GetWindow(this));
    }

    private void OnEditProperty(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is Property p)
            new RentEditDialog(p).ShowDialog(GetWindow(this));
    }

    private void OnDeleteProperty(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is Property p)
        {
            Vm?.DeleteProperty(p);
            NotifyService.Push("收租删除", $"{p.RoomNumber} 已移入回收站", NotifyKind.Info);
        }
    }

    private void OnExportExcel(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var path = ExportService.ExportPropertiesExcel(Vm.Rows.Select(r => r.Property).ToList());
        NotifyService.Push("导出成功", path, NotifyKind.Success);
    }

    private void OnExportImage(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var rows = Vm.Rows.Select(r => (r.RoomNumber, $"{r.Rent} 每{r.DueDay}日  {r.Status}")).ToList();
        var data = ExportService.RenderRecordsImage("收租房源", rows);
        var path = ExportService.SavePng(data, "收租房源");
        NotifyService.Push("导出成功", path, NotifyKind.Success);
    }

    private Window? GetWindow(Avalonia.StyledElement? c)
    {
        while (c != null) { if (c is Window w) return w; c = c.Parent; }
        return null;
    }
}
